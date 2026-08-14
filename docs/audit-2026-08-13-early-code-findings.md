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
| **D4** | DLL core runtime | `Macht` 708/755, `Mimic` 639, `Flamme` 410, `Sein` 342, `Stark` 302, `Lugner`(+Dinput8) 225, `Scharf.h` | ~2,626 | **Macht: none** |
| **D5** | Fern + Frieren | `Fern.cpp` 2490/6028, `Frieren.cpp` 816/1784 | ~3,306 | partly audited before |
| **U1** | LiveWalker / Pointer / ObjectTree VMs | 2900 + 999 + 360 | ~4,259 | good |
| **U2** | Export services | CeXml 1699, Csx 678, Sdk 569, Usmap 397, Symbol 215 | ~3,558 | mixed |
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

*(populated one segment at a time)*

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
| **G6** | LOW | `Serie.cpp:142` | A tag whose key lookup misses is cached as a **permanent** `TAGKEY_MISS`, contradicting `Genau.cpp:1672`'s documented "absent tag = plaintext" rule. A transient miss (the 4 unsynchronized reads in `LookupTagKey` racing a live insert) blanks every FName in every block with that tag for the rest of the process. | S / low |

-----

## 3. Refuted — do not re-raise

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
