# Dev Log — archived (builds 715–937, 2026-05-20 → 2026-06-06)

Archived from `docs/dev-log.md` to keep the live log readable (it had grown past
3,800 lines). Newest-first, same format as the live log. For builds **≥939** see
[../dev-log.md](../dev-log.md). The pre-build-700 pointer is chained at the bottom.

-----

## 2026-06-06 — Value Search engine + app-wide DataGrid sorting + DLL cancellation (builds 926-937, PRs #237/#238)

Five shipments, all live-verified by the user. Tests **1254 C# + 412 dll + 31 utf8**.

### Value Search — lean Candidate (V3-A + V3-B), build 926, PR #237
Interning refactor of the value-scan result record. `ValueScan::Candidate` carried
six `std::string`s copied by value, almost all redundant: class / defining-class /
field name / type / mask / offset are functions of `(class, field)`, and the
instance name is shared per object. Interned the per-(class,field) metadata into a
session `FieldDescriptor` pool and the per-object metadata into an `InstanceRecord`
pool; the lean Candidate keeps only `addr` + value snapshot + `descriptorIdx` +
`instanceIdx` + `elementIndex` (~240 B → ~72 B, and **0 heap strings per numeric
candidate**). Sessions live in the injected DLL, so this is the precondition for any
later maxResults-cap increase. Worker threads intern into thread-local pools
(descriptorIdx cached on the `ScanField`); a custom ascending-tid merge offset-remaps
candidate indices (replaced `ConcatTruncate` for this caller). **Wire JSON shape
unchanged → C#/UI untouched.** Array element names rebuilt via
`ValueScan::FieldDisplayName(desc, elementIndex)`. **V3-B (instance-table dedupe) was
necessarily folded into V3-A** — a lean Candidate can't keep raw instance fields.

### Value Search — TSet / TMap key|value scan (V1a), build 927, PR #237
Closes the biggest "what can't Value Scan reach?" gap after non-UPROPERTY fields.
`ScanField`'s `bool isArray` → `enum ScanContainer { None, Array, Set, MapKey,
MapValue }` + `valueOffset`. `expandFields` emits Set/Map(key|value) ScanFields next
to the ArrayProperty branch (vector inner gated by `ContainerInnerAccepted`); a shared
`scanElement` lambda (factored out of the TArray loop) drives Array + a new sparse
branch that walks the FSet/FMap `TSparseArray` (allocated slots only via
`IsSparseIndexAllocated`, value at `slot + valueOffset`), reusing the Address Finder's
sparse geometry (`GetSetElementStride` / `GetMapPairLayout`). **Refine + Fern needed
ZERO changes** (operate on `c.addr` + descriptor pool; element addr incl. valueOffset
baked in at First-Scan). Rows render `Set[idx]` / `Map.Key[idx]` / `Map.Value[idx]`.
Element addresses are raw, so refine degrades on container reallocation exactly like
TArray. TOptional (V1c) still deferred. Live-verified: a `TMap<NameProperty,IntProperty>`
value (`PlayerData.AttributeAugmentLevels.Value[2]`=481) found via Int32 Exact scan.

### App-wide DataGrid sorting fix + Value Search keyword filter, builds 932-934, PR #237
Column sorting was dead in **every** DataGrid (text and template columns). **Root
cause: compiled bindings** (`AvaloniaUseCompiledBindingsByDefault=true`) — Avalonia
DataGrid does NOT auto-derive a column's sort path from a compiled binding, so without
an explicit `SortMemberPath` nothing sorts. **NOT an AOT/backend-removal regression**
(reproduced on the non-AOT build). Added `SortMemberPath` to every sortable column
across all panels (numeric backing for hex offset/size/score columns so order is
numeric), `CanUserSort="False"` on action columns. Exception kept: SPC `SnapshotPicks`
stays chronological. Plus a **Value Search keyword filter**: case-insensitive
substring across all columns, client-side over the cached set (`FilterText` →
`ApplyFilter` rebuilds the bound **typed** `ObservableCollection` — a non-generic
`DataGridCollectionView` breaks compiled column-binding type inference, AVLN2000).

### DLL-side cooperative cancellation for long operations, builds 936-937, PR #238
Long DLL ops now stop when the UI disconnects or the DLL shuts down, so the DLL no
longer spins after the UI closes and disabling the script / closing the game no longer
hangs while a scan finishes. The pipe is single-connection + synchronous (a scan blocks
the pipe thread and can't be told to stop on the same pipe mid-scan), so this is a
**cooperative-cancel layer**:
- New `Cancel.h`: `Cancel::Requested()` (relaxed atomic) = per-command disconnect flag
  | sticky shutdown flag.
- `Fern::Stop()`/`UE5_Shutdown` call `RequestShutdown()` **before** joining threads →
  in-flight scan bails, accept-thread join completes fast (**fixes "game won't close"**).
- Fern **monitor thread** `PeekNamedPipe`s the in-flight pipe every 200ms (only while
  `m_commandInFlight` — the handler is CPU-bound then, not touching the handle); a broken
  pipe → per-command cancel → orphaned scan bails, pipe frees for the reconnecting UI
  (**fixes the reconnect-within-window stall**).
- Coverage: a watcher in `ParallelGObjectsScan` flips the existing `deadlineHit` (covers
  value scan / find-refs / containers / xrefs / find-by-path with no per-body edits);
  serial loops poll `Cancel::Requested()` every 4096 iters (`ListClasses`,
  `EnumerateAllFunctions`, `SearchByName`, `FindInstancesByClass`, `SearchProperties[Batch]`,
  `WalkClassesBatch`, `CaptureSnapshotChunk`, `Aura::ForEach`, `list_enums`). **No hard
  timeouts** — Full SDK dump etc. must run to completion.
- UI: Value Search gains a per-scan `CancellationTokenSource` + Cancel button (shown
  while scanning). Cancel abandons the UI wait; the DLL self-terminates at its deadline.

Live-verified: value-scan cancel, close-UI-mid-scan stops the DLL, game closes promptly.

-----

## 2026-06-05 — Experimental UX batch 3: pivot index, capture ETA, Delete All, icon (build 923)

Third live-test feedback pass.

- **Persisted pivot class-index (fixes the ~10s+ Class Pivot first-open scan).**
  The picker ran `COUNT(DISTINCT gobjects_index) GROUP BY class_fqn` over ~1.7M
  rows on every snapshot selection. Now precomputed ONCE per snapshot into a small
  `class_counts` table (additive schema, no version bump — existing snapshots kept;
  `pivot_index_built` marker distinguishes "0 array classes" from "not built").
  Built eagerly at `FinalizeSnapshotAsync` (new captures open instantly) with a lazy
  fallback for old snapshots; the picker reads the tiny table. Persists across
  restarts. Cleaned up on delete + quota-eviction. +3 tests.
- **Capture progress: elapsed + ETA + %.** Status now shows
  `Capturing… X/Y (NN%) — objects/fields · 1m23s elapsed, ~45s left` (ETA from the
  elapsed/fraction projection, suppressed under 2%). Finalize shows "building pivot
  index…".
- **Delete All (truncate) button** on the Snapshot tab — `DeleteAllSnapshotsAsync`
  truncates every table for the active game + VACUUM, off the UI thread.
- **Taskbar/window icon was transparent on the AOT exe.** Avalonia's `.ico` decode
  under AOT/Skia is flaky; switched `Window.Icon` to a PNG (extracted from the
  existing `Mainicon.ico`). The exe file icon (`ApplicationIcon`, .ico) is unchanged
  and was already fine. *(The app.manifest already existed + is DPI-aware, so that
  wasn't the cause.)*

Tests **1254 C# + 393 dll + 31 utf8**, all green. AOT publish clean +
launch-verified (window opens, no crash). **LIVE-VERIFY PENDING (user):** Pivot
first-open is now fast; selecting any snapshot responsive; capture shows ETA; Delete
All works; **taskbar icon renders** (PNG fix).

-----

## 2026-06-05 — AOT: Windows-only Avalonia backend (drop X11/macOS/FreeDesktop) (build 918)

The Native-AOT publish emitted a wall of `ILC: ... will always throw because:
Failed to load type 'Tmds.DBus.Protocol.Connection'` warnings from
`Avalonia.X11` / `Avalonia.FreeDesktop` — code paths that can never run in a
Windows-only tool (UE5CEDumper injects into Windows games). Removed them at the
source instead of suppressing:

- **`Avalonia.Desktop` → `Avalonia.Win32` + `Avalonia.Skia`** (the Desktop
  meta-package bundled the X11 / macOS-Native / FreeDesktop backends). Dropped
  the now-orphaned `Tmds.DBus.Protocol` and the Linux/WebAssembly native assets
  (`{HarfBuzzSharp,SkiaSharp}.NativeAssets.{Linux,WebAssembly}`).
- **`Program.cs`: `UsePlatformDetect()` → `.UseWin32().UseSkia()`.**
  `UsePlatformDetect` itself lives in `Avalonia.Desktop`, so it had to go; the
  explicit Win32+Skia wiring is exactly what PlatformDetect resolved to on
  Windows anyway.
- TrimmerRootAssembly: dropped `Avalonia.Desktop`.

Result: AOT publish is now **warning-free** (was a dozen X11 ILC lines). Single-
file exe ~46.8 MB. **LIVE-VERIFY PENDING (user):** launch the published
`dist/UE5DumpUI.exe` and confirm the window opens + renders — backend init is a
runtime concern the build can't prove. (Reference: CrimsonAtomic keeps
Avalonia.Desktop + NoWarn; we went the leaner remove-the-backend route the user
asked for.)

-----

## 2026-06-05 — Experimental UX hardening batch 2 (build 916)

Second live-test feedback pass on the Snapshot / SPC / Pivot tabs.

- **Capture lock-down (real bug).** `OnIsCapturingChanged` only raised `CanCapture`,
  so `CanEditSettings` never re-evaluated → Scope/GameOnly/Quota/Label stayed editable
  during a capture. The capture loop reads `GameOnly` **per chunk**, so toggling it
  mid-capture would corrupt the snapshot. Now raises `CanEditSettings` + `CanRunDiff`,
  and `CanRunDiff` includes `!IsCapturing` (Run Diff disabled during capture).
- **Delete Selected hang.** `DeleteSnapshotAsync` (DELETE over ~1.7M rows,
  `ExecuteNonQueryAsync` runs synchronously) ran on the UI thread → freeze. Wrapped in
  `Task.Run`, added an `IsDeleting` busy flag (disables the Delete button + status),
  refreshes usage after.
- **Class Pivot slow + unresponsive (the 80% CPU / "can't select snapshot" report).**
  `LoadClassesAsync` (GROUP BY `COUNT(DISTINCT gobjects_index)` over ~1.7M rows) +
  `LoadFieldsAsync` were uncancellable, so rapidly changing the snapshot stacked
  several heavy scans on the thread pool. Added a shared `_loadCts` (cancel-prior-on-new)
  threaded into the store list methods (+ early `ThrowIfCancellationRequested`), a
  "Loading classes…" status, and `CancelPendingWork` now also cancels loads. Stacking
  eliminated. **Plus per-snapshot caching (the real fix for "re-scans on every
  dropdown"):** a snapshot is write-once / immutable, so its class + field lists are
  cached in-VM keyed by `(snapshotId, arrayMode)` / `(snapshotId, class)` — computed
  ONCE, instant on every re-select for the session, no dirty-flag needed. Cache entries
  for deleted snapshots are pruned in `RefreshAsync`; the denylist filter is applied on
  top of the cache so hiding a class never triggers a re-scan. +1 test (CountingStore
  proves the class list is scanned once across re-selects).
- **Pivot intro hint** for transient inventory: their object path is identical
  (`//Engine/Transient/Item`), so Identity mode merges them into one group (visible
  collision ⟨N: …⟩) — use a Field key (ItemID) instead. Same transient-path cause as
  the SPC issue, but Pivot degrades gracefully (single-snapshot, no value-pairing).
- **SPC "materials don't show up" (root-caused).** Transient inventory objects all
  normalise to `//Engine/Transient/Item`, so the **Strict** join (norm_path + offset)
  collapsed 4 distinct items into one candidate; with no `ORDER BY` on the row stream
  the cross-snapshot value pairing was arbitrary, so the directional predicate failed.
  **In-session** join (gobjects_index) tracks each object exactly. Fix: auto-select
  In-session when all ticked snapshots share a `GameSessionId`, Strict otherwise
  (cross-session) — overridable; a manual combo change sticks. Cross-session SPC still
  works via Strict/Loose (the user's other ask).
- **Single-click checkboxes everywhere.** `DataGridCheckBoxColumn` needs select-then-click
  (2 clicks). Converted all four (SPC Use + noise Pick, Snapshot noise Pick, Pivot
  Project) to `DataGridTemplateColumn` + centered `CheckBox` (TwoWay) → one click toggles.
- **Snapshot pickers show the timestamp.** A custom label hid *when* a snapshot was taken
  in the one-line diff/pivot ComboBoxes. New `SnapshotMeta.PickerDisplay` = "Label ·
  yyyy-MM-dd HH:mm:ss (local)" used there (the saved-snapshots grid keeps its separate
  Captured column).
- **Diff Old/New auto-swap.** If Old is picked newer than New, the diff swaps them by
  snapshot Id (= capture order) so Increased/Decreased stay correct, noting it in the
  status.
- **Capture layout compaction.** Scope / GameOnly / Quota / Label / Capture / Cancel now
  share one WrapPanel row (was two), denser now that capture is an Expander.

Tests: **1250 C# + 393 dll + 31 utf8 = 1674**, all green; AOT publish clean. **LIVE-VERIFY
PENDING (user):** (1) settings locked during capture; (2) Delete no longer hangs; (3) Pivot
snapshot/class selection responsive (no CPU peg); (4) SPC same-session auto-picks In-session
and the materials now appear; (5) one-click checkboxes; (6) diff/pivot combos show timestamps;
(7) reversed Old/New auto-swaps.

-----

## 2026-06-05 — N1 follow-ups: per-tab denylists, cancellation, grayout, collapsible layout (build 910)

Live-test feedback pass on the N1 noise picker. Six changes:

**Per-tab denylist isolation (was one shared list).** The user wanted each
experimental tab to keep its OWN exclude list — hiding a class in SPC must not
affect Snapshot Diff or Class Pivot. `ClassDenylistSettings` now holds three
independent lists (`Diff` / `Spc` / `Pivot`) in one per-game JSON file; the store
API is `GetClassDenylist(DenylistScope)` / `SetClassDenylist(DenylistScope, set)`
with read-modify-write so writing one scope preserves the other two. SPC VM uses
`Spc`, Snapshot/Diff uses `Diff`, Pivot uses `Pivot`.

**Class Pivot right-click "Hide this class".** Pivot has no result-derived Top-N
picker (it analyses one class), so its denylist is populated by a ComboBox
ContextMenu "Hide selected class from picker" → adds to the Pivot-scope list and
drops the class from the picker. A hidden-class chips bar (with per-chip remove +
Clear all) appears below the results when non-empty (`HasHiddenClasses`).

