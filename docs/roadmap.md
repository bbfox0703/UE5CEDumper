# Roadmap — Current State

Snapshot of capabilities and per-game configuration. Updated when
behaviour or test coverage changes; pair with [todo.md](todo.md) for
upcoming work and [dev-log.md](dev-log.md) for the historical commit
trail. Build number tags reflect when each row reached its current
state.

> **Last refreshed**: 2026-05-29 (build 797) for the rows below. **Branch parity is not tracked
> here** — a hand-maintained "dev = main @ build N" line is stale on the next commit, and this one
> was 57 commits out by 2026-08-05. Ask git: `git rev-list --count main..dev`.
> **This file is STATE, not history.** Shipped work belongs in
> [dev-log.md](dev-log.md); it used to be summarised here too, and the copy went stale
> independently — including a "multipipe Phase 0 SHIPPED" line that survived the
> 2026-08-05 sweep run to kill it, in a file that contradicted itself 124 lines apart.
> One owner, so there is nothing to keep in sync.
>
> ⚠ **The rows below were last swept at build 797 (2026-05-29); the repo is well past it.**
> A 2026-08-06 audit sampled 14 rows: 8 still true, **6 false**. Verify against the tree
> before acting on any row here. Known-false as of that audit, not yet rewritten:
> - Native C++ (non-`UPROPERTY`) fields are **not** excluded from Value Search — Native-C
>   scanning shipped; the banner now offers it.
> - The value-scan deadline is **not** a hard 15 s — it is user-adjustable 10-90 s, default 25.
> - MulticastSparseDelegate at UE 4.23 is **not** untested — PDB-confirmed 2026-07-28.
> - The verified engine floor is **4.11** (NEKOPALIVE), not 4.15.
> - Per-game counts and the tested-games list disagree with
>   [test-games.md](test-games.md), which is the authority for both.
> - Nothing shipped after build 797 is described below at all (Coordinate Library,
>   bookmarks, log compression, leftover-proxy cleanup, the pre-UE4 gate, Live Walker
>   Back/Forward, and the `Hemmung` / `Solide` / `Linie` / `Schlacht` / `Grausam` /
>   `Laufen` modules).

-----

## Current release

**[v3397](https://github.com/bbfox0703/UE5CEDumper/releases/tag/v3397)** (2026-09-06), previous
[v3362](https://github.com/bbfox0703/UE5CEDumper/releases/tag/v3362) (2026-08-26).

This is STATE, not history -- it says what a user running the latest download actually has. The
commit trail is in [dev-log.md](dev-log.md); do not summarise the fixes here, for the reason the
banner above gives.

⚠ **A user on v3397 is not on `main`.** Between a release and the next one, `main` carries fixes
nobody has downloaded, so "is this fixed?" has two different answers. Derive the gap:
`git log --oneline v3397..origin/main`.

### What changed in BEHAVIOUR at v3397 (not a changelog -- these alter the rows below)

* **UE version detection has a second, independent source.** `CrashReportClient.exe` ships with the
  engine rather than being authored by the game team, so its version can never be the *game's*
  version; where it disagrees with the game exe, the log says so. The `UE version override` row
  further down still reads `Auto / 4.18-4.27 / 5.0-5.8` and remains correct.
* **Cached version verdicts re-derive once** on each game's first launch after upgrading.
* **The `dxgi.dll` proxy no longer stops some games launching** (OCTOPATH TRAVELER was the
  reproducer), so the proxy-choice advice in the README is now true for dxgi as written.

-----

## Capability matrix

| Layer | Drill-down | Find Refs |
|-------|-----------|-----------|
| Object / Class / Interface | ✅ | ✅ |
| Weak / Soft{Class} / Lazy (single + array) | ✅ | ✅ |
| TArray of any pointer-shaped inner | ✅ | ✅ |
| TMap / TSet (Object/Class) | ✅ | ✅ (allocated slots only) |
| Delegate (single FScriptDelegate) | ✅ | ✅ (v3) |
| MulticastInline / MulticastDelegate | ✅ | ✅ (v3) |
| TArray<FScriptDelegate> | ✅ | ✅ (v3) |
| MulticastSparseDelegate (UE 5.0+) | ✅ bindings via SPARSE_ES2_1 AOB (build 561-577) | ✅ v4 sparse pass (build 565) |
| MulticastSparseDelegate (UE 4.27) | ✅ same raw-pointer layout as UE5 — `SPARSE_ES2_1` resolves on 4.27, + `SPARSE_DI427_1/2` (build 2399). The "FObjectKey outer key" blocker was a wrong premise, see technical-notes | ✅ |
| MulticastSparseDelegate (UE 4.25) | ✅ PDB-confirmed raw `UObjectBase const*` key, same as 4.27/5.x — `SPARSE_DI427_1/2` + `SPARSE_ES2_1` all UNIQUE-OK (build 2405) | ✅ |
| MulticastSparseDelegate (UE 4.23-4.24) | ✅ **4.23 PDB-confirmed 2026-07-28** on a self-built 4.23.1 sample — raw `UObjectBase const*` key, same as 4.25/4.27/5.x (see [reference-builds.md](reference-builds.md) + `tools/ghidra/GROUND-TRUTH.md`). 4.24 still has no symbolised sample; the walker probes the live key shape and declines if it is not a raw pointer | ✅ (4.23) |
| OptionalProperty\<pointer / weak\> | ✅ | ✅ |
| OptionalProperty\<scalar Int/Float/Bool/Byte/Enum\> | ✅ trailing-bIsSet | — |
| OptionalProperty\<String / Name / Text\> | ✅ intrusive sentinel + value (build 530) | — |
| OptionalProperty\<Struct\> | ✅ (build 528) | ✅ depth-3 descent through inner struct (build 528) |
| FieldPathProperty | ❌ | ❌ |
| TMap / TSet with weak-like inner sides | — | ❌ (v4 candidate) |

## CE export — record flatten / colors / leaf-pointer collapse (build ~1856; PRs #401/#402)

Live Walker → **Options** toggles that flatten the **Copy CE XML / Copy CE Field** output (CSX
stays nested by design). All **address-equivalent** (a flattened row watches the same memory),
**off by default**, persisted in `LiveWalkerUiOptions`. `IsTerminalLeafField` = primitives ∪
`{NameProperty, StrProperty/Utf8Str/AnsiStr}`; `FText` is excluded. See [tips.md](tips.md) for the
user recipe.

| Option | Effect | Encoding |
|---|---|---|
| Flatten primitive-leaf structs | all-numeric struct (FVector, FRotator, FDateTime `Ticks`…) → `Struct ▸ Field` sibling leaves | inline `+(structOff+childOff)`, 0-deref |
| Flatten leaf records (names/strings) | superset: also accepts FName + FString leaves; reaches into **TMap/TArray element structs** (drops the `[i]` folder); no field-count cap | FString child `+combined` `Offsets=[0]`; FName 4-byte; scalar inline |
| Collapse single-leaf pointers | drilled pointer whose target is **one** terminal leaf → one `Ptr ▸ Field` record (vs folder + lone child); multi-field pointee keeps its group | scalar/FName `+ptrOff Offsets=[childOff]`; FString `Offsets=[0, childOff]` |
| Record Colors… | tint flattened **container** rows by element-index parity (CE `<Color>` text colour) | RGB→COLORREF `BBGGRR`; `FlattenColorDialog` + in-app `ColorPickerDialog` (Custom… hue strip + RGB) |

In-game VERIFIED on SEED (`StoryMissionRecord` 222-entry `TMap` flatten + colors); the
pointer-collapse path is unit-tested only (no in-game pointer-to-string case yet).

## Per-game configuration

Persisted in HintCache JSON per PE hash, surfaces in the Pointer panel:

| Setting | Range | Default | Pipe cmd | Since |
|---|---|---|---|---|
| UE version override | Auto / 4.18-4.27 / 5.0-5.8 | Auto (detect) | `set_ue_version_override` | build 549 |
| Invoke timeout | 1000-60000 ms | 5000 ms | `set_invoke_timeout` | build 583 |

## UFunction invoke export (build 590-596)

Three buttons per UFunction row in LiveWalker:

| Button | Mode | Output |
|---|---|---|
| **Generate Script** (`INV`) | In-CE form (existing) | AA Script with `createForm` interactive popup |
| **Pipe Invoke** (`PIPE`) | In-app via DLL pipe | Live invoke + decoded result inline |
| **AA(Baked)** (new) | Non-interactive AA Script | Self-contained AA Script with values baked at generation time; depends on `ue5_invoke_helper.lua` embedded in the user's .CT |

Two ways to get `ue5_invoke_helper.lua` into the user's .CT:

- **Tools -> Inject Helper into Current CE Table** (build 610, preferred) —
  one click; sends the embedded helper straight into the open CE table
  via the AOBMaker plugin's new `InjectTableFile` pipe command (`findTableFile`
  delete-if-exists -> `createTableFile` -> `Stream.write` -> verify).
  Requires the AOBMaker CE Plugin to be loaded; falls back gracefully
  with a status-bar hint if unavailable.
- **Tools -> Export CE Helper Lua File...** — manual fallback when
  AOBMaker isn't installed or CE isn't running. Writes `scripts/ue5_invoke_helper.lua`
  to a user-chosen path; user adds it via CE `Table -> Add File...`.

### Invoke param picker (build 711-715, Stage 1+2)

InvokeParamDialog rows for pointer-flavoured params (UObject* / UClass* /
SoftObject / WeakObject / LazyObject / Interface) now surface the expected
target UClass and provide one-click filling:

- **Label** — `[UObject*: AActor, 8B, off=0x10]` instead of bare
  `[UObject*, 8B]`. The DLL extracts `FObjectPropertyBase::PropertyClass` in
  `WalkFunctions` and ships it via the `obj_class` pipe field; C#
  `FunctionParamModel.ObjectClassName` carries it to the UI. Empty when the
  param genuinely has no class constraint or when an older DLL is in use
  (backward-compatible).
- **[Pick…]** — opens `ObjectInstancePickerDialog` pre-filtered to the
  expected UClass via the existing `find_instances` pipe command. Substring
  match catches subclasses (which is what the param actually accepts). Double-
  click row or "Use selected" fills the textbox with the chosen instance's
  address.
- **[null]** — fills `0x0` (WorldContextObject and other optional pointer
  params).
- **[self]** — fills the invoke target's own address (for utility functions
  that re-target themselves).

