# Bug / Leak Audit #5 — the EARLY code (Feb–May 2026)

> **Date:** 2026-08-13 · **Build:** 2804 · **Scope:** the code this project's audits have never
> covered — the ~49k surviving source lines authored **before 2026-06-01**.
>
> Audits [#3](audit-2026-07-14-findings.md) (post-b1872) and [#4](audit-2026-08-04-findings.md)
> (post-b2168) were both scoped to *"files changed since baseline X"*, i.e. **recent** code. The only
> pass that ever reached the early code was the **4-agent "full audit" at build 974** (2026-06-10,
> commits `2e7218c` / `e588ebc`) — one tenth of audit #4's 48-agent adversarial effort, over a
> codebase that has since grown from build 974 to 2804.
>
> **Status:** all items are **REPORTED, NOT YET FIXED** unless a ✅ note says otherwise. Working
> tracker — write shipped items up in [dev-log.md](dev-log.md), then delete the row here.

-----

## 0. The measurement this plan is built on

Not a guess about which files "feel old". For every tracked source file, `git blame --line-porcelain`
counted how many **surviving** lines have an `author-time` before 2026-06-01:

```
early lines: 48,950 / 131,522 total  (37.2%)  over 321 source files
```

| Area | Early lines | Total | Early % |
|------|------------:|------:|--------:|
| `dll/src` | 23,378 | 51,669 | **45.2%** |
| `ui/…/Services` | 10,067 | 32,506 | 31.0% |
| `ui/…/ViewModels` | 8,652 | 29,219 | 29.6% |
| `ui/…/Views` | 2,303 | 5,769 | 39.9% |
| `ui/…/Models` | 2,207 | 6,875 | 32.1% |
| `scripts/*.lua` | 1,236 | 1,668 | **74.1%** |
| `ui/…/Core` | 806 | 2,095 | 38.5% |

**Why the DLL leads the plan.** It holds 45% of the early code *and* the entire C++ test suite is
**two files** (`dll/tests/dll_helpers_test.cpp`, `utf8_helpers_test.cpp`) which link **headers only**
— so no test target compiles `Ubel.cpp`, `Genau.cpp`, `Macht.cpp` or `Aura.cpp` at all. `Genau` and
`Macht` have **zero** coverage of any kind. That is un-audited, untested, memory-unsafe-by-nature code.

A second, narrower lens agrees: **53 surviving source files have not been touched since May 2026**
(35 UI, 9 UI-tests, 3 scripts, 2 DLL, 2 tools) — see `scratchpad/stale-files.csv`, folded into §T1.

-----

## 1. Segment plan

Deliberately **not** one big parallel fan-out — segments run one at a time so the session budget
holds. Ordered by (early-line count × blast radius ÷ existing coverage).

| # | Segment | Files | Early lines | Coverage today |
|---|---------|-------|------------:|----------------|
| **D1** | Ubel — UStructWalker | `Ubel.cpp` 5214/5968 (87%), `Ubel.h` 451/642 | ~5,665 | no compiled test |
| **D2** | Genau + Serie | `Genau.cpp` 3386/5108, `Serie.cpp` 587/791 | ~3,973 | **Genau: none** |
| **D3** | Aura — ObjectArray | `Aura.cpp` 3711/8763, `Aura.h` 522/1387 | ~4,233 | 1 test file |
| **D4a** ✅ | Macht + Scharf (memory layer) | `Macht` 708/755, `Macht.h`, `Scharf.h` | ~1,000 | **none** |
| **D4b** ✅ | Mimic/Flamme/Sein/Stark/Lugner | `Mimic` 639, `Flamme` 410, `Sein` 342, `Stark` 302, `Lugner`(+Dinput8) 225 | ~1,626 | none |
| **D5** | Fern + Frieren | `Fern.cpp` 2490/6028, `Frieren.cpp` 816/1784 | ~3,306 | partly audited before |
| **U1** ✅ | LiveWalker / Pointer / ObjectTree VMs | 2900 + 999 + 360 | ~4,259 | good |
| **U2** ✅ | Export services | CeXml 1699, Csx 678, Sdk 569, Usmap 397, Symbol 215 | ~3,558 | mixed |
| **U3** | Dump services + MainWindow VM | 1517 + 437 + 1366 | ~3,320 | good |
| **U4** | Dialogs + CE script generators | InvokeParamDialog 1020 (96%), Baked 503, Invoke 399, ObjInstancePicker 295, ParamBuffer 258, CheatTable 245, FreezeDialog 242, FreezeGen 210 | ~3,172 | mixed |
| **U5** | Remaining VMs / Models / Core / scoring | Console 445, InstanceFinder 405, InterestingProps 380, PropertySearch 356, InterestingFuncs 345, LiveFieldValue 478, Logging 362, scoring tables ~800 | ~3,500 | mixed |
| **S1** | Early Lua scripts | `ue5_dissect` 531/555, `ue5_freeze_helper` 417/508, `ue5_invoke_helper` 288/605 | ~1,236 | none |
| **T1** | Tail sweep | every remaining file ≥50 early lines + the 53 never-touched-since-May files | ~10k | — |

### Method per segment

Same shape that made audit #4 trustworthy, scaled down to fit one segment:

1. **Find** — parallel agents over the segment, each with a *distinct lens* (memory/lifetime,
   concurrency, correctness-vs-UE-layout, resource leak, AOT/trim for C#).
2. **Refute** — every finding goes to a skeptic **mandated to refute**, default stance "not a bug".
   A finding that cannot be given a concrete failing input dies there.
3. **Second lens** on every survivor rated HIGH/MEDIUM.
4. **Record** — confirmed items land in §2 below with `file:line`, failure scenario, fix shape,
   effort/risk. Refuted items land in §3 so they are never re-raised.

**No fixes are applied during a scan segment.** Fixing is a separate, later pass — mixing them is how
a "verified against the list it was written from" fix ships (audit #4's own lesson).

-----

## 2. Findings

*(populated one segment at a time)* — **if you are here to FIX something, read
[§4 Cross-segment rollup](#4-cross-segment-rollup--read-this-before-fixing-anything) first.** The
findings group into five clusters that are much cheaper to fix together than to walk one by one.

### D1 — Ubel (UStructWalker) — ✅ scanned 2026-08-13

**24 agents** (5 lenses → 9 refute batches → 10 second-lens), 0 errors. 27 raw claims → **13 refuted**
→ **14 confirmed** → **11 distinct** after merging three lens-duplicate pairs (D1-2/D1-4 were the same
Map/Set defect; D1-3/D1-10 the same struct-preview defect; D1-8/D1-11 the same FName::Number defect).

**Tally: 8 MEDIUM · 3 LOW · 0 HIGH.** Both claimed HIGHs were refuted — see §3.

> **Three root causes, each worth fixing as a pattern rather than site-by-site:**
> 1. **A validation helper exists and is applied at only some of its sites.** `ValidateArrayElemSize`
>    was written *because* `FPROPERTY_ELEMSIZE` is documented in this very file to return garbage — and
>    every `ArrayProperty` path routes through it while **all four Map/Set paths do not** (U1).
> 2. **Layout knowledge is duplicated instead of derived.** The `bCasePreservingName` FName width is
>    computed correctly at `Ubel.cpp:126` and at five sites in `Aura.cpp`, but hardcoded to 8 in
>    `InferScalarSize` (U2); the FName `Number` decode is open-coded three times and drops the field in
>    all three (U8). Same class of defect as audit #4's 4a root cause.
> 3. **Caches with no eviction and no validity check.** Three separate caches keyed by a raw address
>    that the engine recycles, cleared only by two unrelated pipe commands (U4/U5/U6).

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **U1** | MED | `Ubel.cpp:4079`,`4103`,`4127`,`4155`,`4342`,`4374` (+UProperty twins `4212`,`4253`,`4277`,`4417`,`4446`) | Map/Set element sizes are read raw from `FPROPERTY_ELEMSIZE` and gated only on `> 0`, then used as a **per-element `std::vector` size** *and* as the TSparseArray stride. Every `ArrayProperty` path routes the identical read through `ValidateArrayElemSize` (cap 65536) precisely because this read is documented to return garbage. | S / low |
| **U2** | MED | `Ubel.cpp:1373` + `1403` | `InferScalarSize` returns a hardcoded `8` for `NameProperty`, and `ValidateArrayElemSize` treats that as authoritative and **overrides the engine's correct 16** on `bCasePreservingName` games. | S / med |
| **U3** | MED | `Ubel.cpp:1662` | `InterpretValue`'s StructProperty preview skips 8 bytes as a "vtable preamble" (USTRUCTs generally have no vtable) and reads UE5 LWC **doubles as floats**. Prints plausible-looking numbers that are not the struct's values. | M / low |
| **U4** | MED | `Ubel.cpp:818` (+`1055`, `149`) | `WalkClass` `try_emplace`s its result **unconditionally**, including a zero-field `ClassInfo` from a transient read failure or a not-yet-`Link`ed UClass — and no `erase`/`clear` for either class cache exists anywhere in `dll/src`. The poisoned entry is returned for the rest of the process lifetime. | S / low |
| **U5** | MED | `Ubel.cpp:683` | `s_walkClassCache` and `s_walkClassExCache` each retain a **full flattened** `ClassInfo` (14 `std::string`s per `FieldInfo`) per class ever walked, twice, unbounded, for the process lifetime. | M / med |
| **U6** | MED | `Ubel.cpp:411` | `s_nameCache` is keyed by raw `UObject*` with no validation, no bound, no expiry; its only two callers (`begin_snapshot`, `trigger_scan`) are **not** the events that recycle addresses (GC / level transition). Corrupts control flow, not just display — `WalkInstance`'s `isRawStruct` probe (`3360`) switches on `GetName`. | M / med |
| **U7** | MED | `Ubel.cpp:5565` | `s.resize(50)` cuts a validated UTF-8 preview at a raw byte boundary, splitting a multi-byte sequence; nlohmann's default **strict** `dump()` then throws and the **entire `search_properties` response** becomes `{"error":...}` — zero results for a search that actually matched. | S / low |
| **U8** | MED | `Ubel.cpp:1649`,`1653` (+open-coded twins `4568`, `5043`) | `InterpretValue` decodes FName from `ComparisonIndex` only, dropping `FName::Number` — so `Slot_1`/`Slot_2`/`Slot_3` all render as `Slot`. `ReadFNameAt` on the same bytes returns the suffix, so the panel and value-search **disagree about the same 8 bytes**. | S / low |
| **U9** | LOW | `Ubel.cpp:2238` | Byte-enum read casts through `int8_t`, so enumerators ≥ 128 (incl. the standard UHT `MAX = 255` sentinel) sign-extend, miss the UEnum lookup and render as a negative integer. Four sibling sites read it unsigned. | S / low |
| **U10** | LOW | `Ubel.cpp:198` | `ReadFString` **rejects** on `count > 256` and returns `""`, which callers map to the literal label `(empty)` — a 400-character description is displayed as empty while `hexValue` on the adjacent row shows `Count=0x191`. | M / low |
| **U11** | LOW | `Ubel.cpp:4934` | `TOptional<FText>` is decoded as an inline FString at `FText+0x10`, where stock UE stores the `uint32 Flags`. The correct decoder (`ReadFTextString`) already exists ~4,400 lines earlier in the same file and is used by the plain TextProperty path. | S / low |

† **G7 was reframed after being wrong once — worth reading as a method note.** It was first filed as *"a give-up path that reports failure with no reason"*, from a live `validated=NO (DEFAULTS) reason=` on Solarpunk. **That was my own misreading of a TRANSIENT state**: `probe_ran` was `false` at that moment, i.e. the probe had not run *yet* — an empty reason is correct for "not started", and the flag pair expressed it exactly as designed. Re-querying the same still-running process later returned `validated=true, probe_ran=true`. **The original finding is refuted.** What survives is smaller and different: the *log* never re-emits its summary, so it still claims `DEFAULTS` while the DLL is validated. Two lessons, both already in this audit's own record: **the base rate applies to me too, not just the finder agents** (this is the ninth-plus claim to die), and **sampling an initialising system early yields a state that is real but not a verdict** — which is precisely what `probe_ran` exists to disambiguate, and I read past it.

**Verified independently (not just agent-reported):** U2's evidence is stronger than the finding
states — `InferScalarSize`'s own comment two lines below it already knows this failure mode
(*"FScriptDelegate … is 16 or 24 depending on CasePreservingName. Do **NOT** override these here"*),
so the CasePreservingName variance was recognised for the delegate types and missed for `FName`
itself. The fix must reuse the existing `DynOff::bCasePreservingName ? 0x10 : 0x08` expression rather
than introduce a new constant — that expression is already the repo-wide convention
(`Ubel.cpp:126`, `Aura.cpp:2901/2924/3437/5791`, `Genau.cpp:5045`).

### D2 — Genau + Serie — ✅ scanned 2026-08-13

Run `wf_3cc1586f-db1`, in two parts: the 5 finders completed, a session usage limit killed all 9
refute batches and all 22 second-lens agents, then a **resume** replayed the finders from cache and
ran the 14 survivors live. 26 raw claims → **19 refuted (73%)** → 7 confirmed → **6 distinct** after
merging the two `DetectBlockOffsetBits` reports.

**Tally: 3 MEDIUM · 3 LOW · 0 HIGH.** The finders claimed **seven HIGHs; every one of them died** —
six refuted outright, one (`Genau.cpp:4007`) downgraded to MEDIUM by the second lens.

> **The refutation rate is the headline.** D1 refuted 13/27 (48%); D2 refuted 19/26 (73%). Raw
> finder output on this codebase is *mostly wrong*, and it is wrong in a specific direction: it
> reports **plausible-sounding structural criticisms of code whose oddities are load-bearing**, and it
> over-rates them. **A segment that cannot complete its refute pass has produced nothing usable** —
> park it as UNVERIFIED and resume, never promote it.

> ⚠ **One refutation was OVERTURNED by hand — do not re-suppress it.** A skeptic refuted the
> `DetectBlockOffsetBits` claim; two other skeptics confirmed it. I checked the code directly:
> at `testIdx = 1`, `1 >> 16` and `1 >> 14` are **both 0**, and the masks `0xFFFF`/`0x3FFF` do not
> bite below 16384 — so both candidate widths compute the identical `(ci=0, co=1*stride)` and probe
> the same byte. The detector structurally cannot distinguish the two. **The finders were right.**

> **One root cause behind G1/G3 — `bOffsetsValidated` does not mean what its own contract says.**
> `Grimoire.h:244` defines it as *"the values were actually MEASURED and are trustworthy"*, and the
> split from `bOffsetsProbeRan` exists specifically to stop the give-up paths from lying. G1 is that
> lie happening anyway, from **inside** the success path. Everything downstream that writes to game
> memory at a derived offset (`Solide::force_field`, `Wirbel`, `Solitar`, `Laufen`) trusts it.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **G1** | MED | `Genau.cpp:4005-4007` | `ValidateAndFixOffsets` stores `bOffsetsValidated = true` **unconditionally on the success tail**, though three in-path give-up branches (`3701` FField::Name, `3761` FField::Next, `3915` Offset_Internal) fall through to it after logging *"keeping default"*. Provable by construction: `nextOff < 0` ⇒ Step 7 collects 1 field ⇒ Step 8's `matches >= 2` is unsatisfiable ⇒ `FPROPERTY_OFFSET`/`ELEMSIZE`/`FLAGS` keep Step-2.5's blind version guesses ⇒ `Fern.cpp:4543` reports `{"validated": true, "fallback_reason": ""}`. **This is the root of D1's U1** (`FPROPERTY_ELEMSIZE` garbage). | S / low |
| **G2** | MED | `Genau.cpp:2929` (+`CountPreUE4Markers`, `DataScanGObjectsCandidates`, `FindGObjectsStaticStruct`, `FindGNamesByStringRef`) | The whole-image and multi-module sweeps **never poll `Tot::Requested()`** — three sibling scans in the same file do, for exactly this reason. On a ~482 MB image, Tier 2/3 alone is ~9.2e9 runtime-length `memcmp`s (~14 s), and `UE5_Shutdown`'s join blocks for all of it. Per `docs/ce-plugin-sdk-notes.md` §13 the CE-side wait has **no message pump**, so CE reads as hung. | S / low |
| **G3** | MED | `Genau.cpp:3242`, `3167`, `3261` | `ValidateAndFixOffsets` rewrites the DynOff set **in place**, republishing unmeasured version defaults over already-probed values for the seconds a re-run takes. Reachable on a CE `[DISABLE]`/`[ENABLE]` cycle: `UE5_Shutdown` never clears `g_cachedGObjects`/`g_cachedGNames`, so `Mimic`'s poller gate (`Mimic.cpp:388`) is satisfied by **stale** addresses and keeps servicing mailbox commands while `UFIELD_NEXT` is reset 0x38 → 0x28 mid-flight. | L / med |
| **G4** | LOW | `Serie.cpp:303-314` | `DetectBlockOffsetBits` **cannot detect anything**: at `testIdx = 1` both candidate widths compute `ci = 0`, `co = 1*stride`, so 16 always wins and the 14-bit arm is unreachable — yet `Init` logs the result as a measurement. On a real 14-bit pool every FName ≥ `0x4000` reads past the end of a 32 KB block, so engine names resolve and the **game-specific tail comes back blank**. *(Overturned refutation — see the note above.)* | M / med |
| **G5** | LOW | `Serie.cpp:498` | UE4 `TNameEntryArray` mode indexes the chunk with a **negative** element index; the bounds guard the UE5 path has is absent. A poison `ComparisonIndex` of `0xFFFFFFFF` with a non-zero Number skips `GetString`'s `nameIndex <= 0 && number == 0` early-out, so `chunkPtr + (size_t)(-1)*8` is dereferenced as an `FNameEntry*` and a **fabricated name** can be returned as real. | S / low |
| **G7** † | LOW | `Genau.cpp` / `Sein` DynOff summary | **The log's `DynOff:` SUMMARY is emitted once and never re-emitted after a later successful validation, so the log permanently contradicts the live state.** Solarpunk 2026-08-14: the log's only summary says `validated=NO (DEFAULTS) reason=`, while `get_offsets` on the same live process returns `validated=true, probe_ran=true`. Anyone following [log-verification-checklist.md](log-verification-checklist.md) would conclude the offsets never validated. | S / low |
| **G6** | LOW | `Serie.cpp:142` | A tag whose key lookup misses is cached as a **permanent** `TAGKEY_MISS`, contradicting `Genau.cpp:1672`'s documented "absent tag = plaintext" rule. A transient miss (the 4 unsynchronized reads in `LookupTagKey` racing a live insert) blanks every FName in every block with that tag for the rest of the process. | S / low |

### D3 — Aura (ObjectArray) — ✅ scanned 2026-08-13

Run `wf_d6aaff24-28e`, 16 agents, 0 errors. 18 raw claims → **8 refuted (44%)** → **10 confirmed**.

**Tally: 6 MEDIUM · 4 LOW · 0 HIGH — and the finders claimed no HIGH at all.** The prompt carried
the measured refutation rate from D1/D2 and told them to reserve HIGH for something demonstrable
end-to-end; the raw output came back correspondingly disciplined (18 claims from 5 lenses vs D2's 26).

> **⚠ Two findings are outside this segment's files** — `Macht.h:302` (A2) and `Fern.cpp:4483` (A8).
> They were found *from Aura's side* and are recorded here rather than deferred. **Segments D4 and
> D5 must not re-derive them.**

> **A recurring shape, now on its third instance: a cache keyed by an address the engine recycles.**
> D1/U4+U5+U6 (`Ubel`'s class and name caches), D2 (none), D3/A10 (`Aura`'s per-class container and
> reference metadata). Worth one fix pattern — a stored `(InternalIndex, SerialNumber)` witness
> validated on hit, which is exactly the pair UE itself uses to detect a recycled slot — rather than
> three separate patches.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **A1** | MED | `Aura.cpp:1370` | `GetSerialNumber` picks the serial offset with `s_itemSize >= 24 ? 0x10 : 0x0C`, a two-way split covering only strides 16 and 24 — but **stride 20 is reachable** (Avowed's packed `FUObjectItem`, via both `InitWithExtendedLayout` and the `{16,24,32,20}` sweep). At 20 it reads **`ClusterRootIndex`**, so `Ubel::ResolveWeakObjectPtr`'s bare `if (actualSerial != serialNumber) return 0;` declares **every** weak reference stale. Silent — nothing logs a mismatch. | S / low |
| **A2** | MED | `Macht.h:302` *(D4 scope)* | `IsSparseIndexAllocated` judges slots 0..127 from the **stale inline bit words** once a `TSet`/`TMap` has spilled its `TBitArray` to the heap — affecting **13 Aura call sites + 6 in Ubel**, while `Aura::ResolveTMapBitArrayBase` gets the same rule right. A freed slot reads as live, so Find Refs can emit a phantom reference and `ScanForValue` admits a dead element's value. | S / low |
| **A3** | MED | `Aura.cpp:6170` | `ScanForValue`'s struct-expansion cycle guard is **whole-walk instead of path-scoped**, so the 2nd and 3rd field of a repeated struct type are dropped from the scan index. Hits GAS directly — `FGameplayAttributeData Health/MaxHealth/Mana` share one `UScriptStruct`, so only `Health` is indexed. `CollectSchemaLeaves` (`4109`) already does it correctly. | S / low |
| **A4** | MED | `Aura.cpp:6791` | Value Search's deep-container pass **drops every depth-1 leaf**, so values inside `TSet<FStruct>` / `TMap<K,FStruct>` elements are unreachable *even with Deep on* — the static index doesn't cover them either (`collectStructArrayInner` is only reached from the ArrayProperty branch). An everyday `TMap<FName, FItemData>` inventory count is unfindable. | M / low |
| **A5** | MED | `Aura.cpp:4436` | Property Search's inline **Preview samples the CDO**, not a live instance, so the column shows the Blueprint default forever (Health = 100 while the player is at 37). `Solide.cpp:282`, `Wirbel.cpp:328` and `Edel.cpp:94` all already filter `Default__`. | S / low |
| **A6** | MED | `Aura.cpp:4282` | `SearchProperties` reports the **defining** class, so dedup collapses ~4,800 `AActor` subclasses into one row named `Actor`; Force then calls `FindInstancesByClass("Actor", exactMatch)` and resolves an **empty pool**. The concrete class is known at row-emission time and thrown away. | M / med |
| **A7** | LOW | `Aura.cpp:1685` | `FindByAddress` is the **only** full-GObjects walk in the file with neither a `Tot::Requested()` poll nor a deadline — same shape as D2/G2. Responsiveness, not correctness. | S / low |
| **A8** | LOW | `Fern.cpp:4483` *(D5 scope)* | `get_ce_pointer_info` branches on `Aura::IsPacked()` but **never on `Aura::IsFlat()`**, so on a flat `FFixedUObjectArray` (OctoPath, FF7R Intergrade, Extinction, NEKOPALIVE) hop 3 dereferences `Item[0].Object` as a chunk pointer and CE resolves a garbage address **the user can write to**. LOW only because the UI client mitigates. | S / low |
| **A9** | LOW | `Aura.cpp:8380` | `ScanForValueGroup` sets the per-object element budget but **never passes the counter**, leaving `maxTotalElems` inert on the deep walk — so the very stall it was added for (the recorded SEED ~24 s chunk) is still unbounded; only the global 15 s deadline stops it, consuming the whole scan budget on one object. | S / low |
| **A10** | LOW | `Aura.cpp:2990` | Per-class container/reference metadata caches keyed on a raw `UClass*` the engine recycles, never invalidated. After a sublevel unload hands `BP_EnemyA_C`'s address to `BP_ChestB_C`, Find Refs iterates the wrong class's pointer offsets. | M / med |

**Notable evidence quality on A1:** the second lens settled *intent vs omission* with `git blame` —
the ternary is `d410359e` (2026-06-13) while **both** stride-20 paths postdate it (`22aaa523`
2026-06-17, `55a2d092` 2026-08-05), and the explanatory comment above it still enumerates only
16B/24B/UE5.7/packed57. It also re-derived the layout from a source the finder never cited
(`docs/test-games.md:55`) and noted the predicate tests the wrong property: a packed 20-byte item is
the 24-byte item with **tail padding removed**, so every field offset is unchanged and the question
is "does it carry a `ClusterRootIndex`" (stride ≥ 20), not "is the stride ≥ 24".

### D4a — Macht + Scharf (memory layer) — ✅ scanned 2026-08-13

Run `wf_8f75b7cb-452`, 10 agents, 0 errors. 9 raw claims → **5 refuted** → 4 confirmed → **3 distinct**
(two lenses hit `ComputeMapValueOffset` independently). **Tally: 3 MEDIUM · 0 HIGH.**

> **The result inverted the prediction, and that is the finding.** D4a was scoped as the highest-risk
> file in the project because of the **SEH read/write contract** and **AOBScan** — 94% early code,
> zero tests, everything routes through it. **Every claim against those two surfaces was refuted**
> (5 of 5: page-protection restore, missing `Tot::Requested()` polls, stale module snapshots, two
> region-size off-by-ones). All three real defects are in the **small pure arithmetic helpers in
> `Macht.h`** — the part that looks least dangerous and has no test covering the *composition* of
> its pieces.
>
> **They compound.** M3 produces a wrong `valueOffset`, which feeds `pairSize`, which feeds M1's
> stride — the same element address is wrong twice over.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
> **✅ ALL THREE SHIPPED 2026-08-14** (`Macht.h` + `Ubel.cpp` + `dll_helpers_test.cpp`, 1018 C++
> tests green, +21 of them new and all covering the **composition**). `ComputeSetElementStride` took
> an `elemAlign` parameter (default 4 → TSet behaviour byte-identical); a new `Macht::SanitizeAlign`
> rejects anything not a power of two in [1,32]; `ReadTSparseArray` reads `NumFreeIndices` at `+0x34`;
> and a new file-local `Ubel::GetStructAlignment` reads `UScriptStruct::MinAlignment` so struct-typed
> keys/values stop falling through to the size guess. **Not yet verified on a real game** — see
> [todo.md § Pending live-game verification](todo.md#pending-live-game-verification-verify-only--no-code).
> One correction found while fixing: `MinAlignment` is **`int16`** in UE 5.8 (`StructStateFlags`
> takes the other half of the word) but was **`int32`** in UE4 / early UE5 — so it is read as
> `int16_t`, which is correct on **both** on little-endian x64. Reading it as `int32` as originally
> proposed would have picked up the flags on newer engines.

| **M1** | MED | `Macht.h:314` | `ComputeSetElementStride` aligns to **4** and cannot express `alignof(T)`, so the documented `TMap` recipe omits the `TPair`'s **trailing padding**. Real stride is `Align(sizeof(TTuple<K,V>) + 8, max(alignof K, alignof V))`. `TMap<AActor*,float>` → computes **20**, real **24**; `TMap<FString,int32>` → **28** vs **32**; `TMap<UObject*,uint8>` → **20** vs **24**. Every element past index 0 is read at a wrong address. `TSet<T>` is unaffected (a bare `elemSize` is always a multiple of `alignof(T)`). | S / med |
| **M2** | MED | `Macht.h:293` | `ReadTSparseArray` reads `NumFreeIndices` at **`+0x3C`**. The PDB-verified layout in this repo (`Aura.cpp:3037-3038`, Everspace 2 UE 5.4) puts `FirstFreeIndex` at `+0x30` and `NumFreeIndices` at **`+0x34`**; `+0x3C` is padding before the Hash allocator at `+0x40`, zero-initialised and never written. So it always reads **0**, and `Ubel.cpp:4045`'s `mapCount = MaxIndex - NumFreeIndices` **over-reports** the count of any `TMap`/`TSet` that has had entries removed (10 shown, 6 rows rendered). The header comment claiming `TSparseArray` is `0x40` bytes is also wrong — it is `0x38`. | S / low |
| **M3** | MED | `Macht.h:332` | `ComputeMapValueOffset`'s size-guess fallback is taken for **every struct-valued TMap**, because `Scharf::RequiredAlignment` returns **0** for `StructProperty` (`Scharf.h:76-78`). It then guesses `valueSize >= 8 → align 8`. For `TMap<int32,FVector>` the real value sits at **+4** (alignof 4) but it reads **+8**, so *even element 0* shows a wrong vector; `TMap<int32,FGuid>` reads every value 4 bytes late. **A validation helper is being reused as a layout oracle.** | M / med |

**Why the composite was never caught:** `dll_helpers_test.cpp:2013-2033` exercises
`ComputeSetElementStride` and `ComputeMapValueOffset` **separately**, and every case it picks happens
to land on a multiple of 8. The same is true of every empirically PDB-verified data point in the tree
(`Aura.cpp:3046-3054`, `Ubel.cpp:5709-5711`) — all have already-8-aligned pairs, so none of them
discriminates. The M1 skeptic established this by computing both formulas against each verified case
rather than arguing in prose, and noted that the corrected formula reproduces every verified value
exactly — independent corroboration that the missing `Align` is real.

### D4b — Mimic + Flamme + Sein + Stark + Lugner — ✅ scanned 2026-08-14

Run `wf_97eaa194-4c7`, 11 agents, 0 errors — **on the second attempt.** The first launch lost all
five finders to `API Error: 529 Overloaded` at 0 tokens and 0 tool calls each, and the workflow
still returned `{"confirmed": [], "refuted": [], "note": "no findings"}`. **A clean-looking empty
result is what a total finder wipeout looks like** — the `<failures>` block was the only thing that
said so. Resumed from the same run id; nothing was cached, so everything re-ran live.

18 raw claims → **9 refuted (50%)** → 9 confirmed → **9 distinct** (no lens hit the same site twice).
Recorded below: **2 MEDIUM · 6 LOW · 1 INFO**, after two adjustments I made by hand (S1 and P1, both
flagged in place).

**Tally: 0 HIGH — and for the second segment running, the finders claimed none.** They claimed
**13 MEDIUM**; one survived as MEDIUM, four were downgraded to LOW by their skeptic, and eight died.

> **The prediction inverted again, in the same direction as D4a.** The segment was scoped around the
> two surfaces that look most dangerous — the cross-process mailbox written by an external process
> with no lock, and a MinHook trampoline on `ProcessEvent` in a live game. **Both are in good shape,
> and they are in good shape because they have already been audited**: `Stark`'s in-flight-unhook
> race is explicitly handled (`RemoveHook` soft-disables and says why, audit fixes #13/#14), `Sein`'s
> sweep-killing shared `error_code` was fixed as B19, `WriteToFile`'s NULL-`FILE*` termination as
> B11, the poller's raw-`CreateThread` throw as B14. Every one of those carries a comment naming the
> defect it prevents, and **five of the nine refutations are a skeptic finding exactly such a
> comment.** The real defects landed instead on the **unaudited edges**: a proxy `.def` file, an
> `ofstream` nobody checks, a `bool` return nobody reads.

> **Three root causes:**
> 1. **A safety decision read from a field that is not an input.** `HandleInvoke` picks
>    game-thread-vs-off-thread from `functionFlags` (MB1), a mailbox field `Mimic.h` documents as
>    DLL-**output** — and which two *other* handlers overwrite with an unrelated page count.
>    Cluster ② again: the value consulted is computed by a different path than the one that means it.
> 2. **A `bool`/stream-state return discarded at the one site that could report the failure.**
>    `OpenFileInDir` (SE1), `ofstream::good` (FL1), `fs::rename`'s failure (FL2). In each case the
>    subsystem then reports success and dies silently.
> 3. **The forwarding surface is enumerated by hand and is wrong.** PX1: the real `dinput8.dll`
>    exports six names; our `.def` lists five and pins no ordinals, while the sibling `ProxyDxgi.def`
>    pins all twenty and documents *why*.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **ST1** | MED | `Stark.cpp:160` | The queued-invoke drain is gated **only** on `s_queueDepth != 0` — never on thread identity — so it executes on whichever thread entered the patched `ProcessEvent`. `Stark.h:10-12` states the module's whole purpose is that "ProcessEvent is always called from the game thread". Multi-thread PE is **not speculative here**: it is a confirmed finding of audit #3 (L5), whose fix sits two lines above the drain in the same hook body (`Linie.cpp:46-52`). Verified independently: `grep -rn "IsInGameThread\|GGameThread\|GameThreadId\|GetCurrentThreadId" dll/src/` returns **five** hits (re-counted at takeover — D4b wrote four) — three are `Frieren.cpp` log-format arguments, two are `Himmel.h` AOB comment text — so **zero** thread-identity checks on any dispatch path. Second lens confirmed it on a *stronger* path than the finder's: `UE5_CallProcessEventDirect` recomputes the same vtable slot, so `Mimic::HandleInvoke`'s static-native route re-enters our own trampoline **from the mailbox polling thread**. | M / med |
| **PX1** ‡ | MED | `ProxyDinput8.def:15-20` (+ `Lugner_Dinput8.cpp:54`) | The dinput8 proxy forwards **5 of the real DLL's 6 exports**. `GetdfDIJoystick` has no stub and no `.def` entry, and because the `.def` pins **no ordinals**, MSVC assigns them alphabetically and our `@6` becomes `UE5_AutoStart`. A by-name static import fails process creation outright (`STATUS_ENTRYPOINT_NOT_FOUND`, before any of our logging exists); an ordinal-`#6` import calls `UE5_AutoStart`, which kicks off the whole AOB scan and returns a `bool` in RAX where an `LPCDIDATAFORMAT` is expected. `ProxyImportAnalyzer` records only the imported DLL *name*, never a function list, so Proxy Deploy cannot warn. `ProxyDxgi.def` pins `@1..@20` and its own header says why: *"The @ordinal values match real dxgi.dll so ordinal imports resolve too."* | S / low |
| **MB1** | LOW | `Mimic.cpp:557` | `HandleInvoke` decides the game-thread-vs-direct-off-thread route from `g_invokeMailbox.functionFlags`, which `Mimic.h` documents as a **DLL-filled output** and which `CMD_INVOKE`'s documented inputs do not include. A bare re-`FIRE` (`InvokeScriptGenerator.cs:383-384` re-issues `CMD_INVOKE` without re-running `CMD_FIND_FUNCTION`) therefore routes on whatever the *previous* command left at offset `0x024`. A stale `Native|Static` sends a stateful actor UFunction off the game thread — exactly what the comment three lines above forbids. The false-positive is the unsafe direction; the false-negative is harmless. | S / low |
| **MB2** | LOW | `Mimic.cpp:278` | The `EnsureInitialized()` gate is applied to **every** command including `CMD_FOREGROUND`, which `Mimic.h` documents as thread-agnostic pure Win32 and which the pipe path services with no such gate — so on a game whose GObjects scan fails, Keep-Foreground is refused with `-10` and `ForegroundScriptGenerator.cs:79-81` renders it as `hook error -10`, naming MinHook, a subsystem never reached. Second half: `Frieren` latches `s_initialized` only on **success**, so the gate re-runs a whole-image AOB sweep on every command while init keeps failing, pinning the mailbox at `status=0xFF` past `MailboxPollTimeoutMs`. | S / low |
| **SE1** | LOW | `Sein.cpp:473` | `InitProcessMirror` **discards `OpenFileInDir`'s `bool`** and sets `s_filesOpen = true` regardless, then flushes the early buffer into possibly-NULL `FILE*`s and `clear()`s it. A category that failed to open is dead for the session with **nothing logged anywhere**, not even into the `init` file that opened fine. Same shape in `RotateIfNeeded`: a failed reopen leaves `file == nullptr` **and `written = 0`**, so the size test at `:268` returns early forever and the category never retries. The victim is [log-verification-checklist.md](log-verification-checklist.md)'s own procedure — a grep that finds nothing reads as "the code path never ran" when the truth is "the file never opened". | S / low |
| **SE2** | LOW | `Sein.cpp:359` (+ twin `:242`) | Both retention sweeps advance their `fs::directory_iterator` with a **range-for**, which calls the *throwing* `operator++()`, not `increment(error_code&)`. `directory_iterator(p, ec)` reports **construction** failures only — and on failure it equals `end()`, so the loop body never runs and the `if (iterEc) break;` guard inside it is **dead code**. Verified: `dll/CMakeLists.txt:155-157`+`186` strip `/EHsc` for `/EHa`, and `grep -c catch dll/src/Sein.cpp` = **0**, so an escaping `filesystem_error` unwinds out of `InitProcessMirror` into `DLL_PROCESS_ATTACH` unguarded. *Honest limit: the escape path is verified structurally; the trigger (`FindNextFileW` failing mid-enumeration) is not demonstrated.* | S / low |
| **FL1** | LOW | `Flamme.cpp:323` | `ofs << root.dump(2);` then scope-exit, then `fs::rename` — **the stream's error state is never tested.** No `exceptions()` mask, no `good()`, no explicit `close()`, so a short write (full volume, quota) is swallowed by the destructor and the **truncated document is published over the only cache copy**. `LoadHints` then parses it with `allow_exceptions=false`, gets `discarded`, and returns empty — every game's pattern IDs, `ueVersion`, user override and invoke timeout gone. The C# twin structurally cannot do this: `await File.WriteAllTextAsync` throws *before* `File.Move` (`AobUsageService.cs:141-142`). Four copies of the block, so a fix must be a shared helper. | S / low |
| **FL2** | LOW | `Flamme.cpp:325` | `fs::rename(tempPath, path)` is the **throwing** overload; the `catch` at `:331` logs and returns, leaving `<file>.tmp.<pid>` behind. The suffix is the PID, so every affected launch leaks a *distinctly named* full copy of the cache into `%LOCALAPPDATA%\UE5CEDumper\` and nothing ever removes them. Reachable without any disk fault: the UI holds the file via `File.ReadAllTextAsync` (`FileShare.Read`, **no** `FileShare.Delete`), so a replace-rename during a UI read fails. Violates CLAUDE.md's app-data rule directly — the root is for files "app-wide and fixed in number". | S / low |
| **MB3** † | INFO | `Mimic.cpp:211` | `PollingThreadProc` wraps the **entire** `while (s_running)` loop in `Routine::RunThreadGuarded`, so a throw out of any handler ends the CE mailbox for the session. It is the **only loop in the tree** with the whole-body form — `Routine.h:168-169` (`while (SleepSliced(...)) RunTickGuarded(...)`) and `Stark.cpp:208-210` (guard per PE **call**) both guard per iteration. Filed as INFO, not MEDIUM: see † below. | S / low |

‡ **P1 was downgraded to LOW by its skeptic and I put it back to MEDIUM — after verifying it against
the shipped binary rather than the source.** `dumpbin /EXPORTS C:\Windows\System32\dinput8.dll` lists
six: `DirectInput8Create @1 … DllUnregisterServer @5`, **`GetdfDIJoystick @6`**. `dumpbin /EXPORTS
dist\proxy\dinput8.dll` (2026-08-14 build) lists `@1..@5` as the five real names and **`@6 =
UE5_AutoStart`, `@7 = UE5_CallProcessEvent`**. The mechanism is therefore not argued, it is measured,
and the outcome when it fires is total (the game does not launch, or it dereferences a `bool`). What
is genuinely uncertain is only *reachability* — how many titles import `GetdfDIJoystick` — and that
is a reason to state a caveat, not to rate a measured end-to-end defect as LOW.

† **S1 is a MEDIUM that was refuted and is recorded as what survives — the D2/G7 treatment.** The
skeptic confirmed it; the second lens refuted it and was **right on the fact that decided it**, which
I checked myself: the skeptic's "unrevivable" rests on `StartThread`'s `if (s_running.load()) return;`,
but a throw unwinds *past* `s_running.store(false)`, so `s_running` is still **true** — which means
`StopThread`'s own guard (`if (!s_running.load()) return;`) **passes** and the documented CE
Disable→Enable fully restores the poller. The second lens also chased a stronger version neither the
finder nor the skeptic raised — under `/EHa`, `catch (...)` catches SEH too, so an access violation
would kill the poller — and killed that too: every game-memory read in the mailbox call graph goes
through `Macht::ReadSafe`'s `__try/__except`, which absorbs the fault *below* the thread guard. What
survives is the structural asymmetry alone, with no demonstrated bad outcome. **Do not re-raise it as
a MEDIUM.**

-----

### D5 — Fern (PipeServer) + Frieren (ExportAPI) — ✅ scanned 2026-08-14

Run `wf_39143753-6df`, 14 agents, **0 errors, 0 empty results** — the first segment where every agent
returned. 19 raw claims → **11 refuted (58%)** → 8 confirmed → **8 distinct**. Recorded: **5 MEDIUM ·
3 LOW**, and **every one of the five MEDIUMs went through a second independent lens and survived**.

**Tally: 0 HIGH — the third segment running.** One claim arrived as HIGH and its own skeptic cut it to
MEDIUM. `Fern.cpp` is the largest file in the DLL (6,028 lines) and was *partly* audited before —
audits #3/#4 covered the command families added since their baselines, so the finders were pointed at
the old core instead, and that is where all eight landed.

> **The theme of this segment is the reply, not the transport.** The wire protocol and the connection
> machinery came out clean: every framing, parsing and unbounded-allocation claim died (see §3).
> What survived is almost entirely **what the DLL *says* about work it did not finish** — three of the
> eight are a cap or an abort that the reply does not mention, one is a return value dropped, one is an
> error string that describes an execution that provably never happened. Cluster ② and ⑥ now account
> for 5 of D5's 8.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **F1** ‡ | MED | `Fern.h:30` (+ `Frieren.cpp:92`) | `~Fern() { Stop(); }` on a namespace-scope static makes the **entire pipe teardown** — cancel sweeps, watch joins, a **5-second** condition-variable drain and two thread joins — run from the CRT's static-destructor pass during `DLL_PROCESS_DETACH`: precisely the heavy work `Heiter.cpp:288-301` deliberately refuses to do there, and precisely the hazard `Routine.h:51-56` already documents for every *other* module's worker. At process exit `ExitProcess` has already terminated the connection threads, so they can never erase themselves from `m_conns` and the drain predicate is **unsatisfiable by construction** — the full budget burns every time. `Stop` also takes `m_connMutex`, Sein's log mutex and both Radar session mutexes *after* their holders were killed, which is the documented shape of a permanent exit hang. | S / low |
| **F2** | MED | `Fern.cpp:854` (+ `:779`) | `Tot::ResetPerCommand()` runs **only** when a connection is accepted into an *empty* registry (`firstConn = m_conns.empty()`). The monitor latches `g_perCommand` when a pipe breaks, and nothing else ever clears it — so a reconnect that arrives while a dead session's connection is still registered (a bulk lane parked in `EnqueueInvoke`'s wait, up to the 600 s invoke timeout) carries the latch into the **new** session, permanently: the registry cannot go empty again until that session ends. Every cancellable loop then bails on its first poll and returns an empty or partial result **inside a successful response** — `find_instances` 0 rows, `search_properties` truncated, `begin_value_scan` empty. Second lens confirmed it and called it understated: `Stark.h:87-89` advertises the very guard that would have killed the claim, and it does not cover this path. | M / med |
| **FR1** | MED | `Frieren.cpp:767` | `UE5_AutoStart` **discards `UE5_Init`'s return** behind the comment `// Always succeeds (partial init is OK …)` and unconditionally starts the pipe server and publishes `initState = INIT_READY` — a state the enum defines as "init finished **and** the pipe server is up". `UE5_Init` has a real `return false` (a shutdown landing during the multi-second scan; pointers partial, nothing latched). End state: a serving pipe, `s_initialized == false`, engine pointers from an aborted scan, and **no mailbox poller** (`Mimic::StopThread` already ran and `StartThread`'s only other caller is `DllMain`) — so every CE `.CT` feature row writes a command nobody collects and `status` stays `0`, which CLAUDE.md's own rule tells the user means *"stale `g_invokeMailbox` address"*. Sends the user to the wrong diagnosis. Second lens proved the comment stale **by history**: `af7ff3a` (2026-03-02) deleted the old `if (!UE5_Init()) return false;` when it was correct; `ab66d54` (2026-08-04, audit #4 B49) added the `return false` five months later without revisiting the call site. | S / low |
| **F3** | MED | `Fern.cpp:1068` (+ `:1717`, `:4773`) | `Ubel`'s per-UObject name cache is keyed by a **raw `uintptr_t` with no generation/serial**, is never revalidated on hit, and Fern owns its only two purge sites — `begin_snapshot` and `trigger_scan`, neither reachable from ordinary browsing. The last-connection teardown at `:1068` drops Radar sessions, Linie, Sense and Schlacht but **not** this; nor does `Fern::Stop`, nor a CE Disable/Enable. After a level change recycles a UObject slot, every name-bearing response — Object Tree rows, `walk_instance`'s own/outer name, every ObjectProperty target — serves the **destroyed** object's name for the rest of the process's life, while the class is read fresh, so the two disagree with no error anywhere. Only restarting the game clears it. **Cluster ③, exactly.** | S / low |
| **F4** | MED | `Fern.cpp:2645` (+ batch twin) | `Aura::SearchProperties` stops the GObjects walk the instant `results.size()` reaches `maxResults` (**200** by default from the UI) and also breaks on `Tot::Requested()`; the reply carries `total` = the capped row count with **no `truncated` and no aborted flag**. Worse, `scanned_objects` is echoed from a field Aura assigns to the **full** object count *before* the loop starts (`Aura.cpp:4179`), so a walk that stopped a few percent in reports the whole pool as scanned. The panel prints *"Found 200 properties in 3,412 classes (scanned 1,204,338 objects)"*; the user's client-side filter then finds nothing and they conclude the field does not exist. **This is the exact shape behind four "the scan missed my field" reports.** | S / low |
| **F5** | LOW | `Renge.h:282` (+ `Fern::WriteLine`) | `MakeResponse` builds a 3-key envelope then `res.merge_patch(data)`; nlohmann's merge_patch assigns non-object values **by copy**, so a payload a handler carefully built with `std::move` is deep-copied in full — then `WriteLine` materialises a second copy of the serialized string solely to append `"\n"`. Peak ≈ 2× DOM + 2× string, **in the game process's heap**, on `snapshot_chunk` (8192 objects), `find_instances` (50000 cap) and `list_all_functions` (100000 default). | S / low |
| **F6** | LOW | `Fern.cpp:2510` | `walk_world` clamps the Actors loop to the caller's `limit` (the UI sends **500**) and reports `actors.size()` as the level's actor count — the real `actorArr.Count` is read one line earlier and **discarded** — with no `truncated`/`total`, so a 500-actor page is indistinguishable from a 500-actor level and an actor at index 1877 simply is not there. Two sibling failures in the same block (Actors field unresolved; `ReadTArray` fails) also return `actors: []` with `ok:true` and **no `error`**, even though this same handler sets `data["error"]` for the two failures directly above. | S / low |
| **F8** ¤ | MED | `Fern.cpp:2510` (same handler as F6) | **`ULevel` has no reflected `Actors` property on this engine, so `walk_world` enumerates nothing — always.** Found while verifying F6: the fix's new error string named the branch (`actorsOffset < 0`, not a failed read), and walking the live `ULevel` instance confirms it — **29 fields, 7 `ArrayProperty`, none called `Actors`** (`ModelComponents` @208, `NavDataChunks` @264, `StreamingTextures` @320, `DestroyedReplicatedStaticActors` @768; the only `*Actor*` names are `ActorCluster` and `LevelScriptActor`, both `ObjectProperty`). `walk_world`'s entire actor enumeration is built on finding that property by reflection, so on this engine Live Walker's "Load GWorld" shows a populated level as empty. `ULevel::Actors` is a real native member — the fix is a native-offset read (the `Ubel::GuessGapTypes` machinery already exists for exactly this), not more reflection. **CONFIRMED AT THE SOURCE.** `vendor/UnrealEngine/Engine/Source/Runtime/Engine/Classes/Engine/Level.h:428-429` declares `TArray<TObjectPtr<AActor>> Actors;` with **no `UPROPERTY()`** — a plain C++ member the engine never reflects. Our walker is not dropping it: `DestroyedReplicatedStaticActors` (`Level.h:887`), which IS a `UPROPERTY`, appears in the field list we read. So `walk_world`'s reflection-based lookup **cannot ever have worked**, on any engine matching this source, and the feature has been silently returning an empty world since it was written. The alternative hypothesis — "our class walker is truncating the field list", which would have been far worse — is dead. **Reproduced on a second, unrelated title.** Solarpunk (commercial, different engine build) in the 2026-08-14 session: `TX {"cmd":"walk_world","limit":500,...}` → `RX {"actor_count":0,"actors":[],"ok":true,"world_name":"MainLevel",...}`. **2 of 2 games tested return nothing**, so this is not a DumperTest artifact and may not be version-specific at all. *Honest limit: two captures is not a survey — what is established is that it fails on both engines we have evidence for, not the size of the affected range.* | M / med |
| **F7** | LOW | `Fern.cpp:5042` | The `-7` response text reads *"(hook not active, direct call used)"*. `-7` is produced **only** by `Stark::EnqueueInvoke`'s inactive-hook guard or by `Stark::Shutdown` draining the queue — **neither executes ProcessEvent by any route**; the direct fallback lives on the other side of the `if (Stark::IsHookActive())` branch and returns 0/-2/-3/-4/-8. The string reports an execution that provably did not happen, and the dialog still shows `result_hex` — the untouched pre-call buffer. `-8` has no mapping at all. | S / low |

### D5 — LIVE VERIFIED 2026-08-14, headless, on packaged `DumperTest` (Shipping, DLL build 2812)

Three of the eight were reproduced on a real process the same day they were filed. No UI, no CE — the
[§3b recipe](#the-reusable-win-from-today--headless-in-game-verification): launch, `inject-ue.ps1
-ProcessId`, a `NamedPipeClientStream` to `UE5DumpBfx`.

**F1 — paired controls, one variable, 5.5× apart.** Same build, same map, same client; the only
difference is whether a connection was still registered when the game closed. Closed **gracefully**
via `CloseMainWindow()` (`WM_CLOSE` → `ExitProcess` → `DLL_PROCESS_DETACH`), never `Stop-Process
-Force`, which would skip DETACH and prove nothing:

| | Client at exit | `Stop entry` | Drain | Process exit |
|---|---|---|---|---|
| **B** | **held open** | `conns=1` | `TIMEOUT, 1 left (5030 ms, 49 cancel re-asserts)` | **6,046 ms** |
| **A** | disconnected first | `conns=0` | `satisfied, 0 left (0 ms, 0 re-asserts)` | **1,105 ms** |

The 5,030 ms is attributable to a still-registered connection and to nothing else. **This is the
first time this signature has been produced deliberately** — the four attempts recorded in
[todo.md](todo.md) all worked from accidental captures, and B is now a ~30-second on-demand repro
usable as the acceptance test for whatever fix ships. Control A is not a formality: without it, 6 s
is just "how long a UE game takes to close".

**F4 — the reply contradicts itself in a single log line.** `search_properties query="Name" limit=3`:

```
SearchProperties 'Name': 3 matches from 8 classes (scanned 24445 objects)
```

**8 classes were walked; 24,445 objects are reported scanned** — the full pool, because `scannedObjects`
is assigned before the loop. The two numbers are in one reply and cannot both be true. No `truncated`
key is present. A control query that genuinely walked everything (`"Dumper"`, 0 matches) reports
`scanned_classes: 1566, scanned_objects: 24445`, so 1566 is what a real full walk looks like.

**F6 — the silent branch fires on a stock map.** `walk_world` on `ThirdPersonMap` returns
`{"actor_count":0,"actors":[],"ok":true,…}` with `world_addr`, `level_name: "PersistentLevel"` and
`level_offset: 48` all resolved — and **no `error` key**. Re-run with `limit: 5000` to rule out the
cap: identical. So this is the `actorsOffset < 0` / `ReadTArray` branch, and the UI renders a
populated level as an empty one with nothing to indicate a failure. (The truncation half of F6 was
not exercised — DumperTest's level is too small to exceed the UI's 500 cap.)

*Not verified: F2, FR1, F3, F5, F7 — each needs a state DumperTest cannot reach cheaply (a parked
bulk lane, a shutdown landing mid-scan, a level change, a multi-MB payload, a torn-down hook).*

### ✅ F1 and F7 FIXED and re-measured — build 2813, 2026-08-14

**F1**: `Fern::Stop(bool graceful = true)`; `~Fern()` calls `Stop(false)`, which logs the entry path and
returns before the cancel sweeps, the joins and the 5 s drain. Re-ran control B on the fixed build —
same client, same map, connection **held open** through a graceful close: **1,185 ms**, against
6,046 ms pre-fix and 1,105 ms for the pre-fix disconnected control. A connection open at exit now costs
nothing. The new log line is `Stop entry (process exit — skipping drain/joins, the OS reclaims this)`,
which is also what a future capture needs to be attributable to process-exit rather than a CE Disable.

**F7**: the `-7` text now says the invoke was never dispatched, and `-8` gained the mapping it never
had. String-only; nothing to measure.

⚠ **The `graceful=true` path was NOT exercised.** It is unchanged by construction — the fix is an early
return in front of it — but reaching it needs a CE Disable (or `UE5_StopPipeServer`), which the headless
route cannot drive. Filed in [todo.md](todo.md)'s pending register; do not record it as verified.
Note also that `-Target Test` proves nothing about either fix: **no test target compiles `Fern.cpp`.**

### ✅ F4 and F6 FIXED and re-measured — build 2818, 2026-08-14

**F4**: `PropertySearchResult` gained `truncated` / `aborted`; `scannedObjects` is now what the loop
**walked** instead of the pool size assigned before it; the same fix went into the batch twin
(per-query `truncated`, since the batch stops only when *every* query is full). Fern emits all three.
Both paths measured on DumperTest, with an independent cross-check:

| query | `total` | `scanned_classes` | `scanned_objects` | `truncated` |
|---|--:|--:|--:|---|
| `"Name"`, limit 3 | 3 | 8 | **105** | **true** |
| `"Dumper"`, limit 200 | 0 | 1566 | **24445** | false |

`get_object_count` on the same process returns **24445** — so a full sweep now reports exactly the
pool and a capped one reports what it touched. (Pre-fix, the capped query reported 24445 too.) The
log line also names the stop reason now: `…, STOPPED at the result cap — more matches exist`.

> **An off-by-one I introduced and then caught with the same cross-check.** The first build tracked
> `walked = i`, so a full sweep reported **24444** — one short of the pool. It is exactly the kind of
> error that survives when a number is only compared against itself; `get_object_count` is what made
> it visible. Fixed to `walked = i + 1` ("objects ENTERED") and re-measured.

**F6**: `actor_total` and `truncated` added, and both silent branches now set `data["error"]`. On
DumperTest the error fires immediately and names the branch — which is how **F8** was found.

✅ **F4/F6 UI half DONE — build 2823, AOT-trimmed.** `PropertySearchResult` gained `Truncated` /
`Aborted` and `WorldWalkResult` gained `ActorTotal` / `Truncated`; both parse with a **backward-safe
default** (`false` / `-1`), so an older DLL degrades to the old silent behaviour rather than throwing.
Property Search's status line now appends `⚠ STOPPED at the N-row cap — more matches exist, narrow the
query or raise Max` (or `⚠ SCAN CANCELLED — this list is partial`), and Live Walker's GWorld header
reads `⚠ showing 500 of 4,300 actors` when the list is a page. F6's *error* half needed no UI work:
`LiveWalkerViewModel` already surfaced `world.Error`, which was simply never set before build 2818.

`ActorTotal` is **-1**, not 0, when the array was never read — the UI must test `< 0`, because on the
engines measured for F8 that is the normal case and the `Error` string is what should speak there.

Verified: `-Target UI` and the full C# suite green, `check_axaml_strings` clean (1295/1295), then
`-Mode Publish` → **54.3 MB** (the trimmed binary, not the 107.5 MB one) and **launch-checked** — alive
after 12 s with no `crash.log`, because under AOT a successful build is not a successful start.

### ✅ F3(a) and FR1 FIXED — builds 2819 / 2820, 2026-08-14

**F3(a)** — `Ubel::ClearNameCache()` added to Fern's last-connection teardown (beside the Radar /
Linie / Sense / Schlacht drops it sat next to) and to `Fern::Stop`'s post-join block, so a UI
reconnect or a CE Disable/Enable is now a full reset. Verified on DumperTest by comparing
`get_object_list` across a disconnect/reconnect: the ~200 returned names are **byte-identical**, so
the purge does not break lazy repopulation. **The in-session half is deliberately NOT fixed** — a level
change while connected still serves recycled-slot names, and that wants cluster ③'s
`(InternalIndex, SerialNumber)` witness shared with D1/U4–U6 and D3/A10.

> A harness bug worth recording: the first run reported MISMATCH. The PowerShell helper emitted its
> label *inside* the function, so the label joined the return value and two **arrays** were compared,
> not two strings. Re-run with the label on `Write-Host`: MATCH. **A red result from a harness written
> in the same breath as the fix is a claim about the harness until proven otherwise.**

**FR1** — `UE5_AutoStart` now captures `UE5_Init`'s return. `INIT_READY` requires
`ok && inited`, so an aborted scan publishes `INIT_FAILED`; the log names both halves; and the export
returns `ok && inited` rather than the pipe result alone. Deliberately **reuses `INIT_FAILED` rather
than adding a state**: a new enum value is an additive mailbox-contract change and would need a
`MAILBOX_CONTRACT` bump — `tools/check_mailbox_contract.py` passes unchanged.

> **One guard the recommendation did not have.** The chosen option was "keep serving but re-arm the
> mailbox poller". The re-arm is gated on `!Tot::ShutdownRequested()`: the abort path *is* a shutdown,
> `RequestShutdown` is deliberately sticky, and resurrecting the poller while it is still latched would
> fight the user's own untick. So the re-arm covers the transient case only, and the honest-state half
> — which is the part that stops the wrong diagnosis — always applies.

⚠ **FR1's failure path is UNVERIFIED**: reproducing it needs a shutdown landing inside the
multi-second scan. What *was* verified is the regression guard — a normal AutoStart still logs
`pipe server started, init complete -> initState=2` and the pipe serves (`get_object_count` = 24445).

**F2, F5 and F8 remain REPORTED, NOT FIXED.**

¤¤ **A retraction inside F8, kept visible because the shape is the point.** F8 was first filed with
the caveat *"`walk_world` demonstrably works on other titles, so this is engine-version-dependent"*.
**Nothing was checked before writing that** — it was a conservative-sounding assumption, and the
maintainer asking an adjacent question ("DumperTest's own actor isn't under GWorld, does that affect
the judgement?") is what sent me to look. The one other capture on disk **refutes it**. The adjacent
question was answered too, and separately: it does *not* affect the judgement, because F8 is about the
class's reflected field list, not the array's contents — `actorsOffset < 0` means the property was
never found, a different branch from "found it, and your actor is not in it". Two lessons, and the
second is the one worth carrying: an "honest limit" written to sound cautious is still an
**unverified claim**, and it gets less scrutiny than a bold one precisely because it sounds modest.
See [working-lessons.md](working-lessons.md) §1.6.

¤ **F8 did not come from a finder.** It came from *fixing* F6 and then asking why the branch fired —
the D2/G7 shape again. Worth noting as method: **the cheapest new finding of the day was produced by
making an existing silent failure speak**, not by another scan. The audit's own §4 cluster ② predicts
this: when a reply cannot say what it failed to do, the defect underneath it is invisible too.

‡ **F1 — I strengthened this one at takeover, and the strengthening removes the skeptic's own reason
for downgrading it.** The skeptic did real work here: it read the MSVC CRT source to establish that
`dllmain_crt_process_detach` runs the onexit table **unconditionally** (`is_terminating` gates only
`__scrt_uninitialize_crt`), then went to `%LOCALAPPDATA%\UE5CEDumper\Logs` and found three real game
exits whose `Stop entry` proves the destructor path executed. But all three had `conns=0`, so it cut
HIGH → MEDIUM saying *"5 s on EVERY exit is unproven"*. **There is a fourth capture it did not find:**

```
DumperTest/pipe-20260812-092027.log
  09:20:22.158  PipeServer: Stop entry (conns=2)
  09:20:22.159  PipeServer: Stop cancel issued: 0 accepted, 2 had nothing pending
  09:20:27.188  straggler: parked in ReadFile … for 3660559 ms, last cmd 'get_forced_fields'
  09:20:27.188  straggler: parked in ReadFile … for 2457674 ms, last cmd 'walk_function_props'
  09:20:27.188  PipeServer: Stop conn drain TIMEOUT, 2 left (5029 ms, 49 cancel re-asserts)
```

The `conns > 0` case is **measured**, and it burned 5029 ms exactly as predicted. I verified the
load-bearing fact myself rather than taking it on trust: `UE5_Shutdown`'s **first statement** is
`LOG_INFO("UE5_Shutdown: Cleaning up...")` (`Frieren.cpp:588`), `s_pipeServer.Stop()` has exactly two
call sites (`:607` inside `UE5_Shutdown`, `:1777` in `UE5_StopPipeServer`), and the shipped
`scripts/UE5CEDumper.CT:772-780` only *probes* `UE5_StopPipeServer` with `pcall(getAddress, …)` and
then calls **`UE5_Shutdown` alone**, deliberately. `grep -rn "Cleaning up"` over the entire Logs tree
returns **zero**. So no logging call site ran, and **every `Stop entry` on disk came from `~Fern()` at
`DLL_PROCESS_DETACH`.** Severity stays MEDIUM — on outcome, not on doubt: what is demonstrated is a
5-second hang at exit, while the loader-lock deadlock remains a real but undemonstrated escalation.

**F1 also explains a four-times-refuted item in [todo.md](todo.md), and explains it better than the
answer recorded there.** That entry (`Stop conn drain TIMEOUT`) concludes the connection is *"genuinely
blocked in a synchronous `ReadFile`"* with the root cause being the absence of `FILE_FLAG_OVERLAPPED`.
That accounts for `CancelIoEx` failing — but **not** for attempt #3, where `CancelSynchronousIo` was
called on a duplicated thread handle and *also* returned nothing-pending. A **terminated** thread
explains both, and it explains why "0 accepted, 2 had nothing pending" repeats 49 times: there is no
thread left to cancel. The two structural fixes proposed there (close the handle from `Stop`; make the
pipe overlapped) would **not** fix the process-exit path either — a dead thread cannot erase itself
from `m_conns` however its `ReadFile` ends. See the note appended to that entry.

-----

### U1 — LiveWalker + PointerPanel + ObjectTree ViewModels — ✅ scanned 2026-08-14

**16 agents** (5 C# lenses → 4 refute batches → 7 second-lens), 0 errors. Run `wf_0fbfa801-46d`.
19 raw claims → 18 after dedupe → **6 refuted (33%)** → 12 confirmed → **11 distinct** after merging
the two lens-duplicate reports of the same `FindReferencesAsync` defect (`:2366` MED from
mvvm-lifetime and `:2383` LOW from async-cancel are one missing post-await navigation guard; the
MEDIUM is the superset — it covers the reference *list* as well as the header label).

**Tally: 1 HIGH · 6 MEDIUM · 4 LOW.** Three things about this segment break the audit's own pattern
and matter more than any individual row:

> **1. The audit has its first surviving HIGH — the eleventh claimed, after ten died.** V1 survived a
> refute-mandated skeptic, a second lens, *and* my own end-to-end re-derivation at both ends of the
> wire. Until now §4 could say "nothing found so far is an emergency"; that sentence is no longer
> true. V1 writes user-typed bytes to the wrong address **in a live game process**.
>
> **2. The refutation rate collapsed to 33%, against a 56% running mean.** Do not read this as
> "the C# is worse" without noting the confound: this is the first segment whose skeptics had **real
> test coverage** to refute *with*, and the refutations they did produce are visibly stronger for it
> (four of six name a specific caller-side guard, an XAML binding, or computed both sides of the
> arithmetic). The honest reading is that the C# early code has a higher true-positive density AND
> that the skeptics were better equipped. Carry the 33% into U2's calibration paragraph as a range
> ("33–73% across segments"), not as a new constant.
>
> **3. One finding is a regression this audit itself introduced.** V5 is the C# half of cluster ①'s
> `ComputeSetElementStride` fix (`5ef4c2b`, 2026-08-14). The DLL side shipped a two-argument,
> alignment-aware stride; **three independent C# copies of the one-argument formula were left
> behind**, each still carrying a doc comment that claims it mirrors the DLL. Fixing one side of a
> duplicated computation without grepping for its mirrors *re-created cluster ④ while closing
> cluster ①*.

> ### ✅ V1, V2 and V5 FIXED — build 2830, 2026-08-14
>
> One commit, because they are one subsystem and **not independently correct**: V1's write address is
> computed *from* V2's stride, so shipping V1 against the stale stride would have aimed the corrected
> offset off a wrong base. V5 came along because the fix adds fields that the multi-select clone must
> carry — leaving it would have made the new stride reach every path except the filtered export.
>
> **The shape of the fix is the point.** The C# no longer computes container geometry at all. The DLL
> publishes the stride it *actually used to read the elements* (`map_stride` / `set_stride`, additive
> wire fields set at all four `Ubel.cpp` walk sites), and one new
> [`Core/ContainerGeometry.cs`](../ui/UE5DumpUI/Core/ContainerGeometry.cs) consumes it. All **three**
> stale copies of the formula are deleted; the old expression survives only as
> `ContainerGeometry.FallbackStride`, documented as correct only for `alignof(T) <= 4` and reachable
> only when the DLL supplied nothing. UI and DLL can no longer disagree about where an element is,
> because there is only one number.
>
> **V1 needed a second half the finding did not name.** The row also reported
> `Size = MapKeySize + MapValueSize`, and `Size` reaches `FieldValueConverter.TryConvert` as the
> **write length** — so a `TMap<int32, enum4>` would have written 8 bytes over a 4-byte value even
> once the address was right. The row now describes the value in all three respects: type, address
> and size.
>
> **Verified with a negative control, not just a green run.** The 8 new tests in
> `ContainerGeometryTests` pass (3575 total, 0 failed). Both fixes were then reverted in
> `ContainerGeometry.cs` and the suite re-run: **5 tests failed** — the helper, the seam
> (`PopulateMapContainerFields`) and the clone — then the fix was restored and green re-confirmed. Per
> working-lessons §1.3 the seam test is the one that matters: the pre-existing
> `FieldValueConverterTests` passed throughout, in both directions, because the helper was never wrong.
>
> ⬜ **Not yet verified in-game** — tracked in [todo.md](todo.md#pending-live-game-verification-verify-only--no-code).
> `Map_I64ToI32` / `Map_StrToInt` in `DumperTest` are existing witnesses for the DLL half; what needs a
> live check is the **UI** half: that the Address column, an inline edit, and a pushed CE record all
> land on the value.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **V1** ✅ | **HIGH** | `LiveWalkerViewModel.cs:1380` (`PopulateMapContainerFields`) | A TMap element row is built with `TypeName = MapValueType` (so `IsEditableType` passes for any scalar-valued map) but `FieldAddress = dataBase + index*stride` — the TPair base, which is **the key**. The `+ valOffset` the same method computes 40 lines earlier for struct values is not applied. Inline-editing a `TMap<FName,int32>` value writes the user's 4 bytes over the **FName key**; the same wrong address is what "+CE" pushes as a record and what "Copy address" yields. | S / med |
| **V2** ✅ | MED | `LiveWalkerViewModel.cs:1706` (`ComputeSetElementStride`) | The C# stride mirror is still `AlignUp(elemSize,4)+8` after the DLL moved to `Align(Align(elemSize,alignof(T))+8, alignof(T))` in `5ef4c2b`. **Three copies** are stale (`LiveWalkerViewModel.cs:1706`, `CeXmlExportService.cs:3513`, `CsxExportService.cs:877`) across **5 map call sites**. TSet is unaffected by construction. Every map element address the UI computes itself is short by 4+ bytes past index 0. | M / med |
| **V3** | MED | `LiveWalkerViewModel.cs:2366` (`FindReferencesAsync`) | No post-await navigation guard. Every other long round-trip snapshots `CurrentAddress` + `Breadcrumbs.Count` before its `await`; this one reads `CurrentObjectName` and refills `References` *after*, and runs on the **bulk** lane precisely so the user can keep navigating. A scan started on A lands A's reference list under B's header. | S / low |
| **V4** | MED | `LiveWalkerViewModel.cs:5575` (`NavigateToAsync`/`GoBackAsync`) | Navigation commands are separate `AsyncRelayCommand`s with no shared re-entrancy gate, so a drill-down that started first can append its crumb onto a spine `GoBackAsync` has already truncated — leaving a leaf whose `FieldOffset` is relative to a parent no longer in the list, i.e. a silently wrong CE pointer chain. | M / med |
| **V5** ✅ | MED | `LiveWalkerViewModel.cs:1622` (`FilterContainerToElement`) | The Map branch rebuilds the container field property-by-property and **omits `MapValueOffset`**, so the CE emitter falls back to `valOffset = MapKeySize` and derives a different stride. Exporting *selected* map elements produces a different and wrong layout from exporting the same map unfiltered. The sibling clone at `CeXmlExportService.cs:1030` preserves it — the two clone sites have drifted. | S / low |
| **V6** | MED | `LiveWalkerViewModel.cs:5927` (`UpdateDisplay`) | `RefreshAsync` deliberately keeps the field-search keyword alive (`clearFieldSearch: false`) but `UpdateDisplay` swaps in new `LiveFieldValue` objects whose `IsSearchMatch` defaults false, and nothing re-runs `ApplySearch`. Highlights vanish while `SearchMatchCount` and the ↑/↓ buttons keep advertising N live matches. | S / low |
| **V7** | MED | `LiveWalkerViewModel.cs:4812` (`RefreshAsync`) | Refresh failures — including the 10 s hard timeout — are reported through `SetError` → `ErrorMessage`, **a property `LiveWalkerPanel.axaml` never binds**, after `ClearStatus()` blanked the one bound status line. A failed refresh is byte-identical on screen to a successful one, stale values render as live, and a subsequent edit prints `Written: …` over them. | S / low |
| **V8** | LOW | `LiveWalkerViewModel.cs:1348` | Container drill-down renders an `arrayLimit`-truncated element list (default 128) as the complete container. `BuildContainerLimitWarning` computes exactly this warning and is wired into **one** site: the Copy-CE-XML path. | S / low |
| **V9** | LOW | `ObjectTreeViewModel.cs:426` (`SearchAsync`) | `SearchObjectsAsync` is called without the `CancellationToken` it accepts, and `CancelLoadCommand` cancels `_loadCts` — which `SearchAsync` itself cancelled at `:411` and never replaced. For the whole search the panel's only enabled control is a Cancel button that structurally cannot do anything. | S / low |
| **V10** | LOW | `PointerPanelViewModel.cs:551` (`Update`) | `Update(EngineState)` unconditionally resets `IsScanning`/`ScanComplete`/`ScanStatusText`, but `ExtraScanAsync` owns `IsScanning` across its 1.5 s polling loop and clears it in its own `finally`. `Update` is reachable mid-scan via the fire-and-forget `ApplyOverrideAsync`, whose ComboBox is gated on `IsApplyingOverride`, not `IsScanning`. | S / low |
| **V11** | LOW | `PointerPanelViewModel.cs:988` | `CreateSymbolScriptAsync`'s `bool` is branched on only to pick `_log.Info` vs `_log.Warn`. Neither branch touches a bound property, so "Register symbol" looks identical whether CE registered it or the bridge never reached CE. The sibling call site in `LiveWalkerViewModel` does this correctly. | S / low |

**Verified independently (not agent-reported).** Three checks, run by hand before recording:

1. **V1 re-derived end-to-end at both ends of the wire.** UI: `TypeName = sourceField.MapValueType`
   (`:1365`) → `LiveFieldValue.IsEditable` (`Models/LiveFieldValue.cs:318-321`) → `IsEditableType`
   accepts `IntProperty` (`Core/FieldValueConverter.cs:18-24`) → `FieldGrid_BeginningEdit` cancels
   only `!IsEditable` (`Views/LiveWalkerPanel.axaml.cs:382`) → `WriteMemAsync(field.FieldAddress, …)`
   (`:5016`). DLL: `elemAddr = sa.Data + idx*stride`, key read **at** `elemAddr` (`Ubel.cpp:4202`),
   value read at `elemAddr + valOffset` (`Ubel.cpp:4228`), and `mapValueType` is a genuine property
   type name (gated on containing `"Property"`, `Ubel.cpp:4129`). The codebase **already knows** the
   correction: `MapValueDrillOffset`'s doc comment (`:938-948`) states it outright, and it is applied
   at exactly one call site — `NavigateToFieldAsync`'s `navOffset` (`:988`) — while the edit and CE
   consumers do not. That is cluster ① in its purest form: *a correction that exists and is applied at
   only some of its sites.*
2. **V2's blast radius is 3× what the finder filed.** It reported one stale copy; `grep` found
   **three**, all carrying a "Mirrors `Mem::ComputeSetElementStride` in the DLL" comment that is now
   false. Worked arithmetic, `pairSize=12, pairAlign=8`: DLL `Align(Align(12,8)+8,8) = 24`, C#
   `Align(12,4)+8 = 20`. They agree only when the pair alignment is 4.
3. **A whole claim category pre-refuted by measurement.** I swept every expression-bodied computed
   property in the three files against every `OnPropertyChanged(nameof(…))` /
   `[NotifyPropertyChangedFor]`: `PointerPanelViewModel` has 57 computed / 58 raised / **0 never
   raised**. Because that is an *absence*, I ran the negative control working-lessons §1.2 demands —
   deleting the single `OnPropertyChanged(nameof(ShowVersionTooOldWarning))` (the one the file's own
   comment records as historically missing) from a scratch copy made the detector report it. The
   detector can fail, so the zero is real. The three apparent orphans are correct by design
   (`FilterHistory`/`FunctionFilterHistory` return self-notifying `ObservableCollection`s;
   `DisplayNumber => SlotIndex + 1` reads an `init`-only property). **Do not re-raise
   "computed property never notifies" against these three files.**

-----

### U2 — Export services (CE XML / CSX / SDK / USMAP / Symbol) — ✅ scanned 2026-08-14

**21 agents** (5 emitter lenses → 4 refute batches → 12 second-lens), 0 errors. Run `wf_4afb1a7b-927`.
20 raw claims → 16 after dedupe → **4 refuted (25%)** → 12 confirmed → **8 distinct** after merging
two lens-duplicate clusters: four separate reports of the same USMAP version/layout desync
(`:174` ×2, `:175`, `:203`, `:233` — one defect with three concrete desync points) and two of the
same SDK bitfield defect (`:221` / `:382`).

**Tally: 2 HIGH · 5 MEDIUM · 1 LOW.** Seven HIGHs were claimed; after merging, **two distinct HIGHs
survived** and one (`CeXmlExportService.cs:2141`) was downgraded to MEDIUM.

> **1. A shipped export has never once produced a usable file.** W1 is not a regression — `git log -S`
> shows the `Version` constant has exactly **one** commit in the file's history (its creation,
> `7f91295`, **2026-03-01**) and the string `bHasVersion` has **never appeared in any revision**. The
> menu item (`MainWindow.axaml:284`) has been shipping for 5½ months and cannot ever have written a
> parseable header. This is D5/F8's shape again — *the feature that was empty all along* — and it is
> the second time this audit has found one, which makes it a pattern rather than an accident: **an
> export whose consumer is an external tool is never exercised by our own tests, so nothing in the
> project notices that it is dead.**
>
> **2. The refutation rate fell again — 25%, against a 52% running mean.** U1 was 33%, U2 is 25%. The
> emitter code is genuinely denser in real defects than the DLL was, and the reason is visible in the
> findings: **an emitter's output is validated by a program we do not run.** A wrong CE varType, a
> wrong SDK offset and a malformed .usmap all leave our process looking like a success.
>
> **3. Two findings were confirmed against the repo's OWN vendored sources, not the internet.** The
> USMAP skeptic refused to lean on the finder's CUE4Parse citation and instead read
> `vendor/RE-UE4SS/.../Generator.cpp` and `vendor/Dumper-7/.../MappingGenerator.cpp`, which are two
> independent canonical writers sitting in this tree. That is the strongest evidence any segment of
> this audit has produced, and it was available for free.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **W1** | **HIGH** | `UsmapExportService.cs:16`,`173-178`,`203`,`233` | The file stamps `Version = 3` (`LargeEnums`) but writes the **version-0 layout**, in three independent places. (a) `version` is followed straight by `compression` — the mandatory `int32 bHasVersionInfo` that every `version >= PackageVersioning(1)` file must carry is missing, so a reader's `ReadBoolean()` consumes 4 bytes of the size field and throws before a single name is read. (b) enum member count written `uint8`, but `>= LargeEnums` requires `uint16`. (c) per-property ArrayDim written as `(ushort)0` while the format (and the code's own adjacent comment) says `uint8` — so every struct's first property slides the stream by one byte. The file's own comment is also wrong: it says "3 = LongFName", but LongFName is **2**. | S / low |
| **W2** | **HIGH** | `SdkExportService.cs:358` (+`EmitClassHeaderFromLive`) | The emitter assumes `ClassInfoModel.Fields` holds only the class's **own** properties, but `Ubel::WalkClass` deliberately prepends the entire SuperStruct chain and nothing filters it out. Every base-class property is therefore declared a **second time** inside a `struct X : public Super` that already inherits it, so `offsetof` is wrong for every derived class in the generated SDK — the header compiles and is silently mislaid out. | M / med |
| **W3** | MED | `SdkExportService.cs:221` + `:382` | N `FBoolProperty` bitfields that UE packed into **one byte** are each emitted as a whole `bool` and the layout cursor advances by `Size` for each, so the struct grows N−1 bytes from that point. The emitter only pads when `offset > cursor`, so once the bools overshoot, **no padding can compensate** and every subsequent member — and the trailing `// Size:` — is shifted. | M / med |
| **W4** | MED | `CeXmlExportService.cs:3522` + `:3586` (`BuildDropDownContent`) | `<DropDownList>` bodies are built from live FName / enum-name strings read out of game memory and interpolated **raw**, while every `<Description>` goes through `EscapeXmlContent`. One `&` in a `TArray<FName> Tags` entry (`Bow & Arrow`) makes the whole CheatTable malformed — and `EscapeXmlContent`'s own doc comment states the consequence: CE rejects the **entire document**, so a multi-thousand-entry export imports as nothing with no indication which record was at fault. This is audit #4's **B3 defect surviving at the one site B3 did not cover**. | S / low |
| **W5** | MED | `CeXmlExportService.cs:2141` (`EmitDrilledPointer`) | The scalar pointer-drill branch gates on the broad `IsObjectPropertyType`, which includes `WeakObjectProperty` / `SoftObjectProperty` / `SoftClassProperty` / `LazyObjectProperty`, and emits `Offsets=[0]` for slots whose first 8 bytes are **not** a `UObject*` — a weak pointer is an index+serial pair. The array path has an explicit regression test against exactly this. (Claimed HIGH, cut to MEDIUM by the second lens.) | S / low |
| **W6** | MED | `CeXmlExportService.cs:2665` (`MapInnerTypeToCeField`) | `EnumProperty` is hardcoded to CE type `"4 Bytes"` while element **addresses** are laid out with the DLL's real `ArrayElemSize`/`SetElemSize` (1 for the standard `enum class : uint8`), so CE reads 4 bytes at every 1-byte-spaced element. `CeWidthForSize` exists to fix precisely this and the Map and struct-array emitters already route through it. | S / low |
| **W7** | MED | `UsmapExportService.cs:323` | The name table is serialized first, from a snapshot of the pre-registration pass, but the struct-writing pass then calls `GetOrAdd(… : "None")` in four places. `"None"` is never pre-registered, so it is appended at index N **after** the file already declared exactly N names, and every affected property references an out-of-range index. | S / low |
| **W8** | LOW | `UsmapExportService.cs:90` | The class collector accepts only `ClassName is "Class" or "ScriptStruct"`, so every `BlueprintGeneratedClass` / `AnimBlueprintGeneratedClass` / `WidgetBlueprintGeneratedClass` is silently dropped — on a normal shipped title that is thousands of classes vs a few hundred native ones. Both sibling exporters use the whitelist predicate, and `SdkExportService` carries a comment naming this exact bare-`"Class"` check as a bug fixed in DLL build 673. | S / low |

**Verified independently (not agent-reported):**

1. **W4 was found by hand BEFORE the agents reported, and reproduced end-to-end.** A scan of every
   interpolation reaching XML markup in both emitters (filtering indents, counters and hex addresses)
   left 14 sites, of which `dropDownContent` was the only game-derived one. A throwaway test built a
   `TMap<int32,FName>` whose value was `Bow & Arrow`, ran the real `GenerateInstanceXml`, and
   `XDocument.Parse` threw `XmlException: An error occurred while parsing EntityName`. The harness was
   deleted afterwards (scan segments ship no code) — **re-create it as the regression test when W4 is
   fixed.** The agents then reported the same defect independently, which is convergence, not a second
   source.
2. **Why no test caught W4 — the seam again.** All five `CeXmlEscapingTests` put the game string in a
   map **key**, which lands in `<Description>`. None puts one where it lands in `<DropDownList>`. Same
   shape as U1's HIGH: the helper is tested, the path that bypasses it is not.
3. **W1 re-derived from this repo's own vendored writers.** `vendor/RE-UE4SS/…/Generator.cpp:541-547`
   writes `magic → version → int32 bHasVersionInfo → compression → sizes`; ours goes
   `version → compression` with nothing between. `vendor/Dumper-7/…/MappingGenerator.h:13-18`
   documents the layout literally (`if (version >= Packaging) int32 bHasVersionInfo;`), and
   `Generator.cpp:29-33` pins `LongFName = 2, LargeEnums = 3` — so the constant's own comment is wrong
   about which version it names.

-----

## 3. Refuted — do not re-raise

### From U2 (4 of 16 — 25%)

The lowest kill rate of the audit. All four are worth reading because none died on "unreachable" —
they died on a guard or a cap the finder had not looked for:

- **`SdkExportService.cs:364`** — a *second* bitfield claim, arguing the shared offset shifts
  following fields. Refuted as a duplicate framing of the surviving W3 with the mechanism wrong.
- **`SdkExportService.cs:320`** — "generated headers declare none of the types they use / no
  `#include`". Refuted: the single-class export is documented as a fragment, not a translation unit.
- **`CeXmlExportService.cs:1411`** — AOB-symbol export omitting the UE5.7+ "UNVERIFIED packed layout"
  caveat its three siblings carry. Refuted on reachability.
- **`CsxExportService.cs:225`** — CSX drilldown has no entry cap, no shared-object dedup and no
  cancellation. Refuted: a depth bound already terminates it.

### From U1 (6 of 18 — 33%)

The lowest kill rate of any segment, and the refutations are correspondingly better-argued — four of
the six name a specific caller-side guard, an XAML binding, or compute both sides of the arithmetic.

- **`LiveWalkerViewModel.cs:6123`** — Functions keyword box cleared without flushing the keyword
  memory. Code facts right, *reachability* wrong: computed both sides, the pending timer is already
  disposed for sub-minimum text.
- **`PointerPanelViewModel.cs:159`** — the AOT lens's own **negative result**, filed as an INFO row.
  Correctly classified as not-a-finding. Its sweep re-ran clean: no `Activator` / `MakeGeneric` /
  `GetCustomAttribute` / reflective `Invoke` anywhere in the three files. **The AOT/trim lens found
  nothing in U1** — worth knowing before spending a lens on it in U2–U5.
- **`LiveWalkerViewModel.cs:5534`** — `ApplySearch` replacing the bound collection wipes multi-select.
  Refuted: the load-bearing premise ("Avalonia clears selection and raises `SelectionChanged` on an
  `ItemsSource` identity change") was asserted, never verified, and the branch that matters is
  contradicted by the code the claim itself cites.
- **`ObjectTreeViewModel.cs:473`** — `ApplyFilter` nulling `SelectedNode` blanks the Class/Struct
  panel. Refuted by a caller-side null guard: `MainWindowViewModel.cs:717-720` is the only subscriber
  and guards it.
- **`ObjectTreeViewModel.cs:426`** — a *second* claim about `SearchAsync`, this one alleging a missing
  post-await generation guard. Refuted on reachability, and one asserted symptom is contradicted by
  the code. (The **cancellation-token** half of that function is a separate, surviving finding, V9 —
  do not let this refutation suppress it.)
- **`PointerPanelViewModel.cs:725`** — a failed UE-version override leaving the ComboBox showing a
  version the DLL never applied. Refuted: unreachable through the bound controls, and the stated
  premise is factually wrong — the DLL's only error branch is `Fern.cpp:1615`'s range check, which
  the ComboBox cannot violate.

### From D5 (11 of 19 — 58%)

**The whole wire-protocol and connection-teardown surface came out clean**, and three refutations are
worth more than the findings they killed because of *how* they died:

- **`Fern.cpp:4904` — "`invoke_function` sizes the ProcessEvent parameter buffer from the wire's
  `parms_size` and never cross-checks the resolved UFunction's real `ParmsSize`."** The missing
  cross-check is structurally real and the citations are exact; refuted because neither half of the
  failure scenario is reachable.
- **`Fern.cpp:895` — "`ReadLine` issues one `ReadFile` syscall per request byte, inside the game
  process."** The code property is real (one byte per call, vs a single `WriteFile` on the way out).
  **Refuted on arithmetic** — the quantification (1.6–2.1 µs *per request byte*) is what would have
  turned a style observation into a finding, and it does not survive being computed.
- **`Fern.cpp:908`** — an over-length request kills the connection with no error response, making
  `write_mem`'s advertised 65536-byte ceiling unreachable. **The arithmetic is right** (65536 bytes =
  131072 hex characters, over `PIPE_BUF_SIZE`) and the ceiling genuinely is unreachable — refuted
  because no shipping client can produce the request.
- **`Fern.cpp:587`** (`Stop`'s wake-poke gated on a racy `m_listenPipe` snapshot) and **`Fern.cpp:643`**
  (unlocked `scanThread` assignment → double join throwing out of `UE5_Shutdown`): both refuted on
  reachability — the interleave is bounded by a single syscall in the first, and the second needs a
  nanosecond-scale alignment on top of two simultaneous human actions in two applications.
- **`Fern.cpp:860`** — "`HandleConnection`'s manual cleanup is bypassed when `RunThreadGuarded`
  swallows a throw, permanently leaking one of only three pipe instances." Mechanism real; **no throw
  can get out** — `DispatchCommand` is one giant try (`:1492-1498` catches the `json::parse` throw,
  `:1508` opens a second try enclosing every handler and every return).
- **`Frieren.cpp:746`** — "`UE5_AutoStart` is a multi-second blocking export called through a 5 s
  `executeCodeEx`." Refuted: the revive path re-enters `UE5_Init` with the hint cache already written
  for that PE hash, which short-circuits both expensive phases, so the "5–8 s band" premise is wrong
  and the quoted budget comment describes a different path.
- **`Frieren.cpp:1032`** (`UE5_SetDebugCamera` not escalating to the controller swap on a failed
  invoke) — refuted on three grounds; the escalation is deliberately gated on a *confirmed* fire, and
  the finder's own headline trigger (`-5` timeout) is the case where escalating is actively harmful
  because the request stays queued.
- **`Frieren.cpp:648`** — `UE5_SetObjectDecryption`'s stored callback. Refuted: the retract half of
  the contract **is** documented at the declaration (`Frieren.h:52-55`).
- **`Frieren.cpp:1761`** — "`UE5_StartPipeServer` returns true and `AutoStart` publishes `INIT_READY`
  when it started no server, because the pipe name is machine-global." Refuted: `INIT_READY` and
  `INIT_SKIPPED` are **behaviourally identical in the only consumer that exists**
  (`CeReadinessLua.cs:82` breaks its poll on either), so the divergence has no bad outcome.
- **`Frieren.cpp:992`** — debug-camera force-OFF "reports success from the flag it just cleared".
  Refuted: the documented contract is *bail-before-any-write*, and the three guards deliver exactly
  that.

### From D4b (9 of 18 — 50%)

Eight of the thirteen claimed MEDIUMs died here. **Five of the nine refutations were won by finding a
comment that names the defect the code already prevents** — the strongest signal yet that this
codebase's previously-audited surfaces are genuinely fixed, and that a finder reading them cold
re-discovers the *original* bug rather than a live one.

- **`Mimic.cpp:370` — "`StopThread` ignores its 3 s wait, then `CloseHandle`s and `memset`s the
  mailbox under a still-running poller."** Raised by **two** lenses independently, refuted both
  times, on different grounds each time. The load-bearing premise ("the poller is cancel-immune so
  the walk is not shortened") is false: `Tot.h:31-33` scopes `MarkCancelImmune` to the **per-command**
  cancel only, `Requested()` still returns `g_shutdown`, and `Frieren.cpp:594-595` latches
  `RequestShutdown()` **before** `StopThread()`, so the walk bails at `Aura.cpp:1521`.
- **`Mimic.cpp:211` — the whole-body exception guard.** Refuted as a MEDIUM by the second lens; see
  † above for the INFO that survives and why the "unrevivable" half was wrong.
- **`Stark.cpp:208` — "the hook's exception guard allocates inside its `catch`, so it cannot contain
  the allocation failure it was added for."** Refuted: `Routine.h:45-57` records that the DQ7R
  fast-fail was `~std::thread` calling `std::terminate` **directly** — no exception was ever thrown,
  so the guard was not added for that condition.
- **`Sein.cpp:301` — "a second UE5Dumper module truncates and interleaves the first's five live log
  files."** Refuted: **three** further guards exist, each in the exact injection vector named
  (`ProxyDeployViewModel.cs:937`'s `DumperLoaded` short-circuit among them).
- **`Sein.cpp:121` — "`GetLogDirectory`'s `.` fallback aims the folder sweep at the game's CWD."**
  The asymmetry is real; refuted on reachability — it is gated on `SHGetKnownFolderPath` failing,
  which the claim itself justified only speculatively.
- **`Flamme.cpp:395`** ("override/timeout writes no-op on an empty PE hash yet reply `persisted:true`")
  — unreachable at two independent points, both client-side (`PointerPanel.axaml:18`'s `IsVisible`,
  and `if (!HasData) return;` in both change handlers). **`Flamme.cpp:87`** ("four writers share one
  staging path with no lock") — refuted by call-graph: all four are reachable only from inside
  `UE5_Init`, serialized by `s_initMutex` + the `s_initialized` latch.
- **`Lugner.cpp:80` — "the per-export magic static freezes a transient resolve failure for the
  process lifetime."** The pattern is real (17 sites in `Lugner.cpp`, 5 in `Lugner_Dinput8.cpp`, and
  `LoadRealVersion` *does* retry while the function pointer does not) but the trigger requires
  `LoadLibraryW` on a fully-qualified System32 path to fail **once and then succeed**, which was
  asserted rather than demonstrated.

### From D1 (13)

Both claimed **HIGH**s died here, on evidence rather than opinion:

- **`Ubel.cpp:83` — "ReadFName reads Number at +4, which is DisplayIndex on CasePreservingName games."**
  **REFUTED by the engine source vendored in this repo.**
  `vendor/UnrealEngine/…/UObject/NameTypes.h:1257-1267` declares the members in the order
  `ComparisonIndex` → `Number` → `DisplayIndex`, so `Number` is at **+4 even under
  `WITH_CASE_PRESERVING_NAME`**, and the same header `static_assert`s it at `1247-1249`.
- **`Ubel.cpp:4503` — "`FSTRUCTPROP_STRUCT`/`FARRAYPROP_INNER` mutated at runtime with no reader
  synchronisation."** **REFUTED**: the probe list tries delta 0 **first** and the write sits inside
  `if (delta != 0)`, so a write only ever *repairs* an offset that already failed — the race is
  monotonically improving, never a correct→wrong transition. Both globals are naturally-aligned
  `int`s on x64 (no torn read), and `CorrectSubclassOffsets` runs under `s_calibrationMutex` before
  the field loop in both callers.

### From D4a (5 of 9 — the entire SEH/AOB surface)

Recorded together because the pattern matters more than the items: **every claim against the
SEH-guard contract and the AOB scanner was refuted.**

- **`Macht.cpp:57` — "`WriteBytes` restores only the FIRST page's protection to the whole range, and
  can leave game memory writable."** The most dangerous-sounding claim in the segment; refuted.
- **`Macht.cpp:461`/`520` — the AOB scan family never polling `Tot::Requested()`, and
  `AOBScanAllModules` faulting on a `FreeLibrary` mid-sweep.** Refuted (note this is the *same claim
  shape* that was CONFIRMED against `Genau` as D2/G2 and against `Aura::FindByAddress` as D3/A7 —
  here the guards exist).
- **`Macht.cpp:556`/`629` — two `ScanRegionBatch` region-size off-by-ones** against the batch's
  shortest vs longest pattern. Refuted.

### From D3 (8 of 18)

- **`Aura.cpp:3577` — "weak/soft/lazy and delegate graph edges are emitted with the same geometry as
  real pointer edges."** Survived the skeptic, **killed by the second lens**.
- **`Aura.cpp:3838`** `RecoverViaWorldLevel` reporting `ok_via_level` on a failed tail BFS;
  **`Aura.cpp:4831`** `ListClasses` capping by GObjects index then reporting the cap as the true
  total; **`Aura.cpp:4428`** a preview pass whose early-exit "can never fire"; **`Aura.cpp:7851`**
  an unbounded rejected-edge walk in the cross-object group scan; **`Aura.cpp:8430`** and
  **`Aura.cpp:7382`** missing cancellation polls in `RefineCandidates`/`RefineGroupCandidates`
  (note A7 *is* a confirmed instance of this class — these two specific sites were refuted, the
  `FindByAddress` one was not); **`Aura.cpp:3857`** a visited-cap constant sized on a bad estimate.

### From D2 (19 of 26 — a 73% kill rate)

All seven claimed HIGHs died here. The recurring shape: **a criticism that is structurally true but
whose bad outcome a later phase already prevents.** The most instructive:

- **`Serie.cpp:273`/`298` + `Genau.cpp:1740` — "Genau proves the `Blocks[]` offset, discards it, and
  Serie re-derives it from a candidate list missing 0x18/0x28, hardcoding 0x10 on total failure."**
  Claimed HIGH by two lenses. Refuted: the narrower list is not the operative guard, and the
  downstream validation rejects a wrong base before it can be latched — the "silent blank names"
  outcome does not follow.
- **`Genau.cpp:1365` — "a refused GNames candidate leaves its FName-format globals latched."**
  Refuted: the winning candidate re-establishes them.
- **`Genau.cpp:3560` — "the Guid-less `Vector` fallback assumes a 4-byte-float FVector, so it can
  never validate on UE 5.0+ LWC."** Raised by two lenses, refuted both times.
- **`Serie.cpp:430`/`442` (use-after-free on re-init; unfenced field-by-field publish)**,
  **`Serie.cpp:593`** (wide FName branch skipping the XOR key), **`Genau.cpp:4753`** (hardcoded 504
  for failed version detection), **`Genau.cpp:2766`** (un-SEH-guarded whole-image loads),
  **`Genau.cpp:3348`/`4015`** (`USTRUCT_SCRIPT` success-path-only, raised three times),
  **`Genau.cpp:3088`** (`DetectCasePreservingName` counting failed reads as votes).

**Not refuted — `Serie.cpp:313-314` `DetectBlockOffsetBits`.** One skeptic refuted it; two did not.
Checked by hand and the refutation is **wrong** (see G4). Filed as a finding, not a refutation.

### From D1 (13)

Also refuted: the `FARRAYPROP_INNER` "partial update" claim (UE5.7 genuinely puts
`EArrayPropertyFlags` before `Inner`, so **not** dragging the other four offsets along is the correct
behaviour, documented at `Ubel.cpp:2926-2928`); `s_calibrationMutex` held across a probe loop (the
`s_checked` fast path means the window is one class walk, and no path takes the two locks in the
opposite order — the stale "all locks are leaf-level" comment at `Ubel.cpp:67-68` is a doc
inaccuracy, not a defect); `ReadStructArrayElements` negative-size bypass; `FindField` deep-copy cost;
`ResolveEnumValue`'s lazy latch; `WalkDataTableRows`' `FENUMPROP_ENUM` read; and
`ReadSoftObjectPath`'s "dead fallback" (raised twice by two lenses, refuted both times).

-----

## 3b. START HERE — next session picks up at U3

*State as of 2026-08-14, after U2. Read this section first; it is written for a session with no
memory of the previous one.*

> 🔴 **Two HIGHs are open, and one is cheap. Decide before starting U3.**
> - **W1** (`S`/low) — the `.usmap` export has produced an unparseable file since 2026-03-01 and the
>   fix is a handful of field widths plus the missing `int32`. Both canonical writers are vendored in
>   this tree to check against, and it needs a **round-trip test** (write, then read back with the
>   exact widths a real parser uses) or the next drift is equally invisible.
> - **W2** (`M`/med) — every derived class in the SDK header has the wrong `offsetof`. Bigger, because
>   the fix must decide where the SuperStruct filter belongs: in `Ubel::WalkClass`'s output, or in the
>   emitter. **W3 is in the same emitter and the same layout cursor**, so fix them together.
>
> U1's **V1/V2/V5 shipped** (build 2830) but still owe **in-game verification of the UI half** —
> see [todo.md](todo.md#pending-live-game-verification-verify-only--no-code). Cheap; good use of
> leftover budget in any window.

### The pacing rule, learned by hitting it

**ONE segment per 5-hour quota window. Do not plan two.** Measured on 2026-08-14: that window ran the
D4b takeover + the whole of D5 + six fixes (F1, F3a, F4, F6, F7, FR1) with their builds and in-game
verification, and reached **80% of quota**. A segment is a scan *or* a fix batch, not both, unless the
fixes are trivial. If a window has budget left after the segment, spend it on **verification**, which
is cheap and compounds — not on starting the next segment, which will be cut off mid-flight.

### What is done

**Scanning: 8 of 12** — D1, D2, D3, D4a, D4b (`8198309`), D5 (`e131c8a`), U1, U2. Still open:
**U3-U5, S1, T1**. **The DLL is fully scanned; everything left is C# and Lua.**

**Fixing:** cluster ① is 6 of 7 shipped, now on **both** sides of the wire (`5ef4c2b`, `c65fdfc`,
and build 2830 for the C# half U1/V2 exposed); A4 deliberately open. D5 shipped **F1, F3(a), F4, F6,
F7, FR1** across `0d9fcfa` / `a2b616a` / `cfaa5cd` / `1e5ab21`. U1 shipped **V1 (the HIGH), V2, V5**.
**Still open: W1 (HIGH), W2 (HIGH), W3–W8, V3, V4, V6–V11, F2, F5, F8, A4, U3, G7, U2.**
(Note the ID collision: `U2`/`U3` are D1 findings in `Ubel.cpp`, *not* segment names.)

### The C# lens set — as run in U1, with what it actually yielded

U1 ran these five and they are the right five for U2–U5, with one adjustment:

| Lens | U1 yield | Note for U2 |
|---|---|---|
| `domain-correctness` | **the HIGH + 2 MED** | highest-value lens by a distance; keep it first |
| `status-honesty` | 1 MED + 2 LOW | reliably productive; cluster ② is project-wide, not DLL-only |
| `mvvm-lifetime` | 2 MED | productive, but **drop the "computed property never notifies" prompt** — measured clean, see U1's verified-independently note |
| `async-cancel` | 1 MED + 2 LOW | productive |
| `aot-trim` | **nothing** | the lens self-reported clean and its sweep re-ran clean. **Consider dropping it or folding it into another lens** for U2–U5 and spending the slot on a second `domain-correctness` pass with a different sub-scope |

> **The skeptics get something the DLL segments never had: real test coverage.** ~3,567 C# tests
> compile these files, so "no test covers this" is not an available refutation and "a test asserts the
> opposite" is now an available one. Tell the skeptics to look for the test — and remember the trap
> from working-lessons 1.3: a green test can still miss the seam. **U1's HIGH lives exactly there:**
> `FieldValueConverterTests` pins `IsEditableType` in isolation while nothing covers
> `PopulateMapContainerFields` or `CommitFieldEditAsync`, so the tested helper is fed a wrong address
> by an untested caller. **Point the finders at seams, not at helpers.**

### How to run it

Copy the scheduled-task prompt at `~/.claude/scheduled-tasks/audit5-segment-d4b/SKILL.md` and change
the scope, line counts and lens list. It ran unattended in **its own session with its own quota**
(14:31 -> commit 15:20, ~49 min) and survived a Claude Desktop re-login. The workflow script to clone
is now **U1's** — `audit5-seg-u1-viewmodels-wf_0fbfa801-46d.js` under the session's
`workflows/scripts/` — because it already carries the C# lens set, the test-coverage instruction to
the skeptics, and the AOT-specific refutation guidance; the D5 script
(`audit5-seg-d5-fern-frieren-wf_39143753-6df.js`) remains the C++ flavour. Both are 5 finders ->
skeptic batches -> second lens on surviving HIGH/MED, and log a wipeout warning when a finder dies.

**U3 scope** (from section 1): dump services + MainWindow VM — 1517 + 437 + 1366 early lines =
**~3,320**. `MainWindowViewModel` is the composition root and command surface, so the U1 lens set
(mvvm-lifetime / async-cancel / domain-correctness / status-honesty) fits it better than U2's emitter
lenses did. Keep U2's **round-trip instinct** though: ask of every dump artifact whether anything ever
reads back what was written.

**What U2 proved about lens choice, carry it forward:** `aot-trim` was dropped for U2 (it found
nothing in U1) and nothing was lost. `domain-correctness`/`pointer-chain` and `status-honesty` were
the two productive lenses in both C# segments — weight them, and give one lens the *consumer's*
point of view (in U2 that was "does the artifact parse in the tool that reads it", which produced
both HIGHs).

**Put in every agent prompt** (measured to improve output): the calibration — *156 raw claims, 75
refuted (**25–73% per segment**, ~48% overall; the two C# segments are the two lowest at 33% and 25%),
eighteen HIGHs claimed, fifteen died and three were real* — plus *"read the surrounding comments and
the callers first"* (five of D4b's nine refutations were won by finding a comment naming the defect
the code already prevents), the **seam** instruction above, and **REPORT ONLY, no edits**.

**Do not re-derive:** everything already in sections 2 and 3, especially D5's `Fern.cpp` /
`Frieren.cpp` findings, the six shipped D5 fixes, U1's eleven findings + six refutations (V1/V2/V5
now fixed), and U2's eight findings + four refutations.

### One tree, two sessions

A segment session commits its own work and is REPORT-ONLY, so another session may watch — but it must
stay **hands-off the working tree** until the segment commits, or its edits get swept into the
segment's commit.

> **The scheduled-task route works, and it is the right tool for the remaining seven segments.**
> D4b ran unattended from `C:\Users\Andyc\.claude\scheduled-tasks\audit5-segment-d4b\SKILL.md`
> (14:31:08 → commit 15:20:28, ~49 min) in **its own session with its own quota**, and it survived a
> Claude Desktop re-login mid-run. The prompt file is the template: scope by file with line counts,
> the workflow shape, the calibration paragraph, the do-not-re-derive list, and the write-up/commit
> contract. Two properties that made the handoff cheap — it commits its own work, and it is
> REPORT-ONLY, so a parallel session can hold the working tree as long as it stays hands-off (one
> tree, two sessions: **do not edit anything while a segment session is live**).

**D4b takeover check (this session, after the commit):** ST1 and SE1 re-read at source; **PX1
re-verified independently against the shipped binaries** — not with `dumpbin` but with a
throwaway PE export-table parser, which is the stronger check because it reads the artifact the same
way the loader does: `C:\Windows\System32\dinput8.dll` `@6 = GetdfDIJoystick`,
`dist\proxy\dinput8.dll` `@6 = UE5_AutoStart`, `@7 = UE5_CallProcessEvent`. Collision confirmed
exactly as filed. Three count errors corrected in place (D4b's LOW tally, and both distinct-count
cells in §4) — the totals were right, the per-segment cells were not.

**Fixing — cluster ① is 6 of 7 shipped** (`5ef4c2b`, `c65fdfc`):
M1, M2, M3, A2, U1, and U2 (which U1 forced in — they are not independently shippable).
**A4 is deliberately open** — it changes which candidates the value scanner emits, needs a `leafAddr`
dedupe, and was stopped by the maintainer pending in-game confirmation of the geometry underneath it.

**Verification — five of six confirmed on a live process** (see
[todo.md](todo.md#pending-live-game-verification-verify-only--no-code) for the evidence table).
**U2 has no vehicle**: TQ2 and Solarpunk both measured non-CPN, DumperTest cannot be (engine flag).

**Still unfixed and known:** A4 · U3 (*live-confirmed* 2026-08-14, not merely inferred) · G7 (reframed
to LOW after the original filing was retracted) · U2 (unit-tested only).

### The reusable win from today — headless in-game verification

No UI, no CE, ~4 commands. This is now the cheapest verification path the project has and it should
be the default for any DLL fix:

1. Launch the game (or the packaged `DumperTest`).
2. `powershell scripts\inject-ue.ps1 -ProcessId <pid>` — **pass the PID explicitly**; AUTO mode aborts
   when it sees more than one UE process, and the Epic Games Launcher counts as one.
3. Connect a ~10-line `System.IO.Pipes.NamedPipeClientStream` to `UE5DumpBfx`, `WriteLine` a JSON
   request, `ReadLine` the reply.
4. `get_offsets` first, then `find_instances` → `walk_instance`.

**Two traps, both hit today:**
- **`get_offsets` must be read as a triple.** `case_preserving` alone is meaningless — check
  `probe_ran` and `validated` with it. A sample taken 60 s after injection caught Solarpunk
  mid-initialisation and produced a confident wrong answer that survived into a commit.
- **Container element values live under the element's own key.** A `TSet` element is `k`, not `v`;
  reading the wrong one silently yields empty results that look like a real negative.

-----

## 4. Cross-segment rollup — read this before fixing anything

**Status: 8 of 12 segments scanned** (D1, D2, D3, D4a, D4b, D5, U1, U2). **68 distinct findings:
3 HIGH · 39 MEDIUM · 25 LOW · 1 INFO.** 156 raw claims, **75 refuted (48%)**. Remaining: U3–U5, S1, T1.
**The DLL is fully scanned; everything left is C# and Lua.**

| Segment | Agents | Raw | Refuted | Distinct | HIGH claimed → survived |
|---|--:|--:|--:|--:|---|
| D1 Ubel | 24 | 27 | 13 (48%) | 11 | 2 → **0** |
| D2 Genau+Serie | 36+14 | 26 | 19 (73%) | 6 **+1** ✦ | 7 → **0** |
| D3 Aura | 16 | 18 | 8 (44%) | 10 | 0 → 0 |
| D4a Macht | 10 | 9 | 5 (56%) | 3 | 0 → 0 |
| D4b Mimic+Sein+Stark+Lugner | 11 (+5 lost) | 18 | 9 (50%) | 9 | 0 → 0 |
| D5 Fern+Frieren | 14 | 19 | 11 (58%) | 8 **+1** ¤ | 1 → **0** |
| U1 LiveWalker+Pointer+ObjectTree | 16 | 19 | 6 (33%) | 11 | 1 → **1** |
| U2 export services | 21 | 20 | 4 (25%) | 8 | 7 → **2** ◊ |

¤ **D5's section holds 9 rows but its run produced 8** — F8 came from verifying F6, not from a
finder (same as D2's G7 below).

◊ **U2's seven HIGH claims collapse to two distinct findings.** Four were the same USMAP
version/layout desync seen through different lenses, one was downgraded to MEDIUM by the second lens.
The Distinct column counts findings, not claims.

✦ **D2's section holds 7 rows but its run produced 6.** G7 did not come from any finder — it was
found during the 2026-08-14 live-verification session and filed into the D2 section because that is
where it belongs by subject. The Raw/Refuted columns describe the *runs*; the totals above count
*rows*, so the two reconcile only through this row. (Counted directly: `grep -cE '^\| \*\*[A-Z]+[0-9]+\*\*'`
scoped to §2 = **68**, of which 3 HIGH / 39 MED / 25 LOW / 1 INFO. Re-derive it rather than trusting
the table — and **scope the grep to §2**, because over the whole file it also catches §4's cluster
tables and returns 71. ⚠ The severity column does **not** parse uniformly: five rows carry a footnote
marker between the ID and the severity (`G7` †=LOW, `PX1` ‡=MED, `MB3` †=INFO, `F1` ‡=MED, `F8` ¤=MED),
so a regex expecting `| **ID** | SEV` silently drops them; allow an optional marker (`(?: ✅| †| ‡| ¤)?`) and all 68 parse.)

**Eighteen HIGHs have been claimed across the audit; fifteen died and three are real** — and all
three real ones are in the **C# UI**, none in the DLL. For six segments the rule "every claimed HIGH
dies" held and the conclusion drawn was that *nothing found so far is an emergency*. U1/V1 ended that
(a user-initiated write of the wrong bytes to a live game process; **fixed**, build 2830), and U2 added
two more: **W1**, a shipped export that has never once produced a parseable file, and **W2**, an SDK
header whose every derived class has the wrong `offsetof`. The surviving half of the old lesson:
severities from a finder are still worthless *before* refutation — U2 claimed seven HIGHs that collapse
to two. The dead half: "nothing here is urgent" is no longer a safe default for the C# side.

**Why the HIGHs cluster in the UI and not the DLL is worth understanding, because it predicts where
U3–U5 will hurt.** The DLL's output is consumed by our own UI, which is exercised constantly. The UI's
export output is consumed by *other programs* — Cheat Engine, a C++ compiler, a `.usmap` parser — that
nothing in this project runs. A defect there produces a successful-looking export and is discovered
only by a user, if ever. W1 went undetected for 5½ months.

**The refutation rate is 25–73% across eight segments and is NOT a constant — and it is TRENDING
DOWN as the audit moves into C#.** The six DLL segments sat at ~44–73%; U1 came in at 33% and U2 at
**25%**, the two lowest. Two things move it, and they point the same way: the C# skeptics are the first
with **real test coverage** to refute *with* (their kills are visibly better argued), and the C# code
under audit is genuinely denser in real defects because its output is validated by programs we never
run. Quote the range to finders, not a constant, and do not budget U3–U5 on the DLL's ~50%.

### The clusters, in the order worth fixing

**① UE container reading is wrong in six independent places, and they compose. — highest value.**
All six corrupt the *same* user-visible thing: what appears when a `TMap`/`TSet` is expanded, plus
every graph edge and scan candidate derived from one.

| | Defect | Effect on the shared output | Status |
|---|---|---|---|
| D4a/**M3** | `ComputeMapValueOffset` guesses alignment (always, for struct values) | wrong `valueOffset` … | ✅ 2026-08-14 |
| D4a/**M1** | `ComputeSetElementStride` drops the `TPair`'s trailing padding | …which feeds a wrong stride | ✅ 2026-08-14 |
| D4a/**M2** | `ReadTSparseArray` reads `NumFreeIndices` at `+0x3C` not `+0x34` | count over-reported | ✅ 2026-08-14 |
| D3/**A2** | `IsSparseIndexAllocated` reads stale inline bits after heap spill | freed slots read as live | ✅ 2026-08-14 |
| D1/**U1** | Map/Set element sizes unvalidated | 1 GiB allocations, wild stride | ✅ 2026-08-14 |
| D1/**U2** | `InferScalarSize` hardcodes FName = 8, overriding the engine's 16 | halved `TArray<FName>` stride | ✅ 2026-08-14 † |
| D3/**A4** | deep pass drops depth-1 leaves | `TMap<K,FStruct>` values unfindable | ⬜ **open** |

† **U2 was not originally in this cluster — U1's fix forced it.** Routing the Map/Set sizes through
`ValidateArrayElemSize` sends them through `InferScalarSize`, which *overrides* the engine's reported
size. With `NameProperty` hardcoded to 8, fixing U1 alone would have taken a `TMap<FName,V>` key size
of 16 on a CasePreservingName game and **replaced it with 8** — importing U2's bug into a path that
did not have it. The two are not independently shippable.

**A4 remains open** and is deliberately not batched with these: it changes what the value scanner
emits as candidates (dropping the depth-1 exclusion needs a `leafAddr` dedupe against what the static
index already emitted), so it carries a duplicate-candidate and hot-path cost risk that the others do
not, and it cannot be validated by the header-level unit tests that cover M1–M3.

Fix M3 → M1 → M2 in one commit (they are three lines in one header), then A2, then U1. **Add a test
for the *composition*, not the parts** — that is precisely why none of this was caught: both
`dll_helpers_test` cases and every PDB-verified data point in the tree happen to land on multiples
of 8, so none of them discriminates.

> ✅ **The C# half shipped in build 2830 — but read why it was ever separate.** `5ef4c2b`'s
> two-argument, alignment-aware `ComputeSetElementStride` was applied to the DLL and to **none of the
> three C# copies of the same formula** (U1/**V2**), each still carrying a doc comment asserting that
> it mirrored the DLL. Five map call sites disagreed with the engine by 4+ bytes per element past
> index 0. **Closing cluster ① on the DLL alone had re-opened cluster ④.**
>
> The repair was deliberately *not* a fourth copy of the alignment maths: the DLL now publishes the
> stride it already computed (`Ubel.cpp` walk sites) as `map_stride` / `set_stride`, and
> `Core/ContainerGeometry.cs` is the only client-side consumer. All three mirrors are deleted. **The
> standing rule this leaves behind: any fix to a layout computation must start with a `grep` for the
> formula across BOTH languages** — and where the input is an engine fact the wire does not carry
> (an alignment, a size), the answer is to send the number, not to re-derive it.

**② A reported status computed by a different path than the reality.** This is **audit #4's own 4a
root cause recurring in older code**, which is evidence it is a habit of this codebase rather than a
one-off: D2/**G1** (`bOffsetsValidated = true` while probes failed), D3/**A6** (Property Search
reports the *defining* class, so Force resolves an empty pool), D3/**A5** (Preview samples the CDO,
not a live instance), D4a/**M2** (count says 10, six rows render), and **three more from D5** —
D5/**F4** (`search_properties` reports the full pool as `scanned_objects` for a walk that stopped at
the 200-row cap), D5/**F6** (`walk_world` reports the page size as the level's actor count, having
read and discarded the real one), D5/**F7** (the `-7` string names an execution that provably never
happened). **D5 makes this the largest cluster (8 members) and the one with the clearest user cost:**
F4 is the exact shape behind four "the scan missed my field" reports, and its fix is the cheapest in
the audit — Fern can detect the cap locally, since `SearchProperties` stops *exactly* at `maxResults`.

**U2 adds the cluster's most extreme member and a new sub-shape.** U2/**W8** is the familiar one — a
USMAP export silently drops every Blueprint-generated class (thousands on a normal title) and reports
nothing. But U2/**W1** is the shape at its limit: there is no wrong *status* at all, because the export
reports success and writes a file no consumer can open. **Once the artifact leaves the process, "did
it work?" stops being a question our code can answer** — so this cluster's export members need a
different fix from its DLL members: not a corrected count, but a round-trip read-back of what was
just written.

**U1 shows the cluster is not a DLL habit — it is a project-wide one, and the UI half is worse in one
specific way: the DLL at least *computes* a status, while the UI computes one and then fails to
render it.** Four new members, all client-side: U1/**V7** (refresh failure, timeout included, routed
to `ErrorMessage` — a property the panel's XAML **never binds** — so a dead refresh is pixel-identical
to a live one and stale values keep rendering), U1/**V6** (`SearchMatchCount` and the ↑/↓ buttons keep
advertising N matches after a refresh silently dropped every highlight), U1/**V8** (a container
truncated at `arrayLimit=128` renders as complete; the warning helper exists and is wired into exactly
one unrelated path), U1/**V11** ("Register symbol" produces identical UI on success and failure —
the `bool` reaches only `_log`). **V8 is F4's exact twin one layer up**, which means the truncation
cluster now has a DLL end *and* a UI end and should be fixed as one story: the DLL reporting the cap
buys nothing while the panel that receives it has no place to show it.

**③ A cache keyed by an address the engine recycles, never invalidated** — D1/**U4**, **U5**, **U6**
(Ubel's class + name caches), D3/**A10** (Aura's per-class metadata), D5/**F3** (the *purge* half of
the same name cache: its only two purge sites are `begin_snapshot` and `trigger_scan`, neither
reachable from ordinary browsing, and the last-connection teardown that drops every other session
resource skips it). **One fix pattern, not five:** store an `(InternalIndex, SerialNumber)` witness and
validate it on hit — the same pair UE itself uses to detect a recycled slot. F3 additionally wants a
one-line `Ubel::ClearNameCache()` in Fern's `if (last)` teardown, which is a fix for the
*reconnect* case that the witness pattern does not cover.

**④ Layout knowledge duplicated instead of derived** — D1/**U2** (FName width hardcoded 8 while
`Ubel.cpp:126` and five `Aura.cpp` sites derive it), D1/**U8** (FName `Number` open-coded three times,
dropped in all three), D3/**A1** (a `>= 24` ternary that cannot express stride 20), and now the two
U1 members that make this the cluster with the worst recurrence record: U1/**V2** (the TSparseArray
stride open-coded **three** times in C# and left behind when the DLL's copy was fixed — see the
warning box in cluster ①) and U1/**V5** (`FilterContainerToElement` rebuilds a container field
property-by-property and drops `MapValueOffset`, while the sibling clone at
`CeXmlExportService.cs:1030` preserves it). U2 contributes the *partial-application* variant, where the
helper is correct and some call sites simply bypass it: **W4** (`EscapeXmlContent` is applied to every
`<Description>` and to no `<DropDownList>` — audit #4's B3 defect surviving at the one site B3 did not
cover) and **W6** (`CeWidthForSize` is routed through by the Map and struct-array emitters but not by
the enum array/set path).

In every case the correct derivation **already exists elsewhere in the tree**. V2 proves the failure
mode is not only "written twice originally" but "**fixed once, in one of the copies**"; W4 proves it is
also "**fixed once, at one of the call sites**". A property-by-property clone (V5), a re-implemented
formula (V2) and a skipped helper (W4/W6) are the same defect wearing different clothes — and the two
durable repairs are to carry the value across the wire rather than recompute it, and to make the
helper impossible to bypass rather than remember to call it.

**⑤ Long loops that ignore `Tot::Requested()`** — D2/**G2**, D3/**A7**. Note the same claim was
**refuted** against `Macht` (guards present), so this is a per-site fact, not a pattern to apply blind.

**⑥ A failure return discarded at the one site that could have reported it — new in D4b.** Distinct
from ②: there the reported status is *computed wrong*, here it is *never computed at all*, and the
subsystem then reports success and dies silently. D4b/**SE1** (`OpenFileInDir`'s `bool` dropped, so a
log category dies with nothing written anywhere — including into the file that *did* open),
D4b/**FL1** (`ofstream` state never tested before `fs::rename` publishes a truncated cache),
D4b/**FL2** (`fs::rename`'s throw caught and logged, staging file leaked). **One fix pattern:** a
shared `WriteAtomically(path, text)` closes FL1+FL2 together and stops the four copies drifting; SE1
needs the caller to route its failure into whichever category is still alive. Cheap, and each one
currently costs a maintainer the *evidence* they would debug with — SE1 in particular defeats
[log-verification-checklist.md](log-verification-checklist.md)'s grep-by-format-string procedure,
which reads an absent line as "the code path never ran".
D5 adds **FR1** — `UE5_AutoStart` drops `UE5_Init`'s `false` and publishes `INIT_READY` anyway. It is
the most expensive member of the cluster because the state it produces (serving pipe, no mailbox
poller) makes every CE `.CT` row report `status = 0`, which CLAUDE.md's own rule tells the user means
a *stale mailbox address* — so the discarded return does not merely lose information, it manufactures
a confident wrong diagnosis.

**⑦ A hazard the codebase already solved everywhere else, missed at one door — new in D5.** D5/**F1**:
`Heiter.cpp:288-301` refuses heavy work in `DLL_PROCESS_DETACH` and explains why, and `Routine.h:51-56`
documents the same hazard for every feature worker ("at process exit every feature worker is still
joinable, its static destructor runs, and the FIRST one to destruct kills the process") — and
`Fern::Stop`'s explicit `join()` / `wait_for` calls were simply not on that list, so `~Fern()` runs the
whole teardown at DETACH anyway. This is the inverse of D4b's dominant refutation shape: there, five
claims died because the code carried a comment naming the defect it prevents; here the comment exists,
names the defect, and **one path does not obey it**. Worth a sweep of its own: `grep` for namespace-
scope statics with non-trivial destructors in `dll/src` and check each against the DETACH policy.

### One chain crosses segments — fix order matters

**D2/G1 → D1/U1.** `Genau` reports `validated=true` over a blind `FPROPERTY_ELEMSIZE`; `Ubel`'s
Map/Set walkers then use that value as a `std::vector` size with no cap. Fixing **G1 first** shrinks
U1's blast radius, but U1 still needs its own clamp (a bad size can arrive other ways); fixing U1
alone leaves every *other* consumer of the blind offset wrong. **Both, G1 first.**

### Method notes worth carrying into the remaining segments

- **Calibrating finders is cheaper than refuting them.** Putting the measured refutation rate and
  "reserve HIGH for what you can demonstrate end-to-end" into the finder prompt (from D3 on) cut raw
  claims from 26 → 18 → 9 while the *confirmed* yield held (6 → 10 → 3 over shrinking scopes).
- **`git blame` settles intent vs omission.** D3/A1's second lens proved the stride ternary predates
  both stride-20 code paths, turning "deliberate?" into "omission" in one command.
- **When duplicate claims get split verdicts, decide it yourself.** D2's `DetectBlockOffsetBits` was
  refuted by one skeptic and confirmed by two; two minutes of arithmetic showed the *refutation* was
  wrong. Never file the losing side as do-not-re-raise without checking.
- **A segment whose refute pass dies has produced nothing.** Park it as UNVERIFIED and resume;
  the workflow's dead-skeptic fallback puts unrefuted claims in the `confirmed` array.
- **Verify the segment's own headline yourself — the pipeline's confidence is not evidence.** U1's
  HIGH had already passed a mandated skeptic *and* a second lens, which is exactly the state in which
  ten previous HIGHs still turned out wrong. Re-deriving it by hand took ~5 tool calls, confirmed it,
  and **found the finder had understated a sibling MEDIUM by 3×** (V2 reported one stale stride copy;
  `grep` found three). Budget one hand-check per segment for the top item; it is the cheapest
  quality step in the whole method.
- **Pre-refute a whole claim category with a script when the category is mechanically decidable.**
  Before U1's agents reported, a ~40-line scanner compared every expression-bodied computed property
  in the three files against every `OnPropertyChanged(nameof(…))` / `[NotifyPropertyChangedFor]`, and
  a **negative control** (deleting one known-historically-missing raise from a scratch copy) proved
  the scanner could fail. That turned "does the UI go stale?" from a lens's opinion into a measured
  zero — and it is reusable verbatim for U3/U5, which are also ViewModel-heavy.
- **An absence needs its conditions recorded, in an audit finding as much as in a measurement.** The
  `aot-trim` lens's clean result is only meaningful stated as *"no `Activator`/`MakeGeneric`/
  `GetCustomAttribute`/reflective `Invoke` in these three files"* — which is why it is written that
  way in §3 rather than as "AOT is fine".
- **A clean empty result and a total wipeout are the same shape — check `<failures>` every time.**
  D4b's first launch lost all five finders to `API Error: 529 Overloaded` at **0 tokens and 0 tool
  calls each**, and the workflow still returned `{"confirmed": [], "refuted": [], "note": "no
  findings"}` with no error. Read literally that says *this code is clean*. It is the same trap as
  the dead-skeptic fallback one rung earlier in the pipeline, and a 529 is transient — the identical
  script re-run 2 minutes later produced 18 claims. **Never record a segment's result without
  reading the failure block and the per-agent token counts.**
- **Verify a measurable claim against the artifact, not the source.** D4b/PX1 was rated LOW on an
  argument about reachability; two `dumpbin /EXPORTS` runs (the real System32 DLL vs our shipped
  `dist\proxy\dinput8.dll`) turned the mechanism from arguable into measured and moved it back to
  MEDIUM. Where a finding predicts something observable in a built binary, a log or a file, go look.
