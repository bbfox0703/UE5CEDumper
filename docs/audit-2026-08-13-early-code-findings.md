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
| **U3** ✅ | Dump services + MainWindow VM | 1517 + 437 + 1366 | ~3,320 | good |
| **U4** ✅ | Dialogs + CE script generators | InvokeParamDialog 1020 (96%), Baked 503, Invoke 399, ObjInstancePicker 295, ParamBuffer 258, CheatTable 245, FreezeDialog 242, FreezeGen 210 | ~3,172 | mixed |
| **U5** ✅ | Remaining VMs / Models / Core / scoring | Console 445, InstanceFinder 405, InterestingProps 380, PropertySearch 356, InterestingFuncs 345, LiveFieldValue 478, Logging 362, scoring tables ~800 | ~3,500 | mixed |
| **S1** ✅ | Early Lua scripts | `ue5_dissect` 531/555, `ue5_freeze_helper` 417/508, `ue5_invoke_helper` 288/605 | ~1,236 | none |
| **T1** | Tail sweep — **SPLIT into T1a–T1e, see below** | every remaining file ≥50 early lines + the never-touched-since-May files | ~9.4k | — |

### T1 is split into five phases (decided 2026-08-15, re-measured twice)

**Why split.** T1's own criterion — every remaining file with ≥50 early lines — is **9,390 early
lines over 53 files**. That is **1.7× the largest segment ever run** (D1/Ubel, ~5,665) and ~3× the
norm (~3,300). But size is the lesser reason. *Every prior segment was one subsystem*, so a lens
could hold it in view and cross-check siblings — which is how this audit's best findings were made
(`countsPartial` applied at some sites, L14 at 2 of 4, the detach line in 3 of 4 panels). T1 as one
unit hands each lens **54 unrelated files across the DLL, its headers, the C++ test suite, UI
ViewModels, Services, Models, Core and Views** — the exact condition that produced U5's failure mode
(finders pad, skeptic goes lenient), and U5 was only *10* files. Phases are grouped by **shared
contract, not by line count**, so the established method applies unchanged.

| Phase | Contents | Early |
|-------|----------|------:|
| **T1a** ✅ | DLL value-scan engine — `Radar.cpp` 696, `Radar.h` 354, `Methode.cpp` 269, `Heiter.cpp` 185 | 1,504 |
| **T1b** ✅ | DLL contracts + **the entire C++ test suite** — `Himmel.h` 594, `Grimoire.h` 153, `Renge.h` 142, `Utf8Helpers.h` 130, `Genau.h` 123, `Frieren.h` 105, `Fern.h` 61, `Lugner_Dinput8.cpp` 101, `dll_helpers_test.cpp` 741, `utf8_helpers_test.cpp` 244 | 2,394 |
| **T1c** ✅ | Remaining UI ViewModels **+ Core + Models** — `ValueSearch` 415, `ProxyDeployVM` 312, `GameClassFilter` 183, `ClassStruct` 162; `FieldValueConverter` 209, `IDumpService` 185, `AddressHelper` 143, `IAobMakerBridge` 77, `IProxyDeployService` 52; + 13 model DTOs | 2,889 |
| **T1d** ✅ | UI Services — `ProxyDeploy` 399, `AobMakerBridge` 217, `AobUsage` 209, `ClassLocationScorer` 203, `PipeClient` 198, `VdfParser` 153, `StructReturnDecoder` 149, `KnownStructLayouts` 130, `KeywordTokenizer` 117, `WindowsPlatformService` 96, `HelperLuaResource` 54, `FreezeHelperLuaResource` 50 | 1,975 |
| **T1e** ✅ | Views code-behind + app root + **the <50 tail** (226 files, 1,172 lines — swept by targeted pattern-grep, NOT 5 deep lenses; a deep read of 226 five-line files is waste) | 1,800 |

> **Why five and not six, and why the VMs merged.** Two corrections, both from re-measuring rather
> than re-reasoning:
> 1. **`PointerPanelViewModel.cs` (999 early) is already covered by U1** — U1's row reads
>    "LiveWalker / Pointer / ObjectTree, 2900 + 999 + 360", and the 999 *is* this file. The first
>    T1 sizing put it in the remainder because it checked a filename (`PointerViewModel.cs`) that
>    **does not exist in the tree**. T1 drops 10,389→**9,390** and the VM phase drops 2,071→1,072.
>    Validating that every name in a "covered" list actually resolves to a file costs one `os.path`
>    loop and would have caught it immediately.
> 2. **Per-phase cost is roughly FLAT, not proportional to lines.** S1 is the datapoint: 1,236 early
>    lines cost ~25% of a quota window — about what the 3,500-line U5 cost — because the price is the
>    fixed 5-lens + refute + second-lens structure, not the reading. So a 1,072-line phase costs
>    nearly as much as a 2,900-line one, and splitting finely *wastes* budget. The 1,072-line VM
>    group was therefore merged with Core + Models.

**T1b is the one to prioritise if the budget runs out.** §0's entire thesis is that the C++ suite is
two header-only test files that compile **no `.cpp` at all**. Auditing *those tests* — what they
assert, and what they structurally cannot catch — belongs beside the headers defining the contracts
they claim to test.

> **Scope decision, recorded because it is worth 15k lines.** The **UI test project is OUT of scope**
> and stays out. §0's area table has no `ui-tests` row, and that is not an oversight — reproducing
> §0's published per-area numbers *requires* excluding both `ui/UE5DumpUI.Tests` and `.axaml`
> (verified 2026-08-15: the reproduction lands on `scripts` 1,236 and `ui/Core` 806 **exactly**, and
> within 2% everywhere else). Excluding it leaves **15,541 early lines over 142 test files**
> unaudited. T1 pulls in only the test files caught by the "+ never-touched-since-May" clause. If the
> maintainer ever wants the rest, it is a **separate T2**, not a T1 phase — adding it would more than
> double T1.

> **Re-measurement note (2026-08-15, build 2904).** §0's "~10k" for T1 is accurate *on §0's scope
> rule*. A first re-measurement said 30,231 and was **wrong** — it had swept in tests and `.axaml`.
> The lesson is §0's own: a number recorded without its conditions is not a measurement, and the
> condition here is the scope predicate. Current totals on §0's rule: **49,612 early / 138,186 total
> over 331 files** (§0 published 48,950 / 131,522 / 321 at build 2804 — the tree grew ~100 builds).
> Covered by D1–D5 + U1–U5 + S1: **39,050 early over 51 files**. T1 remainder: **10,562 over 279
> files**, of which 9,390 clear the ≥50 bar and 1,172 are the sub-50 tail. (Corrected from an earlier
> 11,561/10,389 that wrongly counted `PointerPanelViewModel.cs` as unscanned — see the note under the
> phase table.)
> The never-touched-since-May list is now **46 files** (§0 recorded 53) — this audit's own fixes
> touched seven of them.

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
| **U1** ✅ | MED | `Ubel.cpp:4079`,`4103`,`4127`,`4155`,`4342`,`4374` (+UProperty twins `4212`,`4253`,`4277`,`4417`,`4446`) | Map/Set element sizes are read raw from `FPROPERTY_ELEMSIZE` and gated only on `> 0`, then used as a **per-element `std::vector` size** *and* as the TSparseArray stride. Every `ArrayProperty` path routes the identical read through `ValidateArrayElemSize` (cap 65536) precisely because this read is documented to return garbage. | S / low |
| **U2** ✅ | MED | `Ubel.cpp:1373` + `1403` | `InferScalarSize` returns a hardcoded `8` for `NameProperty`, and `ValidateArrayElemSize` treats that as authoritative and **overrides the engine's correct 16** on `bCasePreservingName` games. | S / med |
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
| **A2** ✅ | MED | `Macht.h:302` *(D4 scope)* | `IsSparseIndexAllocated` judges slots 0..127 from the **stale inline bit words** once a `TSet`/`TMap` has spilled its `TBitArray` to the heap — affecting **13 Aura call sites + 6 in Ubel**, while `Aura::ResolveTMapBitArrayBase` gets the same rule right. A freed slot reads as live, so Find Refs can emit a phantom reference and `ScanForValue` admits a dead element's value. | S / low |
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

| **M1** ✅ | MED | `Macht.h:314` | `ComputeSetElementStride` aligns to **4** and cannot express `alignof(T)`, so the documented `TMap` recipe omits the `TPair`'s **trailing padding**. Real stride is `Align(sizeof(TTuple<K,V>) + 8, max(alignof K, alignof V))`. `TMap<AActor*,float>` → computes **20**, real **24**; `TMap<FString,int32>` → **28** vs **32**; `TMap<UObject*,uint8>` → **20** vs **24**. Every element past index 0 is read at a wrong address. `TSet<T>` is unaffected (a bare `elemSize` is always a multiple of `alignof(T)`). | S / med |
| **M2** ✅ | MED | `Macht.h:293` | `ReadTSparseArray` reads `NumFreeIndices` at **`+0x3C`**. The PDB-verified layout in this repo (`Aura.cpp:3037-3038`, Everspace 2 UE 5.4) puts `FirstFreeIndex` at `+0x30` and `NumFreeIndices` at **`+0x34`**; `+0x3C` is padding before the Hash allocator at `+0x40`, zero-initialised and never written. So it always reads **0**, and `Ubel.cpp:4045`'s `mapCount = MaxIndex - NumFreeIndices` **over-reports** the count of any `TMap`/`TSet` that has had entries removed (10 shown, 6 rows rendered). The header comment claiming `TSparseArray` is `0x40` bytes is also wrong — it is `0x38`. | S / low |
| **M3** ✅ | MED | `Macht.h:332` | `ComputeMapValueOffset`'s size-guess fallback is taken for **every struct-valued TMap**, because `Scharf::RequiredAlignment` returns **0** for `StructProperty` (`Scharf.h:76-78`). It then guesses `valueSize >= 8 → align 8`. For `TMap<int32,FVector>` the real value sits at **+4** (alignof 4) but it reads **+8**, so *even element 0* shows a wrong vector; `TMap<int32,FGuid>` reads every value 4 bytes late. **A validation helper is being reused as a layout oracle.** | M / med |

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
| **F1** ✅ ‡ | MED | `Fern.h:30` (+ `Frieren.cpp:92`) | `~Fern() { Stop(); }` on a namespace-scope static makes the **entire pipe teardown** — cancel sweeps, watch joins, a **5-second** condition-variable drain and two thread joins — run from the CRT's static-destructor pass during `DLL_PROCESS_DETACH`: precisely the heavy work `Heiter.cpp:288-301` deliberately refuses to do there, and precisely the hazard `Routine.h:51-56` already documents for every *other* module's worker. At process exit `ExitProcess` has already terminated the connection threads, so they can never erase themselves from `m_conns` and the drain predicate is **unsatisfiable by construction** — the full budget burns every time. `Stop` also takes `m_connMutex`, Sein's log mutex and both Radar session mutexes *after* their holders were killed, which is the documented shape of a permanent exit hang. | S / low |
| **F2** | MED | `Fern.cpp:854` (+ `:779`) | `Tot::ResetPerCommand()` runs **only** when a connection is accepted into an *empty* registry (`firstConn = m_conns.empty()`). The monitor latches `g_perCommand` when a pipe breaks, and nothing else ever clears it — so a reconnect that arrives while a dead session's connection is still registered (a bulk lane parked in `EnqueueInvoke`'s wait, up to the 600 s invoke timeout) carries the latch into the **new** session, permanently: the registry cannot go empty again until that session ends. Every cancellable loop then bails on its first poll and returns an empty or partial result **inside a successful response** — `find_instances` 0 rows, `search_properties` truncated, `begin_value_scan` empty. Second lens confirmed it and called it understated: `Stark.h:87-89` advertises the very guard that would have killed the claim, and it does not cover this path. | M / med |
| **FR1** ✅ | MED | `Frieren.cpp:767` | `UE5_AutoStart` **discards `UE5_Init`'s return** behind the comment `// Always succeeds (partial init is OK …)` and unconditionally starts the pipe server and publishes `initState = INIT_READY` — a state the enum defines as "init finished **and** the pipe server is up". `UE5_Init` has a real `return false` (a shutdown landing during the multi-second scan; pointers partial, nothing latched). End state: a serving pipe, `s_initialized == false`, engine pointers from an aborted scan, and **no mailbox poller** (`Mimic::StopThread` already ran and `StartThread`'s only other caller is `DllMain`) — so every CE `.CT` feature row writes a command nobody collects and `status` stays `0`, which CLAUDE.md's own rule tells the user means *"stale `g_invokeMailbox` address"*. Sends the user to the wrong diagnosis. Second lens proved the comment stale **by history**: `af7ff3a` (2026-03-02) deleted the old `if (!UE5_Init()) return false;` when it was correct; `ab66d54` (2026-08-04, audit #4 B49) added the `return false` five months later without revisiting the call site. | S / low |
| **F3** | MED | `Fern.cpp:1068` (+ `:1717`, `:4773`) | `Ubel`'s per-UObject name cache is keyed by a **raw `uintptr_t` with no generation/serial**, is never revalidated on hit, and Fern owns its only two purge sites — `begin_snapshot` and `trigger_scan`, neither reachable from ordinary browsing. The last-connection teardown at `:1068` drops Radar sessions, Linie, Sense and Schlacht but **not** this; nor does `Fern::Stop`, nor a CE Disable/Enable. After a level change recycles a UObject slot, every name-bearing response — Object Tree rows, `walk_instance`'s own/outer name, every ObjectProperty target — serves the **destroyed** object's name for the rest of the process's life, while the class is read fresh, so the two disagree with no error anywhere. Only restarting the game clears it. **Cluster ③, exactly.** | S / low |
| **F4** ✅ | MED | `Fern.cpp:2645` (+ batch twin) | `Aura::SearchProperties` stops the GObjects walk the instant `results.size()` reaches `maxResults` (**200** by default from the UI) and also breaks on `Tot::Requested()`; the reply carries `total` = the capped row count with **no `truncated` and no aborted flag**. Worse, `scanned_objects` is echoed from a field Aura assigns to the **full** object count *before* the loop starts (`Aura.cpp:4179`), so a walk that stopped a few percent in reports the whole pool as scanned. The panel prints *"Found 200 properties in 3,412 classes (scanned 1,204,338 objects)"*; the user's client-side filter then finds nothing and they conclude the field does not exist. **This is the exact shape behind four "the scan missed my field" reports.** | S / low |
| **F5** | LOW | `Renge.h:282` (+ `Fern::WriteLine`) | `MakeResponse` builds a 3-key envelope then `res.merge_patch(data)`; nlohmann's merge_patch assigns non-object values **by copy**, so a payload a handler carefully built with `std::move` is deep-copied in full — then `WriteLine` materialises a second copy of the serialized string solely to append `"\n"`. Peak ≈ 2× DOM + 2× string, **in the game process's heap**, on `snapshot_chunk` (8192 objects), `find_instances` (50000 cap) and `list_all_functions` (100000 default). | S / low |
| **F6** ✅ | LOW | `Fern.cpp:2510` | `walk_world` clamps the Actors loop to the caller's `limit` (the UI sends **500**) and reports `actors.size()` as the level's actor count — the real `actorArr.Count` is read one line earlier and **discarded** — with no `truncated`/`total`, so a 500-actor page is indistinguishable from a 500-actor level and an actor at index 1877 simply is not there. Two sibling failures in the same block (Actors field unresolved; `ReadTArray` fails) also return `actors: []` with `ok:true` and **no `error`**, even though this same handler sets `data["error"]` for the two failures directly above. | S / low |
| **F8** ¤ | MED | `Fern.cpp:2510` (same handler as F6) | **`ULevel` has no reflected `Actors` property on this engine, so `walk_world` enumerates nothing — always.** Found while verifying F6: the fix's new error string named the branch (`actorsOffset < 0`, not a failed read), and walking the live `ULevel` instance confirms it — **29 fields, 7 `ArrayProperty`, none called `Actors`** (`ModelComponents` @208, `NavDataChunks` @264, `StreamingTextures` @320, `DestroyedReplicatedStaticActors` @768; the only `*Actor*` names are `ActorCluster` and `LevelScriptActor`, both `ObjectProperty`). `walk_world`'s entire actor enumeration is built on finding that property by reflection, so on this engine Live Walker's "Load GWorld" shows a populated level as empty. `ULevel::Actors` is a real native member — the fix is a native-offset read (the `Ubel::GuessGapTypes` machinery already exists for exactly this), not more reflection. **CONFIRMED AT THE SOURCE.** `vendor/UnrealEngine/Engine/Source/Runtime/Engine/Classes/Engine/Level.h:428-429` declares `TArray<TObjectPtr<AActor>> Actors;` with **no `UPROPERTY()`** — a plain C++ member the engine never reflects. Our walker is not dropping it: `DestroyedReplicatedStaticActors` (`Level.h:887`), which IS a `UPROPERTY`, appears in the field list we read. So `walk_world`'s reflection-based lookup **cannot ever have worked**, on any engine matching this source, and the feature has been silently returning an empty world since it was written. The alternative hypothesis — "our class walker is truncating the field list", which would have been far worse — is dead. **Reproduced on a second, unrelated title.** Solarpunk (commercial, different engine build) in the 2026-08-14 session: `TX {"cmd":"walk_world","limit":500,...}` → `RX {"actor_count":0,"actors":[],"ok":true,"world_name":"MainLevel",...}`. **2 of 2 games tested return nothing**, so this is not a DumperTest artifact and may not be version-specific at all. *Honest limit: two captures is not a survey — what is established is that it fails on both engines we have evidence for, not the size of the affected range.* | M / med |
| **F7** ✅ | LOW | `Fern.cpp:5042` | The `-7` response text reads *"(hook not active, direct call used)"*. `-7` is produced **only** by `Stark::EnqueueInvoke`'s inactive-hook guard or by `Stark::Shutdown` draining the queue — **neither executes ProcessEvent by any route**; the direct fallback lives on the other side of the `if (Stark::IsHookActive())` branch and returns 0/-2/-3/-4/-8. The string reports an execution that provably did not happen, and the dialog still shows `result_hex` — the untouched pre-call buffer. `-8` has no mapping at all. | S / low |

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

> ### ✅ W6 FIXED — build 2857, 2026-08-14
>
> **The fix is the signature, not the branch.** `MapInnerTypeToCeField` now *requires* the element
> size and applies the enum rule itself, so all five call sites get it by construction. Previously
> each site had to remember a caller-side ternary — and three of five did (struct sub-fields, map
> key, map value) while the TArray and TSet element paths did not.
>
> The rule was not merely known, it was **written down** at the site that got it right: *"a 1-byte
> enum must NOT be read as 4 bytes — that pulls in the next field's bytes."* This is cluster ④'s
> partial-application variant in its clearest form, and the reason the repair is a required parameter
> rather than a fourth copy of the ternary is that a sixth call site would otherwise forget it too.
>
> Deliberately **not** size-driven, and documented as such so a later reader does not "fix" them:
> `NameProperty` stays 4 bytes because the record shows the FName ComparisonIndex paired with a
> DropDownList of names, not the whole 8/16-byte FName; the pointer flavours stay 8 regardless of
> stride.
>
> **Negative control:** reverting the width to a hardcoded `"4 Bytes"` fails 3 tests — the two new
> byte-wide array/set tests **and a pre-existing struct-sub-field test**, which is the useful part:
> it confirms the refactor preserved the behaviour that test was already pinning rather than merely
> satisfying new assertions. 3590 tests, 0 failed.