`ParamBufferBuilder.IsPickablePointerType` is the canonical 7-type list
locked by test theories — adding a new pointer property type requires
mirroring it in both the DLL `WalkFunctions` extraction and the C# helper
or the build catches the drift.

Stage 3 (class validation — DLL `validate_object_class` round-trip before
invoke) deferred until a real class-mismatch crash motivates it; picker
output is almost always class-correct in practice.

## Value Search — by-value scan (build 738 + Phase 2 build 757)

New tab "Value Search" between Interesting Properties and Console. Fills
the search-by-value gap (PropertySearch was by-name; InstanceFinder was
by-address; this is the third axis). Port of discrete's Phase 27b
`ValueScanSession` shape with UE-specific scan engine. Phase 2 expansion
(build 757) extends from numeric primitives to the three deferred type
families.

| Component | What it does |
|---|---|
| `ValueScan::SessionManager` (DLL) | 5-min idle-expiry session container; holds candidate vector + DataType between RPCs. |
| `Aura::ScanForValue` | Walks GObjects (skips UClass meta), per-class field index cached lazily via `Ubel::WalkClassEx` filtered to matching DataType, typed-read + `ComparePredicate` for primitives, `CompareStringPredicate` for strings, `CompareVectorPredicate` for vectors. TArray inner expansion when ArrayProperty's Inner matches. Caller-supplied deadline. |
| `Aura::RefineCandidates` | Re-reads each candidate's bytes/string, applies predicate (prev-value scan types compare against stored `Candidate.prevValue`/`prevStr`), prunes failing, updates snapshots on survivors. Re-reads strings via `c.addr` so array-element strings work uniformly with direct string fields. |
| `Fern.cpp` handlers | `begin_value_scan` / `refine_value_scan` / `end_value_scan` / **`query_candidates`** (V3-C). Wire schema (Phase 2): optional `case_sensitive` for string DataTypes, `tolerance` now also applies to FVector/FRotator (per-axis). `IsScanTypeValidFor` gating rejects nonsensical combos (`FString + Bigger`, `Int32 + Contains`) with explicit errors. **V3-C (build 949):** begin/refine return `total` + only the first `page_size` page (scan order); `query_candidates(session, offset, limit, filter, sort_key, sort_desc)` filters + sorts the WHOLE session set server-side (over the DLL's own pools — no game-memory reads) and returns one window. The DLL session is the single dataset owner; the UI is a windowed view. |
| `ValueSearchPanel.axaml` | Banner: "Native C++ fields (non-`UPROPERTY`) are invisible to a reflection-driven scan. **Enable \"Native-C\" below to scan those raw.** Use Cheat Engine's raw memory scan for full control." (It was a hard lock until Native-C scanning shipped.) DataType + ScanType selectors (ScanType dropdown filters per DataType via `VisibleScanTypeOptions`), Value/Value2 inputs, Case-sensitive checkbox (string types only), Tolerance NumericUpDown (float/vector types), Candidates DataGrid. |

**Supported DataTypes** (build 757):
- **Numeric** (MVP, build 738): Int8/16/32/64, UInt8/16/32/64, Float, Double, Bool (BoolProperty bitfields normalised to 0/1 via FieldMask).
- **String** (Phase 2A, build 757): FString, FName, FText (best-effort — cooked games strip most display strings; ES2 resolved 1/1551 classes).
- **Vector** (Phase 2B, build 757): FVector, FRotator. **FTransform** wire-stable but currently returns zero hits pending per-version Translation offset detection (tracked as todo `#0c FTransform Translation offset`).
- **TArray\<T\>** (Phase 2C, build 757): walks reflected ArrayProperty buffers for primitive / string / vector inner types. Soft circuit-breaker on `Num > 10M` (skip with `LOG_WARN`), Num/Max/Data validation guards. Matching elements appear as `FieldName[N]` rows.
- **TSet\<T\> / TMap\<K,V\>** (V1a, build 927): walks the FSet/FMapProperty `TSparseArray` (allocated slots only via `IsSparseIndexAllocated`) for any supported leaf DataType. Map key and value are scanned independently (a `TMap<int,int>` with Int32 emits both). Rows render as `Set[idx]` / `Map.Key[idx]` / `Map.Value[idx]`. Reuses the same sparse-walk geometry (`Ubel::GetSetElementStride` / `GetMapPairLayout`) as the container-aware Address Finder. Element addresses are raw, so refine degrades on a container reallocation exactly like TArray (SEH-safe read drops the candidate).
- **TOptional\<T\>** (V1c, build 942): scans an `FOptionalProperty` whose wrapped value matches the requested DataType (numeric / string / vector). The value is read inline at the optional's own offset (field+0, same as a direct leaf — `FOptionalProperty` shares `TArray`'s Inner-at-`FARRAYPROP_INNER` shape). Non-intrusive optionals (`{ T value; bool bIsSet; }`) are **gated on the trailing `bIsSet` byte** (offset = `sizeof(T)`, computed by `ValueScan::OptionalFlagOffset` from the inner element size) so a scan for `0` / stale bytes doesn't false-hit unset slots. Rows render under the optional's own field name; refine re-reads `c.addr` (a stable address, unlike sparse-container slots). Drilling into a `TOptional<FStruct>` for nested leaves remains future.