**Cancellation + the tab-switch hang fix.** The reported symptom — switching to
Class Pivot mid-SPC-query froze the UI (50-80% CPU) and the process lingered after
close, blocking re-launch — was an uncancellable multi-million-row in-memory query
competing with the new tab's load. Each experimental VM now owns a
`CancellationTokenSource`, cancels its prior op on a new one, and exposes
`CancelPendingWork()`. `MainTabs_SelectionChanged` cancels every experimental tab's
heavy op when navigating away from it; `MainWindow.OnClosed` cancels all three so
the process exits promptly (releasing the single-instance mutex). Crucially —
`Microsoft.Data.Sqlite`'s `ReadAsync(ct)` runs synchronously and **ignores the
token**, so explicit `ct.ThrowIfCancellationRequested()` was added inside every
heavy DB-read / in-memory loop (SPC anchor + per-pass + eval, diff A-load + B-stream,
pivot row fetch ×2) at a ~64k-row cadence, plus an early bail before opening the
connection. Capture (streaming, yields between chunks) is deliberately NOT cancelled
on tab-switch.

**Gray out inputs during operations.** Snapshot capture region was already gated
(`CanEditSettings`, build 882); added gating for the diff inputs during `IsDiffing`,
SPC query inputs + picker grid during `IsQuerying`, and the Pivot selection +
key-mode + field grid during `IsBusy`. Progress bars / status / result-action
buttons (Open / Copy) stay live.

**Reset on the noise pickers.** SPC + Diff Top-N pickers gain a "Reset ticks" button
(`ResetNoisePicksCommand`) that unticks all rows without touching the persisted
denylist (distinct from "Clear all", which empties it). Pivot's equivalent is its
"Clear all" hidden-classes button.

**Collapsible Snapshot layout + splitter.** The capture region and the compare
region are now `Expander`s (capture force-expanded while capturing via
`CaptureSectionOpen`). A `GridSplitter` between the saved-snapshot list (2★) and the
compare+diff block (5★) lets the user trade vertical space — the diff grid (which
showed very few rows) can now be enlarged by collapsing the two regions and dragging
the splitter.

Tests +9 (scoped denylist independence + per-scope persistence + already-cancelled-
token throws for Diff & SPC) → **1250 C# + 393 dll + 31 utf8 = 1674**, all green.
Native AOT publish clean. **LIVE-VERIFY PENDING (user):** (1) per-tab isolation —
hide a class in SPC, confirm it still shows in Diff/Pivot; (2) Pivot right-click
"Hide this class" → confirm it leaves the picker + a chip appears + restart persists;
(3) the tab-switch hang is gone (switch SPC→Pivot mid-query → "Query cancelled.",
responsive); (4) collapse capture + compare, drag the splitter → diff grid grows.
Note: the Pivot ContextMenu command binding inherits the ComboBox's VM DataContext
(no `$parent` traversal), but ContextMenu-in-popup bindings are an Avalonia AOT risk
worth confirming at runtime.

-----

## 2026-06-05 — Experimental N1: per-game class denylist + Top-N noise picker (build 908)