> ### ✅ W1 and W7 FIXED — build 2853, 2026-08-14
>
> W7 came along because **W1's deliverable does not work without it**: a name index past the end of
> the table corrupts the same file the version fix was meant to make openable.
>
> **The version now describes the bytes.** The writer emits **v4 (ExplicitEnumValues)** — the version
> both vendored canonical writers produce — and every threshold it crosses is honoured: the
> mandatory `int32 bHasVersionInfo` after the version byte, `uint16` enum member counts, and each
> enum member's `int64` explicit value. v4 was chosen over "the cheapest v2" because the writer
> already emitted `uint16` name lengths (so it can never go below 2), because `uint16` counts remove
> a **silent truncation of any enum past 255 members**, and because explicit values stop an enum with
> gaps being flattened to `0..N-1`. **The CEXT extensions block is not written**, which is safe:
> Dumper-7 emits v4 without one.
>
> **Two more desyncs behind the header, both fixed.** ArrayDim is one byte carrying the real
> dimension (it was a hardcoded 2-byte `0`, which both slid the stream and told a reader to register
> no schema slots). And a struct's two counts are genuinely different numbers — the first is the sum
> of every property's ArrayDim, the second is how many records follow; both were `Fields.Count`.
>
> **W7 is fixed by construction, not by care.** `"None"` is pre-registered, the write pass may only
> use a non-appending `IndexOf`, and `NameTable.Seal()` makes any post-serialisation `GetOrAdd`
> throw. The invariant is now enforced by the type rather than remembered by the caller.
>
> **The real deliverable is the round-trip reader.** `UsmapFile.Parse` in the tests parses the file
> the way a consumer does, at the canonical widths, and asserts **the stream is fully consumed** and
> every name index is in range. That is what the old tests could not do: all five skipped a hardcoded
> 12-byte header and read fields at the widths the *writer* happened to use, so they encoded the bug
> instead of checking it. Five were rewritten onto the reader and four added (full round-trip over
> every container shape, static-array slot counting, a 300-member enum, and an unregistered struct
> name).
>
> **Negative controls, one per sub-fix, each reverted alone:** removing `bHasVersionInfo` fails 9
> tests; restoring the `uint8` enum count fails 3; restoring the 2-byte ArrayDim fails 4; un-
> registering `"None"` fails 1. The reader is therefore checking the canonical layout, not mirroring
> the writer. 3587 tests, 0 failed.
>
> ⬜ **Not verified against a real consumer.** The round-trip proves self-consistency at the widths
> the vendored writers define; nobody has yet opened the output in FModel. Filed in
> [todo.md](todo.md#pending-live-game-verification-verify-only--no-code).

> ### ✅ W2 and W3 FIXED — build 2842, 2026-08-14
>
> One commit: both live in the same emitter and the same layout cursor, and W3's byte accounting is
> only observable once W2 stops flooding the struct with inherited members.
>
> **W2 — the boundary is now sent, not guessed.** `walk_class` gained `super_props_size` (the
> immediate super's `PropertiesSize`, read in `Ubel::WalkClass` where `SuperClass` is already
> resolved), because nothing else in the reply implies it: the DLL prepends the **entire** SuperStruct
> chain to `Fields`, so "own vs inherited" is underdetermined client-side. Same shape as V2's
> `map_stride` — *where the input is an engine fact the wire does not carry, send the number*. The old
> first-field heuristic survives only as a fallback for an older DLL, and is documented as a fallback
> because it mis-splits silently when a derived class adds no properties of its own.
>
> **W3 — packed bools become real bitfields at their true bit positions.** N `uint8 bX:1` flags
> sharing one byte arrive at the same offset with Size 1; emitting a whole `bool` each grew the struct
> by N−1 bytes, and padding could never compensate because it is only emitted when the next offset is
> *ahead* of the cursor. They are now emitted as `uint8_t Name : 1` with **unnamed fillers for the
> bits UE left unused**, so bit N in the game is bit N in the header. A native `bool` (mask `0xFF`)
> and an unresolved mask (`0`) both keep their whole byte — an unknown must never rewrite the layout.
>
> **Both duplicated loops are gone.** The schema and live emitters carried byte-identical member-emit
> loops and therefore both defects; they now project into one `SdkField` record and share
> `EmitStructBody`. Fixing this in two places was how it would have drifted again.
>
> **One pre-existing test was CHANGED, which deserves the scrutiny.**
> `GenerateClassHeader_BoolBitfield_EmitsComment` asserted `bool bHidden;` for a field with
> `BoolFieldMask = 0x04` — a single-bit mask, i.e. exactly the packed bitfield W3 is about. Its name
> and its second assertion show it was written for the *mask comment*; the declaration form was
> incidental and pinned the defect. The mask assertion is unchanged and the declaration assertion now
> expects the bitfield. The arithmetic that justifies it: with **one** bool the struct size is the
> same either way, but the bit position was wrong (bit 0, not bit 2) — and with several bools the
> size itself breaks, which the new eight-bool test demonstrates.
>
> **Negative controls, run separately so each fix is independently guarded:** reverting only the
> bitfield grouping turns exactly the two W3 tests red; reverting only the inherited-field filter
> turns exactly the one W2 test red. Restored, 3583 tests, 0 failed.
>
> ⬜ **Not verified in-game.** The unit tests drive the real emitters end-to-end, but the *boundary
> value* now comes from the DLL, and no headless check confirms a real `super_props_size` on a live
> class — see [todo.md](todo.md#pending-live-game-verification-verify-only--no-code).

> ### ✅ W4 FIXED — build 2836, 2026-08-14
>
> Escaping moved into `BuildDropDownContent`, which is the **single choke point**: all six call sites
> (including the cached `_dropDownOwners` link path) build their body there, and both
> `<DropDownList>` emit sites interpolate that body. One change covers every path.
>
> **The fix has two halves, and the second is the one a well-formedness check would have missed.**
> XML metacharacters go through the same `EscapeXmlContent` the Descriptions use. But the body is
> also **line-delimited**, so a CR/LF inside a game string forges an extra dropdown row and shifts
> every following one — without making the document malformed. `CollapseLineBreaks` flattens them.
> The negative control proved the distinction: reverting the fix turned all four new tests red, and
> the newline test was the only one that failed **without** an `XmlException`.
>
> **The regression tests live in `CeXmlEscapingTests`, deliberately.** That suite is where the gap
> was: all five pre-existing tests put the game string in a map **key**, which lands in
> `<Description>`, so the suite passed throughout while the `<DropDownList>` path interpolated raw.
> The four new tests reach that path via a `TMap<int32,FName>` — `NameProperty` map values are routed
> into a dropdown rather than a description. 3579 tests, 0 failed.
>
> Still open in the same family: **W6** (`CeWidthForSize` bypassed by the enum array/set path) is the
> other partial-application defect in this emitter and is untouched.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **W1** ✅ | **HIGH** | `UsmapExportService.cs:16`,`173-178`,`203`,`233` | The file stamps `Version = 3` (`LargeEnums`) but writes the **version-0 layout**, in three independent places. (a) `version` is followed straight by `compression` — the mandatory `int32 bHasVersionInfo` that every `version >= PackageVersioning(1)` file must carry is missing, so a reader's `ReadBoolean()` consumes 4 bytes of the size field and throws before a single name is read. (b) enum member count written `uint8`, but `>= LargeEnums` requires `uint16`. (c) per-property ArrayDim written as `(ushort)0` while the format (and the code's own adjacent comment) says `uint8` — so every struct's first property slides the stream by one byte. The file's own comment is also wrong: it says "3 = LongFName", but LongFName is **2**. | S / low |
| **W2** ✅ | **HIGH** | `SdkExportService.cs:358` (+`EmitClassHeaderFromLive`) | The emitter assumes `ClassInfoModel.Fields` holds only the class's **own** properties, but `Ubel::WalkClass` deliberately prepends the entire SuperStruct chain and nothing filters it out. Every base-class property is therefore declared a **second time** inside a `struct X : public Super` that already inherits it, so `offsetof` is wrong for every derived class in the generated SDK — the header compiles and is silently mislaid out. | M / med |
| **W3** ✅ | MED | `SdkExportService.cs:221` + `:382` | N `FBoolProperty` bitfields that UE packed into **one byte** are each emitted as a whole `bool` and the layout cursor advances by `Size` for each, so the struct grows N−1 bytes from that point. The emitter only pads when `offset > cursor`, so once the bools overshoot, **no padding can compensate** and every subsequent member — and the trailing `// Size:` — is shifted. | M / med |
| **W4** ✅ | MED | `CeXmlExportService.cs:3522` + `:3586` (`BuildDropDownContent`) | `<DropDownList>` bodies are built from live FName / enum-name strings read out of game memory and interpolated **raw**, while every `<Description>` goes through `EscapeXmlContent`. One `&` in a `TArray<FName> Tags` entry (`Bow & Arrow`) makes the whole CheatTable malformed — and `EscapeXmlContent`'s own doc comment states the consequence: CE rejects the **entire document**, so a multi-thousand-entry export imports as nothing with no indication which record was at fault. This is audit #4's **B3 defect surviving at the one site B3 did not cover**. | S / low |
| **W5** | MED | `CeXmlExportService.cs:2141` (`EmitDrilledPointer`) | The scalar pointer-drill branch gates on the broad `IsObjectPropertyType`, which includes `WeakObjectProperty` / `SoftObjectProperty` / `SoftClassProperty` / `LazyObjectProperty`, and emits `Offsets=[0]` for slots whose first 8 bytes are **not** a `UObject*` — a weak pointer is an index+serial pair. The array path has an explicit regression test against exactly this. (Claimed HIGH, cut to MEDIUM by the second lens.) | S / low |
| **W6** ✅ | MED | `CeXmlExportService.cs:2665` (`MapInnerTypeToCeField`) | `EnumProperty` is hardcoded to CE type `"4 Bytes"` while element **addresses** are laid out with the DLL's real `ArrayElemSize`/`SetElemSize` (1 for the standard `enum class : uint8`), so CE reads 4 bytes at every 1-byte-spaced element. `CeWidthForSize` exists to fix precisely this and the Map and struct-array emitters already route through it. | S / low |
| **W7** ✅ | MED | `UsmapExportService.cs:323` | The name table is serialized first, from a snapshot of the pre-registration pass, but the struct-writing pass then calls `GetOrAdd(… : "None")` in four places. `"None"` is never pre-registered, so it is appended at index N **after** the file already declared exactly N names, and every affected property references an out-of-range index. | S / low |
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

### U3 — Dump services + MainWindow VM — ✅ scanned 2026-08-15

**11 agents** (5 lenses → 4 refute batches → 2 second-lens), 0 errors. Run `wf_bf7170d4-f18`.
19 raw claims → 18 after dedupe → **6 refuted (33%)** → 12 confirmed → **11 distinct** after merging
the two reports of the same Dump-All completion line (`:3373` — "success derived from the file's byte
length" and "the MB figure uses integer division" are one statement), **+1 found by hand** = **12**.

**Tally: 3 MEDIUM · 9 LOW · 0 HIGH.** The finders claimed **no HIGHs at all** — the first C# segment
where that happened, and it matches what is left: this segment's defects are missing caveats and
unread flags, not wrong writes.

> **The headline is that this audit's OWN fix was applied to one of its two sites.** X1 is the D5/F4
> truncation fix: the DLL side covered **both** the single and the batch property-search paths — the
> comment at `Fern.cpp:2822-2825` cites *"audit #5 D5/F4"* by name — while the C# side was applied to
> the single path only. The two parsers sit ~80 lines apart in the same file. That is the **third**
> recurrence of the same meta-pattern in three segments: V2 (fixed one side of the wire), W4/W6
> (applied at some call sites), X1 (applied at one of two twins). **A fix in this codebase is not
> done until you have grepped for its siblings** — across languages, across call sites, and across
> single/batch twins.

> ### ✅ X1 FIXED — build 2870, 2026-08-15
>
> **This was the D5/F4 fix reaching its second site.** The DLL had emitted per-query `truncated` and
> batch `aborted` since F4 — the comment at `Fern.cpp:2822-2825` names *"audit #5 D5/F4"* — and the
> C# read them on the single-query path only. The two parsers sit ~80 lines apart in the same file.
>
> `PropertySearchQueryEnvelope` gained `Truncated`, `PropertySearchBatchResult` gained `Aborted` and
> a `TruncatedQueries` convenience, and both discovery panels now say so:
> **Interesting Properties** appends *"⚠ N of 51 keywords STOPPED at the 200-row cap (Max, Count,
> Time, …) — more matches exist"*, and **Detect Player Stats** — which makes the same call at the
> same limit — appends its own shorter form. Both mirror the wording the single-query path has used
> since F4.
>
> **A test hook came out of it, and that is the durable part.** The batch parse was inlined in an
> `async` method that needs a live pipe, so *nothing could reach it* — which is precisely why a
> missing field survived two builds. It is now
> `internal static ParseSearchPropertiesBatch(JsonObject)` with a string-taking test hook, so the
> wire contract is testable without a pipe.
>
> **Negative control:** removing the two parse lines turns the two flag tests red. The third test —
> an older DLL that omits both keys — passes in **both** directions, which is correct and is the
> point of having it: the fix must not start warning on every scan against a pre-2818 DLL.
> 3613 tests, 0 failed.
>
> ⬜ **Not verified in-game.** Nobody has yet run a real batch scan on a title where a seed keyword
> caps and confirmed the strip appears.

> ✅ **X2 fixed in build 2888** — `d2c34d1`-era; the handoffs stopped re-deriving an address they
> were already holding.
>
> **The lookup should never have existed.** Interesting Funcs and Console build their rows from
> `list_all_functions`, which supplies `class_addr` **per row** — and then the three handlers threw
> that away, called `list_classes`, searched the returned page by NAME, and aborted on
> *"Class X not found"* about the class whose own row the user had just clicked. The three events now
> carry `classAddr`, so the common path issues **no pipe call at all**.
>
> **Two twins came with it, and they are the reason this is not a one-line change.**
> `Console.RequestParameterInvoke` (the FIRE dialog for any exec taking parameters) and
> `Console.RequestCopyBakedScript` are byte-for-byte the same body as the cited site, with the same
> `"Class {className} not found"` and the same hard `return`. This is the fourth time this audit has
> found its own defect shape half-covered; the §3b rule caught it this time.
>
> **The cap is now detected at the source, per D5/F4's own lesson.** `Aura::ClassListResult` gained
> `truncated` (set the same way as `SearchResultSet::truncated`) and `list_classes` emits it. The C#
> falls back to inferring it from a full page so a **pre-2888 DLL still gets the honest message**
> rather than silently degrading to "not found".
>
> **What a miss now says.** `ClassListResult.FindClassAddr` is a pure, unit-testable lookup returning
> `ClassAddrLookup`, whose `MissReason` is *"not in the class list — it was CAPPED at 5,000 rows, so
> the class may still exist"* on a truncated walk and plainly *"not found"* on a complete one. The
> four **refuted-blast-radius** sites (`:1191` and siblings) were left behaving exactly as before but
> re-pointed at that helper — their old text asserted *"Find Instances + ListClasses both empty"*,
> which is a false statement about a capped list. **Game Class Filter** appends
> *"⚠ STOPPED at the 5,000-row cap — more classes exist"*, which matters because its
> `total_classes` moves in lockstep with the results vector, so on a truncated walk the line printed
> the cap twice and read as a pool size.
>
> **Negative controls, three, each isolating one claim:** forcing `Truncated = false` in the parser
> reds exactly the 2 truncation tests (the "full walk is not truncated" test correctly stays green —
> it asserts an absence); passing `""` instead of `row.ClassAddr` reds exactly the 3 address-carrying
> tests; flattening `MissReason` to `"not found"` reds exactly the capped-wording test. 3631 tests,
> 0 failed (3624 → 3631). `dist` is the 54.4 MB AOT-trimmed binary, launch-verified, no `crash.log`.
>
> ⬜ **Not verified in-game.** Nobody has yet clicked AA(B) on a class past the cap in a real title
> and watched the script generate. The pure logic is covered; the end-to-end path is not.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **X1** ✅ | MED | `DumpService.cs:1746` (`SearchPropertiesBatchAsync`) | The batch property search parses `query` + `match_count` only, dropping the per-query `truncated` and batch `aborted` the DLL emits for exactly this purpose — `PropertySearchQueryEnvelope` has no such field to parse into. Interesting Properties sends all 51 seed queries at 200 rows each, so common seeds (`Max`, `Count`, `Time`, `Level`, `Hit`) cap routinely and the panel reports *"N unique properties"* with no cap note. **This is the exact report class the F4 fix was written to end**, surviving at F4's other site. `DetectStatsViewModel` makes the same call with the same blind spot. | S / low |
| **X2** ✅ | MED | `MainWindowViewModel.cs:1650` | Class-address lookups scan a single 5,000-row `list_classes` page and report a real class as **"not found"** when it falls past the cap. | S / low |
| **X3** | MED | *(hand-found)* `DumpService.cs` / `Models/EngineState.cs` | The DLL computes and publishes a three-flag offset-validation verdict (`validated`, `probe_ran`, `case_preserving`, plus `fallback_reason`) and **the UI never issues `get_offsets` at all**, so nothing can tell the user the walker is running on unvalidated default offsets. | S / low |
| **X4** | LOW | `MainWindowViewModel.cs:3373` | The Dump All completion line is derived from the **file's byte length** rather than from what the dump did, so a zero-class or half-failed dump still announces an export — and the figure itself is integer division (`Length / 1024 / 1024:F1`), so a 3.7 MB dump prints `3.0 MB` and anything under 1 MB prints `0.0 MB`. | S / low |
| **X5** | LOW | `MainWindowViewModel.cs:2003` | The disconnect fan-out resets **3 of 15** panels; every other panel keeps its rows across a reconnect and goes on offering jumps to addresses from the previous process. | M / med |
| **X6** | LOW | `MainWindowViewModel.cs:3336` | The three long-running export commands take no `CancellationToken`, so every `ct` check inside the services they drive is dead code. | M / low |
| **X7** | LOW | `MainWindowViewModel.cs:2026` | The "game thread paused" banner is a level rebuilt from two independent per-lane edge latches, so it sticks ON after the game resumes. | S / low |
| **X8** | LOW | `MainWindowViewModel.cs:1915` | The Console tab's two AOBMaker actions assert *"AOBMaker not connected"* from a cached flag nothing on their path refreshes. | S / low |
| **X9** | LOW | `MainWindowViewModel.cs:132` | The competing-dumper-host banner lists the game you are connected to among the "also loaded" competitors when the DLL reports no PID. | S / low |
| **X10** | LOW | `MainWindowViewModel.cs:2315` | Time-dilation values are in `ApplyOptions` and `BuildOptions` but missing from `TeleportPersist`, so changing them never schedules a save. | S / low |
| **X11** | LOW | `MainWindowViewModel.cs:3368` | Dump All streams onto the destination with `FileMode.Create` and no temp-then-rename, so an abort leaves a truncated `.jsonl` at the final name, and no consumer checks the trailing summary line that is written only on success. **Scope corrected by hand — see below.** | S / low |
| **X12** | LOW | `MainWindowViewModel.cs:3145` | Install-CE-autorun skips its own manual-placement fallback in exactly the case where the write was denied. | S / low |

**Verified independently (not agent-reported):**

1. **X3 was found by hand, by a sweep the agents did not run — and the sweep's blind spot is worth
   recording.** Diffing every JSON key the DLL *writes* (`X["key"] =` in `dll/src/*.cpp`) against
   every key the C# *reads* (`["key"]` in `ui/UE5DumpUI/**/*.cs`) gives 508 written / 544 read and
   **38 never read**. Three sit together: `validated`, `probe_ran`, `case_preserving`
   (`Fern.cpp:4644-4648`). No C# file mentions any of them, and **the UI never sends `get_offsets`**;
   `EngineState` has no equivalent (its `IsLowConfidence` is about UE *version* detection). The DLL's
   own comment says why the split exists — *"A user reading the honest-looking summary had no way to
   know the walker was about to be useless"* (`Grimoire.h:238-241`) — and **nothing gates serving on
   the flag**: only `FindGEngineSlot` reads it, deliberately using the ProbeRan-ish semantics for
   ordering. So a user can browse a game whose offsets are all guesses with no indication, on the one
   panel that already carries a low-confidence badge, a version-too-old banner and per-pointer
   warnings. `fallback_reason` — the *why* — is unread too.
   **Negative control:** deleting the known-good `map_stride` read from a scratch copy made the sweep
   flag it, so the 38 is a measurement.
   ⚠ **The sweep is key-name-GLOBAL, so it structurally cannot find X1**: `truncated`/`aborted` *are*
   read on the single-query path, which marks them "read" everywhere. The agents found the defect the
   detector was blind to, and the detector found the one they missed. Neither subsumes the other —
   **run both.**
2. **X1 re-derived at both ends.** `Fern.cpp:2822-2825` emits `envelope["truncated"]` per query under
   a comment naming *audit #5 D5/F4*; `Fern.cpp:2834` emits `data["aborted"]`;
   `Models/PropertySearchResult.cs:179-184` shows `PropertySearchQueryEnvelope` with only
   `Query`/`MatchCount`/`Results`, while the single-query `PropertySearchResult` in the same file has
   both flags *with the F4 doc comment on them*.
3. **X11's scope was corrected by hand after a split verdict — and the refutation was right on the
   part that matters.** A near-identical claim against `DumpAllService.cs:463` was **refuted** while
   this one was confirmed. The code settles it: the non-atomic write is real (`FileMode.Create`
   straight to `filePath`, `MainWindowViewModel.cs:3368`), **but
   `DumpExplorer.SetLastExportPath(filePath)` is inside the `try`, after the success line**, so on
   failure the app never advertises the partial file — the `catch` sets *"Dump failed"* and stops.
   The harm is therefore "a truncated file sits at the name the user chose, with no marker check if
   they open it manually", not "the app hands the user a corrupt dump". Filed at LOW with that scope.
   **Do not re-raise the larger version.**

-----

### U4 — Dialogs + CE script generators — ✅ scanned 2026-08-15

**18 agents** (5 lenses → 3 refute batches → 10 second-lens), 0 errors. Run `wf_be335f2c-7b7`.
20 raw claims → 15 after dedupe → **0 refuted** → 15 confirmed → **14 distinct** after merging the two
lens-duplicate reports of the same `EnumProperty` defect (`ParamBufferBuilder.cs:221`/`:224`).

**Tally: 1 HIGH · 8 MEDIUM · 5 LOW.** Four HIGHs were claimed; one survived and three were cut to
MEDIUM.

> ⚠ **A 0% refutation rate is an outlier and must not be read as a quality signal.** Every other
> segment killed 25–73%. Two readings are available and they are not exclusive: this is the densest
> early code in the tree (`InvokeParamDialog` is **96% early**, and the whole segment emits code that
> runs against a live game), or the skeptics underperformed. **Because the rate could not be used as a
> check here, three findings were re-derived by hand** — the HIGH, the merged enum pair, and V-lens
> item Y10 — and all three held, which is why the tally is recorded as-is. Treat the five LOWs as the
> least-scrutinised rows in this audit.

> **The HIGH is a capability that has never worked, for the third time in this audit.** Y1 makes the
> generated CE invoke form pass **0** for every `UObject*` / `FName` parameter the user actually fills
> in — joining W1 (a `.usmap` that never parsed) and V1 (a map edit that wrote to the key). The
> pattern across all three: *the default value happens to work*, so any smoke test passes.

> ### ✅ Y2, Y3, Y4 and Y5 FIXED — build 2866, 2026-08-15
>
> One commit, because they are one defect wearing four hats: **`ParamBufferBuilder` (the FIRE path)
> re-implemented, more weakly, the parsing `BakedScriptGenerator` (Copy AA Script) already did
> correctly.** The same dialog therefore produced two different calls into a live game depending on
> which button the user pressed.
>
> | | typed | FIRE sent | exported script sent |
> |---|---|---|---|
> | **Y2** | `3` into a 1-byte enum | **nothing at all** (game got 0) | 3 |
> | **Y3** | `true` | **0** | 1 |
> | **Y4** | `1,5` | **15.0** | refused |
> | **Y5** | `-1` into an `Int8` | **0** | −1 |
>
> Y2 is the sharpest: `EnumProperty` was grouped with `IntProperty` and gated on
> `available >= 4`, so a 1-byte `enum class : uint8` param failed the guard and **was never written**.
>
> **The fix is to share, not to re-copy.** `BakedScriptGenerator.ParseBoolLiteral` and
> `TryParseHexOrDecimal` became `internal` and the FIRE path now calls them; `EnumProperty` routes
> through a new `WriteBySize` that the size-driven fallback also uses, so those two cannot drift
> apart either. Writing a fifth parser was the obvious move and the wrong one — this audit has now
> found its own fixes half-applied three times (V2, W4/W6, X1), and *sharing the implementation* is
> the only repair that does not depend on a future editor remembering.
>
> Y4's guard is copied verbatim in intent from the sibling's own comment: `TryParse(text,
> IFormatProvider, out _)` defaults to `NumberStyles.Float | AllowThousands` **and accepts `NaN` /
> `Infinity`**, so those reached the game as real floats. The baked path had documented this (as
> B23) and guarded it; the FIRE path had not.
>
> **Negative control:** reverting `ParamBufferBuilder.cs` to its pre-fix state turns **11** tests
> red, mapping to all four defects — 2 for the enum widths, 4 bool spellings (`true`/`TRUE`/`yes`/
> `on`; `1`/`0`/`false`/`no` pass either way, since the old byte parser handled digits), 4 float
> cases, 1 signed byte. 3610 tests, 0 failed.
>
> ⬜ **Not exercised against a live game.** The bytes are unit-verified; nobody has yet watched a
> UFunction receive a 1-byte enum or a `true` from the FIRE button.

> ### ✅ Y6 and Y7 FIXED — build 2881, 2026-08-15
>
> Both are struct params, and both come down to the same question: **what do you do when you cannot
> know the layout?** They answer it in opposite places and the fixes differ accordingly.
>
> **Y6 — the CE form cannot edit a struct, so it must stop pretending to.** The form has one edit box
> per param; `StructProperty` has no scalar spelling, so it fell through `GetMailboxWriteStatement`'s
> size switch to `writeInteger` — **four bytes** of a 12-byte (UE4) or 24-byte (UE5 LWC) `FVector`,
> taken from `math.floor(tonumber(text))`, with the rest left zero. A garbage vector went into a live
> call and nothing said so. The write is now skipped: the params buffer is already zero-filled, so the
> callee receives a well-defined zeroed struct instead of a mangled one, the emitted script carries a
> `-- <name>: struct (N B) left ZEROED` line, and **the box keeps its place but its label says
> `NOT EDITABLE - sent as zeroes`**. The box is kept deliberately — `edits[i]` is indexed by param
> position, so removing it would silently shift every later param's box.
>
> **Y7 — the engine's size overrules the version guess.** `KnownStructLayouts.GetLayout` is keyed on a
> **detected** UE version, and the dialog expanded sub-field editors from it without ever comparing it
> to the size the engine reported for that param. When the two disagree the guess is wrong — a
> mis-detected version (LWC turns `FVector`'s floats into doubles, doubling every offset) or a
> licensee fork with a modified struct, and this project has met both. The layout is now refused on
> mismatch and the caller falls through to the DLL-discovered fields, which came from the actual
> `UScriptStruct`.
>
> **Y7's rule was extracted to `InvokeParamDialog.ResolveTrustedLayout` so it could be tested at
> all.** It had been an inline condition inside a View's control-building loop — unreachable from a
> test, which is the same structural reason X1's missing field survived two builds. Five tests now
> cover it, including both directions of the mismatch and the `engineSize <= 0` case where nothing
> contradicts the guess and keeping it is correct.
>
> **Negative control:** reverting both turns 4 tests red — 2 for Y6 (each struct size) and 2 for Y7
> (each direction of the mismatch). Note `Generate_StructParam_LabelsTheBoxAsInert` stayed green,
> correctly: the label and the write-skip are separate changes and only the latter was reverted.
> 3624 tests, 0 failed.
>
> ⬜ **Not verified in-game.** Nobody has yet passed a struct param through either path against a live
> UFunction.

> ### ✅ Y8 FIXED — build 2875, 2026-08-15
>
> `celua.txt` settles it: `getAddress` *"returns the address of a symbol"* while `getAddressSafe`
> *"returns the address of a symbol, **or nil if not found**"*. The bare form **raises** on a missing
> symbol — which is the exact case the block exists to handle — so when the DLL was not loaded the
> first call aborted the chunk and took three things with it:
> - the **module-prefixed fallback** on the next line (never reached, though both spellings are
>   mandatory: which one resolves depends on how CE picked the module up — lessons-learned B33),
> - the **diagnostic** `showMessage` (the user saw CE's raw Lua error instead of *"make sure
>   UE5Dumper DLL is loaded"*), and
> - the **cleanup timer**, so the memory record stayed **ticked** after a bail-out that applied
>   nothing — against CLAUDE.md's untick rule.
>
> This was the last bare `getAddress` the repo emitted. Both sibling generators already did it
> correctly — `BakedScriptGenerator` via `getAddressSafe`, `CeReadinessLua` via
> `pcall(getAddress, …)` — so this is the same sibling-divergence shape as W4/W6/X1.
>
> **A pre-existing test broke, and it was the RIGHT kind of break.**
> `CeMailboxBailoutTests.NoMailboxWriteEscapesTheIdleWait` uses the mailbox lookup as a *textual
> anchor* for its window scan, and the rename lost the anchor — the test's own failure message had
> predicted exactly that (*"the anchor this scan starts from is gone"*). It now anchors on the
> **symbol** rather than the lookup function, so it survives a change of lookup. Note the
> distinction from Y1, where an actual **assertion** had to change: here only an anchor moved, and
> the test's subject (write ordering) is untouched.
>
> **Negative control:** reverting to `getAddress` turns the two new tests red while
> `CeMailboxBailoutTests` stays **green** — which is the proof the re-anchoring is genuinely
> spelling-agnostic rather than retuned to the new spelling. One of the two new tests was also
> hardened mid-flight: it had anchored on `getAddressSafe(`, so on revert it failed because
> `IndexOf` returned −1 rather than because the untick had gone. It now anchors on the symbol too.
> 3615 tests, 0 failed.

> ### ✅ Y1 FIXED — build 2862, 2026-08-15
>
> The prefix is stripped before the base-16 parse. The emitted expression now matches
> `^0[xX](%x+)$` and parses the **captured digits**, falling back to decimal and then to bare hex:
>
> ```lua
> local s = edits[N].Text or ''; s = s:gsub('%s+','')
> local h = s:match('^0[xX](%x+)$')
> if h then return tonumber(h,16) or 0 end
> return tonumber(s) or tonumber(s,16) or 0
> ```
>
> **Decimal is tried before bare hex deliberately.** This branch also serves `NameProperty`, whose
> value is an FName index a user may well type in decimal — reading `1234` as hex would silently
> change its meaning. That ordering is pinned by a test.
>
> **Verified with three independent detectors** (working-lessons §1.4), because a claim about Lua
> semantics is not settled by reasoning about Lua:
> 1. **Runtime** — the expression was evaluated verbatim in **Cheat Engine's own `lua53-64.dll`**
>    (`_VERSION` = Lua 5.3) against a stub `edits` table. Before: `0x1F2A3B4C5D0` → **0**,
>    `0X7FF6CD120000` → **0**, bare hex → **0**, and only a *decimal* address survived — which nothing
>    in this project produces, since `Renge::AddrToStr` formats every address as `0x` + uppercase hex.
>    After: every form resolves correctly, `1234` stays 1234, junk → 0, and every result is a Lua
>    **integer** (`math.type`), which `writeQword` requires.
> 2. **A standalone Lua 5.4.6 CLI** reproduced all ten inputs **identically** to CE's 5.3 DLL, which
>    also establishes that the behaviour is stable across 5.3 → 5.4 — worth knowing if CE ever
>    updates its bundled Lua.
> 3. **Source** — CE's bundled `Cheat Engine/lua53/lua53/src/lbaselib.c:48-65` shows the mechanism:
>    `int digit = isdigit(*s) ? *s - '0' : toupper(*s) - 'A' + 10; if (digit >= base) return NULL;`
>    For `'x'` that is `'X' - 'A' + 10 = 33`, and `33 >= 16`, so the conversion returns `NULL` → `nil`
>    → `or 0`.
>
> **A detail worth keeping:** the old code accidentally *worked* for padded input — `  0x40  ` made
> `s:sub(1,2)` miss the prefix and fall into the `else`, where plain `tonumber` handles `0x` fine. So
> the same field succeeded or silently returned null depending on stray whitespace.
>
> **Negative control:** reverting the fix turns all four new tests red. One of those tests was itself
> wrong on the first attempt — it asserted the script contains no `tonumber(s,16)` at all, which fails
> the *correct* code, because the fix legitimately uses that form as the bare-hex fallback once the
> prefix is known absent. Corrected to assert the old prefix-detection idiom (`s:sub(1,2)`) is gone,
> which is the defect's precise signature. 3594 tests, 0 failed.
>
> ⬜ **Not verified in Cheat Engine itself.** The Lua semantics are measured, but nobody has yet run
> the generated script against a live game and watched a UFunction receive the right pointer — see
> [todo.md](todo.md#pending-live-game-verification-verify-only--no-code).

> ✅ **Y9 fixed in build 2895** — the dialog is the only place that can tell the user, so it now does.
>
> **Why the dialog and not the writer.** Everything downstream narrows in silence and reports
> success: `ue5_freeze_helper.lua`'s byte writer is `writeByte(addr, math.floor(v) % 256)` and
> `Solide::WriteNumeric` is `static_cast<uint8_t>(llround(value))`. Neither has a channel to say
> "that did not fit" — the freeze is a background timer and the force-hold is a re-assert worker.
> So `9999` on a `ByteProperty` became `15` in the game with nothing on screen, and the user is left
> debugging the game rather than the input.
>
> **`ValidateAndConvert` now checks the WIDTH, not just the parse.** It had only ever asked "does
> this fit a `long`/`ulong`", which is the wrong question for seven of its eight integer types.
> `IntegerRange` is the inclusive per-type table, and the error names both the range and the value
> that would have landed: *"uint8 holds 0 to 255 — 9999 would be written as 15"*. `WrapToRange`
> computes that number with the same modular arithmetic the writers perform, and a test cross-checks
> it, so the quoted number cannot drift from the one that lands.
>
> **Two more sites of the same defect came with it, both inside the same method.** `float` was
> validated as a **double** — the check that rejects `NaN`/`Infinity` (B23) passed `1e300` straight
> through, and CE's `writeFloat` / Solide's `WriteFloatAt` narrow it to `+inf`; `1e-300` collapses to
> `0`. Both are now rejected for `float` and still accepted for `double`, which a test asserts in
> both directions.
>
> **The pre-filled default was half the finding and is inseparable from the fix.** `SuggestedDefault`
> returned a flat `"9999"` for every integer type, so adding the range check alone would have opened
> every `ByteProperty` dialog holding a value its own OK button rejects. It is now derived from the
> same `IntegerRange` table (`min(9999, Max)`), and a test asserts every helper type's suggestion
> survives `ValidateAndConvert` — a property that cannot drift, rather than a list that can.
>
> **One check covers two features.** `PropertySearchPanel.PromptForceValueAsync` reuses this dialog
> for Solide's Force value precisely for its per-type validation, so Force gained the same guard.
>
> **Negative controls, three, each isolating one claim:** deleting the two integer range checks reds
> exactly the 11 width tests (the boundary-acceptance and default tests stay green); deleting the
> float narrowing check reds exactly the 3 float tests; reverting `SuggestedDefault` to the flat
> `"9999"` reds exactly the 4 default tests — **and only for `int8`/`uint8`**, which is the predicted
> shape, since 9999 fits every wider type. That third control also demonstrates the interaction:
> without it the new range check would reject the app's own pre-fill. 3674 tests, 0 failed
> (3631 → 3674). `dist` is the 54.4 MB AOT-trimmed binary, launch-verified, no `crash.log`.
>
> 🆕 **Found while fixing it: Y15**, the *third* site of the `EnumProperty`-is-not-4-bytes family this
> audit has already fixed twice (W6, Y2). Recorded, not fixed — it needs a size plumbed through
> `FreezeScriptParams`, so it is M, not S. **Fixed in 2904; see below.**
>
> ⬜ **Not verified in-game.** The arithmetic is measured against the writers' own masking, but nobody
> has typed 9999 into a real `ByteProperty` freeze and watched the new error instead of a 15.

> ### ✅ Y15 FIXED — build 2904, 2026-08-15
>
> **The engine reported the width all along; the model dropped it.** `MapToHelperType` answered
> `int32` for every `EnumProperty`, and the freeze helper's `int32` writer is `writeInteger` — four
> bytes into UE's dominant one-byte `enum class E : uint8`, 20 times a second, silently destroying
> the three fields after it. The mapping's own comment admitted the gap. `PropertySearchMatch.PropSize`
> carries the real width, but `FreezeScriptParams` had no size field, so the generator **could not**
> know better. The finding is the dropped field, not the guess.
>
> **The repair is the shape W6 and Y2 established**: `HelperTypeForSize(int)` — the direct sibling of
> `CeXmlExportService.CeWidthForSize` — with 1 → `uint8`, 2 → `uint16`, 4 → `int32`, 8 → `int64`, and
> the legacy `int32` for an unreported/nonsense size. Only `EnumProperty` consults it, because it is
> the only type whose width its name does not fix; a test asserts every other type **ignores** the
> size argument, so a bogus value from the wire cannot turn a float into a byte. `PropertySize` is
> `required` rather than defaulted — the compiler now asks the question at every construction site,
> which is the same *"do not depend on a future editor remembering"* reasoning as Y2, with the type
> system doing the enforcing.
>
> **The reusable lesson is about the tests, not the mapping.** The mapping was already covered. The
> two places that USE it were not reachable from a test at all — `FreezeValueDialog`'s constructor
> needs an Avalonia runtime, and `PropertySearchViewModel`'s freeze command needs the AOBMaker bridge
> plus a modal — so the width could have been dropped at either call site with **zero failures**.
> Before writing the test, ask whether the call site can fail it. Two `internal static` seams
> (`FreezeValueDialog.HelperTypeFor`, `PropertySearchViewModel.BuildFreezeParams`, matching the
> existing `BuildRowsFromSelection` precedent) made the end-to-end assertion possible: **the type the
> dialog validates the user's input against is the type the generated script writes with** — this
> audit's recurring root cause (4a: *the report and the reality computed by different code paths*),
> now pinned instead of assumed.
>
> **Four negative controls, each applied alone:** reverting the mapping to flat `"int32"` reds **17**
> — every enum test at widths 1/2/8 across all four test files, while sizes **4 and 0 stay green**
> (`int32` *is* right there) and every non-enum type stays green, so the controls are sensitive to
> the defect rather than to the edit; dropping the size at the dialog reds **3**; at the single-row
> params **3**; at the batch-CT params **4**. **The middle two would have produced zero failures
> without the new seams** — which is the whole argument for extracting them. 3724 tests, 0 failed
> (3674 → 3724). `dist` is the 54.4 MB AOT-trimmed binary, launch-verified, no `crash.log`.
>
> 🆕 **Found while fixing it: Y16**, the *fourth* site of the same family —
> `InvokeScriptGenerator.GetMailboxWriteStatement` writes a 1-byte enum **param** with a 4-byte
> `writeInteger`, clobbering the next param. Recorded, not fixed: it is one line (the method already
> takes `p.Size` and already has a correct `size switch` fallback that `EnumProperty` short-circuits
> past), but it is the invoke subsystem with its own helper Lua and tests, so it deserves its own
> control rather than a ride on this commit.
>
> ⬜ **Not verified in-game.** The widths are unit-verified against the helper's writer table, but
> nobody has frozen a real `enum class : uint8` and confirmed its neighbours survive. Queued in
> [todo.md](todo.md#pending-live-game-verification-verify-only--no-code).

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **Y1** ✅ | **HIGH** | `InvokeScriptGenerator.cs:603` (`GetParseExpression`) | The pointer/FName branch detects a leading `0x` and then passes the **still-prefixed** string to Lua's `tonumber(s, 16)`, which rejects the `x` and returns `nil`; `or 0` then writes a **null pointer** into the params buffer. The DLL memcpys that straight into `ProcessEvent`, so the UFunction is called with `nullptr` — an access violation for any callee that dereferences it — and the script still reports `INVOKED OK` (or closes the Lua window silently when `DEBUG == 0`). The `else` branch's bare `tonumber(s)` **would have worked**: the special case is the only thing breaking it. The default `'0x0'` yields 0 correctly by accident, so a smoke test with unmodified defaults always passes. | S / low |
| **Y2** ✅ | MED | `ParamBufferBuilder.cs:221` (+`:224`) | `EnumProperty` is grouped with `IntProperty`/`UInt32Property` and written as 4 bytes gated on `available >= 4`, so a **1-byte enum param is not written at all** and the game receives 0. FIRE and the exported AA Script therefore send different values for the same dialog input. | S / low |
| **Y3** ✅ | MED | `ParamBufferBuilder.cs:165` | Typing `true` for a bool param: **FIRE sends 0, Copy AA Script bakes 1** — one dialog, two opposite calls. | S / low |
| **Y4** ✅ | MED | `ParamBufferBuilder.cs:185` | Float params: FIRE parses with `AllowThousands` and accepts `NaN`/`Infinity`; the baked generator does neither, so `1,5` **fires as 15.0 and bakes as 0**. | M / low |
| **Y5** ✅ | MED | `ParamBufferBuilder.cs:254` (`ParseByte`) | Rejects the very inputs the sibling baked generator accepts, so `true` and negative int8 values silently become 0 on FIRE. | S / low |
| **Y6** ✅ | MED | `InvokeScriptGenerator.cs:528` | Struct params in the interactive CE form collapse to a **single 4-byte `writeInteger`**, so an `FVector` param is filled with garbage. | M / low |
| **Y7** ✅ | MED | `InvokeParamDialog.cs:328` | Struct params pick their sub-field layout from the **guessed UE version** and never cross-check it against the size the engine reported for the param. | S / low |
| **Y8** ✅ | MED | `InvokeScriptGenerator.cs:164` | The last site in the repo still using bare `getAddress` — its module-prefixed fallback is unreachable, so a wrong-address result is reported as a real one. | S / low |
| **Y9** ✅ | MED | `FreezeValueDialog.cs:231` | Accepts (and pre-fills) values wider than the property — `uint8` 9999 is silently written as 15. | S / low |
| **Y10** | LOW | `BakedScriptGenerator.cs:223` | Verify mode writes into the mailbox (`writeByte(_PD_dbg + i, 0)` over `parmsSize`) **before any contract check** — `BakedScriptGenerator` is the only mailbox-touching generator with no `AppendContractCheck`, and CLAUDE.md's rule is explicit that the check comes *before the first write* because the layout is what is in question. | S / low |
| **Y11** | LOW | `ParamBufferBuilder.cs:228` | FIRE has no unsupported-param-type gate: an `FText`/`TArray`/`TMap` param's textbox is written as a raw int32 into the struct's pointer field. | M / low |
| **Y12** | LOW | `InvokeParamDialog.cs:856` | The baked-invoke clipboard fallback still copies a raw AA body — the build-1986 `WrapAaScriptXml` sweep reached the no-arg sibling **two lines away** and not this one. | S / low |
| **Y13** | LOW | `BakedScriptGenerator.cs:195` | Verify mode's 32-byte dump window cannot contain the complex return it tells the user to read. | S / low |
| **Y14** | LOW | `InvokeParamDialog.cs:850` | *"AA Script created in CE … (N baked param(s))"* is reported even when a param failed to parse and was baked as 0. | M / low |
| **Y15** ✅ | MED | *(hand-found while fixing Y9)* `FreezeScriptGenerator.cs:193` + `Models/FreezeScriptParams.cs` | `MapToHelperType` maps **`EnumProperty` → `int32` unconditionally**, so freezing / force-holding an `enum class : uint8` field emits a **4-byte `writeInteger`** and clobbers the three bytes after it. `FreezeScriptParams` carries no size at all, so the generator *cannot* know better — the DLL's real width is on `PropertySearchMatch.PropSize` and is dropped at the model boundary. **Third site of a family this audit has already fixed twice**: W6 (CE XML export hardcoded `"4 Bytes"`) and Y2 (invoke param buffer gated on `available >= 4`). The code comment at the mapping admits it — *"if a future game has a 1-byte enum we'd want to surface the size and pick uint8 instead. Out of v1 scope."* — which is the whole finding. Needs plumbing, hence M not S. | M / low |
| **Y16** | MED | *(hand-found while fixing Y15)* `InvokeScriptGenerator.cs:558` (`GetMailboxWriteStatement`) | `EnumProperty` is grouped with `IntProperty`/`UInt32Property` → **`writeInteger`**, so the **interactive CE invoke form** writes 4 bytes for a 1-byte enum param and clobbers the next param in `params_data`. Same defect Y2 fixed in `ParamBufferBuilder` (the FIRE path), surviving in a third path — so for one dialog input the three ways of calling a UFunction still do not agree. **Fourth site of the enum-width family** (W6, Y2, Y15). One line: the method already receives `p.Size` and its `_ => size switch` fallback already maps 1/2/8 correctly — `EnumProperty` short-circuits past it, so deleting it from that arm is the fix. **⚠ NOT a one-liner — see Y16's scope note below; it is three sites, not one.** | M / low |

> ### 📋 Y16 scope note — surveyed 2026-08-15, DELIBERATELY NOT FIXED
>
> The maintainer asked for this to be recorded rather than fixed. Recorded here because the survey
> found **the row above understates it**: Y16 is *three* sites, not one, and the sizing rule was
> already at hand at every one of them. Re-rated **M / low**.
>
> **Every call site already holds the size and does not pass it.** That is the whole shape, and it is
> Y15's shape exactly — *the finding is the dropped field, not the guess*:
>
> | # | Site | What it does today | Size in scope? |
> |---|------|--------------------|----------------|
> | 1 | `InvokeScriptGenerator.cs:558` `GetMailboxWriteStatement` — **interactive CE form, WRITE** | `EnumProperty` shares the `IntProperty`/`UInt32Property` arm → `writeInteger`, 4 bytes over the next param | **yes**, `size` is a parameter, and the `_ => size switch` fallback below it already maps 1/2/8 correctly |
> | 2 | `BakedScriptGenerator.cs:385` `MapToHelperType`, reached via `MapInputType` (`:430`) from the baked emit (`:154`) — **Copy AA Script (Baked), WRITE** | emits the token `'int32'`; `writeParams` in `scripts/ue5_invoke_helper.lua:250` is `elseif t == 'int32' or t == 'uint32' or t == 'enum' then writeInteger(...)`, so **even the `'enum'` token is 4 bytes** | **yes**, `v.Size` is in hand at `:154` and used only for the `fstruct` arm |
> | 3 | `CeInvokeReturn.cs:75` (+ the display type at `BakedScriptGenerator.cs:242`) — **RETURN, READ** | classifies via the same sizeless `MapToHelperType` → `ScalarRead("int32")` → `readInteger`, so a 1-byte enum **return** is reported from 4 bytes | **yes**, `size` is already a parameter of the enclosing method; `returnParam.Size` at `:242` |
>
> Sites 1–2 corrupt memory; site 3 only misreports. Fixing site 1 alone would leave the three ways of
> invoking a UFunction from one dialog input **still disagreeing**, which is the row's own complaint.
>
> **`BakedParamValue.Size`'s doc comment states the defective assumption outright** — *"Used for
> sanity checks; the helper allocates from `FunctionInfoModel.ParmsSize` and writes by type, not
> size."* That is the third time in this family a comment has documented the gap before anyone
> reported it (`MapToHelperType`'s "out of v1 scope" for Y15; `CeWidthForSize` existing-but-unused
> for W6).
>
> **The repair needs no Lua change**, which is worth knowing before scoping it: the helper already
> accepts `byte` / `int16` / `int64` tokens (`ue5_invoke_helper.lua:246-253`), so site 2 is a C#-only
> mapping change and no user has to re-embed the helper. Y15 already built the precedent to copy —
> a `(string, int)` overload plus a sizeless overload that behaves as size 0, with only
> `EnumProperty` consulting the size and a test asserting every other type **ignores** it.
>
> **One cosmetic straggler**, to fold in rather than file separately:
> `BakedScriptGenerator.ShortTypeNameForComment` (`:571`) hardcodes `"enum(int32)"`, so the generated
> script's own trailing comment would keep asserting `int32` after the write was corrected.
>
> The family now stands at **4 findings across 7 sites in 4 subsystems** — W6 (CE XML export) ✅,
> Y2 (FIRE param buffer) ✅, Y15 (freeze/force) ✅, Y16 (three invoke sites + one comment) open.

**Verified independently (not agent-reported):**

1. **Y1's second lens MEASURED it rather than arguing it** — the strongest verification any agent has
   produced in this audit. Instead of reasoning about Lua semantics it loaded **CE's own
   `lua53-64.dll`** via ctypes and evaluated the emitted expression verbatim against a stub `edits`
   table: `0x1F2A3B4C5D0` → **0**, `0X…` → 0, bare hex → 0, and only a *decimal* address survives —
   which nothing in this project ever produces, since `Renge::AddrToStr` formats every address as
   `0x` + uppercase hex. Branch isolation confirmed the untaken `else` would have worked. That is a
   negative control in the form this project demands, run by an agent.
2. **A hand-run CeLuaHygiene usage matrix found Y10 before the agents reported, and corrected my own
   analysis.** Cross-tabulating every CE-Lua emitter against the shared helpers showed
   `BakedScriptGenerator` alone hand-rolling `close-on-success` and carrying **no contract check**
   while its twin `InvokeScriptGenerator` uses `AppendContractCheck`. My first pass then wrongly
   concluded Baked performs no mailbox *writes* — the grep covered
   `writeInteger|writeQword|writeBytes|writeFloat` and **missed `writeByte`**, which is exactly what
   `:223` emits. The agents caught what the too-narrow pattern hid, so Y10 is a write-before-check,
   not the cosmetic duplication I had downgraded it to. *(The matrix's control passed:
   `CeLuaHygiene.cs` and 10+ reference generators show as helper users, so its negatives mean
   something.)*
3. **A related duplication the matrix surfaced, filed here so it is not lost:** `0x328` is hardcoded
   at `BakedScriptGenerator.cs:194`/`:309` and `TeleportScriptGenerator.cs:173`/`:185`, while **five
   generators across eight sites** interpolate `CeMailboxLayout.OffParamsData` (`InvokeScriptGenerator`
   even aliases it). The offsets are correct today; this is cluster ④ waiting to happen the next time
   the mailbox layout moves. `TeleportScriptGenerator` is outside U4's scope — fix it in the same
   sweep.

-----

### T1e — Views code-behind + app root + the sub-50 tail — ✅ scanned 2026-08-15 — **AUDIT #5 SCANNING COMPLETE**

**12 agents** (2 deep lenses on the head + 3 grep-driven sweeps over the tail → 3 refute batches →
4 second-lens batches), 0 errors. Scope: **5 head files (628 early lines) read properly + 226 tail
files (1,172 early lines) swept by pattern**, exactly as §1's phase plan specified — a deep read of
226 five-line files is waste.

**32 raw → 30 distinct → 3 refuted → 0 killed by the second lens → 27 confirmed. Kill rate 10%.**

**Tally: 0 HIGH · 6 MED · 17 LOW · 4 INFO.**

> ### ⚠ 10% — the lowest kill rate of the entire audit
>
> Below S1's 14%. **Nothing below MED here is vetted.** The grep-sweep design is the likely cause and
> it cuts both ways: it surfaces real signature hits cheaply, but a hit that a deep read would have
> dismissed in context reaches the skeptic looking plausible. Treat the 17 LOWs and 4 INFOs as
> **leads produced by a pattern search**, not as findings.

> ### 🔍 The second lens corrected the FIRST SKEPTIC — the best adversarial work in the audit
>
> On AF1, the first skeptic decoded Avalonia's shipped IL by hand (RVA→file offset, method header, IL
> bytes) and found `SelectingItemsControl.SelectionChangedEvent` and `DataGrid.SelectionChangedEvent`
> both registering with `ldc.i4.4` = `RoutingStrategies.Bubble`. It then concluded **both** reach the
> handler. The second lens read the same evidence and drew the opposite — correct — conclusion:
> `DataGrid` **Registers a new RoutedEvent** rather than `AddOwner`-ing the existing one, and
> Avalonia keys handlers by RoutedEvent *instance*, so the two are **disjoint routes**. TeleportPanel's
> `CoordGrid` therefore does **not** reach this handler; `ComboBox`/`ListBox` (which derive from
> `SelectingItemsControl`) do.
>
> So the finding survives on a narrower and correct mechanism. **A skeptic that measures can still
> misread its own measurement** — which is the argument for the second lens existing at all, and the
> second time this audit has seen it (T1c/AE1 was the first).
>
> **AF1 itself, hand-verified:** `MainWindow.axaml:420` attaches `MainTabs_SelectionChanged` to the
> **outer** `TabControl`; the handler guards only on `sender is not TabControl` and `DataContext is
> not MainWindowViewModel` — **zero** `e.Source` / `e.Handled` / `OriginalSource` checks (grep count:
> 0). Its own remark at `:551` says *"inner SelectionChanged events bubbling from child grids are
> harmless"* — which defends the **tag read** and not the side-effect body, so picking an older
> snapshot in Class Pivot's ComboBox re-fires the whole per-tab activation routine and
> `RefreshAsync` snaps `SelectedSnapshot` back to `Snapshots[0]`. Root cause **#6** again — now
> **6 for 6**, and it found the lead finding in three consecutive phases (T1a, T1b, T1e).

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **AF1** | MED | `Neu.h:94` (Neu::BuildLayout (FNameData57 branch)) | UEnum member count is range-checked AFTER a signed cast, so the whole upper half of the uint32 range passes and yields a NEGATIVE count | S / low |
| **AF2** | MED | `DetectStatsViewModel.cs:158` (DetectStatsViewModel.DetectAsync) | Detect Stats stops live-probing after 30 classes, and the never-probed rows render identically to rows that were probed and had no live instance | S / low |
| **AF3** | MED | `LiveFuncsViewModel.cs:210` (LiveFuncsViewModel.FetchAndPopulateAsync) | Live PE Profiler fetches only the top 300 functions but reports the DLL's FULL distinct count, and builds the diff baseline from the same truncated page — manufacturing false "NEW" rows | M / low |
| **AF4** | MED | `LiveWalkerPanel.axaml.cs:76` (LiveWalkerPanel.OnDetached) | Live Walker tears down all six VM event subscriptions on visual-tree detach and never re-subscribes on re-attach | S / low |
| **AF5** | MED | `MainWindow.axaml.cs:666` (MainTabs_SelectionChanged) | Per-tab activation routine re-runs on every bubbled child SelectionChanged, silently reverting the user's Class Pivot snapshot/pick selections | S / med |
| **AF6** | MED | `PropertySearchPanel.axaml.cs:68` (PromptForceValueAsync (double.TryParse of the ) | The Force flow funnels the width-validated int64 literal through a double, and any parse failure is returned as "cancelled" | S / low |
| **AF7** | LOW | `Denken.h:37` (Denken::NativeAnalysisResult::budgetHit) | Path-2 native disasm's "result may be partial" flag is written, logged, and then dropped before the wire — the Xref dialog shows a truncated field list as complete | S / low |
| **AF8** | LOW | `Solide.cpp:128` (Solide::ReadNumeric (byte-width fallback)) | Int8Property is READ as unsigned while it is WRITTEN as signed, so a negative forced value never converges and the re-assert worker reports permanent drift against the game | S / low |
| **AF9** | LOW | `Constants.cs:37` (Constants.MaxProcessFolders) | A COUNT cap silently deletes whole per-game log folders inside the 21-day window that CLAUDE.md says is the only retention rule | S / low |
| **AF10** | LOW | `Program.cs:27` (Program.Main) | Main discards the process exit code, so the deliberate Shutdown(1) on the second-instance path reports success | S / low |
| **AF11** | LOW | `CoordinateLibraryStore.cs:43` (CoordinateLibraryStore..ctor) | A third unbounded per-game file family writes to the app-data ROOT, bypassing AppDataFolderMaintenance -- and Constants.cs asserts it does not exist **[2 lenses]** | M / low |
| **AF12** | LOW | `GroupMatch.cs:285` (GroupMatch.Run) | The "shared per-slot cap" invariant the comment insists on is broken the moment the user changes the live cap | S / low |
| **AF13** | LOW | `GroupMatch.cs:302` (GroupMatch.Run (per-slot cap truncation)) | Snapshot group match truncates a slot at 256 leaves with no signal of any kind — the DLL sibling at least logs it | S / low |
| **AF14** | LOW | `MovementScriptGenerator.cs:163` (MovementScriptGenerator.EmitGravDirBlock) | Gravity-direction emitter writes X through CeMailboxLayout.OffParamsData but Y and Z at raw 0x330 / 0x338 in the same statement group | S / low |
| **AF15** | LOW | `TeleportViewModel.cs:3914` (TeleportViewModel.PushCoordLuaNoDllAsync / Sav) | The coordinate-library "N group(s) had no radio button" disclosure is emitted at 1 of the 3 export call sites; the other two discard it with `out _` | S / low |
| **AF16** | LOW | `DetectStatsPanel.axaml.cs:14` (DetectStatsPanel..ctor) | Four panels break the AOT sort rule their sibling panels' comments spell out — column headers are clickable and do nothing in the shipped trimmed build | M / low |
| **AF17** | LOW | `DetectStatsPanel.axaml.cs:14` (DetectStatsPanel..ctor) | Detect Stats panel makes no WireSortComparers call at all; two of its seven sortable columns sort on a path no binding roots | S / low |
| **AF18** | LOW | `FunctionPropsDialog.cs:165` (FunctionPropsDialog.BuildUi (_grid)) | Code-built xref grid enables sorting on six template columns with no CustomSortComparer — every header is dead under AOT | S / low |
| **AF19** | LOW | `LiveFuncsPanel.axaml.cs:15` (LiveFuncsPanel.ResultsSortComparers) | Live Funcs comparer dictionary omits MeanPeriodMs, leaving the Period column — the point of the Phase E cadence feature — unsortable under AOT | S / low |
| **AF20** | LOW | `LiveWalkerPanel.axaml.cs:44` (LiveWalkerPanel (ctor) / WireSortComparers) | AOT sort comparers are wired onto 1 of the file's 3 DataGrids; FunctionGrid's "Params" column breaks the repo's own documented AOT sort rule and its header is a silent no-op in the shipped binary **[2 lenses]** | S / low |
| **AF21** | LOW | `MainWindow.axaml.cs:276` (IsSnapshotPositionAcceptable) | Window-placement guard feeds DIP sizes to a helper documented as taking physical pixels, so on a HiDPI monitor a legitimately-placed window is rejected and its position stops being tracked | S / low |
| **AF22** | LOW | `PropertySearchPanel.axaml.cs:57` (PromptForceValueAsync) | The "Force value…" flow shows the Freeze dialog verbatim, so the button the user clicks says "Create freeze script" while the action writes and holds the field on every live instance | S / low |
| **AF23** | LOW | `PropertyXrefDialog.cs:270` (PropertyXrefDialog.BuildUi (_grid)) | Property-xref grid: same six-template-column sort surface, also never wired to a comparer | S / low |
| **AF24** | INFO | `ObjectTreeFilter.cs:58` (ObjectTreeFilter.MatchesAllTerms) | NEGATIVE RESULT — four of the tail helpers most likely to be half-applied are in fact fully applied | S / low |
| **AF25** | INFO | `CeMailboxLayout.cs:94` (CeMailboxLayout (Cmd opcodes)) | The canonical mailbox-layout class names Teleport as one of its consumers but carries no Teleport opcode; two generators hardcode 8 instead | S / low |
| **AF26** | INFO | `ViewLocator.cs:15` (ViewLocator.Build) | NEGATIVE RESULT — the AOT/trim reflection sweep over all 226 tail files came back clean | S / low |
| **AF27** | INFO | `ViewLocator.cs:23` (ViewLocator.Build) | Negative result on the AOT name-resolution trap -- but Match accepts 22 ViewModel types while Build handles 6 | S / low |

> **Hand-verified:** AF1. **Not re-derived:** the other 5 MEDs, 17 LOWs and 4 INFOs — and at a 10%
> kill rate over a grep-driven sweep, that caveat is heavier here than anywhere else in the audit.

-----

## 2z. Scanning is COMPLETE — 12 of 12 segments, 2026-08-13 → 2026-08-15

Every segment in §1's plan has been scanned. **No segment remains.**

| Segment | Raw → distinct | Kill rate | HIGH / MED / LOW / INFO |
|---------|---------------|----------:|-------------------------|
| D1 Ubel | 27 → 11 | 48% | 0 / 8 / 3 / 0 |
| D2 Genau+Serie · D3 Aura · D4a · D4b · D5 | see each block | 44–73% | — |
| U1 LiveWalker/Pointer/ObjectTree | 18 | 33% | **1** / … |
| U2 Export services | 16 | 25% | 2 / … |
| U3 Dump + MainWindow VM | 18 | 33% | 0 / … |
| U4 Dialogs + CE generators | 15 | 0% | **1** / … |
| U5 Remaining VMs/Models/Core | 30 → 19 | 20% | 0 / 3 / 14 / 2 |
| S1 Early Lua | 65 → 36 | 14% | **3** / 17 / 14 / 2 |
| T1a Radar + entry points | 35 → 30 | 23% | **2** / 5 / 12 / 4 |
| T1b DLL headers + C++ tests | 40 → 34 | 21% | **2** / 4 / 15 / 6 |
| T1c VMs + Core + Models | 46 → 34 | 15% | 0 / 10 / 15 / 4 |
| T1d UI Services | 30 → 26 | 35% | 0 / 2 / 10 / 5 |
| T1e Views + root + tail | 32 → 30 | 10% | 0 / 6 / 17 / 4 |

**The three findings a reader should start from**, all hand-verified against the source:

1. **T1a/AB1 — our DLL crashes Cheat Engine** on a documented install path. `DllMain` starts a
   1 ms-poll thread unconditionally; CE `FreeLibrary`s plugin DLLs; `DllMain`'s `lpReserved` is
   commented out so DETACH cannot tell unload from process-exit; nothing pins the module.
2. **T1b/AD1 — a C++ test target that fails to COMPILE is reported as "skip" and the build exits 0**,
   in CI too. Latent today (the control confirmed the suite runs), but it silently disarms ~700
   assertions the moment a test stops compiling. — ✅ **FIXED with AD2, build 2914** (one shared
   `Invoke-CppSelfTest` helper + a same-class sibling in the C# phase; negative-control verified).
3. **S1/AA1 — a bool freeze writes a WHOLE BYTE over a bit-packed `FBoolProperty`**, ~16×/sec,
   while the DLL sibling reached from the same Property Search row writes only the bit. — ✅ **FIXED
   (2922)**: the FieldMask now travels DLL wire → model → row → params → CFG → Lua.

**Two defect FAMILIES account for more findings than any single subsystem**, and both are greppable:
- **The width family** (an out-of-range value masked to the field width and reported as written):
  **W6, Y2, Y9, Y15, Y16, AE1** — six findings, four subsystems. At every site the correct width was
  in scope and simply not enforced. *(This line originally said "AE9" — a misnumbering; T1c's table
  assigns the `FieldValueConverter.cs` width finding the ID **AE1**, and AE9 is the Sort-picker
  no-op. Corrected 2026-08-15.)*
- **Root cause #4, a fix applied at only some of its sites**: **V2, W4/W6, X1, Y16, AC2, AE10** —
  seven occurrences, and **AC2 is this audit's own Y7 fix at 1 of 5 consumers**.

**The single most productive technique was the comment sweep** — grep for a comment admitting a
limitation or asserting an impossibility, then check it. **6 for 6**, and it produced the lead
finding in T1a, T1b and T1e.

**Harness lessons, measured:** merge claims by location **in the script** (asking the model does not
work — S1 marked zero while 14 locations were multi-lens); cost scales with **claims found**, not
lines read (S1: 31 agents for 1,236 lines; T1a: 11 for 1,504); and **tightening the skeptic's rubric
does not raise its kill rate** — S1's was the strictest written and killed the least.

-----

### T1c — remaining UI ViewModels + Core + Model DTOs — ✅ scanned 2026-08-15

**13 agents** (5 lenses → 4 refute batches → 4 second-lens batches), 0 errors, over 22 files /
2,889 early lines. **46 raw → 34 distinct** (**12 folded in-script — the highest yet**) **→ 3 refuted
→ 2 killed by the second lens → 29 confirmed. Kill rate 15%.**

**Tally: 0 HIGH · 10 MED · 15 LOW · 4 INFO.** No inflated HIGHs this time — the first phase where the
finders did not over-rate anything. Three MEDs re-derived by hand.

> ### ⚠ 15% kill rate — low again, and the LOWs are unvetted
>
> Only S1 (14%) was lower. **Nothing below MED here has been re-derived**; treat the 15 LOWs and 4
> INFOs as leads, not findings. What *did* work well is the pipeline's ability to **correct a
> mechanism instead of passing it through**: on AE1 both the skeptic and the second lens killed the
> finder's headline race (both commands route to the same FIFO interactive lane, so the interleaving
> it needed cannot occur) and kept the finder's *own* secondary trigger, which is real. That is the
> stage doing its job even at a low kill count.

> ### The two patterns that keep recurring, now at four and seven occurrences
>
> **AE1 — the width family reaches a FOURTH subsystem** *(this paragraph originally said "AE9", but
> the table above numbers this finding AE1 — corrected 2026-08-15)*. `FieldValueConverter.cs:198` writes an
> enum-backed field as `1 => new[] { (byte)(rawValue & 0xFF) }` (and `& 0xFFFF` / `& 0xFFFFFFFF` for
> the wider cases) — verified by hand. An out-of-range value is **masked to the field width and the
> untruncated number is reported as written**, which is Y9's defect (`9999` accepted for a `uint8`,
> game gets `15`) in a different file. The family now spans **W6, Y2, Y15, Y16, Y9 and AE9**, and in
> every case the correct width was in scope and simply not enforced.
>
> **AE10 — root cause #4's SEVENTH occurrence.** The "stop gating Locate-in-GWorld on the client
> `IsGWorldAvailable` flag" fix is applied at Value Search only. Verified: `IsGWorldAvailable` has
> **37 references across 12 ViewModels**. The running list is now V2, W4/W6, X1, Y16, AC2 (the
> audit's own Y7 fix at 1 of 5) and AE10. §3b's "grep for siblings before closing a fix" rule was
> written after the fourth; the fact that three more have appeared since says the rule is still not
> being applied *at fix time*.

> ### ✅ AE1 FIXED — build 2950, 2026-08-15 — and with it the whole width family bar Y16
>
> `TryConvertEnum` wrote `(byte)(rawValue & 0xFF)` (and `& 0xFFFF` / `& 0xFFFFFFFF`) and reported
> success, so `9999` into a 1-byte enum put **15** in the game while LiveWalker's status line said
> `Written: Field = 9999`. Every sibling converter in that same file already refused out-of-range and
> named the range — `TryConvertByte` returns *"Invalid byte (range: 0 to 255)"* — so the fix is the
> file's own established idiom, applied to the one path that had never adopted it.
>
> **The predicate now exists once**, as `FieldValueConverter.FitsInWidth(value, sizeBytes)`, because
> five hand-written range checks are five things that drift. It is deliberately
> **signedness-tolerant**: a field of N bytes accepts the union `[-2^(8N-1), 2^(8N)-1]`, since the
> engine reports a width but not always a signedness and both readings are things users legitimately
> type (`-1` into a byte means `0xFF` — Y5's rule, which must not regress; `255` into a signed byte
> is the same bit pattern from the other side). What the union still catches is the case every
> finding in this family was about: a value that fits in **neither** reading.
>
> **Three fixes shipped together** because they are one family and the audit's rule is to fix the
> family at fix time: AE1 here, the confirmed `DecodeParamValue` read-side lead, and the confirmed
> `ParamBufferBuilder` FIRE-path lead — plus the `ParseULong` sibling the tests exposed. See the
> family block in §3c for the per-lead rulings, including the one that was **refuted**.
>
> ✅ **Negative-controlled one break at a time** (3751 → **3820** tests, 0 failures): AE1 reverted →
> 6 failures; the enum-read revert → 3; the FIRE range check → 8; `ParseULong` → 3.
>
> ⚠ **The first control run was itself wrong and had to be redone** — two of the four reverts did not
> compile, so no test ran, and the harness's `'error CS' in out` fallback reported that as
> "DETECTED". A compile error is **inconclusive**, not detection. Worth remembering: a negative
> control needs a revert that BUILDS, or it measures the compiler instead of the tests.
>
> > **AE1 is worth reading before fixing anything in Class/Struct.** `ClassStructViewModel.cs:218`
> latches `_lastLoadedNodeAddress = node.Address` **before** dispatching the walk, and
> `LoadClassAsync`'s `catch` never resets `HasClass`. So a failed walk leaves the key naming node B
> while the grid still shows class A's names, offsets and FProperty addresses — and the entry guard
> `if (_lastLoadedNodeAddress == node.Address && HasClass) return;` then makes **re-selecting B a
> no-op**. Two aggravations the second lens established independently: the panel binds **no**
> ErrorMessage/HasError at all, so `SetError` writes to a property this panel never renders (the
> failure is completely silent); and nothing resets either field on connect/disconnect, so it
> **survives a reconnect**. The user copies A's offsets into CE believing they are B's.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **AE1** ✅ | MED | `FieldValueConverter.cs:198` (FieldValueConverter.TryConvertByte / TryConver) | An enum-backed field silently truncates an out-of-range value and reports the untruncated number as written | S / low |
| **AE2** | MED | `ClassStructViewModel.cs:192` (OnObjectSelected) | Object-Tree selection drives Class/Struct through an async-void handler with NO generation guard, and the two branches issue a different NUMBER of round-trips — so a stale selection can settle last and the panel shows a class that is not the selected node | S / low |
| **AE3** | MED | `ClassStructViewModel.cs:218` (OnObjectSelected) | The dedupe key is latched BEFORE the load, so a failed or out-of-order walk pins the panel on the wrong class with no way to retry **[2 lenses]** | S / low |
| **AE4** | MED | `ProxyDeployViewModel.cs:236` (OnSelectedProxyTypeChanged / RefreshAfterTypeC) | Two rapid proxy-radio clicks race two fire-and-forget refreshes; the loser's proxy type wins the grid and nothing ever re-runs | S / low |
| **AE5** | MED | `ProxyDeployViewModel.cs:1073` (RefreshAsync) | IsScanning is READ as a global busy flag by six guards but WRITTEN by only three of eight operations - Refresh/Deploy/Undeploy/UpdateAll are invisible to every guard | S / low |
| **AE6** | MED | `ProxyDeployViewModel.cs:1106` (DeploySelectedAsync / UndeploySelectedAsync / ) | The four file-mutating Proxy Deploy commands set no busy flag, only TEST one — so Deploy and Undeploy run concurrently over the same Binaries folder and both write the single result line | S / low |
| **AE7** | MED | `ProxyDeployViewModel.cs:1233` (UpdateAllAsync) | UpdateAllAsync iterates the live Games ObservableCollection across awaits while a concurrent scan can Games.Clear() it - and the method has no catch, so the tally is never reported **[2 lenses]** | S / low |
| **AE8** | MED | `ValueSearchViewModel.cs:752` (FirstScanAsync / NextScanAsync) | The DiagnosticsProbe is opened BEFORE the input validation, so every rejected click costs two get_diagnostics round-trips and logs a "Value Scan (First)" measurement for a scan that never ran | S / low |
| **AE9** | MED | `ValueSearchViewModel.cs:906` (NewScanAsync / GroupNewScanAsync) | New Scan resets the internal sort key but not the bound Sort picker, and re-selecting the option the picker already shows is a silent no-op | S / low |
| **AE10** ✅ | MED | `ValueSearchViewModel.cs:951` (ValueSearchViewModel.IsGWorldAvailable) | The "stop gating Locate-in-GWorld on the client IsGWorldAvailable flag" fix is applied at Value Search only — **7** sibling VMs still gate on it, at 19 sites: 14 C# + 5 XAML (recounted 2026-08-15; the earlier "9" counted `LiveWalkerViewModel`'s write-only dead flag and `MainWindowViewModel`'s propagation assignments as gates) | M / low |
| **AE11** | LOW | `AddressHelper.cs:41` (AddressHelper.FormatAddress (AddressFormat.Mod) | ModuleOffset formats a heap address as a wrapped RVA with no in-module check, producing a module-relative address that breaks on relaunch | S / low |
| **AE12** | LOW | `IDumpService.cs:264` (IDumpService.BeginValueScanAsync) | Three doc comments assert native (non-UPROPERTY) fields are unreachable — 18 lines above the `nativeC` parameter that reaches them | S / low |
| **AE13** | LOW | `ValueScanModels.cs:462` (GroupScanBeginResult) | The group scan's per-slot leaf-cap truncation is computed by the DLL and only LOGGED — no wire key, so no DTO can carry it | M / low |
| **AE14** | LOW | `ClassStructViewModel.cs:70` (ApplyFieldFilter) | Fields.Clear() on a selection-bound DataGrid with no SelectedField = null detach - the one panel missing the line the other four carry verbatim **[3 lenses]** | S / low |
| **AE15** | LOW | `GameClassFilterViewModel.cs:81` (RebuildSuggestions) | The Super / Package suggestion lists are built from a truncated class page and presented as complete, ten lines above the code that reads the truncation flag | S / low |
| **AE16** | LOW | `GameClassFilterViewModel.cs:194` (ClearFilters) | ClearFilters blanks the filter box without _filterMemory.Flush(), discarding the keyword the user just typed | S / low |
| **AE17** | LOW | `GameClassFilterViewModel.cs:267` (BatchFindFuncAsync) | Batch Find Func discards res.Scan.DeadlineHit, so a class whose reflection sweep timed out is written as "0" - indistinguishable from "no function takes this class" | S / low |
| **AE18** | LOW | `GameClassFilterViewModel.cs:268` (BatchFindFuncAsync) | Batch Find Func writes "0" for a class whose 30 s reflection sweep was cut short **[2 lenses]** | S / low |
| **AE19** | LOW | `GameClassFilterViewModel.cs:283` (BatchFindFuncAsync) | Batch Find Func reports a pipe disconnect as "you cancelled" — audit #3's L14 at a third unfixed site **[5 lenses]** | S / low |
| **AE20** | LOW | `ProxyDeployViewModel.cs:786` (DeleteSelectedOrphansAsync (and ScanAsync / De) | Six commands accept a CancellationToken, check it in their loops and carry a whole "cancelled" reporting path — and none of them can be cancelled, including the destructive Recycle-Bin delete | S / low |
| **AE21** | LOW | `ValueSearchViewModel.cs:439` (LoadWindowAsync) | "Load More" cancels an in-flight page-0 reload and then derives its offset from Candidates.Count, which still holds the SUPERSEDED window — producing a grid that concatenates two different filters/sorts | S / low |
| **AE22** | LOW | `ValueSearchViewModel.cs:729` (SetEngineState) | Nothing clears the Value Search session on disconnect, so a dead game's session id and candidate rows survive a full reconnect | S / low |
| **AE23** | LOW | `ValueSearchViewModel.cs:820` (FirstScanAsync / NextScanAsync / GroupFirstSca) | Eight bare OperationCanceledException catches report a token-less pipe-disconnect OCE as "you cancelled", two of them silently **[2 lenses]** | S / low |
| **AE24** | LOW | `ValueSearchViewModel.cs:875` (NextScanAsync / GroupNextScanAsync) | A Next Scan erases both truncation signals from a truncated First Scan - the status note and the "Counts are partial" badge - though the survivor set is still a subset of a truncated scan **[3 lenses]** | S / low |
| **AE25** | LOW | `ValueSearchViewModel.cs:1063` (ValueSearchViewModel.GroupScanTypeOptions) | Doc says Between is "intentionally excluded" from the group scan-type picker; Between is the 4th element of that same initializer | S / low |
| **AE26** | INFO | `FieldValueConverter.cs:11` (T1c AOT/trim + Core-purity sweep (negative res) | NEGATIVE RESULT — all 22 T1c files are AOT-clean and Core contains zero platform-specific code | S / low |
| **AE27** | INFO | `ClassListResult.cs:32` (GameClassEntry.Package) | `Package` is documented as "Pre-computed once so the DataGrid binding doesn't recompute per repaint" but is an expression-bodied property that recomputes on every read | S / low |
| **AE28** | INFO | `ProxyDeployViewModel.cs:513` (ProxyDeployViewModel.NotifyOrphanSelectionChan) | `NotifyOrphanSelectionChanged`'s doc names a View call-site that does not exist; the real trigger is the per-row PropertyChanged subscription | S / low |
| **AE29** | INFO | `ProxyDeployViewModel.cs:1310` (ProxyDeployViewModel.NotifySelectionChanged) | `NotifySelectionChanged` documents itself as "called from View" and has no caller anywhere; the `HasSelection` it raises has no per-row hook, unlike its sibling in the same file | S / low |

> **Also worth noting among the refuted:** the claim that `ClassListResult.TotalClasses` is rendered
> as a pool census died — correctly, because **X2 already fixed exactly that** (build 2888 added the
> `truncated` flag and the cap note). A refutation landing on this audit's own shipped fix is the
> pipeline confirming a fix took, which is worth as much as a finding.
>
> **Hand-verified:** AE1, AE9, AE10. **Not re-derived:** the other 7 MEDs, 15 LOWs and 4 INFOs.

-----

### T1b — DLL contract headers + the entire C++ test suite — ✅ scanned 2026-08-15

**13 agents** (5 lenses → 4 refute batches → 4 second-lens batches), 0 errors, over 10 files /
2,394 early lines. **40 raw → 34 distinct** (6 folded in-script) **→ 7 refuted → 0 killed by the
second lens → 27 confirmed. Kill rate 21%.**

**Tally: 2 HIGH · 4 MED · 15 LOW · 6 INFO.** The two HIGHs are **one defect** reported by two lenses
at the two arms of the same branch; both were re-derived by hand.

> ### 🔴 AD1/AD2 — a C++ test target that FAILS TO COMPILE is reported as "skip", and the build exits 0
> ### ✅ FIXED build 2914 — the finding as written is below, the fix record follows it
>
> This is what putting the tests in scope as *subjects* was for. **The pass/fail signal is derived
> from "did an exe path get assigned", not from "did the build succeed"** — and one failure mode of
> the former is indistinguishable from a benign one. Verified line by line:
>
> - `build.ps1:351-357` — `$cppTargets` is **UE5Dumper + the four proxies only**. Neither test target
>   is ever built by the DLL phase, so a break confined to a test target cannot fail any other phase.
> - `build.ps1:184` — `if ($LASTEXITCODE -ne 0) { return $false }`, so a compile error makes
>   `$dllBuildOk` false.
> - `build.ps1:670-677` — `$dllTestExe = $null`, assigned **only inside `if ($dllBuildOk)`**.
> - `build.ps1:691-693` — the `else` arm is **exactly one line**:
>   `Write-Info "dll_helpers_test.exe not available (skip — run -Target DLL or All first)"`.
>   **`$exitCode` is never touched.** The identical shape sits at `:665-667` for the utf8 suite.
> - `.github/workflows/ci.yml:142` — `./build.ps1 -Mode Publish -Clean -NoBumpBuildNumber`, and
>   `build.ps1:43` is `[string]$Target = "All"`, so **CI runs straight through this branch**, with
>   `-Clean` guaranteeing no stale exe can rescue it.
>
> Rename a symbol, update the one DLL caller and not the test, and: the five shipping targets build
> clean, `dll_helpers_test` fails to compile, the script prints an INFO line, `Status: SUCCESS`, CI
> green — and **~700 assertions across Radar/Orden/GraphPath/Lineal/Denken/Solitar/Solide/Macht stop
> executing with no signal**, including the memory-corruption class `Test_Solitar_ApplyBoolBit` and
> `Test_Packed_*` exist to catch.
>
> **The message actively misdirects.** "not available (skip — run -Target DLL or All first)" tells the
> operator they forgot a prerequisite — when they ran the *right* target and a compile error scrolled
> past in the same transcript. The correct diagnosis is the one the text argues against.
>
> ✅ **Negative control, run rather than assumed:** `build.ps1 -Target Test` on the current tree prints
> `>> Running utf8_helpers_test... [OK] utf8_helpers_test passed` and `>> Running dll_helpers_test...`,
> so **the C++ suite really is executing today** and every "tests green" claim in this session's
> commits is backed by a real run. This defect is **latent, not currently biting** — which is the
> distinction that decides its priority: it is a trap for the next person who breaks a test's
> compilation, not an active hole.
>
> Fix is small and splits the two causes: set `$exitCode = 1` + `Write-Fail` when `$xxxBuildOk` is
> false, and keep the benign "skip" only for the genuinely-absent-`$BUILD_DIR` case at `:645`/`:671`.
>
> ### ✅ AD1 + AD2 FIXED — build 2914, 2026-08-15
>
> **Both sites collapsed into ONE function**, `Invoke-CppSelfTest` (`build.ps1`, beside
> `Invoke-CmdInVsEnv`), rather than the two parallel edits the finding asked for. Two hand-copied
> blocks *are* root cause #4's shape — the audit's strongest recommendation is to fix the family, and
> here the family could be eliminated outright: adding a third C++ test target can no longer add a
> third copy of the logic. The call sites are now two `if (-not (Invoke-CppSelfTest …)) { $exitCode = 1 }`.
>
> Three outcomes that used to collapse into one "skip" line are now three distinct failures:
> **target failed to compile**, **built but no `.exe` found**, **build dir absent**. The benign-skip
> arm was *removed*, not narrowed — under `-Target Test` / `All` the C++ suite is expected to build
> and run, so no absence of it is benign.
>
> **Sibling found and fixed at fix time** (the rule this audit says is not being applied at fix time):
> the C# phase's `if (-not (Test-Path $TEST_PROJ)) { Write-Info "Test project not found, skipping" }`
> is the same defect class — the csproj is checked into the repo, so its absence means a broken tree,
> and it left a green exit code with **zero C# tests run**. Now `Write-Fail` + `$exitCode = 1`.
>
> **PowerShell trap worth keeping:** moving the runner into a function changes what `& $exe.FullName`
> does — a native command's stdout joins the *pipeline*, so the test's own output would have been
> captured into the function's return value and every call would read as truthy. Piped to `Out-Host`;
> `$LASTEXITCODE` is unaffected. (The `Write-*` helpers were checked first — all use `Write-Host`.)
>
> ✅ **Verified by negative control, not by inspection** — mandatory here, since the finding *is* "a
> check that cannot fail":
> 1. **Positive control**: `-Target Test` on the clean tree → `[OK] utf8_helpers_test passed`,
>    `[OK] dll_helpers_test passed` (1029 assertions), 3724 C# passed, `Status: SUCCESS`, **exit 0**.
> 2. **Negative control**: appended a deliberate syntax error to `dll/tests/utf8_helpers_test.cpp` →
>    `[FAIL] utf8_helpers_test FAILED TO COMPILE - the C++ suite did not run (this is a build failure,
>    not a skip)`, `Status: FAILED`, **exit 1**. The pre-fix code emitted `Write-Info "…(skip — run
>    -Target DLL or All first)"` and **exit 0** in exactly this state. `dll_helpers_test` still ran and
>    passed in the same invocation — one broken target must not hide the other's result.
> 3. Source restored (`git status` clean for that file); re-ran → green, exit 0.
>
> CI inherits the fix with no workflow change: `ci.yml:142` / `release.yml:70` already gate on
> `build.ps1`'s exit code, which is what was wrong — the script was reporting success.
>
> **Scope note:** `build.ps1` is not one of T1b's ten files. It is recorded here because it is the
> **sole enforcement path** for the two files that *are* in scope — a property of the suite under
> audit, not a stray finding. Two in-repo comments assert the opposite contract in writing
> (`dll/CMakeLists.txt:503-504` "a non-zero exit fails the test phase";
> `dll/tests/utf8_helpers_test.cpp:8-9` "any failure short-circuits the test phase") — both describe
> the **run** path, and the **build** path has no failure channel at all. Root cause #6 again.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **AD1** ✅ | HIGH | `build.ps1:666` (Unit Tests block — utf8_helpers_test / dll_helpe) | A C++ test target that fails to COMPILE is reported as "skip", not as a failure — the whole C++ suite can go silent, including in CI **[2 lenses]** | S / low |
| **AD2** ✅ | HIGH | `build.ps1:692` (Test phase — dll_helpers_test / utf8_helpers_tes) | A C++ test target that FAILS TO COMPILE is reported as "skip" and leaves exitCode 0 — the whole C++ suite silently stops running, in CI too | S / low |
| **AD3** | MED | `Genau.cpp:1120` (ScanForTarget — hint phase `sorted.erase(it)`) | The cached-hint fast path inspects only the FIRST match, then deletes the pattern from the full scan | S / low |
| **AD4** | MED | `Renge.h:106` (Renge::CMD_GET_PROTECT_STATE / UE5_GetProtectSta) | The GodMode want/live/resolvable command is fully shipped in the DLL and has zero clients; the UI reads the live-only proxy instead | S / low |
| **AD5** | MED | `Utf8Helpers.h:324` (DecodeFStringBuffer (KNOWN RESIDUAL)) | The header's own "KNOWN RESIDUAL" mis-decodes an ASCII FUtf8String as UTF-16 and produces a CJK glyph — and no test pins the boundary it admits | M / low |
| **AD6** | MED | `dll_helpers_test.cpp:243` (Test_Mimic_PollLatency_OneMillisecond) | The mailbox poll-latency test exercises ZERO Mimic code — it re-implements the mechanism inside the test process, while its own comment claims it "covers the actual mechanism" **[2 lenses]** | M / low |
| **AD7** | LOW | `Frieren.h:5` (Frieren.h file header comment (and Fern.h:5)) | The two headers the CI derived-count gate DERIVES FROM carry stale counts of their own contents, and are the only claim sites the gate does not check | S / low |
| **AD8** | LOW | `Frieren.h:84` (UE5_CallProcessEvent) | The exported invoke's documented error table stops at -4, omitting -5/-7/-8 — including the one code that means "do not free your params buffer" | S / low |
| **AD9** | LOW | `Genau.h:174` (Genau::FindGEngineSlot) | The declaration names bOffsetsValidated as the enforced precondition; the code gates on bOffsetsProbeRan, and Grimoire.h records that swapping to the strict flag regresses &GEngine | S / low |
| **AD10** | LOW | `Himmel.h:209` (IsCeReplayableAob) | IsCeReplayableAob approves RipDeref and the deref half of RipBoth, which the published (pattern, pos, len) triple cannot express | M / low |
| **AD11** | LOW | `Himmel.h:1552` (Sig::GOBJECTS_PATTERNS GOBJ_DI427_1 / Sig::GWORL) | Two table entries can never win: a broader sibling with identical resolve geometry sits at a lower priority number and matches everywhere they do **[2 lenses]** | S / low |
| **AD12** | LOW | `Himmel.h:1581` (GOBJECTS_PATTERNS[] entry "GOBJ_PS1") | GOBJ_PS1's instrOffset points 2 bytes inside its LEA, so every match resolves to garbage | S / low |
| **AD13** | LOW | `Himmel.h:1611` (GOBJECTS_PATTERNS[] entry "GOBJ_PS6") | GOBJ_PS6's instrOffset names the displacement, so the arithmetic anchor never resolves | S / low |
| **AD14** | LOW | `Himmel.h:1808` (GWORLD_PATTERNS[] entry "GWLD_G427_2") | GWLD_G427_2 is strictly subsumed by GWLD_SF_4 at a lower priority number, so it can never be reached | S / low |
| **AD15** | LOW | `Himmel.h:1814` (Sig::GWORLD_PATTERNS — GWLD_TQ_3 / GWLD_TQ_4) | GWLD_TQ_3 and GWLD_TQ_4 put the DISPLACEMENT offset (3) in the instrOffset field, so both resolve to a garbage address — and TQ_4 can publish it as &GWorld **[2 lenses]** | S / low |
| **AD16** | LOW | `Himmel.h:1815` (GWORLD_PATTERNS[] entry "GWLD_TQ_4") | GWLD_TQ_4 resolves off the displacement instead of the instruction, and its allowNull flag lets the resulting arbitrary address validate | S / low |
| **AD17** | LOW | `Himmel.h:2019` (ASSERT_TABLE_ORDER / the compile-time table inva) | Nothing validates a signature's (instrOffset, opcodeLen, totalLen) against its own pattern text — the blocktest oracle covers 35 of 158 entries | S / low |
| **AD18** | LOW | `Lugner_Dinput8.cpp:33` (LoadRealDinput8) | GetSystemDirectoryW's return value is discarded and the path is built with the unbounded wsprintfW — on failure the proxy LoadLibraryW's a drive-root-relative "\dinput8.dll" | S / low |
| **AD19** | LOW | `Renge.h:215` (Renge::StrToAddr) | Renge ships a strict and a lenient address parser with a documented preference; the strict one is used at 8 sites and the lenient one at 22, including write_mem's own address | M / low |
| **AD20** | LOW | `Utf8Helpers.h:34` (Utf8Helpers::Sanitize / the header's published c) | Utf8Helpers is the repo's declared defence against nlohmann's strict validator yet offers no LENGTH-BOUNDED variant, and its test file has zero truncation tests — which is exactly how U7 shipped **[2 lenses]** | M / low |
| **AD21** | LOW | `dll_helpers_test.cpp:3363` (Test_Routine_SafeThread) | The SafeThread test detaches a thread that keeps writing to a stack local after the frame is gone, corrupting the three tests that run after it | S / low |
| **AD22** | INFO | `Fern.h:5` (Fern (file banner)) | The Fern.h banner still says "~30 commands" against a real 99 — the seventh stale copy the derived-count CI gate was built for, and the gate does not cover source files | S / low |
| **AD23** | INFO | `Himmel.h:156` (file-header UE 5.8 note ("back into a base ancho) | The header says the -0x14 and +0x0C adjustments produce an FUObjectArray base anchor; the entries themselves say they land on ObjObjects | S / low |
| **AD24** | INFO | `Renge.h:268` (Renge::MakeResponse / MakeError / MakeEvent / Ad) | Enumerated coverage gaps in the in-scope headers — including the pipe response envelope, which is structurally UNLINKABLE from the only test target that includes it | M / low |
| **AD25** | INFO | `Utf8Helpers.h:236` (IsWellFormedUtf8) | hasMultiByte is left true when the function returns false, and nothing documents that the out-param is only meaningful on a true return | S / low |
| **AD26** | INFO | `utf8_helpers_test.cpp:204` (Test_EncodeUtf16_OutputAlwaysValidUtf8 / Test_De) | Two test comments describe coverage the tests do not provide — one input is not the pathological case it is named for, the other's deliberate setup is inert | S / low |
| **AD27** | INFO | `utf8_helpers_test.cpp:358` (Test_Decode_Utf8CjkWithTrailingHeapBytes) | The test's deliberately-non-zero UTF-16 terminator bytes are unreachable setup, and its comment is contradicted by the test 80 lines below | S / low |

> **Hand-verified:** AD1/AD2 in full, including running the negative control. **Not re-derived:** the
> 4 MEDs, 15 LOWs and 6 INFOs — 21% kill rate, below the 33–73% band, so re-derive before fixing.

-----

### T1d — UI Services — ✅ scanned 2026-08-15

**11 agents** (5 lenses → 3 refute batches → 3 second-lens batches), 0 errors, over 12 files /
1,975 early lines. **30 raw → 26 distinct** (4 folded in-script) **→ 9 refuted → 0 killed by the
second lens → 17 confirmed. Kill rate 35% — the FIRST phase to land inside the audit's 33–73% band.**

**Tally: 0 HIGH · 2 MED · 10 LOW · 5 INFO.** Both MEDs were re-derived by hand.

> ### 🔁 AC2 is the FIFTH time this audit has found its own fix half-applied
>
> `ResolveTrustedLayout` — the **Y7 fix, shipped in build 2881** — encodes the rule *"the engine's
> reported size overrules the version-guessed layout"*. Verified by hand: it is defined at
> `InvokeParamDialog.cs:1065` and has **exactly one call site** (`:328`), while **four** other files
> consume `KnownStructLayouts` — `StructReturnDecoder.cs`, `ParamBufferBuilder.cs`,
> `FunctionInfoModel.cs`, and the table itself. The decoder that renders an invoke's **return value**
> therefore still trusts a layout keyed on a detected UE version without checking it against the size
> the engine reported — the exact defect Y7 fixed on the input side.
>
> The running tally of this shape: **V2** (one side of the wire), **W4/W6** (some call sites),
> **X1** (one of two twins), **Y16** (one of three sites), **AC2** (one of five consumers). The §3b
> rule "before closing any fix, grep for its siblings" exists because of the first four — and the
> fifth was found anyway, which says the rule is not being applied at fix time. **Treat "where else
> does this predicate belong?" as part of the fix, not part of the next audit.**

> ### AC1 — a per-operation consent stored as a durable global, over a file we can name
>
> `ProxyDeployService.cs:916` is the only refusal protecting a foreign DLL:
> `if (File.Exists(targetDll) && !IsOurProxyDll(targetDll) && !force)`, and `:955` is
> `File.Copy(sourceDllPath, targetDll, overwrite: true)` — no backup, no Recycle Bin. Verified that
> `force` is **persisted**: `UiOptionsSettings.cs:217`, saved at `MainWindowViewModel.cs:2581`,
> restored at `:2437`. So a checkbox ticked once to push our proxy onto **one** game survives restarts
> and then applies to a **Select All → Deploy** batch, silently replacing every third-party
> `dxgi.dll` (ReShade, Special K, Ultimate ASI Loader) with no way back.
>
> Root cause #1, and unusually clean: `RefreshDeployStatusAsync` **already computed the identity of
> the file at risk** — it reads `FileVersionInfo.GetVersionInfo(targetDll)` and puts
> *"Other proxy: {ProductName}"* on the row — while the action path 60 lines later treats the same
> target as an unnamed blank, and `:956` logs only the destination. The report knows the name; the
> action does not use it.
>
> Held at **MED, not HIGH**, deliberately: the write is the literal function of a checkbox the user
> ticked at least once. The defect is *stale, global consent*, not an unrequested destructive act.
> Note the contrast the same file sets for its **leftover-cleanup** feature — dry-run report, confirm
> dialog listing every path, refusal on a volume with no Recycle Bin, and `MoveToRecycleBin` rather
> than unlink — a far higher bar for a file it is *more* confident is ours.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **AC1** | MED | `ProxyDeployService.cs:916` (ProxyDeployService.DeployAsync) | A persisted global "Force Overwrite" silently destroys a third party's DLL that the grid is simultaneously naming on screen | S / low |
| **AC2** ✅ | MED | `StructReturnDecoder.cs:55` (StructReturnDecoder.Decode / StructReturnDecoder.C) | Audit #5's own Y7 fix (ResolveTrustedLayout) is applied at **1 of 4 `GetLayout` call sites** (recounted 2026-08-15; unguarded: `InvokeParamDialog.cs:693`, `StructReturnDecoder.cs:55` + `:79`) — the invoke dialog refuses a size-contradicted layout for the INPUT boxes and accepts it for the RESULT grid **[2 lenses]** | S / low |
| **AC3** | LOW | `AobMakerBridgeService.cs:470` (AobMakerBridgeService.ReconnectAsync) | ReconnectAsync's bare `catch` swallows every connect failure with no logging, so all seven "AOBMaker not connected" outcomes reach the user with zero diagnostics | S / low |
| **AC4** | LOW | `AobUsageService.cs:121` (AobUsageService.LoadFileAsync) | A corrupt usage file is silently replaced with an empty one, wiping every other game's cached scan record — while the DELIBERATE reset of the same file keeps ten numbered backups | S / low |
| **AC5** | LOW | `AobUsageService.cs:124` (AobUsageService.LoadFileAsync / RecordScanAsync) | A corrupt cache file is answered by writing a one-game file over it — every OTHER game's user-set UE-version override and invoke timeout are destroyed, with no backup, from a Warn nobody reads | S / low |
| **AC6** | LOW | `AobUsageService.cs:139` (AobUsageService.SaveFileAsync) | The only atomic-write site in Services with no stale-temp cleanup, and its PID-suffixed name means the residue is unbounded | S / low |
| **AC7** | LOW | `ClassLocationScorer.cs:56` (ClassLocationScorer.FunctionBonuses / PropertyRule) | Two table comments claim the class bonus is "keyword-gated" and that "Player" is a fallback for classes missing the specific tokens; neither gate exists, and Player+Character = exactly the interesting threshold | M / med |
| **AC8** | LOW | `ClassLocationScorer.cs:163` (ClassLocationScorer.PropertyRules / FunctionBonuse) | Two rule blocks justify themselves as "keyword-gated"; neither scorer implements any gate | S / low |
| **AC9** | LOW | `ClassLocationScorer.cs:189` (ClassLocationScorer.PropertyRules — UCheatManager ) | The `UCheatManager` rule can never fire (UE class names carry no U/A prefix), and the comment justifying the row beneath it names an example that rule does not match either | S / low |
| **AC10** | LOW | `PipeClient.cs:187` (PipeClient.SendAsync) | The `!IsConnected` disjunct in the write-failure filters fires ONLY on unexpected pipe death, and converts it into a token-less OperationCanceledException — the exact case the file's own comment says must be an IOException | S / low |
| **AC11** | LOW | `ProxyDeployService.cs:955` (ProxyDeployService.DeployAsync) | Deploy writes straight over the live target with no staging, and a half-written proxy is then invisible AND unremovable to both of this file's own removal paths | S / low |
| **AC12** | LOW | `VdfParser.cs:141` (VdfParser.ExtractPaths) | The token stream carries neither key/value position nor nesting validity, so a value that reads "path" injects a fake library and any brace imbalance silently yields zero libraries | M / low |
| **AC13** | INFO | `PipeClient.cs:224` (PipeClient.SendAsync) | PipeTransportStats.Record sits in a finally that wraps only the response await, so a request failing in the WRITE contributes zero transport time — precisely the case its own comment says must be counted | S / low |
| **AC14** | INFO | `PipeClient.cs:240` (PipeClient.ReadLoopAsync) | ReadLoopAsync null-checks `_reader` as a field and then dereferences the field, the exact pattern SendAsync captures into a local at :140 to avoid — an NRE the loop's IOException/ObjectDisposedException catches do not cover | S / low |
| **AC15** | INFO | `ProxyDeployService.cs:420` (ProxyDeployService.TryDetectUeVersion) | `TryDetectUeVersion` reads every game exe's full VERSIONINFO resource and unconditionally discards it; the property it feeds is never read by anything **[2 lenses]** | S / low |
| **AC16** | INFO | `ProxyDeployService.cs:1608` (ProxyDeployService (leftover-proxy cleanup region)) | NEGATIVE RESULT — CLAUDE.md's three leftover-cleanup invariants all hold as written; do not re-audit this surface without new evidence | S / low |
| **AC17** | INFO | `WindowsPlatformService.cs:790` (WindowsPlatformService.VolumeHasRecycleBin) | The fixed-drive pre-filter on the Recycle-Bin gate answers about the HOST volume — `new DriveInfo(root)` re-runs the exact `Path.GetPathRoot` lookup the comment three lines above says it avoids | S / low |

> **Hand-verified:** AC1 and AC2 (both MEDs). **Not re-derived:** the 10 LOWs and 5 INFOs — though at
> a 35% kill rate this is the best-vetted phase so far.

-----

### T1a — Radar value-scan engine + Methode/Heiter entry points — ✅ scanned 2026-08-15

**11 agents** (5 lenses → 3 refute batches → 3 second-lens batches), 0 errors, over 4 files /
1,504 early lines. **35 raw → 30 distinct** (5 lens-duplicates folded **in-script**) **→ 6 refuted
→ 1 killed by the second lens → 23 confirmed. Kill rate 23%.**

**Tally: 2 HIGH · 5 MED · 12 LOW · 4 INFO.**

> ### ✅ The harness fix worked — 11 agents where S1 spent 31
>
> S1 cost 31 agents because refute/second-lens batches scale with **claims found**, not lines read
> (65 claims → 25 batches). Two changes: **merge by location in the SCRIPT** rather than asking the
> model (S1 marked zero duplicates while 14 locations were multi-lens; here 5 folded automatically,
> *before* the expensive stages), and batches 6→10 / 4→8. Same rigour, **a third of the agents**,
> and the kill rate rose 14% → 23%. **Do this in every remaining phase.**

> ### 🔴 AB1 is the most consequential finding of this audit — hand-verified in full
>
> **Our DLL crashes Cheat Engine, the user's primary tool, on a documented install path.**
>
> `DllMain(DLL_PROCESS_ATTACH)` unconditionally starts a **1 ms-poll thread** — `Heiter.cpp:274`
> `Mimic::StartThread();`, comment *"Runs in both proxy and inject modes"* — plus the auto-start
> thread at `:279`. Neither is conditional on the host process, and CE loads this DLL as a **plugin**.
>
> I verified every link myself rather than taking the agent's word:
> - **`Heiter.cpp:190` declares `DllMain(HMODULE, DWORD, LPVOID /*reserved*/)` — the parameter is
>   COMMENTED OUT.** So `DLL_PROCESS_DETACH` structurally *cannot* distinguish a `FreeLibrary`
>   unload from process exit; both hit the same `break;`. Its own comment claims *"Only the implicit
>   process-exit DETACH is a no-op"* — a distinction the signature makes impossible to draw.
> - **`Fern.cpp:533-537` asserts the case away**: *"The only case this gives up on is FreeLibrary of
>   this DLL with the process still alive … **Nothing in this repo does that** … and Heiter.cpp's
>   no-op DETACH already relies on the same fact."* Two modules resting on one unverified premise.
> - **CE's own source refutes it**: `D:/Github/cheat-engine/Cheat Engine/plugin.pas:1525` is
>   `freelibrary(hmodule);`. And this repo's *own mirrored doc* already records the cycle —
>   `docs/ce-plugin-api-reference.md:95-102`, *"TRAP — CE opens your DLL twice, and throws the first
>   pass away … your DllMain(PROCESS_ATTACH) and GetVersion run once against a module that is
>   immediately unloaded."*
> - **Nothing pins the module**: `grep GET_MODULE_HANDLE_EX_FLAG_PIN dll/src/` = **0 hits**.
> - **The guard exists and is applied at the wrong site**: `Grimoire::IsCheatEngineExeName` has
>   exactly **one** call site in the entire DLL — `Heiter.cpp:158`, *inside the auto-start thread
>   body*, i.e. inside the very thing it should have prevented from being created.
> - **`CEPlugin_DisablePlugin` (`Methode.cpp:386`) calls only `UnregisterFunction`** — it never stops
>   the poller.
>
> Net: add the plugin via Settings→Plugins→Add and CE does LoadLibrary → GetVersion → **FreeLibrary**,
> unmapping the image while a 1 ms thread lives in it. It is also every CE *exit*, via
> `FormClose → pluginhandler.free → UnloadPlugin → FreeLibrary`, which runs **before** CE writes its
> settings — so the crash also loses that session's CE settings. Fix is two small guards: call the
> existing `IsCheatEngineExeName` on the host before creating threads, and pin the module. **Do not**
> try to join threads from DETACH — the existing comment is right that that deadlocks.
>
> This is root cause **#6** in its sharpest form to date: a comment asserting an impossibility,
> contradicted by a source **mirrored into this same repo**. Every time this audit has followed such
> a comment it has found a real defect — now 4 for 4.

> **The other root causes, unchanged:** #5 the width family reaches Radar — AB3/AB5 hardcode a
> 12-byte 3×float `FVector` while accepting UE5 LWC's **24-byte double** struct (the exact shape that
> cost W6/Y2/Y15/Y16); #7 raw addresses as identity (AB7); #1 the report and the reality by different
> paths (AB6: the group grid **sorts** by `slotMatches[0][0]` while it **displays**
> `slotMatches[0][picks[0]]`).

> ### ✅ AB2 FIXED — build 2932, 2026-08-15 — **AUDIT #5 HAS NO OPEN HIGHS LEFT**
>
> **The mechanism, re-read in CE's own source and sharper than the register recorded it.**
> `CEFuncProc.pas:1346-1360` waits `counter := 10000 div 10` × 10 ms — a hard, unconfigurable **10 s**;
> `:1332-1343` (Settings' `cbInjectDLLWithAPC`) does not wait at all, just `CreateRemoteAPC` then a flat
> `sleep(1000)`; and `:1379-1387` `finally ... virtualfreeex(..., MEM_RELEASE)` runs **unconditionally on
> both paths**. So the page our function is executing on is released while it runs, and the `ret`
> lands on freed memory — in the **GAME**, not CE. *(CE's own timeout string even claims "Injection
> routine not freed", which that `finally` contradicts.)*
>
> **Fixed as the register proposed: `UE5_AutoStart` spawns and returns.** The work moved to
> `AutoStartWork()`, reached through two entry points sharing a one-at-a-time latch —
> `UE5_AutoStart()` (spawns, exported) and `UE5_AutoStartBlocking()` (inline, **not** exported, for
> `DllMain`'s auto-start thread, which already owns a thread). Readiness was already published via
> `Mimic::InitState` and the emitted scripts already poll it (`CeReadinessLua::AppendPollLoop`), so no
> caller lost information — and the Lua inject path never passed a function name to `injectDLL` in the
> first place, i.e. **only our own plugin was taking the dangerous route.**
>
> Two details the fix had to get right:
> - **In a Cheat Engine host it runs INLINE, no thread** — AB1's rule (CE `FreeLibrary`s plugin DLLs).
>   There is no remote stub to outrun in-process, so there is nothing to gain and a crash to lose.
> - **The latch is real, not theoretical.** In the ordinary plugin flow `LoadLibrary` spawns DllMain's
>   auto-start thread *and* CE's stub then calls the export — two full scans that were previously
>   survived only incidentally (`UE5_Init`'s `s_initialized` plus a pipe-exists probe).
>
> **A second defect was found while fixing this, in the half the register only called "misleading".**
> `ce_InjectDLL` (`pluginexports.pas:622-640`) returns false *only if an exception escapes*, and CE's
> own handler swallows one of the three it can raise (`CEFuncProc.pas:1050-1051`, `1391-1396`):
>
> | CE outcome | Exception class | `InjectDLL` returns |
> |---|---|---|
> | injection thread > 10 s | plain `Exception` | **false** |
> | "Failed executing the function of the dll" | `EInjectDLLFunctionFailure` — a **sibling** of `EInjectError`, not a subclass, so `on e:EInjectError` misses it | **false** |
> | "Failed injecting the DLL" | `EInjectError` → caught, falls back to `forceLoadModule` | **true** |
>
> So the BOOL is **true on a real injection failure** and **false while the DLL is loaded and working**
> — our dialog was not merely worded badly, it was reading an inverted signal. `OnInjectAndConnect` now
> **decides by looking**: it re-runs the same module-list walk it already uses for the
> already-loaded check (with a short retry, because the APC path does not wait on the loader) and
> reports what is actually mapped, naming CE's unreliable result when the two disagree.
>
> ### Verified by measurement + negative control
>
> `UE5_AutoStart` is behaviour, not shape, and **no test target compiles `Frieren.cpp`**. So
> **`tools/probe_autostart_async.py`** (stdlib-only) loads the shipped DLL, times the export, and reads
> `InitState` at the instant it returns:
>
> | build | elapsed until return | `initState` at return | verdict |
> |---|---|---|---|
> | fixed | **2.3 ms** (1.0 ms on a re-run) | 0 IDLE — work not started | ASYNC CONFIRMED |
> | spawn reverted | **3486.5 ms** | 2 READY — work already done | STILL BLOCKING |
>
> The 3.5 s is in a **Python host with no game at all**; a real UE AOB scan is 2-8 s, i.e. squarely
> inside CE's 10 s ceiling and always past the APC path's 1 s. Export table checked against the
> **shipped artifact**, not the source: `UE5_AutoStart` exported, `UE5_AutoStartBlocking` correctly
> **not** (64 exports).
>
> The probe is a manual tool, not a build step — it loads the DLL into the running process, starts
> real workers and opens the pipe, so it wants a throwaway process. (And a build step that skips when
> its precondition is missing is the AD1 defect.)
>
> ⚠ **Not verified against a real Cheat Engine + game.** The measurement proves the export returns in
> time; it cannot prove CE is happy. See todo.md's register — the AB1 entry already asks for a real CE
> session and this rides along with it.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **AB1** ✅ | HIGH | `Heiter.cpp:274` (DllMain (DLL_PROCESS_ATTACH) → Mimic::StartThread) | DllMain starts a 1 ms-poll thread in EVERY host, including Cheat Engine — and CE FreeLibrary's plugin DLLs, so the thread runs on after the image is unmapped | S / low |
| **AB2** ✅ | HIGH | `Methode.cpp:307` (OnInjectAndConnect) | InjectDLL is handed the multi-second AOB scan as `functiontocall`; CE frees the remote stub out from under the still-running thread **[2 lenses]** | S / low |
| **AB3** | MED | `Radar.cpp:288` (VectorStructNames / SizeOf / CompareVectorPredicate) | FVector/FRotator scan hardcodes a 12-byte 3xfloat layout but accepts UE5's 24-byte LWC "Vector"/"Rotator" structs, so every UE5 game's vector scan compares junk | M / med |
| **AB4** | MED | `Radar.cpp:508` (Radar::BuildNumericTargets) | The width-fit gate is right for Exact and wrong for the ordered predicates: fields whose entire range satisfies Smaller/Bigger are silently skipped | M / low |
| **AB5** | MED | `Radar.cpp:797` (Radar::CompareVectorPredicate) | The FVector/FRotator scan is hardcoded to 3×float / 12 bytes, so it reads junk on every UE5 (LWC double) game — while the reflected 24-byte size is captured and thrown away | M / med |
| **AB6** | MED | `Radar.cpp:1441` (BuildGroupOrderedView::slot0Num / slot0Offset) | Group sort keys read slotMatches[0][0] while the row displays slotMatches[0][picks[0]] — the grid orders by a leaf the user cannot see | S / low |
| **AB7** | MED | `Radar.h:384` (Radar::Candidate / Radar::InstanceRecord) | A scan session's candidate addresses are raw addresses used as IDENTITY across RPCs; the GObjects index is captured but never validated and no serial witness is stored | M / med |
| **AB8** | LOW | `Heiter.cpp:41` (g_hAutoStartThread) | The auto-start thread handle is stored, never waited on and never closed; two comments assert a join that no code performs | S / low |
| **AB9** | LOW | `Heiter.cpp:234` (DllMain → Sein::Init / Sein::InitProcessMirror) | DllMain does shell32 + unbounded recursive filesystem work (two directory sweeps and possibly a multi-GB remove_all) inline, under the loader lock | M / med |
| **AB10** | LOW | `Heiter.cpp:268` (DllMain (proxy mutex diagnostic)) | The unarmed-guard warning prints a GetLastError() captured hundreds of API calls after the failure it claims to report | S / low |
| **AB11** | LOW | `Heiter.cpp:268` (DllMain (UE5_PROXY_BUILD unarmed-mutex warning)) | The B47 diagnostic logs a GetLastError() captured after ~6 intervening Win32/CRT calls, so the error code it blames CreateMutexW for is not CreateMutexW's | S / low |
| **AB12** | LOW | `Methode.cpp:211` (IsAlreadyLoadedInTarget) | The 1024-entry module array is clamped correctly but its truncation is invisible, and the consequence is no longer just a wasted map | S / low |
| **AB13** | LOW | `Methode.cpp:293` (OnInjectAndConnect) | The one path whose value is functional still uses GetModuleFileNameA — the fix applied at both display sites in this same file was not applied here **[3 lenses]** | S / low |
| **AB14** | LOW | `Radar.cpp:335` (Radar::TryDataTypeFromPropertyTypeName) | EnumProperty is absent from the numeric type map, so enum-backed state fields are invisible to every value scan although the DLL resolves their width correctly elsewhere | M / low |
| **AB15** | LOW | `Radar.cpp:463` (Radar::BuildNumericTargets) | A leading zero makes one user value mean two different numbers inside a single meta scan: octal for the integer widths, decimal for Float/Double | S / low |
| **AB16** | LOW | `Radar.cpp:996` (CandidateMatchesFilter) | The server-side Value Search filter does not cover the displayed "Origin" column, so filtering for `native` reports zero matches on rows that visibly read "Native-C (Int32)" | S / low |
| **AB17** | LOW | `Radar.cpp:1154` (Radar::SessionManager::ExpireOldSessions / GroupSess) | The documented 300 s idle expiry is reachable ONLY from `Begin` on the same manager, so it never fires for the case the header says it protects against — a client that goes quiet | S / low |
| **AB18** | LOW | `Radar.cpp:1353` (PickGroupWitnessAssignment) | Greedy witness picker can show the same leaf in two slots, asserting a pairing the group-scan contract forbids — while Orden already computed a valid assignment and threw it away | M / low |
| **AB19** | LOW | `Radar.h:687` (Radar::GroupCandidate::slotMatches / GroupSessionMan) | A group session's leaf memory is the unbounded product of three caps and only one of them is bounded against memory; the clamp that exists names the hazard it does not cover | M / low |
| **AB20** | INFO | `Heiter.cpp:278` (g_hAutoStartThread) | Two comments claim shutdown waits for the auto-start thread via a stored handle; no code anywhere waits on it, and the handle is file-static so UE5_Shutdown cannot reach it | S / low |
| **AB21** | INFO | `Methode.cpp:352` (CEPlugin_InitializePlugin / CEPlugin_GetVersion / Ex) | Negative result — the static ABI surface itself is correct; no Radar/Radar.h finding from this lens | S / low |
| **AB22** | INFO | `Radar.h:80` (DataType (enum banner comment)) | The DataType enum's banner says TArray<T> scan "remains deferred" 28 lines after the file header says it shipped in build 757 | S / low |
| **AB23** | INFO | `Radar.h:676` (Radar::GroupSlotMatch::ownerClass) | `GroupSlotMatch` carries a by-value `std::string` per leaf, re-introducing exactly the per-record heap string that V3-A's interning was built to remove | S / low |

> **Hand-verified:** AB1 in full (above). **Not re-derived:** everything else — the 23% kill rate is
> better than S1's 14% but still below the 33–73% band, so re-derive before fixing.

-----

### S1 — Early Lua scripts — ✅ scanned 2026-08-15

**31 agents** (5 lenses → 11 refute batches → 14 second-lens batches), 0 errors, over 3 files /
1,668 lines — **1,236 of them early, the densest early-code concentration in the tree at 74%**, and
with **zero test coverage of any kind**.

**65 raw claims → 7 refuted → 2 killed by the second lens → 56 survived → 36 distinct** after merging
by location. **Corrected tally: 3 HIGH · 17 MED · 14 LOW · 2 INFO.**

> ### ⚠ The kill rate fell again — 14%, and this time the skeptic prompt was the STRICTEST yet
>
> U5 refuted 20% and was recorded as lenient, so S1's skeptic was explicitly constrained: it could
> only refute by filling a `counter_input` field naming the guard or documented behaviour that makes
> the scenario impossible, and the second lens ran on **every** survivor rather than HIGH/MED only.
> **It refuted less: 7/65 = 11%, plus 2 second-lens kills = 14% total**, against the audit's 33–73%
> band. Tightening the rubric did not tighten the skeptic.
>
> **And it marked ZERO duplicates while 14 locations were reported by more than one lens** — the one
> mechanical job it was given, with U5's five missed pairs quoted at it as the reason. Merging by
> location alone takes 56 → 36. Treat `duplicate_of_idx` as **structurally unreliable** in this
> harness and merge by hand; do not spend prompt budget asking for it again.
>
> **What I hand-verified, and what changed.** The finders returned **6 HIGHs** — extraordinary against
> a history where ten of eleven claimed HIGHs died. Merging by location gives 5, and re-deriving each
> against the source gives **3**:
> - `writeBool:153` — **CONFIRMED HIGH.** The code's own comment says it: *"We do NOT support packed
>   bitfield bools … generating a freeze script for one will overwrite the whole byte, clobbering
>   sibling bools."* The DLL sibling reached from the **same Property Search row** (Solitar's Force)
>   does a masked read–modify–write via `ApplyBoolBit`. One row, two actions, opposite correctness.
> - `:452` / `:461` — **CONFIRMED HIGH** (one defect, two lenses). True by construction: the guard
>   tests *"is there a non-zero qword at addr"*, not *"is addr still the object I enumerated"*, so a
>   recycled slot **cannot** trip it. The second lens earned its place here by killing the finder's
>   *"writes past the object"* sub-claim on MallocBinned same-size-bin grounds while keeping the
>   finding.
> - `fillGaps:289` — **DOWNGRADED to MED.** Verified dead (one `grep` hit: the definition) and the
>   header does advertise it at `:15`, but a dead feature plus a lying header is not a wrong write.
> - `start:474` — **DOWNGRADED to MED.** Verified: `_lastError` is written at `:462`/`:467` and read
>   **nowhere**, and `start()` discards `rescan()`'s outcome, so both timers arm over an empty cache
>   and the script proceeds to its auto-close. Real, but a failure-to-act, not a wrong act.
>
> **The low kill rate is not, by itself, evidence these findings are wrong.** S1's files are the
> densest early code in the repo, have no tests, and Y9 and Y15 both found real defects in this exact
> freeze path within the last two builds. A genuinely higher true-positive rate here is plausible.
> What the rate *does* mean is that **nothing below HIGH has been vetted to this audit's standard** —
> re-derive any MED or LOW before fixing it, exactly as for U5's Z-findings.

> **Root causes, and the freeze helper is where they concentrate** (18 of 36 findings):
> 1. **A raw pointer used as an identity.** AA2/AA3 are the audit's recycled-address hazard — already
>    recorded three times on the DLL side (D1/U4–U6, D3/A10, D5/F3) — reaching the Lua tier, where
>    there is no `GetSerialNumber` witness on the wire to fix it cheaply. *(✅ fixed 2926 — and the
>    resolution is worth carrying to the other three: the cheap witness was not object identity at
>    all, but the weaker predicate the FEATURE actually needs. Ask what the caller must not do before
>    reaching for a serial number.)*
> 2. **`callDLL` returns `nil` on failure and not one of its 14 call sites handles it** (AA5, AA6).
>    `nil ~= 0` is **true** in Lua, so a failed read is recorded as success — root cause #1 in its
>    purest form, and a Lua-specific trap that has no C# analogue.
> 3. **Root cause #6 again, and it is now the audit's most reliable predictor.** AA1's defect is
>    stated verbatim in its own comment; `fillGaps`' header advertises a feature that does not run.
>    Every time this audit has followed a comment admitting a limitation, it has found a real defect.

> ### ✅ AA1 FIXED — build 2922, 2026-08-15
>
> **The mask was absent end-to-end, so the fix is a five-tier wire-through, not a Lua edit.** The
> engine reported the FieldMask all along and every tier dropped it:
>
> | Tier | Change |
> |---|---|
> | DLL wire | `Fern.cpp` — both `search_properties` encoders now emit `bool_mask` when non-zero (single-query **and** batch; the value comes from the class field walk, not the preview pass, so the no-preview batch path carries it too) |
> | Model | `PropertySearchMatch.BoolFieldMask` + both `DumpService` parsers |
> | Row | `ScoredPropertyRow.BoolFieldMask` — forwarded, exactly like `PropSize` one line above it |
> | Params | `FreezeScriptParams.BoolFieldMask`, **`required`** — Y15's own template, and it earned its keep immediately (see below) |
> | Script | `FreezeScriptGenerator` emits `boolMask = 0xNN` into CFG **only** for a genuinely packed bool |
> | Lua | `writeBool(addr, v, mask)` does a masked read-modify-write; `tick` passes `cfg.boolMask` |
>
> **What decided the scope: no `ByteOffset` is needed, and that is verified rather than assumed.**
> The DLL sets `boolFieldMask` only after reading `FieldSize == 1` (`Ubel.cpp:662` and `:1044`, both
> conditions checked). A one-byte property has nowhere for a `ByteOffset` to point, so a row that
> carries a mask always has its bit in the byte at `prop_offset`. Consistently,
> `PropertyMatch::boolByteOffset` is **declared and never assigned anywhere in the tree** — dead, and
> now known to be harmlessly so.
>
> **`0xFF` is not a bit mask.** UE's `SetBoolSize` writes `FieldMask = 255` for a native bool, so
> both `0` (no mask reported) and `0xFF` must fall back to the whole-byte write. The accept set is
> exactly `{0x01,0x02,0x04,0x08,0x10,0x20,0x40,0x80}`, encoded identically in C#
> (`IsPackedBoolMask`) and Lua (`BOOL_BIT_MASKS`).
>
> **Reused three existing correct implementations instead of inventing a fourth rule.** The bit rule
> already existed at `Solitar::ApplyBoolBit` (C++), `FieldValueConverter.ApplyBoolMask` (C#, the Live
> Walker edit path) and `UE5T_setbit` (Lua, the standalone trainer). The last of those also supplied
> the *idiom*: **CE's Lua has no `bAnd`/`bOr`/`bNot`**, so the helper uses pure arithmetic
> (`math.floor(b / mask) % 2`), which is version-proof and writes only on drift. `readByte` /
> `writeByte` were verified against `celua.txt` before use, per CLAUDE.md.
>
> **A failed read must not fall through.** If `readByte` returns nil the tick returns without writing
> — falling through to the whole-byte write is the exact corruption the branch exists to prevent.
>
> **`required` caught a tier the plan had missed.** The build failed on
> `InterestingPropertiesViewModel` because `ScoredPropertyRow` forwards `PropSize` but not the mask —
> the same dropped-field shape, one row further down the same file. Left optional, that call site
> would have silently kept the bug on the batch-CT path.
>
> ✅ **Verified by negative control.** 24 new tests (3724 → **3748**, 0 failures). Then both halves of
> the fix were reverted — the CFG emission and the mask argument in `tick` — and the packed-mask
> theories **failed**, naming the exact 8 masks; restored, green again. Also asserted: a native bool
> emits **no** `boolMask`, `0xFF`/multi-bit/negative emit none, a non-bool type never emits one even
> when a stale mask is present, and the helper source no longer contains the comment
> *"We do NOT support packed bitfield bools"* — the comment that documented this defect as intended
> behaviour and is why it survived. (Root cause #6, one more time.)
>
> ⚠ **Not yet verified in-game.** The remaining risk is entirely in the DLL→UI direction: whether a
> real packed bool's mask actually arrives on the `search_properties` wire. Bench-checking it needs a
> game whose class has a `uint8 bFoo:1`.


> ### ✅ AA2 + AA3 FIXED — build 2926, 2026-08-15
>
> **The fix shape differs from the one this register recommended, deliberately.** The register said
> the honest fix needs an identity witness (`InternalIndex`, `SerialNumber`) so the tick can ask *"is
> this still the object I enumerated?"*. Re-deriving it against the feature's actual contract says
> that is the **wrong question**, and answering it would make the freeze less correct, not more:
>
> - This freeze is **class-wide by design** — it locks a property on *all* live instances of a class
>   and picks up newly spawned ones every rescan. So a slot recycled by **another instance of the same
>   class** is not a hazard: that object is a target too, and the next rescan would enumerate it. A
>   serial-number check would *refuse* that write, i.e. refuse to do the feature's job for 5 s.
> - The write that actually corrupts is into an object of a **different class**, where `propOffset`
>   addresses something else entirely.
>
> So the witness that matters is **class membership**, and it is also much cheaper: one `UClass*` and
> one offset are constant across the whole enumeration, so they ride in two previously-unused
> **output** fields (`instanceAddr`, `ufuncAddr`) instead of widening every 8-byte entry. The page
> size, the entry stride and the 128-per-page cap are all unchanged, which makes this **additive** —
> `MAILBOX_CONTRACT` 1 → **2**, `MAILBOX_CONTRACT_MIN` stays **1**, so every saved `.CT` from
> contract 1 keeps working.
>
> | Change | Where |
> |---|---|
> | Publish `instanceAddr` = enumerated `UClass*`, `ufuncAddr` = `OFF_UOBJECT_CLASS` | `Mimic.cpp` `HandleListInstances` |
> | Contract 1 → 2 (+ the rule for why it is additive) | `Mimic.h`, `CeMailboxLayout.cs`, `check_mailbox_contract.py` |
> | tick re-reads `ClassPrivate` and refuses a foreign class | `ue5_freeze_helper.lua` |
> | Bounded failure streak → drop the cache, stop writing, say so once | `ue5_freeze_helper.lua` `rescan()` |
> | `handle.lastError()` / `handle.isAbandoned()` | `ue5_freeze_helper.lua` |
>
> **Both fields are cleared before use.** An earlier command may have left a real `UObject*` /
> `UFunction*` there, and a caller must never mistake that for a witness — so the DLL zeroes them,
> and the Lua additionally range-checks the offset (`8 ≤ off ≤ 0x200`) so a leftover 64-bit address
> cannot masquerade as one. Getting this wrong fails *closed* (every write refused = a silent no-op),
> which is why it is checked twice.
>
> **AA3's "indefinitely" is now bounded at three consecutive failures** (~15 s at the default rescan
> interval). One failure is usually a transient `mailbox busy` and keeping the cache is right; the
> unbounded cases named in the re-verify note — DLL unloaded/re-injected, contract mismatch, a wedged
> `_ue5_invoke_busy` — never self-heal, and now stop the writes and print **once** (ungated: CE Lua
> hygiene keeps genuine failures unconditional). `_lastError` had three writers and zero readers; it
> has two readers now.
>
> ### The verification is the notable part: the helper is now EXECUTED, not just grepped
>
> A Lua 5.4 interpreter turned out to be available on the dev machine, so
> **`scripts/tests/freeze_helper_test.lua`** stubs the ~15 CE globals the helper touches (memory
> reads/writes, timers, symbol lookup) over a plain table, runs the real `freezeProperty` /
> `tick` / `rescan`, and asserts on **what was actually written**. That is the first executable test
> of any script in S1 — the segment the audit flagged as having none — and it covers AA1 as well.
>
> **23 checks, 0 failures.** Then each of the three fixes was reverted **one at a time**:
>
> | Reverted | Result |
> |---|---|
> | AA1 → unconditional whole-byte stamp | **DETECTED** — 4 failures |
> | AA2 → old vtable-only guard | **DETECTED** — 11 failures |
> | AA3 → keep the stale cache, silently | **DETECTED** — 6 failures |
>
> The first attempt broke all three at once and the results were **uninterpretable** — AA2's break
> made an AA1 case fail for an unrelated reason. One break at a time is the only version that proves
> anything. The same run also caught the harness aborting on its first failure and hiding every later
> case; it now uses safe accessors.
>
> **It is deliberately NOT wired into `build.ps1` or CI.** `lua` is not a declared dependency, and a
> test step that silently skips when its tool is missing is precisely the defect AD1/AD2 just fixed
> one commit earlier. Three C# tests act as the CI tripwire — they cannot prove the guard *works*,
> only that nobody deleted it, and one of them pins the helper's own `UE5_SCRIPT_CONTRACT` to
> `CeMailboxLayout.ContractVersion` so the hand-maintained copy cannot drift.
>
> **Residual, stated honestly:** a slot that is freed and *not* reused can keep its old class pointer
> until the allocator hands it out, so a write can still land in dead memory. The class check cannot
> see that; nothing cheap can. What it removes is the write into a *live object of another class*,
> which is the case that corrupts.
>
> ⚠ **Not verified in-game.** Needs a class whose instances are destroyed and respawned (combat
> deaths, level streaming) with a freeze active — see todo.md's register.

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **AA1** ✅ | HIGH | `ue5_freeze_helper.lua:153` (writeBool) | Bool freeze writes a WHOLE BYTE over an FBoolProperty bitfield — clobbers the sibling bits and sets the wrong bit **[2 lenses]** | M / med |
| **AA2** ✅ | HIGH | `ue5_freeze_helper.lua:452` (freezeProperty -> tick) | The freeze tick's liveness guard cannot detect a recycled UObject slot, so it writes 20x/sec into the wrong live object for up to 5 seconds *(re-measured 2026-08-15: ~16/s per cached address — TTimer 50 ms quantised to ~62.5 ms; 5 s is the BEST case — see §3c)* | M / med |
| **AA3** ✅ | HIGH | `ue5_freeze_helper.lua:461` (rescan / tick) | A failed rescan KEEPS the stale pointer cache, and tick's only liveness test is a non-zero vtable read — so it writes 20x/s into freed and recycled objects, indefinitely **[2 lenses]** *(2026-08-15: "indefinitely" holds under PERSISTENT failure — DLL re-inject / contract mismatch / wedged `_ue5_invoke_busy`; transient busy self-heals in 5 s; `_lastError` is write-only — see §3c)* | M / med |
| **AA4** | MED | `ue5_dissect.lua:53` (callDLL) | Bare getAddress RAISES on a missing symbol (CE source-verified), so the 'DLL function not found' message is dead code and the registered dissect override breaks CE's dissect for unrelated addresses **[2 lenses]** | S / low |
| **AA5** | MED | `ue5_dissect.lua:63` (callDLL) | callDLL returns nil on every executeCodeEx failure and not one of its 14 call sites handles nil: `<= 0` raises, `~= 0` inverts **[2 lenses]** | S / low |
| **AA6** | MED | `ue5_dissect.lua:173` (addFieldsToStruct / walkClassFields) | callDLL returns nil on failure and every caller treats nil as SUCCESS — `nil ~= 0` is true in Lua, so a failed field read is silently recorded as a duplicate of the previous field **[2 lenses]** | S / low |
| **AA7** | MED | `ue5_dissect.lua:289` (fillGaps) | "Gap filling" is advertised in the header but fillGaps is never called — and its coverage test would emit overlapping elements if it were **[2 lenses]** | S / low |
| **AA8** | MED | `ue5_dissect.lua:341` (addUObjectHeader) | UObject header offsets are hardcoded (Outer=0x20) while the DLL detects and switches them (DynOff::UOBJECT_OUTER = 0x28 on case-preserving-FName games) **[2 lenses]** | M / low |
| **AA9** | MED | `ue5_freeze_helper.lua:95` (header SAMPLES block) | The file's own copy/paste samples produce a freeze that cannot be stopped: `local h` in [ENABLE] is invisible to [DISABLE], and SAMPLES 1-3 show no stop at all | S / low |
| **AA10** | MED | `ue5_freeze_helper.lua:341` (fetchInstancePage) | The mailbox has two mutually-blind concurrency guards: generated scripts wait for cmd==IDLE, the standalone helpers use a Lua flag no generated script sets | M / low |
| **AA11** | MED | `ue5_freeze_helper.lua:386` (rescanInstances) | A page-fetch failure after page 0 is thrown away, so a PARTIAL instance list is returned as a clean success and replaces the freeze cache **[2 lenses]** | S / low |
| **AA12** | MED | `ue5_freeze_helper.lua:471` (freezeProperty -> handle.start) | A freeze that applied NOTHING reports clean success: start() cannot raise, so the generated script's pcall succeeds, the Lua window auto-closes and the CE record stays ticked | S / low |
| **AA13** | MED | `ue5_freeze_helper.lua:474` (handle.start) | Every freeze failure is silent: start() discards rescan()'s error, nothing reads _lastError, and the generated script then auto-closes the Lua window over a ticked record that froze nothing **[2 lenses]** | S / low |
| **AA14** | MED | `ue5_invoke_helper.lua:215` (writeFStringInline) | allocateMemory's nil return is unchecked, so a failed allocation ships an FString of { Data = nullptr, Num = n+1 } into the game **[2 lenses]** | S / low |
| **AA15** | MED | `ue5_invoke_helper.lua:223` (writeFStringInline) | allocateMemory's nil return is never checked, so a failed allocation stamps an FString with Data=nullptr and ArrayNum=n+1 into a live UFunction call | S / low |
| **AA16** | MED | `ue5_invoke_helper.lua:293` (writeParams) | BakedScriptGenerator can emit five param-type tokens writeParams has never accepted, so the exported script aborts the whole invoke at CE runtime | S / low |
| **AA17** | MED | `ue5_invoke_helper.lua:308` (writeBakedParams) | The params buffer is zeroed only up to the CALLER'S parmsSize while the DLL hands ProcessEvent all 1024 bytes — stale bytes from an earlier command become live parameters **[2 lenses]** | S / low |
| **AA18** | MED | `ue5_invoke_helper.lua:363` (waitDone) | A mailbox timeout reports the STALE errorMsg left by an earlier command as this command's reason — the guessed diagnosis CLAUDE.md forbids | S / low |
| **AA19** | MED | `ue5_invoke_helper.lua:464` (invokeUFunction) | The reentrancy flag is cleared on the timeout path — exactly when the DLL still owns the mailbox — so the next invoke scribbles on an in-flight command and is reported OK though it never ran | M / med |
| **AA20** | MED | `ue5_invoke_helper.lua:512` (readUFunctionReturn) | readUFunctionReturn decodes int32/int16 returns UNSIGNED, so a UFunction returning -1 reads as 4295067295 -- while the same file passes the signed flag two functions earlier **[2 lenses]** | S / low |
| **AA21** | LOW | `ue5_dissect.lua:23` (module state (structList / structCache)) | Module state is per-dofile while CE's structure list and Lua state are global and never rebuilt, so a re-load duplicates every structure and orphans the old ones | S / low |
| **AA22** | LOW | `ue5_dissect.lua:24` (dissect.enableAutoCallback) | The already-registered guard is a chunk-local, so a second dofile double-registers the dissect override and disableAutoCallback can only unregister the newest one | S / low |
| **AA23** | LOW | `ue5_dissect.lua:208` (addFieldsToStruct) | The struct-recursion depth cap returns silently, so a nested StructProperty deeper than 6 levels is simply absent from the dissect with no marker | S / low |
| **AA24** | LOW | `ue5_dissect.lua:222` (addFieldsToStruct) | A CreateRemoteThread round-trip plus a target-process allocation per StructProperty field, whose result is discarded — and it leaks if the call raises | S / low |
| **AA25** | LOW | `ue5_dissect.lua:243` (addFieldsToStruct) | A failed callDLL mid-walk raises with beginUpdate() unmatched, orphaning a CE structure that is never registered in structList and so can never be freed by clearAll() | S / low |
| **AA26** | LOW | `ue5_dissect.lua:244` (addFieldsToStruct (BoolProperty branch)) | The bitmask is stored in ChildStructStart, a CE property that means 'byte offset inside a child struct', not a mask **[2 lenses]** | S / low |
| **AA27** | LOW | `ue5_dissect.lua:522` (dissect.enableAutoCallback) | The CE-global dissect override is registered with no automatic unregister, and CE never rebuilds its Lua state — so after the DLL goes away it breaks CE's 'guess structure' for the rest of the session | S / low |
| **AA28** | LOW | `ue5_freeze_helper.lua:220` (checkContract) | An unreadable contract symbol is reported as "stale address — re-inject the DLL" when the process is simply gone, though the same file already knows how to say that | S / low |
| **AA29** | LOW | `ue5_freeze_helper.lua:303` (waitDone) | Lua `and`/`or` precedence keeps the documented-as-dormant iteration fallback permanently live, so the effective deadline is min(tick, iterations) and the printed "%dms" is not the arm that fired **[2 lenses]** | S / low |
| **AA30** | LOW | `ue5_freeze_helper.lua:409` (module-level re-declaration guard (`if not freezeProperty then`)) | The generated loader re-loads the helper on every [ENABLE] but the helper's own `if not X then` guards make that a no-op, so an updated helper silently never takes effect in a running CE | S / low |
| **AA31** | LOW | `ue5_invoke_helper.lua:40` ((file header — Debug Camera sample)) | The setDebugCamera sample this file tells users to paste leaves the memory record ticked when the call raises or returns -1 | S / low |
| **AA32** | LOW | `ue5_invoke_helper.lua:227` (writeFStringInline / freeInvokeStringBuffers) | One un-reclaimed target-process allocation per FString param per invoke, tracked in a global table that survives every script re-activation | M / med |
| **AA33** | LOW | `ue5_invoke_helper.lua:235` (writeParams) | writeParams takes regionSize and never bounds a single write by it — a param offset near the end writes past g_invokeMailbox | S / low |
| **AA34** | LOW | `ue5_invoke_helper.lua:239` (writeParams) | `p.value or 0` turns a missing string param into the literal string "0" passed into the game | S / low |
| **AA35** | INFO | `ue5_freeze_helper.lua:355` (fetchInstancePage) | The uint16 sign fixup is dead code resting on a false claim about CE's readSmallInteger | S / low |
| **AA36** | INFO | `ue5_invoke_helper.lua:88` (file-scope mailbox layout constants) | The CI contract gate hashes Mimic.h and CeMailboxLayout.cs only — the two standalone helpers hold a third hand-copied layout it cannot see | S / low |

> **Not re-derived by hand:** everything rated MED and below. The three HIGHs were verified against
> the source as described above; the rest carry the 14%-kill-rate caveat.

-----

### U5 — Remaining VMs / Models / Core / scoring — ✅ scanned 2026-08-15

**11 agents** (5 lenses → 5 refute batches → 1 second-lens batch), 0 errors, over 10 files /
~5,830 lines (~3,500 early). **30 raw claims → 6 refuted → 24 survived → 19 distinct** after merging
five lens-duplicate pairs the skeptic failed to mark.

**Tally: 0 HIGH · 3 MED · 14 LOW · 2 INFO.**

> ### ⚠ Read this before trusting the LOWs — U5's refute rate is the audit's LOWEST
>
> **6 of 30 (20%) refuted**, against a measured per-segment band of **33–73%** over the eight prior
> segments (working-lessons §2). Worse, **two of those six did not die on the merits**: one was a
> duplicate and one was a self-declared *negative result* (the AOT lens correctly reporting that all
> ten files are clean). The on-the-merits kill rate is therefore **4/30 ≈ 13%** — an outlier by a
> wide margin, and the honest reading is that this skeptic was lenient, not that this code is clean.
>
> **The second lens killed zero.** It ran on all 3 HIGH/MED survivors and confirmed all 3. That is
> not nothing — see below — but a 0% kill on top of a 13% kill is two lenient stages in a row.
>
> **The 14 LOWs never faced a second lens at all** (the method sends only HIGH/MED). They are the
> least-scrutinised items in this audit. **Re-derive any LOW before fixing it.** The same warning was
> attached to U4's five LOWs and is repeated here for the same reason.
>
> **The skeptic also failed at dedup**, which is a job it was explicitly given (`duplicate_of_idx`).
> It caught one pair and missed five: `InterestingFunctionsViewModel.cs:439`,
> `KeywordScoringTable.cs:82`, `InterestingPropertiesViewModel.cs:224`,
> `InstanceFinderViewModel.cs:466`, and the `InstanceFinderViewModel.cs:274`/`:289` pair (one
> mechanism — the detach-then-restore cycle — reported as two defects). Merged here by hand; that is
> why 24 "confirmed" is 19 findings.
>
> **What the second lens DID do is the reason to keep the stage.** On Z1 it demolished the mechanism
> while keeping the finding: the finder claimed a two-toggle race, and the second lens showed that
> race is not demonstrable (during `LoadAsync` the guard early-returns, so there is no competing run;
> post-load both runs do byte-identical work with A starting first). It then found the *real* window
> by asking a different question — can the data reach that branch at all? — and produced a concrete
> one: the tick is swallowed during the **2-second AOBMaker probe that runs after the grid has
> already filled and the status line already says "done"**. It also corrected `:383`→`:377` and
> struck the unobserved-exception half of the claim as having no failing input. A kill count of zero
> undersold that stage considerably.

> **Two root causes, and both are repeat offenders rather than new ones:**
>
> 1. **Cluster ① again — a cap treated as the whole set (5 of the 19).** The `countsPartial`
>    parameter on `ClassFacetFilter.Rebuild` **exists, defaults to false, and is left unpassed at both
>    Property Search and Interesting Properties** — in each case *within fifteen lines of the same
>    method reading the truncation flag to print its own cap warning*. So one toolbar shows
>    "⚠ 14 of 50 keywords STOPPED at the 200-row cap" while the class picker beside it reports a clean
>    census of the same data. `ValueSearchViewModel.cs:808` passes it correctly.
> 2. **Cluster ④ again — a fix applied at some of its sites (6 of the 19).** Audit **#3's L14**
>    (a bare disconnect-`OperationCanceledException` reported as "you cancelled") is fixed at
>    Property Search and Instance Finder and **not** at Interesting Properties or Interesting
>    Functions — L14's own "Where" list named only the two it fixed. `scan.deadline_hit` is displayed
>    by the single-row xref dialog and dropped by **all three** batch callers. `SelectedResult = null;
>    // detach before rebuilding the selection-bound list` appears verbatim in three of the four
>    panels in scope.
>
> **A third, new shape worth naming: the fetch list and the score list are two hand-maintained tables
> that must agree, and don't** (Z3). It is root cause #1 in table form, and it is the only U5 finding
> whose cost is "the feature silently cannot find your field".

| ID | Sev | Location | Defect | Effort/Risk |
|----|-----|----------|--------|-------------|
| **Z1** | MED | `InterestingFunctionsViewModel.cs:439` (`RescoreAsync`) | `RescoreAsync` bails on `if (IsLoading \|\| _entries.Count == 0) return;`, and `LoadAsync` holds `IsLoading` true through a trailing `await CheckAobMakerAsync()` (`:377`) that this file's own comment documents as **a 2 s pipe-connect timeout when CE isn't running** — *after* `ApplyFilter()` has filled the grid (`:362`) and the final status text is written (`:368`). The Gameplay-Actions CheckBox has no `IsEnabled` gate, and its `IsChecked` is TwoWay. So a tick in that window **latches ON while the rows stay scored with `includeGameplayActions:false`**, forever: nothing re-runs. The user filters `shop`/`dash`, sees nothing, and concludes the pack found nothing — against a tooltip promising *"Re-scores the loaded set in place"*. Recovery is untick+retick, which is not discoverable. **The covering test never touches this path** — `InterestingFunctionsViewModelTests.cs:233` sets the property and then `await`s `RescoreAsync()` directly (working-lessons §1.3 seam). Fix is a pending-rescore latch, **not** the generation guard the finder proposed. | S / low |
| **Z2** | MED | `InterestingPropertiesViewModel.cs:409` (`ApplyFilter`) | Bare `Results.Clear();` with `Results` bound as DataGrid `ItemsSource` and `SelectedResult` as its `SelectedItem`. The other **three** panels in scope carry `SelectedResult = null;   // detach before rebuilding the selection-bound list` verbatim (`InterestingFunctions:454`, `PropertySearch:556`, `Console:303`), a convention introduced by the build-879 fix + `Services/UiCollection.Reset` after a shipped Avalonia selection-model crash. ⚠ **Honest caveat carried from the finder:** audit #3's L18/L19 confirmed this same class at two other VMs and **downgraded it to "cosmetic churn, zero functional effect"**. New site, but rank it by that precedent. | S / low |
| **Z3** | MED | `PropertyScoringTable.cs:509` (`SeedQueries`) | `SeedQueries` is the **only** thing deciding which properties Interesting Properties (and Detect Player Stats) ever see, and the DLL matches each seed as a case-insensitive **substring of the property name** (`Aura.cpp:4624`). The Stats seed line carries `HP, Health, Mana, Stamina, Energy, Level, Experience, Max, Dead, Alive` — while `StatsKeywords` (`:66`) *scores* `MP`, `SP`, `XP`, `Exp`, `Lv`, `Lvl`, and the category doc advertises "HP / MP / Stamina / XP". **`CurrentMP` contains no seed, so it is never fetched and those scoring arms are dead** (`MaxMP` survives only because `Max` is seeded — same field family, opposite outcome). Same drift hits most of Utility (`Save`/`Checkpoint`/`Score`/`NoClip`/`GodMode`), Movement (`Velocity`/`Dash`), and the build-678 Combat additions the table's own comment says were confirmed across 14/15 games. Cost of the fix is near zero — the DLL walks GObjects **once** and tests every field against every query in that pass. | S / low |
| **Z4** | LOW | `PropertySearchViewModel.cs:498` + `InterestingPropertiesViewModel.cs:359` | `ClassFilter.Rebuild(...)` omits `countsPartial`, so the class-noise picker presents a capped page as a complete class census — **in both cases within fifteen lines of the same method reading the truncation flag to print its own cap warning**. The `⚠ Counts are partial` string exists in `en.axaml` and can never appear on either panel. `ValueSearchViewModel.cs:808` does it right. | S / low |
| **Z5** | LOW | `InterestingPropertiesViewModel.cs:224` + `InterestingFunctionsViewModel.cs:269` | Bare `catch (OperationCanceledException)` → *"Find Funcs cancelled at N/M"*. `PipeClient.cs:192`/`:198` throw a **token-less** OCE on connection loss, so a game crash or DLL unload reads as "you pressed Cancel", with nothing logged. **This is audit #3's L14, applied at 2 of its 4 sites** — the two siblings carry the fix with an explicit `(L14)` comment. Both files also skip the `oldCts`/`Dispose` half. | S / low |
| **Z6** | LOW | `InstanceFinderViewModel.cs:466` (`LookupAddressAsync`) | A bare `_searchGen++` steals the generation from an in-flight class search, but `LookupAddressAsync` owns only `IsLookingUp` and never touches `IsSearching`. The superseded search's `finally` (`:452`, *"only the latest op owns the flag"*) then skips its clear, so the indeterminate ProgressBar bound at `InstanceFinderPanel.axaml:65` **animates for the rest of the session** with nothing in flight. The invariant is "whoever bumps the generation takes over the flag"; this is the one bumper that is not itself a search. | S / low |
| **Z7** | LOW | `InstanceFinderViewModel.cs:274`+`:289` (`ApplyInstanceFilter`) | The method's doc promises it *"Preserves the current selection (and its loaded field grid)"* and *"purely client-side, no server round-trip"*. `UiCollection.Reset` calls its detach callback **unconditionally**, so `SelectedInstance = null` fires `Fields.Clear()`, and the restore at `:289` re-enters the handler and issues a **fresh `walk_instance`** for the address already loaded. Every keystroke pause (200 ms debounce) blanks the field grid and refetches it; one class-noise tick costs **two** walks, since `ApplyInstanceFilter` runs both immediately and again when the server re-run lands. | S / low |
| **Z8** | LOW | `ConsoleViewModel.cs:238` (`LoadAsync`) | `list_all_functions` inherits `limit = 100000`, and `Aura::EnumerateAllFunctions` breaks out of the GObjects walk at the cap **emitting no truncation marker of any kind** — unlike its `search_properties` sibling. The one field that could expose it is degenerate: `totalFunctions++` sits *inside* the push loop (`Aura.cpp:5080`), so it is identically `entries.size()`, and nothing reads the model property anyway. The panel then states a **positive claim about the game** — *"No UFUNCTION(exec) commands found in this game (scanned 100,000 functions…)"* — from a scan that never reached the classes in question. `InterestingFunctionsViewModel` has the identical blind spot and additionally **cannot** pass `countsPartial` to its `Rebuild` because no flag exists to pass. Needs a DLL change, hence M. | M / low |
| **Z9** | LOW | `PropertySearchViewModel.cs:675` (+ `InterestingPropertiesViewModel.cs:209`, `InstanceFinderViewModel.cs:827`) | The batch xref loops consume `res.Xrefs` and discard `res.Scan.DeadlineHit`, which the DLL latches on a real 30 s budget. A row whose sweep timed out is written as **`0`** — the signal the user reads as *"no Blueprint function touches this field, so freezing it is safe"*. The single-row dialog built on the same call prints `[DEADLINE HIT — partial]`, so two UI paths over one DLL call disagree. | S / low |
| **Z10** | LOW | `PropertySearchViewModel.cs:512` | The cap suffix advises *"narrow the query or raise Max"* on a panel with **no Max control** — the string was lifted from Instance Finder, which really does own an `InstanceSearchCap` NumericUpDown. This panel never passes `limit`, so the cap is the compile-time default 200. Either drop the advice or add the lever (the latter is the better fix; 200 is very low for `Health`). | S / low |
| **Z11** | LOW | `PropertySearchViewModel.cs:396` (`ApplyForceAsync`) | `held == 0` is reported as *"nothing held"*, but `Solide::AddForce` only discards a job when the field was found **and type-refused**; with no live instances yet it keeps the job and **starts the re-assert worker**. So the hold is armed and will begin writing into the game the moment an instance spawns, having told the user it did nothing. The `resolved` field that separates the two zero cases is already on the wire (`Fern.cpp:5457`) and never parsed. The "Forced fields" strip and the status line say opposite things. | S / low |
| **Z12** | LOW | `InstanceFinderViewModel.cs:518` | The `[scanned X/Y in Zms]` suffix — whose stated purpose is *"so the user can tell a clean miss from a deadline-truncated scan"* — is built from the **shallow** pass's counters. `container_scan.deep_scan` is emitted by the DLL and never parsed, and `Fern.cpp:4013` only swaps in the deep stats when the deep pass found **nothing**. So a deep-scan success reports the shallow pass's 180 ms, and a deep miss never reveals that the 256-element cap, not exhaustion, ended the search. | S / low |
| **Z13** | LOW | `KeywordScoringTable.cs:82` + `PropertyScoringTable.cs:66` | `StatsKeywords` lists both `"HP"` and `"Hp"`; `CountTokenHits` re-tokenises each keyword and lowercases, so both yield `["hp"]` and **one distinct keyword is counted twice**. This contradicts the method's own documented contract, quoted at `:326`: *"Each keyword counts once total even if multiple tokens would match."* Every HP-named row gets a silent +5 over every other stat keyword in the column the panel sorts on, and the tooltip renders "keywords(2 hits)". Duplicated across both scoring tables. | S / low |
| **Z14** | LOW | `PropertyScoringTable.cs:74` | `"IsDead"`/`"IsAlive"` can never match without `"Dead"`/`"Alive"` also matching, and the comment justifying their presence describes the tokenisation that makes them redundant. | S / low |
| **Z15** | LOW | `InterestingFunctionsViewModel.cs:443` | Toggling Gameplay Actions re-scores every row but leaves the status line's *"above threshold: N"* count from the **previous** scoring mode — the counts are written by `LoadAsync` and `RescoreAsync` never updates them. Distinct from Z1 (that is the re-score not running; this is the re-score running and the report not following). | S / low |
| **Z16** | LOW | `PropertySearchViewModel.cs:606` (`CopyOffset`) | `clipboard?.SetTextAsync(...)` — a Task returned and dropped inside a `void` command, reaching into `Application.Current` by hand even though `IPlatformService` is **injected into this very VM and used by every other copy site**. A clipboard failure (another process holding it open) surfaces nowhere, logs nothing, and sets no status, so the user pastes a stale offset into CE. It is the only unawaited `SetTextAsync` in the UI. | S / low |
| **Z17** | INFO | `LoggingService.cs:18` + `ILoggingService.cs:46` | Two doc comments on the logging surface state a **generation-count** rotation policy ("2-version rotation") — the exact policy CLAUDE.md's app-data rule replaced with age-based retention, and which it explains at length *cannot* express the requirement. The code follows the age rule; only the docs are stale. Per this audit's own §1.11 lesson a comment admitting/asserting the wrong contract is worth filing, but there is no behavioural defect. | S / low |

> **Negative result worth recording** (it reproduces, and an absence is the cheapest thing to produce
> by accident): the AOT/trim lens swept all ten files for `Activator.`, `MakeGeneric`,
> `GetCustomAttribute`, `typeof(…).Get*`, `Type.GetType`, `Assembly.`, `Expression.`, `dynamic`,
> `Convert.ChangeType`, `Enum.Parse/GetValues/GetNames` and non-source-generated `JsonSerializer`
> calls, and found **none**. The two `ComboBox` bindings in scope do not bind a boxed value to
> `SelectedItem`. U5's C# is AOT-clean.

-----

## 3. Refuted — do not re-raise

### From U5 (6 of 30 — 20%, the audit's lowest; see the leniency warning in §2)

Only four of these died on the merits; read the caveat before treating the survivors as vetted.

- **`ConsoleViewModel.cs:72`** — the sticky-instance pin surviving a reconnect to a *different* game.
  Refuted: the self-heal is designed for exactly this and names it — *"A pinned address can go stale
  (object freed, level change, PIPE RECONNECT) … drop the pin and retry once with a fresh classname
  resolution"* — and the code delivers it.
- **`ConsoleViewModel.cs:161`** — three VMs own an `IDisposable KeywordSearchMemory` without being
  `IDisposable` themselves. Refuted: every code fact is correct and **no bad outcome follows** — the
  700 ms debounce timer is harmless at process exit. Worth remembering as the shape of a true
  "correct but inconsequential" claim.
- **`InterestingPropertiesViewModel.cs:224`** — died as a **duplicate** of the async-thread lens's
  better-evidenced report of the same defect, *not* on the merits. The surviving copy is **Z5**.
- **`InterestingFunctionsViewModel.cs:269`** — the L14 twin, refuted on its stated failure input
  ("the game crashes at…") while the underlying site is real; folded into **Z5**, which cites it.
- **`InterestingPropertiesViewModel.cs:409`** — duplicate of the claim recorded as **Z2**.
- **`InterestingPropertiesViewModel.cs:593`** — the AOT lens's self-declared **negative result**,
  correctly not a finding. Recorded as a negative result in §2 instead.

### From U4 (0 of 15 — 0%)

**Nothing was refuted**, which is itself the entry: no U4 claim has been cleared, so nothing from this
segment is on the do-not-re-raise list. See the outlier warning in §2 — the three rows re-derived by
hand held, but the five LOWs are the least-scrutinised findings in this audit and a future session
should not treat their survival as confirmation.

### From U3 (6 of 18 — 33%)

- **`DumpAllService.cs:463`** — the completeness-marker claim, refuted on the harm chain (see X11
  above; the narrower half survives as X11, so this does not clear the whole area).
- **`MainWindowViewModel.cs:674`** — the Extra Scan / version-override fan-out never recording the
  AOB scan result.
- **`MainWindowViewModel.cs:1191`** — seven handoffs calling a class "not resolvable" from a
  truncated class list. Refuted here, but note **X2 is the surviving, narrower form of the same
  underlying cap** — the truncation is real; what was refuted is this framing of its blast radius.
  *(Post-X2 note, build 2888: these four sites were left BEHAVING as before — the refutation stands —
  but their message was re-pointed at `ClassListResult.FindClassAddr`, because the old text asserted
  "Find Instances + ListClasses both empty", which is a false statement about a capped list.)*
- **`MainWindowViewModel.cs:2222`** and **`:2604`** — two framings of the debounced options save
  copying `ProxyDeploy` collections off the UI thread. Both refuted.
- **`MainWindowViewModel.cs:1163`** — panel→Live-Walker handoffs reporting a navigation the
  `AsyncRelayCommand` refused.

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

## 3c. THE OPEN REGISTER — every finding still outstanding

*Generated from §2's tables, not hand-tallied. Re-derive after any fix with:*

```bash
python -c "import re;s=open('docs/audit-2026-08-13-early-code-findings.md',encoding='utf-8').read();r=re.findall(r'^\| \*\*([A-Z]{1,2}\d+)\*\*( ✅)?(?: †| ‡| ¤)? \| (?:\*\*)?(HIGH|MED|LOW|INFO)(?:\*\*)? \|',s,re.M);print(len([x for x in r if not x[1]]),'open /',len(r),'total')"
```

> ⚠ **The first version of this command (2026-08-15 AM) undercounted — 263 total instead of 272.**
> Two parser traps, both now tolerated above: (a) the five dagger-marked rows (`G7`†, `PX1`‡, `MB3`†,
> `F1`‡, `F8`¤) that §4's own footnote at "allow an optional marker" had already documented, and
> (b) the four rows whose severity cell is **bold** (`| **V1** ✅ | **HIGH** |` — V1, W1, W2, Y1, all
> fixed HIGHs), which nothing had documented. On top of that, eleven findings fixed in §2's
> prose blocks (M1–M3, A2, U1, U2, F1, F4, F6, F7, FR1 — commits `5ef4c2b`, `c65fdfc`, `0d9fcfa`,
> `a2b616a`, `cfaa5cd`, builds 2813–2830) had never been ✅-marked on their table rows, so the
> register counted them open. Rows are now marked; the numbers below are the corrected derivation.

**230 of 272 findings are still open** (42 fixed — F3 counts as open: only its reconnect half
shipped, the in-session half is deliberately deferred to cluster ③). Open: **0 HIGH · 70 MED ·
133 LOW · 27 INFO**.

> **BOTH named families are now closed** apart from the parked Y16 — the width family (b2950) and
> root cause #4 (b2961). What remains in MED is no longer family-grouped; pick by segment or by
> subsystem, and keep applying the sibling grep at fix time (it found two new occurrences in these
> two batches alone).

> **The width family is CLOSED** (build 2950) apart from the parked Y16 — see the family block below,
> which also records one refuted lead and one new sibling found at fix time. Fixed HIGHs: **11 of 11** (V1, W1, W2, Y1, AB1, AD1, AD2, AA1, AA2, AA3, AB2).

> ## ✅ THERE ARE NO OPEN HIGHs. Every HIGH this audit raised is fixed.
>
> The queue from here is the **MED tier, grouped by family rather than by segment** — see the family
> block below. And the standing caution is unchanged and now dominant: **LOW/INFO were never vetted to
> this audit's standard, so re-derive before fixing.**

> **Updated 2026-08-15 (build 2914): AD1 + AD2 FIXED**, counts above re-derived with the command
> rather than hand-tallied. Both sites were collapsed into a single `Invoke-CppSelfTest` helper, a
> same-class sibling in the C# phase was fixed alongside them, and the fix was proved with a negative
> control (a deliberately broken test source now fails the build; it previously printed "skip" and
> exited 0). Full record in T1b's block, §2.

> ⚠ **The LOW and INFO tiers are NOT vetted to this audit's standard.** Kill rates ran 10–35%
> against the audit's own 33–73% band, and only HIGH/MED got a second lens in most segments. Each
> §2 block states what was hand-verified. **Re-derive any LOW before fixing it** — several are
> pattern-sweep leads, not findings.

### The HIGHs — all 11 fixed

> ✅ **All six were re-verified against the source 2026-08-15 (PM double-check pass — see the block at
> the end of this section). Zero line drift on all six.** The per-finding corrections below are from
> that pass; the original one-line claims stand, but several details a fixer would code against
> changed. **Three of the six (AD1, AD2, AA1) have since been fixed** — builds 2914 / 2922.

~~1. **AD1** / 2. **AD2**~~ — ✅ **FIXED, build 2914, 2026-08-15.** Both collapsed into one
`Invoke-CppSelfTest` helper (two hand-copied blocks were themselves root cause #4), the three
outcomes that shared the "skip" line are now three distinct failures, the C# phase's same-class
sibling was fixed at the same time, and a negative control proved the check can now fail. Record in
T1b's block, §2. *Nothing about the remaining HIGHs changed.*

~~**AB2**~~ — ✅ **FIXED, build 2932, 2026-08-15.** Every correction in the re-verify note held up
and two mattered to the fix: the 10 s ceiling is the gate (so the hazard is "total remote work >
10 s", which is why it never reproduced reliably), and the APC path's 1 s makes it near-certain for
anyone with that Settings box ticked. The fix is the proposed spawn-and-return — **measured at 2.3 ms
to return against 3486 ms for the same build with the spawn reverted.** The note's "misleading
dialog" aside turned out to understate a second defect: CE's `InjectDLL` BOOL is *inverted* for the
common cases, so the plugin now decides by observing the target's module list. Full record in T1a's
block, §2.

## ✅ EVERY HIGH IS FIXED — the queue below is MED and lower

### Where the rest live

| Segment | HIGH | MED | LOW | INFO | Open |
|---------|-----:|----:|----:|-----:|-----:|
| S1 early Lua scripts | – | 17 | 14 | 2 | **33** (AA1/AA2/AA3 fixed b2922-2926) |
| T1c VMs + Core + Models | – | 8 | 15 | 4 | **27** (AE1, AE10 fixed b2950/2961) |
| T1b DLL headers + C++ tests | – | 4 | 15 | 6 | **25** (AD1+AD2 fixed b2914) |
| T1e Views + app root + tail | – | 6 | 17 | 4 | **27** |
| T1a Radar + entry points | – | 4 | 12 | 4 | **20** (AB2 b2932, AC2 b2961) |
| U5 remaining VMs / Models / Core | – | 3 | 13 | 1 | **17** |
| T1d UI Services | – | 2 | 10 | 5 | **17** |
| U3 Dump services + MainWindow VM | – | 1 | 9 | 0 | **10** |
| D1 Ubel | – | 6 | 3 | 0 | **9** (U1, U2 fixed) |
| D3 Aura | – | 5 | 4 | 0 | **9** (A2 fixed) |
| U1 LiveWalker / Pointer / ObjectTree | – | 4 | 4 | 0 | **8** |
| D2 Genau+Serie | – | 3 | 4 | 0 | **7** (incl. G7 †) |
| U4 Dialogs + CE generators | – | 1 | 5 | 0 | **6** |
| D5 Fern | – | 3 | 1 | 0 | **4** (open: F2, F3's in-session half, F8 ¤, F5) |
| D4b Mimic | – | 0 | 2 | 1 | **3** (incl. MB3 †) |
| U2 Export services | – | 1 | 1 | 0 | **2** |
| D4b Flamme | – | 0 | 2 | 0 | **2** |
| D4b Sein | – | 0 | 2 | 0 | **2** |
| D4b Stark | – | 1 | 0 | 0 | **1** |
| D4b Lugner | – | 1 | 0 | 0 | **1** (PX1 ‡ — was dropped by the old regex) |
| D4a Macht | – | 0 | 0 | 0 | **0** (M1–M3 all fixed 2026-08-14) |
| D5 Frieren | – | 0 | 0 | 0 | **0** (FR1 fixed build 2820) |
| **TOTAL** | – | **70** | **133** | **27** | **230** |

### Fix order recommended

1. ~~**AD1/AD2**~~ — ✅ **DONE, build 2914.** It was first because it is *meta*: while it stood,
   every other fix's "tests green" was one compile error away from meaning nothing. That is no
   longer true, so the fixes below now rest on a test phase that can actually fail.
2. ~~**AA1**~~ — ✅ **DONE, build 2922.** The mask was indeed absent end-to-end and had to be added
   at five tiers (DLL wire → `PropertySearchMatch` → `ScoredPropertyRow` → `FreezeScriptParams` →
   CFG → Lua). It is now a fourth implementation of the one bit rule that `Solitar::ApplyBoolBit`,
   `FieldValueConverter.ApplyBoolMask` and `UE5T_setbit` already shared.
3. ~~**AA2/AA3**~~ — ✅ **DONE, build 2926.** It was a mailbox-contract change as predicted
   (1 → 2, additive), but **not** the widening this line proposed: the witness that matters for a
   class-wide freeze is class membership, not object identity, and that rides in two unused output
   fields with the 8-byte entries untouched. See S1's block for why a serial-number check would
   have been *less* correct.
4. ~~**AB2**~~ — ✅ **DONE, build 2932.** Spawn-and-return in `UE5_AutoStart`, exactly as proposed,
   measured at 2.3 ms against 3486 ms for the reverted build. A second defect surfaced while fixing
   it: CE's `InjectDLL` BOOL is *inverted* for the common cases, so the plugin now decides by
   observing the target's module list instead of trusting it. See T1a's block.
5. Then the MED tier, **grouped by family rather than by segment** — see below.

### Fix by FAMILY, not by ID — this is the audit's strongest recommendation

The two recurring families are both greppable. Status as of the 2026-08-15 double-check:

- **The width family — ✅ CLOSED except the parked member** (build 2950). Nine occurrences across
  five subsystems: W6 ✅ b2857, Y2 ✅ b2866, Y9 ✅ b3da36ca, Y15 ✅ b2904, **AE1 ✅ b2950**, plus the
  two double-check leads that survived re-derivation and one sibling found while fixing them (all
  ✅ b2950, detailed below). Only **Y16 remains — PARKED, do not pick up, ask first**.
  **At every site the correct width was already in scope and simply not enforced**, so the repair
  is now a single predicate — `FieldValueConverter.FitsInWidth(value, sizeBytes)` — rather than N
  hand-written range checks that can drift apart again.

  **The three double-check leads, re-derived by hand before any of them was treated as a finding**
  (they were single-agent finds with no skeptic pass, so §2's ~50% base rate applied):

  | lead | verdict |
  |---|---|
  | `InvokeParamDialog.cs` `DecodeParamValue` reads a 1-byte enum as 4 | **CONFIRMED** — `"EnumProperty"` was grouped with `"IntProperty"` behind `available >= 4`, so a 1-byte enum mid-buffer returned its own byte plus three belonging to the next param. The READ side of what Y2 fixed on the write side of the same file. Fixed via a shared `DecodeBySize`, the mirror of `WriteBySize`. |
  | `SdkExportService` enum declared width vs the layout cursor | **REFUTED — do not re-raise.** The premise requires `InferEnumUnderlyingType` and the layout cursor to meet, and they never do: `GenerateEnumDefinition`/`InferEnumUnderlyingType` have **zero production callers** (grep across `ui/` returns only `SdkExportServiceTests`). The class layout goes through `MapCppType`, which emits the enum's NAME, not a width. |
  | `ParamBufferBuilder` FIRE path masks with no validation | **CONFIRMED, and the caveat was the whole story.** `WriteBySize` masks at every width (`unchecked((byte)u)`, `(short)`, `(int)`), so 9999 into a 1-byte param fired 15 silently. The masks are Y5's fix (a signed `-1` must arrive as `0xFF`) and are **kept**; the repair is a signedness-aware range check in front of them, surfaced through the dialog's existing red result label. Removing the cast would have re-introduced Y5. |

  **A NEW sibling was found at fix time, by the test rather than by a finder** — root cause #4's
  **eighth** occurrence. `ParseULong("-1")` returns **0**, because `ulong.TryParse` rejects the
  sign: so typing `-1` for a `UInt16Property` / `UInt64Property` / pointer param fired **0** at the
  live game while *Copy AA Script* baked `0xFFFF…`. That is **precisely the defect Y5 fixed**, and
  Y5 fixed it in `ParseByteOrSByte` only, leaving every unsigned path with the original bug. It
  surfaced because a test asserted `EffectiveIntWidth` against how many bytes `WriteParam` really
  touches — i.e. it was caught by testing the SEAM, not the helper (§1.3).
- **Root cause #4 — ✅ CLOSED except the parked member** (build 2961). Eight occurrences: V2 ✅,
  W4 ✅, W6 ✅, X1 ✅, **AC2 ✅ b2961**, **AE10 ✅ b2961**, the `ParseULong` sibling ✅ b2950, and
  **Y16 — PARKED, ask first**.

  **AC2** — `ResolveTrustedLayout` (the audit's OWN Y7 fix) guarded 1 of 4
  `KnownStructLayouts.GetLayout` call sites. The reason it could not spread is structural and worth
  keeping: **Y7 wrote the predicate as a private helper inside `InvokeParamDialog`, a View**, so the
  two consumers in `StructReturnDecoder` (a Service) could not reach it even in principle. The fix
  moves it to `KnownStructLayouts.GetTrustedLayout`, beside the table it guards — **a predicate that
  guards a table belongs with the table**, and then all four sites reach one implementation.

  **AE10** — 19 gate sites (14 C# + 5 XAML) across 7 VMs, all removed, and then the flag itself
  deleted from **9** ViewModels. Removing the gates alone would have left `IsGWorldAvailable`
  write-only in nine places — the exact dead-flag shape this register already noted in
  `LiveWalkerViewModel` — and a flag nobody reads is an invitation to re-gate on it. Deleting it
  makes the mistake unavailable. `EngineState.HasGWorld` remains for anything that wants to *display*
  GWorld status; what must not return is gating an ACTION on it.

  **The premise, verified rather than assumed** (two tests argued the other way and one of them
  argued well): `IsGWorldAvailable` comes from `EngineState.HasGWorld`, whose own definition is
  *"`GWorldAddr` is non-empty and non-zero"* — i.e. **"the AOB scan produced a &GWorld slot
  address"**, NOT "a live UWorld exists". The DLL has world-recovery fallbacks that work when that
  scan did not, which is why the button was dead on games where locate worked (TQ2, proxy mode). It
  is a **cheap proxy signal substituted for a predicate the DLL already computes correctly** —
  audit #4's recorded root cause, and the reason the counter-argument in
  `InterestingPropertiesViewModelTests` ("a property is a class-level definition, so without a
  resolved GWorld there is nothing to locate against") is sound in its conclusion and wrong in its
  premise.

  ⚠ **Two existing tests were pinning the defect**, one in each finding — `CanDecode_KnownStruct_
  ReturnsTrue` asserted a UE5 layout for a 12-byte param, and `LocateResultInGWorld_RaisesEvent_
  OnlyWhenGWorldAvailable` asserted the gate. **This is how both survived: the sibling was fixed, a
  green test kept saying the other site was fine.** When a fix is under-applied, expect its siblings
  to have tests defending them, and read those tests as evidence about the BELIEF, not the code.

**The single most productive technique was the comment sweep** — grep for a comment admitting a
limitation or asserting an impossibility, then check it. **6 for 6**, and it produced the lead
finding in T1a, T1b and T1e.

### 🔎 Double-check pass — 2026-08-15 PM (verification only, no code changed)

Five parallel read-only verifiers re-derived the fix queue's head against tree `8941fb4` before
handing it to the fixing session. Scope: the 6 open HIGHs, the open family members (AE1, AC2, AE10),
W5 (next in queue), the Y16 parked survey, and **every ✅ mark in §2** (all 22 row-marked + the 11
prose-fixed rows now marked). Everything below was verified by locating code by IDENTIFIER, not by
the row's line number.

**Verdicts:**

| Finding | Verdict | Key correction (details inline above where load-bearing) |
|---|---|---|
| AD1, AD2 | CONFIRMED, zero drift | Two independent sites; CI hole confirmed end-to-end |
| AB2 | CONFIRMED, zero drift | 10 s ceiling gate; victim is the game; APC path = 1 s |
| AA1 | CONFIRMED, zero drift | Also no-ops the intended bool when mask ≠ 0x01 |
| AA2 | CONFIRMED, details drifted | ~16 writes/s per address (TTimer quantised), not 20; 5 s is best case |
| AA3 | CONFIRMED, details drifted | "Indefinitely" = persistent-failure cases only; `_lastError` write-only |
| W5 | CONFIRMED, exact line | The look-alike test (`…EmitsLeafWith8BytesNotGroupHeader`) passes **no `resolvedInstances`**, so the drill branch is untested; the correct predicate `IsRawObjectPtrArrayInner` + the refusing comment sit at `:478-486` of the same file |
| AE1 | CONFIRMED, unfixed | Untouched by the Y9/Y15 fixes (`FieldValueConverter.cs` has exactly one commit ever); defect is specific to the enum-backed branch — plain `ByteProperty` correctly rejects 9999 |
| AC2 | CONFIRMED, counts corrected | 1 of **4** `GetLayout` call sites guarded (3 distinct consumers); unguarded: `InvokeParamDialog.cs:693`, `StructReturnDecoder.cs:55` + `:79`; size in scope at all three; `StructReturnDecoder.cs` already `using UE5DumpUI.Views;` |
| AE10 | CONFIRMED, counts corrected | **7** VMs / 19 sites still gate (ClassPivot, Snapshot(+Group), SpcQuery(+Group), InstanceFinder, InterestingFunctions, InterestingProperties, DetectStats); todo.md's pending list under-names them |
| Y16 (parked) | Survey re-verified, no drift | All three sites + the cosmetic straggler exist exactly as surveyed (`CeInvokeReturn.cs` lives in `Services\`) |
| All 33 ✅ fixes | **VERIFIED-FIXED in tree, 0 failures** | Includes the 11 newly row-marked (M1–M3, A2, U1, U2, F1, F4, F6, F7, FR1) |

**Residuals left inside shipped fixes (string/comment-level, worth sweeping with the next commit in
each file):**

- **AB1 (✅ 2913):** the helpers are at **global scope**, not `namespace Grimoire` (`Grimoire.h`
  closes the namespace at `:347` before declaring them — `Grimoire::HostAllowsBackgroundThreads`
  would not compile; §3b's bullet and the memory files say otherwise). Three stale comments
  survived the fix: `Heiter.cpp:353` ("Only the implicit process-exit DETACH is a no-op" — the
  unnamed `lpReserved` still can't tell), `Heiter.cpp:329` ("Store the handle so DETACH can wait" —
  nothing waits), and `Fern.cpp:533-537` (cites `Heiter.cpp:288-301`, now the middle of the new
  comment block, and justifies safety by the falsified premise instead of the new PIN).
- **V2 (✅ 2830):** `ContainerGeometry.FallbackStride` deliberately keeps the old
  `Align(elemSize,4)+8` for pre-2830 DLLs (documented, correct only for `alignof(T) <= 4`), and
  `CsxExportService.cs:623` still carries a doc comment asserting the dead formula directly above
  code that calls `ContainerGeometry.MapStrideOf`.

**Three NEW width-family leads (⚠ single-agent finds, NO skeptic pass — re-derive before filing as
findings, per §2's own ~50% raw kill rate):**

1. `InvokeParamDialog.cs:992` — `DecodeParamValue` reads a 1-byte enum as 4 bytes when
   `available >= 4` (the C# read side of the invoke result grid; NOT one of Y16's three sites, which
   are all on the generated-Lua side). Tell: a 1-byte enum at the END of the buffer decodes right
   (guard fails → correct size fallback), one mid-buffer doesn't.
2. `SdkExportService.cs` — the generated enum's declared width comes from
   `InferEnumUnderlyingType(entries)` while the layout cursor advances by the real `f.Size`
   (`:264-265`, `:319` hardcode `uint8_t`) — a header that silently mis-lays out members after a
   wide enum.
3. `ParamBufferBuilder.cs` — the FIRE path masks out-of-range values (`(short)`, `(int)`,
   `unchecked((byte)u)`) with zero validation in `InvokeParamDialog` — Y9's defect in the invoke
   dialog. Caveat: the byte wrap is PARTLY deliberate (Y5's `-1` → `0xFF`), so the repair is a
   signed/unsigned-aware range check, not removing the cast.

**Bookkeeping corrected in this pass:** the register command + counts (263→272 total, see the
header note), the 11 unmarked fixed rows, the segment table, and one misnumbering: the width-family
member in `FieldValueConverter.cs` is **AE1** per T1c's own table — §2z and T1c's prose called it
"AE9" (which is actually the Sort-picker no-op finding). Both prose sites now corrected.

-----

## 3b. START HERE — scanning is DONE; everything left is fixing

*State as of 2026-08-15. Read this first; it is written for a session with no memory of this one.*

> ✅ **ALL 12 SEGMENTS ARE SCANNED.** D1–D5, U1–U5, S1, and T1's five phases T1a–T1e. See §2z for the
> completion summary: per-segment kill rates, the three findings to start from, and the two defect
> families that account for more findings than any single subsystem.
>
> 📋 **The complete open list is §3c, "THE OPEN REGISTER" — read that first.** It is generated
> from §2's tables (**239 open of 272; 6 HIGH / 73 MED / 133 LOW / 27 INFO** — corrected 2026-08-15
> PM: the earlier "245 of 263" came from a regex that dropped nine rows and from eleven fixed rows
> that were never ✅-marked; see §3c's header note), names the six open HIGHs with file:line, says
> where every remaining finding lives, gives a recommended fix order, and carries the command to
> re-derive itself after a fix. **All six open HIGHs were re-verified against the source
> 2026-08-15 PM (zero line drift)** — per-finding correction notes sit inline in §3c's HIGH list.
>
> ## ✅ ALL ELEVEN HIGHs ARE FIXED (builds 2913 - 2932). There is no HIGH work left.
>
> AB1 (2913) · AD1/AD2 (2914) · AA1 (2922) · AA2/AA3 (2926) · AB2 (2932) — see dev-log for each.
> **The queue from here is the MED tier, grouped by FAMILY rather than by segment** (§3c's family
> block). Before picking anything below MED, re-read the vetting warning: LOW/INFO were scored at
> 10-35% kill rates against this audit's own 33-73% band, so **re-derive a LOW before fixing it**.
>
> 🔴 **The two most consequential findings, both hand-verified against the source:**
> - **T1a/AB1 — our DLL crashes Cheat Engine on a documented install path. ✅ FIXED (2913).** `DllMain` starts a
>   1 ms-poll thread unconditionally; CE `FreeLibrary`s plugin DLLs on Settings→Add and on every exit;
>   `DllMain`'s `lpReserved` is commented out so DETACH cannot distinguish unload from process-exit;
>   nothing pins the module; and `IsCheatEngineExeName` (global scope in `Grimoire.h` — NOT
>   `Grimoire::`, the namespace closes before it is declared) had exactly one call site — *inside*
>   the thread it should have prevented. Fix is two small guards; **do not** join threads from DETACH.
>   Fix verified in-tree 2026-08-15; three stale comments survived it — see §3c's double-check block.
> - **T1b/AD1 — a C++ test target that fails to COMPILE is reported as "skip" and the build exits 0**,
>   CI included (`build.ps1:691-693`; the `else` arm never touches `$exitCode`). It was **latent** —
>   the control was run and the suite did execute — but it silently disarmed ~700 assertions the
>   moment a test stopped compiling. **✅ FIXED with AD2 (2914):** both sites are now one
>   `Invoke-CppSelfTest` helper, the "skip" arm is gone, the C# phase's same-class sibling was fixed
>   with them, and a negative control (a deliberately broken test source) now fails the build.
> - **S1/AA1 — a bool freeze stamped a WHOLE BYTE over a bit-packed `FBoolProperty`** ~16x/sec,
>   wiping up to 7 sibling bools and — unless the mask was 0x01 — never setting the intended one.
>   **✅ FIXED (2922):** the FieldMask was structurally absent end-to-end and now travels DLL wire →
>   model → row → params → CFG → Lua. Live-game half is UNPROVEN — see todo.md's register.
> - **T1a/AB2 — CE freed the remote inject stub out from under our still-running scan**, crashing the
>   GAME. **✅ FIXED (2932):** `UE5_AutoStart` spawns and returns (2.3 ms measured, vs 3486 ms
>   reverted). It also exposed that CE's `InjectDLL` BOOL is inverted for the common failures, so the
>   plugin now reports what it can observe rather than what CE claims.
>
> ⚠ **Vetting is uneven and the doc says so per segment.** Kill rates ran 10–35% across the T1
> phases against the audit's own 33–73% band. **Everything hand-verified is marked as such in its
> block; re-derive anything else before fixing it.**
>
> 🔁 **Before closing ANY fix, grep for its siblings.** Root cause #4 is at **seven** occurrences
> (V2, W4/W6, X1, Y16, AC2, AE10 — four now fixed, see §3c's family block) and **AC2 is this
> audit's own Y7 fix applied at 1 of 4 `GetLayout` call sites** (recounted 2026-08-15 — the earlier
> "1 of 5 consumers" counted two non-consumers and "1 of 3 sites" missed `InvokeParamDialog.cs:693`).
> The rule has been in this section since the fourth; three more have appeared since, which means it
> is still not being applied *at fix time*. Treat "where else does this predicate belong?" as part of
> the fix.

> **Next fix in the queue: W5** — `CeXmlExportService.cs:2141`, `S`/low: weak/soft/lazy pointers
> drilled with `Offsets=[0]`, i.e. dereferencing a slot that is not a pointer. Re-confirmed
> 2026-08-15 (exact line): the branch predicate is the broad `IsObjectPropertyType` family while the
> correct narrow predicate `IsRawObjectPtrArrayInner` + the refusing comment already exist at
> `:478-486` of the same file. ⚠ The existing test
> `GenerateInstanceXml_WeakObjectProperty_EmitsLeafWith8BytesNotGroupHeader` passes **no
> `resolvedInstances`**, so the drill branch is currently untestable-by-accident — extending that
> test IS the regression test for this fix.
> **Y16 remains PARKED at the maintainer's request** (2026-08-15) — surveyed in full in §2, three
> sites, M/low. Do not pick it up as filler; ask first.

### The pacing rule, learned by hitting it

**ONE segment per 5-hour quota window. Do not plan two.** Measured on 2026-08-14: that window ran the
D4b takeover + the whole of D5 + six fixes (F1, F3a, F4, F6, F7, FR1) with their builds and in-game
verification, and reached **80% of quota**. A segment is a scan *or* a fix batch, not both, unless the
fixes are trivial. If a window has budget left after the segment, spend it on **verification**, which
is cheap and compounds — not on starting the next segment, which will be cut off mid-flight.

### What is done

**Scanning: 12 of 12 segments started — only T1 remains**, and T1 is split into five phases
(T1a–T1e, see §1). D1, D2, D3, D4a, D4b (`8198309`), D5 (`e131c8a`), U1, U2, U3, U4, U5, S1 are done.
**The DLL, the C# and the Lua are all scanned; what is left is the T1 tail.**

**Fixing:** cluster ① is 6 of 7 shipped, now on **both** sides of the wire (`5ef4c2b`, `c65fdfc`,
and build 2830 for the C# half U1/V2 exposed); A4 deliberately open. D5 shipped **F1, F3(a), F4, F6,
F7, FR1** across `0d9fcfa` / `a2b616a` / `cfaa5cd` / `1e5ab21`. U1 shipped **V1 (the HIGH), V2, V5**.
**Still open: Z1–Z17, Y10–Y14, Y16, X3–X12, W5, W8, V3, V4, V6–V11, F2, F5, F8, A4, U3, G7, U2** — no HIGHs
remain. U4 shipped **Y1** (2862), **Y2+Y3+Y4+Y5** (2866), **Y8** (2875), **Y6+Y7** (2881),
**Y9** (2895) and **Y15** (2904) — 10 of its 16 (Y15 was hand-found while fixing Y9, and Y16 while
fixing Y15); U3 shipped **X1** (2870) and **X2** (2888) — 2 of its 12.
U2 shipped **W4** (2836), **W2+W3** (2842), **W1+W7** (2853), **W6** (2857) — 6 of its 8 findings.
U3 shipped nothing yet; **X1 is the cheapest and closes a known-recurring gap** (S/low, and it is the
other half of a fix this audit already paid for).
(Note the ID collision: `U2`/`U3` in that list are D1 findings in `Ubel.cpp`, *not* segment names.)
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

**U5 scope** (from section 1): the remaining VMs / Models / Core / scoring — Console 445,
InstanceFinder 405, InterestingProps 380, PropertySearch 356, InterestingFuncs 345, LiveFieldValue 478,
Logging 362, scoring tables ~800 = **~3,500**. It is the last UI segment; S1 (early Lua scripts) and
T1 (tail sweep) follow.

**Two U3/U4 findings land in U5's files — do not re-derive, but DO check their siblings:** X1's
consumers are `InterestingPropertiesViewModel` and `DetectStatsViewModel` (the panels that present a
capped pool as complete), and the scoring tables are what feed them. Ask instead what *else* those
panels report as complete.

**What U4 proved about lens design, and it is the most transferable result so far.** The
`ce-lua-hygiene` lens — pointed at CLAUDE.md's MUST-follow rule block as a *specification* — produced
the HIGH. A rule set precise enough to make violations **decidable rather than arguable** is worth a
lens of its own. For U5 the equivalent specifications are CLAUDE.md's **keyword-search-box contract**
(space = AND via `ObjectTreeFilter.MatchesAllTerms`, never a single `.Contains` over a concatenated
string; per-keyword memory via `KeywordSearchMemory` with `Schedule` vs `Commit` for async counts;
`Flush()` before clearing on navigation) and the **UI-strings rule** (English only, in
`Resources/Strings/en.axaml`, referenced via `StaticResource` — CI-gated by `check_axaml_strings`, so
a violation is decidable). Point one lens at each.

**What the C# segments proved about lens choice:** `aot-trim` was dropped after U1 (it found nothing)
and nothing was lost. `domain-correctness` and `status-honesty` were productive in all three. Give one
lens the **consumer's** point of view — in U2 that was "does the artifact parse in the tool that reads
it" and it produced both HIGHs; in U4 the consumer is **Cheat Engine's Lua engine**, so
docs/CE-Bugs-Minesweeper.md and docs/ce-plugin-sdk-notes.md are the grounding.

**Run the unread-wire-key sweep again for U4/U5 — but know its blind spot.** Diffing DLL-written vs
C#-read JSON keys found X3; it structurally could NOT find X1, because a key read on ONE command's
path counts as read everywhere. A per-command version of that sweep would find both, and would be
worth writing once.

**Put in every agent prompt** (measured to improve output): the calibration — *195 raw claims, 81
refuted (**0–73% per segment**, ~42% overall; U4 refuted nothing, which is an outlier and not a
target), twenty-two HIGHs claimed, eighteen died and four were real* — plus *"read the surrounding
comments and the callers first"*, the **seam** instruction above, and **REPORT ONLY, no edits**.

**And add U4's lesson about what hides a defect:** all four real HIGHs lived in code whose happy path
is only ever exercised with **default or absent input** — a `'0x0'` default that parses correctly, a
map row whose first element is at offset 0, a `.usmap` nobody opened. Ask of each finding: *would a
smoke test with default values catch this?* If not, that is the finding to chase.

**Do not re-derive:** everything already in sections 2 and 3 — D5's `Fern.cpp`/`Frieren.cpp`
findings and its six shipped fixes, U1's eleven (V1/V2/V5 fixed), U2's eight (six fixed), U3's twelve
+ six refutations, and U4's fourteen (nothing refuted — see the outlier warning).

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

**Status: 10 of 12 segments scanned** (D1, D2, D3, D4a, D4b, D5, U1, U2, U3, U4). **94 distinct
findings: 4 HIGH · 50 MEDIUM · 39 LOW · 1 INFO.** 195 raw claims, **81 refuted (42%)**. Remaining:
U5, S1, T1. **The DLL is fully scanned; everything left is C# and Lua.**

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
| U3 dump services + MainWindow VM | 11 | 19 | 6 (33%) | 11 **+1** ✧ | 0 → 0 |
| U4 dialogs + CE generators | 18 | 20 | **0 (0%)** ⚠ | 14 | 4 → **1** |

¤ **D5's section holds 9 rows but its run produced 8** — F8 came from verifying F6, not from a
finder (same as D2's G7 below).

◊ **U2's seven HIGH claims collapse to two distinct findings.** Four were the same USMAP
version/layout desync seen through different lenses, one was downgraded to MEDIUM by the second lens.
The Distinct column counts findings, not claims.

✧ **U3's section holds 12 rows but its run produced 11** — X3 came from a hand-run sweep of
DLL-written vs C#-read JSON keys, not from a finder (same as D2's G7 and D5's F8).

⚠ **U4 refuted NOTHING, which is an outlier, not a result.** Every other segment killed 25–73%. The
rate is therefore unusable as a quality signal for U4; three of its rows were re-derived by hand
instead and held. Its five LOWs are the least-scrutinised findings in this audit.

✦ **D2's section holds 7 rows but its run produced 6.** G7 did not come from any finder — it was
found during the 2026-08-14 live-verification session and filed into the D2 section because that is
where it belongs by subject. The Raw/Refuted columns describe the *runs*; the totals above count
*rows*, so the two reconcile only through this row. (Counted directly: `grep -cE '^\| \*\*[A-Z]+[0-9]+\*\*'`
scoped to §2 = **94**, of which 4 HIGH / 50 MED / 39 LOW / 1 INFO. Re-derive it rather than trusting
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

**The refutation rate is 25–73% across nine segments and is NOT a constant.** The six DLL segments
sat at ~44–73%; the three C# segments came in at 33% / 25% / 33%, i.e. consistently lower. Two things
move it and they point the same way: the C# skeptics are the first with **real test coverage** to
refute *with* (their kills are visibly better argued), and the C# code is genuinely denser in real
defects because much of its output is validated by programs we never run. Quote the range to finders,
not a constant, and budget U4/U5 on ~30%, not the DLL's ~50%.

**Severity does NOT decay monotonically — that prediction was wrong and is worth keeping as a
correction.** After U1 (one HIGH), U2 (two) and U3 (none claimed), the note here predicted U4 would
"look like U3". **U4 produced a HIGH**, and the same kind as U1's and W1's: a capability that has never
worked, hidden because its *default value happens to be correct*. The lesson is not about severity
trending but about **what makes a defect invisible** — all four real HIGHs in this audit were in code
whose happy path is exercised only with default or absent input.

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

**U3 makes the cluster's recurrence undeniable: the fix for one of its own members was half-applied.**
U3/**X1** is the D5/F4 truncation fix present on the DLL for both property-search paths and on the C#
for one, so the batch path — which is what the two *discovery* panels use — still presents a capped
pool as the pool. U3 adds three more: **X3** (the DLL publishes a three-flag verdict that the walker
may be running on unvalidated default offsets, and the UI never asks for it), **X4** (a Dump All
completion line derived from the file's byte length rather than from what the dump did) and **X2**
(a real class reported "not found" because the lookup reads one capped page).

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