**Scan bounds** (how many array elements get scanned, and the global caps):
- **Per-array: ALL elements** are scanned (`for idx = 0 .. Num-1`) — Value Search deliberately does **not** inherit the export-side array-size cap, because a hit at a high index is still a legitimate hit (see memory `project_value_search_caveats`). The only per-array limit is the **soft circuit-breaker**: an array whose `Num` is negative or `> 10,000,000` is skipped whole with `LOG_WARN` (corruption / freed-memory / OptionalProperty-misread guard, not a "scan first N" cap). `Max < Num`, null `Data`, and `Num == 0` are also skipped.
- **Global `maxResults` cap** (default 50,000; UI-configurable 100..**1,000,000** since V2/build 954): bounds the total candidate count **across all objects and array elements combined**, not per array. In the parallel walk it's a per-thread local cap, then the ascending-tid merge truncates to `maxResults` (so the kept set is the lowest-index matches the serial walk would have stopped at). Hitting it sets no special flag — the user simply sees fewer than the true total; raise Max or narrow the predicate. **Since V3-C (build 949)** the UI no longer materializes the whole set: begin/refine return `total` + the first page, and the grid pages via `query_candidates` (server-side filter/sort over the full set). The DLL holding a large set is cheap (lean Candidate, V3-A). **V2 (build 954)** then raised the ceiling to 1M and verified the server-side ordered view stays sub-second at that size (~640 ms sort / ~715 ms filter over 1M in the scale bench).
- **Deadline (user-adjustable)**: the scan bails at the deadline and sets `deadline_hit` → the status row shows a "scan truncated" note and points at the Timeout slider. **10–90 s, default 25** (`ValueSearchViewModel._scanTimeoutSeconds`); it was a hard 15 s once, and `Constants.ScanSessionDeadlineMs = 15000` survives only as the interface default for callers that pass nothing — Value Search always passes its own.

The combined effect: ordinary arrays (equipment/buff lists, etc.) are fully enumerated; the practical ceiling a user hits first is usually the 50k global cap, especially with NumericAll on a small value.
- **NumericNoByte** (multi-numeric meta, build 794-795): one pass over **every** word/dword/qword/float/double field, each compared by its OWN declared width — the "I know the value but not whether it's int or float" starting point. Distinct from CE's raw "All" (no byte-reinterpret false hits — our walk knows each field's declared type). Excludes Int8/UInt8/Bool to prevent small-value result explosion. `BuildNumericTargets` pre-parses the value into one buffer per fitting width (`70000` → no 16-bit; `100.5` → float/double only; hex → integer only); each field resolves its own DataType via `TryDataTypeFromPropertyTypeName` and compares against the matching buffer. Results-grid Type column shows which width matched. Tolerance applies per-member to float/double fields only.
- **NumericAll** (multi-numeric meta with byte, build 796-797): same one-pass scan as NumericNoByte but **including** the 1-byte families (`Int8Property` → Int8, `ByteProperty` → UInt8; Bool still excluded). 10 members. Use when the value really is a byte. Because small values (0/1/255) match a huge number of 1-byte fields, the panel shows an orange **result-volume warning** (`ValueSearchViewModel.DataTypeWarning`) whenever NumericAll is selected. `BuildNumericTargets` range-gates byte widths (`300` → no Int8/UInt8; `-5` → Int8 yes / UInt8 no).

**ScanType matrix**:
| DataType family | Valid scan types |
|---|---|
| Numeric | Exact / Bigger / Smaller / Between + Changed / Unchanged / Increased / Decreased |
| Vector | Same 8 (component-wise per axis; "Increased" triggers when ANY axis moves up) |
| String | Exact / Contains / StartsWith / EndsWith + Changed / Unchanged |

Native C++ (non-`UPROPERTY`) fields are **opt-in, not excluded** — the "Native-C (raw)" toggle scans the holes inside `PropertiesSize`; see [native-c-value-scan-spec.md](native-c-value-scan-spec.md). Cross-tab navigation: per-row "Open in Live Walker" opens the owning instance with the field address pre-populated.

**Live verification (ES2, UE 5.5, build 757)**: FString Contains "Engine" → 54 candidates / 323ms; FName Contains "Engine" → 7119 / 415ms; FText Contains "Engine" → 1 / 396ms; FVector Exact tol=0.01 → 49966 / 303ms; FRotator Exact tol=0.01 → 16819 / 290ms. No `LOG_WARN: skipping TArray` fired across ~1.15M scanned objects.

### Multiple Values Group Scan (object-aware "Group Scan"; builds 1276-1319)

A **Single / Group** toggle in the Value Search tab. Group mode finds **objects** that simultaneously hold ALL of N values (2–4) at **distinct** numeric-property offsets, in any order (Str + Def + Dex + Int in one stats object) — multiplicatively more selective than N separate scans. Full design + extension points (the source-agnostic `Orden` SDR matcher = the reuse seam for Snapshot/SPC/Pivot): [group-value-scan-spec.md](group-value-scan-spec.md).

