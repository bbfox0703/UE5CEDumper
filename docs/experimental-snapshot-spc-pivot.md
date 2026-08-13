# Experimental: Snapshot / SPC Query / Class Pivot — Design

> **Status: SHIPPED — C1 / C3 / C4 / C5 / C6 (builds 873–1727); C2 and the heavier C3 scorer
> are still open.** The per-phase status lines further down are accurate and are cited by
> section from the source — trust those over this summary. This header read "PLAN ONLY (no code
> yet)" until 2026-08-05. Ported in concept from the Unity sister
> project `discrete` (Phases 29b–29i). This document is the design of record;
> `docs/todo.md` carries the phased work block.

This brings three analysis features from `discrete` into UE5CEDumper, gated
behind an opt-in "experimental" flag so the default UI stays unchanged:

- **Snapshot** — point-in-time capture of all numeric UPROPERTY values across
  GObjects, persisted for diff / SPC / pivot.
- **SPC Query** — find fields whose value *sequence* across N snapshots matches
  a predicate chain. **Multi-session, type-agnostic, value-agnostic.**
- **Class Pivot** — group a class's instances by a business-key field's value
  and project value fields across snapshots. Value-keyed, cross-session safe.

-----

## 0. Why this works *better* in UE than in Unity

`discrete`'s SPC/Pivot fight Unity's runtime model: managed instances are
**anonymous** — the only handle is a managed pointer that drifts on every GC and
every relaunch. So cross-session SPC "collapses to statics only" (a documented
`discrete` limitation), and Pivot is *forced* to make the user guess a business
key field because there is no intrinsic identity to fall back on.

UE inverts all of this:

| UE property | Consequence |
|---|---|
| **GObjects index is stable while an object lives** | In-session join is rock-solid — no `discrete` "instance #124 → #123" walk-order drift. |
| **UObjects have intrinsic identity** (FName + outer chain → `full_path`) | Cross-session join has a real handle. Pivot can default to *identity* — often **no key field needed at all**. |
| **UPROPERTYs are reflected, named, typed** (`Health`, `ItemID`, `RowName`) | `NamePrior` for key discovery actually works, vs gambling on Unity's mangled `_propertyID`. |
| **The repo already ships `PropertyScoringTable` / `ClassLocationScorer`** | A property-importance scorer calibrated across 20 games — reuse it directly as the key-discovery NamePrior. |
| **Structured property walk knows each field's declared type** | Type-agnostic UX *with* type-correct comparison → no CE-style byte-reinterpret false hits (same advantage the build-794 `NumericAll` already exploits). |
| **Struct array elements have named/typed inner fields; object array elements point to real UObjects** | Inventory/cargo arrays can be tracked by **inner key**, not array index — solving the array case `discrete` deferred (its Layer B). |

**Net:** the single hardest `discrete` pain — "the user doesn't know the key
field" — is largely *dissolved* in UE, because the object model already carries
identity and the properties are already named and scored.

-----

## 1. The motivating use cases

### 1a. Money tracking (SPC, value-known)
Gold reads `10160 → 9910 → 9410 → 9410` in-game. SPC with predicate chain
`[Exact? or Any, Decreased, Decreased, Unchanged]` uniquely isolates the gold
property → one-click CE export. (Within a session: GObjects-index join is exact.)

### 1b. Energy bar (SPC, value-*unknown*, type-*unknown*, multi-session) — the driver
An energy/stamina bar has **no on-screen number**. You only know qualitatively
that it drains and refills. Capture across **multiple game sessions**:

```
Session1: Full   Session2: Full   Session3: drained   Session4: refilled
predicate:  Any      Unchanged        Decreased           Increased
```

SPC finds the field whose value moved `same, same, down, up` — **without the user
ever entering a type or a value**. This is the CE "Unknown Initial Value +
Increased/Decreased" workflow, generalized to N persisted snapshots **across game
restarts**.

This case is exactly where `discrete` *fails* (anonymous Unity instances cannot
be re-joined cross-session when both value and address are unknown/unstable) and
where UE *wins* (the energy field lives on a persistent singleton-ish object —
Pawn / PlayerState / a stamina component / a GAS AttributeSet — whose structural
identity is stable across restarts).

### 1c. Spaceship cargo (arrays, inner-key)
A cargo hold is `TArray<FCargoSlot{ FName ItemID; int32 Quantity; }>` (a
max-length object/struct array). Track `Cargo{ItemID=Fuel}.Quantity: 100 → 80 →
60` **by inner key (ItemID), not array index**, so it survives slot reordering and
session restarts.

-----

## 2. Concept mapping: discrete (Unity) → UE5CEDumper

