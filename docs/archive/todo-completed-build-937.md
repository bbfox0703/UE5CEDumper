# Historical: full todo.md snapshot at build 937 (archived 2026-06-06)

Frozen snapshot of [docs/todo.md](../todo.md) taken at build 937, just
before the open-only cleanup. It retains the **full completed/closed-item
detail** (effort/risk retrospectives, files touched, test counts, decision
rationale) that was inlined into todo.md over builds 590–937.

This file is **history**, not a live action list:

- For the running milestone log of what shipped, see
  [../dev-log.md](../dev-log.md) — it already mirrors every shipped build
  referenced below (757 / 760 / 775 / 824 / 830 / 877 / 908 / 926 / 927 …).
- For the **current open work**, see the slimmed [../todo.md](../todo.md).
  The still-open items below (e.g. V3-C, Path 1 v2b, #5 v2, Class Family
  Browser) were extracted into that file as flat, self-describing entries;
  their full original context lives here.

Everything from this point down is the verbatim pre-cleanup todo.md.

-----

# Todo

Prioritized open work. **Read this when deciding what to do next.**
Move items to [dev-log.md](dev-log.md) once they ship; update
[roadmap.md](roadmap.md) when capability state changes.

> Format: each item has **Effort** (S/M/L/XL — rough; S=hours, M=1
> session, L=multi-session, XL=spans weeks), **Risk** (low/med/high —
> likelihood of breaking existing behaviour or introducing perf
> regression), and **Why** (the reason it's on the list). Strike-through
> when shipped.

-----

## Bytecode cross-reference: property ↔ function (Path 1) — core SHIPPED 2026-06-03

Static Kismet-bytecode xref answering "which methods use this field (read/write)?"
and the inverse "which fields does this function touch?". Shipped on `dev`
(builds 838-861, commits `574031e`→`248f631`); see dev-log 2026-06-03 for the full
breakdown. BP/script only — native functions have empty bytecode (that's Path 2).

- ~~Path 1 `find_property_xrefs` (property → functions); `USTRUCT_SCRIPT = PROPSSIZE+8`.~~ ✅
- ~~UI entry from Class Struct / Property Search / Interesting Properties (Find Funcs button + context menu).~~ ✅
- ~~v2a: ubergraph hits attributed to the BP event (nearest entry offset).~~ ✅
- ~~read/write distinction (`EX_Let*` LHS detection).~~ ✅
- ~~Reverse edge `walk_function_props` (function → properties + scope) on Interesting Functions.~~ ✅

Deferred follow-ups:

- **v2b — CFG-precise attribution.** Effort: L. Risk: med-high. Why: v2a's
  "nearest entry offset" mis-attributes a sub-graph reached from multiple events
  via jumps; the `EX_Let*` write detector misses wrapped LHS (`Other.Field` /
  `Struct.Member` / `Arr[i] = x`). A real variable-length decoder (follow
  `EX_Jump 0x06` / `EX_JumpIfNot 0x07` / `EX_ComputedJump 0x4E`, parse the LHS
  expression tree) fixes both. Reference: `vendor/RE-UE4SS/cppmods/
  KismetDebuggerMod/src/KismetDebugger.cpp` (`render_expr`) + `EExprToken` in
  `vendor/UnrealEngine/.../UObject/Script.h`. Only do it when a real
  mis-attribution motivates the cost.
- ~~**Path 2 — native UFunction analysis.**~~ ✅ **SHIPPED 2026-06-03 (builds
  862-872, dev).** Forward direction (native UFunction → properties): Zydis
  decoder (`Denken` module, `vendor/zydis` submodule) disassembles the exec thunk,
  tracks the `this` register (RCX), records `[this+off]` accesses, follows the
  thunk→impl call, and maps offsets to UPROPERTYs via `Ubel::WalkClass`. Plugs into
  the existing `walk_function_props` / `FunctionPropsDialog` seam (tagged
  `method="disasm"` + per-row confidence). See dev-log 2026-06-03 (Path 2).
  **Deferred follow-ups:** (a) **reverse direction** (property → native funcs —
  needs disassembling every native function per query; expensive); (b) **SIB-indexed
  / `[reg+idx*scale+disp]` accesses** (currently skipped); (c) **CFG-aware branch
  following** (only fall-through + direct call/tail-jmp are followed today);
  (d) live tuning of the `this`-tracking + Func-offset detector across more games.

-----

## 🧪 EXPERIMENTAL: Snapshot / SPC Query / Class Pivot (Phase A done 2026-06-02)

Port of three analysis features from the Unity sister project `discrete`, gated
behind an opt-in experimental flag. **Design of record:
[experimental-snapshot-spc-pivot.md](experimental-snapshot-spc-pivot.md)** — read
it first (concept mapping, UE-vs-Unity advantages, SQLite schema, identity-key
join, array inner-key handling, the Q#4 key-field improvement).

> **STATUS (builds 805-884).** Phase 0 + A + B (SPC) + **Phase C C1+C3-lite+C4+C5+C6**
> all shipped. Pivot groups a class by identity/key field with three `Source` modes:
> Snapshot (scalar), Snapshot Array (C6 inner-key), DataTable (C4 zero-config). C5 =
> right-click "Pivot this property…" handoff. Diff + SPC have AutoCompleteBox
> pickers + global filter + value range.
>
> **ENGINE REWORK (builds 879-884, live-test driven):** the gated tabs are now
> hardened + fast. Crash (selection-model on `Clear()`) and hang (sync SQLite on the
> UI thread) fixed; **Diff and SPC now run as in-memory hash-joins** (ported from
> `discrete`) instead of SQL self-joins — O(n), index-independent. With Pivot already
> in-memory, the three heavy composite indexes were dropped for a single lean
> `ix_fields(snapshot_id, class_fqn)` (**~½ DB, schema v4**). SPC gained per-snapshot
> **absolute value predicates** (Exact/Between/≥/≤) before the cap, to cut
> directional-but-irrelevant UI noise. (A v2 normalise attempt was reverted — it broke
> diff perf; see dev-log.)
>
> **LIVE-TEST HARDENING (builds 908-923, three rounds).** ~~**N1 per-tab class
> denylist**~~ ✅ (Top-N picker on SPC+Diff, right-click hide on Pivot, 3 independent
> per-game lists). Plus: heavy-query **cancellation** (cancel-on-tab-switch/close +
> explicit ct checks in the SQLite loops — fixed the SPC tab-switch hang); **persisted
> pivot class-index** (`class_counts` table, precomputed at finalize — killed the ~10s+
> Class Pivot first-open scan); **SPC same-session auto In-session join** (fixed
> transient inventory "materials don't show up"); capture ETA; Delete All; single-click
> checkboxes; combo timestamps; Old/New auto-swap; op-time grayout; collapsible layout.
> See dev-log builds 908/910/912/916/923.
>
> **AOT: Windows-only Avalonia backend** ✅ (builds 918-919): `Avalonia.Desktop` →
> `Win32`+`Skia`+`HarfBuzz`; dropped X11/macOS/FreeDesktop → AOT publish warning-free.
> Window icon → PNG (.ico flaky under AOT). **Discipline:** launch-test `dist\*.exe`
> after any backend change (build success ≠ launch success).
>
> **Remaining experimental:** C2 (find-by-value locator → pivot handoff); A3c (CE .CT
> freeze-export from a diff/SPC/pivot hit — Copy Address covers the manual path);
> heavier C3 scorer; *optional* `discrete`-style gzip blob storage; **N1 v2** —
> per-`(class,prop)` deny granularity (v1 is by-class). See the Phase C block + design
> §"Phase C"/§6.

Locked decisions: SQLite (raw ADO.NET, no EF Core) · all three features ·
persisted gating · **multi-session first-class** for SPC/Pivot · type-agnostic
directional SPC (energy-bar / CE Unknown-Initial-Value generalization) ·
Strict+Loose cross-session join · all-numeric capture scope · **v1 array
inner-key join** (struct/object/primitive — the cargo-hold case `discrete`
deferred).

Build in order; each phase gates the next:

- ~~**Phase 0 — Gating checkbox** (persisted).~~ ✅ **SHIPPED 2026-06-02
  (`5b8a47d`, build 805).** `ExperimentalGate` service +
  `IExperimentalGate` (persists to `%LOCALAPPDATA%\UE5CEDumper\experimental.json`,
  source-gen JSON, shared between PointerPanelVM checkbox + MainWindowVM tab
  gating via `Changed`). System-tab bbfox credit → checkbox (tooltip "Enable
  advanced experimental features"); 3 placeholder tabs appended last,
  `IsVisible` bound to `ExperimentalEnabled`. +4 gate tests → 1489 green; AOT
  publish clean. (Also fixed an unrelated `build.ps1 -Target Test` crash from a
  stray empty `--no-restore` arg, `4292004`.)
- **Phase A — Snapshot** (multi-session persistent foundation).
  - ~~A1a DLL scalar numeric capture + `begin_snapshot`/`snapshot_chunk`.~~ ✅
    **SHIPPED `fe8b5c2` (build 808)** — `Aura::CaptureSnapshotChunk` +
    `ValueScan::SelectSnapshotNumericFields` (dll_helpers 349→365).
  - ~~A1b DLL array element capture (inner-key).~~ ✅ **SHIPPED `ba7c370`
    (build 823).** Struct-array inner numeric fields keyed by a reorder-immune
    inner key (`ValueScan::SelectArrayInnerKey` + `Aura` container reuse);
    `arrays` wire field; C# parse + array-element rows; scalar diff excludes
    them. +6 tests → 1569 green. **Follow-ups:** object/primitive arrays + the
    Pivot inner-key join (Phase C). Live struct-array verify = user.
  - ~~A2 C# SQLite store + models + capture UI.~~ ✅ **SHIPPED `0747065` (A2a
    data layer) + `e832b4c` (A2b capture UI), builds 809-811.** Native AOT
    publish verified clean + bundles `e_sqlite3.dll`. +50 tests → 1535 green.
  - ~~A3 Diff engine (in-session index join) + grid + CE export handoff.~~ ✅
    **SHIPPED `aeba44d` (build 817).** `DiffSnapshotsAsync` (single indexed SQL
    join, changed rows + churn counts + filters) + diff grid + Copy Address.
    +per-game quota/usage (`ab874a4`). Full CE .CT freeze-export deferred to A3c
    (Copy Address covers the manual path). **Phase A capture→compare loop works
    end-to-end.** Remaining: A1b (array capture).
- ~~**Phase B — SPC Query** (multi-session, type-agnostic directional). Pure C#.~~
  ✅ **SHIPPED 2026-06-02 (build 824).**
  - ~~B1 engine: Strict/Loose join + relative predicate chain.~~ `SpcQueryBuilder`
    (pure N-way self-join SQL compiler) + `SnapshotStore.SpcQueryAsync`;
    directional predicates (Any/Unchanged/Changed/Increased/Decreased) pushed
    into indexed SQL; Strict/Loose/In-session join modes. +19 engine/builder tests.
  - ~~B2 UI: cross-session picker + direction predicates + CE export.~~
    `SpcQueryViewModel` + `SpcPanel` (snapshot picker with session-tail column +
    per-row predicate combo, join-mode toggle, filters, value-sequence results;
    Copy Address + Open in Live Walker). +6 VM tests. **Follow-ups (v2):** absolute
    Exact/Range/Delta predicates; per-snapshot value columns (deferred to keep the
    grid AOT-safe). Live multi-session verify = user.
- **Phase C — Class Pivot** (value-keyed, cross-session safe). Pure C# (+opt DLL).
  - ~~C1 PivotEngine (identity mode + collision render).~~ ✅ **SHIPPED 2026-06-02
    (build 830).** `PivotEngine` (pure: identity/field grouping, per-group value
    projection, `⟨N: …⟩` collision) + `SnapshotStore.PivotAsync` /
    `ListPivotClasses` / `ListPivotFields`; `ClassPivotViewModel` + `ClassPivotPanel`
    replace the placeholder. +13 engine/store tests + 4 VM tests.
  - ~~C3 Key discovery — reuse `PropertyScoringTable` + UE type/name priors.~~ ✅
    **SHIPPED (build 830, C3-lite).** `PivotKeyScorer` (type + name + cardinality
    key prior; `SuggestKey`; value interest via `PropertyScoringTable`). +5 tests.
    **v2 follow-ups:** Jaccard stability + greedy compound key + class shortlist /
    volatility ranking (the heavier 29i-3 scorer) still open.
  - C2 Find-by-value locator + handoff (closes the loop). *Effort M · med.*
  - ~~C4 DataTable-native pivot (RowName is the key, zero-config).~~ ✅ **SHIPPED
    (build 873).** `DataTablePivotEngine` (pure: one group per row keyed by
    RowName, struct fields projected, no discovery) + a `Source` toggle
    (Snapshot/DataTable) on `ClassPivotViewModel`/`ClassPivotPanel` driving the
    live `walk_datatable_rows` path. Reuses the existing results grid + CE handoff
    (Copy Address / Open in Live Walker). +8 tests (6 engine + 2 VM). Lives inside
    the already-gated experimental tab, so it's invisible when the flag is off.
  - ~~C5 Right-click handoff from LiveWalker / PropertySearch / InterestingProps.~~
    ✅ **SHIPPED (build 877).** "Pivot this property…" context-menu item on all
    three panels → `NavigateToPivot(class, prop)` → MainWindowVM switches to the
    Class Pivot tab + `ClassPivotViewModel.PivotForAsync` selects the class in the
    newest snapshot and ticks the property. Menu item `IsVisible` binds a
    per-VM `PivotEnabled` flag (set = ExperimentalEnabled && Pivot exists), so it's
    hidden when experimental is off. +4 command tests + 2 PivotForAsync tests.
  - ~~C6 Array-element pivot (inner-key join on captured struct arrays — cargo
    case).~~ ✅ **SHIPPED (build 877).** "Snapshot Array" source mode: pick a
    snapshot + array-class + struct-array field; groups elements by inner-key value
    (e.g. Cargo by ItemID — reorder-/owner-immune), projecting inner numeric props.
    Store: `ListPivotArrayClasses/Fields/Props` + `PivotArrayAsync` (maps each
    (owner, element) to a synthetic instance keyed by inner_key_value → reuses
    `PivotEngine.Build` in Identity mode, zero new engine). +6 store + 1 VM test.
- ~~**N1 — Per-game class denylist (Top-N noise picker).** *Effort M · low risk.*~~ ✅
  **SHIPPED 2026-06-05 (build 908).** Per-game denylist persisted next to the DB
  (`snapshots.<pe_hash>.denylist.json`), applied at the anchor-load step of SPC +
  Diff (so denied classes never enter the candidate dict — saves memory AND
  result-cap budget), and removes denied classes from the Class Pivot picker.
  Top-N (max 50) contributor picker on SPC + Diff result tabs; each row =
  checkbox + class FQN + hit count + 3 sample prop names. "Apply &amp; re-run"
  command pushes ticks into the denylist and re-runs the query; chips below
  show active denylist with one-click remove + "Clear all". +6 tests
  (Diff/SPC filter, Top-N ranking, per-game persistence round-trip, no-active-
  game safety, pe_hash filename sanitisation). All 1247 C# + 393 dll + 31 utf8
  tests green; AOT publish clean.
  - **Decision deviations from the original spec**: persistence landed
    *next to the per-game DB* (`snapshots.<pe_hash>.denylist.json`) instead of
    inside `experimental.json` — auto-follows the game, survives FIFO eviction,
    no key-by-pe_hash dict needed. Pivot got NO Top-N picker (it's per-class —
    no "Top contributor" makes sense from a single class).
  - **Per-tab isolation (build 910, supersedes the original "shared denylist").**
    Live-test feedback: each tab keeps its OWN list (`DenylistScope` Diff/Spc/Pivot
    in one JSON file). SPC + Diff populate theirs via the Top-N picker; Pivot via a
    right-click "Hide this class" on the class picker (+ hidden-class chips). Same
    build also added: op-time input grayout (all 3 tabs), heavy-query **cancellation**
    (cancel-on-tab-switch + on-close + explicit ct checks inside the SQLite read
    loops, since `ReadAsync(ct)` ignores the token) fixing the tab-switch UI hang,
    "Reset ticks" on the pickers, and a collapsible (Expander) capture+compare layout
    with a GridSplitter so the diff grid can grow.
  - **v2 nuance to leave room for**: per-`(class, prop)` granularity. Some
    classes (`ACharacter`, `APawn`) carry both gameplay-relevant fields
    (`Health`) and noise (`Velocity`, `LastRenderTime`). v1 by-class catches
    the widget/anim/component bulk; v2 would chevron-expand each Top-N row to
    its Top-K noisiest props for finer deny. Defer until v1 proves the bulk
    case is solved.

New work concentrates in A1 (DLL capture, mostly reuse) + C3 (UE-ify scorer);
B/C are largely portable `discrete` C# + indexed SQL.

-----

## 🎯 NEXT SESSION STARTING POINT (2026-05-29 latest, build 797-798, dev = main)

Shipped this session (merged dev→main): the two **multi-numeric Value Search
meta types** — find a value across many numeric widths in one pass, each field
compared by its OWN declared type (a *structured property walk*, so unlike CE's
raw "All" there are **no byte-reinterpret false hits**).

| Build | Feature | Status |
|---|---|---|
| 794-795 | **NumericNoByte** — word/dword/qword/float/double in one pass; excludes 1-byte + bool | ✅ PR #220 |
| 796-797 | **NumericAll** — NumericNoByte + Int8/UInt8, plus a result-volume **warning** (small values flood 1-byte fields) | ✅ PR #221 |

Both in-game verified OK. Tests now **349 DLL helpers + 31 utf8 + 1105 C# = 1485**.
Implementation notes in [dev-log.md](dev-log.md) build 794-795 / 796-797; capability
matrix in [roadmap.md](roadmap.md) Value Search section. The meta machinery is keyed
on `ValueScan::IsMultiNumericDataType`, so a future third variant (e.g. include Bool,
or a vector-family meta) is a small add.

**Remaining picks** unchanged — the 2026-05-27 block below is still the source of
truth (#7 View Snap Hotkey, #2 Live PE Call Profiler, #0c FTransform Translation
offset, #5-v2 ObjectProperty return resolution, LiveWalker batch generator).

-----

## 🎯 NEXT SESSION STARTING POINT (2026-05-29 update, build 792)

Shipped this session (committed to dev): **P1b parallelization** of the three
GObjects-walk scans — `ScanForValue` / `FindInContainers` /
`FindReferencesToUObject` — plus thread-safe `Ubel` caches that the parallel
walk now requires. Source: `<private-ce-repo>/docs/Memory-Scanning-Internals.md`
§16. Full write-up in [dev-log.md](dev-log.md) 2026-05-29 entry. Build + all
1358 tests green; **in-game verified OK 2026-05-29** (user-confirmed: correct
results, no hang/crash, multi-core First-Scan speedup).

**New follow-ups from this work:**

- ~~**Extract `ParallelGObjectsScan<ResultT>` template helper**~~ — ✅ **done
  2026-05-29 (build 793)**. Added `ParallelGObjectsScan<PerThreadT>(count, body)`
  (owns nthreads / perThread vector / atomic deadline / `ParallelIndexRanges`
  plumbing) + `ConcatTruncate(perThread, &T::member, maxResults)` (the
  ascending-tid merge + truncate ordering invariant, now one source of truth).
  All three scans call them identically; per-thread stat folds stay in each
  caller (counter types differ). Behavior byte-identical (build + 1358 tests
  green).
- ~~**In-game parallel-scan verification**~~ — ✅ **done 2026-05-29**.
  User-verified: parallel result set matches the serial walk, no hang / crash,
  and the multi-core First-Scan speedup held on a live target.

The 2026-05-27 block below remains the source of truth for the remaining
picks (#7 / #2 / 0c / #5-v2 / LiveWalker-batch).

-----

## 🎯 NEXT SESSION STARTING POINT (2026-05-27 refresh, build 780, dev = main)

Last session (2026-05-27) merged **PR #208, #209, #210, #211** to main —
seven shipments across the picks list:

| Build | Pick | Status |
|---|---|---|
| 757 | Value Search Phase 2 (FString/FName/FText + FVector/FRotator + TArray) | ✅ PR #208 |
| 760 | #3 Multi-row → One .CT batch generator (Interesting Funcs + Properties tabs) | ✅ PR #210 |
| — | #4 `scripts/analysis/diff_dumps.py` (same-game patch diff) | ✅ PR #209 |
| 771 | NuGet bump + dotnet test → MTP mode (`global.json` + `--project` flag) | ✅ PR #211 |
| 775 | #5 Structured-return DataGrid (FVector/FRotator/FHitResult/user USTRUCT rendering) | ✅ PR #211 |
| 778 | #6 UCheatManager stripped-body Console-panel hint + memory + lessons-learned bullet | ✅ PR #211 |
| 780 | AOT-warning fix on #5 DataGrid (`FuncDataTemplate<T>` instead of string-path Binding) | ✅ PR #211 |

Test count this session: 1052 → **1080** C# (+28 from #3/#5/#6) on top
of the Phase 2 work (+71 C#, +123 DLL helpers from earlier in the
day). Total now **247 DLL helpers + 31 utf8 + 1080 C# = 1358 tests**.

### Active candidates (pick one to start the next session)

Picks #2 / #7 / 0c / #5-v2 / LiveWalker-batch all live as detailed
sections further down this file. Sorted here by recommended start order:

1. **#7 View Snap Hotkey (Property → Snap Hotkey)** — S-M (~2-3 days), low risk. New PropertySearch / LiveWalker row action + `SnapHotkeyDialog` + `scripts/ue5_snap_helper.lua` + `SnapHotkeyScriptGenerator.cs`. 95% mirrors build-719 freeze Route B. Generalises beyond rotation (MoveSpeed multipliers, zoom cycling, time-dilation).
2. **Bonus add-on: AA(Baked) Auto-tick every N ms checkbox** — S, low. 1-day item; both #7 and #3 already touched the script generator + dialog wiring.
3. **#5 v2 — ObjectProperty return resolution + recursive struct expansion** — S+S follow-ups to PR #211. Needs a new DLL pipe call (`resolve_object_name(addr)` or extend invoke_function response) for UObject* → "Name (Class)" rendering.
4. **0c FTransform Translation offset** — S, low risk. Phase 2 footnote. Needs per-version Translation offset detection (UE4 / UE5-non-LWC at +16, UE5 LWC at +32). Currently `VectorStructNames(FTransform)` returns empty → zero hits.
5. **#2 Live ProcessEvent Call Profiler** — M-L (~1 week), med risk. Hot-path DLL change. The big-impact one but biggest blast radius. Stark already has `s_hookFireCount` — extend with per-UFunction atomic counter + Recording toggle + 3 pipe cmds + new "Live Funcs" tab.
6. **LiveWalker batch generator (v2 of #3)** — S-M. Heterogeneous rows (functions + fields + struct sub-fields + array elements). Needs UX pass on drilldown state.
7. **Dual-connection pipe (eliminate head-of-line blocking)** — M, **high risk** (in-process DLL concurrency). **POSTPONED 2026-06-01** — full design written up in [multi-connection-pipe-proposal.md](multi-connection-pipe-proposal.md). Mirror discrete's interactive/bulk split (one pipe, two UI connections, `nMaxInstances=4`, per-client server thread) so value-scan / find-refs / list-all don't freeze browsing. Engine-side concurrency is already safe (build 792/793 + SessionManager); residual risk is Fern's accept/shutdown rewrite. Phase the DLL change behind the unchanged single-conn UI first. Benefit is moderate (parallelised scans already shrank the blocking window) — revisit only if "UI freezes during a big scan" becomes a real pain.

### Live-game verification waiting on user (no code needed)

- **Phase 2 paths on Geri (UE 4.27)** — ES2 (UE 5.5) already verified across all 5 new DataTypes + Contains/StartsWith/EndsWith.
- **TArray stress test on Satisfactory inventory arrays** — confirms the 10M soft circuit-breaker doesn't false-positive on legit deep arrays.
- **#5 grid on Geri's `PlayerCameraManager::GetCameraLocation`** — expect 3 rows (X / Y / Z floats) at offsets 0x4 / 0x8 / 0xC.
- **#6 diagnostic on `UCheatManager::Fly`** — confirm orange footer hint appears + Result=0 + no effect + GetHookFireCount > 0 + game-specific BC function on same session works.
- **NumericNoByte (build 794-795) on a 1M-object game** — scan a known HP value (e.g. `100`) with DataType=NumericNoByte; confirm candidates appear across mixed property types (the Type column should show IntProperty / FloatProperty / etc.), Next Scan (Decreased after taking damage) prunes correctly, and the result count + scan time stay sane (no runaway explosion now that 1-byte/bool are excluded). Then sanity-check a float-only value (e.g. `337.5` with tolerance 0.5) only hits float/double fields.

-----

## Value Search coverage + memory — container scan, cap, lean Candidate (planned 2026-06-05, build 923)

Three related Value Search items surfaced from a "what can't it scan?" review.
They have a **dependency order**: the lean-Candidate refactor (V3) is the enabler
for both wider coverage (V1) and any cap increase (V2), because every candidate
lives in the **injected DLL inside the game process** — so per-candidate bytes are
the real ceiling. Recommended order: **V3-A → V3-B → V1 (Map/Set) → V2/V3-C**.

The底層 sparse-container walk **already exists** (Address Finder uses it) — V1 is
mostly wiring ValueScan's `expandFields` + per-instance loop into it, not new
memory-reading code.

### V3. Lean Candidate record — string interning + deferred enrichment

- ~~**V3-A — shared FieldDescriptor.**~~ ✅ **SHIPPED on dev (build 926, commit
  `1161411`) — live First/Next scan verified OK by user 2026-06-05.** Effort: **M**.
  Risk: **low** (pure DLL-internal, no wire-schema change). Why: `ValueScan::Candidate`
  ([ValueScan.h:294](../dll/src/ValueScan.h#L294)) is **~240 B, 6 `std::string`s**,
  and `className` / `definingClassName` / `fieldName` / `fieldType` / `boolFieldMask`
  / `fieldOffset` are all functions of `(class, field)` — identical across thousands
  of candidates of the same class, yet copied by value into each. They're **already
  computed once** in `sci->fields` / `sci->className`
  ([Aura.cpp:4109](../dll/src/Aura.cpp#L4109)). Intern them into a session-level
  `vector<FieldDescriptor>` + a string pool; Candidate stores a `uint32 descriptorIdx`.
  Target: **~240 B → ~72 B**, and (the dominant win) 5 fewer heap-string
  allocations per candidate — numeric scans now allocate zero per candidate.
  **Live-verify**: on a 1M-object game run a known value First Scan across
  numeric / string / FVector / TArray-element / NumericNoByte, confirm the
  candidate rows (class / defining-class / field name incl. `Field[idx]` /
  value) render identically to pre-926, then a Next Scan prunes correctly.
  The interning + parallel-merge index remap is only exercised end-to-end
  in-process, so a clean unit run ≠ correct scan.
- ~~**V3-B — instance table dedupe.**~~ ✅ **SHIPPED as part of V3-A (build 926,
  commit `1161411`).** A lean Candidate can't keep raw `instanceAddr`/`instanceName`,
  so the `InstanceRecord` pool + `Candidate::instanceIdx` were necessarily built in
  the same change: `ScanForValue` resolves `Ubel::GetName(obj)` once per object
  (`curInstanceIdx`, [Aura.cpp:4246](../dll/src/Aura.cpp#L4246)) and every matching
  field of that object shares the index — one record per object, not per candidate.
  No separate work remains.
- **V3-C — deferred enrichment.** Effort: **M**. Risk: **med** (new pipe cmd +
  UI paging). Why: refine never needs the display strings (it only re-reads `c.addr`,
  [Aura.cpp:4507-4536](../dll/src/Aura.cpp#L4507)), and the UI only shows a window at
  a time. Resolve `className` / `instanceName` lazily at view-time via a new
  `resolve_candidate_window` pipe cmd. Unblocks the pipe/UI walls in V2.

### V1. TMap / TSet / TOptional value scan

- ~~**V1a — TMap / TSet (First-Scan).**~~ ✅ **SHIPPED on dev (build 927) — live-verified
  OK by user 2026-06-06** (Avowed/Star-game `TMap<NameProperty,IntProperty>`
  `PlayerData.AttributeAugmentLevels.Value[2]`=481 found via Int32 Exact scan).
  Shipped: `ScanField` gained a
  `ScanContainer { None, Array, Set, MapKey, MapValue }` enum + `valueOffset`;
  `expandFields` emits Set/Map(key|value) ScanFields (vector inner gated by
  `ContainerInnerAccepted`); a shared `scanElement` lambda (factored out of the
  TArray loop) drives Array + the new sparse branch (`ReadTSparseArray` +
  `IsSparseIndexAllocated`, value at `slot + valueOffset`). **Refine + Fern needed
  ZERO changes** — they already operate on `c.addr` + the descriptor pool, and the
  element addr (incl. valueOffset) is baked in at First-Scan. Rows render
  `Set[idx]` / `Map.Key[idx]` / `Map.Value[idx]`. Tests: dll_helpers 400 → 412
  (Map display names + sparse stride/offset geometry). **Live-verify**: scan a
  known value held in a `TMap`/`TSet` UPROPERTY (inventory/stat maps are common),
  confirm rows appear with `Map.Key/Value[idx]` names + a Next Scan prunes; watch
  for false hits from freed-slot reads. Effort: **M** (~1.3× the build-757 TArray
  path). Risk: **med**. Why: closes the "containers other than TArray are invisible" gap
  (intentional skip at [Aura.cpp:4079-4080](../dll/src/Aura.cpp#L4079)). Reuse
  `Macht::ReadTSparseArray` / `IsSparseIndexAllocated`
  ([Macht.h:165-200](../dll/src/Macht.h#L165)), `Ubel::GetMapPairLayout` /
  `GetSetElementStride` ([Ubel.cpp:1176-1238](../dll/src/Ubel.cpp#L1176)),
  `Macht::ComputeMapValueOffset` ([Macht.h:225](../dll/src/Macht.h#L225)), and the
  proven Map(key+value)/Set iteration template at
  [Aura.cpp:2194-2270](../dll/src/Aura.cpp#L2194). Changes: (1) widen `ScanField`'s
  `bool isArray` → `enum ContainerKind { None, Array, Set, MapKey, MapValue }` +
  `pairStride` / `valueOffset` ([Aura.cpp:3897](../dll/src/Aura.cpp#L3897)); (2) emit
  Set/Map ScanFields in `expandFields` next to the ArrayProperty branch
  ([Aura.cpp:4035](../dll/src/Aura.cpp#L4035)); (3) a sparse branch in the per-instance
  loop next to `if (sf.isArray)` ([Aura.cpp:4246](../dll/src/Aura.cpp#L4246)). Display
  names `Field[e]` / `Field[e].Key` / `Field[e].Value` (matches Address Finder).
  Sparse containers are usually small, so the marginal candidate count is low.
- **V1b — prev-value refine for containers (stable key).** Effort: **M**. Risk: **high**.
  Why: `Candidate.addr` stores a **raw element address**
  ([Aura.cpp:4278](../dll/src/Aura.cpp#L4278)); TArray realloc already makes it stale
  (handled by SEH + "First-Scan again", [Aura.cpp:4515-4520](../dll/src/Aura.cpp#L4515)),
  and **TSparseArray is worse — freed slots get reused**, so `c.addr` on refine may
  point at a different logical entry → Changed/Unchanged semantics silently lie.
  V1a ships **First-Scan-only for containers** (refine drops slots that no longer
  validate). V1b stores `container addr + slot index` as a stable key and re-walks
  the sparse array on refine — same idea as snapshot's `SelectArrayInnerKey`
  ([ValueScan.h:227](../dll/src/ValueScan.h#L227)). Do only if refine-on-container is
  actually requested.
- **V1c — TOptional.** Effort: **S-M**. Risk: **med**. Why: different shape from
  Map/Set — inline `[T value][bool bIsSet]`, not sparse. Needs `OptionalProperty`
  inner resolution in `WalkClassEx` first, then a flag-gated leaf read. **Do
  separately, after V1a** — don't bundle the three "deferred containers" together.

### V2. Raising the global `maxResults` cap

- **V2 — paged streaming, not a bigger flat cap.** Effort: **M-L**. Risk: **med**.
  Why: the 50k cap ([roadmap.md:140](roadmap.md#L140)) is bounded by four walls,
  nearest first: (1) **DLL memory in the target process** — the hard one, addressed
  by V3; (2) **pipe JSON serialization** of N candidates; (3) **Avalonia DataGrid**
  holding N rows under AOT; (4) the 15 s deadline
  ([Aura.cpp:4424](../dll/src/Aura.cpp#L4424)) + the fact that 500k results aren't
  user-actionable (refine is meant to converge). Correct shape: keep the **full
  lean-candidate set in the DLL session** (cheap after V3) but **stream only a
  window/summary to the UI** (depends on V3-C's paging cmd). Don't just bump the
  number.

-----

## UI/UX: DataGrid sorting + Value Search filter (build 934, 2026-06-06)

User-reported during V1a live test. Three items; two shipped, one deferred.

- ~~**DataGrid column sorting broken app-wide.**~~ ✅ **SHIPPED (build 933-934).**
  Root cause: the project uses **compiled bindings**
  (`AvaloniaUseCompiledBindingsByDefault=true`), and Avalonia DataGrid does **not**
  auto-derive a column's sort path from a compiled binding — so NO column sorted
  (text or template), in every grid. **Not** an AOT/Linux-removal regression (the
  user tested the non-AOT `-Target Test` build). Fix: added explicit
  `SortMemberPath="<Prop>"` to every sortable column across all panels (numeric
  backing for hex offset/size/address/score columns so order is numeric, not
  lexical); `CanUserSort="False"` on action/button/checkbox columns. **Exception
  kept:** SpcPanel `SnapshotPicks` stays `CanUserSortColumns="False"` (chronological
  old→new). Panels swept: ValueSearch, ProxyDeploy, LiveWalker (Fields/Refs/Funcs),
  InstanceFinder (×3), PropertySearch, Interesting Funcs/Props, ClassStruct,
  GameClassFilter, Snapshot (×3), Spc (×2), ClassPivot (×2). **Lesson:** any new
  DataGrid in this project MUST set `SortMemberPath` per sortable column or it won't
  sort (compiled bindings).
- ~~**Value Search keyword filter.**~~ ✅ **SHIPPED (build 932).** Case-insensitive
  substring filter over all displayed columns, client-side over the cached candidate
  set (`FilterText` → `ApplyFilter()` rebuilds the bound `Candidates` from
  `_allCandidates`; reflection-free, AOT-safe). Kept the bound collection a **typed**
  `ObservableCollection` (not a `DataGridCollectionView`) — a non-generic view breaks
  compiled column-binding type inference (AVLN2000).
- **Live Walker focus-on-field when navigating from a result.** Effort: **S-M**.
  Risk: low. **DEFERRED — next.** "Open in Live Walker" from a Value Search row opens
  the owning instance but doesn't scroll to / select the target field, so the user
  can't see the correct position. Infra already exists: `LiveWalkerViewModel`'s
  `_pendingScrollFieldName` + `ScrollToFieldRequested` event + `LiveWalkerPanel`'s
  `ScrollIntoView`/`SelectedItem` handler (used by Find References). Need to thread
  the candidate's `FieldOffset` through `OpenInLiveWalker` → `NavigateToInstance` →
  `NavigateToAddressAsync`, then match by offset in `UpdateDisplay` (field names
  aren't unique). See the cross-nav investigation in the 2026-06-06 session.

-----

## DLL-side cooperative cancellation for long operations (build 936-937, 2026-06-06)

User-reported: long DLL ops (value scan, snapshot, etc.) must be cancellable +
clean up; and if the UI closes, the DLL must STOP — not spin idle and block the
game from closing. Root constraint: Fern's pipe is **single-connection,
synchronous** (`nMaxInstances=1`), so a long scan blocks the pipe thread and the
client can't be told to stop on the same pipe mid-scan.

- ~~**Global cooperative-cancel + shutdown-abort + disconnect monitor.**~~ ✅
  **SHIPPED.** New `Cancel.h` (`Cancel::Requested()` = relaxed atomic; per-command
  flag + sticky shutdown flag). (1) `Fern::Stop()`/`UE5_Shutdown` call
  `RequestShutdown()` **before** joining threads → an in-flight scan bails so the
  accept-thread join completes fast (**fixes "game won't close"**). (2) Fern
  **monitor thread** `PeekNamedPipe`s the in-flight pipe every 200ms (only while
  `m_commandInFlight`, when the handler is CPU-bound and not touching the handle);
  a broken pipe → `RequestPerCommand()` → orphaned scan bails → pipe frees for the
  reconnecting UI (**fixes the reconnect-within-window stall the user flagged**).
  (3) Per-command flag reset at each command start. **Coverage:** parallel scans via
  a watcher inside `ParallelGObjectsScan` (flips the existing `deadlineHit`, covers
  value scan / find-refs / containers / xrefs / find-by-path — zero per-body edits);
  serial loops via `Cancel::Requested()` every 4096 iters (`ListClasses`,
  `EnumerateAllFunctions`, `SearchByName`, `FindInstancesByClass`, `SearchProperties`,
  `SearchPropertiesBatch`, `WalkClassesBatch` (Full SDK dump), `CaptureSnapshotChunk`,
  `Aura::ForEach` → covers walk_world fallback, `list_enums`).
- ~~**Value Search cancel button.**~~ ✅ **SHIPPED.** Per-scan `CancellationTokenSource`;
  Cancel button visible while `IsScanning`. Cancel abandons the UI wait immediately;
  the DLL self-terminates at its deadline (≤15s, usually sub-second) and the orphaned
  response is discarded (single pipe can't interrupt mid-scan while connected).
- **Decision:** deliberately **no hard timeouts** on serial ops — Full SDK dump etc.
  are meant to run to completion; the cooperative cancel (disconnect/shutdown) already
  covers the user's two concerns. **Live-verify pending:** confirm in-game that (a)
  disabling the script / closing while a long scan runs no longer hangs, (b) closing
  the UI mid-scan stops the DLL and a reopened UI reconnects promptly.

-----

## Next-priority enhancements (decided 2026-05-26, post build 738)

### 0. ~~Value Search tab (by-value scan)~~ — ✅ shipped this session (2026-05-26, build 738)

**Effort actual**: ~half a day (Sonnet-driven port of discrete Phase 27b
shape, mostly mechanical) | **Risk**: low — purely additive (3 new DLL
files, 3 new pipe cmds, 1 new tab; no existing-behaviour regressions
in 957 C# + 124 DLL tests). **Why**: filled the search-by-value gap
(PropertySearch = by-name; InstanceFinder = by-address; this is the
third axis). Port motivated by cross-repo discussion at session start —
the Unity-side `whatIsAt` + `beginValueScan` workflow has direct
analog here, and UE's reflection metadata makes the candidate
enrichment cleaner than discrete's whatIsAt path.

**What shipped**:
- DLL: `ValueScan.h/.cpp` (DataType / ScanType enums, Candidate,
  SessionManager with 5-min idle expiry, ComparePredicate), Aura
  `ScanForValue` + `RefineCandidates`, 3 pipe cmds
  (`begin_value_scan` / `refine_value_scan` / `end_value_scan`).
- C#: `Models/ValueScanModels.cs`, `IDumpService` + `DumpService`
  begin/refine/end, `ValueSearchViewModel`, `ValueSearchPanel.axaml`
  with **hard-locked** "Native C++ fields not findable here" banner
  (literal text locked in by `Banner_LiteralText_IsPresentInEnAxaml`
  test — see memory `project_value_search_caveats`).
- Tests: +31 DLL helpers, +22 C# (incl. scan-type partition theory,
  prev-value scan type omits `value` field on the wire, First Scan
  auto-ends orphan session before new Begin).

**Live verification target (next session, user-side)**:
- Geri (UE 4.27) + ES2 (UE 5.5): scan for HP (Int32 or Float, depending
  on game), take damage, switch ScanType=Decreased, click Next Scan,
  confirm candidates prune correctly. Smoke-test the cross-tab
  "Open in Live Walker" navigation flow.

**v2 deferred** (revisit on user signal):
- ~~**FString / FName / FText support**~~ — ✅ shipped **build 757** (Phase 2A,
  2026-05-27, PR #208). Contains / StartsWith / EndsWith scan types with
  CE-style case-insensitive default; opt-in case sensitivity. FText is
  best-effort in cooked builds (ES2 resolved 1/1551 classes — expected).
- ~~**TArray\<T\> scan**~~ — ✅ shipped **build 757** (Phase 2C, 2026-05-27,
  PR #208). Walks reflected ArrayProperty buffers for primitive / string /
  vector inner types. Soft circuit-breaker on `Num > 10M` (skip with
  `LOG_WARN`), Num/Max/Data validation guards, SEH-safe stale-addr reads.
  No `LOG_WARN: skipping TArray` fired across ~1.15M scanned objects on ES2.
- ~~**FVector / FRotator / FTransform**~~ — ✅ shipped **build 757** (Phase 2B,
  2026-05-27, PR #208). Component-wise compare with shared per-axis
  tolerance; `"X,Y,Z"` CSV input. **FTransform footnote**: reserved on the
  wire but currently returns zero hits — pending per-version Translation
  offset detection (UE4 / UE5-non-LWC at +16, UE5 LWC at +32). Tracked as
  follow-up **#0c FTransform Translation offset** below.
- ~~**Multi-numeric "All" scan (no-byte variant)**~~ — ✅ shipped **build 794-795**
  (2026-05-29). `ValueScanDataType.NumericNoByte`: one pass over every
  word/dword/qword/float/double field, comparing the value against each field
  by its own declared width (no byte-reinterpret, unlike CE's "All"). Excludes
  Int8/UInt8/Bool to avoid small-value result explosion. See dev-log build
  794-795.
- ~~**#0d with-byte variant `NumericAll`**~~ — ✅ shipped **build 796-797**
  (2026-05-29). Adds Int8/UInt8 to the multi-numeric member set (still no
  Bool). Includes a result-volume warning (`DataTypeWarning` VM property →
  orange hint in the panel) since small values flood 1-byte fields. Rode on
  the NumericNoByte meta machinery. See dev-log build 796-797.

> _Ordering note (kept from earlier 2026-05-26 review)_: items below
> were ordered by value/effort ratio at the end of the build-719 freeze
> session. "Game Profile persistence" was dropped because [Flamme
> HintCache](../dll/src/Flamme.cpp) already persists per-PE-hash AOB
> winning pattern IDs + UE version + version-detected flag + user
> override + invoke timeout, shared with the C#
> [AobUsageService](../ui/UE5DumpUI/Services/AobUsageService.cs) via
> `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{COMPUTERNAME}.json`. The
> remaining gaps (DynOff cache, favorites, invoke param presets) are
> low-pain compared to the AOB scan which is already cached.

### 1. ~~UE Console / Exec Command Bridge~~ — ✅ shipped this session (2026-05-26)

**Effort actual**: ~3 hours (zero DLL change) | **Risk**: low (no
existing-behaviour regressions; ConsoleViewModelTests +15 / 920 → 935
C# tests). **Why**: skips the "find UFunction → build ParamBuffer →
invoke" workflow for games that ship with debug exec commands intact
(common even in cooked Shipping — Epic's `UCheatManager` subclasses
+ many game-specific exec functions survive cooking). Many cheat-
relevant capabilities (`fly`, `ghost`, `god`, `giveitem`, `teleport`,
`summon`) are *already implemented by the game developer* as
`UFUNCTION(exec)` — using them means we deliver effects the game has
literally pre-built for cheating.

**What shipped**:
- `AllFunctionEntry.IsExec` + `Exec` short-flag decoder (corrects
  the previously-undecoded `FUNC_Exec = 0x00000200` bit — note:
  earlier draft of this doc wrote 0x4, which is actually
  `FUNC_BlueprintAuthorityOnly`).
- `ConsoleViewModel` + `ConsoleHistoryEntry` model with Load (filters
  `IsExec` client-side from the existing `list_all_functions` payload
  — no new DLL/pipe surface), filter-text, RunSelected (direct invoke
  via existing `InvokeFunctionAsync` for no-arg commands; raises
  `RequestParameterInvoke` for commands with parameters), command-
  line-style `>` input with `/fly`-style typed dispatch, 20-entry
  history with one-click replay.
- `ConsolePanel.axaml` — new tab between Interesting Props and Game
  Classes. Top toolbar: Load / GameOnly / Filter / typed-command
  input. Centre: DataGrid of discovered exec commands. Bottom:
  history pane (160 px max-height, scrollable).
- MainWindow integration: new `Console` child VM property, wired
  three events (`NavigateToFunction`, `RequestCopyBakedScript`,
  `RequestParameterInvoke`) to the existing flows. ClassStruct tab
  index shifted 6 → 7 due to Console insertion (3 updated call
  sites).
- 15 new `ConsoleViewModelTests` covering Load filter, sort order,
  filter text, no-arg direct invoke, multi-arg dialog route,
  history cap + replay, the FUNC_Exec bit-decoder belt-and-braces
  guard against the historical 0x4-vs-0x200 confusion.

**v1 deferred** (revisit on user feedback):
- Inline scalar arg parsing for typed commands (`setspeed 5`).
  Currently parses the command name, ignores args, routes to dialog.
  Full inline parsing needs the FString-input gap from build
  643-644 to land first.
- Bridge through `APlayerController::ConsoleCommand` for full UE
  string-parsing semantics — same FString-input blocker.
- Live ProcessEvent profiler overlap (next pick).

**Verification target on real game**: Geri (UE 4.27) ships
`UCheatManager` exec commands; ES2 (UE 5.5) also has them. Open
the Console tab → Load → check `UCheatManager` Fly/Ghost rows
appear; double-click Run → should toggle in-game.

### 2. Live ProcessEvent Call Profiler — pick #2

**Effort**: M-L (~1 week) | **Risk**: med (PE is hot path) | **Why**:
Solves the keyword-scoring blind spot — functions whose names give
nothing away but are called every time the user triggers an action.
Current Interesting Funcs ranks by name heuristics; this ranks by
**observed behaviour**.

**Implementation sketch**:
- [Stark](../dll/src/Stark.cpp) already hooks ProcessEvent and ticks
  `s_hookFireCount`. Extend with per-UFunction atomic counter (lock-
  free hash map keyed by `UFunction*`) — increment at the top of
  `HookedProcessEvent`.
- "Recording" mode toggle so the counters are only ticked when armed
  (PE fires thousands of times/sec idle; counting always-on would burn
  CPU for no gain when no one's watching).
- New pipe cmds: `start_pe_profile`, `stop_pe_profile`,
  `get_pe_profile` (returns top-N most-called UFunctions in last
  window). Streaming optional v2.
- UI: new "Live Funcs" tab (or sub-panel under Interesting Funcs?
  decide during implementation) — sliding-window top-N display, pause
  button for "snapshot before vs after" diff workflow.

**Workflow win**: user presses "Start", performs gameplay action
("open inventory"), presses "Stop", sees the 10 functions called
since Start. Functions that fired ONLY during that action are
hypotheses about what implements the action.

**Risk mitigation**: lockless atomic ring-buffer / `std::atomic<uint64>`
per slot. PE hot-path overhead must stay < 100ns/call. Benchmark in
`dll_helpers_test` before shipping.

### 3. ~~Multi-row → One .CT Batch Generator~~ — ✅ shipped (build 760, PR #210)

**Effort actual**: S-M (~half day) | **Risk**: low (purely additive). Polished the existing AA(Baked) / Freeze single-row export into a multi-row batch on the Interesting Functions + Interesting Properties tabs.

**What shipped**:
- `Models/CheatTableRow.cs` — discriminated row type (`CtPropertyRow` wraps `FreezeScriptParams`, `CtFunctionRow` wraps Baked params). Source-panel-agnostic.
- `Services/CheatTableBuilder.cs` — `<CheatTable CheatEngineTableVersion="46">` XML matching CE's File→Save As shape. Root group → per-category sub-groups (alphabetical, Uncategorised trails) → one `<CheatEntry>` per row with `<VariableType>Auto Assembler Script</VariableType>` body. IDs sequential from `BaseId=1000`. XML-escapes all five canonical entities.
- DataGrid `SelectionMode="Extended"` + 📦 toolbar button + code-behind Click handler on both panels.
- Property rows: per-UE-type "obvious cheat" freeze literal (Float=9999.0, Int=99999, Bool=true, Byte=255). Struct/array/non-scalar rows skipped with status counter. Function rows: BakedValues empty (helper zero-fills); description for parameterised funcs reads "edit baked PARAMS in CE".
- Default filename `{Source}-batch-yyyyMMdd-HHmmss.CT`. MainWindow `SaveCheatTableAsync` helper owns the platform dialog + UTF-8 write.

**Deferred**:
- **LiveWalker integration** — heterogeneous row types (functions + fields + struct sub-fields + array elements) + drilldown state. Needs its own UX pass. Tracked as `LiveWalker batch generator v2` below.
- **AOBMaker direct-inject** of generated CT (currently save-to-disk).

Tests: 1028 → 1052 (+24).

### 4. ~~Game Version Diff (SDK / Dump Compare)~~ — ✅ shipped (`9c225ff`, PR #209)

**Effort actual**: S (~2 hours) | **Risk**: zero (pure Python, offline). Cheat-table maintainer pain target: when a game ships a silent UPROPERTY offset shuffle, hand-coded tables break.

**What shipped**:
- `scripts/analysis/diff_dumps.py` — sister to `analyze_dumps.py`, same JSONL corpus, N=2 patch-vs-patch diff. Match keys: class `path` (canonical UE id; `addr` is session-local), properties + functions by `name` within class. Path normalisation (`//Script/X` ≡ `/Script/X`).
- Reports: AddedClasses / RemovedClasses / Moved fields (same name, different offset/size) / Type changed (FloatProperty → DoubleProperty incl. inner_type / struct_type / obj_class / enum) / Function signature changed (return_type / num_parms / parms_size / flags) / Added/Removed functions.
- CLI: `--minimal` (breaking changes only), `--include-engine` (default skips `/Script/`), `--self-test` (6 built-in synthetic-fixture scenarios so the diff logic is checkable without external dumps).
- README extended with the same-game-patch workflow + match-key + known limitations sections.

**Smoke tests passed**: self-diff Geri vs Geri (87 unchanged, 0 changes — identity holds); cross-game Geri vs ES2 (1652/86/0 — module-mismatch warning fires, doesn't crash on massive divergence); `--self-test` all assertions pass.

**Known limitations** (documented in README): no auto-rename detection (renamed field shows as Removed + Added); no function body comparison — only metadata is dumped (logic-only refactors invisible; covered by #2 Live PE Profiler in the future).

### 5. ~~UFunction Return Value Structured Walker~~ — ✅ shipped (build 775, PR #211)

**Effort actual**: S (~half day) | **Risk**: low (purely additive UI panel). Renders struct returns as a 4-column DataGrid (Field / Type / Value / Offset) below the existing single-line decode in InvokeParamDialog.

**What shipped**:
- `Models/StructFieldValue.cs` — pure record. Offset is **absolute** buffer offset (return param offset + sub-field offset) so users can copy it into Find In Containers / CE memrec setup directly.
- `Services/StructReturnDecoder.cs` — static `Decode` + `CanDecode`. Resolution order: KnownStructLayouts (per-version locked) → DLL-discovered dynamic StructFields → empty list. Delegates each byte→typed-value cell to `InvokeParamDialog.DecodeParamValue` so the grid + result-label never disagree on a byte mapping. SafeDecode wraps with try/catch so a single bad field doesn't blow the whole grid.
- InvokeParamDialog: pre-resolves `_returnParam` at construction; clears + hides grid at top of `OnFireClicked` so stale rows don't flash; header label includes struct name.
- **Build 780 follow-up (PR #211)**: switched the DataGrid columns from string-path `Binding` to `DataGridTemplateColumn` + `FuncDataTemplate<StructFieldValue>(lambda)` to clear 18 IL2026/IL3050 AOT warnings.

**v2 follow-ups** (deferred — listed under "Remaining picks" below):
- **ObjectProperty / ClassProperty return resolution** to "Name (Class)" — needs DLL pipe round-trip via `Ubel::GetName` on the returned address.
- **Recursive struct expansion** — `FHitResult.Location` (FVector) renders as one StructProperty row; nested expansion needs recursive DLL-side discovery in WalkFunctions.

Tests: 1052 → 1065 (+13). **Live verification target (user-side)**: Geri's `PlayerCameraManager::GetCameraLocation` returns FVector — grid should show 3 rows (X / Y / Z) at offsets 0x4 / 0x8 / 0xC.

### 6. ~~UCheatManager stripped-body diagnosis + feedback memory~~ — ✅ shipped (build 778, PR #211)

**Effort actual**: S (~half day, mostly write-up) | **Risk**: zero. Documented the cooker-stripped-body gotcha (`#if !UE_BUILD_SHIPPING` wraps the body, reflection metadata survives) and added a Console-panel UX hint.

**What shipped**:
- `ConsoleViewModel.IsLikelyUCheatManagerExec(entry)` — public + static, case-insensitive substring match on `ClassName` OR `SuperName` against "CheatManager". Catches engine class + game-defined subclasses + immediate-super patterns.
- `ConsoleViewModel.SelectedExecHint` — computed property; orange-bordered footer Border on ConsolePanel visible when non-empty. Text: "⚠ UCheatManager subclasses are often body-stripped in cooked Shipping (Result=0 + no in-game effect). Try a game-specific exec or BC function for verification. See memory feedback_ucheatmanager_stripped."
- Memory file `feedback_ucheatmanager_stripped.md` — full diagnostic table comparing wrong-vtable-slot vs cooker-strip failure modes; the discriminator is `Stark::GetHookFireCount()` (>0 + Result=0 + no effect = cooker strip; ==0 = hook on wrong slot).
- `docs/lessons-learned.md` — new bullet under "UFunction Invoke / ProcessEvent".

**Deferred**: full super-chain walk via a new `walk_super_chain` pipe call to catch second-degree subclasses (`BP_MyCheatManager_C : MyGameCheatManager : UCheatManager`) — substring heuristic covers the first two layers; revisit if false-negatives surface.

Tests: 1065 → 1080 (+15). **Live verification target (user-side)**: on Geri or ES2 — (a) `UCheatManager::Fly` returns OK + does nothing; (b) game-specific BC function on the same session works → confirms diagnostic.

### 7. View Snap Hotkey (Property → Snap Hotkey) — pick #7

**Effort**: S-M (~2-3 days) | **Risk**: low | **Why**: Common cheat
need that no existing exec covers — UE has no built-in `setyaw 90` /
`rotate90` / `snapview` exec, and `AddYawInput` is a tiny 1.5°/tick
accumulator unsuitable for snap. The natural shape is "find
`ControlRotation.Yaw` on the live PlayerController + bind a CE
hotkey that snaps it to the next 90° (or arbitrary step)". 95% of
the building blocks already exist from build 719's property freeze
Route B.

**Architecture sketch (mirrors freeze Route B)**:
- New PropertySearch / LiveWalker row action **"Snap Hotkey…"** for
  Float / Double properties (FRotator.Yaw is a Float inside the
  struct; user navigates to the inner property via existing struct
  drill-down).
- New `SnapHotkeyDialog`: step size (default 90°, free-text override),
  direction (next / previous / nearest), hotkey capture (VK_NUMPAD9
  default), wrap-around enable for angle-like fields (auto-mod 360
  when min-max span ≈ 360).
- New `scripts/ue5_snap_helper.lua` embedded resource — shares the
  `_ue5_invoke_busy` reentrancy flag with the invoke + freeze
  helpers. Public API: `snapProperty(cfg, deltaSign) → handle`,
  reuses the CMD_LIST_INSTANCES paginated walk (build 719) to
  resolve live targets per-keystroke (cheap; instances list typically
  < 5 for PlayerController).
- New `SnapHotkeyScriptGenerator.cs` — renders an AA Script with the
  CFG block + `createHotkey(VK_*, snapNext)` + `createHotkey(VK_*,
  snapPrev)` + [DISABLE] cleanup. Per-script keyed handle table
  `_ue5_snap_handles[KEY]` so multiple snap bindings coexist.
- New `Tools → Inject Snap Helper Lua` + `Export Snap Helper Lua…`
  Tools-menu entries (sister to the invoke + freeze pair).
- Reuses AOBMaker `CreateAAScriptAsync` for delivery; no clipboard
  fallback (matches freeze decision — keeps the surface tight).

**Type scope (v1)**: Float, Double. Could extend to Int32 / Int64
(snap to nearest enum value), but Float covers the rotation use
case cleanly.

**Why this generalises beyond rotation**: the same "snap to nearest
N" pattern covers MoveSpeed multipliers (0.5x / 1.0x / 2.0x / 4.0x
cycling), zoom levels, time-dilation cycling — anywhere the user
wants a small discrete set of values bound to a single hotkey
toggle. Not pitched as "snap by 90°" specifically but as
"snap-to-step on a property".

**Test plan (live)**:
- Geri (UE 4.27): find `APlayerController::ControlRotation.Yaw`
  via LiveWalker, bind NumPad9 = +90°, NumPad7 = -90°. Press in-
  game, view should snap to the four cardinal directions with no
  visible interpolation.
- ES2 (UE 5.5): same test path; confirms the helper handles UE5
  PlayerController layout without per-version branches.

**Out of scope for v1**: animated/interpolated snap (would need a
tick loop, not just a hotkey write); collision-aware snap (skipping
to next free direction); chord-key hotkeys.

### Add-on: Universal Hotkey Loop option for AA(Baked) — bonus S item

**Effort**: S | **Risk**: low | **Why**: AA(Baked) currently runs the
generated script once per [ENABLE]. Many cheat scenarios need
"every X ms": refill health every tick, write speed multiplier each
frame, etc. CE's [ENABLE]/[DISABLE] block already supports `timer`
hooks; FreezeScriptGenerator (build 719) shows the pattern.

Add an "Auto-tick every N ms" checkbox to InvokeParamDialog's
CopyBakedScript mode. When checked, generated script wraps the
`invokeUFunction` call in a `createTimer(N, callback)` block —
[DISABLE] tears it down. Same handle-table pattern as FreezeScript
keyed by `class::func@instance` so multiple ticking scripts coexist.

Lands as a 1-day add-on after pick #3 (CT batch generator) — both
touch the script generator + dialog wiring.

### Dropped from consideration — reasoning preserved

- ~~**Game Profile persistence**~~ — already covered by
  [Flamme::SaveResults / LoadHints](../dll/src/Flamme.cpp) +
  [AobUsageService](../ui/UE5DumpUI/Services/AobUsageService.cs).
  Remaining gaps (DynOff offset table cache, user favorites, invoke
  param presets) are low-pain UX polish, not value-visible wins. Only
  revisit if a user reports DynOff re-derivation as a noticeable wait
  (currently < 100ms after AOB scan completes).
- ~~**32-bit UE4 support**~~ — discussed 2026-05-26, deferred. Would
  need a parallel Win32 DLL build + new 32-bit AOB pattern bank + per-
  ABI ParamBuffer rewrite (thiscall vs Microsoft x64). Estimated 2-3
  weeks + corpus validation. No identified user demand; most UE4
  titles people target are 64-bit.

-----

## Active plan: Call-UE-function strengthening

The "Call UE function" capability is currently the weakest link in the
"discover → use" workflow. Five-step plan agreed 2026-05-10:

### 1. AA-Script export from UI ([3a]) — ✅ shipped (build 590-596)

**Effort**: M (actual: ~M as estimated) | **Risk**: low (no regressions)

LiveWalker UFunction rows gained a third button **AA(Baked)** that
opens the existing param dialog in `CopyBakedScript` mode and ships
a non-interactive AA Script via AOBMaker / clipboard. Sister to the
existing `Generate Script` (in-CE form) and `Pipe Invoke` (in-app
test) buttons.

Architecture (revised from the original plan after design review):
- **Helper file in CE table** — instead of inlining the mailbox
  protocol in every AA Script, the generated script depends on
  `ue5_invoke_helper.lua` being embedded in the user's .CT via
  Cheat Engine's `Table -> Add File...` menu. The script uses
  `findTableFile()` + `load()` to resolve the helper at runtime. No
  filesystem fallback — explicit error + setup instructions if the
  file is missing.
- **Tools menu export** — `Tools -> Export CE Helper Lua File...`
  streams the embedded helper to a user-chosen path so they can drop
  it next to their .CT.
- **Re-declaration safe** — helper functions use the
  `if not invokeUFunction then function ... end
  registerLuaFunctionHighlight('invokeUFunction') end` pattern so
  multiple AA scripts loading the same helper don't redefine it.
- **Print discipline** — generated scripts are silent on success
  (auto-close the lua engine via `synchronize(getLuaEngine().Close())`
  per the user's hygiene rule), print + showMessage on error.

Files touched:
- `scripts/ue5_invoke_helper.lua` (new, ~285 lines)
- `ui/UE5DumpUI/Models/BakedParamValue.cs` (new)
- `ui/UE5DumpUI/Services/BakedScriptGenerator.cs` (new, ~250 lines)
- `ui/UE5DumpUI/Services/HelperLuaResource.cs` (new)
- `ui/UE5DumpUI/Views/InvokeParamDialog.cs` (`InvokeDialogMode` enum,
  `Copy AA Script` button, `CollectBakedValues` helper)
- `ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` (`CopyBakedScriptCommand`)
- `ui/UE5DumpUI/Views/LiveWalkerPanel.axaml` (third button column)
- `ui/UE5DumpUI/Views/MainWindow.axaml` (Tools dropdown)
- `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs` (`ExportCeHelperLuaCommand`)
- `ui/UE5DumpUI/UE5DumpUI.csproj` (EmbeddedResource link to helper)
- `ui/UE5DumpUI/Resources/Strings/en.axaml` (button + tooltip + Tools menu strings)
- `ui/UE5DumpUI.Tests/InvokeScriptTests.cs` (+36 test cases:
  baked-render correctness for each UE type, struct flattening,
  unparseable-input fallback, Lua-quote escaping, helper resource
  reachable from assembly manifest)

Tests: 597 -> 633 (504 -> 540 C# + 62 dll_helpers + 31 utf8_helpers).

### 2. Interesting-functions finder ([3c]) — ✅ shipped (build 597-607)

**Effort**: M (actual: ~M as estimated) | **Risk**: low (no regressions)

New "Interesting Funcs" tab between Property Search and Game Classes.
Per-row Live + AA(B) actions. Architecture decisions actually shipped:

- **Scoring is UI-side** (not DLL) so keyword tables can be tuned
  without DLL rebuild. DLL just enumerates via new `list_all_functions`
  pipe cmd.
- **Scoring file**: `KeywordScoringTable.cs` -- 5 visible categories
  (Stats / Inventory / Movement / Combat / Utility) + an
  ExplicitMovementCheats sub-bucket (NoClip/Fly/God/Ghost/Invincible/
  Invisible at +8 per-hit) so explicit movement cheats outrank a
  Utility-noisy `DebugCheatManager` class name.
- **Substring-noise lesson** (caught by unit tests): short acronyms
  like HP/MP/SP/XP/TP collide with engine-spam words ("Component"
  contains "mp"). Dropped them from the keyword tables; full forms
  only (Health/Mana/Stamina/Experience/Teleport).
- **Tab insertion shifts ClassStruct from index 4 -> 5**; updated
  `GameClassFilter.NavigateToClassStruct` accordingly.
- **Cross-tab nav**: Live button uses FindInstancesAsync -> non-CDO
  pick -> Live Walker; falls back to ClassStruct (with status hint)
  when class is CDO-only. AA(B) button reuses the InvokeParamDialog
  CopyBakedScript mode from step 1.

Files touched:
- `dll/src/Aura.h` + `Aura.cpp` (`AllFunctionEntry` /
  `EnumerateAllFunctions`)
- `dll/src/Renge.h` + `Fern.cpp` (CMD_LIST_ALL_FUNCTIONS handler)
- `ui/UE5DumpUI/Models/AllFunctionsResult.cs` + `ScoredFunctionRow.cs`
  (new)
- `ui/UE5DumpUI/Services/KeywordScoringTable.cs` +
  `CategoryDisplayConverter.cs` (new)
- `ui/UE5DumpUI/Services/DumpService.cs` + `Core/IDumpService.cs`
  (`ListAllFunctionsAsync`)
- `ui/UE5DumpUI/ViewModels/InterestingFunctionsViewModel.cs` (new)
- `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs` (cross-tab handlers
  + `_aobMaker` field for AA Script delivery)
- `ui/UE5DumpUI/Views/InterestingFunctionsPanel.axaml` + .cs (new)
- `ui/UE5DumpUI/Views/MainWindow.axaml` (new TabItem; ClassStruct
  shifted)
- `ui/UE5DumpUI/Resources/Strings/en.axaml` (~14 new strings)
- `ui/UE5DumpUI.Tests/CsxExportServiceTests.cs` (un-seal
  `StubDumpService`, mark `ListAllFunctionsAsync` `virtual`)
- `ui/UE5DumpUI.Tests/KeywordScoringTableTests.cs` + `InterestingFunctionsViewModelTests.cs`
  (+60 test cases)

Tests: 633 -> 693 (540 -> 600 C# + 62 dll_helpers + 31 utf8_helpers).

### 3. UFunction metadata exposure ([4]) — ❌ skipped (build 608 research)

**Effort**: M (estimated) | **Risk**: med (now confirmed: ~total no-op)

Original plan: read `UField::MetaDataMap` to surface Blueprint
`DisplayName` / `ToolTip` / `Category` / `Keywords`.

Research finding ([dev-log build 608](dev-log.md)): the metadata map
is `#if WITH_METADATA` (= `WITH_EDITORONLY_DATA`). On Windows/Mac/Linux
Shipping builds the macro is `1` so the `MetaDataMap` POINTER exists,
but the cooker strips the actual content during cook -- `GetMetaData()`
returns empty string at runtime in every cooked Shipping game.
Verified against Engine/Source/Runtime/CoreUObject/Private/UObject/Field.cpp
+ Core/Public/Misc/CoreMiscDefines.h.

Implication: implementing this would only pay off on DebugGame /
Development-config builds, which a cheat-engine user almost never
encounters. The work was estimated at ~250 LoC + per-version offset
table -- not worth shipping for ~zero real-world value.

**Pivot**: did B (CamelCase tokeniser) instead -- closes the substring-
noise gap from the v1 finder so short acronyms (HP/MP/SP/XP/TP) can
work safely as keywords, materially improving the existing scorer
without needing metadata at all. Shipped build 608+.

If a user reports the finder missing obvious cheat-relevant functions
in a real game, revisit this -- but expect to need a per-version
`MetaDataMap` offset table only useful for testing dev-config builds.

**Effort**: M | **Risk**: med

Blueprint-derived UFunctions carry a metadata map (`UFunction::*Meta*`
calls in UE source) with `DisplayName` / `ToolTip` / `Category` /
`Keywords`. Currently we only expose the cooked function name. Surfacing
metadata gives:

### 4. Update interesting-functions + existing function lists with metadata ([3c rev2]) — ❌ skipped (depended on 3)

Skipped because step 3 was skipped — see step 3 entry above. Tokeniser
work delivered the same end value (better keyword matching) without
needing metadata.

The "stale step 3" original spec preserved here as historical context:

- Better display strings ("Add Player Currency" beats `AddMoney`)
- A **second corpus** for the keyword scorer in step 2 (matches against
  Category `Player|Stats|Combat` etc. are higher-signal than matches
  against function names alone, which can collide with engine-internal
  helpers)
- Tooltip text in the UI invoke dialog

Implementation notes if a user requests this anyway:
- `dll/src/Ubel.cpp` UFunction reader — probe `MetaDataMap` (TMap<FName,
  FString>) at the version-specific offset
- `Aura::WalkClass` augment per-function row with `metadata: { displayName,
  toolTip, category, keywords }`
- Pipe schema bump
- UI: new columns in the function lists, tooltip wired
- Pre-req: read UnrealEngine source for the `UMetaData` /
  `UFunction::FindMetaData` chain; build per-version offset table
  (UE 4.27, 5.0, 5.4, 5.7 minimum)

Original step 4 incremental rev2 spec follows for completeness:

- Keyword scorer in step 2 also matches against `DisplayName`,
  `Category`, `Keywords` metadata fields (each weighted higher than
  function-name match because they are author-curated, not cooked)
- Existing function lists (Class Structure, PropertySearch results)
  show `DisplayName` as primary label with cooked name as small
  secondary text

### 5. UI invoke dialog overflow fix ([3b]) — ✅ shipped (build 609)

**Effort**: S (actual: ~XS) | **Risk**: low (no regressions)

The actual issue wasn't a missing ScrollViewer (that was already there
since the dialog was first written). It was the hard `MaxHeight=700`
window cap that prevented users on big monitors from resizing the
dialog larger to see all params.

Fix:
- Window `MaxHeight` 700 → 1100; added `Height=480` default + `MinHeight=240`
- `SizeToContent = SizeToContent.Height` so the dialog grows to fit
  the form, then caps at MaxHeight
- ScrollViewer wrapping the param panel gained `MinHeight=200` -- when
  the FIRE result label expands after a successful invoke, DockPanel
  would otherwise let the bottom panel squish the scroll area down to
  a sliver. 200px floor keeps ~6 param rows visible regardless.
- Skipped the "Reset to defaults" button -- v1 use case for this is
  unclear; can add if requested.

File touched: `ui/UE5DumpUI/Views/InvokeParamDialog.cs` (~10 lines).

### 6. One-click helper inject into open CE table — ✅ shipped (build 611)

**Effort**: S (actual: ~S) | **Risk**: low (additive — old export menu kept as fallback)

Removes the manual save-to-disk + `Table -> Add File...` dance for
brand-new users by wiring a new `InjectTableFile` pipe command into the
AOBMaker CE Plugin and a matching `IAobMakerBridge.InjectTableFileAsync`
on the UE5DumpUI side.

Plugin side (`D:\Github\AOBMaker\plugins\CEPlugin\src\pipe_server.cpp`):
`HandleInjectTableFile` runs `findTableFile` (delete-if-exists) +
`createTableFile` + `Stream.write` + `Stream.Size` verify under
`synchronize` so all CE Lua APIs execute on CE's main thread. Long-bracket
level is chosen dynamically so any payload is safe (even one containing
`]==]`). Protocol constants added to `protocol.h`; routed from
`HandleClient`.

UE5DumpUI side: new `Tools -> Inject Helper into Current CE Table` menu
item -> `MainWindowViewModel.InjectCeHelperLuaCommand` -> probe
`CheckAvailabilityAsync` -> `InjectTableFileAsync(fileName, content)` ->
status text covers all four user-visible end-states (no bridge, CE not
running, success, failure). Inject path uses a 15 s response timeout
(vs. 5 s default for navigation calls) to give synchronize round-trip
headroom for ~10 KB payloads.

Tests: integrated via cherry-pick onto current dev. Final total
670 C# xunit (+8 from this change after subclassed-stub bridge
addition) + 62 dll_helpers + 31 utf8_helpers = **763 total**.
Coverage: AobMakerInjectTableFileTests (3 — wire-model serialization,
relaxed encoder for single quotes, bridge service arg validation),
MainWindowInjectHelperTests (4 — all four end-states via recording bridge),
plus FakeAobMakerBridge stub gained the new method (+1 test indirectly).

Spawned session worked in a separate worktree from `aa2ac0d`; cherry-
picked `44a3943` onto current dev as `67fd61b` rather than merging,
keeping the linear history. Doc-only conflicts (dev-log.md / roadmap.md /
todo.md "latest" headers) resolved by keeping both entries with
this one bumped to build 611.

-----

## Pending live-game verification

Features that shipped + unit tests pass but need real game smoke tests
before we can declare them solid on multiple titles.

### Property freeze (Route B) — needs live test on a respawning-NPC game (build 719, 2026-05-24)

**Effort**: 0 (verification only) | **Risk**: low | **Why**: 1015 unit
tests green, but the helper's interaction with CE's main-thread timer
pump under a real game's frame load is unproven. What to watch for:

1. **Tick FPS impact**: Default 50ms tick = 20 writes/sec per cached
   instance. On a game with 8-16 teammates, that's 160-320
   `writeFloat` per second on the CE main thread. Verify no visible
   stutter in the game window while a freeze script is active.
2. **Rescan cadence at respawn**: kill a teammate, confirm the next
   5-second rescan picks up the new instance and the freeze resumes
   on the respawned actor (cache turnover working).
3. **Vtable liveness guard**: the helper skips writes when the
   instance's first qword is 0 (freed). Sanity check: enable a
   freeze, force a level transition, confirm no crash even if the
   rescan hasn't fired yet (stale pointers in cache pointing to
   recycled pages).
4. **AOBMaker gating UX**: with CE closed, button should be disabled
   and tooltip should explain the requirement. Re-open CE, switch
   to PropertySearch tab — button should re-enable within the 5s
   cooldown.
5. **Multi-script coexistence**: enable Freeze HP + Freeze MP
   simultaneously on the same class. Both should run; disabling
   one should NOT stop the other (per-script keyed handle table is
   the contract).

First good candidate: Geri (UE 4.27, ProcessEvent verified) since it
has respawning NPCs and a known healthy invoke pipeline. Use
PropertySearch to find a float property on a Pawn-derived class.

### ~~ProcessEvent vtable fix — partial confirmation on ES2 (UE 5.5)~~ — ✅ FULL VERIFICATION (build 648, 2026-05-11)

**Effort**: 0 (done) | **Risk**: — | **Why**: Verified end-to-end on
**two UE versions** spanning the original repro range:

| Game | UE | Actual PE slot | Old hardcoded | Validator | Scenarios passed |
|---|---|:---:|:---:|---|---|
| EverSpace 2 | 5.5 | `vtable+0x278` | `0x228` (off 10 slots) | 2360 fires / 1500ms | KismetMath Add_IntInt=7, Multiply_DoubleDouble=12, InventoryLib::GetTotalCargoSpaceOfShip=73 |
| The Artisan of Glimmith (Geri) | 4.27 | `vtable+0x220` | `0x218` (off 1 slot) | 1260 fires / 1500ms | KismetMath Add_IntInt=7, Multiply_FloatFloat=12, **CharacterMovementComponent::GetMaxJumpHeight=89.99 (instance method, game-thread dispatch)**, **PlayerCameraManager::GetCameraLocation=FVector (struct return, game-thread dispatch)** |

**KismetMathLibrary "stub" hypothesis is now falsified on both UE
versions** (see retracted [feedback_kismet_stubs.md]). Both
static-native fast path (KismetMath) AND game-thread dispatch (Geri
scenarios 3 + 4 are instance methods on CharacterMovementComponent /
PlayerCameraManager) work correctly.

Remaining nice-to-have verification (lower priority — fix is already
considered solid):
- A UE 4.18-4.24 game (smaller vtable, lower slot offset) to make
  sure the pattern scanner's `[0x100, 0x300]` window catches lower
  slot positions. Pick from Octopath Traveler / IDOLM@STER STARLIT
  SEASON / DQ XI S.
- A custom publisher fork (Square Enix DQ/FF7 series) to confirm the
  pattern scan finds PE even when the binary has been heavily
  modified post-cook.

### Mimic: zero ReturnValue slot before invoke so verify-mode dumps are unambiguous

**Effort**: S | **Risk**: low | **Why**: ES2 live test 2026-05-11
showed `GetTotalCargoSpaceOfShip(0)` returning Before/After dumps:
```
Before: 00 00 00 00 49 00 00 00
After : 00 00 00 00 49 00 00 00   <- 0x49 = 73 in ReturnValue slot
```
The `0x49` was identical pre- and post-invoke, so we can't tell apart
"function ran and wrote 73" from "function ran but didn't touch
ReturnValue (leaving stale 73 from previous call)". Fix:
`Mimic::HandleInvoke` should overwrite the ReturnValue slot with a
sentinel (e.g. `0xCDCDCDCDCDCDCDCD`) or zero before calling PE. Then
the After dump unambiguously shows what PE wrote — if it's still the
sentinel, PE didn't write the return slot. Affects: all verify-mode
AA Scripts. Trivial 2-line patch in `dll/src/Mimic.cpp`'s static-
native fast path + the game-thread dispatch path.

### CE Lua hang during AA Script activation (ES2 2026-05-11 session 2)

**Effort**: M (mitigation) | **Risk**: low | **Why**: After restarting
the game (new proxy DLL load) + UE5DumpUI, the user tried Scenario 3
again. DLL stayed healthy: `find_instances InventoryLib: 1 found` at
22:27:23, then 72-second silence with no `Mailbox: received cmd=4`,
ending with `Client disconnected` at 22:28:35 + `PipeServer Stopped`
at 22:28:43. The AA Script never reached the mailbox — CE Lua either
froze or showed a hidden error dialog. Mitigations to consider:

1. **Re-arm helper-injected check on every UI Connect**: when UI
   connects to a fresh DLL session, optionally re-prompt the user
   (or auto-inject) the `ue5_invoke_helper.lua` if AOBMaker is
   reachable. Currently the helper persists in the .CT across CE
   restarts but NOT across game restarts in proxy mode (since proxy
   doesn't touch the CT). Easy to forget.
2. **Mailbox heartbeat on the AA Script side**: have the generated
   AA Script `print()` a "starting" line before writing to the
   mailbox, so the CE Lua log shows progress even if the mailbox
   write later hangs.
3. **Timeout/watchdog on the CE Lua side**: less feasible because
   we don't control CE's Lua engine. But we can add an explicit
   `if not g_invokeMailbox then showMessage('Helper not loaded —
   run Tools → Inject Helper first'); return end` early-exit in
   the helper itself.

We can't distinguish AA-Script-error from CE-Lua-freeze from our DLL
side because the mailbox just never receives anything. So this is
primarily a UX hardening task, not a correctness bug.

### Static-native ProcessEvent fast path (build 636)

**Effort**: S | **Risk**: low | **Why**: Verified on ES2 (logs show
`static-native fast path (flags=0x14022403, bypassing GameThreadDispatch)
... INVOKE result=0`). Need to confirm on a game where the user is
actively playing (game thread pumping) so we can compare static-native
fast path latency vs. instance-method GameThreadDispatch latency on the
same session. Also confirm that **stateful** UFunctions (BlueprintEvent
/ RPC / non-static) still correctly route through GameThreadDispatch
and don't fall into the fast path by accident.

Test plan:
- Active game session (player moving), Interesting Funcs -> any static
  BFL function (Stats/Math) -> AA(B) + Verify -> should print result
  in <50ms regardless of game idle/active state
- Same session, instance method (e.g. PlayerController::* setter) ->
  AA(B) + Verify -> uses GameThreadDispatch; should also succeed if
  game thread is active. Idle test (let game sit on title screen) ->
  expect timeout `-5` for the instance method but **not** for the
  static native helper.

### FPROPERTY_FLAGS offset fix (build 642)

**Effort**: S | **Risk**: med | **Why**: The +8 -> +4 offset fix
flipped how the walker reads CPF_ReturnParm / CPF_OutParm / CPF_Parm
on **every** UFunction parameter across **every** UE version. The
verify-mode PARAMS table is now correctly emitting ReturnValue as a
return slot (not as an input), but the same flag-read also feeds
`docs/dll-spec.md` UFunction listings, the Class Structure tab's
function display, USMAP export, etc. Sweep the 12+ tested games
quickly to confirm no regression on any of them.

Test plan:
- For each game in [docs/roadmap.md](roadmap.md#tested-games-last-verified-2026-05-10),
  open Class Structure on a known UClass with mixed input/output params
  (e.g. Character::AddMovementInput, PlayerController::GetMousePosition)
  and check the Functions section's Return column populates.
- Quick sanity: Interesting Funcs -> any function with a return value
  -> AA(B) -> generated PARAMS should NOT include ReturnValue (used
  to before this fix).

### Verify Return Value diagnostic mode (build 637 / refined 644)

**Effort**: S | **Risk**: low | **Why**: Live-tested on ES2 and worked
end-to-end (mailbox resolution + Before/After dump + decoded scalar
print). Need to confirm pointer-return functions show 0x prefix
correctly and FString-return functions show the "see After: dump
above" hint.

Test plan:
- Pick a function returning UObject* (e.g. `GetWorld`, `GetGameInstance`,
  `GetOuter` if BC) -> Verify on -> expect `(pointer@N) = 0xFFFFFFFF...`
- Pick a function returning FString (e.g. `GetGameName`) -> Verify on ->
  expect `(fstring@N, size=16B) -- complex return; see After: dump above`
  and dump shows non-zero ptr+count when the call succeeded.

-----

## ~~CRITICAL: ProcessEvent vtable detection is wrong~~ — ✅ shipped (build 648)

**Effort**: M (actual: ~M) | **Risk**: med (no regressions in 786-test
suite; live-game smoke test pending — see "Pending live-game
verification" section above).

Replaced the version-table vtable detector with a **function-body
pattern scan** modeled on Dumper-7's approach (vendor/Dumper-7/Dumper/
Engine/Private/OffsetFinder/Offsets.cpp:15-74). Iterate vtable slots in
the `[0x100, 0x300]` window, read each candidate function's first 0xF00
bytes, look for two `TEST [reg+disp32], imm32` instructions that
ProcessEvent uniquely contains:

- Pattern 1 (within first 0x400 bytes): `imm32 = 0x00000400` (FUNC_Native test)
- Pattern 2 (within first 0xF00 bytes): `imm32 = 0x00400000` (high-flag test)

`disp32` points at `UFunction::FunctionFlags` (`0x88..0xC0` across UE
versions) — we wildcard those bytes so the scan is FunctionFlags-offset
agnostic. The old UE-version-table heuristic is kept as a `LOG_WARN`
fallback path for unusual compiler output (heavily-optimised LTO,
custom publisher fork).

**Belt-and-braces validation** (the real safety net): `Stark` now
exposes `GetHookFireCount()`, an atomic counter ticked at the top of
`HookedProcessEvent`. After `InstallHook` succeeds, `Frieren::
TryInstallGameThreadHook` spawns a detached 1500ms validator thread —
if the counter is still 0 when it wakes up, log a loud `[ERROR]
GameThreadDispatch: VALIDATION FAILED` with the hooked address. UE's
real ProcessEvent fires many times per second under normal gameplay,
so a zero reading after 1.5s is strong evidence of a wrong-slot hook.
Silent vtable-misdetection is the whole reason this bug slept for
600+ builds; we now refuse to keep that failure mode silent.

Files touched:
- `dll/src/Frieren.cpp` — new `DetectProcessEventVTableOffsetByPattern`
  + legacy `ByVersion` fallback + post-install validator thread
- `dll/src/Stark.h` + `Stark.cpp` — `s_hookFireCount` atomic +
  `GetHookFireCount()` API; counter ticked in `HookedProcessEvent`
- `docs/lessons-learned.md` — new entry "Vtable-index detection is
  unreliable" + "Validate hooks by side-effect, not metadata"
- `docs/dev-log.md` — build 648 entry

Tests: 786 total (693 C# + 62 dll_helpers + 31 utf8_helpers) — no
regressions. Build 648 on dev.

### Pending live-game re-verification (CRITICAL)

Until the hook is observed firing on real games, treat the build 648
fix as "compiles + tests pass" — not "live-verified". Need:

1. Run on **ES2 (UE 5.5)** with a player-controlled session, look for
   `GameThreadDispatch: validation OK — hook fired N times` in
   `init-*.log` after first invoke. The instance-method invokes that
   previously timed out at `-5` should now succeed.
2. Run on **Geri / The Artisan of Glimmith (UE 4.27)**, same check.
3. Re-test KismetMathLibrary helpers (the `feedback_kismet_stubs.md` memory
   note, which is machine-local and not in git — the link that stood here
   carried a concrete `C:\Users\<name>\...` path and was unfollowable)
   — with a correctly-hooked PE they *might* return real values; if
   they don't, the stub-pattern hypothesis stands. Either way, update
   the memory note based on actual observation.

### Out of scope for build 648

- AOB scan of the PE prologue (the original `Fix plan` step 1). The
  function-body pattern approach is functionally equivalent — both
  rely on PE's distinctive byte sequence — but is more robust because
  we don't have to guess where the prologue lives in `.text`. Can
  revisit if the function-body scan whiffs on a real game.
- Hot re-hook if validation fails. Unhook-while-game-thread-may-be-
  inside-the-trampoline is itself unsafe (see `Stark::RemoveHook`
  comment); the validator just observes and logs. User reaction is to
  collect logs + report so we can extend pattern coverage.

-----

## Call-UE-function feature gaps (discovered build 643-644 live test)

Two real gaps surfaced when test-driving the verify-mode AA Script
flow on Everspace 2 (UE 5.5). Both are caller-experience improvements,
not correctness bugs in our existing code.

### Document KismetMathLibrary stub-pattern in cooked Shipping; suggest better verification targets

**Effort**: S | **Risk**: low | **Why**: A naive user trying to verify
the invoke pipeline reaches for `KismetMathLibrary::Exp(8) -> 2980.957`
or `Add_IntInt(3, 4) -> 7` because they're the simplest possible
sanity tests. On UE 5.5+ cooked Shipping these consistently return 0
even though the function lookup, fast-path, and ProcessEvent
dispatch all succeed -- the cooker leaves the reflection metadata
intact but the `execXxx` thunk has been stripped or replaced with
a no-op stub (likely a side effect of UE's BlueprintFastCall
optimisation, where the Blueprint VM bytecode bypasses ProcessEvent
for these helpers entirely).

Live verification on ES2 (UE 5.5):
- `KismetMathLibrary::exp` (lowercase) -> 0 (Before/After identical)
- `KismetMathLibrary::Multiply_DoubleDouble(3, 4)` -> 0 (A and B
  written, ReturnValue stays 0)
- `KismetMathLibrary::Add_IntInt(3, 4)` -> 0 (same pattern; rules
  out "double precision specifically broken" hypothesis)

What to do:
- Add a "Recommended verification targets" hint in the InvokeParamDialog
  status footer when the selected class is `KismetMathLibrary` /
  `KismetSystemLibrary`: "These BlueprintFunctionLibrary helpers are
  often stub-only in cooked Shipping. Verify with game-specific
  classes instead."
- Update [docs/lessons-learned.md](lessons-learned.md) and
  [docs/test-games.md](test-games.md) so the lesson survives across
  sessions.

This is **NOT** a feature to enable calling KismetMathLibrary
helpers -- there's nothing we can do from outside the cooker. It's
a UX hint to redirect users to verification targets that actually
work (game-specific instance methods on a UObject when the game is
actively playing, so ProcessEvent traffic drains the queue).

### FString / FText / TArray input support in baked AA Script

**Effort**: M | **Risk**: med | **Why**: Functions like
`KismetSystemLibrary::PrintString` are observable side-effect targets
(player sees text in-game) ideal for verifying ProcessEvent works
end-to-end -- but currently unreachable because we can't bake an
FString **input** value. Helper's `writeBakedParams` only handles
scalar inputs (bool/int/float/double/pointer); FString needs:

1. Allocate a wide-char buffer in CE address space
2. Write the FString header at the param offset:
   - ptr (qword) = buffer address
   - count (int32) = char count
   - max (int32) = char count (typically same as count)
3. Keep the buffer alive across the ProcessEvent call (CE's
   `allocateMemory` returns a stable address)
4. Free the buffer after the call (in the cleanup timer)

Same pattern applies to FText (slightly more complex header) and
TArray of scalars (count + capacity + ptr to elements).

Implementation sketch:
- Helper-side: new `writeFString(buffer, header_addr, str)` /
  `freeFString(buffer)` shared utilities
- Generator-side: detect `StrProperty` / `TextProperty` / scalar
  `ArrayProperty` in `BakedParamValue` and emit the alloc + header
  + free dance instead of the simple `writeQword` path
- Dialog-side: TextBox should accept the unquoted user string;
  generator emits Lua-string literal with escaping
- Cleanup-side: extend the cleanup timer to free any allocated
  buffers before disabling the memrec

Out-of-scope for v1: complex-typed return decoding (FString return
already handled via "see After: dump" hint -- input support doesn't
imply output support); StructProperty inputs (the dialog flattens
known structs but generating allocs for nested containers is
significantly more work).

Read-back path: the helper's `readUFunctionReturn` already has
distinct read paths per type -- could grow a `'fstring'` token that
returns the decoded Lua string instead of a number. Optional v2.

-----

## Property Origin Resolver — proposals B + C still on table

> The starter block at the **top of this file** (build 780 refresh) is the current source of truth for next-session picks. The historical 2026-05-20 (build 715) block + its 7-item suggestion list has been **archived to [docs/archive/todo-history-build-715.md](archive/todo-history-build-715.md)**.
>
> Of the 7 starter suggestions in that historical block:
> - #1 Live-game verification of Invoke Stage 1+2 → user-side, still open
> - #2 More dumps for genre coverage (pure-horror / fighting / RTS / sports-sim) → user-side, still open
> - #3 `walk_functions_batch` follow-up → still open, S effort
> - #4 FString / FText / TArray **input** for baked AA Script (distinct from the Value Search Phase 2 work) → still open, M effort, med risk
> - #5 Invoke Stage 3 (class validation) → still deferred until a real crash motivates
> - #6 Class Family Browser (Proposal C) → still open, L effort, needs own planning
> - #7 Runtime `keywords.json` override → still open, M effort, only if a user asks
>
> All seven are detailed in the [Call-UE-function feature gaps](#call-ue-function-feature-gaps-discovered-build-643-644-live-test) section + the build-689 "Class Family Browser" subsection below.

### Proposal B — DEFERRED (build 689)

Original B (per-row "similar BP-added properties" suggestions on a row
click) is now deferred indefinitely. B' (the broad-sweep "find HP/MP/etc
in unusual containers" approach) shipped and is calibrated, which gives
us the cheat-discovery workflow without B's added complexity. If a real
user reports the gap B was meant to fill ("the engine field at the wrong
layer — show me the BP-added bool that the game's TakeDamage override
actually checks"), revisit B then; until then, skip.

Proposal A (dedupe-by-defining-class) shipped as build 610.
**Proposal B' shipped as build 670 + calibrated through build 687.**

-----

## New pending items (discovered build 657-689)

### ~~Fix SdkExportService BPGC filter~~ — **shipped build 690**

C# Full SDK export was filtering with bare `ClassName is "Class" or "ScriptStruct"`
which silently dropped every BlueprintGeneratedClass — same bug class as the
build 673 DLL fix, different code path. Now calls
`DumpAllService.IsClassLikeMetaName` directly so the whitelist
(Class + BPGC + AnimBPGC + WidgetBPGC + DynamicClass) stays in lockstep.
Regression test `GenerateFullSdkAsync_AcceptsAllClassLikeMetasAndScriptStruct`
covers every meta variant explicitly so it can't silently regress.

### ~~`walk_class_batch` — Full SDK / Dump All pipe round-trip amortisation~~ — **shipped build 693-696**

C# Full SDK export + Dump All Metadata both ran one pipe round-trip per
class (~4400 on a TQ2-size game). Build 693-696 batches in chunks of
200 via a new DLL `walk_class_batch` command. Implementation pattern
mirrors build-685 `search_properties_batch` — `Aura::WalkClassesBatch`
is a trivial loop over `Ubel::WalkClassEx`, so each batch element is
byte-identical to a single `walk_class` response. Shared JSON
serialiser (`EncodeClassInfoToJson` in Fern.cpp) + shared C#
deserialiser (`DumpService.DeserializeClassInfo`) make byte-equivalence
structural rather than tested.

Three-layer safety net per the user's "I can't tell if SDK export drops
a class" concern:

1. DLL batch = loop over single → byte-identical at the source
2. Single + batch share the JSON encoder/decoder → no wire drift
3. `WalkClassBatchEquivalenceTests.cs` runs both consumers against a
   250-class fixture through a happy-path stub AND a forced-fallback
   stub; asserts byte equality at 7 class-count edges (0/1/199/200/201/
   250/400) + a truncated-batch defensive test.

Consumers chunk batches at 200 with per-chunk single-call fallback so
per-class error attribution (the `// ERROR:` line in SDK / `kind=error`
JSONL in Dump All) survives any batch failure.

Tests: 802 → 817 C# (+15). Estimated wall-time speedup on big games:
2-5× for Full SDK Export (latency amortisation only; WalkClass doesn't
re-walk GObjects).

**Follow-up candidate**: `walk_functions_batch` for DumpAll (still does
WalkFunctions single-call per class). Same shape, smaller win — skip
unless profiling shows it as the new bottleneck.

### ~~Multi-module GWorld scan (Satisfactory class)~~ — **shipped build 691**

**Status**: Both halves resolved, neither was the originally-suspected bug.

- **Scan side** (originally framed as "multi-module GWorld scan needs implementing"):
  turns out `Macht::AOBScanAllModules` was ALREADY in place from the build-509 SIMD
  scanner rewrite (commit `589fc35`), and `Genau::ScanForTarget` already invokes it
  with `tryMultiModule=true` for GObjects / GNames / GWorld / SparseDelegates. The
  15-game dump corpus already contained `FactoryGameSteam`'s clean output — the
  scan side was working all along; only the roadmap note was stale (now corrected).
- **Proxy deploy side** (the real bug): user couldn't drop `version.dll` because
  Satisfactory's actual launcher exe lives in `Engine\Binaries\Win64\` (modular UE
  build), NOT `<Game>\Binaries\Win64\`. UI proxy-deploy scanner was explicitly
  skipping `Engine\` subdir + breaking on the first `*.exe` it saw, which for
  modular layouts meant the launcher dir was invisible. Build 691 removes the
  Engine-skip and filters `CrashReportClient.exe` via `IsKnownStubExe`, so the
  scanner walks `Engine\Binaries\Win64\` but never surfaces phantom rows for
  monolithic games (where that folder only contains CrashReportClient).

Files touched: [ProxyDeployService.cs:140](../ui/UE5DumpUI/Services/ProxyDeployService.cs#L140),
[ProxyDeployTests.cs](../ui/UE5DumpUI.Tests/ProxyDeployTests.cs) (3 new tests —
modular layout, monolithic regression, orphan-Engine-dir edge case),
[lessons-learned.md "Proxy DLL Deploy"](lessons-learned.md#proxy-dll-deploy).

User-side verification 2026-05-12: manual `version.dll` drop into
`<Satisfactory>\Engine\Binaries\Win64\` → pipe connects, dump completes.

### More-genre dump coverage (calibration follow-up)

**Effort**: S (mostly user-side dumping; analyzer already does the
heavy lifting) | **Risk**: low | **Why**: The 15-game corpus is
heavy on JRPG / sim / ARPG / FPS / racing / sandbox. Missing genres:
MMO, fighting, horror, RTS, sports-sim. Each genre has its own
vocabulary (e.g. fighters: combo / cancel / parry / juggle; MMOs:
threat / aggro / dispel / cooldown_ms).

Workflow: dump 3-5 games per missing genre, re-run
`scripts/analysis/analyze_dumps.py work/dump/*.jsonl`, look at the new
cross-game tokens, PR additions to PropertyScoringTable /
KeywordScoringTable with the analysis output attached as evidence.

Process documented in [scripts/analysis/README.md](../scripts/analysis/README.md).

### Runtime `keywords.json` override (anti-bias UX)

**Effort**: M | **Risk**: med | **Why**: Discussed during build-679
anti-bias conversation. Users who disagree with the default scoring
tables currently have to fork + recompile. A runtime override file
(`keywords.json` alongside the exe) would let users add their own
genre-specific keywords without touching C# / build env.

Constraints:
- Must be AOT-compat — use source-generated JsonSerializerContext per
  CLAUDE.md rule
- Default tables stay hardcoded as fallback (so behaviour is sane
  even when the JSON is missing / malformed)
- Schema mirrors the C# tables 1:1 (StatsKeywords / CombatKeywords /
  …) plus an extension mode (additive vs replace)
- One-click "Export current tables to JSON" UI button to seed the
  customisation file

Not blocking — only do if a user actually asks for it.

### Class Family Browser (Proposal C) — still on the wishlist

**Effort**: L | **Risk**: med | **Why**: New tab "Class Family" with
a bucketed view of game classes by inferred role (Character / Pawn /
Inventory / Stats / Save / Components / DataAssets / DataTables /
GameMode). Real answer to "I have no idea where to start exploring a
new game". Needs its own planning round before starting — the
classification heuristic + UI design is the hard part, not the
implementation. **NOT a "jump in and code" task.**

Pre-work would benefit from the dump corpus: cluster 15 games' BPGCs
by property-name similarity to derive concrete "Inventory-like" /
"Character-like" / etc. archetype patterns.

### Proposal B: per-row "similar BP-added properties" suggestions

**Effort**: M | **Risk**: low

When a user lands on `bCanBeDamaged @ AActor`, surface a side-panel
with fuzzy-matched game-specific bools that semantically overlap
(e.g. `bIsImmortal @ BP_PlayerCharacter_C`). Reuses the
`KeywordTokenizer` + `KeywordScoringTable` machinery to score
similarity. **Why**: closes the "engine field is at the wrong layer;
show me the BP-added bool that the game's TakeDamage override
actually checks" gap from the analysis.

**UX**: anchor-driven. User already has a property selected (the
engine field); we surface its likely game-specific counterpart in BP
subclasses.

### Proposal B': "Unusual Location" Property Detection — **new insight 2026-05-12**

**Effort**: S–M (small if folded into B's PR) | **Risk**: low

Complementary to B but a different entry point: **find game-state-
suggestive properties (HP/MP/Stamina/XP/Damage/Health/etc.) regardless
of whether an engine equivalent exists, AND flag the cases where
they're sitting in a class you wouldn't expect.**

**Motivation**: developers don't always follow Unreal conventions.
HP/MP fields routinely show up in non-standard containers — observed
patterns include `LocalPlayer`, `GameViewportClient`, `HUDClass`,
`GameInstance` subclasses, even random `UObject`-derived service
classes. From a cheat-development perspective these are the most
valuable hits because they're **not where you'd think to look first**.
Function-side already does this kind of class-location-aware ranking
(`Character / Pawn / PlayerController / PlayerState +3`,
`Anim / Niagara / Sound -2`, etc. in `KeywordScoringTable`); the
Property side needs the same treatment.

**UX**: broad sweep, no anchor needed. Could land as either:
- a new **"Interesting Properties"** tab (analogous to Interesting
  Funcs), OR
- a **scoring-aware mode toggle** in the existing PropertySearch tab

**Scoring sketch** — reuse `KeywordTokenizer` for property-name
matches, layer class-location bonuses/penalties on top:

| Class bucket                                      | Bonus | Interpretation                |
|---------------------------------------------------|------:|-------------------------------|
| Character / Pawn / PlayerState / Inventory        |   +3  | Expected location             |
| GameMode / GameInstance / SaveGame                |   +2  | Expected (game-level state)   |
| AbilitySystemComponent / Stats / Status           |   +2  | Expected (gameplay subsystem) |
| **LocalPlayer / GameViewportClient / HUD**        |  **+4** | **Unusual — high-value hit**  |
| Anim / Niagara / Sound / Audio / Particle / Mesh  |   −2  | Noise (visual/effect classes) |
| UI / Widget                                       |   −1  | Noise (UI display)            |

The Unusual category gets a **positive bonus** because a HP field in
`LocalPlayer` is more interesting than a HP field in `BP_Player_C`
(the latter is the "normal" place; the former is the cheat-finder's
gold). Display this as a **"⚠ Unusual Location"** badge on the row
so the user immediately sees why this hit is unconventional.

**Keyword starter list** (extend in C# scoring table):
- Stats: `HP`, `MP`, `SP`, `Health`, `Mana`, `Stamina`, `Energy`,
  `XP`, `Exp`, `Experience`, `Level`, `Lv`, `Lvl`
- Combat: `Damage`, `Defense`, `Armor`, `CritRate`, `CritDamage`,
  `Attack`, `MoveSpeed`, `JumpHeight`
- Resources: `Gold`, `Coin`, `Money`, `Currency`, `Gem`, `Diamond`

Apply `KeywordTokenizer` whole-token matching so short acronyms
(HP/MP/SP/XP/Lv) don't substring-collide with engine spam
(`Component`, `Levitate`, etc.) — same lesson from build 609.

### Pairing rationale (why B + B' together)

Both proposals lean on the same building blocks:
1. `KeywordTokenizer.cs` — whole-token matching, already proven
2. `KeywordScoringTable.cs` — already has Function-side scoring
   tables; extend with PropertyScoringTable using the same shape
3. `ScoredFunctionRow`-style row model for `ScoredPropertyRow`
4. Class-location bonus/penalty machinery — already mature for
   functions, factor out to a shared `ClassLocationScorer` helper

Doing them together = ~1.3× the work for both, vs ~1× + ~1× sequential.
Estimate **M total** if done in one PR.

### Open design questions (decide before starting)

1. **B as side-panel vs B' as new tab vs B' as PropertySearch mode** —
   pick one of: (a) side-panel for B + new "Interesting Properties"
   tab for B', (b) extend PropertySearch with a "Scored" sort/filter
   mode covering both. Option (b) is fewer moving parts but more
   crowded UI; option (a) keeps the discovery/exploration entry
   points separate.
2. **Anchor-driven B's fuzzy threshold** — too loose = noise, too
   tight = no hits. Need a calibration round on 3-4 games. The
   build-609 KeywordTokenizer threshold-5 lesson applies.
3. **PropertyScoringTable keyword list** — start with the table
   above, calibrate on real games (ES2's `bCanBeDamaged` / `Health`,
   Geri's `MaxJumpHeight` are good anchors).

### Files in scope (pre-implementation guess)

- `ui/UE5DumpUI/Services/PropertyScoringTable.cs` (new, mirror of
  `KeywordScoringTable.cs`)
- `ui/UE5DumpUI/Services/ClassLocationScorer.cs` (new, extracted from
  `KeywordScoringTable`'s class-bonus logic — refactor first so
  Function side benefits too)
- `ui/UE5DumpUI/Models/ScoredPropertyRow.cs` (new)
- `ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs` (extend with
  scoring + Unusual Location badge) OR new
  `InterestingPropertiesViewModel.cs` (depending on #1 above)
- `ui/UE5DumpUI/Views/PropertySearchPanel.axaml` (Scope column
  already exists for B's "+N inheritors"; add Unusual Location badge)
- `dll/src/Aura.cpp` — possibly extend `EnumerateAllFunctions`-style
  scan for properties if PropertySearch's current pagination can't
  serve the new flow

### Out of scope for this round

- Full Class Family Browser (Proposal C) — separate planning
- Anchor-driven function fuzzy-match (Function v2 equivalent of B) —
  Function v1 closed; defer until concrete user request

### Proposal C: Class Family Browser

New tab "Class Family" — bucketed view of the game's classes by
inferred role (Character / Pawn / Inventory / Stats / Save /
Components / DataAssets / DataTables / GameMode). The "where do
character / item data live?" entry point. **Effort**: L | **Risk**:
med (needs careful family-classification rules per UE version) |
**Why**: real answer to "I have no idea where to start exploring a
new game" -- bigger work, separate planning round before starting.

-----

## Carryover capability gaps

Existing gaps from before the plan above. Pick up when the active plan
finishes or when blocked.

### MulticastSparseDelegateProperty UE 4.23-4.27

**Effort**: L | **Risk**: med | **Why**: Closes the only remaining
delegate-flavour gap. UE5 path landed in PR #194 (build 561-577); UE4
needs a separate AOB + walker branch.

- Outer key is `FObjectKey { FWeakObjectPtr Object; int32
  ObjectSerialNumber; }` (12B + 4 pad = 16B) instead of raw
  `UObjectBase*`
- Outer stride changes ~0x60 → ~0x68
- Key match logic must reconstruct `FObjectKey` from
  `(owner, Aura::GetSerialNumber(InternalIndex))`
- Need separate AOB — UE4 binaries don't share the UE5 `lea` sequence

Walker currently returns `supported=false` for UE < 5.0 to make this
gap explicit.

### Find Refs v4 — TMap / TSet weak-like inner sides

**Effort**: M | **Risk**: low | **Why**: Currently Object/Class only;
weak/soft pointer collections (`TMap<UObject*, FWeakObjectPtr>` etc.)
silently miss target hits.

Reuse the v3 weak-resolve helper inside the existing TMap / TSet
walkers in `Aura::FindReferencesToUObject`.

### FieldPathProperty drill-down + Find Refs

**Effort**: M | **Risk**: low | **Why**: Last remaining no-handler
property type. Rare in shipping games — only seen in Editor-derived
classes — so genuinely low priority.

### GWorld coverage

**Effort**: S each | **Risk**: low | **Why**: Two remaining
unverified / failing titles.

- **Star Wars Jedi: Survivor** (UE 4.27?): untested — needs an AOB
  sweep run + result triage
- **Satisfactory** (UE 5.3, modular DLL build): two related issues,
  both stemming from the same root — game splits CoreUObject into a
  separate DLL rather than baking it into the main exe.
  1. **Proxy DLL injection fails** (verified 2026-05-12, user feedback):
     dropping version.dll / dinput8.dll into the install folder doesn't
     attach. The game's loader or launcher bypasses normal proxy
     hooking. **Workaround**: CE DLL injection (manual). This was good
     enough for the 10-game dump-for-analysis run, but breaks the
     proxy-deploy UX path entirely on Satisfactory.
  2. **GWorld scan fails on the main exe**. Pattern likely lives in
     `CoreUObject-Win64-Shipping.dll`. Adapt `Genau::FindAll` to scan
     multiple modules when the primary scan fails.

  **Effort**: M (multi-module scan in Genau) + investigate why proxy
  DLL doesn't attach. Once attached via CE injection the rest works —
  dump produced 4868 BPGCs cleanly, the biggest game-class count of
  the analysis-corpus dataset.

### `kPublishers[]` table additions

**Effort**: S each | **Risk**: high (if added casually) | **Why**:
Wrong publisher bias overrides correct detection.

Only add a publisher when we have ≥3 misdetected titles from that
publisher AND a clear pattern (e.g. "all UE4-fork builds shipping under
this LegalCopyright string"). Wait for real misdetection reports.

-----

## Speculative — pick if active plan finishes ahead of schedule

Items from the brainstorm that aren't yet committed to:

- **Invoke history / favorites panel** — auto-record (target, args,
  result) per UI invocation; one-click re-fire from history
- **Dry-run-first invoke** — for never-called functions, invoke with
  zero/sentinel params first to detect crash before letting user
  commit to real args
- **CE table builder** — bundle selected pointer entries + AA scripts
  into a single `.ct` file, auto-grouped by category
- **Hotkey binding** — global hotkey assignment for shortlisted
  functions ("give 1000 gold" on Ctrl+G)
- **Property freeze — Route A (manual CE workflow, deferred)**:
  re-use the existing **CE XML / CSX export** to land a pointer chain
  in CE, then the user manually ticks Freeze in CE's address list.
  Works today, no code needed — just docs. Tradeoff: pointer chain
  is bound to the single resolved instance at export time, so it
  breaks on respawn / level transition. Active alternative: **Route B**
  (AA Script + class+name dynamic resolution + timer) — see the new
  Property-freeze active plan when it lands. Keep Route A on the list
  so users who only need a one-shot freeze (e.g. a static singleton
  manager) don't have to wait for the dynamic version.

-----

## Done (recent — moved to [dev-log.md](dev-log.md))

Recent items that shipped, kept here briefly until the next refresh:

- ✅ **Walker false-positive sweep + Scharf alignment helper** (build
  582-583, PR #195)
- ✅ **Per-game GameThreadDispatch invoke timeout** (build 583-588, PR #195)
- ✅ **`FillPointerSnapshot` refactor** (build 588, PR #195)
- ✅ **UI strict address validation** (build 588, PR #195)
- ✅ **Drill Depth slider 0-6 with warning band** (build 588, PR #195)
- ✅ **dev-log split into roadmap.md + todo.md** (this document, post
  build 589)
- ✅ **Copy AA Script (Baked) UFunction export** (helper file +
  generator + dialog + Tools menu, build 590-596)
- ✅ **Interesting Functions Finder** (list_all_functions pipe +
  KeywordScoringTable + new tab + cross-tab nav, build 597-607)
- ✅ **AOBMaker availability gating + Notes column + pipe-broken
  guard** (build 608)
- ✅ **CamelCase keyword tokeniser** (KeywordTokenizer + tokens
  replace substring matching, restored short acronyms, build 609)
- ✅ **Invoke dialog overflow fix** (window MaxHeight + ScrollViewer
  MinHeight, build 609)
- ❌ **UFunction metadata exposure (steps 3+4) skipped** — research
  confirmed metadata is stripped from cooked Shipping binaries; would
  be ~zero value for real cheat-table targets. Pivoted to tokeniser
  instead.
- ✅ **PropertySearch dedupe-by-defining-class (Property Origin
  Resolver A)** — `bCanBeDamaged` now one row "+4822 inheritors"
  instead of 4823 indistinguishable rows (build 610)
- ✅ **One-click Inject Helper into Current CE Table** (AOBMaker
  plugin's new InjectTableFile pipe cmd + UE5DumpUI Tools menu;
  cherry-picked from spawned session, build 611)
- ✅ **Multi-select Copy CE Field(s)** — LiveWalker DataGrid Extended
  mode; container-view multi-select emits one filtered container
  with N elements (build 660)
- ✅ **System tab "UI build: 0" bug** — `Version.Revision` not `.Build`
  (build 662)
- ✅ **Tab labels shortened** + **status text overflow fix** +
  **⚙ Options popover** (build 666)
- ✅ **Interesting Properties tab (B' round 1)** — Stats / Combat /
  Resources / Movement / Utility categories, Unusual Location flag
  for LocalPlayer / GameViewportClient / HUD / CheatManager
  (build 670)
- ✅ **DLL BPGC filter fix** (`IsClassLikeMeta` whitelist in
  SearchProperties / ListClasses / EnumerateAllFunctions) + **surgical
  Anim penalty** (AnimMan_Player_C no longer punished) + **Player +2 rule**
  (build 673)
- ✅ **Export → Dump All Metadata (.jsonl)** + Python analyzer pipeline
  (`scripts/analysis/analyze_dumps.py` + README anti-bias section)
  (build 676)
- ✅ **15-game data-driven keyword adds**:
  CombatKeywords +6 (Effect/Target/Radius/Ability/Modifier/Duration);
  ResourcesKeywords +2 (Item/Items); PropertyRules +3 (Weapon/Projectile/Battle)
  — all backed by cross-game evidence (build 678)
- ✅ **`search_properties_batch`** — DLL walks GObjects ONCE for N
  queries; ~30× speedup on big games (build 685)
- ✅ **Phase 2 function-side analysis** confirms KeywordScoringTable
  is comprehensive; class-bonus side gets Enemy +2 (both Function +
  Property) and Weapon +2 (Function side mirror) (build 687)
- ✅ **AOBMaker "Notes" column removed** — replaced with single
  inline status-row indicator (build 689)