SPC over BPGC-heavy games was flooding the 50k cap with directional-but-
irrelevant hits — game-side widgets / anim BPs / tick components (`W_HUD_C`,
`WBP_Inventory_C`, `BP_CooldownComponent_C`, …). Static denylists don't travel
between games (each title's noise classes are named differently), and the
existing `Aura::IsEnginePackage` skip only covers `/Script/*` not game-side
BPGCs. N1 turns "what's noisy?" into a one-look UI question over the result
the user just paid to compute.

**Surface — Top-N picker on SPC + Diff result tabs.** Each run produces a
`TopContributors: List<ClassNoiseRow>` (max 50) ranked by hit count over the
*matched-rows* set (not raw capture), each row carrying up to 3 sample prop
names. A fold-out Expander under the result grid lets the user tick rows and
hit "Apply &amp; re-run" — the picks join the per-game denylist and the SPC/Diff
query re-runs immediately so the cleaned result is visible without leaving the
tab. Below the picker: chips showing the active denylist with one-click remove
(`RemoveFromDenylistCommand`) + a "Clear all" button.

**Persistence — sibling JSON next to the per-game DB**: `snapshots.&lt;pe_hash&gt;.
denylist.json`. Deviation from the original spec (which proposed extending
`experimental.json` with a per-pe_hash dict). Reasons: (a) the denylist
auto-follows the game already keyed by pe_hash; (b) it survives FIFO snapshot
eviction (eviction drops snapshots, not the user's noise picks); (c) no need
to plumb pe_hash through the gate service. Source-gen JSON (`ClassDenylistJsonContext`),
atomic temp-then-rename writes, swallow-and-log on failure — same pattern as
`ExperimentalGate`. Filenames sanitise pe_hash to ASCII alphanumerics
(same defence as the DB filename). Save is gated on an active game so the
default DB never accumulates game-specific picks.

**Filter application — at the anchor-load step (saves memory AND match cost).**
`SpcQuery` and `SnapshotDiffFilter` gain `ExcludedClasses: HashSet&lt;string&gt;?`.
In `SpcQueryAsync` denied classes are skipped on the anchor-load row stream
*and* on every subsequent snapshot pass, so they never enter the candidate
dict — cuts the in-memory hash-join's peak working set on noisy games. In
`DiffSnapshotsAsync` denied classes are filtered out of BOTH the A-load and
the B-stream (and `bTotal` excludes them too, so the Added/Removed churn
numbers reflect only the visible classes). The Top-N accumulator counts
post-filter, so the picker never re-suggests an already-denied class.

**Pivot — picker filtering only, no Top-N UI.** Class Pivot is per-class
(user picks ONE class to pivot), so a "Top contributor" computation from a
single class is meaningless. Instead `ClassPivotViewModel.LoadClassesAsync`
reads `_store.GetClassDenylist()` and skips denied entries before populating
the bound `_allClasses` list. Symmetric UX: the same denylist that hides
classes from SPC/Diff results also hides them from the Pivot picker.

Files:
- New `ui/UE5DumpUI/Models/ClassDenylistSettings.cs` (model + source-gen JSON ctx).
- `ui/UE5DumpUI/Models/SpcModels.cs`: `SpcQuery.ExcludedClasses`,
  `SpcResult.TopContributors`, `ClassNoiseRow`.
- `ui/UE5DumpUI/Models/SnapshotDiffModels.cs`: `SnapshotDiffFilter.ExcludedClasses`,
  `SnapshotDiffResult.TopContributors`.
- `ui/UE5DumpUI/Core/ISnapshotStore.cs`: `GetClassDenylist` / `SetClassDenylist`.
- `ui/UE5DumpUI/Services/SnapshotStore.cs`: denylist persistence (sibling JSON),
  filter at anchor/per-pass row reads (SPC + Diff), `NoiseAccumulator` helper that
  Top-N-ranks contributors with up to 3 sample props each.
- `ui/UE5DumpUI/ViewModels/SpcQueryViewModel.cs` +
  `ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs`: denylist state,
  `RebuildNoiseRows`, `ApplyNoisePicksAsync` / `RemoveFromDenylistAsync` /
  `ClearDenylistAsync` commands, `NoiseRowVm` (shared).
- `ui/UE5DumpUI/ViewModels/ClassPivotViewModel.cs`: denylist filter on
  `LoadClassesAsync`.
- `ui/UE5DumpUI/Views/SpcPanel.axaml` + `SnapshotPanel.axaml`: Expander +
  picker DataGrid + chip ItemsControl (AOT-safe — no string-path Bindings).
- `ui/UE5DumpUI/Resources/Strings/en.axaml`: `str.Noise.*` keys.
- Tests +6 in `SnapshotStoreTests.cs`: `DiffSnapshots_ExcludedClasses_*`,
  `DiffSnapshots_TopContributors_*`, `SpcQuery_ExcludedClasses_*`,
  `ClassDenylist_PersistsAcrossStoreReload_PerGame`,
  `ClassDenylist_WithoutActiveGame_DoesNotPersist`,
  `ClassDenylist_FilenameSanitisedForPeHash`.

Tests: **1247 C# + 393 dll + 31 utf8 = 1671**, all green. Native AOT publish
clean (zero IL2026 / IL3050 — source-gen JSON for the new settings type
registered via `ClassDenylistJsonContext`). LIVE-VERIFY PENDING (user):
(1) capture two SPC-friendly snapshots on a noisy game; (2) tick the top 1-2
W_*/WBP_*/anim_BP rows; (3) Apply &amp; re-run → confirm gameplay rows now
dominate the result; (4) flip to the Pivot tab → confirm denied classes are
absent from the picker; (5) restart the app → denylist still loaded.

-----

## 2026-06-04 — Experimental Snapshot/SPC/Pivot: live-test hardening + in-memory engines (builds 879-884)

A long live-test + iteration pass on the gated Snapshot / SPC Query / Class Pivot tabs.

**879 — crash + hang fixes.** Live test surfaced a crash + a hang (root-caused from the
UI view log). (a) `ObservableCollection.Clear()`+repopulate while bound to a
ComboBox/DataGrid selection trips Avalonia's selection model
(`ArgumentOutOfRangeException` / "Cannot change ObservableCollection during a
CollectionChanged event") → new `UiCollection.Reset()` detaches the selection before
mutating, applied to every selection-bound rebuild. (b) `Microsoft.Data.Sqlite`'s
`*Async` runs synchronously on the caller, so the Pivot/SPC/Diff queries froze the UI
→ all heavy store calls wrapped in `Task.Run`. Also: SPC oldest-first ordering, baseline
predicate forced to "Any" + disabled, 2-snapshot warning; experimental checkbox split
from the author credit (Opacity 0.1); Snapshot "Open DB folder" button
(`IPlatformService.RevealInExplorerAsync`). Merged to main (PR #230).

**880 — diff + SPC result filters.** AutoCompleteBox pickers (Class/Field/Object, distinct
from the result set), a global filter (any column), and value ranges (diff Old/New;
SPC value-sequence first/last) with Apply/Reset.

**881 → 882 — DB normalize, then REVERTED.** Tried normalising identity into an `objects`
table + `vfields` view (halved size) — but it made Run Diff take >1 min on ~1.8M rows
(the self-joins fell off their single-table composite covering indexes). Reverted to the
denormalised schema; kept the version-gated drop-on-old mechanism. Added indeterminate
progress bars + "Running…" status on all three tabs, and locked capture controls during
a capture/diff.

**883 — in-memory hash-join diff (the real fix, ported from `discrete`).** The Unity sister
project never diffs in SQL — it loads both snapshots into dictionaries and diffs in two
O(n) passes. Adopted: `DiffSnapshotsAsync` streams A into a
`Dictionary<(class,gobjects_index,prop),(hex,num)>`, streams B and hash-looks-up A
(changed rows + Added/Removed churn in one pass). O(n), independent of index/schema shape.

**884 — in-memory SPC + drop heavy indexes (~½ DB) + SPC absolute predicates.** Replaced the
N-way SQL self-join (`SpcQueryBuilder`, deleted) with an in-memory intersection + new pure
`SpcEngine` predicate evaluator. With diff & SPC in-memory and Pivot filtering by
`(snapshot_id, class_fqn)`, dropped the three heavy composite indexes
(`ix_strict/loose/insession`, ~450 MB) for a single lean `ix_fields(snapshot_id, class_fqn)`
— roughly halving the DB at zero capture cost (schema v4). Added per-snapshot **absolute
value predicates** (Exact / Between / ≥ / ≤) applied before the 50k cap, fixing the case
where directional-but-irrelevant UI noise (e.g. `SizeBox.WidthOverride` 1920→0) crowded
real gameplay values out of the cap.

Tests 1358 → 1241 net for the experimental suite churn (the totals shifted as
`SpcQueryBuilderTests` were replaced by `SpcEngineTests`); full suite green throughout
(1241 C# / 393 dll / 31 utf8 at build 884). LIVE-VERIFIED by user: diff fast + correct;
SPC results + value-filter pending final confirm. The `discrete` techniques still
unmined: gzip per-class blob storage (further size), lazy field-index + eviction.

-----

## 2026-06-04 — Class Pivot C5 (right-click handoff) + C6 (array-element pivot) (build 877)

Two more Phase C closures in one session.

**C6 — array-element pivot ("Snapshot Array" source).** Phase A1b already captures
struct-array elements with their inner-key (e.g. `Cargo[].ItemID`) + inner numeric
props (`Quantity`) into the `fields` table's array columns. C6 surfaces them: a third
`Source` mode lets the user pick a snapshot → array-class → struct-array field, then
groups the elements **by inner-key value** (reorder- and owner-immune) projecting the
inner props. The neat part: **no new engine.** `SnapshotStore.PivotArrayAsync` fetches
the element rows and maps each `(owner GObjects index, element index)` pair to a
*synthetic* `PivotInputRow.ObjectIndex`, with `NormPath = inner_key_value`. Run through
`PivotEngine.Build` in **Identity mode**, that groups by inner-key value with the exact
same collision rendering as the scalar pivot. New store methods:
`ListPivotArrayClasses/Fields/Props` (array_field IS NOT NULL filters) + `PivotArrayAsync`.

**C5 — right-click "Pivot this property…" handoff.** A context-menu item on
PropertySearch, InterestingProperties, and LiveWalker raises a new
`NavigateToPivot(className, propName)` event. MainWindowViewModel switches to the Class
Pivot tab and calls `ClassPivotViewModel.PivotForAsync`, which forces scalar Snapshot
mode, selects the class in the newest snapshot (clearing any class filter), and ticks
the handed-off property as a value field (unless it became the auto-suggested key).
Graceful no-op with a status hint when no snapshot/class match exists.

**Gating (the "invisible when experimental off" requirement).** C6 lives entirely
inside the already-gated Class Pivot tab. C5's menu items bind `IsVisible` to a new
per-VM `PivotEnabled` flag that MainWindowViewModel sets to
`ExperimentalEnabled && Pivot != null` (refreshed on `gate.Changed`) — so the handoff
disappears the moment experimental features are toggled off, and never appears when no
snapshot store is wired.

Tests +12 (6 `ArrayPivotStoreTests` + 3 `ClassPivotViewModelTests` [array-source +
2× `PivotForAsync`] + 4 `PivotHandoffCommandTests`). All green: 1244 C# / 393 dll /
31 utf8. AOT publish clean. **LIVE-VERIFY PENDING (user):** (1) C6 — capture a snapshot
of a game with a struct-array inventory, pivot it by inner key; (2) C5 — right-click a
property in PropertySearch/InterestingProps/LiveWalker → confirm it lands in Class Pivot
pre-selected. Remaining Phase C: C2 (find-by-value).

-----

## 2026-06-04 — Class Pivot C4: DataTable-native zero-config pivot (build 873)

Closed Phase C4 of the experimental Snapshot/SPC/Pivot work. A DataTable is already
a `RowName → struct` map, so it pivots with **no key discovery**: each row is its own
group keyed by RowName, and the row struct's fields are the projected columns.

**Design — reuses the existing seam end-to-end.** No new pipe command and no new tab:
the DLL's `walk_datatable_rows` (+ `DumpService.WalkDataTableRowsAsync`) already
existed but had no UI consumer. C4 adds a **`Source` toggle (Snapshot / DataTable)**
to the existing Class Pivot tab. DataTable mode swaps the snapshot/class pickers for a
live DataTable picker (`FindInstancesAsync("DataTable")`, subclass-tolerant, filtered
to class names containing "DataTable"), walks the selected table, and feeds the rows
through the new pure `DataTablePivotEngine` into the *same* results grid + CE handoff
(Copy Address / Open in Live Walker) the snapshot pivot already uses.

- **`DataTablePivotEngine`** (pure, AOT-safe, unit-tested): `Build(dt, valueFields)`
  → one `PivotResultRow` per row (Count 1, never a collision, `ObjAddr` = row struct
  address for CE); `Fields(dt)` aggregates struct fields across rows (type / distinct /
  instance counts) to drive the value-field picker.
- **`ClassPivotViewModel`** gains an optional `IDumpService`, the `Source`/DataTable
  state, `RefreshDataTablesAsync` + `LoadDataTableFieldsAsync`, a DataTable branch in
  `RunPivotAsync`, and `ShowKeyField`/`IsSnapshotSource`/`IsDataTableSource` so the
  Group-By/key controls hide in DataTable mode (replaced by a "Key = RowName" hint).
- **Gating:** C4 lives entirely inside the Class Pivot tab, which is already
  `IsVisible="{Binding ExperimentalEnabled}"` — so the whole feature is invisible when
  the experimental flag is off. No new ungated surface was added.

Tests +8 (6 `DataTablePivotEngineTests` + 2 `ClassPivotViewModelTests` DataTable-source
cases); made `StubDumpService.FindInstancesAsync`/`WalkDataTableRowsAsync` `virtual` so
the VM test can subclass. All green: 1232 C# / 393 dll / 31 utf8. AOT publish clean.
**LIVE-VERIFY PENDING (user):** pick a real game's DataTable and confirm rows + CE
handoff. Remaining Phase C: C2 (find-by-value), C5 (right-click handoff), array pivot.

-----

## 2026-06-03 — Native UFunction property xref via x64 disassembly (Path 2, builds 862-872)

The complement to Path 1: **answer "which fields does this *native* (C++) function
read/write?"** for functions that Path 1 can't see — `FUNC_Native` functions have
empty Kismet bytecode. Path 2 disassembles the native function's machine code with
**Zydis** and maps each `[this+offset]` access back to a UPROPERTY on the owning
class. Forward direction only (function → properties); the reverse (property →
native funcs) would mean disassembling every native function per query and is
deferred. Heuristic by nature (vs Path 1's exact byte match), so confidence is
surfaced honestly in the UI.

**Design — reuses the Path 1 seam end-to-end.** No new pipe command, no new UI
panel: `walk_function_props` already returned empty for native functions and
`FunctionPropsDialog` already showed *"native, no bytecode"*. Path 2 plugs into
exactly that gap — when `UStruct::Script` is empty the DLL falls back to
disassembly and returns the same `FunctionPropRef` rows, tagged `method="disasm"`
with a per-row `confidence`.

- **Zydis vendored as `vendor/zydis` submodule** (v4.1.1 + nested `zycore`),
  matching the `vendor/minhook` convention. Built **static, decoder-only**
  (`ZYDIS_FEATURE_ENCODER/FORMATTER OFF`) → ~250KB. Linked into `UE5Dumper`, both
  proxy DLLs, and `dll_helpers_test`. RE-UE4SS's copy was only a FetchContent stub
  (downloads at build time — rejected as against the offline-vendoring rule).
- **`Denken` module** (`dll/src/Denken.{h,cpp}`, naming map #25). Pure Zydis-only
  decoder core: `Analyze(startAddr, MemReader)` where all process-memory access
  goes through a caller-supplied reader callback — so the TU links into the leaf
  test with no Macht/Win32 dependency. `this` is seeded in **RCX** (MS x64 ABI; the
  exec-thunk signature is `Func(UObject* Context, FFrame&, void*)`), tracked across
  register copies; `[reg+disp]` accesses are recorded high-confidence when the base
  is a proven this-alias, low otherwise (stack/RIP bases excluded). Follows up to
  a few direct `CALL`/tail-`JMP` handoffs (the thunk → real C++ impl) when RCX
  still holds this; instruction budget caps runaway. **Zydis v4 gotcha:** the
  displacement-present flag is `op.mem.disp.has_displacement`, not `.disp.size`.
- **`UFunction::Func` offset detection** (`Aura::EnsureUFunctionFuncOffset`,
  `DynOff::UFUNCTION_FUNC`). Detected **lazily** on first Path-2 use (GObjects
  guaranteed ready, zero startup cost): every UFunction's `Func` is an in-module
  code pointer (native → execXxx thunk; script → ProcessInternal), and no other
  pointer member in the `0x80..0x158` window is `MEM_IMAGE` executable, so the
  first offset where all sampled UFunctions hold a `Macht::LooksLikeCodePointer`
  IS Func. `0` = not found → Path 2 silently disabled (method `"none"`), Path 1
  unaffected. New `Macht::LooksLikeCodePointer` validates via `VirtualQuery`
  (`MEM_IMAGE` + `PAGE_EXECUTE*`).
- **Aura wiring** (`WalkFunctionPropertyRefs`). On empty Script → read the exec
  ptr, `Denken::Analyze`, then map accesses to properties via `Ubel::WalkClass`
  (already includes the full inherited super chain with absolute `Offset_Internal`,
  so an offset matches at most one property). Unmapped accesses counted, not shown.
  Rows sorted high-confidence → writers → frequency → name.
- **Pipe + UI.** `walk_function_props` response gains `method` + `unmapped` and
  each row gains `offset` + `confidence`. C# `FunctionPropRef(sResult)` mirrors them
  (defaulting `method` to `"bytecode"` for old DLLs); `FunctionPropsDialog` is now
  method-aware — a **Confidence** column appears for disasm results (low-conf rows
  amber), the status line flags `[native disasm — heuristic, N unmapped]`, and the
  `"none"` case keeps the old "no analysis available" message. Caveat reworded to
  cover both methods.

**Tests.** +5 `Denken_*` cases in `dll_helpers_test` (hand-assembled x64: this
write / non-this read / alias propagation / call-handoff followed / non-this call
NOT followed / terminators+guards) → 393 dll-helper assertions. +4 C#
`WalkFunctionPropsAsync` cases (bytecode / disasm with offset+confidence+unmapped /
none / backward-compat default) → 1224 C#. Suite: **1224 C# + 393 dll + 31 utf8 =
1648**, all green. Native AOT publish clean (no IL2026/IL3050 from the new
DataGrid column — uses the existing `FuncDataTemplate<T>` pattern). **Live verify
pending (user):** Geri (UE4.27) / ES2 (UE5.5) — Interesting Functions → a native
getter/setter → Props → expect disasm rows naming the touched field(s).

-----

## 2026-06-03 — Property ↔ Function bytecode cross-reference (Path 1, builds 838-861)

A new RE capability line: **answer "which methods use this field, read or write?"
and the inverse "which fields does this function touch?"** by statically scanning
Kismet (Blueprint) bytecode. Six commits on `dev`. No full disassembler — every
step anchors on a known **address** (zero false positives) plus a few stable
`EExprToken` opcode values, so version risk stays low. Coverage is BP/script
functions only; native (`FUNC_Native`) functions have empty bytecode and are
invisible (complementary to CE access-breakpoints, which cover native but drown on
shared/inlined code). Headless verifier: `scripts/xref_probe.ps1` (a pipe CLIENT —
the opposite of `test_pipe.ps1`'s mock server).

- **`574031e` — DLL Path 1 (`find_property_xrefs`).** Given a target `FProperty*`,
  byte-scan every UFunction's `UStruct::Script` for the (unaligned) pointer; the
  variable-access opcodes embed the live fixed-up `FProperty*` directly.
  `DynOff::USTRUCT_SCRIPT` is **derived as `USTRUCT_PROPSSIZE + 0x08`** (MinAlignment
  always sits between them) — verified against every RE-UE4SS `MemberVariableLayout`
  template (UE 4.18-5.7) AND every shifted custom-game layout (Atomic Heart / Silent
  Hill F / Outer Worlds), so the +8 invariant is universal and inherits PROPSSIZE's
  calibration. Reuses `ParallelGObjectsScan` + `ConcatTruncate`; 30s deadline; relies
  on Ubel's mutex-guarded caches (build 792).
- **`0548ed3` — UI wiring.** `PropertyXrefDialog` (code-behind, AOT-safe
  `FuncDataTemplate` columns); Class Struct field grid → right-click "Find functions
  using this field". Self-contained dialog, no new tab (experimental-tab layout
  untouched).
- **`a84eca9` — extend + UX consistency.** DLL `search_properties{,_batch}` now emit
  `field_addr`; Property Search + Interesting Properties get the same xref (row
  "Find Funcs" button + context menu); Class Struct gains a client-side field
  Filter box + row button. Shared `PropertyXrefDialog.ShowForFieldAsync` owner
  resolver. VMs take `IPlatformService` (optional where tests construct them).
- **`4d349d6` — v2a ubergraph → event attribution.** A BP event's stub calls
  `ExecuteUbergraph(<int entryOffset>)` via `EX_(Local)FinalFunction(0x46/0x1C)` +
  `EX_IntConst(0x1D)`. `BuildUbergraphEntryTable` anchors on the ubergraph
  function's address, reads each stub's entry offset → `(entryOffset, eventName)`;
  a serial post-pass attributes each reference offset to the event whose entry
  offset is the largest ≤ it. So `ExecuteUbergraph_*` hits resolve to the actual
  event (e.g. `ItemEffects_BeginPlay`). Best-effort: shared sub-graphs reached from
  multiple events can mis-attribute (needs v2b CFG walk).
- **`9286350` — read/write distinction.** `IsWriteContext` detects assignment
  destinations via the `EX_Let*` LHS shapes (`EX_LetBool 0x14`/`MulticastDelegate
  0x43`/`Delegate 0x44`/`Obj 0x5F`/`WeakObjPtr 0x60` = `[LetOp][varOp][ptr]`;
  `EX_Let 0x0F` = `[0x0F][propptr 8B][varOp][ptr]` with a `LooksLikeHeapPtr` check;
  `EX_LetValueOnPersistentFrame 0x64`). Conservative (high precision); wrapped LHS
  (`Other.Field` / `Struct.Member` / `Arr[i] = x`) reads as a read.
- **`248f631` — reverse edge (`walk_function_props`).** Given ONE UFunction, parse
  its bytecode and list every `FProperty` it references with read/write tally +
  **scope** (instance / local / default / sparse / struct / frame).
  `Ubel::ResolvePropertyNameType` validates each candidate (type name must contain
  "Property"). Sorted instance-first so BP compiler temporaries
  (`CallFunc_*_ReturnValue`, scope=local) don't drown the class fields.
  `FunctionPropsDialog` has a **"Class fields only"** filter (default on);
  Interesting Functions panel gains a "Props" row button.

Live-verified throughout on Everspace 2 (UE5): `BP_Ship_Player_C.
OkkarCatalystCargoUnit2HPBuff` → 7 refs, 1 write, event `ItemEffects_BeginPlay`;
`ExecuteUbergraph_BP_Ship_Player` → 6231 props, instance fields surfaced first.
Every commit: DLL + UI Release build OK, **tests 1220 C# + 370 dll + 31 utf8 green**,
Native AOT publish clean. New pipe commands: `find_property_xrefs`,
`walk_function_props` (+ `field_addr` on `search_properties{,_batch}`,
`ustruct_script` in `get_offsets`).

**Deferred (see todo):** v2b (CFG-precise event attribution + wrapped-LHS r/w),
Path 2 (Zydis disasm of native UFunctions).

## 2026-06-02 — Experimental: Class Pivot (Phase C, build 830)

Third tranche of the experimental Snapshot / SPC / Pivot feature. **Phase C
(Class Pivot)** ships the value-keyed grouping core + UI. Pure C# over the
existing SQLite corpus, **zero DLL change**. Design of record:
[experimental-snapshot-spc-pivot.md](experimental-snapshot-spc-pivot.md)
§"Phase C".

- **C1 — PivotEngine (pure, AOT-safe).** `PivotEngine.Build` folds the captured
  (instance, field) rows into per-instance records, groups them by **intrinsic
  identity** (normalised path — spawn-counter siblings `BP_Enemy_C_0/_1/…`
  collapse into one group so the value cells show the spread) **or by a chosen
  key field's value** (e.g. inventory by `ItemID`), and projects the requested
  value fields per group. Differing values within a group render as a collision
  `⟨N: v1,v2,+M⟩` (ported from the Unity sister project's 29e-2 polish). Sorts
  most-populous-first; caps at `MaxGroups`. `SnapshotStore.PivotAsync` fetches
  only the key + value rows for the class then calls the engine; `Pivot*Async`
  helpers list classes (with instance counts) and fields (with cardinality).

- **C3 lite — key discovery.** `PivotKeyScorer` ranks numeric fields as group
  keys by **type prior** (Byte/enum + int good, float/double poor), **name prior**
  (id/index/type/slot/tag/… tokens), and **cardinality** (a key must actually
  partition: `1 < distinct < instances`). `SuggestKey` auto-selects the best;
  value-field interest reuses the calibrated `PropertyScoringTable.Score`. This
  is the UE answer to `discrete`'s "user must guess the business key" pain.
  (Top-level capture is all-numeric, so v1 keys are int/enum IDs; FName keys live
  on array-element rows — a later array-pivot path.)

- **C1 UI.** `ClassPivotViewModel` + `ClassPivotPanel` replace the Class Pivot
  placeholder tab: snapshot picker, class picker (filter + most-populous-first),
  a key-mode toggle (Identity / Field) with an auto-suggested key field, a field
  grid (tick value fields; shows type / distinct / instances / **key score**),
  and a results grid (Count / Key / Projected values). Selecting a class loads
  fields, suggests the key, and pre-ticks the most interesting value fields.
  Group → **Open in Live Walker** / **Copy Address** hands its representative
  instance to CE. Wired into `MainWindowViewModel.Pivot`; refreshed on tab
  activation. Like SPC, the projected-values column is a single rendered string
  (collision-aware) to stay AOT-safe — no per-field dynamic bindings.

- **Tests +22 → 1219 C# (1620 total: 1219 C# + 370 dll + 31 utf8).**
  `PivotEngineTests` (field/identity grouping, missing-key bucket, truncation,
  the `⟨N: …⟩` collision render), `PivotKeyScorerTests` (int>float, partitioning
  key > unique id, value interest via the scoring table), `PivotStoreTests`
  (class/field listing + field/identity pivots end-to-end), and
  `ClassPivotViewModelTests` (load → key suggestion → run, via a `PendingLoad`
  test seam on the selection-triggered async loads). Full clean Native AOT
  publish is clean. **Remaining experimental: C2 (find-by-value locator),
  C4 (DataTable-native pivot), C5 (right-click handoff from other panels),
  array-element pivot, A3c (CE .CT export).**

-----

## 2026-06-02 — Experimental: SPC Query (Phase B) + opt-in checkbox lock (build 824)

Second tranche of the experimental Snapshot / SPC / Pivot feature. **Phase B (SPC
Query)** ships the multi-session, type-agnostic directional query engine + UI —
the energy-bar driver case. Pure C# over the existing SQLite corpus, **zero DLL
change**. Plus a UX change on the experimental opt-in checkbox. Design of record:
[experimental-snapshot-spc-pivot.md](experimental-snapshot-spc-pivot.md)
§"Phase B".

- **B1 — engine (pure C#, no pipe).** `SpcQueryBuilder.Compile` turns an
  `SpcQuery` (ordered snapshot ids + per-snapshot predicate chain + join mode +
  filters) into one indexed SQLite statement: an **N-way self-join** over the
  `fields` table where the oldest snapshot `f0` is the anchor and every later
  snapshot `f{i}` inner-joins on the chosen identity key (so only fields present
  in ALL selected snapshots survive — the candidate intersection). Directional
  predicates become `WHERE` clauses comparing `f{i}` vs `f{i-1}`:
  Unchanged/Changed compare raw `hex` (type-exact); Increased/Decreased compare
  `numeric_value` by the field's declared width (no byte-reinterpret false hits).
  Pushing predicates into SQL lets a selective chain collapse a million-row
  intersection to a handful via the `ix_strict`/`ix_loose`/`ix_insession`
  indexes. Snapshot ids + limit are inlined (validated long/int — injection-safe);
  only the two LIKE filters are parameterised. `SnapshotStore.SpcQueryAsync`
  executes it and renders each snapshot's value via `SnapshotNumeric.Render`.
  **Join modes:** Strict `(class, norm_path, prop, offset)` / Loose
  `(class, outer_chain, prop)` / In-session `(class, gobjects_index, prop)`.
  `SpcModels` carries `SpcPredicateKind` (Any/Unchanged/Changed/Increased/
  Decreased — directional v1), `SpcJoinMode`, `SpcQuery`, `SpcResultRow`
  (rendered value sequence as one AOT-safe column), `SpcResult`.

- **B2 — UI.** `SpcQueryViewModel` + `SpcPanel.axaml` replace the SPC placeholder
  tab. A snapshot picker (DataGrid: tick + label + captured + **session tail** so
  cross-session spans are visible + a per-row predicate ComboBox), a Strict/Loose/
  In-session toggle, class/field filters, and a results grid (Class / Object /
  Field / Type / **value sequence**). The oldest ticked snapshot is the baseline
  (its predicate ignored); each later one compares to the previous ticked. Hit →
  **Copy Address** (newest snapshot's `obj_addr` + offset) / **Open in Live
  Walker** (the existing `NavigateToInstance` handoff). Wired into
  `MainWindowViewModel.Spc` (shares the snapshot store), refreshed on tab
  activation so a just-captured snapshot appears. Results column is a single
  rendered "value sequence" string — no per-snapshot `Binding("Values[i]")`, which
  would trip the IL2026/IL3050 AOT warnings (build-780 lesson).

- **Opt-in checkbox UX (user request).** The System-tab experimental-enable
  checkbox now renders at **Opacity 0.25**, and once it is checked **and** the
  user has opened any experimental tab (Snapshot / SPC Query / Class Pivot) it can
  **no longer be unticked**. `IExperimentalGate` grows `IsLocked` + `Lock()`
  (`IsEnabled` setter also refuses to go false while locked — defence in depth).
  The three experimental `TabItem`s gained `Tag`s; `MainTabs_SelectionChanged`
  calls `MainWindowViewModel.LockExperimental` (idempotent, no-op unless enabled)
  on first open. `PointerPanelViewModel` exposes `CanToggleExperimental`
  (`!IsLocked`) bound to the checkbox `IsEnabled`. The lock is **session-only**
  (NOT persisted) — a restart clears it, so the user can untick again until they
  re-open an experimental tab.

- **Tests +29 → 1197 C# (1598 total: 1197 C# + 370 dll + 31 utf8).**
  `SpcQueryBuilderTests` lock the SQL shape (join keys per mode, predicate
  clauses, filters, limit, validation) without a DB; `SpcStoreTests` run the
  engine end-to-end against a temp SQLite (money "decreased twice" + the
  cross-session energy-bar "same/same/down/up" + norm_path spawn-counter merge +
  filters + intersection drop); `SpcQueryViewModelTests` cover refresh/auto-select/
  run/copy-address/navigate; `ExperimentalGateTests` +4 lock the lock contract.
  Full clean Native AOT publish (`build.ps1 -Mode Publish`) is clean (the only ILC
  notes are the pre-existing benign X11/DBus Linux-backend trim messages).
  **Remaining experimental: Phase C (Class Pivot) + A3c (CE .CT export).**

-----

## 2026-06-02 — Experimental: Snapshot capture / diff (Phase 0 + A, builds 805-823)

First tranche of the experimental Snapshot / SPC / Pivot feature ported in
concept from the Unity sister project `discrete`. Gated behind an opt-in flag so
the default UI is unchanged. Design of record:
[experimental-snapshot-spc-pivot.md](experimental-snapshot-spc-pivot.md).
**The full capture → quota → compare loop now works end-to-end; A3 diff was
live-verified by the user (caught `DOLLFriendGameCharacter.HP 99→120`).**

- **Phase 0 — gating (build 805, `5b8a47d`).** The System-tab `bbfox` credit
  becomes a checkbox; checked → three experimental tabs appear (tooltip "Enable
  advanced experimental features"). `ExperimentalGate`/`IExperimentalGate`
  persists the opt-in to `%LOCALAPPDATA%\UE5CEDumper\experimental.json` (source-
  gen JSON; shared between the checkbox VM and tab-visibility VM via `Changed`).
  Also fixed an unrelated `build.ps1 -Target Test` crash from a stray empty
  `--no-restore` arg passed to the MTP runner (`4292004`).

- **A1a — DLL scalar capture (build 808, `fe8b5c2`).** Stateless cursor-paginated
  `begin_snapshot` / `snapshot_chunk` pipe commands. `Aura::CaptureSnapshotChunk`
  walks GObjects (game-only via `IsEnginePackage`), reuses cached
  `Ubel::WalkClassEx`, emits per-object identity (index/addr/name/class/
  outer_class/path) + every numeric scalar UPROPERTY (via the pure
  `ValueScan::SelectSnapshotNumericFields`, keyed on the existing NumericNoByte/
  NumericAll member sets).

- **A2 — SQLite store + capture UI (builds 809-813, `0747065` + `e832b4c` +
  `0da1248`).** `Microsoft.Data.Sqlite` raw ADO.NET (no EF Core). **Native AOT
  publish is clean and bundles `e_sqlite3.dll`** — the design's headline risk,
  resolved. `SnapshotStore` (denormalised `fields` table, strict/loose/in-session
  join indexes, streaming chunk writes); `SnapshotViewModel` orchestrates capture
  (begin → loop chunks → store → finalise, progress/cancel); `SnapshotPanel`.
  Pure helpers `SnapshotIdentity.NormalizePath` (leaf-only FName-suffix strip) +
  `SnapshotNumeric.TryFromHex`. **Per-game DB** `snapshots.<pe_hash>.db` — no
  cross-game mixing / unbounded growth / shared corruption blast radius.

- **A2c — quota + usage (build 815, `ab874a4`).** Per-game size quota with FIFO
  auto-eviction on capture (`EnforceQuotaAsync` drops oldest until ≤ quota then
  VACUUMs; newest always kept) + `GetUsageAsync` + per-snapshot `EstBytes`. Quota
  persisted in experimental.json; UI = quota dropdown + used/quota bar + Est.Size.

- **A3 — diff (build 817, `aeba44d`) + polish (build 820, `0731d4e`).** Both
  snapshots live in one per-game DB, so the diff is a single indexed SQL join on
  (class, GObjects index, property) WHERE bytes differ → changed rows (rendered
  via `SnapshotNumeric.Render`, direction ▲/▼) + Added/Removed churn counts. UI:
  Old/New pickers (default to the two newest), client-side **live**
  Class/Field/Object/Direction filters, Copy Address (CE handoff), Open in Live
  Walker.

- **A1b — struct-array inner-key capture (build 823, `ba7c370`).** The cargo/
  inventory case: `TArray<FStruct>` element inner numeric fields keyed by a
  reorder-immune inner key (`ValueScan::SelectArrayInnerKey`: keyworded-FName >
  FName > int > none). `Aura::CaptureSnapshotChunk` resolves the inner
  UScriptStruct (`ArrayProperty::Inner → StructProperty::Struct`), walks it, reads
  the `TArray`, emits ≤ `array_cap` elements with rendered inner key + numeric
  inner hex. Pipe `arrays` field; C# array-element rows; scalar diff excludes them
  (array diffing is Pivot). **Gotcha:** `FieldInfo`/`ClassInfo` are GLOBAL scope
  in Ubel.h, not `Ubel::` (C2039).

**Tests across the tranche**: 1485 → **1569** (1168 C# + 370 dll_helpers + 31
utf8). Every phase AOT-publish-clean. **Remaining (next sessions):** Phase B
(SPC multi-session directional — the energy-bar case), Phase C (Class Pivot,
incl. the array inner-key join + object/primitive arrays), A3c (CE .CT export).

-----

## 2026-05-29 — Value Search: with-byte variant `NumericAll` + result-volume warning (build 796-797)

Follow-up to NumericNoByte (build 794-795, todo #0d). Adds
`ValueScanDataType.NumericAll` — the same one-pass structured multi-numeric
scan but **including** the 1-byte families (`Int8Property` → Int8,
`ByteProperty` → UInt8). Bool stays excluded (it has its own single-type
scan). Members: Int8/UInt8 + Int16/UInt16 + Int32/UInt32 + Int64/UInt64 +
Float + Double (10).

Implementation rode almost entirely on the NumericNoByte plumbing — the meta
machinery (`IsMultiNumericDataType` / `ScanForValue` / `RefineCandidates` /
`CandidateToJson`) is keyed on `IsMultiNumericDataType`, so it picked up the
new type for free. The only DLL deltas: `MultiNumericMembers(NumericAll)`
(adds Int8/UInt8), `PropertyTypeNames(NumericAll)` (10-name union),
`TryDataTypeFromPropertyTypeName` now resolves `Int8Property`/`ByteProperty`
(safe for NumericNoByte — its union never feeds those names in),
`BuildNumericTargets` Int8/UInt8 fit-checks, and the SizeOf/NameOf/parse/
Format switch cases. `BuildNumericTargets` range-gates byte widths as
expected (`300` → no Int8/UInt8; `-5` → Int8 yes / UInt8 no; `200` → UInt8
yes / Int8 no).

**Result-volume warning** (user-requested): small values (0/1/255) match a
very large number of 1-byte fields, so the candidate set can explode. New VM
property `ValueSearchViewModel.DataTypeWarning` (non-empty only for
NumericAll, raised on DataType switch) drives an orange italic hint TextBlock
in `ValueSearchPanel.axaml` (binds via `IsNotNullOrEmpty`, same pattern as
`ErrorMessage`). DataType tooltip in `en.axaml` updated. `SupportsTolerance`
generalised to `IsMultiNumericDataType`; `DumpService.ToleranceAppliesTo`
includes NumericAll.

**Tests**: +30 DLL EXPECTs (members=10 incl. byte, byte property-name
resolution flipped from reject→resolve, NumericAll union-consistency lock,
BuildNumericTargets byte range-gates) + 10 C# (dropdown/classification,
tolerance, the warning is NumericAll-only + raises PropertyChanged, scan-type
mirror, wire name). All green: dll_helpers 349, utf8 31, C# 1105 (total 1485),
zero warnings. **In-game verification pending** (the volume-explosion behavior
the warning guards against is exactly what to sanity-check live).

-----

## 2026-05-29 — Value Search: multi-numeric "NumericNoByte" meta scan (build 794-795)

New `ValueScanDataType.NumericNoByte` — a "find this value across **every**
word/dword/qword/float/double field in one pass" mode, the natural starting
point when you know the value (e.g. `100`) but not whether it's stored as
`int32`, `float`, `uint16`, … Unlike CE's raw "All" type (which reinterprets
the *same untyped bytes* as multiple widths and produces overlapping false
hits), our scan is a **structured property walk** — each candidate's DECLARED
type is known — so each field is compared using *its own* declared width. A
`float Health` compares as float, an `int32 Ammo` as int32: **zero
byte-reinterpret false positives**. "No byte" deliberately excludes
`Int8`/`UInt8`/`Bool` (1-byte fields are too numerous; a small value would
flood the candidate set — the same reason CE breaks "Byte" out separately).
The with-byte variant is the planned follow-up.

Members: `Int16/UInt16`, `Int32/UInt32`, `Int64/UInt64`, `Float`, `Double`.

**DLL** (`ValueScan.{h,cpp}`, `Aura.{h,cpp}`, `Fern.cpp`):
- `IsMultiNumericDataType` / `MultiNumericMembers` / `TryDataTypeFromPropertyTypeName`
  (property-type-name → concrete DataType, rejecting Byte/Int8/Bool/non-numeric)
  / `NumericTargetSet` + `BuildNumericTargets`. The target set holds one
  little-endian buffer **per member width the value can represent** —
  `70000` yields no Int16/UInt16; `-5` no unsigned; `100.5` only Float/Double;
  hex `0x10` integer widths only. `PropertyTypeNames(NumericNoByte)` returns the
  8-name union (locked in a test to exactly match what
  `TryDataTypeFromPropertyTypeName` resolves, so a field can't be accepted yet
  fail per-field resolution).
- `ScanForValue` / `RefineCandidates` gained optional `multiTargets` /
  `multiTargets2` params. In multi mode the scalar-field **and** TArray-element
  comparison sites resolve each field's own DataType + matching target and call
  the existing `ComparePredicate`. Refine re-resolves each candidate's width
  from its stored `fieldType`. `CandidateToJson` renders each row's value with
  its own resolved width. Single-type paths are byte-identical (new branches
  are gated on `isMulti`).
- Tolerance flows through (applies per-member to float/double fields only;
  integer members ignore it, exactly as the single-type path already does).

**UI** (`ValueScanModels.cs`, `ValueSearchViewModel.cs`, `DumpService.cs`,
`en.axaml`): enum member + dropdown entry (listed first), `SupportsTolerance`
+ `ToleranceAppliesTo` include it, case-sensitive stays string-only. The
results grid's existing **Type** column shows each candidate's concrete
property type so the user sees which width matched. DataType tooltip updated.

**Tests**: +3 DLL test fns (`MultiNumericMembers` / `DataTypeFromPropertyTypeName`
incl. the union-consistency lock / `BuildNumericTargets` fit-rules) + 7 C# tests
(dropdown presence, family classification, tolerance/case gating, scan-type
validity mirror, wire-name + tolerance attach). All green: dll_helpers 319,
utf8 31, C# 1095 (total 1445). Zero compile warnings.

**Pending**: in-game verification (correctness + result-volume sanity on a
1M-object game), then the with-byte variant.

-----

## 2026-05-29 — Refactor: extract `ParallelGObjectsScan<ResultT>` template (build 793)

Follow-up to build 792 (logged in todo.md): the three parallelized scans each
carried ~identical scaffolding — a `ThreadResult` struct, the `nthreads` /
`perThread`-vector / `std::atomic<bool> deadlineHit` triplet, a `worker` lambda
open, the `ParallelIndexRanges` call, and a result-concat-with-truncate merge
loop. Centralised into two anon-namespace helpers in `Aura.cpp`:

- `ParallelGObjectsScan<PerThreadT>(count, body)` — owns `ScanThreadCount` +
  the `perThread` vector + the shared `std::atomic<bool> deadlineHit` + the
  `ParallelIndexRanges` call. `body(tr, beginIdx, endIdx, deadlineHit)` is the
  per-thread loop (per-object work + local maxResults cap + deadline check).
  Returns `{ perThread (moved), nthreads, deadlineHit.load() }`.
- `ConcatTruncate(perThread, &PerThreadT::member, maxResults)` — concatenates
  each thread's result vector (selected by pointer-to-member) in ascending-tid
  order, truncating to maxResults. **This is the ascending-merge + lowest-index
  truncation invariant, now a single source of truth** instead of triplicated.

Each scan keeps its own `ThreadResult` (the variable part — different element
type + counter set) and folds its per-thread stat counters inline (sum of
scanned/classesPrimed/classesWalked; `ScanForValue` unions its
`classesWithFields` set). `FindReferencesToUObject` carries the parallel phase's
deadline into its serial sparse-delegate pass via a plain `bool` seeded from
`scan.deadlineHit`.

Pure structural change — the per-object loop bodies are untouched, so behaviour
is byte-identical (the build-792 merge semantics are preserved exactly). Build
793 clean (zero warnings); **1358 tests unchanged** (31 utf8 + 247 dll_helpers +
1080 C#).

-----

## 2026-05-29 — Parallelize GObjects-walk scans + thread-safe Ubel caches (build 792)

Applied the **P1b parallelization** from `<private-ce-repo>/docs/Memory-Scanning-Internals.md` §16 to the three GObjects-array scans. They were single-threaded `for (i = 0 .. count)` walks — the wall-clock floor on 1M+ object / multi-GB-heap games. Each walk is read-only against game memory + init-time constants (FNamePool offsets, `g_cachedUEVersion`, the FUObjectArray layout), so partitioning the index range across worker threads parallelizes cleanly. (Unlike the `discrete` Unity dumper that doc also covers, our scan is a *structured property walk*, not a raw VirtualQuery sweep, so the doc's SIMD / interval-tree advice doesn't apply — only parallelization does. AOBScan already had AVX2 + executable-section filtering, and our reads are already SEH `memcpy` with no per-chunk VirtualQuery.)

**Infra (`Aura.cpp`, anon namespace):**
- `ScanThreadCount(workItems)` = `clamp(hardware_concurrency - 2, 1, 16)`; returns 1 for < 8192 objects (thread-spawn cost dominates). The `-2` leaves headroom for the game's own threads + our pipe/UI thread.
- `ParallelIndexRanges(count, nthreads, body)` — partitions `[0,count)` into contiguous ascending chunks; chunk 0 runs inline on the calling thread, the rest on `std::thread`, all joined before return. Each worker body is wrapped in `try/catch(...)` + `LOG_WARN` so a throwing chunk (e.g. `bad_alloc`) can't `std::terminate` the host game — it degrades to a best-effort partial merge. 64-bit chunk math guards an int32 multiply overflow if a corrupted count comes back huge.

**Three scans rewritten** (`ScanForValue`, `FindInContainers`, `FindReferencesToUObject`): per-thread caches + result buffer, shared `std::atomic<bool> deadlineHit`, per-thread local cap at `maxResults`. Results merge in ascending-tid order then truncate to `maxResults` → **byte-identical result set to the serial walk** (same addresses, ascending order, lowest-index-preserved-on-truncation) for both under-cap and over-cap cases; deadline-hit is best-effort partial. `FindReferencesToUObject`'s MulticastSparseDelegate pass (a single global-TMap walk, *not* a GObjects walk) stays serial and runs once after the merge. `ScanForValue`'s `scannedClasses` stat is deduped via a per-thread set union.

**Ubel cache thread-safety (the real prerequisite):** the workers call `WalkClass(Ex)` / `GetName` / `ResolveEnumValue` / `GetCachedStructFields` concurrently, all of which memoize into file-scope `unordered_map`s — concurrent first-touches would race. Added 5 leaf-level mutexes (`s_nameCacheMutex` / `s_enumCacheMutex` / `s_walkClassCacheMutex` / `s_structFieldCacheMutex` + `s_calibrationMutex`). Pattern: run the expensive walk/read WITHOUT the lock, guard only the map find/insert, and return either a value copy (`ClassInfo` / `std::string`) or a node-stable `unordered_map` reference (insert/rehash never invalidates element references). `CorrectSubclassOffsets`' one-time `DynOff::` writes use double-checked locking (atomic `s_checked` fast-path + calibration mutex); the existing acquire/release on `s_checked` plus the invariant "every `DynOff` calibrated-offset read is preceded on the same thread by a `WalkClassEx → CorrectSubclassOffsets` call that observed `s_checked == true`" make the offset reads race-free without locking every read.

**Code review** (xhigh effort, 9 finder angles + sweep): no correctness bug in the merge/locking core. Fixed 3: (1) deadline check made **chunk-relative** — `((i - beginIdx) & mask) == 0` so it fires from each chunk's first iteration regardless of where `beginIdx` lands (the old `(i & mask) == 0` could delay the deadline + cross-thread `deadlineHit` check by up to a full stride, or never fire for sub-stride chunks); (2) worker `try/catch`; (3) int64 chunk math. Refuted with reasoning: DynOff "race" (synchronized via `s_checked` acquire/release), `WalkClass`/`GetCachedStructFields` reference-return (`unordered_map` node-stability), calibration↔name deadlock (single, consistent lock order — `GetName` holds no lock while calling out), `classesPrimed` "2× over-count" (the serial code already counted per-instance, not per-unique-class, so the per-thread sum matches).

Tests: **1358 unchanged** (31 utf8 + 247 dll_helpers + 1080 C#). The existing suites stub the DLL / test predicates, so they confirm no compile-or-logic regression but don't exercise the live parallel walk. **In-game verified OK by the user (2026-05-29)** — parallel result set matches the serial walk, no hang / crash, and the expected multi-core First-Scan speedup held.

**Follow-up (logged in todo.md):** the `ThreadResult` / `perThread` / merge scaffolding is ~100 lines duplicated across the three functions — a candidate for a `ParallelGObjectsScan<ResultT>` template helper. Deliberately deferred to avoid refactoring just-verified concurrency code in the same change.

-----

## 2026-05-27 (PR #211 merged dev → main) — AOT-warning cleanup on Invoke structured-return DataGrid (build 780)

The pick #5 structured-return DataGrid (build 775) wired each `DataGridTextColumn` with `new Avalonia.Data.Binding("PropertyName")`. That ctor has `RequiresUnreferencedCodeAttribute` + `RequiresDynamicCodeAttribute` because Avalonia's string-path Binding uses reflection to resolve the property — directly violates CLAUDE.md's "Native AOT compatible, no reflection-based APIs" rule. `dotnet publish` emitted 18 warnings (IL2026 + IL3050) across the four column declarations + their forwarded constructor analysis.

Fix: switch the four columns to `DataGridTemplateColumn` + `FuncDataTemplate<StructFieldValue>(lambda)`. Each cell's text comes from a strongly-typed `Func<StructFieldValue, string>` so no reflection or dynamic dispatch fires. Centralised via a new private helper `AddStructuredReturnColumn(header, width, textSelector)` so the four column declarations stay one-line each.

Trade-off documented in the commit: FuncDataTemplate materialises each cell once at row creation and doesn't observe per-property INPC after that. Acceptable for this panel because `UpdateStructuredReturnGrid` replaces `ItemsSource` wholesale on each FIRE — no in-place mutation case.

Tests: 1080 (unchanged — pure UI plumbing fix). Publish build emits zero IL2026/IL3050; UE5DumpUI.exe still trims to 42.4 MB.

-----

## 2026-05-27 — Console panel: UCheatManager stripped-body hint footer (#6, build 778)

First live test of the build-731 Console tab surfaced the canonical gotcha: `UCheatManager::Fly` / `Ghost` / `God` / `Walk` / `Slomo` / `ChangeSize` invokes return `Result=0` (OK) but produce no in-game effect on cooked Shipping builds. Root cause: UE wraps these in `#if !UE_BUILD_SHIPPING`; Epic ships with that defined, so the function bodies compile out but the `UFUNCTION(exec)` reflection metadata (generated pre-cook by UHT) survives. PE call really happens, function returns 0, no-op.

**Distinct failure mode** from the build 647-648 wrong-vtable-slot bug. The discriminator is `Stark::GetHookFireCount()`: `>0 + Result=0 + no effect = cooker strip`; `==0 = hook on wrong slot`.

Surface area:
- `ConsoleViewModel.IsLikelyUCheatManagerExec(entry)` — public + static, case-insensitive substring match on `ClassName` or `SuperName` against "CheatManager". Catches engine class + game-defined subclasses (`MyGameCheatManager` / `BP_CheatManager_C`) + super-chain-via-immediate-super (`AFooCheats : UCheatManager`). Public so tests lock the heuristic without standing up a VM.
- `ConsoleViewModel.SelectedExecHint` — computed property re-evaluated on `SelectedResult` change via `OnSelectedResultChanged` partial; warning text when the row is UCheatManager-derived, empty otherwise.
- `ConsolePanel.axaml` — orange-bordered footer Border below the status row, IsVisible bound to non-empty SelectedExecHint. Same visual treatment as the Value Search "native C++ fields" banner so users recognise the warning pattern.
- `docs/lessons-learned.md` — new bullet under "UFunction Invoke / ProcessEvent" with the diagnostic flow.
- Memory file `feedback_ucheatmanager_stripped.md` — full diagnostic table + per-version UE source pointer + "what we don't try to do" scope (bypassing the strip is out of scope; this tool is discovery + dispatch).

Tests: 1065 → 1080 (+15). 10-row theory for the predicate (5 positive incl. case-insensitive + super-name-only variants, 4 negative), null guard, SelectedExecHint empty/populated/refresh-on-change.

Deferred: full super-chain walk to catch second-degree subclasses (`BP_MyCheatManager_C : MyGameCheatManager : UCheatManager`) — current substring heuristic catches the first two layers.

-----

## 2026-05-27 — Invoke result: structured-return DataGrid for struct returns (#5, build 775)

Existing decoder already produced `"X=1.0, Y=2.0, Z=3.0"`-style comma joins inside `_resultLabel` for FVector / FRotator returns. Pick #5 wires the same decode into a small 4-column DataGrid (Field / Type / Value / Offset) below the text decode so each sub-field becomes its own row with absolute buffer offset.

What landed:
- `Models/StructFieldValue.cs` — pure record (Name, Type, Value, Offset). Offset is **absolute** buffer offset (return param offset + sub-field offset) so users can copy it into Find In Containers / CE memrec setup directly.
- `Services/StructReturnDecoder.cs` — static `Decode` + `CanDecode`. Resolution order: KnownStructLayouts (per-version locked) → DLL-discovered dynamic StructFields → empty list. Delegates each byte→typed-value cell to `InvokeParamDialog.DecodeParamValue` so the grid and result-label never disagree on a byte mapping. SafeDecode wraps with try/catch so a single bad field doesn't blow the whole grid.
- InvokeParamDialog — pre-resolves `_returnParam` at construction; clears + hides grid at top of `OnFireClicked` so stale rows don't flash across invocations; `UpdateStructuredReturnGrid` populates after a successful FIRE; header label includes struct name (e.g. `"Return value (decoded — Vector):"`).

What's NOT done (deferred):
- **ObjectProperty / ClassProperty return resolution** to "Name (Class)". Pointer returns still show as 8-byte hex in the existing decode; resolving to UObject name needs a DLL pipe round-trip (`Ubel::GetName` on the returned address) — separate scope.
- **Recursive struct expansion**. `FHitResult.Location` (FVector) renders as one "Location (StructProperty)" row with the inner FVector showing as raw bytes; WalkFunctions only goes one level deep on `param.structFields` by design. Nested expansion needs recursive DLL-side discovery.

Tests: 1052 → 1065 (+13). New `StructReturnDecoderTests` covers CanDecode contract, FVector + FRotator decode shape, KnownStructLayouts-wins-over-StructFields precedence (locked by giving the same param both inputs with conflicting field lists), dynamic-fields fallback per-type decode, absolute offset surfacing, short-buffer tolerance (out-of-bounds reads degrade to "?" instead of throwing).

Verification target: Geri's `PlayerCameraManager::GetCameraLocation` returns FVector — grid should show 3 rows (X / Y / Z floats) at offsets 0x4 / 0x8 / 0xC of the post-call param buffer.

-----

## 2026-05-27 — NuGet packages bump + dotnet test migration to MTP mode (build 771)

User-driven NuGet bumps surfaced a .NET 10 + `Microsoft.Testing.Platform.MSBuild` 2.x compat break: the legacy VSTest bridge target was dropped on .NET 10 SDK, so the existing `dotnet test <proj>` invocation errored with "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later" (see https://aka.ms/dotnet-test-mtp-error).

Migration per the official upgrade path:

1. **New `global.json`** at repo root with:
   ```json
   { "test": { "runner": "Microsoft.Testing.Platform" } }
   ```
   switches `dotnet test` to MTP mode natively, replacing the VSTest bridge entirely.

2. **`build.ps1` invocation updated**: `--project <proj>` instead of legacy positional form (the latter silently downgrades to VSTest mode). Dropped `--nologo` / `-v minimal` since they aren't in MTP mode's allowed dotnet-test flag list — unknown flags get forwarded to xunit.v3, which prints help + exits 5.

3. **Dropped explicit pins** on `Microsoft.Testing.Platform.MSBuild` / `Microsoft.Testing.Extensions.Telemetry` / `Microsoft.Testing.Extensions.TrxReport.Abstractions`. xunit.v3 bundles its own MTP bridge (`Xunit.MicrosoftTestingPlatform.*`) compiled against a specific MTP API surface; pinning explicit 2.2.3 versions overrode xunit.v3's tested transitives and triggered `MissingMethodException` on `IOutputDevice.DisplayAsync` at test-run time. Let xunit.v3 resolve transitively.

NuGet bumps kept (all transitives surfaced as explicit pins by the user's IDE bump):
- Avalonia.Angle.Windows.Natives 2.1.27548.20260419
- HarfBuzzSharp.NativeAssets.* 8.3.1.5
- SkiaSharp + NativeAssets.* 3.119.4
- Tmds.DBus.Protocol 0.93.0 → 0.94.0
- Microsoft.NET.Test.Sdk 18.5.1 → 18.6.0
- Microsoft.Extensions.* / System.Memory.Data / System.Security.Cryptography.ProtectedData → 10.0.8
- Azure SDK chain transitives (Azure.Core 1.57.0 / OpenTelemetry / Microsoft.Identity.Client 4.84.1 etc.)

Tests: 1052 passing under MTP mode (no behaviour change — pure dependency bump + test runner mode migration).

**Lesson logged**: never explicitly pin packages that come in transitively via a test framework's own bridge. Let the framework own the version dance for its internal compatibility surface.

-----

## 2026-05-27 — Multi-row → One .CT batch generator (#3, build 760)

Polishes the existing single-row AA(Baked) / Freeze export into a multi-row batch on the **Interesting Functions** + **Interesting Properties** tabs. Promotes the discover→use workflow from "research toy" to "shareable cheat-table author":

1. Discover (existing flow: Load → score → filter → scan)
2. Select N relevant rows (Ctrl/Shift+click; DataGrid is now `SelectionMode="Extended"`)
3. Click 📦 Generate CT → save-dialog → one .CT with per-row AA Script entries, grouped by category

### Architecture

- `Models/CheatTableRow.cs` — discriminated row type (`CtPropertyRow` wraps `FreezeScriptParams`, `CtFunctionRow` wraps Baked params). Source-panel-agnostic so future call sites (LiveWalker mixed rows, Live PE Profiler hits) can feed the same builder without churn.
- `Services/CheatTableBuilder.cs` — assembles N rows into a `<CheatTable CheatEngineTableVersion="46">` XML matching CE's File→Save As shape. Root group → per-category sub-groups (alphabetical, Uncategorised trails) → one `<CheatEntry>` per row with `<VariableType>Auto Assembler Script</VariableType>` body. IDs sequential from `BaseId=1000`. XML escapes all five canonical entities so `TArray<int>` / `&` / quotes in descriptions can't break a CT load.
- Property rows reuse `FreezeScriptGenerator`; function rows reuse `BakedScriptGenerator`. No new generator code.
- VM stays IO-free — emits `RequestSaveCheatTable(defaultName, ctXml)` event; MainWindow owns the platform save-file dialog + UTF-8 write via the existing `SaveCheatTableAsync` helper.

### UX details

- **Property rows**: defaults to a per-UE-type "obvious cheat" freeze literal (Float = `9999.0`, Int = `99999`, Bool = `true`, Byte = `255`); user edits CFG.value in CE before activating. Struct / array / non-scalar rows are skipped (status: "Generated N entries (skipped K unsupported)"). Description includes the defining-class hint when it differs from the user-picked class.
- **Function rows**: BakedValues intentionally empty (helper zero-fills PARAMS); description for parameterised funcs reads `"Class::Func (N (XB)) — edit baked PARAMS in CE"` so users know to populate before activating. No-arg funcs read `"Class::Func()"`.
- Default filename: `{Source}-batch-yyyyMMdd-HHmmss.CT` where Source is `InterestingProperties` / `InterestingFunctions`.

Tests: 1028 → 1052 (+24). `CheatTableBuilderTests` covers structural shape (CheatTable root + nesting + per-row VariableType + UserdefinedSymbols), category alphabetical ordering + Uncategorised-trails-last, input order preservation within a category, 3-row / 10-row / mixed property+function selections, ID uniqueness + sequential allocation, empty/null rows throw, XML escaping for `TArray<int>` / `&` / quotes, DefaultFileName format + fallback, SanitizeFileName, VM mapping (BuildRowsFromSelection skip-unsupported counts, defining-class targetClass choice, per-type freeze literal theory).

Out of scope for v1: LiveWalker integration (heterogeneous row types — needs its own UX pass); AOBMaker direct-inject of the generated CT (currently save-to-disk + user opens in CE).

-----

## 2026-05-27 — `scripts/analysis/diff_dumps.py` — same-game patch diff at UProperty granularity (#4)

Pure-Python sister script to `analyze_dumps.py` — consumes the same `Dump All Metadata` JSONL corpus but does N=2 patch-vs-patch diff instead of N=many cross-game aggregation. Closes the cheat-table-maintainer pain: when a game ships a silent UPROPERTY offset shuffle, hand-coded tables break and the binary search for the new offset is hours of grinding.

Surface area:
- Class-level: Added / Removed / props_size delta
- Property-level: Added / Removed / **Moved** (same name, different offset/size) / **Type changed** (FloatProperty → DoubleProperty incl. inner_type / struct_type / obj_class / enum sub-changes)
- Function-level: Added / Removed / **Signature changed** (return_type / num_parms / parms_size / flags differ — bodies aren't in the dump so logic-only changes are invisible; documented limitation)

Match keys: classes by `path` (canonical UE id; `addr` is session-local and ignored). Path normalisation (`//Script/X` ≡ `/Script/X`). Properties + functions by `name` within class.

CLI:
```bash
python diff_dumps.py <old.jsonl> <new.jsonl>           # full report → stdout
python diff_dumps.py <old.jsonl> <new.jsonl> -o diff.md
python diff_dumps.py <old.jsonl> <new.jsonl> --minimal       # Moved + sig-changed only
python diff_dumps.py <old.jsonl> <new.jsonl> --include-engine
python diff_dumps.py --self-test                              # synthetic fixtures
```

`--self-test` runs 6 built-in synthetic-fixture scenarios (Added / Removed / Moved / TypeChanged / SignatureChanged paths, engine-class filter, path normalisation, self-diff identity, Markdown render edge cases) so the diff logic is checkable without external dumps.

Verified:
- Self-test all assertions pass
- Self-diff Geri vs Geri → 87 unchanged, 0 changes (identity property holds)
- Cross-game Geri vs ES2 → module-mismatch warning fires; 1652 added / 86 removed / 0 changed (doesn't crash on massive divergence)

README extended with the same-game-patch workflow + match-key + known limitations sections. No auto-rename detection (renamed field shows as Removed + Added); no function body comparison (only metadata is dumped — logic-only refactors invisible; covered by Live ProcessEvent Profiler in the future).

-----

## 2026-05-27 — Value Search Phase 2: FString / FName / FText + FVector / FRotator + TArray<T> (build 757)

Closes the v2-deferred list from `docs/todo.md` Section 0 — extends the build-738 numeric-primitives Value Search to the three deferred type families:

### Phase 2A — FString / FName / FText

- New ScanTypes `Contains` / `StartsWith` / `EndsWith` with CE-style case-insensitive default (ASCII fold; non-ASCII bytes compare bitwise). Opt-in case sensitivity exposed as a UI checkbox visible only for string DataTypes.
- `Ubel::ReadFStringAt` / `ReadFNameAt` / `ReadFTextStringAt` exposed publicly so Aura's scan path doesn't duplicate the FString-header decode + UTF-16 sanitize logic.
- FText is best-effort (cooked games strip most display strings — ES2 smoke test resolved 1/1551 classes, expected).

### Phase 2B — FVector / FRotator (component-wise tolerance)

- `CompareVectorPredicate` compares X / Y / Z per axis with shared tolerance. Exact / Bigger / Smaller / Between apply to all axes; Changed / Increased / Decreased trigger when ANY axis moves (matches in-game movement patterns).
- StructProperty inner-name match against `VectorStructNames` (`Vector` / `Vector3f` / `Rotator` / `Rotator3f` / NetQuantize variants) so we don't read 12 bytes from every UE struct.
- FTransform DataType reserved on the wire but mapped to no struct names — returns zero hits pending per-version Translation offset detection (UE4/UE5-non-LWC: +16, UE5 LWC: +32). Documented in `VectorStructNames()`.

### Phase 2C — TArray<T> with safety circuit-breaker

- `ScanField` gains `isArray` + `elemStride` + `elemTypeName`. `expandFields` emits `ArrayProperty` entries when Inner matches the wanted DataType (primitive / string / vector inner all supported).
- Per-instance loop branches on `isArray` and walks `TArray.Data`, emitting one candidate per match labelled `FieldName[N]`.
- **Soft circuit-breaker** per memory `project_value_search_caveats`: `Num > 10M` skips with `LOG_WARN`; `Num >= 0`, `Max >= Num`, `Data != null` guards. Macht's SEH-wrapped reads turn stale post-reallocation addresses into safe failures (candidate drops without crash).
- Refine re-reads strings via `c.addr` instead of `(instanceAddr, fieldOffset)` so array-element strings work uniformly with direct string fields.

### Wire-schema additions (all backward-compatible)

Fields are omitted in the common case so pre-Phase-2 traffic is byte-identical:
- `begin_value_scan` / `refine_value_scan` gain optional `case_sensitive` (attached only for string DataTypes when true).
- Existing `tolerance` now applies to FVector/FRotator too (component-wise per axis).
- DataType strings: `FString`, `FName`, `FText`, `FVector`, `FRotator`, `FTransform`.
- ScanType strings: `Contains`, `StartsWith`, `EndsWith`.

Pipe handlers in Fern.cpp gain `IsScanTypeValidFor` gating that rejects nonsensical combinations (`FString + Bigger`, `Int32 + Contains`, etc.) with an explicit error rather than letting the scan run and silently return 0 hits.

### Live verification (EVERSPACE 2, UE 5.5)

| DataType | ScanType | Value | Result |
|---|---|---|---|
| FString | Contains | "Engine" | 54 / 323ms ✓ |
| FName | Contains | "Engine" | 7119 / 415ms ✓ |
| FText | Contains | "Engine" | 1 / 396ms (cooked-build limitation) |
| FVector | Exact (CSV) tol=0.01 | 49966 / 303ms (hit max_results cap) |
| FRotator | Exact tol=0.01 | 16819 / 290ms ✓ |

No `LOG_WARN: skipping TArray with Num=` fired across ~1.15M scanned objects — ES2 has no pathological arrays.

### Tests

1081 → 1306 total (+225).
- DLL helpers: 124 → 247 (+123). ScanType partition for Contains/StartsWith/EndsWith; type-family predicates (`IsStringDataType` / `IsVectorDataType` / `IsSubstringScanType`); `IsScanTypeValidFor` matrix; `CompareStringPredicate` Exact/Substring/Changed/Unchanged; `CompareVectorPredicate` Exact/Ordering/Between/PrevValue/RejectsSubstring; `VectorStructNames` per-family content.
- C# tests: 957 → 1028 (+71 spread across this + tolerance work). `IsScanTypeValidFor` 27-row theory mirroring DLL contract; `VisibleScanTypeOptions` filtering per DataType; `SelectedScanType` auto-reset on incompatible-type switch; `SupportsCaseSensitive` / `SupportsTolerance` gating; wire-shape locks for `case_sensitive` (string-only) and `tolerance` (now also vector types).

-----

## 2026-05-26 — Value Search: Float/Double tolerance (CE-style rounded scan) (build 746)

User-requested follow-up after the build-744 fix landed TQ2 GAS scans. Game UIs commonly display attributes rounded to the nearest integer ("HP: 338") while the underlying float is something like 337.5. Without tolerance, a scan for "338" misses 337.5 and the user has to guess at decimal precision.

New `tolerance` parameter on `ValueScan::ComparePredicate` + `Aura::ScanForValue` + `Aura::RefineCandidates`, only meaningful for `Float`/`Double` (integer types ignore it -- exact comparison stays the default for non-floating types where tolerance semantics don't transfer cleanly). Per-ScanType behavior:

| ScanType | Tolerance semantic |
|---|---|
| Exact     | `\|cur - target\| <= tol`              (matches displayed-rounded values) |
| Bigger    | `cur > target + tol`                  (clearly above tolerance band) |
| Smaller   | `cur < target - tol` |
| Between   | `target1 - tol <= cur <= target2 + tol` (widen the inclusive range) |
| Changed   | `\|cur - prev\| > tol`                  (changed beyond noise) |
| Unchanged | `\|cur - prev\| <= tol` |
| Increased | `cur > prev + tol`                    (strictly above prev beyond noise) |
| Decreased | `cur < prev - tol` |

The `Float Exact tol=0.5` scan on the user's TQ2 repro example: target 338, candidates whose underlying float falls in `[337.5, 338.5]` match. The CurrentValue / BaseValue floats sitting at 337.5 get found whether the user types 337.5, 338, or 338.5 (with tol >= 0.5).

UI: new `± Tol` `NumericUpDown` (default 0.5) appears next to Value/Value2 when DataType is Float or Double. Hidden for integer types. `SupportsTolerance` VM property drives the visibility binding.

Wire shape: `tolerance` JSON field on `begin_value_scan` / `refine_value_scan` requests. **Omitted when the value is 0 or the type is integer** -- preserves byte-identical wire shape for existing exact-scan call sites and integer scans (back-compat with any external pipe consumer that might be sniffing the protocol). DLL defaults to 0 if absent.

Tests:
- DLL: +18 assertions (124 -> 142 dll_helpers) covering Float Exact within / outside band, tol=0 strict equality back-compat, Bigger / Smaller band shift, Unchanged within drift / Changed beyond drift, Increased / Decreased prev-value semantics, Between widening both bounds, Int32 / UInt64 integer types IGNORE non-zero tolerance.
- C#: +11 (957 -> 968) covering tolerance attached on the wire for Float, omitted for 5 integer types (theory), omitted when zero, refine_value_scan attaches tolerance, VM SupportsTolerance gates by DataType (Int -> false, Float/Double -> true, UInt64 -> false), tolerance pass-through Float / dropped Int through the VM.

-----

## 2026-05-26 — Value Search hotfix #2: WalkClassEx must calibrate FSTRUCTPROP_STRUCT before reading (build 744)

Build-740 recursion fix shipped but TQ2 live test STILL returned 0 candidates. Targeted DIAG logging on `class name contains "AttributeSet"` revealed the actual failure mode:

```
ValueScan DIAG: class 'GrimAttributeSetHealth' WalkClassEx fields=8:
  [1] name='MaximumHealth' type='StructProperty' offset=0xC8 size=37
      addr=0x1FB83F53800 structType=''   ← empty!
  ...
ValueScan DIAG: class 'GrimAttributeSetHealth' emitted 0 ScanFields
```

`structType=''` on EVERY StructProperty was the smoking gun. `ReadSubclassTypeName(f.Address + FSTRUCTPROP_STRUCT)` returned empty because the runtime `FSTRUCTPROP_STRUCT` was still at default `0x78` — TQ2 (UE 5.07) actually needs `0x74`. My nested-struct recursion uses the same offset, so it also bailed on every read.

Root cause: `CorrectSubclassOffsets` (Ubel.cpp:2441) is the calibration routine that probes the right offset, but it was **only called from `WalkInstance`** (line 2864). Any caller that hit `WalkClassEx` without a prior `WalkInstance` saw the uncalibrated default — including `Aura::ScanForValue`'s GObjects walk. The reason the bug was hidden in earlier features: `WalkInstance` typically fires from Live Walker before any other operation, so PropertySearch / etc. always inherited a calibrated value. Value Search is the first feature whose hot path doesn't depend on Live Walker firing first.

Fix: added `CorrectSubclassOffsets(info.Fields)` call at the top of `WalkClassEx`, just after `WalkClass`. The function is idempotent (atomic-guarded), so calling it on every WalkClassEx is a no-op after the first successful probe. Forward declaration added near the top of Ubel.cpp because the definition lives at line ~2441.

Same fix strengthens other `WalkClassEx` consumers (SearchProperties / EnumerateAllFunctions / SdkExport) — they'd also have shown empty `structType` / `innerType` if invoked before any WalkInstance, just hadn't triggered the bug because of LiveWalker-first usage patterns.

957 C# + 124 DLL tests still pass. TQ2 live re-verification pending.

-----

## 2026-05-26 — Value Search: recurse StructProperty so GAS / FGameplayAttributeData values are reachable (build 740 hotfix)

Live-game repro on TQ2 (UE 5.07) the same session as the shipping build
738 entry below: user opened CE Structure Dissect, walked `GWorld →
OwningGameInstance → LocalPlayers[0] → PlayerController → Character →
m_pStatsComponent → m_pAttributeSetHealth → MaximumHealth
(GameplayAttributeData) → BaseValue = 337.5`. Opened Value Search,
Type=Float, Scan=Exact, Value=337.5 → **0 candidates in 743 ms
(scanned 380748 objects, 1717 classes with matching fields)**.

Root cause: `Aura::ScanForValue::buildClassIndex` walked only
top-level `ClassInfo.Fields` entries. `BaseValue` / `CurrentValue` live
inside `FGameplayAttributeData` — a USTRUCT used as a StructProperty
member of `UAttributeSetHealth`. The scan saw `MaximumHealth` as a
StructProperty (not a leaf float type) and dropped it. Same dead-end
would apply to FVector / FRotator / FTransform members of any UObject,
not just GAS-style attribute sets.

Fix: replaced the linear field loop with a recursive `expandFields`
lambda. For each StructProperty encountered, reads `FSTRUCTPROP_STRUCT`
to resolve the inner `UScriptStruct*` and recurses with cumulative
offset + dotted name prefix. Emits ScanField entries for every
matching-type leaf at the correct cumulative offset. Cycle guard via
visited-set; hard depth cap at 4 to bound worst-case CPU on
pathological types (self-referencing USTRUCTs declared as linked
nodes).

```
Before fix (TQ2 repro):
  UAttributeSetHealth.MaximumHealth → StructProperty → skipped
  → 0 candidates

After fix:
  UAttributeSetHealth.MaximumHealth → recurse FGameplayAttributeData
    → MaximumHealth.BaseValue    @ +0x48 → leaf, emitted
    → MaximumHealth.CurrentValue @ +0x4C → leaf, emitted
  → 2+ candidates per AttributeSetHealth instance
```

Uses `Ubel::WalkClassEx` at every depth so BoolProperty FieldMask is
populated for nested bitfield bools on the UE5 FProperty path
(WalkClass alone covers UE4 UProperty path only).

Container properties (Array / Map / Set / Optional) are intentionally
NOT recursed — TArray\<T\> remains v2 gated by the crash-risk plan in
`project_value_search_caveats` memory. StructProperty recursion is
strictly safe: no allocation walk, no Num-bounded iteration.

No new test surface — recursion is implementation-detail inside the
scan lambda; visible only through end-to-end scan results on a live
UE process. Existing predicate / session / parser tests unchanged
(957 C# + 124 DLL still pass on build 740). TQ2 live re-verification
is the contract test.

-----

## 2026-05-26 — Value Search tab: CE-style First Scan / Next Scan over UPROPERTY fields (build 738)

New end-to-end capability: given a value (int / float / bool / etc),
walk every UPROPERTY-declared field of every UObject instance and
return the addresses + class + field metadata for each match. Refines
with the standard CE Next-Scan predicates (Exact / Bigger / Smaller /
Between + Changed / Unchanged / Increased / Decreased) inside a
DLL-side session. Fills the long-standing search-by-value gap —
PropertySearch was search-by-name; InstanceFinder was search-by-address;
this is the third axis. Port of discrete's Phase 27b
[ValueScanSession](../../discrete/dll/src/shared/ValueScanSession.h)
shape with UE-specific scan engine.

Cross-repo motivation: discussed at session start whether the Unity-
side feature (D:\Github\discrete `whatIsAt` + `beginValueScan`) was
worth porting to UE5CEDumper. Verdict: better suited here than there
because UE's reflection metadata is more uniform than IL2CPP's mix of
typed instances + raw native arrays, and the FindByAddress / GObjects-
walk infrastructure for enriching candidates was already mature.

### Architecture

```
UI Value Search tab
  ↓ begin_value_scan { data_type, scan_type, value, value2?, game_only, max_results }
DLL Aura::ScanForValue
  ↓ walks GObjects
  ↓ skips UClass meta-objects (IsClassLikeMeta filter)
  ↓ per-class field index cached lazily via Ubel::WalkClassEx, filtered to
     fields whose TypeName matches the requested DataType
  ↓ typed-read instance+offset bytes, apply ComparePredicate
  ↓ on hit: lookup FindDefiningClass (cached), build ValueScan::Candidate
ValueScan::SessionManager::Begin → sessionId
  ↑ candidates echoed back to UI

UI Next Scan
  ↓ refine_value_scan { session_id, scan_type, value?, value2? }
DLL Aura::RefineCandidates
  ↓ re-reads each candidate's bytes
  ↓ predicate compares to user value (Exact/etc) or candidate.prevValue
     (Changed / Unchanged / Increased / Decreased)
  ↓ prunes failing, updates prevValue on survivors
  ↑ surviving candidates returned

UI New Scan
  ↓ end_value_scan { session_id }
ValueScan::SessionManager::End drops the session.
Sessions auto-expire at 5 min idle so abandoned sessions clear lazily.
```

### MVP scope (build 737)

- **Types**: Int8/16/32/64, UInt8/16/32/64, Float, Double, Bool.
  BoolProperty bitfields normalized to 0/1 via FieldMask so refine
  predicates see stable boolean semantics across sibling-bit flips.
- **Scan candidate source**: GObjects → UProperty fields only.
  Deliberately NOT raw memory scan — the UE precedent is better than
  discrete's because UProperty metadata gives lossless typing and the
  raw-memory false-positive problem is sidestepped entirely.
- **Scan deadline**: 15s; `deadline_hit` surfaces in the response so
  the UI can show "scan truncated — narrow predicate" instead of
  silently returning a partial set.
- **Hard-locked UX contract**: Value Search tab MUST surface a banner
  reading "Native C++ fields (non-UPROPERTY) cannot be found here — use
  Cheat Engine's raw memory scan for those." Locked in by a literal-text
  test (`ValueSearchTests.Banner_LiteralText_IsPresentInEnAxaml`,
  `Banner_IsReferencedByValueSearchPanel`). Rationale: scan walks
  UProperty reflection metadata, so non-reflected C++ fields (private
  members not declared UPROPERTY, or fields inside non-UObject native
  structs embedded in a UObject) are invisible. Without the banner the
  user would assume "value not found" means "value isn't there" rather
  than "this tab can't see it" — a silent failure mode of the worst
  kind.

### Deferred (v2)

- **FString / FName / FText / TArray\<T\> / FVector / FQuat / FTransform**
  — UE-specific types where typed read is non-trivial.
- **TArray scan**: see memory `project_value_search_caveats` for the
  open risk. Existing Copy CE XML / CSX / SDK Header exports apply an
  array size cap to keep payloads bounded; the value scan must NOT
  inherit that cap (a hit at index 50000 of a TArray\<int32\> is still a
  legit hit). Concern: removing the cap may risk crash / hang on
  pathological containers (malformed Num, freed slack, OptionalProperty
  mis-decoded as TArray). Mitigation plan: soft circuit-breaker on Num
  (>10M elements skip with telemetry log) rather than a hard cap; verify
  `Aura::FindInContainers`'s 15s deadline is enough back-pressure;
  stress-test on Satisfactory inventory arrays before shipping.
- **Native C++ field scan**: explicitly excluded; banner directs user
  to CE for those (intended behaviour, not a future task).

### DLL — 3 new files + 3 new pipe cmds

- `dll/src/ValueScan.h` / `.cpp` — DataType / ScanType enums, Candidate
  struct, SessionManager (singleton, 5-min idle expiry), ComparePredicate
  (typed-load + ordered predicate for int64/uint64/double). Heap-leaked
  singleton matches discrete's precedent so DLL teardown doesn't
  destructor-storm tens of thousands of candidates.
- `dll/src/Aura.h` / `.cpp` — adds `ScanForValue` (GObjects walk +
  per-class field index + FindDefiningClass cache) and
  `RefineCandidates` (re-read + prune + prevValue update).
- `dll/src/Renge.h` — `CMD_BEGIN_VALUE_SCAN` / `CMD_REFINE_VALUE_SCAN` /
  `CMD_END_VALUE_SCAN` constants; **pipe cmds now 39** (+3).
- `dll/src/Fern.cpp` — 3 new handlers + `ParseValueBytes` (string →
  LE bytes per DataType, with 0x-hex prefix support for unsigned ints)
  + `FormatValueBytes` (inverse) + `CandidateToJson` helpers.

### Tests

- **DLL** (`dll/tests/dll_helpers_test.cpp`): +31 new assertions across
  `Test_ValueScan_DataTypeSizes`, `Test_ValueScan_ParseDataTypeRoundTrip`,
  `Test_ValueScan_ScanTypePartitioning`, `Test_ValueScan_Predicate_Int32`,
  `Test_ValueScan_Predicate_Int8Negative` (signed-extension regression
  guard), `Test_ValueScan_Predicate_Float`, `_Double`, `_Bool`,
  `_UInt64_RangeBoundary` (ensures unsigned path on 0xFFFF...
  values that would be negative as signed), `Test_ValueScan_SessionLifecycle`
  (Begin → ViewWith → RefineWith mutation → End → missing-session
  contract). DLL test suite **93 → 124** (utf8 31 + dll-helpers 93).
- **C#** (`ui/UE5DumpUI.Tests/ValueSearchTests.cs`): +22 tests including
  service-level JSON round-trips, scan-type partition theory (8
  predicates × 2 buckets = 16 assertions), VM workflow contract
  (First Scan rejects prev-value scan types; Between requires Value2;
  Next Scan with prev-value type omits `value` field; New Scan ends
  session + clears candidates; First Scan auto-ends orphan session),
  and the two banner-literal-text tests that lock the UX rule.
  C# total **935 → 957**.

### C# UI — new files

- `Models/ValueScanModels.cs` — `ValueScanDataType`, `ValueScanType`,
  `ValueCandidate`, `ValueScanBeginResult`, `ValueScanRefineResult`.
- `Services/DumpService.cs` + `Core/IDumpService.cs` — adds
  `BeginValueScanAsync`, `RefineValueScanAsync`, `EndValueScanAsync`
  + shared `ParseValueCandidate` JSON helper.
- `ViewModels/ValueSearchViewModel.cs` — DataType / ScanType selectors,
  Value/Value2 inputs (visibility-bound to scan type), First Scan /
  Next Scan / New Scan commands, NavigateToInstance event for
  cross-tab "Open in Live Walker".
- `Views/ValueSearchPanel.axaml` + `.axaml.cs` — top banner (warm-amber
  styled, locked by test), inputs row, status row, DataGrid of
  candidates with Class.Field / Type / Value / Offset / Addr / Instance
  columns + per-row Open / Copy buttons.
- `Views/MainWindow.axaml` — new tab between Interesting Props and
  Console (header `str.Tab.ValueSearch` = "Value Search").
- `ViewModels/MainWindowViewModel.cs` — wires `ValueSearch` child VM +
  navigation + clipboard events.
- `Resources/Strings/en.axaml` — 16 new string keys (banner + labels +
  tooltips).

### Workflow (golden path)

1. Open Value Search tab → see banner explicitly stating native-field
   limitation.
2. Pick DataType (e.g. Int32), ScanType=Exact, type the value you're
   looking for (e.g. current HP = 100), click **First Scan**.
3. DLL walks GObjects + matching-type UProperties, returns N candidates
   in seconds.
4. Take damage in-game (HP drops to 75), switch ScanType=Decreased,
   click **Next Scan** → candidates pruned to fields that dropped.
5. Repeat with Changed / Unchanged / Decreased until candidate count
   drops to a single-digit list.
6. Click **Open in Live Walker** on a candidate → cross-tab navigation
   opens the owning instance with the field highlighted. Or click
   **Copy Address** to send the address straight to CE.

### Open items (next session — see todo.md)

- Live-game verification on Geri (UE 4.27) + ES2 (UE 5.5): scan for
  HP, take damage, refine. End-to-end smoke test before declaring
  the feature stable on the broader 18-game corpus.
- v2 type expansion (FString first as the easiest; TArray gated by
  the crash-risk plan above).
- Optional UX polish: keyboard shortcut for First/Next Scan; "Add to
  Watch List" right-click action.

-----

## 2026-05-24 — Property freeze (Route B): horizontal lock across all class instances (build 719)

New end-to-end capability: given a property surfaced by **PropertySearch**, generate an AA Script that holds the value at a constant across **every live instance** of the owning class, with automatic instance re-enumeration on a timer so respawns / new spawns / destroys are handled transparently. Sister capability to the existing CE-XML pointer-chain export (Route A, kept in [todo.md → Speculative](todo.md)) — fundamental difference: CE XML pins ONE pointer chain to ONE instance; the freeze script tracks a property by **class + offset + type** and writes to every live instance every tick.

### Architecture

```
PropertySearch row
  → [Freeze] button (grayed when AOBMaker plugin not detected)
  → FreezeValueDialog (single input, type-validated)
  → FreezeScriptGenerator.Generate(...)
  → AOBMaker CreateAAScriptAsync — script lands in CE's address list

Generated AA Script [ENABLE]
  → findTableFile('ue5_freeze_helper.lua') → load() the helper
  → freezeProperty(cfg) → handle
  → handle.start() → createTimer(50ms tick) + createTimer(5s rescan)

Tick (every 50ms): for each cached instance addr → writer(addr + offset, value)
Rescan (every 5s): CMD_LIST_INSTANCES → refresh cache

[DISABLE]: handle.stop() → destroys both timers, clears cache
```

### DLL — CMD_LIST_INSTANCES = 6 ([Mimic.h](../dll/src/Mimic.h), [Mimic.cpp](../dll/src/Mimic.cpp))

New mailbox cmd that paginates **live (non-CDO) UObject* pointers** of a class. Match policy: `exactMatch=true` — partial matching would have `"Pawn"` pull every pawn subclass in the world and the property offset only makes sense for the exact class chain PropertySearch identified. Hard cap 2000 instances, 128 ptrs per page (8 bytes each = exactly 1024 bytes paramsData). Output mirrors the LIST_FUNCTIONS shape: `parmsSize=total`, `numParms=this page`, `functionFlags=total pages`.

Reuses Aura's existing `FindInstancesByClass`; CDO filter (`name contains "Default__"`) drops template objects.

### Lua — `scripts/ue5_freeze_helper.lua` (new, ~340 lines incl. 5 commented samples)

- Public API: `freezeProperty(cfg) → handle` with `handle.start()` / `handle.stop()`
- `cfg` fields: `className`, `propOffset`, `valueType`, `value`, `tickIntervalMs` (default 50), `refreshIntervalSec` (default 5), `filter` (optional `fn(addr) → bool`)
- Type writers cover bool + int8/uint8 + int16/uint16 + int32/uint32 + int64/uint64 + float + double (aliases: byte/sbyte/word/dword/qword/int/long/boolean)
- Shares `_ue5_invoke_busy` reentrancy flag with `ue5_invoke_helper.lua` — neither helper touches the mailbox while the other is mid-call
- Tick has a vtable-null liveness guard so a freed instance between rescans doesn't write to recycled memory
- 5 commented samples in the file header: basic teammate HP, god mode bool, filter-out-local-player, multi-property freeze in one script, how to edit CFG after generation

Bundled as an `<EmbeddedResource>` in `UE5DumpUI.csproj` so the UI can ship it to disk or inject it into the CE table via AOBMaker.

### C# — Models / Services / Views

| File | Purpose |
|---|---|
| `Models/FreezeScriptParams.cs` | DTO: ClassName, PropertyName, PropertyOffset, UeTypeName, ValueLiteral |
| `Services/FreezeScriptGenerator.cs` | Renders the AA Script; per-script keyed handle table (`_ue5_freeze_handles[KEY]`) so multiple Freeze scripts coexist without clobbering each other's globals |
| `Services/FreezeHelperLuaResource.cs` | Embedded-resource accessor (mirrors `HelperLuaResource`) |
| `Views/FreezeValueDialog.cs` | Single-input modal with read-only target details + type-aware validation (`ValidateAndConvert`); accepts bool as `true/false/1/0` (case insensitive) |
| `ViewModels/PropertySearchViewModel.cs` | New `CopyFreezeScriptCommand`, `IsAobMakerAvailable` flag + `FreezeUnavailableTooltip`, `RefreshAobMakerAvailabilityAsync` with 5s cooldown |
| `Views/PropertySearchPanel.axaml` + `.axaml.cs` | New **Freeze** button per row; `DataContextChanged` wires `FreezeValuePrompt` callback so the VM stays View-free |
| `ViewModels/MainWindowViewModel.cs` | New `InjectFreezeHelperLuaCommand` + `ExportFreezeHelperLuaCommand` mirroring the existing invoke-helper Tools entries |
| `Views/MainWindow.axaml` + `Resources/Strings/en.axaml` | Tools menu gains two entries with a `<Separator/>` from the invoke helper pair; 3 new strings + 3 new tooltips |

### Gating — no clipboard fallback, AOBMaker required

Decided per user request to keep the surface tight: the Freeze button is **disabled** when the AOBMaker bridge can't reach CE, with a tooltip explaining the setup. No copy-paste fallback path (would duplicate the helper-loader chrome and split the docs). The AOBMaker plugin's existing `CreateAAScriptAsync` (used by the Pipe Invoke / AA(Baked) flow since build 590) delivers the script directly into CE's address list.

### Per-script handle key (subtle correctness fix during dev)

Initial generator used a single global `_freezeHandle` — would have been clobbered by a second active Freeze script. Switched to `_ue5_freeze_handles[KEY]` table keyed by `ClassName::PropName@0xOffset`. Deterministic key so re-enabling the same script reuses the same slot; defensive stop in [ENABLE] catches the rare "AA Script reload while active" case.

### Tests — +47 tests (target file path includes both the helper resource sanity check and the wiring)

- `FreezeScriptGeneratorTests.cs` — type mapping (12 known + 6 unsupported), Lua escaping (6 cases), generated script section structure (5 facts incl. defining-class preference, hex offset render, helper resource read)
- `FreezeValueDialogValidationTests.cs` — bool / float / double / signed-int / unsigned-int / unsupported (20 cases via theory + 4 facts)
- `PropertySearchFreezeTests.cs` — 7 gating + happy-path scenarios + 1 tooltip test (no-bridge / unavailable / unsupported-type / cancel / happy / defining-class preferred / rejected / tooltip flag)

Final total: 920 C# xunit + 64 dll_helpers + 31 utf8_helpers = **1015 tests**, all green.

### What changed in MEMORY.md

Test count bumped 786 → 1015; tested-games / capability-matrix entries remain valid (freeze is additive, no regressions to existing flows).

### Not done in this round (Route A still on the table)

- **Live-game verification**: needs a UE 4.x or 5.x cooked game with a teammate-style property to confirm the rescan cadence + tick writer doesn't disturb gameplay. Smoke-tested unit-level; first live test should be a single-player game with respawning NPCs (e.g. Geri) where a respawn-induced cache refresh is observable.
- **Bitfield bool detection**: the helper writes a full byte for `bool`, which is wrong for packed bitfield bools (`uint8 bFoo : 1`). PropertySearch doesn't currently surface bitfield mask metadata so we can't gate the button accordingly — deferred until a user hits this.
- **FString / FName / struct field freeze**: out of v1 scope per user (numerics + bool first).
- **Route A polish**: existing CE-XML export already handles single-pointer-chain freeze; the [todo Speculative entry](todo.md) documents it as the "static singleton manager" option.

-----

## 2026-05-20 (PR #199 merged dev → main) — Mailbox poll 10ms→1ms + Invoke param picker Stage 1+2

Three-shipment session on top of the build-696 close-out. Total: 4 commits (build 707 → 715), all pushed to dev, then dev merged into main via PR #199 as a fast-forward (30 commits caught up — first dev→main merge since build 590).

### A. Mailbox poll latency cut (build 707-710, [74db6b5](https://github.com/bbfox0703/UE5CEDumper/commit/74db6b5))

CE Lua's `invokeUFunction` blocks on a status-flag flip driven by Mimic's polling thread. The historic `Sleep(10)` between iterations added ~5ms avg of pure idle wait per invoke — so a tight Lua loop of N invokes used to burn ~N×5ms in the polling loop alone. Lowered to `Sleep(kPollIntervalMs=1)` with a `timeBeginPeriod(1)` / `timeEndPeriod(1)` bracket so Sleep(1) reliably delivers ~1-2ms regardless of host timer state (legacy 15.6ms tick would otherwise defeat the win on idle/server SKUs). Win10 2004+ scopes timeBeginPeriod per-process; no global cost.

Added `Test_Mimic_PollLatency_OneMillisecond` to `dll_helpers_test`: brackets the same timeBeginPeriod pair, asserts 100×Sleep(1) lands under 300ms. Observed 188-194ms on the dev machine (~1.9ms/sleep) — 5× under the ~1560ms a legacy-tick regression would produce. `Winmm` linked into main DLL + both proxy DLLs + the test exe.

The Stark queue (game-thread FIFO with per-request promises) and the UE ProcessEvent throughput itself are unaffected — those are fundamental constraints. This change only kills the mailbox-side idle wait, which was the dominant latency layer for sequential CE-Lua-driven invokes.

### B. Invoke param Stage 1 — surface UObject* expected UClass (build 711, [024b6fd](https://github.com/bbfox0703/UE5CEDumper/commit/024b6fd))

Pain point: invoking a UFunction with a UObject*/UClass*/Soft*/Weak*/Lazy*/Interface parameter, the user had no idea what type was actually expected — the DLL had the info (`FObjectPropertyBase::PropertyClass`) but threw it away when walking function params. The InvokeParamDialog label just said `[UObject*, 8B]` with no class hint, leaving the user to guess or grep the SDK header.

This is Stage 1 of a 3-stage plan to make invoking pointer params tractable. Stage 2 (instance picker) and Stage 3 (class validation) build on the metadata exposed here.

- **DLL**: `Ubel.h::FunctionParam` gains `objClassName` field (mirrors `FieldInfo`). `Ubel.cpp::WalkFunctions` extracts `PropertyClass` for the 7 pointer-flavoured types on both UE5/4.25+ (via `ReadSubclassTypeName`) and UE4 <4.25 (via `UPROPERTY_OFFSET+0x2C` — same delta the StructProperty path uses, since both derived types put their first member at the same subclass slot). `Fern.cpp` walk_functions JSON adds optional `"obj_class"` key alongside `"struct_type"`.
- **C#**: `FunctionParamModel.ObjectClassName` (default "" for backward compat). `DumpService` parses `obj_class`. `InvokeParamDialog` + `InvokeScriptGenerator` labels become `[UObject*: AActor, 8B, off=0x10]` when the class is known, fall back cleanly to the original form when empty.
- **Tests**: 2 new (with-class / without-class label format).

### C. Invoke param Stage 2 — instance picker dialog (build 715, [515a344](https://github.com/bbfox0703/UE5CEDumper/commit/515a344))

When InvokeParamDialog renders a pointer-flavoured param, the row now grows three buttons after the textbox:

```
[param-name]  [type, classHint, NB]  [textbox]  [Pick…] [null] [self]
```

- **[Pick…]**: opens new `ObjectInstancePickerDialog` pre-filtered to the param's expected UClass (from Stage 1). Substring-match default catches subclasses (which is what an ObjectProperty actually accepts). Double-click row OR "Use selected" → textbox fills with chosen address. Cancel leaves textbox alone. Greyed when `ObjectClassName` is empty (older DLL or genuinely unconstrained param — user can still type address by hand).
- **[null]**: fills `0x0` for optional pointer params (WorldContextObject, etc.).
- **[self]**: fills invoke target's own address — for utility functions that re-target themselves. Disabled when no target instance (definition-only views).

Zero DLL change: picker reuses the build-547 `find_instances` pipe command (InstanceFinder has used it for nearly 200 builds). Picker dialog mirrors InvokeParamDialog's code-behind style — no XAML, no CompiledBinding, AOT-safe.

`ParamBufferBuilder.IsPickablePointerType` is the canonical list of the 7 pointer types — 7 positive + 14 negative test theories lock the DLL↔UI contract so a future type drift breaks at compile time.

### Files / counts

- New: `ui/UE5DumpUI/Views/ObjectInstancePickerDialog.cs` (260 lines)
- Modified: `dll/src/Ubel.h`, `dll/src/Ubel.cpp`, `dll/src/Fern.cpp`, `dll/src/Mimic.cpp`, `dll/CMakeLists.txt`, `dll/tests/dll_helpers_test.cpp`, `ui/UE5DumpUI/Models/FunctionInfoModel.cs`, `ui/UE5DumpUI/Services/DumpService.cs`, `ui/UE5DumpUI/Services/ParamBufferBuilder.cs`, `ui/UE5DumpUI/Services/InvokeScriptGenerator.cs`, `ui/UE5DumpUI/Views/InvokeParamDialog.cs`, `ui/UE5DumpUI.Tests/InvokeScriptTests.cs`, `ui/UE5DumpUI.Tests/ParamBufferBuilderTests.cs`

Tests: 910 → **935** (DLL self-tests 93 → 95 +mailbox latency; C# 817 → 840 +2 Stage-1 label tests +21 IsPickablePointerType theories). Build 715 / dist still 704 (no Publish rebuild this session).

### What's still open

- **Stage 3 (class validation)** — explicitly deferred to "real crash drives it" per Stage 2 close-out conversation. DLL would gain `validate_object_class(addr, expectedClassName)`; UI would warn (not block) on mismatch before invoking. Picker output is almost always class-correct in practice.
- **`walk_functions_batch`** — sister to `walk_class_batch`, still on the next-session bench.
- **FString / FText / TArray input for baked AA Script** — still open since build 643-644 ES2 verification.

-----

## 2026-05-20 (dev branch, docs only) — 18-game bias recheck (Frontiers added — first MMO/ARPG-flavoured dump)

User added one new dump (`Frontiers-Win64-Shipping.exe`, UE 4.26,
107,872 objects, 1,310 game classes / BPGCs) bringing the corpus to
**18 games**. Tool / workflow unchanged from the 17-game refresh
([4f50ea0](https://github.com/bbfox0703/UE5CEDumper/commit/4f50ea0)):
same `python scripts/analysis/analyze_dumps.py work/dump/*.jsonl
--min-games 3` run, same ad-hoc drill-down snippets reusing the
analyzer's `load_dump` / `tokenize` / `_resolve_own_props` helpers.

### Genre signature: predicted "out-of-genre" target

Characteristic class names (`TL_*` asset prefix, `BossMonster*`,
`BossFightObserver*`, `Pet*`, `Affix*`, `Dungeon*`, `Sharpshooter`,
`Captain`, `Cursed` archetypes) point to a Korean-MMO / ARPG-style
title — exactly the genre family
[docs/todo.md → More dumps for genre coverage](todo.md) flagged as
the only kind that could move the calibration needle further. The
17-game corpus was heavy on JRPG / sim / action-adventure / sandbox /
racing; this is the first MMO/ARPG-flavoured entry.

### Bias verdict — tables still stable

Token-by-token drill-down on every candidate with ≥3-game support:

- **`skill`** (8 / 18 games, 90 classes) looked promising at first but
  per-name inspection showed ~85% UI-widget noise: `SkillList`,
  `SkillIcon`, `Txt_SkillName`, `Img_SkillIcon`, `Pnl_SkillName_Mask`,
  `Hrz_SkillName`. Genuine cheat-tunable hits
  (`CurrentSkillPoints`, `SkillPointsRequired`, `IsSkillPurchased`)
  are buried under ~15% of the surface. Adding `Skill` to
  PropertyScoringTable would over-fire on UI properties. Function-side
  `Skill` keyword in CombatKeywords remains correct since function
  names like `UseSkill` / `LearnSkill` are unambiguously action verbs.
- **`effects`** (7 / 18), **`aura`** (4 / 18), **`gameplay`** (4 / 18),
  **`requirements`** (3 / 18), **`expiration`** (3 / 18), **`tags`**
  (5 / 18) — all >95% TQ2-skewed (TQ2 contributes 477-905 of each
  token's hit count; other games combined are single-to-low-
  double-digit). Single-game spikes, not cross-game signal.
- **Frontiers-unique tokens** (`affix`, `pet`, `dungeon`, `captain`,
  `sharpshooter`, `cursed`) all concentrated in Frontiers alone. Same
  single-game-spike rejection rule as the 17-game pass.
- **Class-side candidates** (Frontiers top class-x-prop pairs) are all
  UI-flavoured (`credits→credit`, `widget→item`, `bar→resource`,
  `dungeon→level`); none generalise across the corpus.

**No keyword additions, no class-rule additions, no scoring weight
changes.** Second consecutive bias recheck confirming the build-678 /
687 calibration generalises to genre-adjacent AND genuinely out-of-
genre unseen titles.

### Why this is stronger evidence than the 17-game pass

The 17-game recheck added two same-family titles (Star Wars Jedi:
Fallen Order — EA action-adventure; Ghostwire: Tokyo — Tango action-
horror). Both reinforced existing patterns, but the prediction was
that they wouldn't move the needle because they sit in already-well-
represented genres.

This 18-game pass added a predicted-to-be-different-genre title
(MMO-flavoured ARPG, never previously represented). The prediction was
that out-of-genre dumps would surface new vocabulary. The data says
the build-678 / 687 calibration **also covers MMO/ARPG vocabulary**
without any new keyword. That's stronger evidence for table robustness
than two more same-genre dumps would have been.

### Genres still completely absent from the corpus

MMO/ARPG is now (partially) represented. Still missing:

- Pure horror (no Resident Evil / Silent Hill style — only action-horror
  hybrids like GWT / Hogwarts dark sequences)
- Fighting (Tekken / Street Fighter / Mortal Kombat)
- RTS (Age of Empires / StarCraft / Company of Heroes)
- Sports-sim (FIFA / NBA 2K / car-tuning sims)

A dump from any of these would test calibration against vocabulary
genuinely outside the current action-adventure / RPG / sim / shooter /
ARPG neighbourhood.

### Files touched

Docs only. No code, no tests, no scoring tables. The Frontiers dump +
regenerated `work/dump/analysis-report-18games.md` live under `work/`
which is gitignored.

-----

## Older entries (builds 547-696) → archive

Pre-build-700 milestones (2026-05-09 → 2026-05-12) are in
[archive/dev-log-2026-05-pre-build-700.md](archive/dev-log-2026-05-pre-build-700.md)
— grep `^## ` there for the older index.