| discrete concept | UE equivalent | Status in repo |
|---|---|---|
| Heap walk (GC roots / IL2CPP metadata) | GObjects array walk (`Aura`) | ✅ exists |
| Managed instance (anonymous, address-only) | UObject (index + FName + class + outer + path) | ✅ richer |
| C# field (`_propertyID`) | UPROPERTY (FProperty/UProperty — named + typed) | ✅ |
| Field value decode | `Ubel::WalkInstance` (typed value + array elements) | ✅ exists |
| Array element scan / nested-in-struct TArray | `Aura::ScanForValue` / `FindInContainers` container cache | ✅ exists (reuse) |
| FieldIndex key `{asm}\|{fqn}\|{addr}\|{off}\|{name}` | identity key (see §5) | new key shape |
| Static fields (Mono/IL2CPP statics) | CDOs + global singletons (GameInstance/GameMode/GameState) | UE analog |
| SnapshotEnvelope → SQLite | streaming capture → **SQLite** (raw ADO.NET) | new |
| Diff / SPC / Pivot / KeyDiscovery engines (pure C#) | **portable**, swap key type + scorer | port |

> Most of the *algorithm* layer is pure C# and portable. The genuinely new work
> is the **DLL streaming capture** (Phase A1, mostly reusing `Aura`'s container
> walk) and the **UE-ification of the key-discovery scorer** (Phase C3).

-----

## Phase 0 — Experimental gating (prerequisite, ships first)

**Goal:** the `bbfox` author-credit text in the System tab (the `Pointers` tab /
`PointerPanel` Diagnostics area) becomes a checkbox. Checked → the three new tabs
appear. Its tooltip reads **"Enable advanced experimental features"**. The toggle
is **persisted**.

**Facts (confirmed):**
- `bbfox` text lives in `Views/CreditFooter.axaml` (`str.Credit.AuthorPrefix`).
  *(Since build 2808 that row is the AOBMaker download link, and the key is
  `str.Credit.AobMakerPrefix` — the checkbox sits beside it either way.)*
- `CreditFooter` is used twice: `PointerPanel.axaml:490` (System tab bottom) and
  `LiveWalkerPanel.axaml:512` (empty state). **Only the System-tab instance
  becomes a checkbox**; LiveWalker stays plain text.
- Tabs are in `MainWindow.axaml:239-275`, DataContext = `MainWindowViewModel`.
- `MainWindowViewModel` constructs all child VMs (`Pointers =
  new PointerPanelViewModel(...)` at `MainWindowViewModel.cs:166`) — clean
  injection point for a shared flag.
- No general settings file exists yet, but `AobUsageService` already persists a
  small JSON under `%LOCALAPPDATA%` — model the gate on it.

**Design:**
- `MainWindowViewModel.ExperimentalEnabled` (`[ObservableProperty] bool`). The
  three new tabs bind `IsVisible="{Binding ExperimentalEnabled}"` (same
  DataContext — direct).
- Replace the System-tab `<views:CreditFooter/>` with a checkbox styled like the
  credit line (or add an optional `IsToggleMode` styled property to
  `CreditFooter`). `IsChecked` routes to the shared flag (inject a shared gate
  reference into `PointerPanelViewModel`; avoid `$parent[Window]` ancestor binds).
  `ToolTip.Tip` = new string `str.Experimental.EnableHint`.
- **Persistence:** new tiny `ExperimentalGate` service →
  `%LOCALAPPDATA%\UE5CEDumper\experimental.json`, AOT via `[JsonSerializable]`
  source-gen (mirror `AobUsageService`).
- New en.axaml strings (English only, per repo rule):
  `str.Experimental.EnableHint`, `str.Tab.Snapshot/SpcQuery/ClassPivot`.
- *(Optional)* one-time confirmation dialog on first enable.

**Effort:** S · **Risk:** low · No DLL change.

> **Update (build 824):** the opt-in checkbox renders at **Opacity 0.25**, and
> once it is checked **and** the user opens any experimental tab the opt-in is
> **locked** for that session (can no longer be unticked). The lock is
> session-only — NOT persisted — so a restart clears it. The gate gained
> `IsLocked` + `Lock()`; `MainTabs_SelectionChanged` locks on first
> experimental-tab open; the checkbox `IsEnabled` binds to
> `PointerPanelViewModel.CanToggleExperimental`.

-----

## Phase A — Snapshot (multi-session persistent foundation)

A type-agnostic full capture: for each (scoped) UObject, emit **every numeric
UPROPERTY** value (`NumericAll` spirit — word/dword/qword/float/double), plus the
identity columns needed for cross-session join, plus array elements (§4).

### A1 — DLL streaming capture
- New pipe commands `begin_snapshot` / `snapshot_chunk` / `end_snapshot` (mirror
  the Value Search session 3-step + the `walk_class_batch` chunk-of-200 pattern).
- **Never hold the whole JSON in the game process** (`discrete`'s OOM lesson on
  活俠傳). The server holds only `{scope filter, GObjects cursor}`; each chunk
  streams ~25–200 objects; the UI writes each chunk straight to SQLite.
- **Reuse, not rewrite:** `Ubel::WalkInstance` for scalar fields; `Aura`'s
  `ContainerCacheEntry` walk for arrays (it already reaches
  `TArray<struct>.inner` and `TArray<UObject*>` elements — `Aura.cpp:1290-1438,
  1719-2099`). Snapshot **emits** values where scan **predicate-matched**.
- **Scope (default `all-numeric`):** every numeric UPROPERTY. Filters available:
  game-only, specific class list, actors-only (via GWorld).
- **Per-field emit:** identity columns (§5) + `declared_type` + `numeric_value`
  (per declared width) + `hex` (raw bytes for exact Changed/Unchanged).
- **Capacity guards (carry `discrete`'s lessons):** per-class instance cap,
  array-element cap (default 256), hard object-count guard (mirror Value
  Search's 10M guard), `ReadTArray`'s existing 1M sanity cap.
- **Inner-key exception to all-numeric:** for array elements, also emit
  key-eligible *non-numeric* inner fields (FName / int / enum / SoftObjectPath)
  so the engines can inner-join (e.g. `FCargoSlot.ItemID`). See §4.

### A2 — C# model + SQLite persistence + capture UI
- Models: `SnapshotEnvelope / ClassEntry / InstanceEntry / FieldEntry`
  (portable from `discrete`; add `GObjectsIndex`, `NormPath`, array columns).
- **SQLite via raw ADO.NET** (`Microsoft.Data.Sqlite` +
  `SQLitePCLRaw.bundle_e_sqlite3`). **No EF Core** (reflection-based query breaks
  trim/AOT — violates the repo's AOT rule). Schema in §6. **Stream chunk writes**
  so neither the game nor the UI holds the full capture.
  - ✅ **Publish check DONE (build 811):** Native AOT publish
    (`build.ps1 -Mode Publish`) is clean and bundles `e_sqlite3.dll`.
- **Per-game DB file** (build 813): `snapshots.<pe_hash>.db`, NOT one shared
  file. `pe_hash` is stable across launches of the same build, so all sessions
  of a game share one file; a patch (new pe_hash) gets a fresh file. This keeps
  games isolated — no cross-game mixing in the list or in SPC/Pivot joins,
  isolated growth, isolated corruption blast radius. The store's active game is
  set on connect (`SetActiveGame`); pe_hash is sanitised to ASCII alphanumerics
  (no path traversal).
- **Folder + retention** (build 2726): the DB set lives in
  `%LOCALAPPDATA%\UE5CEDumper\Snapshots\`, not at the app-data root, and a game
  whose set has gone unused for `Constants.DataMaxAgeDays` (21) days is DELETED
  at startup — file-level, to reclaim what a dead game's multi-GB capture is
  holding. Both are `Services/AppDataFolderMaintenance`, called from the
  `SnapshotStore` constructor so nothing can open a connection before the folder
  has been migrated. Three properties worth knowing before touching it:
  - **A game's files move and expire as a GROUP** (`.db`, `-wal`, `-shm`,
    `.denylist.json`). A `.db` migrated without its `-wal` has silently dropped
    every transaction that WAL held, so a blocked move rolls the whole group
    back and leaves it at the old location.
  - **"Unused" is the WRITE time, which `SetActiveGame` stamps — never
    last-access.** NTFS last-access updates are on by default on Windows
    (`fsutil behavior query DisableLastAccess` = 2), so AV / backup / indexer
    reads keep every file looking like today; honouring them makes the sweep a
    permanent no-op. Connecting to a game resets its window even in a session
    that never opens this tab.
  - **Bookmarks deliberately do NOT expire.** `Bookmarks\` uses the same folder
    scheme with the sweep disabled (`maxAgeDays: 0`) — a few KB of hand-placed
    navigation is not what the disk-reclaim argument is about.
- Session identity: store `pe_hash` + `ModuleBase` (ASLR, per-launch) →
  `game_session_id` distinguishes restarts WITHIN a game's file.
- New gated "Snapshot" tab: capture controls (scope / caps), snapshot list,
  status row (reuse the existing status-row architecture).

### A3 — Diff engine + grid
- Pure C# (port from `discrete`). **Join key:** in-session uses
  `(class_fqn, gobjects_index, prop_name)` validated by a `(index, class, name)`
  triple (guards GObjects slot reuse after GC); cross-session uses the §5 path
  key. Diff is the in-session view (compare two captures of one playthrough).
- Grid: Added / Removed / Changed + direction (↑/↓) / range / field-name filters.
- **UE bonus:** a diff row hands off to the existing **CE XML export**
  (`get_ce_pointer_info` from `obj_addr` + `prop_offset`) → instant CE freeze
  entry. `discrete` can only view; UE can act.

**Effort:** A1 = M · A2 = M · A3 = S · **Risk:** capture memory/perf on big
games (mitigated by caps + the repo's already-parallel GObjects walk; native dep
bundling is the one publish-time risk).

-----

## Phase B — SPC Query (multi-session main use case)

Pure C# over the SQLite corpus — **zero DLL change**. Multi-session is
first-class, not a degraded mode.

### B1 — Engine
- Predicate kinds (port `discrete` `SpcPredicateKind`): absolute `Any / Exact /
  Range / NotEqual`; relative `Unchanged / Increased / Decreased / DeltaExact /
  DeltaRange`. Relative predicates compare snapshot *i* vs *i-1*.
- Candidate set = fields present in **all** selected snapshots under the chosen
  join mode (intersection); evaluate the predicate chain over each candidate's
  value sequence.
- **Type-agnostic UX, type-correct engine:** the user picks only directions
  (`Any / Unchanged / Decreased / Increased`) — no type, no value. Increased/
  Decreased compare `numeric_value` (per the field's *declared* width →
  no byte-reinterpret false hits); Changed/Unchanged compare `hex`.
- **Join modes (Strict + Loose — both shipped):**
  - **Strict:** `(class_fqn, norm_path, prop_name, prop_offset)` must match
    exactly. Lowest false-positive; misses runtime-spawn objects cross-session.
  - **Loose:** relax to `(class_fqn, outer_chain, prop_name)` — yields multiple
    candidates per logical field; the predicate chain filters them down.
  - In-session: `(class_fqn, gobjects_index, prop_name)` (exact).
- SQLite-friendly: candidate intersection + sequence gather expressed as indexed
  `JOIN`s on the `ix_strict` / `ix_loose` / `ix_insession` indexes.

### B2 — UI
- New gated "SPC Query" tab: a multi-select snapshot picker (**may span
  sessions**), one predicate row per selected snapshot (relative predicates are
  the headline), a Strict/Loose toggle, a results grid with one value column per
  snapshot. Hit → **CE export** (from `obj_addr` + `prop_offset`).

> **SPC ≡ `NumericAll` generalized:** persisted × N-snapshot × cross-session ×
> user-defined direction chain. Value Search is in-memory / 2-round / 5-min
> expiry; SPC persists it, makes it N-round, and joins across sessions by
> structural identity.

**Effort:** B1 = S-M · B2 = S · **Risk:** low (pure C#, heavily unit-testable per
the repo's test culture).

-----

## Phase C — Class Pivot (value-keyed, cross-session safe)

Group a class's instances by a key field's value, project value fields across
snapshots. Pure C# over SQLite — **zero DLL change** (except optional C4).

> **Update (build 1742): C3 discovery made USABLE — bounded N-snapshot + shape ranking.**
> The build-1160 discovery loaded the whole older snapshot into a RAM dictionary
> (`LoadIntersectedCandidatesAsync`); with no class filter (its whole point) that OOM'd
> the UI (~11 GB, hang) on big games. Rebuilt for the Strict path as
> `DiscoverChangesSqlAsync`: a single bounded SQL statement pivots each identity's
> per-snapshot values into columns server-side (`ROW_NUMBER` dedup keeps one physical
> sibling per (snapshot, identity)) and returns **only the changed instances** — memory
> is bounded by the changed-group count, not the instance count (verified 229 MB on a
> 2.85 GB / 10.4M-row DB; `cache_size=-65536`, **not** `temp_store=MEMORY`; cancellable
> via `sqlite3_interrupt`). The Before/After pair became a **2–4 snapshot picker**, and
> `PivotDiscoveryEngine` gained a **shape** sub-score: with ≥3 snapshots a one-time change
> (changed in a single interval) is boosted, a monotonic trend is neutral, per-frame jitter
> is demoted — the energy-bar §1b "unchanged, unchanged, changed" case. `class_counts`
> supplies the Total. Also: result-row **Locate in GWorld/GameEngine** and a **resizable +
> filterable** results grid. UI/C#-only, no DLL/schema/wire change.
>
> **Class picker (build 1764, in-game VERIFIED):** the build-1742 `AutoCompleteBox` froze the UI
> — its two-way `SelectedItem` binding oscillated `SelectedClass` (item↔null) and re-fired the
> field load ~135×/sec = a runaway connection-open loop (ProcMon: `.db-shm` offset-123 churn, all
> `Result=SUCCESS` granted). Replaced with a **filter `TextBox` + `ListBox`** (a ComboBox dropdown
> drops clicks on a per-keystroke rebuild; a ListBox has no Popup so clicks commit). `ApplyClassFilter`
> skips the rebuild when the filtered set is unchanged and re-selects a surviving pick, so a spurious
> post-click re-filter can't wipe the selection. Typing the filter never touches the DB; a deliberate
> pick loads that class's fields once (cached). + `busy_timeout=5000`, an always-on post-capture
> `wal_checkpoint(TRUNCATE)`, cache-before-supersession, and a collapsible Source `Expander`.
>
> **Status (build 1160): C1 + C3-lite + **C3 change-driven discovery** + C4 + C5 + C6 SHIPPED.**
> **C3 change-driven discovery (build 1160)** is the automatic *front-door* that
> dissolves the remaining "which class do I even pick?" pain (the reason Pivot was
> under-used when the target is unknown): a **"🔍 Suggest targets"** Expander on the
> Class Pivot tab takes a *Before* + *After* snapshot pair, finds the (class, prop)
> targets whose value MOVED, and ranks them by **interest × change × selectivity ×
> population** (sub-scores exposed for calibration). **Use →** pivots the chosen
> candidate (forces it as a projected value + Identity grouping + runs). Pure C# —
> `PivotDiscoveryEngine` (rolls (instance,field) sequences up per (class,prop), gates
> on "moved", ranks) + `SnapshotStore.DiscoverChangesAsync`, which **reuses the SPC
> cross-snapshot intersection load** via the shared `LoadIntersectedCandidatesAsync`
> helper (no DLL/pipe change). +10 engine / +7 store / +3 VM tests. ⚠ in-game
> live-verify pending. The original C3-lite + the rest:
>
> `PivotEngine`
> (identity/field grouping + `⟨N: …⟩` collision render) + `PivotKeyScorer`
> (type/name/cardinality key prior, `SuggestKey`, value interest via
> `PropertyScoringTable`) + `SnapshotStore.PivotAsync`/`ListPivotClasses`/
> `ListPivotFields` + `ClassPivotViewModel`/`ClassPivotPanel`. A `Source` toggle
> offers three modes: **Snapshot** (scalar), **Snapshot Array** (C6, build 877 —
> inner-key pivot of a captured struct array; `PivotArrayAsync` maps each (owner,
> element) to a synthetic instance keyed by inner_key_value → reuses
> `PivotEngine.Build` in Identity mode), and **DataTable** (C4, build 873 —
> zero-config, RowName is the key, `DataTablePivotEngine` over `walk_datatable_rows`).
> **C5 (build 877):** right-click "Pivot this property…" handoff from PropertySearch
> / InterestingProperties / LiveWalker (`NavigateToPivot` → `PivotForAsync`), gated
> by a per-VM `PivotEnabled` flag so it's hidden when experimental is off.
> Remaining: C2 (find-by-value), and the heavier C3 scorer (Jaccard stability /
> compound key). The "volatility ranking" half of C3 shipped as **change-driven
> discovery (build 1160)** — see the status block above.
>
> **Composite multi-field key (build 1727):** Field mode can group by a **TUPLE** of
> key fields (`Team · Slot`), not just one — the user's "multi-value Pivot" request,
> resolved on the **aggregation axis** (NOT the `Orden` SDR group-scan seam, which
> stays served by SPC Group / Snapshot Group). `PivotQuery.KeyFields` +
> `EffectiveKeyFields` (falls back to `[KeyField]` → single-key path byte-identical);
> `PivotEngine.RenderCompositeKey` joins rendered segments with `" · "` (1 element =
> verbatim); a new **Key** checkbox column (`PivotFieldPick.IsKey`) ticks the extra
> key fields alongside the primary key ComboBox. Inert in Identity / DataTable /
> Snapshot Array. UI/C#-only, no DLL/wire change; +4 tests; AOT green. ⚠ in-game
> live-verify pending.

### C — How UE dissolves the key-field problem (the `discrete` pain, your Q#4)
`discrete`'s root cause: anonymous Unity instances *force* a guessed business
key. UE's six-layer improvement:

1. **Identity-first mode (biggest win):** offer "pivot by object identity" before
   asking for any key — in-session GObjects index, cross-session FName/path.
   Many cases need **no key field** because the UObject is already identified.
   This is the part `discrete` literally cannot do.
2. **Reuse the calibrated scorer:** `PropertyScoringTable` *is* the NamePrior,
   tuned on 20 games — strictly better than `discrete`'s 6-substring table.
3. **UE-idiom type prior:** `NameProperty` (FName) ranks highest (UE's canonical
   key type), then enum, then int — vs `discrete`'s `int=1.0 / string=0.85`.
   Boost props named `RowName / *ID / *Tag / GameplayTag`.
4. **DataTable as a first-class source (C4):** `walk_datatable_rows` already
   returns key→row. A DataTable is a literal key→struct map → **zero-config
   pivot** (RowName *is* the key; no discovery needed).
5. **Value-locator closes the loop (C2):** player sees "Gold = 9410" → Find by
   value → `(class, property)` → pivot. UE can additionally hand the hit to the
   live Value Search engine or to CE export.
6. **Right-click handoff (C5):** "Pivot this class/property" from LiveWalker /
   PropertySearch / InterestingProperties — UE has more navigation entry points
   than `discrete`.

### Sub-phases
- **C1 — PivotEngine core:** group by key tuple **or** by intrinsic identity;
  collision rendering `⟨N: v1,v2,…,+M⟩` (port `discrete` 29e-2 polish). Includes
  the identity mode (no key field).
- **C2 — Find-by-value (value-locator):**
  - *Value Search → Pivot handoff ✅ **SHIPPED (build 1161)**:* the live Value Search
    panel (which already resolves a value to `(class, field, addr)`) gained the C5
    "📊 Pivot" handoff it was the only source panel missing — `ValueSearchViewModel.
    NavigateToPivot(ClassName, FieldName)` → `PivotForAsync`. This is the
    **value-known → pivot** half of "closing the loop" (build 1160's discovery is the
    value-unknown half), realised by reusing the live scan + C5 contract.
  - *Remaining:* port `SnapshotValueLocatorEngine` (29i-4) — set-membership over the
    persisted SQLite corpus (address-agnostic, cross-session, Exact + Delta) so a value
    can be located without a live scan.
- **C3 — Key discovery + change-driven discovery:**
  - *C3-lite (shipped):* reuse `PropertyScoringTable` for NamePrior + UE type prior
    in `PivotKeyScorer.SuggestKey` (auto-suggests the key field within a class).
  - *Change-driven discovery ✅ **SHIPPED (build 1160)**:* the "🔍 Suggest targets"
    front-door — "likely game-state" volatility ranking (29i-3) realised as
    `PivotDiscoveryEngine` over a Before/After snapshot pair (interest × change ×
    selectivity × population), reusing the SPC intersection load. This is the part
    that makes Pivot usable when the target class is unknown.
  - *Remaining:* port `discrete`'s stability (Jaccard) + greedy compound key + class
    shortlist (CV / presence / field ratios) for the heavier multi-snapshot scorer.
- **C4 — DataTable-native pivot:** ✅ **SHIPPED (build 873).** Zero-config (RowName
  is the key). No DLL touch needed — `walk_datatable_rows` already returns the row
  struct fields; `DataTablePivotEngine` + a `Source` toggle on the Class Pivot tab
  consume them into the existing results grid.
- **C5 — Right-click handoff** from existing panels. ✅ **SHIPPED (build 877).**
  "Pivot this property…" context-menu item on PropertySearch / InterestingProperties
  / LiveWalker → `NavigateToPivot(class, prop)` → `PivotForAsync` selects the class
  in the newest snapshot and ticks the property. Gated by a per-VM `PivotEnabled`.

> **C6 — Array-element pivot** ✅ **SHIPPED (build 877).** A "Snapshot Array" source
> mode pivots a captured struct array (§4) by inner-key value — reorder- and
> owner-immune. `SnapshotStore.PivotArrayAsync` fetches the array-element rows and
> maps each (owner GObjects index, element index) to a synthetic instance whose
> Identity key is the inner_key_value, so `PivotEngine.Build` (Identity mode) does
> the grouping with zero new engine code.

**Effort:** C1 = M · C2 = M · C3 = M-L · C4 = S · C5 = S · **Risk:** med (port
volume; algorithms pure C# + testable).

-----

## 4. Array handling (struct / object / primitive) — reorder-immune inner-key

`discrete` expands arrays by **index** (Layer A), so a sorted/reordered array
breaks the join (its documented "index-shuffle weakness"); reference-array
inner-pivot (Layer B) was deferred. **UE does the correct version in v1** because
struct elements have named/typed inner fields and object elements point to real
UObjects.

**Principle: track array contents by *inner key value*, never by array index** —
the same "use intrinsic identity, not position" idea as the top-level §C win.

| Array shape | Example | Tracking (reorder-immune) |
|---|---|---|
| **Struct array** | `TArray<FCargoSlot{ItemID, Quantity}>` | inner key = `ItemID`; track `Cargo{ItemID=Fuel}.Quantity` |
| **Object array** | `TArray<UItem*>` | element points to a real UItem (captured independently top-level → its fields already tracked); the array gives membership; inner key = pointed-object identity |
| **Primitive/Name array** | `TArray<FName>` ItemIDs | element value itself = set-membership key (item added/removed) |

**DLL reality:** `Aura`'s container cache already walks these for value scan
(struct inner numeric fields via `intraOffset`, object-pointer elements, nested
arrays inside structs to depth 3). A1 reuses that walk and **emits** instead of
predicate-matching, plus emits the inner-key non-numeric field.

**Capacity:** array-element cap (default 256) + `ReadTArray`'s 1M sanity cap. A
"max-length" cargo array is bounded by design, but the cap is still mandatory.

-----

## 5. Identity keys (the crux of multi-session join)

| Scope | Join key | Notes |
|---|---|---|
| In-session | `(class_fqn, gobjects_index, prop_name)` | GObjects index is stable while the object lives; validate `(index, class, name)` triple to guard slot reuse after GC |
| Cross-session **strict** | `(class_fqn, norm_path, prop_name, prop_offset)` | `norm_path` = `full_path` with FName numeric suffix normalized (`BP_Player_C_0` → `BP_Player_C_#`) |
| Cross-session **loose** | `(class_fqn, outer_chain, prop_name)` | `outer_chain` = signature of the outer UClass chain; yields candidates, filtered by the predicate chain |

**Strongest cross-session anchors:** singleton-ish objects (GameInstance,
PlayerController, PlayerState, Pawn, `*Component`, GAS AttributeSet) and CDOs —
re-created in deterministic order on restart, so paths are stable. **Failure
mode (documented honestly):** randomly-spawned, dynamic-count objects may not
re-join cross-session → lower SPC hit rate for those. Player-state data (energy /
HP / money) lives on the stable anchors, so it joins well.

-----

## 6. SQLite schema (raw ADO.NET)

> **Schema v4 (build 884) — denormalised, in-memory queries (current).** The `fields`
> table keeps identity columns per row, but **Diff and SPC no longer query in SQL** —
> they load snapshots into in-memory dictionaries and hash-join in C# (the technique
> the `discrete` sister project uses; O(n), index-independent). Pivot was already
> in-memory (`PivotEngine`). So the three heavy composite covering indexes
> (`ix_strict`/`ix_loose`/`ix_insession`, ~450 MB of a ~1.2 GB capture) were **dropped**
> — a single lean `ix_fields(snapshot_id, class_fqn)` serves the `WHERE snapshot_id`
> scans (diff/SPC load) and the `(snapshot_id, class_fqn)` filters (pivot/list).
> Roughly **halves the DB** at zero capture cost. `PRAGMA user_version`-gated:
> an older DB is dropped + recreated on open (no in-place migration; recapture ~2 min).
>
> **Why in-memory:** a v2 attempt (build 881, reverted) normalised identity into an
> `objects` table to shrink the DB, but splitting the join key across two tables made
> the SQL self-join's covering indexes unusable → `Run Diff` >1 min on ~1.8M rows.
> Moving the joins in-memory removes the covering-index dependency entirely, which is
> what then made dropping the indexes (for size) safe. SPC additionally supports
> per-snapshot **absolute value predicates** (Exact / Between / ≥ / ≤,
> `SpcAbsolutePredicate`) applied before the result cap, so directional-but-irrelevant
> noise (UI widget sizes, etc.) doesn't crowd real values out of the 50k. Further size
> (not yet done): `discrete`-style gzip per-class blob storage.



> One DB file **per game**: `%LOCALAPPDATA%\UE5CEDumper\Snapshots\snapshots.<pe_hash>.db`.
> `game_session_id` (pe_hash + ModuleBase) distinguishes restarts within that
> file. Cross-game isolation is by file; cross-session join is by the columns
> below.

```sql
-- One row per capture round (may span game sessions).
CREATE TABLE snapshots(
  id INTEGER PRIMARY KEY,
  label TEXT,
  captured_at TEXT,           -- ISO timestamp passed in from UI (engine has no Date.now)
  pe_hash TEXT,               -- from get_pointers; identifies the game build
  game_session_id TEXT,       -- pe_hash + launch token; distinguishes restarts
  ue_version INTEGER,
  object_count INTEGER,
  scope TEXT                  -- "all-numeric" | "actors-only" | class-list hash | ...
);

-- One row per captured numeric field (top-level OR array element).
CREATE TABLE fields(
  snapshot_id INTEGER,
  -- cross-session identity
  class_fqn   TEXT,
  norm_path   TEXT,           -- FName-number-normalized full path
  outer_chain TEXT,           -- outer UClass chain signature (loose join)
  prop_name   TEXT,
  prop_offset INTEGER,
  declared_type TEXT,         -- IntProperty/FloatProperty/... (type-correct compare)
  -- in-session identity
  gobjects_index INTEGER,
  obj_addr    TEXT,           -- session-local; for CE export handoff
  -- value
  numeric_value REAL,         -- normalized to declared width (Increased/Decreased)
  hex         TEXT,           -- raw bytes (exact Changed/Unchanged)
  -- array element columns (NULL for top-level scalar fields)
  array_field     TEXT,       -- owning ArrayProperty name
  elem_index      INTEGER,
  inner_key_name  TEXT,       -- e.g. "ItemID"
  inner_key_value TEXT,       -- e.g. "Fuel" (FName/int/enum/path rendered)
  inner_prop_name TEXT,       -- e.g. "Quantity"
  PRIMARY KEY(snapshot_id, gobjects_index, prop_offset, elem_index)
);

CREATE INDEX ix_strict    ON fields(class_fqn, norm_path,      prop_name);
CREATE INDEX ix_loose     ON fields(class_fqn, outer_chain,    prop_name);
CREATE INDEX ix_insession ON fields(class_fqn, gobjects_index, prop_name);
CREATE INDEX ix_array     ON fields(class_fqn, norm_path, array_field, inner_key_value, inner_prop_name);
```

SPC/Pivot inner-join arrays on
`(class_fqn, norm_path, array_field, inner_key_value, inner_prop_name)` →
reorder-immune and cross-session safe.

-----

## 7. Pipe protocol additions (Phase A1)

Three new commands, session-based like Value Search; full wire-shape to be
specified during A1:

```jsonc
// Open a capture session. Returns sessionId + total object/field estimate.
{ "id": 60, "cmd": "begin_snapshot",
  "scope": "all-numeric", "game_only": true,
  "instance_cap": 0, "array_cap": 256 }

// Stream the next chunk of captured objects (advance by "cursor"/"scanned").
{ "id": 61, "cmd": "snapshot_chunk", "session_id": 1234, "cursor": 0, "limit": 100 }

// Close the session (idempotent).
{ "id": 62, "cmd": "end_snapshot", "session_id": 1234 }
```

Each chunk object carries identity + numeric fields + array elements (the §6
column set, JSON form). The UI writes each chunk straight to SQLite.

-----

## 8. Execution roadmap (effort / risk)

```
Phase 0  Gating checkbox (persisted)                              [S,  low,  no DLL]
Phase A  Snapshot
  A1  DLL streaming capture: all-numeric + identity + arrays      [M,  mem risk, DLL]
  A2  C# model + SQLite (raw ADO.NET) + capture UI                [M,  publish native dep, no DLL]
  A3  Diff engine (in-session index join) + grid + CE export      [S,  low,  no DLL]
Phase B  SPC Query  (multi-session)
  B1  Engine: Strict/Loose join + relative predicate chain        [S-M, low, no DLL]
  B2  UI: cross-session picker + direction predicates + CE export [S,  low,  no DLL]
Phase C  Class Pivot  (multi-session, value-keyed)
  C1  PivotEngine (identity mode + collision render)              [M,  med,  no DLL]
  C2  Find-by-value + handoff (closes the loop)                   [M,  med,  no DLL]
  C3  Key discovery (reuse PropertyScoringTable + UE priors)      [M-L, med, no DLL]
  C4  DataTable-native pivot (RowName is the key)                 [S,  low,  maybe small DLL]
  C5  Right-click handoff from existing panels                    [S,  low,  no DLL]
```

The genuinely new engineering concentrates in **A1** (DLL streaming capture +
identity/array emit, mostly reusing `Aura`) and **C3** (UE-ifying the scorer).
B and C are mostly pure C# + portable `discrete` code + indexed SQL.

-----

## 9. Risks & open questions

1. **GObjects slot reuse** after GC → join must validate `(index, class, name)`,
   never bare index.
2. **SQLite native dep** in the self-contained trimmed publish — verify
   `e_sqlite3` bundling post `build.ps1 -Mode Publish`. Raw ADO.NET only (no EF).
3. **Cross-session identity for spawned objects** — runtime FName suffixes drift;
   Strict misses them, Loose recovers some. Document the limitation in-UI.
4. **Big-game full-numeric capture** time/memory — mitigated by scope filters +
   caps + the existing parallel GObjects walk; default scope = game-only.
5. **FText in cooked builds** — display strings often stripped (known Value
   Search limitation); arrays/fields of FText are best-effort.
6. **No general settings infra** — Phase 0.3 introduces the first persisted UI
   setting file (model on `AobUsageService`).

-----

## 10. Provenance

Source project: `D:\Github\discrete` (Unity sister project, same arch: C#
Avalonia UI + C++ DLL + named pipe). Relevant `discrete` docs:
`docs/class-pivot.md` (the three-stage funnel + 29i usability roadmap),
snapshot/SPC phase notes. This repo's Value Search is itself "a port from
discrete Phase 27b" — the two share DNA, which is why the capture machinery
(`Aura` container walk, `Ubel::WalkInstance`, the session model) already exists.