- **P1 + Deep** (builds 1276-1285): per-object numeric leaves (direct + struct descent); an opt-in **Deep** checkbox additionally matches a group *within* one nested numeric container / struct-array element (shared with the single-value deep pass). In-game verified on SEED (UE4.27). **Scalar-valued/keyed maps** (`TMap<Name,int>`) captured too (builds 1561-1562; value block `<map>.Value`, key block `<map>.Key`) — the shared `WalkContainerLeaves` now emits map scalar sides for deep Value Search / Snapshot / Group Scan.
- **P2** (builds 1295-1302, MERGED PR #311): **per-slot scan type** — First Scan `Exact / Bigger / Smaller / Between` (Between = the bounded-unknown entry, e.g. an HP bar in [1,100]); Next Scan also the prev-value four `Changed / Unchanged / Increased / Decreased` (compare each located leaf vs its own previous round). A **locked-offset table** (`🔒 Class — Str@0x20, Def@0x24`) appears once every slot converges. (A "Copy CE / export" was deliberately dropped — export the chain from Live Walker.) prev-value refine in-game verified on SEED.
- **P4 increment 1** (builds 1303-1313, MERGED PR #313): an opt-in **Cross-object (owned components)** checkbox folds the numeric leaves of the sub-objects an actor OWNS (its components + a GAS ASC's `SpawnedAttributes` → `UAttributeSet`, a 2-level owned BFS gated by an Outer-chains-back test) into the actor's block, so a group whose values span {actor, components, attribute sets} matches. Ownership + value driven, not class-name driven. In-game VERIFIED on TQ2 (UE5.07, GAS).
- **P4 increment 2** (builds 1318-1319): each slot carries an `owner_class` so a cross-object slot's **Pivot** handoff lands on the owned sub-object's class (the stats component / `UAttributeSet`) instead of the candidate actor's. The object handoffs (Live Walker / Locate) already targeted the sub-object by address (inc 1); this completes the class side. Closes P4.

### Snapshot Group Match — Multiple Values over a captured snapshot (S1–S5, builds 1563-1569, MERGED main PR #348; in-game VERIFIED on SEED)

The same object-aware Group Scan, run over the **captured-snapshot** corpus via a pure-C# port of the `Orden` SDR matcher (the seam group-value-scan-spec §3.1 reserved). A **Diff / Group** toggle in the Snapshot panel. **Mode A** finds objects holding N absolute values in one snapshot; **Mode B** (pick a "Compare with" older snapshot) compares each value across two snapshots with per-slot `Changed / Unchanged / Increased / Decreased` — the **"Current HP↓ + Max HP unchanged"** case (groups need `Unchanged`); **S5 Deep** folds nested array / struct-array element values into the owner's block. No DLL/schema change. Spec: [snapshot-group-match-spec.md](snapshot-group-match-spec.md).

### SPC Group — Multiple Values over N snapshots with per-slot chains (builds 1575-1584; in-game VERIFIED on SEED)

The **N-snapshot, object-aware generalisation** of Snapshot Group Mode B, as a **Single / Group** toggle in the SPC Query tab. Where Mode B compares 2 snapshots first-vs-last, SPC Group lets each of the 2–4 value-slots carry its **own per-snapshot predicate CHAIN** (the SPC directional chain + per-snapshot absolute window), and finds OBJECTS whose N fields each follow their chain at distinct offsets — e.g. *Current HP `·→↓→↑`* while *Max HP `·→=→=`* over `[full, damaged, healed]`. Reuses the SPC intersection load + `SpcEngine` chains + the `GroupMatch` SDR; **Deep (struct-array elements) is inherent** (the SPC load already includes `array_field` rows). The N×M predicate matrix is edited one slot-column at a time (an "Editing: Value N" selector + a prominent active-slot chip). No DLL/schema change. Spec: [group-value-scan-spec.md](group-value-scan-spec.md) §3.1. *Remaining same-matcher reuse: Class Pivot "co-varying tuples".*

## Multi-row → One .CT batch generator (build 760, pick #3)

Interesting Functions + Interesting Properties tabs gain a **📦 Generate CT** toolbar button that wraps the current DataGrid multi-select (`SelectionMode="Extended"`) into a single ready-to-share `.CT` file. Promotes the discover→use workflow from "research toy" to "shareable cheat-table author".

| Component | What it does |
|---|---|
| `Models/CheatTableRow.cs` | Discriminated row type (`CtPropertyRow` wraps `FreezeScriptParams`; `CtFunctionRow` wraps Baked params). Source-panel-agnostic so future call sites (LiveWalker, Live PE Profiler) can feed the same builder. |
| `Services/CheatTableBuilder.cs` | `Build(title, rows)` → CE XML matching CE's File→Save As shape. Root group → per-category sub-groups (alphabetical; "Uncategorised" trails) → one CheatEntry per row with `<VariableType>Auto Assembler Script</VariableType>` body. IDs sequential from `BaseId=1000`. XML-escapes all five canonical entities so `TArray<int>` / `&` / quotes in descriptions can't break a CT load. |
| `MainWindowViewModel.SaveCheatTableAsync` | Platform save-file dialog with `.CT` filter; writes UTF-8 (no BOM, matching the bundled UE5CEDumper.CT). Logs source label for grep. |

Per-row defaults users edit in CE before activating:
- **Properties**: per-UE-type "obvious cheat" freeze literal (Float = `9999.0`, Int = `99999`, Bool = `true`, Byte = `255`). Struct / array / non-scalar rows are filtered out (`FreezeScriptGenerator.IsTypeSupported`); status text reports the skip count.
- **Functions**: BakedValues intentionally empty (helper zero-fills PARAMS); description for parameterised funcs reads `"Class::Func (N (XB)) — edit baked PARAMS in CE"` so users know to populate before activating.

Default filename: `{Source}-batch-yyyyMMdd-HHmmss.CT` (Source = `InterestingProperties` or `InterestingFunctions`).

Not yet wired into LiveWalker (heterogeneous row types — functions + fields + struct sub-fields + array elements — need their own UX pass; tracked in todo.md as `LiveWalker batch generator v2`). AOBMaker direct-inject of the generated CT is also v2.

## UFunction Return Value structured walker (build 775, pick #5)

InvokeParamDialog FIRE result now shows a 4-column DataGrid (Field / Type / Value / Offset) below the existing single-line decode when the return is a StructProperty. Renders FVector / FRotator / FHitResult / user USTRUCT returns as a property grid instead of `"X=1.0, Y=2.0, Z=3.0"` comma joins.

| Component | What it does |
|---|---|
| `Models/StructFieldValue.cs` | Pure record (Name, Type, Value, **absolute** buffer offset = return param offset + sub-field offset). |
| `Services/StructReturnDecoder.cs` | Resolution order: `KnownStructLayouts` (per-version locked) → DLL-discovered dynamic `StructFields` → empty list. Delegates each byte→typed-value cell to `InvokeParamDialog.DecodeParamValue` so the grid and result-label never disagree on a byte mapping. SafeDecode wraps with try/catch — a single bad field doesn't blow the whole grid. |
| `InvokeParamDialog` | Pre-resolves `_returnParam` at construction; clears + hides grid at top of `OnFireClicked` so stale rows don't flash across invocations; header label includes struct name (`"Return value (decoded — Vector):"`). Columns use `DataGridTemplateColumn` + `FuncDataTemplate<StructFieldValue>(lambda)` — AOT-clean (build 780 fix), zero IL2026/IL3050 warnings on publish. |

**v2 follow-ups** (deferred, tracked in todo.md):
- **ObjectProperty return resolution** — needs DLL pipe round-trip via `Ubel::GetName` on the returned address; pointer returns currently render as 8-byte hex in the existing single-line decode only.
- **Recursive struct expansion** — `FHitResult.Location` (FVector) renders as one `Location (StructProperty)` row; nested expansion needs recursive DLL-side discovery in WalkFunctions.

**Verification target (Geri, UE 4.27)**: `PlayerCameraManager::GetCameraLocation` returns FVector — grid should show 3 rows (X / Y / Z floats) at offsets 0x4 / 0x8 / 0xC of the post-call param buffer.

## UCheatManager stripped-body hint (build 778, pick #6)

Console panel surfaces an orange-bordered footer warning when the selected exec's class or super name contains "CheatManager" (case-insensitive substring). Redirects users from the `Result=0 + no in-game effect` failure mode (UE wraps these in `#if !UE_BUILD_SHIPPING` — reflection metadata survives the cook, function bodies don't) to a game-specific verification target.

**Discriminator** (locked in `feedback_ucheatmanager_stripped` memory): `Stark::GetHookFireCount()`:
- `>0 + Result=0 + no effect` = cooker-stripped body (the bug this hint addresses)
- `==0 + Result=0 + no effect` = hook on wrong vtable slot (closed by build-648 pattern scan)

Detection lives in `ConsoleViewModel.IsLikelyUCheatManagerExec(entry)` — public + static, tested across 10 row variants (engine class / game subclass via own name / subclass via super name only / case-insensitive / 4 negatives). Out of scope for v1: full super-chain walk to catch second-degree subclasses (`BP_MyCheatManager_C : MyGameCheatManager : UCheatManager`) — would need a new `walk_super_chain` pipe call; revisit if false-negatives surface.

`docs/lessons-learned.md` gained a corresponding bullet under "UFunction Invoke / ProcessEvent" so the diagnostic flow survives across sessions even when memory rotates.

## Game-patch diff: `scripts/analysis/diff_dumps.py` (pick #4)

Pure-Python sister to `analyze_dumps.py` — consumes the same `Dump All Metadata` JSONL corpus but does N=2 patch-vs-patch diff at UProperty / UFunction granularity. Closes the cheat-table-maintainer pain when a game ships a silent offset shuffle.

Match keys: class `path` (canonical UE id; `addr` is session-local). Path normalisation (`//Script/X` ≡ `/Script/X`). Properties + functions matched by name within class.

Report sections: Added / Removed classes; **Moved fields** (same name, different offset/size); **Type changed** (FloatProperty → DoubleProperty incl. inner_type / struct_type / obj_class / enum); Added/Removed properties; **Function signature changed** (return_type / num_parms / parms_size / flags); Added/Removed functions; per-class `props_size` delta.

CLI flags:
- `--minimal` — breaking changes only (moved + sig-changed), hides added/removed lists.
- `--include-engine` — opens up `/Script/` classes (default skips them since they rarely shift across game patches).
- `--self-test` — 6 built-in synthetic-fixture scenarios; no external dumps needed.

Known limitations (documented in README): no auto-rename detection (renamed field shows as Removed + Added); no function body comparison — only metadata is dumped (logic-only refactors are invisible, covered by todo pick #2 Live PE Profiler in the future).

## Property freeze — horizontal class-wide (build 719)

PropertySearch rows gain a **Freeze** button that ships an AA Script
into CE locking the property at a constant across **every live instance**
of the owning class **and of every subclass of it** (2026-08-19 — an
exact-class pool held only the class that DECLARES an inherited field,
which for `bCanBeDamaged` is `Actor` and never the player's pawn).
Different from the CE-XML export (Route A — kept
in [todo Speculative](todo.md)) which pins a single pointer chain to a
single instance: the freeze script enumerates instances by class+offset
every 5 s, so respawns / new spawns / destroys are handled transparently.

| Component | What it does |
|---|---|
| `Mimic::CMD_LIST_INSTANCES` (mailbox cmd 6) | Paginates live (non-CDO) `UObject*` pointers of a class. **Two scopes, chosen by `cmdFlags & LI_IN_DERIVED` (mailbox contract 3):** clear = `Aura::FindInstancesByClass(exactMatch=true)`, 8-byte entries, 128/page, cap 2000; set = `Aura::FindInstancesDerivedFrom` (the class **and every subclass**), 16-byte entries carrying a per-instance `UClass*` witness, 64/page, cap 1024. The flag defaults OFF and is cleared after every use, so a pre-contract-3 `.CT` keeps the exact-match pool byte for byte. A capped pool is reported back as `LI_OUT_TRUNCATED`. |
| `scripts/ue5_freeze_helper.lua` (embedded, **v1.3**) | `freezeProperty(cfg) → handle` API; tick timer (50 ms default) + rescan timer (5 s default); type writers for bool / int8-64 / uint8-64 / float / double; shares `_ue5_invoke_busy` reentrancy flag with invoke helper. **`handle.start()` returns `(ok, err, count, capped)`** (1.2 added the first three, build 3125; 1.3 added `capped`) so a caller can separate a HARD failure (no DLL / contract mismatch → untick + keep the window open) from a freeze that is legitimately **armed with no live instances yet** (stays ticked; applies as they spawn), and can say when `count` is a floor rather than a total. **1.3 also adds `cfg.derived`** (default TRUE — hold every subclass, because a Property Search row for an inherited field is keyed to the class that DECLARES it) and **`cfg.memrec`** (an abandoned freeze disables its own CE record instead of leaving a checkbox claiming a cheat nothing is applying). *In-game verification pending (key: FreezeOutcome)* |
| `FreezeScriptGenerator` | Renders AA Script with editable `CFG = {...}` block (incl. `derived`), per-script keyed handle table so multiple Freeze scripts coexist, and wires the CE record through as `CFG.memrec`. |
| `FreezeValueDialog` | Single-input modal with type-aware validation (bool accepts true/false/1/0), plus a **Scope** line naming every object the freeze will write to and a warning when the field is inherited (so "freeze my pawn's `bCanBeDamaged`" is not read as one object). |
| `Tools -> Inject/Export Freeze Helper Lua` | Sister entries to the invoke-helper Tools menu — one-click AOBMaker inject or manual file export. |

Supported property types (v1): BoolProperty, ByteProperty, Int8/16/32/64Property, UInt16/32/64Property, EnumProperty, FloatProperty, DoubleProperty. **Not supported**: StructProperty / FString / FName / containers (deferred).

Gating: the Freeze button is disabled when AOBMaker plugin isn't reachable; tooltip explains the setup requirement. **No clipboard fallback** — script delivery is AOBMaker-only by design (keeps the surface tight).

## Interesting Functions Finder (build 597-609)

New tab "Interesting Funcs" between Property Search and Game Classes.
Backed by the `list_all_functions` pipe cmd which flattens every
UFunction across every UClass into a single one-shot payload (~4MB
for a 50k-function game). Client-side scoring via
`KeywordScoringTable` + `KeywordTokenizer` (build 609 -- whole-token
match instead of substring, so short acronyms HP/MP/SP/XP/TP only
fire on standalone tokens):

- **Categories**: Stats / Inventory / Movement / Combat / Utility (with
  ExplicitMovementCheats sub-bucket: NoClip/Fly/God/Ghost/Invincible
  weighted +8 to outscore Utility's noisy `Cheat` + `Debug` matches)
- **Class bonuses**: Character/Pawn/PlayerController/PlayerState +3,
  Player +2 (build 673), Enemy / Weapon +2 (build 687 — Phase 2),
  GameMode/GameInstance/SaveGame +2; AnimInstance/AnimMontage/AnimSequence/
  AnimNotify/AnimGraph/AnimBlueprint -2 (build 673 — surgical compound
  names, was bare "Anim" before which broke game classes like AnimMan_*),
  NiagaraSystem/NiagaraEmitter/NiagaraComponent/SoundCue/SoundWave/
  SoundBase/AudioComponent/ParticleSystem/ParticleEmitter -2,
  UserWidget/WidgetComponent -1
- **Flag bonuses**: BlueprintCallable +2, BlueprintEvent +1, Pure-or-
  Const safe getter +1, ParmsSize > 64 -1
- **Threshold = 5**; Show All toggle bypasses

Per-row actions:
- **Live**: open in Live Walker via `find_instance` lookup, falls back
  to ClassStruct tab if class is CDO-only
- **AA(B)**: shortcut into the Copy AA Script (Baked) flow; reuses the
  same dialog as the LiveWalker AA(Baked) button

**AOBMaker availability gating** (build 608, refined build 689) — when
AOBMaker CE Plugin pipe is unreachable, both LiveWalker Functions and
Interesting Funcs panels show a single inline italic status indicator
("AOBMaker plugin not found — AA Script export will fall back to
clipboard"). Was previously a per-row Notes column on every row (pure
noise since the value is VM-level); build 689 collapsed it to one
place. Re-checked on tab activation (5s cooldown so rapid switching
doesn't stack 2s pipe-connect timeouts). Send-time guard distinguishes
"pipe broke during send" (warning) vs "plugin never configured"
(informational).

## Interesting Properties Finder (B' — build 670-687)

Symmetric tab to Interesting Funcs but for properties. Backed by
`search_properties_batch` (build 685) — DLL walks GObjects ONCE and
checks every property against every keyword in one pass, ~30× faster
than the build-670 sequential approach. Uses `PropertyScoringTable.cs`
(separate from KeywordScoringTable since property naming differs from
function naming) + shared `ClassLocationScorer.cs`:

- **Categories**: Stats / Combat / Resources / Movement / Utility
  (no Inventory — uses Resources instead; no ExplicitMovementCheats —
  property names rarely encode cheat-mode verbs)
- **Class bonuses (PropertyRules)**:
  Character/Pawn/PlayerController/PlayerState/AbilitySystem/AttributeSet/
  Inventory/Equipment +3; Player +2; GameMode/GameInstance/SaveGame/
  PlayerProfile +2; **LocalPlayer / GameViewportClient / HUD /
  UCheatManager / CheatManager +4 with ⚠ Unusual Location flag**
  (build 670); Weapon / Projectile / Battle / Enemy +2 (build 678 + 687
  — empirically derived from 15-game cross-game analysis)
- **No visual/audio penalties on Property side** — property names alone
  filter the noise (an "PlaybackSpeed" on UAudioComponent doesn't match
  any keyword, so it scores 0)
- **Threshold = 4** (slightly lower than Function side because per-hit
  weights are lower)

Key concept: **Unusual Location flag** highlights cheat-relevant fields
hosted in non-canonical containers (LocalPlayer / GameViewportClient /
HUD / CheatManager) — the kind of properties developers placed outside
where you'd think to look first.

Per-row actions:
- **Live**: open the property's owning class in Live Walker via
  `find_instance`, fall back to ClassStruct on CDO-only classes.
  Pre-fills the LiveWalker SearchText with the property name so the
  user lands with it highlighted.
- **Name**: copy the bare property name to clipboard.

## Dump-for-analysis pipeline (build 676-687)

`Export → Dump All Metadata (.jsonl)` streams every class + props +
funcs as JSON Lines via the existing pipe endpoints (`get_object_list`
+ `walk_class` + `walk_functions` — no new DLL command). Mirrors the
`IsClassLikeMeta` whitelist so BPGCs are included.

Companion Python script `scripts/analysis/analyze_dumps.py` aggregates
N dumps cross-game and emits a Markdown report with:

- Top OWN property names (with `_resolve_own_props` filter to dedup
  inherited fields counted N times across the inheritance chain)
- Top OWN property TOKENS — candidate keywords, cross-referenced
  against existing category buckets
- Candidate Unusual Location class tokens — class × prop-token
  co-occurrence ranked by cross-game frequency
- Same three sections for the Function side (build 687)
- `--min-games` filter (default 3) drops single-game spikes

15-game corpus (DQ7R / DQI&IIHD2D / ES2 / FSD-DRG / FactoryGameSteam /
Geri / HogwartsLegacy / ManorLords / NMKART / Octopath / Stray / TQ2 /
TowerOfMask / ff7rebirth / ff7remake) drove the build 678 + 687 scoring
table additions. Two subsequent bias rechecks at **17 games** (Star Wars
Jedi: Fallen Order + Ghostwire: Tokyo, 2026-05-12) and **18 games**
(Frontiers — first MMO/ARPG-flavoured entry, 2026-05-20) confirmed
stability with **no further keyword additions** in either pass. Anti-
bias workflow documented in
[scripts/analysis/README.md](../scripts/analysis/README.md) — users
whose preferred genres aren't well-represented dump their own games +
PR with analysis output as evidence.

## Publisher detection

`Genau::DetectPublisherFromPE` reads `LegalCopyright` + `CompanyName`
via `VerQueryValueW` and matches against `kPublishers[]`. A match
forces `bLowConfidence=true` (override the Tier promotion) AND
applies the publisher's `biasFallback` when detection fails. Currently:

| Publisher | Bias fallback | Reason |
|---|---|---|
| `SQUARE_ENIX` | UE 4.27 | UE4 forks shipped without canonical version strings + bundled SDKs leak misleading 5.x numbers |

Adding more entries casually risks wrong bias overriding correct
detection — wait for a real misdetection report before adding.

**Version detection is cached per `peHash` (build 1521).** The slow
`DetectVersionDetailed` memory string scan (5+ s on large games) runs only
**once** per game build; subsequent connects reuse the cached version when it
was stamped by the current `Genau::kVersionDetectLogicRev`, so stripped-version
publisher games (Square Enix) no longer re-detect every connect. The
low-confidence badge stays honest — the cache-reuse path re-applies publisher
low-confidence **live** (`bLowConfidence = cached || publisher!=nullptr`), since
`DetectPublisherFromPE` runs every launch. **Changing a `biasFallback` value
above requires bumping `kVersionDetectLogicRev`** so already-cached games
re-detect under the new bias. Force a fresh detect anytime via the per-game
Delete-cache button; a UE version override still wins over everything.

## Tested games (last verified 2026-06-11)

- **Everspace 2** ✅ (UE 5.4): item template ID via container scan; Find
  Refs v3 returns 9 correct references in 224ms (cache hot, scan
  complete: 1180536/1180536); auto-scroll-to-field after Open works;
  Class Structure for `LocalPlayer` shows correct fields after the
  class-like routing fix; PropertySearch type filter `OptionalProperty`
  finds 9 matches across 5 real classes + 4 test-object fields.
  **`SPARSE_ES2_1` resolves SparseDelegates @ +9AA5F10** (build 575,
  ground truth from PDB).
- **Titan Quest II** ✅ (UE 5.7, 486k objects): cross-version validation —
  same `SPARSE_ES2_1` AOB hits `+D46D170`. ⚠ The
  `bCasePreservingName=**true**` claim that used to be on this row is
  **REFUTED** — a live inject logged `votes standard=20, CPN=0` / `CPN=no`
  (see `docs/test-games.md:13`), so this does **not** exercise the
  case-preserving walker branch (pair slot 0x10, inner stride 0x28); that
  branch has never been run against a real game.
  Was source of 194 `ValidateArrayElemSize` warnings/session pre-build
  583 → now Debug-only.
- **DQ I&II HD-2D / FF7 Rebirth / FF7 Remake** (UE4 forks, Square Enix
  publisher): Square Enix publisher detected → ⚠ Low Confidence badge +
  Publisher chip; user can set Override = UE 4.27 / 4.18, persists
  across launches. Char Lv / HP / Party Lv in non-reflected memory
  (custom allocator) — out of reflection scope; use CE pointer scan.
  **Build 589 verified**: invoke_timeout=6000 round-trip OK after
  `FillPointerSnapshot` fix; Square Enix purple chip + Low Confidence
  amber badge both surface from `scan_status` payload now.
- **Meltopia** ✅ (UE 5.0.5): full scan OK; was source of ~75
  misalignment + ~58 empty-map false-positives + 4 UFunction timeouts →
  all resolved in build 582-583 (Scharf alignment helper + empty-map
  guard + per-game invoke timeout 6000ms via UI NumericUpDown).
- **Squirrel With A Gun** ✅ (UE 5.0.2): full scan OK; was source of
  `walk_instance` `std::invalid_argument` crash on unsubstituted CE
  placeholder `0x[ply_base]` → resolved by `Renge::TryStrToAddr` in
  build 582.
- **Caravan Sandwitch** ✅ (UE 5.0.4): full scan OK; was source of 49
  empty-TMap false-positives → resolved by count=0+Data=null guard.
- **Retro Rewind Demo** ✅ (UE 5.0.4): full scan OK.
- **The Occupation** ✅ (UE 4.19): UE4 path with `GNAM_CT3`, GWorld OK.
- **TimeSplitters Rewind Early Access V0.3.3** ✅ (UE 4.25): full scan,
  GWorld OK.
- **The Artisan of Glimmith** ✅ (UE 4.27, exe `Geri-Win64-Shipping.exe`,
  24K objects): full scan + GWorld OK. Build 647 cross-version
  reproducer for the wrong-vtable-slot bug (PE was on `vtable+0x220`,
  the old detector picked `0x218` — off by 1 slot) — **fixed and
  fully re-verified on build 648 (2026-05-11)**: pattern scanner
  picks the correct slot, validator confirms 1260 hook fires in
  1500ms, and four real invokes succeed: KismetMath helpers (Add_IntInt
  = 7, Multiply_FloatFloat = 12) via static-native fast path, plus
  instance methods via game-thread dispatch (CharacterMovementComponent
  ::GetMaxJumpHeight = 89.99 float, PlayerCameraManager::
  GetCameraLocation = FVector struct).
- **Squad-Win64-Shipping** ✅ (UE 5.7, 240K objects): build 488 user
  reported 13 `get_object_list` 0xA0 UTF-8 exceptions → root cause was
  Serie wide-path surrogate encoding bug, fixed in build 555. Should
  now work clean post-560.
- **Barn Finders** ✅ (UE 4.25, 137K objects, build 560 user logs):
  full scan OK, UE5-Extended layout (strict). GWorld ✅. No new issues
  surfaced — pre-existing `find_by_address` `stoull` exception on
  malformed `0xrank` input from the Lookup field is already fixed in
  build 561+ (UI side `AddressHelper.TryNormalizeAddress` + DLL side
  `Renge::TryStrToAddr` noexcept). Walker Misaligned-EnumProperty
  warnings (163 in session) cleaned up by `Scharf.h` in build 582.
- **Colossal** ✅ (UE 5.03, 41K objects, build 560 user logs, publisher:
  Atan, exe `Colossal-Win64-Shipping.exe`): full scan OK, UE5-Extended
  layout (strict), TaggedFFieldVariant (UE5.3+). GWorld ✅
  (`GWLD_ES2_6`). Project still ships Epic default copyright/company
  placeholder strings — no publisher thumbprint match expected.
- **Extinction** ✅ (UE 4.15, 230K objects, build 560 user logs,
  publisher: Modus Games, exe `Extinction.exe` under `Blink/Binaries/
  Win64/`): flat-array UE 4.15, verified end-to-end. **Not the floor** —
  NEKOPALIVE (UE 4.11) is, and `Grimoire::MIN_SUPPORTED_UE_VERSION = 411`
  matches it; this row said "expands the 4.18+ floor down to 4.15" until
  2026-08-06, two engine revisions after that stopped being true. Flat (non-chunked)
  `FFixedUObjectArray`, UProperty mode (UE < 4.25), `UField::Next=+0x28`.
  Patterns: GOBJ_RE2 (1.8s, 2 batches) / GNAM_CT3 (4.6s, 4 batches) /
  GWLD_G42_1 (3.3s, 3 batches) — ~10s total scan but all three globals
  resolved on first scan and validated. GWorld ✅.
- **Star Wars Jedi: Fallen Order** ✅ (UE 4.21, 313 887 objects, build
  704 user logs 2026-05-12, EA Origin / Steam launcher): full scan OK —
  GObjects=0x7FF7316F5CD0, GNames=0x12B65A10080,
  **GWorld=0x7FF7317EBAB8** (non-zero, valid). DynOff full UE4 layout
  (`UField::Next=+0x28`, `UStruct::ChildProperties=+0x50`,
  `UProperty::ElemSize=+0x34`). Install path
  `H:\SteamLibrary\steamapps\common\Jedi Fallen Order`, exe layout has
  TWO identical 58.4 MB copies side-by-side in `SwGame\Binaries\Win64\`:
  `SwGame-Win64-Shipping.exe` (canonical UE name) +
  `starwarsjedifallenorder.exe` (EA-launcher target name). CE shows the
  running process as the latter. **Proxy DLL caveat**: neither
  `version.dll` nor `dinput8.dll` proxy gets loaded by the EA launcher —
  must inject via Cheat Engine after the game is running. Scan +
  dump pipeline works identically once the DLL is in-process.
- **MS Gundam SEED Battle Destiny Remastered** ✅ (UE 4.27, 57K objects
  → 72K mid-game, build 1016 user logs 2026-06-11, Steam): full scan OK —
  GObjects=0x7FF758C32550 (GOBJ_ES53_1, UE5-Extended layout strict, Max
  2.16M / 33 chunks), GNames=0x7FF758BF6200 (GNAM_V8, FNamePool hdrOff=0
  stride 2, `UE4Names=no` — a UE4.27 on the UE5-style FNamePool),
  **GWorld=0x7FF758D77040** (GWLD_GH_1). FProperty mode (CPN=no,
  TagFFV=no; `FField::Next=+0x20`, `Name=+0x28`; `FProp::Offset=+0x4C`,
  `StructProp=+0x78`). `version.dll` proxy loads (real version.dll).
  ProcessEvent on **vtable+0x220**, game-thread dispatch validated
  (15 646 hook fires / 1500 ms) and invokes succeed. Install
  `H:\SteamLibrary\steamapps\common\SEED BATTLE DESTINY REMASTERED`, exe
  `Game_SBDR\Binaries\Win64\SEED BATTLE DESTINY REMASTERED.exe`; internal
  UClasses use a `Life` prefix (`LifeGameInstance` 15 fields,
  `BP_LifeSaveData_C`). Bandai Namco (publisher=- — no thumbprint match).
- **Solarpunk** ✅ (stock UE 5.7, ~149K objects, rokaplay, `version.dll` proxy,
  PE hash `ED3D085C0811F000`, build 1259 user logs 2026-06-17, **re-verified
  build 2380 2026-07-25**): the real stock-UE5.7 game that exposed +
  live-confirmed the **`Object`@+0x08 within-item layout** (24-byte FUObjectItem:
  `FlagsAndRefCount@0, Object@8, Serial@0x10`). The classic `+0x00` pass is
  bad-dominated (`named=66 / bad=69` — a stride-16 mis-read hits Object only ~1/3
  of the time) so `Aura::DetectItemSize` now falls through to the `+0x08` pass →
  `size=24, offset=+0x08, 200 named / 0 bad` → name resolution ~100% (was 45.9% /
  sanity 4/10 before the **build-1257** fix → now 10/10). GObjects `GOBJ_V13`,
  GNames `GNAM_SAT425_3`, **GWorld ✅ via instance-scan recovery** (raw deref hits
  a decoy `0x1C2D5`). ProcessEvent vtable+0x260, dispatch validated. Closes the
  build-1064 "+0x08 needs a real stock-5.7 game" live-confirm. **Re-verified
  2026-07-25 on an UPDATED binary (build 2380; PE hash changed to
  `ED3D085C0811F000`):** 149,782 objects, all still ✅ (item +0x08/24, sanity
  10/10, no walk WARN/ERROR); GWorld again via instance-scan recovery with
  `gworld_aob` nulled → the Live Walker AOB export toggle stays correctly
  disabled (the fallback-GWorld case behind the AOB-toggle persistence
  exception, dev `2a497c4`).
- **Pionero Capital Demo** ✅ (stock UE 5.7, 117,663 objects, Pionero Games,
  `dxgi.dll` proxy, build 1.0.0.2202 user logs 2026-07-15): the **second**
  stock-UE5.7 `Object`@+0x08 title, re-confirming the +0x08 fall-through on a
  different game (classic +0x00 `named=66 / bad=69` → +0x08 pass `size=24,
  offset=+0x08, 200 named / 0 bad` → sanity 10/10). GObjects `GOBJ_ES53_1`
  (`0x7FF6F65689D0`), GNames `GNAM_V5` (`0x7FF6F649A940`), **GWorld ✅ directly**
  (`GWLD_TQ_1` unique, `0x7FF6F66F3780` — no decoy / instance-scan recovery, unlike
  Solarpunk), SparseDelegates `SPARSE_ES2_1`. FProperty + TaggedFFieldVariant
  (UE5.3+), CPN=no; enum names ✅ (`ENetRole`, UEnum::Names @ +0x40). GWorld walk
  ✅ — live world `NewPioneroIsland` (World 42 fields), `JoebillGameInstance`
  (6 fields). **First stock-UE5.7 title verified through the `dxgi.dll` proxy**
  (D3D12 EXE, like Elliot / Echoes of Aincrad / Avowed). Install
  `H:\SteamLibrary\steamapps\common\Pionero Capital Demo`, exe
  `PioneroCapital\Binaries\Win64\PioneroCapital-Win64-Shipping.exe`; internal
  UClasses use a `Joebill` prefix (`JoebillGameInstance`, `BP_JoebillGameMode_C`,
  `BP_JoebillPlayer_C`) — a city-builder / tycoon sim. PE hash `C200F9770A5F1000`.
  Pionero Games (publisher=- — Epic default copyright placeholder, no thumbprint).
- **MindsEye** ✅ (Build A Rocket Boy, **UE 5.4.4 licensee fork**, 530,638 objects,
  Steam, `version.dll` proxy, builds 2220 + 2238, 2026-07-19) — **⚠ verified on
  game version 7.3.1 ONLY** (PE hash `0863E3B90C993000`; the exe carries no
  game-version resource, so pin the build by that hash). The first title where the
  tool reported **`GObjects=OK` on garbage** (`Count=509`, `named=0`) and the first
  with **obfuscated FName payloads**. Only three things differ from stock UE 5.4:
  reordered `FChunkedFixedUObjectArray` (preset `MindsEye-Extended`
  `{0x28,0x10,0x24,0x20,0x14}`), a **32-byte `FUObjectItem` with `UObject*`@`+0x10`**
  (preset-bound `itemHint` — it aliases stride 16 perfectly, so it must never enter
  the shared sweep), and `FNameEntry` gaining a `u16` tag at `+0x02` with chars moved
  to `+0x04` and single-byte XOR-obfuscated **per tag** (key read straight out of the
  fork's own open-hash table; we never call its de-obfuscator). GWorld
  `GWLD_ES2_6` ✅ and SparseDelegates `SPARSE_ES2_1` ✅ needed **no change** — both
  still match uniquely. Result: `Count=530638, ItemSize=32`, name sanity **10/10**,
  Live Walker descends `GWorld → PersistentLevel → StormWP →
  EVMindsEyeGameInstance → LocalPlayers → LocalPlayer → BP_PlayerController_C`.
  Experimental-gated (`Flamme::IsExperimentalEnabled`, same `experimental.json` the
  UI writes) — a title without the fingerprint runs byte-identical code. **Not
  recoverable, by design:** the game renamed its own non-engine symbols at build
  time (21,635 generated 16-char lowercase identifiers sit verbatim in `.rdata`), so
  game-specific class/property *names* are gone for every tool; engine symbols read
  normally and value-based search is unaffected. Wide FName entries are not yet
  de-obfuscated (known, low impact). Re-derivation playbook:
  [mindseye-fork-notes.md](mindseye-fork-notes.md).

GWorld success ratio: **100% of all tested games** — see the
[test-games.md](test-games.md) GWorld Status Summary for the authoritative tally
— **the count is deliberately not restated here**, because two copies of a
tally drift and this one was 2 behind by 2026-08-06);
the list above itemises only a subset and is otherwise last-verified 2026-06-11.
Satisfactory (modular DLL build): scan side OK — `Macht::AOBScanAllModules`
falls through to `FactoryGameSteam-CoreUObject-Win64-Shipping.dll`
under `Engine\Binaries\Win64\` and the 15-game dump corpus includes
its 4,868 BPGCs cleanly. Proxy deploy was previously broken because
the UI skipped the `Engine` subfolder; fixed build 691 (the real
game .exe lives in `Engine\Binaries\Win64\` for this title, not
under `FactoryGame\`).
Star Wars Jedi: Fallen Order: scan side OK as above; proxy deploy
inherently broken because of EA launcher (see lesson in
[lessons-learned.md → Proxy DLL Deploy](lessons-learned.md#proxy-dll-deploy)).
For both EA-launcher and other launcher-wrapped titles, recommend CE
manual injection as the documented workaround.

## Long-running concerns

These are not actionable next-session work — see
[todo.md](todo.md) for that — but they are worth re-checking before
shipping any major Walker / Detection change:

- **`kPublishers[]` table review** — every new publisher we add changes
  detection behaviour for all that publisher's titles. Touch with care;
  prefer per-game user override over a publisher-wide bias unless we
  have ≥3 misdetected titles from the same publisher.
- **AOB pattern decay** — UE engine source rotates roughly every minor
  version. The 158 signature entries in `Himmel.h` are time-stamped per
  introducing build; any pattern that hasn't matched in ≥4 minor
  versions is a candidate for removal at the next clean-up.
- **HintCache schema additions** — the `FillPointerSnapshot`
  refactor (build 588) closed *one* instance of a recurring trap. New
  scan-time fields must land in BOTH `CMD_GET_POINTERS` *and*
  `CMD_SCAN_STATUS` payloads. The shared helper enforces this for
  pointer fields; the equivalent guarantee for object-list / walker
  payloads does not yet exist.
- **UE 6.0 layout parity (core path already UE6-ready)** — diffed every
  dumper-critical header `origin/5.8..origin/ue6-main` (2026-06-30). For
  **normal shipping** UE6 builds, **6.0 is layout-identical to 5.8** across
  every structure the dumper reads (FUObjectArray/chunked array, FUObjectItem
  `Object@+0x08`, UObjectBase Class/Name/Outer/Index/Flags, UStruct/UClass,
  FProperty/FField, FName/FNamePool) — the 5.8 cache-locality field reorder
  already ships in our `{0x00,0x0C,0x08,0x14,0x10}` "UE5.8" preset
  (`Aura.cpp`/`Genau.cpp`); 6.0 adds only AutoRTFM annotations + virtual-sig
  changes (no data members). **Nothing to implement now.** Two deferred
  watch-items (effort/risk in [todo.md](todo.md), per-structure layout in
  [technical-notes.md](technical-notes.md)): (1) if a UE6 game ships the
  experimental `UE_WITH_REMOTE_OBJECT_HANDLE` ON (off in normal shipping),
  hardcoded `OFF_UOBJECT_*` offsets shift by `sizeof(FRemoteObjectId)` and
  FUObjectItem packing is forced off; (2) the version-needle table
  (`kVersionNeedles`, `VersionNeedleScan.h`) tops out at `{"5.8.",508}`, so a
  UE6 binary matches no needle and, when nothing else answers, lands on the
  **flat `504` default** in `Genau.cpp` — not on "a bias fallback": the bias
  branch just above it is reachable only for a thumbprinted shipper, and
  `kPublishers[]` holds exactly one (`SQUARE_ENIX` → 427). Dynamic detection is
  unaffected (the AOB scan is version-agnostic) and the runtime marker ladder
  still raises a string-stripped UE5 title as far as 508 (`Frieren.cpp`), so the
  residue is a cosmetic badge. **UE 5.9 needs nothing added today** — the
  PE-VERSIONINFO path already answers `5.9 → 509` and the pipe accepts
  `418..509`; only a 5.9 title that is *also* string-stripped badges one minor
  low, and nothing in the 505–509 band is version-gated. If `{"5.9.",509}` and
  `{"6.0.",600}` are ever added, add them together so the
  `kVersionDetectLogicRev` bump is paid once; 600 additionally needs the pipe's
  upper bound and `PointerPanelViewModel`'s override list widened. No
  pre-emptive UE6 AOBs needed.
