# Archived todo units — closed 2026-11-20 (build 3776)

Units lifted **byte-identical** out of [`../todo.md`](../todo.md) on 2026-11-20, when the maintainer asked for the
oversized docs to be checked for archiving. `todo.md` went from **1,628,918 bytes / 13,318 lines** to **1,484,527
bytes / 11,889 lines** (pointer blocks included); this file holds the **1,481 moved lines (151,541 bytes)** of **61
units**: 41 sections and 20 table rows.

⛔ **Kept byte-identical on purpose — do not repair anything in here**, including the relative links, which were
written relative to `docs/` and are therefore wrong from this folder. [`README.md`](README.md) says how to read them.

**How these were selected.** The rule of the 2026-09-03, 2026-09-26 and 2026-10-28 passes
(`todo-closed-2026-10-28-build-3661.md` states it in full), unit by unit and, in the tables, row by row: the unit
announces its own closure; nothing in it says work is still owed (a hit about a different item does not block when
that item is closed or has its own live row); it is not a pointer stub, standing warning, policy text or a table's
header; no gate counts or needs it; a `[FIXPASS-2026-09-10]` row moves only when its fix shipped and its live check
passed or is explicitly not owed; a BLANK-sweep write-up only when every finding it raised is closed or has its own
row. **This time:** an open item listed in the `[LIVEPASS-2026-11-20]` section's lists counts as having its own live
row, as one listed in `[OPEN-CENSUS-2026-10-28]` already did. **The census itself did not move**, by the same rule
4 as before: its Table A rows are "fixed in place" by its own charter, so archiving its closed rows is a change to
that charter, which is the maintainer's call. Five proposers, one per region, and an adversarial verifier per region
(workflow `wf_b5b94773-aa0`); the verifiers sent back `[P4-CONTAINER-BASE]` (its closing bullets record two
pre-existing conditions "so they are not re-raised", found nowhere else) and `[A1-DETECT-REPUBLISH]` (a standing trap
for the still-open July L18 item that census Table D lists). The scripts are in `tools/docs_archive/` from this pass
on: `move_units.py` asserted the interleave before writing, and `verify_split.py` re-checked both files from git's
original afterwards.

**What moved**, by section of `todo.md`:

| from `todo.md` | moved |
|---|---|
| the 2026-11-04 en.axaml readiness request `[I18N-READINESS-2026-11-04]` | its P3 row |
| `[AOBMAKER-EVAL-2026-09-29]` | `[DENKEN-WRITE-SITES]` |
| vendor audit #9 `[VENDOR-AUDIT9-2026-11-05]` | `[VND9-DOC]`, `[SYNCTOOLS-FALSE-FETCH-FAILED]` |
| THE SECOND BLANK `[BLANK-AUG-PLAN-2026-09-10]` | 39 closed finding sections of the P4 / P5 pattern sweep and the A1-A4 area sweeps, and 16 `[FIXPASS-2026-09-10]` table rows (2 ledger, 2 live-check backlog -- L26 and L58 -- and 12 raised findings, most closed live by `[LIVEPASS-2026-11-20]`) |
| `[A4-ASSESS-2026-09-09]` | its fix pass round 2 (rows 4, 6, 7, 8) |
| `[D4B-SITE6-2026-09-09]` | the whole section |

-----


| # | Severity | Problem | Fix |
|---|---|---|---|
| P3 | ✅ **DONE 2026-11-05** (was HIGH) | ✅ red `a42adbcc`; green: the commit that marks this row. **ProxyDeploy:** `DescribeUnreadable` and `DetailNamesUnreadable` share one template (`ProxyDeployService.UnreadableTemplate` + `UnreadableNameSeparator`); the parse splits the writer's own template at its slot, so a translation changes both at once (English behaviour unchanged; the existing round-trip test passes). Keeping the names on the row instead was set aside: `StatusDetail` is written, cleared, preserved and appended in about ten places, and a parallel list missed by one of them would bring the doubled note back. **Live Walker:** no exposure today -- every container-view crumb records its `FieldName`, so the CE XML export's `" ["` strip runs only for a crumb without one; `EveryContainerViewCrumb_RecordsItsFieldName` pins that (mutation-checked). C# 7462 passed, 0 failed (red: 1). Was: **Display text parsed back by logic.** `ProxyDeployService.DescribeUnreadable` writes "Cannot read … here (…", and `DetailNamesUnreadable` parses it back with `Head = "Cannot read "`. The result decides whether `ProxyDeployViewModel` names the files again. Translate the sentence and the parse stops matching, which brings back the doubled note that parse was written to prevent. Live Walker's breadcrumb label is also read back. | Keep the data on the row (the unreadable names; the field name and descriptor), not the sentence. |

| id | sev | status | what | where |
|---|---|---|---|---|
| `[DENKEN-WRITE-SITES]` | LOW | ✅ **FIXED 2026-11-17** (the maintainer asked for it that day; builds 3731-3732). Denken keeps, per offset, the address of each instruction that WRITES it -- once, through-`this` first and counted (`writeSitesViaThis`), at most 8 (`kMaxWriteSites`, flagged past it). Aura maps them onto every property at the offset (packed bools share one: `sharedOffset`), `walk_function_props` sends `write_sites` / `write_sites_via_this` / `write_sites_truncated` / `shared_offset`, and the Props dialog has a Write sites column, "Write site in CE" (the AOBMaker CE plugin's disassembler, through `AobMakerActions.MoveViewAsync`) and "Copy write sites". Commits: red `b3b73a51` / green `c685c639`, red `aa98c77e` / green `a36c9d8d`; review `wf_c87eff0e-53f` (8 real): red `e038387f` / green `4bf68606`; rigs `8b04d3d4`, `d4fca524`. Live on 3732 (verification-register.md): DumperTest Shipping, 2,962 native functions walked, 73 written rows all with sites, 74 of 74 sites' own displacement the field's offset. ~~Original:~~ ⬜ open (our half of Eval C5) | Denken merges native field accesses by offset (`NativeFieldAccess`) and drops the instruction addresses. Keep them. Then a field's native write site is found statically, with no debugger. It can seed `GenerateAob` today, and AOBMaker R13's injection script later. Effort M · Risk low. | `Denken.h` / `Denken.cpp`, `walk_function_props` |

| id | sev | effort | what | where |
|---|---|---|---|---|
| `[VND9-DOC]` | ✅ **FIXED 2026-11-05** (was DOC) | S | ✅ The commit that marks this row: the `SubPathString` comments in `Ubel.cpp` (two), `Frieren.cpp`, `technical-notes.md` and `live-funcs-timeline-plan.md` say FString through 5.5 / FUtf8String from 5.6; the USMAP header gains point (3), and `BuildUsmap_EnumProperty_WithNoEnum_WritesNoneAtTheWidthOfItsSize` pins the Dumper-7 shape (3 cases; writing RE-UE4SS's bare integer turns all three red). The container half was checked against UE 5.8.4 `FEnumProperty::SerializeItem` (saving writes an FName). Was: `SubPathString` comments still say FString for 5.1+ (fix before or with VND9-01); the USMAP header's null-enum sentence and one test (container inners stay FName-shaped on purpose; do not align to RE-UE4SS); audit #7's reflection-date correction is recorded in audit #9. | `Ubel.cpp`, `Frieren.cpp`, `UsmapExportService.cs`, `technical-notes.md` |
| `[SYNCTOOLS-FALSE-FETCH-FAILED]` | ✅ **FIXED** `795ce835` | S | `sync_tools.ps1` printed `FETCH FAILED` over a RE-UE4SS fetch that had succeeded: the fetch also fetched the moved UEPseudo pin through its `git@github.com:` URL. The clone fetch passes `--no-recurse-submodules`; the RE-UE4SS block syncs the submodules over https. | `sync_tools.ps1` |
##### ✅ `[P4-OTHER-INSTANCE]` HIGH — opening a second instance of the same class reuses the first one's rows (FIXED IN SOURCE 2026-09-10)

`LiveWalkerViewModel.cs:6524`. The in-place branch never compares the address, and no navigation
clears `Fields` first. This was re-checked by hand:
- `NavigateToAddressAsync:2801` clears only `Breadcrumbs`.
- `NavigateToAsync:6112-6141` goes straight to `UpdateDisplay`.
- The only other `Fields.Clear()` sites are GWorld (`:972`), disconnect (`:5818`) and a failed Locate (`:6409`).
- Back, Forward, a breadcrumb jump, Parent and a bookmark load all reach the same `UpdateDisplay`.

- **Scenario:** in Instance Finder, open instance A of `BP_Enemy` in Live Walker. Go back to the
  finder, open instance B the same way. The count and first name match, so B's values are copied onto
  A's row objects. The header, the values and the Address column are all B's. But `StructDataAddr`
  is **absolute** (`instanceAddr + offset`, `Ubel.cpp:5311`) and still A's. Drilling B's `Stats`
  struct walks **A's** struct under the label "B > Stats". **Editing `Stats.Health` then writes A's
  memory in the running game** (`CommitFieldEditAsync :5406`). The same mix-up reaches:
  - Map and Set previews: B's count, A's entries.
  - Array drills: B's elements at A's addresses.
  - Copy CE XML and CSX: A's entries, and A's pointer targets' classes.
- **Widest variant:** two sibling subclasses with equal field counts and the same inherited first
  field. Here even `Name`, `TypeName` and `Offset` are the other class's.
- **Also reached** by a pointer drill from A to a B of A's exact class (`Owner`, `AttachParent`, a
  `Next` link).
- ✅ **FIXED IN SOURCE 2026-09-10 — the fix pass's third row, in the refuter's shape.**
  - **Change:** `UpdateDisplay` captures the on-screen `CurrentAddress` / `CurrentClassName` /
    `_currentClassAddr` before it overwrites them. The in-place branch now needs
    `IsSameObject` **and** `HasSameRowLayout` (below). `IsSameObject` means the same address,
    compared numerically, plus the same class name, plus the same class address when both sides
    have one.
  - **Everything else rebuilds.**
  - **Sibling-subclass variant:** closed as well, because the address differs.
  - **Coupled row:** `[A4-EDIT-STALE-PENDING]`'s "A's text written into B" variant is closed too,
    because B gets fresh rows.
  - **Red before green:** `LiveWalkerSameObjectGateTests.OpeningASecondInstanceOfTheSameClass_…`
    failed first with *"Expected 0x200020, Actual 0x100020"* (A's `StructDataAddr`), then passed.
  - **Evidence and live check:** in the `[FIXPASS-2026-09-10]` ledger and backlog.
- ✅ **Safe fix (the refuter's):** gate the branch on identity. Capture the previous `CurrentAddress`
  **before** `:6436` overwrites it. Require the same address **and** the same class, because a struct
  at offset 0 shares its owner's address. Anything else takes the existing Clear+Add rebuild. The
  grid jumping to the top when the object changes is expected.
- ⛔ **Unsafe fixes:**
  - Reverting to `Fields[i] = newFields[i]` brings back the measured drift.
  - Adding the missing members to `CopyLiveValuesFrom` makes the structural members mutable, and it
    still cannot fix the sibling-subclass variant.
- **Caught by:** a VM test that calls `NavigateToAddressAsync(A)` and then `(B)`: same class,
  different `StructDataAddr`; assert that the row holds B's. Live: on DumperTest, open two actors of
  one class via Instance Finder, drill a struct row, and compare the breadcrumb address with B's base.
- 🛡 **Until the fix, no code needed:** before opening another instance of a class already on
  screen, click 🌍 GWorld first; `PopulateFromWorld` clears the grid. At the very least, do not edit
  a struct or container sub-field right after switching instances.

##### ✅ `[P4-GUESS-SHIFT]` MED — with Guess? on, a same-object refresh pairs rows by index after the guessed rows moved (FIXED IN SOURCE 2026-09-10, with `[P4-OTHER-INSTANCE]`)

`LiveWalkerViewModel.cs:6543`. The guessed rows are re-derived from the bytes on every walk:
padding-run length, pointer vs two int32s, a float at 0.0 turning into padding (`Ubel.cpp:3633`,
`:3761-3944`). They are sorted in among the reflected rows by offset, so equal counts do not mean the
rows line up.

**Cross-gap variant:** in the same tick, one gap loses a row and another gains one. Every reflected
row between the two gaps then shows its **neighbour's** value and address under its own name, and
stays editable. An edit writes this row's type of bytes into the neighbour (`:5397-5406`).

- **Guess? is not rare:** `AutoFillGapsRetryAsync` turns it on silently and leaves it on (`:6188`).
- It self-heals as soon as the count next differs.
- The finding's within-gap example (ptr+double) depends on the values; the cross-gap one does not.
- ✅ **Safe fix:** the same gate as `[P4-OTHER-INSTANCE]`, plus a per-row `Name` + `Offset` +
  `TypeName` + `Size` match. The cost is honest: on a noisy Guess? object with auto-refresh, a layout
  change jumps the grid to the top. That is strictly better than an editable row pointing at its
  neighbour.
- ⛔ **Unsafe:** making the structural members observable and copying them. `SelectedField` and
  `RestoreSelectedField` would then silently re-point to a different field under the user.
- ✅ **FIXED IN SOURCE 2026-09-10, in the same commit as `[P4-OTHER-INSTANCE]`** (they share one
  gate, as planned). `HasSameRowLayout` compares, row by row: `Name`, `Offset`, `TypeName`, `Size`
  and `IsGuessed`. The cross-gap test (`Refresh_WhenGuessedRowsMoved_…`) failed first with
  *"Expected 0x100014, Actual 0x100018"*, then passed.
  - ⚠ **The gate's own adversarial review found a regression in the first draft,** confirmed by
    two lenses independently and not refuted. The DLL sets a guessed float's or double's type
    label from its VALUE (`Ubel.cpp` `IsLikelyFloat`: "Float" only for a clean .0/.5 value at or
    below 1000, "Float?" otherwise). The Name (`?0x14_float`), Offset and Size stay the same.
  - **The draft's failure:** it compared `TypeName` exactly, so Auto on a draining stat rebuilt
    the grid on every label flip. That is the `[LWREFRESH-2026-08-21]` jump-to-top, with no layout
    change at all.
  - **The fix:** a guessed row's `TypeName` is compared without its trailing `?`. The kind is
    already in the Name, and guessed rows are never editable or navigable, so the only cost is that
    a reused row keeps the first walk's label (stated in `LiveFieldValue.TypeTooltip`'s remarks).
  - **Tests for it:** `Refresh_GuessedRowOnlyChangesItsConfidenceLabel_KeepsTheRows` (3 cases)
    failed first, then passed. The same review asked for one-fact-per-case pins of every check:
    class name, class address, the lenient path, Name, Offset, TypeName, Size and IsGuessed. They
    were added too (working-lessons §1.2a).
  - **A second, narrower review** covered the `?` rule and test pinning, with a refuter defaulting
    to REFUTED. It found the `?` rule sound:
    - The DLL's only `?` pairs are Float/Float? and Double/Double?, each with the same Name hint
      and Size.
    - `NormalizeGuessedTypeToProperty` and the CE export treat both spellings alike.
    - Guessed rows are never editable or navigable.
  - **What it did find:** the row-COUNT check had no pinning test. Deleting it left the suite
    green, and the in-place loop would then throw on a Guess? toggle, and on every DataTable refresh
    (the RowMap row is appended). `Refresh_SameObject_TrailingRowCountChanges_Rebuilds` (+1 / −1)
    was added. **Mutation-checked:** with the count check removed, both cases fail; restored
    byte-exact, 17 / 17 pass. Its Double confidence case now uses the DLL's real `?0x10_double`,
    8-byte row.
  - 🟡 **Lead, LOW, not filed** (pre-existing; this change narrows it): in the in-place branch,
    `SearchMatchCount` comes from the fresh rows but the highlights are re-marked on the reused
    rows, and the second `MarkSearchMatches` return value is discarded.
    - **When they can differ now:** since the gate, only on a reused guessed row whose kept label
      differs from the fresh one, and only for a search term containing the `?`.
    - **Fix shape:** take the count from the second `MarkSearchMatches` call.

##### ✅ `[P4-PTRCLASS]` LOW — a retargeted pointer keeps its first-walk `PtrClassAddr`, and the exporters walk the new target with the old class (FIXED IN SOURCE 2026-09-11)

`LiveFieldValue.cs:176`. `PtrClassAddr` is `GetClass(target)`, which is a value (`Ubel.cpp:4165-4168`).
`PtrAddress`, `PtrName` and `PtrClassName` are copied; `PtrClassAddr` is not. Both exporters pass it
to `walk_instance` as the class override (`CeXmlExportService.cs:422/591`, `CsxExportService.cs:483`),
and `WalkInstance` honours any non-zero override (`Ubel.cpp:3987`). All three conditions must hold
for it to bite:
- it is export only;
- drilldown depth is ≥ 1 (the default is 0, but the value is persisted once raised);
- the pointer retargets to a different class (e.g. `Pawn` goes from hero to car).

- ✅ **Safe fix:** copy it in `CopyLiveValuesFrom`; nothing binds it.
- ⛔ **Unsafe:** dropping the override in the exporters. The CSX DataTable drilldown relies on it for
  row data that is raw struct memory (`CsxExportService.cs:871-881`), and the synthetic sub-field
  pointers carry it on purpose.
- ✅ **FIXED IN SOURCE 2026-09-11, the safe shape.** `CopyLiveValuesFrom` now copies it; nothing
  binds it, and the exporters keep the override. `Refresh_PointerRetargetsToAnotherClass_…` failed
  first with *"Expected 0x920000, Actual 0x910000"*.

##### ✅ `[P5-GROUP-ADVICE]` LOW — Group First Scan names the candidate cap but advises only deadline remedies (FIXED IN SOURCE 2026-09-12)

`ValueSearchViewModel.cs:1449`. `ScanForValueGroup` folds its 50,000-candidate cap
(`Aura.cpp:9459-9460`) into the same `deadline_hit` as the clock (`:9384`). The text reads
*"⚠ truncated (Ns deadline / result cap) — raise the Timeout slider or refine"*. Neither remedy
helps a cap stop:
- A longer timeout re-scans into the same cap.
- A refine prunes the capped set, and the panel's own `InheritedTruncation` (AE24,
  `PartialResultNotice.cs:149-167`) says that cannot surface a match that never entered the set.
- The control that does address the cap, **Max**, sits inside the single-mode panel
  (`ValueSearchPanel.axaml:72`/`:209`). It never renders in group mode.

The single-mode twin at `:1026` gets this right.

- **Measured, not constructed.** The archived AE13 run on DQ7R
  (`docs/archive/todo-closed-2026-08-23-build-3337.md:1674-1689`) printed exactly this text on a cap
  stop, at 1,849 ms of a 25 s budget. The duration being shown is the only mitigation.
- ✅ **Safe fix:** re-word `:1449` alone to name a remedy that is reachable in group mode, and drop
  "or refine". For example: *"raise the Timeout slider (deadline) or use more / more distinctive
  values (result cap)"*. No test pins that string. `begin_group_scan` is pipe-only, so a separate cap
  field would not be a three-exit P3 hazard, but it is not needed.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded re-word** (batch L21). The line now reads "raise the
  Timeout slider (deadline) or use more / more distinctive values (result cap)", with no "or refine".
  **Test, red first:** a group First Scan stopped at 1,849 ms, as in the DQ7R run. 6/6 mutants killed.
- ✅ **More precise, still no wire change:** a cap stop yields `Total == the sent max_results`
  exactly. Every push is followed by a `>= maxResults` break, and the clock is checked only at the
  top of an iteration. So the UI can tell the two causes apart. Capture `MaxResults` in a local
  before the await.
- ⛔ **Unsafe:**
  - advising "raise Max" in group mode, where it is not rendered (the Z10 trap), unless the same
    change adds Max to the group row;
  - a per-cause field on the pipe;
  - folding anything more into `deadline_hit`;
  - copying this wording onto `:1026`.

##### ✅ `[P5-PIVOT-FETCHCAP]` LOW — Class / Array Pivot says "(capped at 5,000)" when a different cap fired (FIXED IN SOURCE 2026-09-12)

`ClassPivotViewModel.cs:986` / `:1005`. `PivotResult.Truncated` is defined as the **group** cap: 5,000
(`PivotEngine.cs:88-93`), the top N groups of a complete input, with every count exact. Commit
220443c7 later folded the 2,000,000-row **fetch** cap into the same flag (`SnapshotStore.cs:2239/2257`
for class, `:2382/2409` for array). Its consequence is different:
- the pivot is built over a **prefix**, in `gobjects_index` order;
- the instance count, the group count and every group's count are all undercounts;
- the break can land mid-instance, which in Field mode creates a bogus "(missing)" group.

The status still says *"N groups (capped at 5,000) from M instances"*. Only a view-log Warn names the
fetch cap. It is rare: the snapshot keeps at most 256 elements per array, so it takes something like
800 owners × 256 elements × 10 ticked props. The feature is experimental-gated; this is reasoned,
not measured.

- ✅ **Safe fix** (C# only; no wire change, no P3): give the fetch cap its own `PivotResult` flag and
  its own sentence, and stop presenting the counts as totals when it fires. Both caps can fire on one
  run, so both sentences must be able to show.
- ⛔ Word the remedy *"tick only the fields you need"*, not "fewer fields". With **zero** value fields
  ticked, the `prop_name IN` filter is dropped and every prop is fetched (`SnapshotStore.cs:2217/2355`).
- ⛔ Do not remove `result.Truncated = true` at `:2257/:2409` unless the new flag lands in the same
  change. Otherwise the fetch cap goes silent, which is exactly what 220443c7 fixed.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L22, with `[W1-DT-TRUNC]`).
  - `PivotResult.FetchCap` / `FetchCapped` is the fetch cap's own flag. SnapshotStore sets it instead
    of `Truncated`, in the same change, so `Truncated` means the group cap again.
  - Both pivot Runs go through `ClassPivotViewModel.PivotRunStatus`. A fetch cap puts "≥" on the
    counts and adds its own sentence ("…cover a prefix and are not totals — tick only the fields you
    need"), and both caps can show together.
  - **Tests, red first:** the fetch cap alone and both caps together. The group cap alone is the
    control. 4/4 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5233/5233.
  - ⚠ **Survivors by construction:** the two call sites and the store's flag assignment. No fake store
    drives a fetch-capped Run; the status rule is pinned.

##### ✅ `[A1-COORD-RESURRECT]` MED — Teleport's coordinate library "Clear all" comes back after reconnect or restart (FIXED IN SOURCE 2026-09-11)

`CoordinateLibraryStore.cs:123`. `TryRead` returns null for a **missing** main file (`:140`) as well as
a corrupt one, and in both cases `Load` falls back to the rolling `.bak` (`:123`). Clear all deletes only
the main file and deliberately leaves the backups (`:222`).

So the next `LoadCoordLibraryForGame` silently restores the library as it was before the last save,
and logs *"main file unreadable, recovered from .bak"*. That load runs on every connect, on the Extra
Scan fan-out, and on every restart. The confirm dialog had promised it *"deletes all N entries … and
removes the library file"* (`en.axaml:1267`). Any library that has been saved twice has a `.bak`, so
this is the normal case, not an edge case. Hand-verified at source.

- ✅ **Safe fix:** try `.bak` only when the main file EXISTS but cannot be read (`File.Exists(path)`),
  which is the documented intent. `Load_CorruptMainFile_RecoversFromBackup` still passes. Add a test:
  Save, Save, SavePreClearBackup, Delete, Load → empty.
- ⛔ **Unsafe:** making `Delete` also remove the rolling `.bak`. `ClearCoordLibraryAsync` calls
  `Delete` even when the pre-clear copy FAILED, because the copy swallows its own exception. In that
  case `.bak` is the only surviving copy, and deleting it turns a resurrection into exactly the
  unrecoverable loss B6 fixed.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (batch B10, one commit with
  `[A1-COORD-BACKUP]`).
  - `Load` tries the `.bak` only when the main file EXISTS but cannot be read. A missing main file
    means "Clear all" happened, and that loads as empty.
  - `Delete` still keeps every backup, and `Load_CorruptMainFile_RecoversFromBackup` still passes.
  - **Test, red first:** `ClearAll_ThenLoad_DoesNotResurrectTheLibrary` (Save, Save,
    SavePreClearBackup, Delete, Load → empty).
- ✅ **Review follow-up 2026-09-11** (the review of B09-B12; B10's share was 6, all LOW; see also
  `[A1-COORD-BACKUP]` below).
  - **Clear all could lose the NEWEST revision.** `TryRead` reads a sharing violation as
    "unreadable", so a transient lock at Load recovers the OLDER `.bak` while the main on disk is
    the newest good file. The pre-clear backup is written from that in-memory library, and `Delete`
    then removed the only copy of the newest. `Delete` now rolls a main that PARSES to `.bak` first,
    as `Save` does (`ClearAll_AfterATransientLockAtLoad_KeepsTheNewestRevision`, red first).
  - That is not the recorded-unsafe "Delete also removes `.bak`". Every backup is still kept, and
    when the pre-clear copy fails, the surviving `.bak` is now the newest revision instead of the
    one before it.
  - The resurrect test also pins that `Delete` keeps `.bak`, so the recorded-unsafe shortcut can no
    longer pass it.
- ✅ **Second review follow-up 2026-09-11** (the review of 70f9d372).
  - `Delete` REFUSES when the roll to `.bak` fails: it existed to keep that revision, and deleting it
    anyway lost exactly that (red first, `.bak` held open). The next Load shows the library again.
  - Pinned: `Delete`'s roll is guarded on the main PARSING, as `Save`'s is, so a recovered-from-`.bak`
    session never rolls garbage over the good copy.

##### ✅ `[A1-COORD-BACKUP]` LOW — after a `.bak` recovery, the one-shot backups copy the corrupt file (FIXED IN SOURCE 2026-09-11)

`CoordinateLibraryStore.cs:218` (+ `:197`). `SavePreClearBackup` and `SavePreImportBackup` copy
whatever main file is on disk. After a `.bak` recovery, the file on disk is still the corrupt one,
because the load runs with persistence suppressed.

- **Clear all** then backs up garbage and still reports *"Backed up to …preclear.bak"*.
- **The import twin is worse.** A Replace-import backs up the corrupt main. The same Save then rolls
  the corrupt main over the only good `.bak`, so the pre-import library ends up in no file at all.
- **Why LOW, down from the finder's MED:** it needs the rare corrupt-main state, AND a Clear or
  Replace as the first change after connect. Any ordinary save in between self-heals.
- ✅ **Safe fix:** write the one-shot backup from the IN-MEMORY library (temp file + rename).
  Separately safe: make `Save` refuse to roll an unparseable main file over `.bak`.
- ⛔ **Unsafe:** "re-persist after a `.bak` recovery" by calling `Save`.
  - `Save`'s roll copies the corrupt main over the only good `.bak` before the rename. A crash in
    between leaves both files corrupt, and this would now happen on every connect.
  - `TryRead` also treats a sharing violation as "unreadable", so an automatic re-persist would push
    the newest revision into `.bak` with no user action.
- ⚠ **Fix these two rows together.** They are in the same file, and each one's safe fix assumes the
  other's.
- ✅ **FIXED IN SOURCE 2026-09-11, both recorded safe shapes** (batch B10, with `[A1-COORD-RESURRECT]`).
  - `SavePreClearBackup` / `SavePreImportBackup` take the IN-MEMORY library, and write it by temp
    file and rename. `TeleportViewModel` passes the snapshot `PersistCoordLibrary` saves
    (`CurrentCoordFile()`). An empty library writes nothing.
  - `Save` rolls the main file to `.bak` only when it PARSES, so a corrupt main never overwrites the
    good copy.
  - There is no automatic re-persist after a recovery, which is the recorded unsafe shape.
  - **Tests, red first:** both backups after a `.bak` recovery, and a Save over a corrupt main
    (3 red). The rolling-backup control was green both ways. They were written against the old
    one-argument API, so they failed on behaviour; the green then passes the library.
  - ⚠ **The view model's side is covered by compilation only:** it now passes `CurrentCoordFile()`
    to both backups, but no test drives "Clear all" through the view model.
- ✅ **Review follow-up 2026-09-11.**
  - **`Save` over an unparseable main destroyed it.** The fix stopped rolling it over the good
    `.bak`, and the rename then overwrote it, so whatever it still held (a half-written save a user
    can repair by hand) ended up in no file.
    - It is now moved aside as `teleport-coords.<game>.json.corrupt-<stamp>`, at most
      `AtomicFileHygiene.MaxCorruptCopies` of them, as `AobUsageService` does. The good `.bak` is
      untouched.
    - A move that fails refuses the Save rather than overwriting.
    - The copy keys to the game (`AppDataRetentionPolicy.GameKeyOf`), so it moves and expires with
      the game's group.
    - Red first: `Save_OverAnUnparseableMain_MovesItAside_InsteadOfDestroyingIt`.
  - **Pins that were missing** (green before and after):
    - the one-shot backups come from the library PASSED IN, not from a re-read of the disk;
    - `ZTolerance` survives the hand-built copy;
    - the rolling-backup control takes three saves, so a guard that rolls only once cannot pass.
  - 6/6 mutants killed across both rows.
- ✅ **Second review follow-up 2026-09-11** (the review of 70f9d372).
  - The corrupt main is COPIED aside now, not moved: moved first, a rename that then failed left NO
    main, which Load reads as a Clear all.
    - ⚠ Not unit-reproducible: it needs the rename to fail after the quarantine. Its mutant (move
      instead of copy) is the one expected survivor of the mutation check, reported as such.
  - The prune never deletes the copy just made. A future-stamped copy (clock skew, a file from
    another machine) outranked it, so the fresh one was pruned; it is excluded and counted, as in
    `AobUsageService` (red first).
  - 3/3 expected mutants killed; the 4th (move instead of copy) is the documented survivor. UI 5033/5033 (one suite run over the three second-round follow-ups together).
- ✅ **Third review follow-up 2026-09-11** (adversarial review of B14–B21: `coord-delete-destroys-corrupt-main`
  LOW/PLAUSIBLE, `coord-quarantine-says-moved` LOW/CONFIRMED).
  - **`Delete` was the second door.** After a `.bak` recovery, "Clear all" deleted the unparseable
    main outright: the destruction the first follow-up removed from `Save`. It now copies such a main
    aside first, exactly as `Save` does, and a copy that fails refuses the Delete.
  - The quarantine's Warn log and `Save`'s comment still said "moved aside" / "a move that fails"
    after the switch to copy. Both say copy now.
  - Red first: `Delete_OfAnUnparseableMain_CopiesItAsideFirst` (no main left, one `.corrupt` copy
    holding the old bytes, the `.bak` untouched). 1/1 mutants killed; UI 5089/5089.
- ✅ **Fourth review follow-up 2026-09-11** (the second adversarial review, of b496c866: three LOW, all CONFIRMED).
  - **Two claims were pinned by nothing**, and now are, as pins (the code already did both; their red is
    the mutation check):
    - "A copy that fails refuses the Delete" (`Delete_RefusesWhenTheCorruptMainCannotBeCopiedAside`). A
      handle sharing DELETE but not READ makes the main unreadable AND the copy fail while a delete would
      succeed. A Delete that swallowed the failure would remove a main nobody copied.
    - Only an UNPARSEABLE main is copied aside (`Delete_OfAHealthyMain_LeavesNoCorruptCopy`). Copying
      every main would fill the bounded `.corrupt` set with good files and evict a real half-written save.
  - Save's comment credited the COPY to `AobUsageService`, which MOVES its file: the unsafe shape the
    quarantine was changed away from. Only the bound follows it; the comment now says so.
  - 2/2 mutants killed; UI 5100/5100.

##### ✅ `[A1-LOG-RESUME]` LOW — after one 8 MB roll, every later session appends to the old `{cat}-0_NNN.log` (FIXED IN SOURCE 2026-09-12)

`LoggingService.cs:286`. When there is no checkpoint, Serilog.Sinks.File 7.0.0 resumes the
highest-sequence file (the refuter decompiled `RollingFileSink.OpenFile` to confirm this).
`ArchivePreviousLog` handles only `-0.log` and `-1..9.log` (`:312-318`), never `-0_NNN.log`. So once a
category has rolled:
- `pipe-0.log` / `view-0.log` are never created again;
- sessions are no longer archived under their own date;
- the documented "grep view-0.log" steps look at the wrong file;
- the compression sweep treats the live rolled file as idle after an hour and reports it as "failed".

Field evidence already exists: a 28-minute session on build 3262 logged into `pipe-0_005/006.log`
(`docs/archive/todo-closed-2026-08-25-build-3356.md:498`). No data is lost; the harm is to
diagnostics only.

- ✅ **Safe fix:** have `ArchivePreviousLog` also archive `{prefix}-0_*.log` at startup (oldest first),
  and rewrite `:286-288`.
- ⛔ **Unsafe:** widening `LogCompressionPolicy.IsLiveLog` to match `-0_NNN.log`. That would mark every
  closed 8 MB rolled file as live forever, dropping the largest and most compressible files from both
  sweeps. The in-session case needs a different rule: the newest `-0*` file per prefix in our own
  folder is the live one.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L35), the recorded safe fix:
  - `ArchivePreviousLog` now also archives every `{prefix}-0_*.log` at startup, oldest first, so the new session
    starts at `-0.log` again;
  - `CreateFileLogger`'s comment is rewritten: the live-file guard knows only `-0.log`, and `IsLiveLog` must not be
    widened.
  - **Red first:** rolled files beside a `-0.log` and rolled files alone are both archived, in order, and another
    category's are left alone.
  - ✅ Residual FIXED 2026-11-02 as `[LOG-RESUME-LIVE-ROLLED]` (see the census). As found: inside ONE session that rolls, the compression sweep can still call the live rolled file
    idle after a quiet hour. That is the "different rule" above, and it is not built.

##### ✅ `[A1-SLOTSYM-FAILED]` LOW — a failed second "Get GWorld" record tears down a live record's symbol (FIXED IN SOURCE 2026-09-12)

`CeLuaHygiene.cs:846`. `AppendSlotSymbolRelease` decrements the refcount unconditionally. But:
- CE runs `[DISABLE]` on the deferred untick after every failed ENABLE (`MemoryRecordUnit.pas`
  `setActive`; this repo measured it live as `[B30-REOPEN]`);
- every PointerQuery ENABLE bail returns **before** the register step
  (`PointerQueryScriptGenerator.cs:176-195/:210-221`).

With two records, B's ENABLE fails, its `[DISABLE]` runs, `_rc` drops to 0, and
`unregisterSymbol('UE_GWorld')` fires while A still reads ticked. Every `[UE_GWorld]+offset` record then
resolves to `??`. The only report is a `dbg()` line, which is silent at DEBUG=0.

The refuter **measured** this under real Lua over the UI-emitted script: *"A is still ticked but
UE_GWorld is GONE"*. The existing rigs (`slotsym_gworld_test.lua`, `slotsym_release_test.lua`) have no
failed-ENABLE case.

- ✅ **Safe fix:** per-record ownership in the shared emitters. Keep a holder set
  `UE5_slotSymHolders[sym][memrec.ID]`: add the record on a successful register, remove it on release,
  and unregister only when the set is empty. Fall back to the count when `memrec` is nil.
- ⛔ **Unsafe:**
  - Copying `[B30-REOPEN]`'s ownership flag. It is one global boolean, and two records share Lua
    globals, which is exactly the case SLOTSYM exists for.
  - A `getAddressSafe(sym)` guard. A's registration makes it true for B too.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L36, with `[A1-LUA-WAIT]`), the recorded safe fix, per-record ownership in
  the shared emitters:
  - `UE5_slotSymHolders[sym][memrec.ID]` is set on a successful register and cleared on release;
  - the release decrements only for a record that registered, so a failed record's `[DISABLE]` releases nothing;
  - the count stays the fallback without `memrec`.
  - **Red first:** a shape pin, because this suite has no Lua runtime. The register records the holder, and the release
    checks ownership before it decrements. The two-record failed-ENABLE run is a CE live check.

##### ✅ `[A1-LUA-WAIT]` LOW — `_tick and (elapsed >= Ms) or (iters >= N)` keeps the iteration bound live (FIXED IN SOURCE 2026-09-12)

`CeLuaHygiene.cs:192-194` and `:644-646`. In Lua, `a and b or c` evaluates `c` whenever `b` is false.
So both mailbox deadlines are **min(real ms, N × sleep cost)**, not the real deadline that the comments
and commit 45eb7c2f promise.

- **Harmless** while CE's `sleep(1)` costs ~15.5 ms, as on the measured machines.
- **Where it is shorter,** the 1.5 s idle wait and the 10 s status wait shrink by up to ~15×. One such
  case is Windows before 10 2004: there a 1 ms timer request is global, and the injected DLL makes one
  itself (`Mimic.cpp:78-82`). A timeout while the DLL is still processing then unticks a record that
  the DLL goes on to apply.
- ⭐ **A P3 twin of a fixed defect.** `AA29` found exactly this in `ue5_freeze_helper.lua` /
  `ue5_invoke_helper.lua`, and e8893e5a fixed only those two. The C# emitter that feeds every generated
  script kept the idiom. `pattern_p3.py` never compares Lua text twins, so the P3 sweep could not see it.
- **Evidence:** the Lua semantics were measured; the platform leg is inferred.
- ✅ **Safe fix:** mirror the helpers' `if … elseif _tick then … else … end` shape exactly, in both
  emitters. Add a test that runs the emitted loop under Lua. The current
  `IdleWait_measures_a_real_deadline_not_sleep_iterations` asserts substrings, and passes both before
  and after the fix.
- ⛔ **Unsafe:**
  - deleting the iteration arm, which leaves no bound at all where `getTickCount` is missing;
  - any rewrite that keeps the `a and b or c` form;
  - retuning the constants;
  - changing only one of the two emitters.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L36), the recorded safe fix: both emitters now use the helpers'
  `if st == nil … elseif tick … else iters … end` shape exactly. The iteration count is the fallback only without
  `getTickCount`, and the constants are unchanged.
  - **Red first:** a shape pin on both waits. The `a and b or c` form is absent, and the iteration arm must be the `else`
    of the getTickCount branch. The substring test above passed both before and after, as recorded.
  - ⚠ **The row asked for a test that RUNS the loop under Lua.** This repo has no Lua runtime, so that run is a CE live
    check (backlog), not a unit test.

##### ✅ `[A2-TOPTIONAL-INTRUSIVE]` MED — TOptional set/unset is decided by the inner type's NAME, and is wrong on every engine version that has FOptionalProperty (FIXED IN SOURCE 2026-09-11)

`Ubel.cpp:5770-5839`. UE decides "set" through `ValueProperty->HasIntrusiveUnsetOptionalState()`.
Checked at every tag from 5.3 (where `FOptionalProperty` first appears; the comment's "5.2+" is off)
to 5.8.2:
- **Object, class, weak, soft, lazy and interface optionals are NON-intrusive:** a trailing `bIsSet`
  flag. (An object optional is intrusive only under `CPF_NonNullable`.) The walker derives "set" from
  the value bytes instead:
  - `TOptional<AActor*>` set to null shows "(unset)";
  - after `Reset()`, which writes no bytes, the old actor's name and a **drillable stale pointer** are
    published.
- **TArray / TMap / TSet optionals are INTRUSIVE from 5.5 on.** The walker reads `bIsSet` at
  `field + innerSize`, which is the NEXT property's first byte. That is the neighbour-aliasing bug
  build 530 fixed for Str / Name / Text, still live for containers.
- **The measured discriminator is already in hand:** `fi.Size` vs `innerSize`.
- **Twin:** Find Refs (`Aura.cpp:3217-3231`) holds the same wrong belief and reports a reset
  optional's stale pointer as a live reference.
- **Population:** stock-engine runtime code declares no pointer or container `TOptional` UPROPERTY
  (editor gizmos and tests do). Game code is unmeasured. The defect is systematic and affirmatively
  wrong, hence MED.
- ✅ **Safe fix:** match UE's exact `CalcSize`, and refuse anything else:
  - non-intrusive only when `fi.Size == Align(innerSize+1, align)`;
  - intrusive only when `fi.Size == innerSize`.
- ✅ For intrusive fields, use the per-type sentinel (`ArrayMax == -1` at +12 for TArray / FString)
  and never the trailing byte.
- ✅ For non-intrusive object / weak fields, read the flag AND gate `ptrValue` on it.
- ✅ Keep the okProbe refusal first. Fix Find Refs in the same change, and correct
  `technical-notes.md:586-603`.
- ⛔ **Unsafe:**
  - a version gate: from 5.5 intrusiveness is per type and per `CPF_NonNullable`;
  - a loose "`fi.Size > innerSize` means trailing flag": a garbage `innerSize` would send an intrusive
    field back to the neighbour byte.
- 🟡 **Unfiled lead the refuter surfaced:** on 5.3 / 5.4, Str / Name / Text optionals are
  non-intrusive too. The build-530 sentinel arms were measured on a 5.5 title, so a default-constructed
  unset `TOptional<FString>` on 5.4 would read as set `""`. The DumperTest 5.4 fixture has
  `Opt_Str_Set` but no `Opt_Str_Unset`.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (batch B08).
  - **UE's CalcSize, matched exactly.** `Ubel::ClassifyOptionalLayout(fi.Size, sizeof(T), alignof(T))`:
    - intrusive only when `fi.Size == sizeof(T)`;
    - trailing flag only when `fi.Size == Align(sizeof(T)+1, alignof(T))`;
    - anything else is refused with a reason.
    - It uses no version gate and no loose "bigger than T". It was checked against UE 5.8's
      `PropertyOptional.h` and the Array / Set / Map / Object property sources.
  - **Shared resolver.** `Ubel::ResolveOptionalLayout` resolves the value property, its size and
    its alignment. The walker and Find Refs both use it.
  - **Walker.** It decides set / unset by the layout and decodes the value only when set.
    - An intrusive optional uses its per-type sentinel: `ArrayMax == -1` at +12 for TArray /
      FString, FName `~0u`, FText null, a non-nullable object null.
    - An intrusive TSet / TMap / struct is refused, never guessed: UE 5.8's sparse and compact
      sets store different unset states.
    - A `TOptional<UObject*>` set to null shows `(set: null)`.
    - The okProbe refusal still comes first.
  - **Find Refs, in the same change.** The pointer entries carry `setFlagOffset`, and all six scans
    over them check `OptionalGateOpen`. A layout it cannot prove is not bucketed.
  - **The 🟡 lead above is closed by the same rule:** a 5.3 / 5.4 FString optional is sized as a
    trailing flag (24 bytes) and read through its flag.
  - `technical-notes.md` § OptionalProperty is rewritten.
  - **Tests, red first.** `dll_core_test` OPTLAYOUT is a pool-faking block, and 9 checks started red:
    - a reset object optional that published a stale name and pointer;
    - an optional set to null that read `(unset)`;
    - an intrusive TArray, unset and set, whose flag came from the neighbour's byte;
    - the 5.4-shape FString that read `""`;
    - an unrecognised size that was not refused;
    - Find Refs' enumerator emitting the stale pointer.
  - **Controls, green throughout:** the set pointer, the set-empty string, Find Refs on a set
    optional, and the UNREADVAL TOptional cases (unchanged: an 8-byte optional of an 8-byte object
    is the intrusive layout). `dll_helpers_test` pins `ClassifyOptionalLayout`.
  - ⚠ **Coverage, stated honestly:** of the six Find Refs scan sites, only
    `EnumerateOutgoingObjectPtrs`' direct-pointer site is driven by a test, because
    `FindReferencesToUObject` needs a live object array. The other five carry the identical
    one-line gate.
  - 🟡 **Leads, not filed:**
    - Value Scan V1c's `optionalFlagOffset` still assumes a trailing flag, so an intrusive 5.5+
      FString optional gates on its neighbour's byte.
    - Find Refs' descent into a `TOptional<FStruct>` still assumes an unset slot is zeroed.
  - ✅ **Review follow-up 2026-09-11 (adversarial review of cc430176 + cd73ec38: 11 survived, all LOW after refutation; 2 refuted).**
    - **A regression this change exposed:** `Scharf::RequiredAlignment("LazyObjectProperty")` said 8.
      `alignof(FLazyObjectPtr)` is 4 in every era, so every 5.3+ `TOptional<TLazyObjectPtr>`
      (0x1C bytes) was "not recognised" and dropped from Find Refs. The same wrong 8 also put a
      `TMap<int32, TLazyObjectPtr>` value at +8 instead of +4. Now 4. OPTLAYOUT's lazy case and the
      Scharf pin were red first.
    - **Pins the review found missing,** all green pins:
      - the trailing-flag okProbe (UNREADVAL: a readable pointer with its flag across the page edge);
      - the intrusive FString / FName / FText sentinels, unset and set;
      - a struct optional, where the probe and MinAlignment decide the layout;
      - Find Refs not bucketing an unknown layout;
      - `(set: null)` asserted exactly.
    - **Filed rather than half-fixed:** the two 🟡 leads above are now `[A2-TOPTIONAL-STRUCT-DESCENT]`,
      which gains the Address Finder half, and `[A2-TOPTIONAL-VALUESCAN]`. Both need plumbing
      through every entry kind.

##### ✅ `[A2-TOPTIONAL-STRUCT-DESCENT]` LOW — Find Refs and the Address Finder walk into a reset `TOptional<FStruct>` (filed 2026-09-11; FIXED IN SOURCE 2026-09-12)

`Aura.cpp` `CollectRefMetaRecursive` (the OptionalProperty/StructProperty branch) and
`CollectContainersRecursive` (`:2318`) descend into a struct optional at the same offset. Nothing gates
that descent on the optional's `bIsSet`, and both comments claim "an unset slot is zero". That is
false. UE's `MarkUnset` runs `DestroyValue`, then clears the flag, and zeroes no bytes; a TArray keeps
its Data / Num after `~TArray`. So:
- a reset `TOptional<FMyStruct{ AActor* Target }>` is reported by Find Refs as a live `Opt.Target`
  reference, and also by `EnumerateOutgoingObjectPtrs` (graph paths);
- the Address Finder reads the destroyed array's stale header and freed buffer. The reads are
  SEH-safe, so the result is false hits in dead memory, not a crash.

This is the twin `[A2-TOPTIONAL-INTRUSIVE]` left: its pointer-shaped branch was gated; this descent
was not. The review of cc430176 rated it LOW, because the shape is narrow and the outcome is a false
positive.
- ✅ **Safe fix (the reviewer's):**
  - Resolve the struct optional with `Ubel::ResolveOptionalLayout(f.Address, f.Size, "StructProperty")`.
  - Do not descend when it is Intrusive or Unknown.
  - For a TrailingFlag optional, carry the flag's absolute offset (optional absOffset + sizeof(T))
    down the recursion, and store it on EVERY entry kind produced underneath: direct, weak-like,
    object / interface / weak arrays, maps, sets, `ContainerCacheEntry`. Every scan then checks it
    before reading.
  - A nested optional-in-optional needs an AND of flags, or a refusal.
  - Correct both comments.
- ⛔ **Unsafe:** dropping the descent altogether. That loses every true reference inside a SET struct
  optional.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L40), the reviewer's safe fix:
  - Both collectors resolve the struct optional with `Ubel::ResolveOptionalLayout`, and never descend
    when it is Intrusive or Unknown.
  - A TrailingFlag optional's flag offset (absolute) goes down the recursion as `gateAbs`. Every entry
    underneath stores it relative to itself, the shape `DirectPointerEntry` already had. That covers
    object / interface / weak arrays, maps, sets and `ContainerCacheEntry`, plus direct and weak-like
    pointers.
  - Every consumer checks `OptionalGateOpen` first: Find Refs' and the enumerator's loops for each
    kind, and each container walk (the Address Finder, `MatchAddrInStructContainers`,
    `WalkContainerLeaves`, the deep outgoing pass and deep raw).
  - An optional inside a gated optional would need two flags ANDed, so it is **refused**: a
    TrailingFlag optional under a gate is not bucketed; an intrusive object optional takes the outer
    gate.
  - Both comments are corrected.
  - **Red first:** dll_core_test's OPTLAYOUT block builds a `{ UObject* }` and a `{ TArray<UObject*> }`
    struct optional.
    - A reset one reports neither its pointer nor its array; a set one reports both (controls).
    - An Unknown-layout one is not descended.
    - The container cache entry carries `setFlagOffset` 24.
    - An InvokeScriptTests source pin covers Find Refs' own loops, which need a live GObjects.
  - ⬜ **Lead, unverified (from mapping this row):** `CollectSchemaLeaves` (Property Search's deep
    schema, Aura.cpp `OptionalProperty` + `StructProperty` branch) descends the same way. It reads no
    instance itself; whether its leaves are later read per instance was not traced.
    ✅ **Traced 2026-11-02** (`[LEAD-PROPSEARCH-TOPTIONAL-LEAVES]`, the census row has it): nothing reads
    them per instance; Freeze and Force now refuse a nested row in the command, not just by hiding it.

##### ✅ `[A2-TOPTIONAL-VALUESCAN]` LOW — Value Scan V1c still sizes the TOptional flag with the loose rule (filed 2026-09-11; FIXED IN SOURCE 2026-09-12)

`Aura.cpp` V1c (~`:7143-7163`) takes the flag offset from `Radar::OptionalFlagOffset(f.Size, innerSize)`:
a loose "bigger than T" rule that `[A2-TOPTIONAL-INTRUSIVE]` names as unsafe. It never gates an
intrusive optional either, so on 5.5+ an unset FString / FName / FText optional is scanned as a value,
and its "flag" is read from the neighbour's byte.
- ✅ **Safe fix (the reviewer's):**
  - Use `Ubel::ResolveOptionalLayout`.
  - TrailingFlag: gate on the byte at `sizeof(T)`.
  - Unknown: skip the field.
  - Intrusive: carry a per-type sentinel into the `ScanField` and test it before the read —
    FString `ArrayMax == -1` @+12, FName `ComparisonIndex == ~0u`, FText `TextData` null.
  - Drop `Radar::OptionalFlagOffset`'s loose rule.
- ⛔ **Unsafe:** skipping every intrusive optional. That silently drops 5.5+ string optionals from
  every scan.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L41), the reviewer's safe fix:
  - V1c takes the layout from `Ubel::ResolveOptionalLayout`, and a pure `Ubel::V1cOptionalGate` decides from it.
    - TrailingFlag: gate on the byte at `sizeof(T)`.
    - Intrusive FString / FName / FText: carry that type's sentinel. `ScanField.optionalSentinel` is tested on the value
      bytes by `IntrusiveOptionalIsUnset` before the read.
    - Unknown, or an intrusive T with no known sentinel: skip the field.
  - `Radar::OptionalFlagOffset` and its pin test are removed.
  - **Red first:** dll_helpers_test's V1C block pins the decision and all three sentinels, against inert helpers. An
    InvokeScriptTests source pin covers the Aura wiring, because no test drives `ScanForValue`.
  - ⬜ **Lead, unverified (from mapping this row):** the value-scan REFINE path appears to re-read V1c candidates with no
    optional gate at all, since Radar's `FieldDescriptor` has no optional member. It was not traced end to end.

##### ✅ `[A2-LAZY-LATCH-GUESS]` LOW — `TArray<TLazyObjectPtr>` replaces the engine's ElementSize with a version guess, then latches the guess as "measured" (FIXED IN SOURCE 2026-09-12)

`Ubel.cpp:1724` (+ `:1745`, `:1780`, `:3033`, `:394`).
- `InferScalarSize("LazyObjectProperty")` is a version guess (`ver >= 503 ? 0x18 : 0x1C`).
- `ValidateArrayElemSize` and `ResolveInnerSize` let that guess override the engine's raw
  ElementSize.
- `ReadLazyObjectArrayElements` then feeds the overridden size into the envelope latch, which logs
  *"payload envelope measured"*. `Ubel.cpp:391-393` forbids exactly this.

**Modelled in Python:** a 5.0-5.2 title mis-resolved as 504 strides 0x18 over 0x1C elements, reads
every GUID 4+4i bytes early, and writes a false "measured +0x08" line. Needs a version mis-resolved
across 5.2/5.3 AND an array walked before any scalar lazy field; the first scalar lazy walk heals it.
- ✅ **Safe fix:** for LazyObjectProperty only, derive the size from the RAW engine value via
  `LazyGuidOffset(raw)+0x10`, which accepts only 0x0C / 0x08 and otherwise falls back as today.
- ⛔ **Unsafe:** deleting the `InferScalarSize` entry. The generic arm accepts any raw size from 1 to
  65536, so a garbage ElementSize would reach the pipe, the exporter and FindInContainers.
- 🟡 **Same shape, tracked elsewhere:** the soft path's `>= 501` discriminator
  (`verification-register.md:500`).
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L10).
  - For LazyObjectProperty only, `ValidateArrayElemSize` returns `LazyGuidOffset(raw) + 0x10`, and
    `ResolveInnerSize` no longer asks the guess before reading the engine.
  - The `InferScalarSize` entry stays (the unsafe deletion was not made).
  - The array reader, which is only ever handed that derived size, now derives its envelope WITHOUT
    the latch. A fallback is never latched as "measured"; a real raw size is still measured once, where
    the size is resolved.
  - **Test, red first:** dll_core_test at a mis-resolved 504, with guards that garbage still falls back
    and latches nothing:
    - a real 0x1C is kept and latches +0x0C;
    - `ResolveInnerSize` reads 0x1C;
    - the reader latches nothing from the size it is handed.
    3/3 mutants killed; dll_core_test 304/304; UI 5212/5212.
  - 🟡 The soft path's `>= 501` discriminator is untouched, and stays tracked where it is.

##### ✅ `[A2-WALKCLASSEX-UNMAPPED]` LOW — WalkClassEx permanently memoizes an UNMAPPED class address, defeating Aura's refusal gate (FIXED IN SOURCE 2026-09-12)

`Ubel.cpp:1229`. `WalkClass` sets `info.Address` BEFORE its read-fault early return (`:968`, `:994-998`),
and `ReadSafe` zeroes on fault. So an unmapped address comes back as `{Address=addr, PropertiesSize=0}`.
- `WalkClassEx` calls `ShouldPublishClassWalk(true, 0)`, which accepts, and memoizes the empty result
  forever.
- Aura's two refusal gates (`WalkClassEx(cls).Address != cls`, `Aura.cpp:2371`/`:3387`) then PASS and
  pin empty container / ref metadata in two more never-erased caches.
- ⚠ **This makes A10b's "FIXED" claim false for the exact scenario it names**, a transient read fault
  on `cls`. Its live control measured only the healthy side.
- Reached by `walk_class` / `walk_class_batch` with a raw address, e.g. from the ungated Class Pivot
  handoffs `[PATTERN-P6-2026-09-10]` records.
- ✅ **Safe fix:** thread `WalkClass`'s read-fault verdict out (a `WalkClassImpl(addr, bool& ok)`),
  pass it to the gate, and add a `VirtualFree` / re-commit `dll_core_test` case.
- ⛔ **Unsafe:**
  - gating on `Fields.empty()` or `Name.empty()` (field-less classes are legitimate, forks return "");
  - re-reading PropertiesSize (a race);
  - un-memoizing with no once-per-address log guard (it would flood `walk-0.log`);
  - bounding or evicting the cache (the dangling-`const&` hazard).
- **Twin, fix together:** `GetCachedStructFields` (`:2549-2645`) publishes `WalkClass`'s result
  unconditionally into a third never-erased cache.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix, with its twin** (batch L09).
  - `WalkClassImpl(addr, bool& readOk)` carries the fault exit's verdict. `WalkClassEx` passes it to
    `ShouldPublishClassWalk`, so an unreadable class is refused and NOT memoized, and Aura's two gates
    refuse with it.
  - `GetCachedStructFields` serves an unreadable struct empty, again without memoizing it.
  - None of the recorded unsafe shapes: no Fields/Name gate, no second PropertiesSize read, no cache
    bound or eviction.
  - The un-memoizing got its log guard: WalkClass's warning is once per address (bounded at 4096), and
    WalkClassEx says nothing extra for an unreadable class.
  - **Test, red first:** dll_core_test decommits a page. WalkClassEx refuses it and the twin memoizes
    nothing. It re-commits the page with a plausible class, and the class walks. 3/3 mutants killed;
    dll_core_test 297/297; UI 5212/5212.
  - ⚠ **Survivor by construction:** the once-per-address guard. Its only effect is log volume.

##### ✅ `[A2-GNAMES-PTRSCAN-ABORT]` LOW — a WIDENING of `[P1-GENAU-ABORT]`: the GNames tier-3 pointer scan aborts with no log and no flag (FIXED IN SOURCE 2026-09-12)

`Genau.cpp:2122`. `FindGNamesByPointerScan` returns 0 on `Tot::Requested()`, silently.
`s_gnamesReport.cancelled` stays false, so `UE5_Init` can latch initialized with GNames missing, which
is the recorded row's consequence. The recorded row names only the three LOGGED sweeps. The P1 matcher
enumerates log calls, so a bail with no log line was outside its population by construction. Tier 3 is
reachable: a DumperTest D1 run resolved GNames by `pointer_scan`.
- The `ExtraScanGWorld` bails (`:4515`/`:4558`) are real but mostly masked: `RecoverGWorldViaEngine`
  follows with no Tot poll, and GWorld is non-critical.
- ✅ **Safe fix:** set `s_gnamesReport.cancelled` at the bail, in the recorded row's shape.
- ⛔ **Harmful:** ORing `ExtraScanGWorld`'s bail into the latch guard. That refuses a complete init
  over a non-critical pointer that has its own recovery route.
- ✅ **FIXED IN SOURCE 2026-09-12, the safe fix** (batch L01, with `[P1-GENAU-ABORT]`): the bail sets
  `s_gnamesReport.cancelled` and now logs. `ExtraScanGWorld` is untouched. Pinned by the `GENAUABORT` block.

##### ✅ `[A2-CRC-PATH-LS]` LOW — CrashReportClient version detection logs its path with `%ls`, which this file forbids (FIXED IN SOURCE 2026-09-12)

`Genau.cpp:2849`/`:2853`, written by 6a74065a twelve days after `[NONASCIILS-2026-08-24]` and 2,700
lines below the file's own *"CONVERT FIRST; NEVER %ls"* note (`:116`). On a non-ASCII install path the
record comes out empty; `utf8_helpers_test.cpp:582-609` pins the CRT behaviour. The version source is
still named by its ASCII label elsewhere, so the loss is only the resolved path and the record's
integrity.
- ✅ **Safe fix:** `Utf8Helpers::EncodeUtf16`, then `%s`. It is byte-identical for ASCII paths.
- **Gate gap:** nothing scans call sites for `%ls` in log calls.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix and the gate** (batch L11).
  - The red is the gate: `InvokeScriptTests.DllLogCalls_NeverFormatAWideString` scans every dll/src
    file for a `%ls` inside a `Sein::` / `LOG_` call. It skips comment lines and wide printfs.
  - The gate found **eight** sites, not two, and all eight now `Utf8Helpers::EncodeUtf16` first, which
    is byte-identical for ASCII:
    - both CrashReportClient lines;
    - the VERSIONINFO key;
    - Fern's pipe-name line;
    - both proxies' System32-path lines, which now keep `GetLastError()` before the conversion.
  - 4/4 mutants killed, one of them a wide format on the second line of a multi-line call; dll_core_test 304/304; UI 5213/5213.

##### ✅ `[A2-HEAP-ANCHOR-TEXT]` LOW — a data-scan GObjects anchors to the heap, and later refusals print the "GObjects never validated" text (FIXED IN SOURCE 2026-09-12)

`Genau.cpp:1666`. `DataScanGObjectsCandidates` returns a HEAP `FUObjectArray` by design, so the module
anchor has no module and collapses to `AnchorState::None`. The anchor line prints `'(unknown)'`. Every
later Pass-2 refusal then logs *"GObjects never validated this run"*, the AA38 python.exe signature, on
a run where GObjects DID validate. On this PC that happens whenever `GWLD_V3` matches inside
Bitdefender's `atcuf64.dll`. **No published pointer moves:** both states refuse on a monolithic build.
- ✅ **Safe fix:** text only. Or add a fourth `AnchorState` that refuses with truthful text.
  - ⚠ The constexpr switch ends in `return Accept; // unreachable`, so a new enum value without its
    case silently ACCEPTS. Extend the truth table from 12 rows to 16.
  - Keep the `None` wording byte-identical: an archived live check greps it.
- ⛔ **Unsafe:** treating a heap anchor as modular / Accept, which re-admits the Bitdefender GWorld
  candidate.
- ✅ **FIXED IN SOURCE 2026-09-12, in the enum form** (batch L12).
  - `AnchorState::Heap` is a validated anchor in no module, and `ModuleAdmission::RefuseHeapAnchored`
    refuses exactly where `None` refuses, with a text that says GObjects validated on the heap. The
    anchor line says "set on the HEAP" instead of `'(unknown)'`.
  - The mapping moved into the pure `Genau::ClassifyAnchor`. `CurrentAnchorState` only asks Windows.
  - The switch's tail, which silently ACCEPTED a value without its case, now fails closed.
  - The truth table is 16 rows. The `None` wording is byte-identical.
  - **Test, red first:** dll_helpers_test. A no-module anchor classifies Heap. A heap anchor refuses a
    foreign candidate with its own verdict and admits the producer. The main-module loop covers all four
    states. The red made the silent Accept explicit. 3/3 mutants killed; dll_helpers_test 2716/2716, dll_core_test 304/304; UI 5213/5213.
  - ⚠ **Survivors by construction:** `CurrentAnchorState`'s three-line wiring and the two log texts.

##### ✅ `[A3-ST1-SUPER-DRAIN]` MED — ST1's fail-open direct call still re-enters our ProcessEvent detour, and drains the invoke queue off the game thread (FIXED IN SOURCE 2026-09-12)

`Frieren.cpp:2261-2279`. `Mimic` auto-routes any `Native|Static` UFunction to
`UE5_CallProcessEventDirect` (`Mimic.cpp:641`/`:702`). When the instance's class overrides ProcessEvent,
which is every `AActor`, the direct call correctly fails open to the override. But
`AActor::ProcessEvent` then calls `Super::ProcessEvent` (vendor 5.8.2 `Actor.cpp:1488-1523`), which is
the address MinHook patched. So `HookedProcessEvent` runs on the mailbox thread with
`InOwnPeCall() == false`, and the drain executes **every queued request there, off the game thread**.

- **The danger is the queue.** An invoke that timed out (-5) stays queued on purpose. A later static
  native on an actor class (e.g. stock `APawn::GetMovementBaseActor`) then runs that stateful call on
  the wrong thread: ST1's exact crash class, through a path its fix did not reach.
- **Mailbox exit only.** The pipe queues these calls; `direct_call` is used only by the
  KismetMathLibrary self-test, whose CDO does not override.
- ✅ **Safe fix:** a Stark helper (e.g. `CallAddressAsOwnSEH`) that holds `OwnPeCallGuard` in an OUTER
  frame and calls `peAddr` inside its own `__try` frame (MSVC C2712). Call it from the fail-open branch.
  It must live in `Stark.cpp`, where the guard is. No contract bump.
- ⛔ **Harmful:**
  - routing overriding classes through the trampoline, which skips `AActor`'s world, GC and delegate
    checks;
  - queueing static natives on actor classes, which brings back the idle-menu timeouts;
  - setting the guard in `CallProcessEventSEH`, which suppresses the legitimate game-thread drain.
- **Live confirmation, which needs CE — ask first:** `tools/verify/st1_queued_drain_sideeffect.py` with
  a frozen game thread, a queued `SetActorHiddenInGame`, then a mailbox static-native invoke on an
  actor. `bHidden` flips while the thread is frozen.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch B30). No contract bump.
  - `Stark::CallAddressAsOwnSEH` holds `OwnPeCallGuard` in its own (outer) frame and calls the resolved
    address through `CallAddressSEH`'s `__try` frame (MSVC C2712), exactly the shape of
    `CallOriginalSEH`.
  - `UE5_CallProcessEventDirect`'s fail-open branch calls it, so an override's `Super::ProcessEvent` that
    re-enters our detour now finds `InOwnPeCall()` true and does not drain.
  - None of the harmful fixes landed: not the trampoline, not a queue, and the guard is not set inside
    `CallProcessEventSEH`, so the legitimate game-thread drain is untouched.
  - **Test, red first:** a source pin in `InvokeScriptTests` (the `ClassListCapTests` pattern: no test
    target compiles Frieren.cpp or Stark.cpp). The fail-open branch must go through the helper with no
    raw call left, and the helper must hold the guard and keep the `__try` in a separate frame.
    2/2 mutants killed; UI 5135/5135.
  - ⚠ The pin reads source, so it proves the shape, not the runtime behaviour. The live confirmation
    above (CE, announce first) is the backlog check.
  - ✅ **Review 5 follow-up 2026-09-12** (of 0edd5214: three LOW, one filed MED).
    - **Live check L36 named a rig that cannot do its step 2.** `st1_queued_drain_sideeffect.py` freezes,
      queues one invoke and resumes at once, so its PASS does not depend on this fix. L36 now says so, and
      gives the manual route: the pipe's `direct_call: true` reaches the fail-open branch without CE.
    - **Stale comments.** The rewritten function's header (Frieren.cpp) and Stark.h still said our direct
      calls never enter the detour, and that the body could not be shared through a helper. Corrected.
    - **The pin missed two one-line mutants:** a commented-out guard, and the recorded HARMFUL trampoline
      swap inside `CallAddressSEH`, whose body it never read. It now reads both bodies with their comments
      stripped. 2/2 mutants killed; UI 5193/5193.

##### ✅ `[A3-DEPLOY-CANCEL]` MED — "Cancel operation" during Deploy or Undeploy crashes the UI (FIXED IN SOURCE 2026-09-11)

`ProxyDeployViewModel.cs:1391-1414` (+ Undeploy `:1452`). AE20 made "Cancel operation" reach all nine
commands, but `DeploySelectedAsync` and `UndeploySelectedAsync` have **no try/catch at all**. The cancel
rethrows through `AsyncRelayCommand` onto the dispatcher, where the refuter decompiled both
CommunityToolkit.Mvvm 8.4.2 and Avalonia 12.1.1. `DispatcherFaultGuard` sees our frames on the stack,
refuses to swallow, and **the process dies**.

- Even a ONE-game deploy reaches it, through the post-loop refresh's token.
- The writes are staged-atomic, so no half-written DLL is left. What is lost is the app and the
  connected session.
- Refresh's `catch(Exception)` shows the user's own cancel as a red "Refresh failed".
- ✅ **Safe fix:** wrap each loop plus its refresh in `catch (OperationCanceledException)`, mirroring
  `UpdateAll :1562-1567`.
  - Report the partial tally.
  - Still call `RequestOptionSave` when picks changed.
  - Do NOT re-run the refresh with the cancelled token inside the catch, because it throws again. Skip
    it, or use `CancellationToken.None`.
  - Give Refresh a neutral "cancelled" branch.
- ⛔ **Unsafe:**
  - `FlowExceptionsToTaskScheduler`, which hides every real fault;
  - teaching `DispatcherFaultGuard` to swallow OCE;
  - dropping Deploy/Undeploy from the cancellable set, which reverts AE20;
  - a bare `catch(Exception)`.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (batch B09, one commit with
  `[A3-RADIO-MIDDEPLOY]`).
  - **Deploy and Undeploy** wrap each loop and its refresh in `catch (OperationCanceledException)`,
    mirroring `UpdateAll`. The catch:
    - reports the partial tally: `Deploy cancelled — deployed: N, failed: M` /
      `Remove cancelled — removed: N, failed: M`;
    - still calls `RequestOptionSave` when a pick changed;
    - never re-runs the refresh with the cancelled token. `RefreshAfterCancelAsync` refreshes with
      `CancellationToken.None`, and a failure there is shown and logged, not rethrown.
  - **Refresh** has a neutral `Refresh cancelled` branch ahead of its `catch (Exception)`.
  - It uses no `FlowExceptionsToTaskScheduler`, no `DispatcherFaultGuard` change and no bare catch
    as the fix. Deploy / Undeploy stay cancellable.
  - **Tests, red first.** `ProxyDeployConcurrencyTests`, 5 red:
    - Deploy and Undeploy cancelled mid-run (the cancel escaped `ExecuteAsync`);
    - the saved pick;
    - a one-game Deploy whose cancel reaches the final refresh;
    - Refresh's red "Refresh failed".
  - The no-cancel control was green throughout.
  - The harness gained an opt-in `ThrowOnCancelledRefresh`, modelling the real service honouring its
    token. It is off by default, so the existing tests keep the stub that does not.
- ✅ **Review follow-up 2026-09-11** (the adversarial review of B09-B12: 18 survived, 4 refuted; B09's
  share was 5, all LOW).
  - **The shipped Deploy / Remove code was correct. The pins were weaker than ledger row 20 said.**
    "The recorded-unsafe re-run with the cancelled token" was killed for Deploy only:
    - every Undeploy test left `ThrowOnCancelledRefresh` off, so a Remove catch that refreshed with
      the cancelled token stayed green;
    - no test checked that the post-cancel refresh actually LANDED, so a `RefreshAfterCancelAsync`
      that skipped it stayed green too.
  - Now pinned:
    - the mid-run Remove turns the flag on, and a one-game Remove twin mirrors the one-game Deploy;
    - every cancel test asserts that a refresh landed (`svc.Applied`) and that `ErrorMessage` is null;
    - Refresh's cancel is asserted neutral (`#888888`), not only "not failed".
  - **Update All**, the model this fix was copied from, reported its partial tally on a cancel but
    never refreshed. The rows kept the old versions for the games it HAD written. Its catch now calls
    `RefreshAfterCancelAsync` too (`UpdateAll_Cancelled_BringsTheGridBackInLine`, red first; the
    other additions are pins on code that was already right, green before and after).
  - 4/4 mutants killed, the Remove catch refreshing with the cancelled token among them.

##### ✅ `[A3-MIMIC-INIT-FASTPATH]` LOW — the CE mailbox skips B5's init serialization in the last 30-45% of every init (FIXED IN SOURCE 2026-09-12)

`Mimic.cpp:470-471` returns "initialized" whenever `g_cachedGObjects && g_cachedGNames`. `UE5_Init`
publishes those right after `FindAll`, **before** `Serie` / `Aura` init, decoy recovery and
`ValidateAndFixOffsets`, which writes defaults and then probes. `UE5_Shutdown` never clears them.

- So a CE hotkey in that tail runs against unprobed DynOff with no "waiting" line. That breaks
  `Frieren.cpp:83-91`'s promise that it "waits and returns the first caller's result".
- **Measured from real init logs:** the unguarded tail is 190-445 ms per init. Commands resolving
  UFunctions or properties through DynOff give one-off errors that succeed on retry.
- ✅ **Narrowest safe fix:** an "init in progress" atomic set under `s_initMutex`, so the fast path
  waits only when it is set.
- ⛔ **Unsafe:**
  - always calling `UE5_Init`: a pathological rescan can pass the 10 s mailbox timeout, and after a
    cancelled scan a full cancel-immune `UE5_Init` would run on the poller;
  - clearing the globals in `UE5_Shutdown`;
  - moving the publish to the end of init.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded narrowest fix** (batch L14).
  - `g_initInProgress` is raised under `s_initMutex`, by an RAII scope that opens before `FindAll`
    publishes the globals and closes on every exit.
  - The mailbox's fast path is now `Mimic::InitFastPathOk(gobjects, gnames, inProgress)`. While an
    init scans, it falls through to `UE5_Init`, which waits on the mutex, logs the "waiting" line, and
    returns the first caller's result.
  - None of the three unsafe shapes was taken.
  - **Tests, red first:** the rule in dll_helpers_test, plus source pins for both ends of the wiring
    (Frieren and Mimic reach no test target). 3/3 mutants killed; dll_helpers_test 2719/2719, dll_core_test 304/304; UI 5215/5215.
  - ⚠ **Survivor by construction:** the scope's store semantics. No harness runs `UE5_Init`; the pin
    fixes the scope's position.

##### ✅ `[A3-RADIO-MIDDEPLOY]` LOW — the proxy-type radio stays live during Deploy (FIXED IN SOURCE 2026-09-11)

`ProxyDeployViewModel.cs:1396`/`:1406-1408`. The radios have no `IsEnabled`, and their handler takes no
gate, so a click mid-Deploy silently re-targets every remaining game to another DLL flavour. The
result line never says so.
- The finding missed a deterministic part: the in-flight game was deployed with the OLD flavour, but
  `LastManualProxyByGame` records the NEW one and persists it. That is a wrong remembered fact which
  feeds the Suggested column.
- ✅ **Safe fix:** `IsEnabled="{Binding !IsBusy}"` on the four radios, not on their panel, which also
  holds the LKG checkbox.
- ⛔ **Unsafe:** disabling the foreign-overwrite checkbox mid-run, which would stop the user withdrawing
  consent; gating the handler alone.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (batch B09).
  - The four proxy-type radios carry `IsEnabled="{Binding !IsBusy}"`: on the radios, not on their
    panel.
  - The LKG checkbox and the foreign-overwrite checkbox stay live.
  - Pinned from the AXAML (`ProxyTypeRadios_AreDisabledWhileBusy_TheLkgCheckboxIsNot`, red first).
    The binding compiles in the UI build.
  - ✅ **Review follow-up 2026-09-11:** the pin checked only that the LKG checkbox stays live. It now
    also refuses both recorded-unsafe shapes: an `IsEnabled` on the foreign-overwrite checkbox, and
    one on the radios' panel. 2/2 mutants killed.
  - ✅ **Second review follow-up 2026-09-11** (the review of 64b28058): the foreign-overwrite
    checkbox's OWN panel is pinned too; disabling that parent disables the checkbox as surely as an
    attribute on it. 1/1 mutant killed. UI 5033/5033 (one suite run over the three second-round follow-ups together).

##### ✅ `[A3-CONTAINER-4096-ADVICE]` LOW — "raise the Array Limit slider" for arrays the slider does not govern (FIXED IN SOURCE 2026-09-12)

`ContainerTruncation.cs:43` via `LiveWalkerViewModel.cs:1254-1277`. The scalar-array re-fetch asks for
the full count, but the DLL clamps every request at 4,096 (`Ubel.cpp:42`/`:2227`). So a 10,000-element
`TArray<float>` shows 4,096 rows plus advice the slider cannot satisfy, with no paging. That is the Z10
shape that `FixedCapStatusLine` exists to prevent. Commit 3860bfbc's "Scalar arrays are re-fetched in
full" was false past 4,096 on the day it shipped. Pointer and struct arrays have the same problem at
slider ≥ 8192.
- ✅ **Safe fix:** a UI honesty fix derived from the reply (`ReadCount < TotalCount` despite a full
  request, then `FixedCapStatusLine`), never a hardcoded 4096.
- ⛔ **Unsafe:** unbounded paging; raising the DLL cap.
- ✅ **FIXED IN SOURCE 2026-09-12, derived from the reply** (batch L21).
  - Each array-drill branch records what it asked for: the full count for the scalar re-fetch, the
    slider for the inline preview.
  - Fewer elements back than asked means the DLL capped the reply, and gets `FixedCapStatusLine`; the
    slider advice stays for a slider-capped view. No hardcoded 4096, no paging, no DLL change.
  - **Tests, red first:** a scalar drill clamped at 4,096 of 10,000 by a fake reply. The existing
    128-of-199 pointer test is the control, and it kills the mutant that counts the full count as
    requested. 6/6 mutants killed.

##### ✅ `[A3-COORD-NONFINITE]` LOW — a coordinate CSV / Lua import stores `NaN` / `Infinity` / `1e400` as 0 without a word (FIXED IN SOURCE 2026-09-12)

`CoordinateLibraryFile.cs:151-154`. `double.TryParse` accepts non-finite values, and `Round` maps them to
0 without recording an issue. So a teleport lands at the world origin, which is exactly the silent wrong
coordinate that B21's note says must be a visible rejected row. The Lua fence is reachable through
`1e400`.
- ✅ **Safe fix:** `&& double.IsFinite(value)` in `CoordPrecision.TryParse`, the in-tree idiom.
- ⛔ **Unsafe:** changing `Round`, which is also the pose-capture and writer path; a NaN would reach
  generated Lua as a nil global.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L39), the recorded safe fix: `&& double.IsFinite(value)` in
  `CoordPrecision.TryParse`. `Round` is untouched.
  - **Red first:** a CSV row whose x is `NaN`, `Infinity`, `-Infinity` or `1e400` is a rejected row with an issue on
    column x, and a Lua entry with `x=1e400` is reported, not imported.

##### ✅ `[A4-NAV-BACKFIRST-GRAFT]` MED — a row clicked during Back's walk grafts the old level's field onto the new parent (FIXED IN SOURCE 2026-09-11)

`LiveWalkerViewModel.cs:1090-1095`. V4's fix claims that capturing the parent "at gesture time" handles
the reverse ordering. It does not:
- `GoBackAsync` pops the crumb synchronously (`:2352-2356`), then awaits the walk. The grid keeps
  showing the level just left, and the Back button has no `IsEnabled`.
- A → click on one of those stale rows captures `parentAtGesture = CurrentCrumb`, which is already
  the popped-to crumb.
- `IsStillOnParent` then passes, and the old level's `field.Offset` is appended under the new parent.
- Copy CE XML, Copy CE Field, the AA script and a saved bookmark all persist the wrong chain. This is
  V4's own corruption, through the half its re-derivation named (test #2, never written).

The same window is open after Parent, Forward and a breadcrumb jump. The container drill takes the
same path.
- ✅ **Safe fix:** stamp which crumb the rendered `Fields` belong to, and compare that stamp to
  `CurrentCrumb` by reference at gesture entry.
  - Do it BEFORE the scroll-hint and view-state writes, in both `NavigateToFieldAsync` and
    `NavigateToContainerAsync`.
  - Set the stamp at EVERY Fields-population site, including the six that bypass `UpdateDisplay`,
    and after `HydrateSlot` / `PathStepToBreadcrumbs` rebuild crumbs.
  - Add the re-derivation's test #2.
- ⛔ **Unsafe:**
  - `IsEnabled="{Binding !IsLoading}"` on row buttons: every auto-refresh tick would blink them off,
    and `IsLoading` is one shared flag;
  - a global nav mutex;
  - clearing `Fields` on Back, where the Reset jumps the grid to the top.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (fix-pass batch B03).
  - **The stamp:** `_renderedCrumb` is the crumb the rendered rows belong to. It is stamped by
    every grid-population site: `UpdateDisplay`, placed BEFORE its pending-scroll auto-drill;
    `PopulateFromWorld`; and the DataTable / Array / Map / Set views. It is cleared with the grid.
  - **The check:** `NavigateToFieldAsync` and `DrillContainerAsync` refuse, at entry and before
    any write, a drill of a row that is ON SCREEN and whose stamp is not `CurrentCrumb` (by
    reference). The refusal carries a status line.
  - **Rows that are not rendered are not checked** (`Fields.Contains`). A programmatic drill of a
    row the caller built makes no claim about the grid. The V4 tests that drill detached fields
    with hand-added crumbs still pass unchanged.
  - **Red → green (the re-derivation's test #2, finally written):** Back (row drill and container
    drill), a breadcrumb jump, Forward and Parent each hold their walk on a gate and click a row of
    the level being left. All 5 were red on the old code (grafted), and all 5 are green now.
  - **Negative controls, green before AND after:** a current-level row drills; a current-level
    container opens; after Back lands, the new level's rows drill; an element row inside a
    struct-array container view drills; after Back from a container view, the parent's rows drill.
  - ⛔ **Not done, as recorded:** `IsEnabled="{Binding !IsLoading}"`, a nav mutex, clearing `Fields`
    on Back.
  - ⚠ **The batch's adversarial review** (3 lenses, 16 agents): **9 survived, 4 refuted.** The 4
    refuted were test-gap claims about pins added while the review ran. The 9 completed the
    stamp's instance list and were repaired in a follow-up commit:
    - **The stamp recorded the crumb current at RENDER time, not the crumb the walk was for.**
      Back's walk landing after a Forward pressed meanwhile installed A's rows as B's.
      `RenderSuperseded(target)` now drops a navigation's render when its target is no longer
      current. It covers Back (both branches), Forward, a jump, Parent, the synthetic-container
      re-hydrate, bookmark load (both renders) and all three Locate renders.
    - **Refresh:** it discards itself across a crumb change (the identity is checked, not only the
      address and count, since a re-rooted Back keeps both), and it **never re-stamps**. It
      re-walks the object on screen, and after a Back or Parent whose walk FAILED, re-stamping
      recorded the old level's rows as the new crumb's.
    - **The same window through other commands:** Copy CE XML, Copy CE Field(s), CSX and Save
      Bookmark combine the current spine with the grid. They now refuse while the grid is behind
      the spine (`RefuseWhileGridBehindSpine`). Push to CE, "+CE" and the AA script use absolute
      field or object addresses, and were checked unaffected.
    - **A failed re-root left every drill refused:** a Go-box typo, a DLL-rejected address, or a
      failed GameEngine start clears the spine but keeps the rows. A re-root ticket
      (`ReleaseLeftoverGrid`) now declares such rows a rootless leftover, so a drill re-roots at
      the pointee as it always did.
      - An older failure cannot release a newer re-root's guard.
      - A re-root still IN FLIGHT keeps refusing; that window is the graft.
    - **The refusal text is now true in both states** (still loading, or failed).
    - **"Before any write" is pinned:** a refused drill leaves Back's destination crumb view state
      and its loading state untouched.
  - **Follow-up red → green:** 6 new gated or failing interleavings (Back then Forward, a failed Back
    then Refresh, a Refresh across a same-count spine swap, a failed re-root, the exports and
    bookmark save in the window, plus a re-root-in-flight control). All were red on the recorded
    mechanism; `LiveWalkerNavStampTests` is now 20/20.

##### ✅ `[A4-PARENT-CRUMB-VTABLE]` MED — the Parent (Outer) crumb claims `[child + 0]` with a dereference, so CE exports through a Parent hop read the child's vtable (FIXED IN SOURCE 2026-09-11)

`LiveWalkerViewModel.cs:2577-2584` (hand-verified): `FieldOffset = 0, IsPointerDeref = true`. The
e88190ba fix named exactly this shape as the defect ("a positive claim of `[UWorld + 0]`") and gave the
`-1` sentinel to two producers of an Outer hop. `GoToParentAsync` is the third, and was missed. So an
Instance-Finder object followed by Parent exports `[E+0]`, E's vtable, and every ULevel record is
applied to it. A GWorld spine passes the AA script's `FieldOffset >= 0` gate too.
- ⛔ **The finding's own fix is PARTLY HARMFUL.** The refuter modelled it in Python: stamping `-1`
  breaks the commonest Parent use, drilling Actor › RootComponent and pressing Parent. Both XML
  commands run `AnchorAtLastUnchainableHop` BEFORE `CleanBreadcrumbs`, so the `-1` re-roots the chain
  before the cycle collapse. A restart-stable GWorld chain would become a session-only address.
- ✅ **Safe fix, either of:**
  - (a) in `GoToParentAsync`, when `parentAddr` is already on the spine, truncate to that crumb, as a
    breadcrumb jump does; stamp `-1` only otherwise;
  - (b) run `CleanBreadcrumbs` before `AnchorAtLastUnchainableHop` in both export commands.
- Add both scenarios as tests. Do not special-case the name "Outer".
- ✅ **FIXED IN SOURCE 2026-09-11, safe shape (a)** (fix-pass batch B04).
  - **Outer already on the spine:** `GoToParentAsync` finds the last OBJECT crumb there (not a
    container view; no `ClassAddr`, which excludes inline structs and DataTable rows; the address is
    compared numerically) and makes Parent a breadcrumb jump to it.
  - **Otherwise:** it pushes the Parent crumb as an offset-less hop, `FieldOffset = -1`, the marker
    the other two producers already use.
  - **No special case on the name "Outer".**
  - **Red → green:** both scenarios, as the record asks.
    - An Instance-Finder root followed by Parent is now a `-1` hop, and the export re-anchors at
      the parent's own address. It was red: "Expected -1, Actual 0".
    - GWorld › PersistentLevel › Hero › RootComponent › Parent lands back on the Hero crumb, with
      RootComponent on the forward history and every hop a real offset (restart-stable). It was
      red: the spine had grown an `Outer` crumb.
  - ⚠ **The batch's adversarial review** (3 lenses, 12 agents): **6 survived, 3 refuted.** It was
    repaired in a follow-up commit.
    - **The Outer was the GWorld ROOT.** Parent from PersistentLevel on a GWorld spine jumped to
      the synthetic root, whose view is the cached actor list, not the UWorld object. That lost the
      UWorld's own fields and its further Parent. Now it skips the jump for that crumb and takes the
      `-1` hop with a live instance walk.
    - **A detour back onto the spine re-anchored a GWorld chain as session-only** (MED; the harm
      the record called PARTLY HARMFUL, reached by a detour). Parent to an Outer off the spine,
      then a drill back to Hero, makes a cycle, and both XML exports anchored at the `-1` BEFORE
      collapsing cycles. **The record's option (b) now applies as well:** both exports clean
      first, then anchor. The re-anchor note and log compare against the cleaned spine. BuildAaScript
      already cleaned first, so the three paths now agree.
    - **Pins added:** the container-view skip (Parent from an array element lands on the owner
      object), last-match (an Outer on the spine twice jumps to the nearest), and Forward after
      Parent returning to RootComponent. One test doc misattributed the `-1` history; it is
      corrected.
    - **Red → green:** the two defects (GWorld root, detour export) were red first, and the pins
      passed both ways as pins should. Mutation-checked: 4 mutants were all killed, restored by
      sha256.

##### ✅ `[A4-USMAP-ENUM-UNDERLYING]` MED — USMAP writes every EnumProperty's underlying type as ByteProperty (FIXED IN SOURCE 2026-09-11)

`UsmapExportService.cs:312-317` (hand-verified). The band header claims the file was *"checked byte for
byte against the two canonical writers"* (21ca54f8, audit #5 W1). But both vendored writers write the
enum's REAL underlying property: Dumper-7 `MappingGenerator.cpp:203-208` and RE-UE4SS
`Generator.cpp:250-254`.
- **Why it matters:** in unversioned cooked data an enum UPROPERTY is serialized as an integer of its
  own width (vendored UE 5.8 `UnversionedPropertySerialization.cpp:126-133`, `:261`). CUE4Parse takes
  that width from the mapping's inner type.
- **Consequence:** a `ENiagaraCoordinateSpace : uint32` field makes FModel / CUE4Parse read 1 of 4
  bytes and misalign every later property of that object. The vendored 5.8 tree has 101 non-uint8
  enums used directly as UPROPERTY types (136 properties).
- **Arm 3** (`:349-356`, a ByteProperty with an enum) is written as a bare ByteProperty, so every
  `TEnumAsByte` shows as a number instead of its name.
- **Arm 2** (container inners) is narrowed by the refuter: 5.8 serializes container enums as FName
  through `SerializeItem`, so its desync is doubtful.
- Not caught: the round-trip test reader skips the inner as one byte, and the register row W1/W7 only
  "loads" a mapping.
- ⚠ `docs/audit-2026-09-05-vendor-ue582.md:430` asserts that *".usmap has no underlying-type field"*.
  Both vendored writers contradict it.
- ✅ **Safe fix:**
  - Arm 3: emit the canonical fake `[26][0][enumName]`.
  - Arm 1: map `Size` 1/2/4/8 to Byte/UInt16/Int/Int64, falling back to Byte, never 0xFF.
  - Make the test reader recurse, and add a Size=4 case.
  - The exact fix is a DLL-side underlying-type key on `walk_class`.
- ✅ **Register:** add a step that PARSES an asset with a non-uint8 enum, using both our `.usmap` and a
  Dumper-7 one.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe fix** (batch B24).
  - **Arm 1:** the underlying type comes from the enum's `Size`: 1/2/4/8 map to Byte / UInt16 / Int /
    Int64 (`EnumUnderlyingTypeFor`). Anything else falls back to Byte, never the unmapped 0xFF. That is
    the width a consumer deserializes; the signedness still needs the DLL-side key.
  - **Arm 3:** a ByteProperty carrying an enum (TEnumAsByte) is written as the canonical
    `[26][0][enumName]`. The old ByteProperty arm, whose comment called the bare byte "correct for
    USMAP", is gone.
  - **Arm 2** (a container's enum inner) is left as recorded, with a comment. An inner carries no size,
    and 5.8 serializes container enums as FName.
  - The band header's "checked byte for byte" claim now names this one known difference. The audit
    note at `docs/audit-2026-09-05-vendor-ue582.md:430` carries an inline correction.
  - **Tests, red first:** a Size theory (2 / 4 / 8 red; 1 and the 3-byte fallback green both ways) and
    the TEnumAsByte shape. A plain ByteProperty is the control.
  - The round-trip reader now reads an enum's underlying type through its inner reader, not as one
    byte, and records each property's type, underlying type and enum name, as the fix asked.
  - 4/4 mutants killed; UI 5098/5098.
  - ⬜ **Review 3 (2026-09-12):** a container's TEnumAsByte inner is still a bare Byte, and this row's USMAP
    header had called that desync "doubtful". It is certain, and predates B24. Filed as
    `[A4-USMAP-CONTAINER-ENUM]` (MED, batch B32), which needs a DLL change. The header is corrected.

##### ✅ `[A4-CDOSCOPE-ANCESTOR]` LOW — the CDOSCOPE preview credits a live subclass only to the NEAREST preview class (FIXED IN SOURCE 2026-09-12)

`Aura.cpp:4985-5002`. `previewBaseOf` breaks at the first preview class on the super chain and
memoizes it. An exact instance short-circuits too. So an ancestor row reads *"(CDO default)"*, which
Aura.h defines as *"subclasses were searched and none was live"*, while Force and Freeze on the same
row act on N live instances. That is the disagreement CDOSCOPE was written to remove, reached through
its own untested chain walk. Modelled in Python: `Pawn · BaseEyeHeight` samples `Default__Pawn`.
- ✅ **Safe fix:** credit EVERY preview class on the chain, including for exact hits, starting from the
  super. Keep the per-class memo, and pin it as a pure helper.
- ✅ **FIXED IN SOURCE 2026-09-12, exactly that** (batch L17, with `[A4-CDOSCOPE-NESTED-PREVIEW]`).
  - The pure helper is `Aura::PreviewAncestorsOf`, and the per-class memo now holds its vector.
  - The Phase-2 sweep makes an object the exact sample for its own class, and a derived sample for every
    preview class above it, exact hits included.
  - **Tests, red first:** the helper in dll_core_test.
    - A live C credits both B and A.
    - An exact B still credits A.
    - A root credits nothing.
    - Controls: a non-preview class in between is passed over, and a self-loop terminates.
  - 3/3 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5215/5215.
  - ⚠ **Survivor by construction:** the sweep's wiring. No harness drives `SearchProperties`' preview
    phase; the rule it calls is pinned.

##### ✅ `[A4-CDOSCOPE-NESTED-PREVIEW]` LOW — CDOSCOPE removed the swap that kept Deep nested rows out of preview; container-element rows now show a wrong Preview (FIXED IN SOURCE 2026-09-12)

`Aura.cpp:5075` → `Ubel.cpp:6479`. A nested row's lookup key became its root field's defining class. A
Deep search matching both a direct field and a `Slots[].Count` leaf of the same class therefore
previews `inst + 0x08`, a UObject header word, sometimes with a source suffix. Aura.h:604, the band
comment and a69e23ba's body all promise that nested rows are never previewed.
- ✅ **Safe fix:** skip `isNested` rows in the Phase-2 loop.
- ⛔ **Unsafe:** restoring the swap, which brings back CDOSCOPE's proxy defect.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L17, with `[A4-CDOSCOPE-ANCESTOR]`).
  `ResolvePropertyPreviews` skips `isNested` rows, and Aura.h's promise now names the skip. The swap
  was not restored.
  **Test, red first:** dll_core_test previews a direct row and a nested row that share a class. The
  direct row is previewed and the nested one is not. 3/3 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5215/5215.

##### ✅ `[A4-PIVOT-CROSSGAME-ID]` LOW — Class Pivot restores the previous game's snapshot pick and class list into a different game (FIXED IN SOURCE 2026-09-12)

`ClassPivotViewModel.cs:479-520`, `:562`, `:672`. Snapshot ids are per-game-DB `AUTOINCREMENT`, and
`SetEngineState` switches the DB file but clears neither `_classCache` nor `_fieldCache`. So after a
reconnect to a different game:
- AF5's restore-by-Id preselects B#5 instead of the newest;
- A's cached class list is shown for it, under a normal status.

The prune comment *"a recaptured Id can't ever read a stale list"* holds within one file only.
Out-of-band twins that carry picks across a game switch by Id: `SnapshotViewModel.RefreshAsync :632-643`
and `SpcQueryViewModel.RefreshAsync :453-467`.
- ✅ **Safe fix:**
  - put the PeHash into both cache keys, captured at load start;
  - drop the kept selection and ticks only when the PeHash CHANGED;
  - fix the three twins together.
- ⛔ **Unsafe:**
  - clearing inside `SetEngineState`: it also runs on a same-game Extra Scan, which reopens AF5;
  - a clear alone: an in-flight load re-inserts A's list after it.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix, all three twins** (batch L18, with
  `[W1-PIVOT-LOADCTS]`).
  - Both Class Pivot caches are keyed by the game's PeHash, captured at load start. The prune is scoped
    to the current game.
  - Each view model records which game its list shows (`_listedPe`), and keeps the pick and ticks by
    Id only when the game is unchanged: Class Pivot's selection and discovery ticks, Snapshot's Diff and
    Group picks, SPC's ticks and predicates.
  - Nothing is cleared in `SetEngineState`, so a same-game Extra Scan still keeps AF5's pick.
  - SPC gained a `PendingRefresh` seam, as the other two had.
  - **Tests, red first:** one cross-game test per view model on the real per-game store. Each checks
    that the other game's newest or default wins, and Class Pivot's also checks that the other game's
    class list is never served. 5/5 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5225/5225.

##### ✅ `[A4-AB4-UINT64]` LOW — AB4's ordered-predicate verdict never covers the 64-bit members, so `Bigger -5` still drops every UInt64Property field (FIXED IN SOURCE 2026-09-11, batch B13)

`Radar.cpp:494-513`. `IntegerMemberRange` has no UInt64 or Int64 arm, and its premise ("one that
parsed as unsigned fits UInt64") is false for a negative target. So:
- UInt64 gets no entry at all, and every UInt64Property is silently skipped on first scan and pruned on
  refine;
- `Smaller 9223372036854775808` drops every Int64 field the same way.

The AB4 commit says *"Bigger -5 dropped every UInt16/UInt32/UInt64 field … the same verdict covers
it"*. Modelled in Python. `dll_helpers_test` asserts only UInt16/32.
- ✅ **Safe fix:** a UInt64 arm with an exact `scalar >= 2^64` upper test, and an Int64 arm with an
  exact `>= 2^63` test. Rewrite the false comment, and pin `FindEntry(UInt64)`.
- 🟡 **Records gap:** the AB4 "Between" residual is cited as "recorded in todo.md" by three sources
  and is NOT there. It survives only in `working-lessons.md:2805-2807`.
- ✅ **FIXED IN SOURCE 2026-09-11, the safe fix as written.**
  - `IntegerMemberRange` returns an EXCLUSIVE max (max + 1) and now lists Int64 (2^63) and UInt64
    (2^64). The verdict is `scalar >= hiEx`.
  - Why exclusive: INT64_MAX and UINT64_MAX have no double. Both round UP to exactly those values,
    so `scalar > (double)max` would miss the target 2^63 itself. For the narrow members `>= max + 1`
    is the old test unchanged, because `scalar` is always integral there.
  - The false comment is rewritten, and `FindEntry(UInt64)` / `FindEntry(Int64)` are pinned.
  - **Tests, red first:** 5 ⭐ in the AB4 block:
    - Bigger -5 → UInt64, and its predicate;
    - Smaller 2^63 → Int64;
    - Smaller 2^64 → UInt64 and Int64.
    Controls: Smaller -5, Bigger 2^63, Exact -5, and the narrow boundary Smaller 32768 → Int16,
    which also kills a `>` mutant.
  - The records gap is closed: the Between residual is its own row now, `[A4-AB4-BETWEEN]` below.
- ✅ **Review follow-up 2026-09-11** (the review of aaf6a022: 13 survived across B13 and the four
  follow-ups, all LOW; B13's share was 4).
  - **The sign leak survived for zero.** A '-'-prefixed target whose integer value is 0 ("-0", or a
    negative fraction that rounds to 0) never got an unsigned reading: the sign CHARACTER suppressed
    it, so every unsigned width was dropped, even under Exact, while the snapshot matcher kept them.
    `BuildNumericTargets` now takes an unsigned reading from a non-negative signed one (red first:
    3 ⭐ `AB4-SIGN`; `Exact(-1)` the control; a C# parity row).
  - **±Infinity:** `EveryValueSatisfies` refused a non-finite target, so for `Smaller Infinity` the
    live matcher (now reading the verdict) kept every integer leaf and the snapshot mirror kept none.
    It now refuses only NaN (red first: two C# rows).
    - ⬜ Older and separate, recorded here only: FLOAT leaves still differ on a non-finite target (the
      DLL encodes it, `TargetFitsWidth` rejects it), and "1e400" is an error in the DLL (`stod` throws)
      but +Infinity in .NET. Degenerate input, both sides.
  - **GROUPREFINE** gained an unsigned 0 under `Bigger -5` and an Int16 32767 under `Smaller 70000`:
    a refine comparing an AlwaysTrue entry's ZEROED bytes passed the old cases (3 > 0).
  - 3/3 mutants killed. UI 5033/5033 (one suite run over the three second-round follow-ups together).

##### ✅ `[A4-REROOT-STALE-WARNING]` LOW — every re-root overwrites UpdateDisplay's freed/recycled warning with the Back hint (FIXED IN SOURCE 2026-09-12)

`LiveWalkerViewModel.cs:2822` (+ `:2758`). This is the path the stale warning itself names as common:
Snapshot and Pivot handoffs, 13 cross-tab handlers, the Go box, Find Refs' Open. The freed object opens
to an empty grid with *"← Back returns to X"*, or with nothing at all. The same commit, e0dec505, wrote
the opposite ordering rule into the GoBack twin.
- ✅ **Safe fix:** compose at both sites; never assign `""` over a status.
- ⛔ **Unsafe:** moving the hint before the walk, which loses the only return path when the user needs
  it most.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L07, with `[P1-WALK-UNREADABLE]`).
  - Both re-root sites compose. The Go box path keeps what UpdateDisplay said (`_reRootWalkStatus`) and
    appends the Back hint; the Find Refs Open composes the same status with its "Opened … · Back" line.
  - `ComposeReRootStatus` never drops either half, and never assigns "" over a status.
  - **Tests, red first:** a first re-root onto a freed object keeps the warning. A later one keeps the
    warning AND the way back. An unreadable walk says so. A theory pins the compose rule.
    6/6 mutants killed; UI 5204/5204.
  - ⚠ **Survivor by construction:** the Find Refs Open site's compose. No harness drives
    `OpenReferenceOwnerAsync` end to end, but the helper it calls is pinned.

##### ✅ `[A4-LW-DISCONNECT-PARENT]` LOW — X5's ClearOnDisconnect leaves the Parent button, the References header and the function list (FIXED IN SOURCE 2026-09-12)

`LiveWalkerViewModel.cs:5805-5833`. The method promises *"a reconnect never shows an object (and its
live addresses) from the previous game"*. But `HasParent` / `CurrentOuter*`, `HasReferences` and
`_allFunctions` survive:
- the Parent button stays live, and after a reconnect it walks the previous process's Outer address
  under a stale crumb;
- the Functions filter brings the old game's UFunctions back.

X5's live PASS was rooted at UWorld, where `HasParent` is false, so it could not see this.
- ✅ **Safe fix:** call `ClearDisplayedNode()` + `ClearReferences()`, and clear the function list.
  `Flush()` the keyword memory first if the filter box is blanked.
- ✅ **FIXED IN SOURCE 2026-09-12, exactly that** (batch L19, with `[A1-DETECT-REPUBLISH]`).
  - `ClearOnDisconnect` goes through `ClearDisplayedNode()` and `ClearReferences()`, and it clears the
    FULL function list (`_allFunctions`, not only the visible one) and `HasFunctions`.
  - The filter box is not blanked, so there was nothing to `Flush()`.
  - **Test, red first:** a walker rooted on an object with an Outer and a loaded function list is
    disconnected. The Parent button, the References header and the function list stay gone, even after a
    filter edit rebuilds the list. 7/7 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5218/5218.

##### ✅ `[A4-PUSHCE-UNPADDED]` LOW — the batch "Push CE Field(s)" still pushes the unpadded FieldAddress (FIXED IN SOURCE 2026-09-12)

`LiveWalkerViewModel.cs:4800-4802`. `[CEPATHS-UNPADDED-2026-09-09]` moved HEX and +CE to
`PayloadAddress` and claims *"both CE-facing handlers"*. The batch push is a third, self-described as
*"the multi-select batch form of the per-row +CE"*. On a checked build a delegate row lands on the
access detector.
- ✅ **Safe fix:** `field.PayloadAddress` at `:4802` only. Correct the stale doc at `:5479-5480`, and
  add a caller-level test.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L03a, with `[W5-CSX-DELEGATEPAD]`).
  - `PushCeFieldToCeAsync` sends `field.PayloadAddress`, as the per-row +CE and HEX already did.
  - **The stale doc** was `AddFieldToCeAsync`'s summary: "batch adds go through Copy CE Field". It now
    names the batch push, and says it sends the same payload address.
  - **Test, red first:** a caller-level push through a recording bridge. A delegate row with pad 8 sends
    its payload; an IntProperty row sends its field address unchanged. 5/5 mutants killed; UI 5162/5162.

##### ✅ `[A4-GAMEONLY-ADVICE]` LOW — Interesting Functions and Console advise "Game classes only" when it is already ticked (FIXED IN SOURCE 2026-09-12)

Interesting Functions `:650-665`, Console `:287-293`. This turns the P5 lead into a measured defect.
Z10's written rule, *"only while it is still OFF"*, reached Property Search only (same commit
a87706c7). Interesting Functions defaults the box to ticked, so its only advice is already applied.
Two tests PIN the wrong text. The panels' checkbox actually reads "Game Only".
- ✅ **Safe fix:** carry the scan-time GameOnly in `LoadScanFacts`, capture Console's before its
  await, and invert the two tests. Never read the live checkbox.
- ✅ **FIXED IN SOURCE 2026-09-12, exactly that** (batch L21).
  - `LoadScanFacts.GameOnly` and Console's local capture the scan-time value before the await.
  - The advice names the panels' own label, "Game Only", and only while it was off. With it on,
    Console says it is already on.
  - The two pinning tests are inverted, and each has a control. 6/6 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5229/5229.

##### ✅ `[A4-STEALTH-PRIME]` LOW — BADGEPRIME's connect prime skips the Stealth card, and gate 17d cannot see it (FIXED IN SOURCE 2026-09-12)

`TeleportViewModel.cs:2330-2331` / `:2373-2399`. The fix promises a *"read-back of EVERY badge the
disconnect branch resets"*. Stealth is reset with a tuple assignment and never primed.
- So after a reconnect the card reads "Off" while Solide keeps holding the meter.
- The M9 experimental gate-off keys on `StealthState` and therefore releases nothing.
- Gate 17d matches only `Apply\w+State(-1)`, so it prints `CHECK OK` (measured).
- Adjacent: Property Search's `ClearOnDisconnect` empties the Forced-fields strip claiming "holds die
  with the process", which is false for a pipe drop.
- ⛔ **Unsafe:** treating any numeric job at 0 as the Stealth hold. It may be a Property Search Force,
  and gate-off would then release it.
- ✅ **Safe fix:** intersect `find_stealth_meter`'s candidates with `get_forced_fields`, and fall back
  to an honest "Unknown". Teach gate 17d the tuple form.
- ✅ **FIXED IN SOURCE 2026-09-12, exactly that** (batch L20).
  - `ApplyStealthState(-1/0/1)` gives the card the shape every other badge has. The disconnect branch
    sets it Unknown, not "Off".
  - `RefreshHeldStealthStateAsync` joins the connect prime:
    - a numeric 0-hold on one of the meter's candidates reads Holding, and re-arms the card's candidate so
      Reset and the gate-off release it;
    - nothing forced at all reads Off;
    - anything else stays Unknown — never "any numeric job at 0".
  - Gate 17d now reads the tuple form too. Its `--selftest` gained a third control, a tuple reset nothing
    primes.
  - The adjacent Property Search comment is corrected: holds survive a pipe drop.
  - **Tests, red first:** a still-held meter, a Property Search force that must not be claimed, and a
    disconnect that says Unknown. A control: nothing forced reads Off. The old disconnect test's "Off"
    was the defect, so it became the red. 4/4 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5221/5221.

##### ✅ `[A2-SENTINEL-OVERREAD]` LOW — the intrusive-optional gate reads 16 bytes from a field that can be 8 (found 2026-09-12 by review 6; FIXED IN SOURCE 2026-09-12)

`Aura.cpp` ScanForValue V1c reads a FIXED 16 bytes before testing an intrusive optional's sentinel
(`[A2-TOPTIONAL-VALUESCAN]`, batch L41). An intrusive optional is exactly `sizeof(T)`, so an intrusive
`TOptional<FName>` is 8 bytes (12 under case-preserving names). When such a field sits within 16 bytes
of an unmapped page the SEH-guarded read fails, the `||` short-circuits to `continue`, and a **SET**
optional is silently absent — on some instances of a class and not others. FString (16) and FText
(≥16) never over-read, so NameProperty is the only affected type. The verifier confirmed it and
narrowed it: no crash and no garbage value, only the silent miss.
- ✅ **Safe fix:** the sentinel declares its own span (`Ubel::SentinelBytesNeeded`: 16 / 4 / 8 / 0) and
  the gate reads exactly that, capped by `sf.size`, the field's own width, which it already holds.
- ⛔ **Unsafe:** widening the buffer (keeps the over-read) or dropping the read (ungates every
  intrusive optional, which is what L41 fixed).
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L46): `Ubel::SentinelBytesNeeded` (16 / 4 / 8 / 0) and a gate
  that reads `min(need, sf.size)`. Red first: dll_helpers_test's V1C block pins all four counts against
  an inert helper claiming 16 for everything, and the InvokeScriptTests source pin now requires the
  gate to use it and refuses the flat `sizeof(v16)` read.

##### ✅ `[A2-TOPTIONAL-REFINE]` MED — Value Scan's REFINE re-reads a V1c optional with no gate (found 2026-09-12 by review 6; FIXED IN SOURCE 2026-09-12)

The first scan gates a `TOptional` leaf on its `bIsSet` byte, but `RefineCandidates` re-reads each
candidate purely by its stored absolute address — `Radar::FieldDescriptor` carries no optional member
at all. UE's `MarkUnset` clears the flag and zeroes no bytes, so a reset trailing-flag optional still
reads as its old value: a Next Scan with **Unchanged** (or **Exact** the stale value) keeps the row,
and the panel shows a value for a slot the engine considers empty. This is the lead L41 recorded and
did not trace; review 6 traced it end to end and it is reachable from the UI's Next Scan button.
- ⚠ **The verifier corrected two overstatements:** the row is NOT uneliminable (Changed / Increased /
  Decreased / a different Exact all drop it), and the 5.5+ intrusive string case is largely
  self-healing — `Ubel::ReadFString` returns `""` for a null Data / zero Count and never dereferences
  a dangling pointer. The residual string case is the converse: a reset optional whose previous value
  was `""` survives an Unchanged refine.
- ✅ **Safe fix:** carry the same two facts the first scan uses (`optionalFlagOffset`, and the sentinel
  as its `int8_t` base — Radar.h includes no project header) on `FieldDescriptor`, stamp them in
  `ensureDescriptor`, and apply the identical gate in `RefineCandidates` before any re-read. V1c emits
  only Direct anchors, so `c.addr` IS the value address and the flag is at `c.addr + flagOffset`; a
  group session leaves the members at their defaults (audit #5 A12) and is unaffected.
- ⛔ **Unsafe:** dropping every optional candidate at refine — a SET optional is a true hit.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L47), the safe fix above: `FieldDescriptor` carries
  `optionalFlagOffset` + `optionalSentinel`, `ensureDescriptor` stamps them, and `RefineCandidates`
  applies the same gate before any re-read (with `SentinelBytesNeeded`, not a flat 16 — L46).
  - **Red first:** dll_core_test's new **REFINEOPT** block drives `RefineCandidates` over a hand-built
    session. A reset optional and an intrusive unset one are dropped; a SET optional, an intrusive set
    one and an ordinary leaf survive as controls. It fakes no pool — refine reads plain process memory,
    so a static buffer is the object — and building the pools by hand is itself the point: the gate has
    to live on the DESCRIPTOR, the only per-field thing a refine is handed.
  - An InvokeScriptTests source pin covers the stamping in `ensureDescriptor`.

##### ✅ `[A1-REVIEW6-PINS]` LOW — two stale decision comments, and a scan gate with no guard-the-guard (found 2026-09-12 by review 6; FIXED IN SOURCE 2026-09-12)

- `Mimic.cpp:326` still says the exemption list is *"today only CMD_FOREGROUND, whose handler is pure
  Win32"* after `[W5-OFFSETS-MAILBOX]` added a second exemption whose handler is not; and
  `dll_helpers_test`'s *"Exactly ONE exemption"* comment sits directly above
  `EXPECT("exactly two commands are init-exempt", ...)`. Mimic.h's own block was updated, which is
  what makes the other two stale rather than merely old.
- `InvokeScriptTests.DllLogCalls_NeverFormatAWideString` ends on `offenders.Count == 0` with nothing
  asserting the scan matched anything, so renaming the logging entry points it keys on (or adding a
  DLL source in a subdirectory — the enumeration is non-recursive) makes it pass forever. Both sibling
  source scans added in the same range guard their scans explicitly.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L49): both comments now name **both** exemptions and give each
  its own reason, and the `%ls` gate counts the files it scanned and the log-call lines it recognised.
  - **Red first for the comments:** a source pin requires both to name `CMD_FOREGROUND and
    CMD_OFFSETS_VERDICT` and refuses the old text.
  - **The guard-the-guard passes the moment it is written**, so its red IS the mutation check: one mutant
    stops the enumeration finding sources, another stops it recognising the loggers, and both turn the
    gate red. Recorded here because "red first" means something different for a test-gap item.


| # | row | sev | commit | offline evidence |
|---|---|---|---|---|
| 40 | `[W2-TPREL-MAP]` | MED | `git log --grep W2-TPREL-MAP` (batch B22) | 5 tests red first (the ParsePose key, the directional TP keeping map and source, the add-time read, the connect prime, an unknown map); the reported-map control green both ways. 11/11 mutants killed; UI 5063/5063 |
| 72 | `[A4-STEALTH-PRIME]` | LOW | `git log --grep A4-STEALTH-PRIME` (batch L20) | TeleportViewModelTests, red first: a still-held meter primes Holding, a Property Search force is not claimed (Unknown), and a disconnect says Unknown; nothing forced reads Off. Gate 17d reads the tuple form (a third selftest control). 4/4 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5221/5221. The recorded safe fix |

| # | row | the check | needs |
|---|---|---|---|
| L26 | `[W2-TPREL-MAP]` | A connected game with a Coordinate Library for the current map:
1. **Directional TP:** use TP facing. The Current Pose Map row keeps the map name, and the library rows keep their distances — no "⚠ different map (you are on '')".
2. **Fresh connect:** reconnect the UI and do NOT press ↻. The map shows at once. *Add from fields* saves an entry carrying the map (its Map column).
3. **Main menu:** connect with no pawn. The library rows are not flagged as another map's. ✅ **STEPS 1-2 PASSED 2026-09-12**; ✅ **step 3 PASSED 2026-11-20** `[LIVEPASS-2026-11-20]` on this fixture after all (DLL and AOT UI 3776, DumperTest Shipping): the pawn was removed with `ClientRestart(nullptr)` on the live PlayerController (L25's method; `get_movement_params` and `teleport_get_pose` → code -3). *Control first, pawn present:* Coordinate Library (5), *Current map only* ticked, five `ThirdPersonMap` rows, none flagged. Then **Disconnect → Connect, ↻ never pressed**: Current Pose read `—` for every value and an empty **Map**, and the same five rows were listed under *Current map only* with no *"⚠ different map (you are on '')"*. The pawn is not respawned (the game was killed afterwards). The 2026-09-12 note "not reachable on this fixture" follows. DumperTest Shipping (pid 23232, DLL `1.0.0.3546`) + AOT UI, with a 4-entry library for the current map `ThirdPersonMap` (all `Dist 0`, the pawn standing on the saved spot). **1. Directional TP:** *Teleport forward* (Distance 100 uu, "Horizontal only" ticked) moved the pawn from X **900 → 1000.000** with Y/Z unchanged — so the step size is confirmed, not assumed. Afterwards the Current Pose row still read **`Map: ThirdPersonMap`** (kept, not blanked), and all four library rows kept their Map column and now showed **`Dist = 100`**, exactly the configured step. No *"⚠ different map (you are on '')"* anywhere. **2. Fresh connect:** Disconnect → Connect, and **↻ was never pressed** — the Map row read `ThirdPersonMap` at once. *+ From fields* then created `ThirdPersonMap 5` from the (0,0,0) fields and it **carries the map**, confirmed in three independent places: the grid's Map column, the detail line *"ThirdPersonMap 5 — ThirdPersonMap — 1,497 uu away"*, and the on-disk entry `map='ThirdPersonMap'`. ⬜ **Step 3 needs a no-pawn state on a game that ALSO owns a library for its own key.** DumperTest boots straight into `ThirdPersonMap` with a pawn and has no main menu, and the CheatManager pawn-destroying execs cannot supply one: Shipping has no live `CheatManager`, and in a cooked build its bodies are stripped (working-lessons §1.1a). The natural host is EVERSPACE 2 — its title screen genuinely has no live `PlayerController` — but its library would have to be built in-game first, so it belongs to a future ES2 trip. | a game + UI |
| L58 | `[A4-STEALTH-PRIME]` | A game where the stealth meter auto-finds (experimental gate on):
1. Detect, then Hold @0. Restart the UI with the game still running and reconnect. The Stealth card reads "Holding @0", and Reset releases it.
2. With only a Property Search Force active, the card reads "Unknown", and turning the experimental gate off does not release that Force. ✅ **PASSED 2026-09-23 — RED → GREEN, BOTH steps on DumperTest Shipping 5.4.** ✅ **The optional M9 check, run 2026-11-20** `[LIVEPASS-2026-11-20]` (DLL and AOT UI 3776, DumperTest Shipping): Stealth card **Hold @0** (`force_field: … StealthDetection … value=0.0000` at 14:49:28), then the System tab's experimental checkbox unticked: the game's `pipe-0.log` logged `reset_field: class=DumperTestStealthComponent field=StealthDetection` at 14:50:08.008, then `set_foreground_lock enable=0`; `get_forced_fields` → `[]`; re-ticked, the card read **Off** with no value in its readout. The gate-off releases a Holding card, as step 2's inverse requires. Step 1 was "fixture-limited"; the 2026-09-22 fixture change (`UDumperTestStealthComponent`, attached to the player pawn, `float StealthDetection` rising 0.1/s and wrapping at 1) made it reachable. With the UI closed, `find_stealth_meter` → code 0, top candidate **`DumperTestStealthComponent::StealthDetection`** (score 19, `FloatProperty`), then `SkeletalMeshComponent::CapsuleIndirectShadowMinVisibility` (5). **GREEN** (`dist\UE5Dumper.dll` + AOT UI `b1ea2660`; experimental gate on). *Step 1:* Teleport → Stealth Meter → **Detect meter** → badge *Ready*, readout `DumperTestStealthComponent::StealthDetection = 0.3`. **Hold @0** → *Holding @0*; game `pipe-0.log`: `force_field: class=DumperTestStealthComponent field=StealthDetection kind=numeric value=0.0000`. UI closed and relaunched, Connect, wait for the prime: the card read **"Holding @0"** with the readout **`DumperTestStealthComponent::StealthDetection = 0 (held)`**. **Reset** → badge *Off*, `reset_field: class=DumperTestStealthComponent field=StealthDetection`. ⚠ The readout kept saying *"= 0 (held)"* after the Reset: raised as `[STEALTH-RESET-STALE-HELD]` (LOW). *Step 2:* Properties → `F32` (Game classes only) → right-click the DumperTestActor / F32 row → *Force field (hold across instances)* → *Force value…* → **0** → *Hold this value* (a numeric force AT 0 on a non-candidate, as trap 1 requires). The status read *"✓ Holding DumperTestActor::…"* and the strip showed `DumperTestActor :: F32 (1 held)`; `force_field: class=DumperTestActor field=F32 kind=numeric value=0.0000`. UI restart → the Stealth card read **"Unknown"**. Unticking the experimental gate sent only `set_foreground_lock enable:false`: **no `reset_field`** for F32. Re-ticking it, the forced strip still listed `DumperTestActor :: F32 (1 held)`. *Control:* forced strip *Clear all* (`reset_all_fields`) → reconnect → the card read **"Off"**. **RED** (the pre-fix UI, `git archive a28ed440^` → `dotnet build`, run from `dist\` via `dist_swap.py`; HEAD's DLL injected from a byte copy in `out\`; a fresh process). Detect → Hold @0 → *Holding @0* (`force_field … StealthDetection … value=0.0000` at 11:33:01). UI restart → the card read **"Off"** with readout `—`, over a LIVE hold. The game's `pipe-0.log` received no `get_forced_fields` and no `find_stealth_meter` from that UI after its `init`, so "Off" was its initial value, not a reading. With the UI closed, `get_forced_fields` then returned `[{class_name: DumperTestStealthComponent, field_name: StealthDetection, held: 1, kind: numeric, value: 0.0}]`, and `reset_all_fields` released it. ⬜ Not run: the optional M9 gate-off-releases-a-Holding-card check. Every force released; `dist\` restored and verified (16 files, 0 mismatches, `UE5DumpUI.exe` `b1ea2660`); `ui-options.json` restored; `experimental.json` byte-identical to its backup (on). Game and UI closed. | a game + UI |

| id | sev | what | where |
|---|---|---|---|
| `[WALK-NULLCLASS-SILENT]` | LOW | ✅ **FIXED 2026-10-27** (red `a3bf584c` (core) + `4411fbde` (UI)` / green `55c98015` (core) + `b0e856fa` (UI)`): a walk that resolved its own class and found a null or unmapped ClassPrivate returns `no_header` (lean and full) with a Sein warning; Live Walker says "No UObject header at this address — freed, or not an object" and never retries fill_gaps. ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (DLL and AOT UI 3776, DumperTest Shipping): two pages allocated in the game from outside -- a committed zero page (ClassPrivate 0) and a committed page whose +0x10 points at a reserved, uncommitted page (ClassPrivate unmapped). Over the pipe, `walk_instance` full and lean on each: `ok`, `no_header: true`, no fields, and per call one walk-0.log WARN *"WalkInstance: 0x… has no UObject header (ClassPrivate is null)"* / *"(ClassPrivate 0x… is unreadable) -- freed, or not an object; nothing walked"* (the unmapped arm also logged `WalkClass: … is not readable at +0x58` once). In the UI, Live Walker's Go to each address showed *"⚠ No UObject header at this address — freed, or not an object. Re-open it from 🌍 GWorld or the finder."*; the UI pipe log holds ONE `walk_instance` per address (`no_header:true` in the reply) and view-0.log *"Skipping fill_gaps auto-retry … no UObject header"*. Pages released afterwards. **Raised by L47's first attempt, 2026-09-23.** A walk on a COMMITTED address whose header reads `ClassPrivate` = 0 comes back as `{…,"class":"","class_addr":"0x0","fields":[],…,"name":"None",…}`, with no `stale` and no `unreadable` key. Live Walker then shows a blank grid headed `None` and **no status at all**. **Measured:** l47's rig RELEASED a page holding a cloned UObject, and the running DumperTest re-committed that 64 KB region within about 25 s, as all zeros. The next Refresh read that reply (`ui-pipe-20260923-083950.log:69`, 08:39:50.564), and the walk log logged a plain `WalkInstance 'None' (): 0 fields` (`walk-20260923-084409.log:139`). **Why it slips through:** `GetClass` returns 0, `WalkClass(0)` yields `PropertiesSize` 0, and `IsPlausiblePropertiesSize` accepts it (`>= 0`, `Ubel.h:604`), so the stale gate at `Ubel.cpp:~4431` never fires. A live UObject never has a null `ClassPrivate`, so this is the recycled-slot case that `[A4-REROOT-STALE-WARNING]`'s *"freed/recycled"* text exists for, in its most common shape: a zeroed allocation. ⚠ **The sibling shape, the same silence:** a header whose `ClassPrivate` is non-null but UNMAPPED. `WalkClass()` at `Ubel.cpp:4421` discards the `readOk` verdict that `[A2-WALKCLASSEX-UNMAPPED]` added (`WalkClassImpl`, `:1008-1051`). The fault exit returns `PropertiesSize` 0, the plausibility gate accepts it, and the reply is again classless, fields-empty and flag-free. **Fix, with its limits.** Mark the walk when no class was PASSED and either (a) the header's class is 0, or (b) the class read faulted (use `WalkClassImpl`'s `readOk` in place of `WalkClass()`). A raw-struct walk always passes its class, and only Live Walker reads `IsStale` (`AutoFillGapsRetryAsync`, `UpdateDisplay`), so the other callers are unaffected. ⚠ **But do NOT reuse the stale text:** the Go box accepts any address, `module.exe+RVA` included, and a first Go to something that was NEVER a UObject would be told it *"appears to have been freed/recycled"*. That is the over-claim this repo keeps refusing (`[FTEXT-REFUSAL-OVERCLAIM]`). The UI's stale branch also logs *"(implausible PropertiesSize)"*, which would be false here. So: a distinct key and wording that covers both readings (e.g. *"no UObject header here — freed, or not an object"*), plus a DLL `Sein::Warn` naming the reason. Red tests: a no-class walk of a readable zero buffer and of an unmapped-class header both report the new verdict, a raw-struct walk with an explicit class does not, and no text claims *"implausible PropertiesSize"*. | `dll/src/Ubel.cpp` `WalkInstance` (the `GetClass` at ~:4343 and the stale gate at ~:4431); `dll/src/Ubel.h:604` |
| `[SNAPDIFF-SHADOWED-PROP]` | LOW | ✅ **FIXED 2026-10-27** (red `284d9624` + `4882d67e` / green `ae0ca0cd`): the diff's join key and SPC's InSession key carry prop_offset (Strict already did); Loose stays name-only on purpose, matching across sessions and patches where an offset may move. SPC InSession had MISSED the real change. ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (AOT UI 3776, DumperTest Shipping, one game session): snapshots `14:10:14` (#6) and `14:41:29` (#7), each 719 objects / 17,040 fields, NumericAll, game objects only, and each holding both shadowed pairs (`MovieSceneCinematicShotSection` #21374 and `TemplateSequenceSection` #22591, `PrerollTime` at +196 `00000000` and +296 `FFFF7FFF`; 4 rows per snapshot, read-only SQLite). **Snapshot Diff** #6 → #7: *"8 changed · +0 added · −0 removed"* -- no `PrerollTime` row, and the removed count is 0, not −2. **SPC Query, Join In-session**, the pair ticked, #7 Compare = Changed: *"8 match across 2 snapshots (in-session)"*, the same 8 rows (`TickCount` 99 → 1974, `F32_Ticking`, `F64_Ticking`, `FrameCountReflected`, `Health.CurrentValue`, the Stealth component's two, the ABP's `InternalTimeAccumulator`). Second witness: a SQL join of #6 and #7 keyed with `prop_offset` returns exactly those 8 rows. **Raised by L65's run, 2026-09-23.** Snapshot Diff's hash join keys a row by `(class_fqn, gobjects_index, prop_name, array_field, elem_index)` and assumes that key is *\"unique within one snapshot\"* (`SnapshotStore.cs:937`). It is not, when a class carries two properties with the same NAME at different offsets (a shadowed name). **Measured** on DumperTest Shipping 5.4, snapshots #3 and #4 (`Snapshot 2026-09-23 10:35:45` / `10:35:53`): `MovieSceneCinematicShotSection` #21363 and `TemplateSequenceSection` #22578 each hold two `PrerollTime` rows, at +196 (`00000000`) and +296 (`FFFF7FFF`), so each snapshot has 16,942 rows but only 16,940 distinct keys (read-only query, `immutable=1`). **What it costs:** A's map keeps only the last row per key while every B row is matched, so the +196 B row is compared against A's +296 value. Result: **phantom \"changed\" rows** for values that did not change (the two sequence classes in L65's noise picker), and `RemovedCount = aMap.Count - matched` goes **negative**. The status read *\"10 changed  ·  +0 added  ·  −-2 removed\"* (`SnapshotViewModel.cs:1505`, `SnapshotStore.cs:1051-1052`). **Safe fix:** add `prop_offset` to the join key on both sides; a red test with two same-named props at different offsets must show 0 changed and 0 removed. Check the other consumers that key by prop name (SPC, Pivot, Group) for the same assumption. | `ui/UE5DumpUI/Services/SnapshotStore.cs:930-1052`; `ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1505` |
| `[FINDINSTANCE-SUBSTRING-USERFACING]` | LOW | ✅ **FIXED 2026-11-03**: derivation first, substring only for a name no class has; the census row of the same tag has the details. **Raised by `[SEETHRU-PROBE-SUBSTRING]`'s fix, 2026-09-23 (read from source, not measured).** The USER-facing "an instance of class X" lookups still gate on a class-name SUBSTRING and fall back to the FIRST matching CDO: the C ABI `UE5_FindInstanceOfClass`, which the pipe's `invoke_function class_name` calls (the Console's by-class exec passes `className`, `ConsoleViewModel.cs:480-486`), and Mimic's `FIND_INSTANCE` / `LIST_FUNCTIONS`, which copy its shape for CE. That fix moved every INTERNAL caller to `Aura::FindLiveOrDefaultOf` and left these, because what a user-facing lookup returns is a contract decision (the mailbox's is versioned in `Mimic.h`). **What it costs:** on a stock 4.27+ development build with GAS, a Console exec on `CheatManager` can resolve a live `UAbilitySystemCheatManagerExtension` at a lower GObjects index and answer *Function not found*; a partial name keeps matching whatever contains it, which a CE script may rely on. **Fix candidates (not decided):** exact name + derivation (`FindLiveOrDefaultOf`) for all three, or derivation first and the substring only when no class has that exact name. | `dll/src/Frieren.cpp` (`UE5_FindInstanceOfClass`); `dll/src/Fern.cpp` (`invoke_function`, `class_name`); `dll/src/Mimic.cpp` (`HandleFindInstance`, the `LIST_FUNCTIONS` class-name branch) |
| `[STEALTH-RESET-STALE-HELD]` | LOW | ✅ **FIXED 2026-10-27** (red `2401c60c` / green `4118e264` + `4012e4c3`): Reset leaves `{Class}::{Field}` without the value, and with no meter says nothing was released (en.axaml `str.TP.StReleased` / `StNothingToRelease`). ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (DLL and AOT UI 3776, DumperTest Shipping, experimental gate on): Teleport → Stealth Meter → **Detect meter** (*Ready*, `DumperTestStealthComponent::StealthDetection = 0.833`) → **Hold @0** (*Holding @0*; game `pipe-0.log` `force_field: … StealthDetection kind=numeric value=0.0000`). UI closed by WM_CLOSE with the game running, relaunched, Connect: the card primed to **Holding @0**, readout **`… = 0 (held)`**, holder badge *"Held by a program that has exited (pid 39196)"*. **Reset** → badge **Off**, readout **`DumperTestStealthComponent::StealthDetection`** with no value and no "(held)", the tab's status line *"Released the stealth-meter hold."*, `reset_field: class=DumperTestStealthComponent field=StealthDetection` in the game's log, and `get_forced_fields` → `[]`. **Raised by L58's run, 2026-09-23.** After a UI restart, the Stealth card's connect prime sets the readout to `{Class}::{Field} = 0 (held)` (`TeleportViewModel.cs:2484`, added by `a28ed440` `[A4-STEALTH-PRIME]`). **Reset** then releases the hold (`reset_field: class=DumperTestStealthComponent field=StealthDetection`) and sets the badge to *Off* and the status to *\"Released the stealth-meter hold.\"*, but `ResetStealthAsync` (`:1880-1895`) never touches `StealthFieldText`. So the card shows badge *Off* over a readout that still claims *\"= 0 (held)\"*. Measured on DumperTest Shipping 5.4 with the AOT UI `b1ea2660`. Without a restart the readout was the Detect value (`= 0.3`), so only the primed path is affected. **Safe fix:** Reset clears the \"(held)\" readout (e.g. back to `—`, or the candidate's name without a value); red test: prime a held field, Reset, and the readout must not contain \"(held)\". | `ui/UE5DumpUI/ViewModels/TeleportViewModel.cs` `ResetStealthAsync` (~:1880) and `:2484` |
| `[WALK-REFRESH-SCALAR-SHRINK]` | LOW | ✅ **FIXED 2026-10-27** (red `3629c0f9` / green `dbb989b2`): Refresh re-reads a drilled scalar array in full through the drill's own helper and re-derives the status line and crumb for array / map / set / DataTable views. Left: `RepopulateContainerView` ignores a container whose count dropped to 0 (filed below). ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (AOT UI 3776, DumperTest Shipping): `Arr_Churn` grown to **5,004** over the pipe (`V1a_GrowContainers(5000)`), drilled at Array Limit **16384**: crumb `Arr_Churn [5004 x IntProperty] ⚠ showing 4,096 of 5,004`, header `Arr_Churn ⚠ showing 4,096 of 5,004`, status *"Showing the first 4,096 of 5,004 elements — this view is capped at 4,096 per fetch."* Slider moved to **64**: the view kept the full re-fetch and all three still read 4,096 of 5,004 (was: header `showing 64 of 5,004`, status cleared, crumb stale); a **Refresh at the unchanged limit** kept them too. ⚠ Found on the way: several Refreshes in flight at once (each slider step runs one) write the status sentence once EACH, joined by " — " -- filed as `[LW-REFRESH-STATUS-DUP]` (census Table A). The slider was restored to 64. **Raised by L60's run, 2026-09-23; promotes the unfiled lead \"Live Walker refresh inside a container view\" (Leads, not filed).** A drilled scalar array whose walk preview came back short is re-fetched in full, up to the DLL's 4,096 per-request cap (`LiveWalkerViewModel.cs:1368-1376`). The preview is short when the array is longer than the Array Limit, or longer than 4,096, because the walk's inline read has the same cap (`Ubel.cpp:42`, `:4783-4786`). Any Refresh of that view renders only the parent walk's inline elements, which is min(Array Limit, 4,096) rows. `RefreshAsync` (`:5387`) calls `ClearStatus()` (`:5421`). Its container branch (`:5429`) re-walks the PARENT with `arrayLimit: ArrayLimit` (`:5462`) and hands the matched field to `RepopulateContainerView` (`:5474` → `:1740-1759`), which never re-fetches and is given no crumb. So a drill and a Refresh at the same limit disagree: the slider does not govern a scalar drill (`[A3-CONTAINER-4096-ADVICE]`), and a Refresh re-applies it. **Measured** on DumperTest Shipping 5.4 with the AOT UI `b1ea2660`, through the slider: `Arr_Churn` grown to 5,004 and drilled at Array Limit 16384 showed *\"Showing the first 4,096 of 5,004 elements — this view is capped at 4,096 per fetch.\"* Moving the toolbar slider to 64 re-rendered the header as `Array<IntProperty>` · `Arr_Churn  ⚠ showing 64 of 5,004`; the slider reaches the setter through `MainWindowViewModel.cs:402-407`, and the setter (`:280-291`) runs Refresh. The status line was cleared, and the crumb still read `⚠ showing 4,096 of 5,004`. No log line records it, because the container branch logs nothing. **Code-read, not measured:** a Refresh at an UNCHANGED limit, and the other entry points: the Refresh button, Auto refresh, the Preview Limit slider (`:294-306`), the Guess? toggle (`OnFillGapsChanged`, `:817-823`), and an inline element edit, where `CommitFieldEditAsync` calls `RefreshAsync` (`:5778`). An edit of element [N] with N ≥ Array Limit would make the edited row vanish right after \"Written: …\". The cleared status and the stale crumb are not scalar-specific: every container view loses its drill-time status on a Refresh, and the branch writes back neither `containerBc.Label` nor `ContainerField`. This qualifies `[P4-CONTAINER-BASE]` (\"A refresh from **inside** the container view re-walks and is correct\") and `[W1-CONTAINER-STALE]` (\"refreshing while already inside the container view is clean\"): the refresh is correct only while the view shows no more than min(Array Limit, 4,096) elements. Related: the unfiled lead that the same branch leaves the crumb's cached `ContainerField` stale (the `RepopulateContainerView` paragraph above). **Safe fix:** in that branch, re-fetch a scalar array the way the drill does. For every container kind, re-derive the badge and status line from what came back, and write both `Label` and `ContainerField` back to the crumb. Red tests: refresh a drilled scalar array whose count exceeds Array Limit, and its rows and crumb must not drop to Array Limit; refresh a truncated pointer array, and it keeps its status line. | `ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` `RefreshAsync` container branch (:5429-5477), `RepopulateContainerView` (:1740) |
| `[LW-DISCONNECT-STALE-STATUS]` | LOW | ✅ **FIXED 2026-10-27** (red `fe50bf63` / green `9f244b93`): ClearOnDisconnect calls ClearStatus. Left: ClassStruct's ClearOnDisconnect never calls ClearError (filed below). ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (AOT UI 3776, DumperTest Shipping): Live Walker on `DumperTestActor_0` → **Find Refs** set the status to *"Found 4 reference(s) [scanned 24530/24530 in 51ms]"*; `taskkill /F` on the game: the header went **Disconnected** and Live Walker returned to its empty view with no status line; DumperTest relaunched and injected, **Connect** (`Connected — UE504 (24530 objects)`): still no status line. **Raised by L57's run, 2026-09-23.** Live Walker's `ClearOnDisconnect` (`LiveWalkerViewModel.cs:6172`) resets the node, crumbs, references and the function list. It never resets `StatusText`, and never `ErrorMessage` either (no `ClearError`; code-read). The panel shows each on its own line whenever it is non-empty (`LiveWalkerPanel.axaml:511-513`, `:519-522`). Measured on DumperTest Shipping 5.4 with the AOT UI `b1ea2660`: **Find Refs** set the status to `Found 4 reference(s)  [scanned 24516/24516 in 202ms]` (`:2879`). After the game was killed, the header read *Disconnected* and the Live Walker went back to its landing view with the References panel hidden, but the line stayed. It was still there after a new process connected, quoting a scan of a process that no longer exists above an empty landing view. The pre-fix UI (`e21bb297^`, whose `ClearOnDisconnect` does not touch `StatusText` either) showed `Found 4 reference(s)  [scanned 24516/24516 in 203ms]` after its kill. LOW: the line is only text, with no command or jump attached, and the next navigation clears it, because `StartFromWorldAsync` and the other entry points call `ClearStatus()` first. Live Walker is the one panel whose `ClearOnDisconnect` leaves its status behind; InstanceFinder, PropertySearch, ObjectTree, DetectStats, Console, InterestingFunctions/Properties and ValueSearch all reset theirs. **Safe fix:** call `ClearStatus()` (`:837`) in `ClearOnDisconnect`, next to `ClearReferences()`. It clears `StatusText`, `ErrorMessage` and `LocateFailureMessage` in one call. Red test: set `StatusText` and `ErrorMessage`, call `ClearOnDisconnect`, and both must be empty (extend `AuditL11HonestyTests`' LiveWalker ClearOnDisconnect test). Sibling lead: ClassStruct's `ClearOnDisconnect` never calls `ClearError` either (Leads). | `ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` `ClearOnDisconnect` (:6172) |
| `[FABRICATE-DEFAULT-UNSEEDED]` | MED | ✅ **FIXED 2026-10-27** (red `63db63bd` / green `3ce1180d`; `FabricateDefaultSeedTests` 3/3): the main window seeds `LiveWalker.FabricateArrayCount` right after building Live Walker. ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (AOT UI 3776 started fresh, `ui-options.json` `fabricateArrayCountExponent: 2` = Fabricate 4, slider never touched; DumperTest Shipping, `DumperTestActor_0` at `0x18587D58D40`): Copy CE Field on the selected `Arr_Delegates` (+0x960, 2 live elements) said **"Copied: 0 objects, 62 XML lines."**, and the clipboard held `[0]` / `[1]` and the fabricated `[2] (20)` / `[3] (30)` (8 Bytes each, under `+960` with offset 0) -- the 62-line shape the 2026-09-23 run got only after nudging the slider, not its 46. **Raised by L43's run, 2026-09-23 (measured three times: HEAD on DumperTest dev and on Shipping, and the `6ffd827a^` UI).** ⚙ Options reads `Fabricate: 4`, but Live Walker's **Copy CE Field** fabricates nothing until the slider is moved: the same selected `Arr_Delegates` row gave "Copied: 0 objects, 46 XML lines." (the two live rows) and, after a 4 → 8 → 4 nudge, 62 lines with `[2]`/`[3]` fabricated. Cause: `LiveWalkerViewModel.FabricateArrayCount` (`:318`) has no initializer, so it starts at 0, and its only assignment is `MainWindowViewModel.OnFabricateArrayCountExponentChanged` (`:430`). The exponent's field default is 2 (`:193`, and `UiOptionsSettings.cs:76`), and the [ObservableProperty] setter skips the handler on an equal value, so ApplyOptions loading the persisted default 2 (`:2463`) never reaches Live Walker. Only a change of exponent (the slider, or a persisted value other than 2) seeds it. It has been present since `93852adf` changed the default from 0 to 2. The sibling options avoid it because their Live Walker defaults match (`DropDownLimit`, `CeStringLength`, …). **Safe fix:** seed `LiveWalker.FabricateArrayCount = FabricateArrayCount` after ApplyOptions (or initialise it from the same constant). Red test: a VM built with the default options exposes `LiveWalker.FabricateArrayCount == 4`. | `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs` (:193, :430, :2463), `ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` (:318) |
| `[OBJTREE-SEARCH-TIP]` | ✅ **FIXED IN SOURCE 2026-09-26** (was LOW) | ✅ **FIXED IN SOURCE 2026-09-26**: the tooltip now ends *"As you type, it suggests common UE class names from a fixed list (not your past searches)."*; `ObjectTreeViewModelFilterTests.SearchFieldTooltip_describes_the_fixed_suggestion_list_the_box_binds` pins it to the panel's `ItemsSource` (red on the old text). ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]`: hovered on the AOT build 3776 (`dist\UE5DumpUI.exe` 62,882,304 bytes, sha256 `cab59503ae5050a1`…), not connected; the tooltip ends *"As you type, it suggests common UE class names from a fixed list (not your past searches)."* word for word, and nothing in it mentions recent searches. **Raised by the Wiki re-translation check, 2026-09-26 (code-read, confirmed against the source).** The Object Tree search box's tooltip (`str.Tip.ObjectTree.SearchField`) ends *"Recent searches are suggested as you type."* The box's suggestions are `ObjectTreeViewModel.SearchSuggestions`, a fixed list of common UE class names, and the VM says so on purpose (the top box is a server-side search; the remembered-keyword `KeywordSearchMemory` belongs to the bottom filter box). The tooltip describes a feature that does not exist. **Fix:** say what the list is. Test: pin the wording against the panel's `ItemsSource`. | `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI/Views/ObjectTreePanel.axaml` |
| `[LW-SEARCH-TIP]` | ✅ **FIXED IN SOURCE 2026-09-26** (was LOW) | ✅ **FIXED IN SOURCE 2026-09-26**: the tooltip names the AND, the matched columns (name, type, value, the class or struct type behind the field), the 2-character floor and the ▲ / ▼ stepper; `LiveWalkerSearchHighlightTests.SearchTooltip_names_what_the_matcher_matches` pins it (red on the old text). ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (AOT UI 3776, DumperTest Shipping, Live Walker from GWorld): the field-search box's tooltip reads word for word as `str.Tip.LiveWalker.Search` -- every keyword ANDed; a field's name, type or value, or the class or struct type behind it; *"at least 2 characters"*; rows *"highlighted, not hidden: ▲ / ▼ step between them"*; nothing says it searches by name only. **Raised by the Wiki re-translation check, 2026-09-26 (code-read, confirmed against the source).** Live Walker's field-search tooltip (`str.Tip.LiveWalker.Search`) says it searches *"fields by name"*, but `LiveWalkerViewModel.MarkSearchMatches` matches every keyword against the field's name, type, displayed value, pointed-to class and struct type (`MatchesAcrossFieldsNotJustName` already pins that), and ignores a query under 2 characters. **Fix:** name what is matched, the AND, the 2-character floor, and that rows are highlighted rather than hidden. Test: pin the wording. | `ui/UE5DumpUI/Resources/Strings/en.axaml` |
| `[LW-SEARCH-CLEAR-KEEP]` | ✅ **FIXED IN SOURCE 2026-09-27** (was LOW, a UX request) | **Requested by the maintainer, 2026-09-27:** find a field with the Live Walker search (e.g. at 0x1138), clear the keyword to look for something else, and the grid jumped back to its first row. Cause: `ApplySearch` re-sets the grid's items on every change (so the highlight styles re-evaluate), which returns the grid to the top and drops the selection; a non-empty keyword then scrolls to its first match, an empty one does not. **The maintainer's rule**, only when the keyword goes from 2+ characters to EMPTY in one edit (select all, Delete): the selection contains the match ▲/▼ last landed on → the view stays exactly where it is; it does not → the first selected row (grid order) comes to the top. Typing, pasting, shortening, a partial delete, 1 → 0 characters, and a navigation clearing the box keep the old behaviour. The maintainer's follow-up the same day: with NOTHING selected, the view stays where it is too. ✅ **FIXED IN SOURCE 2026-09-27**: the VM captures selection + top row before the swap and hands them to the view restore `[LW-BACK-SCROLL]` built. `LiveWalkerSearchClearKeepsViewTests` (3 red first, 6 controls). ✅ **Live check PASSED 2026-09-27 on build 3572** (AOT `UE5DumpUI.exe` `450684c436e8`, DLL `63a87743b364`), DumperTest 5.4 Shipping (pid 47456, UE504, 24,518 objects), DumperTestActor_0, keyword `Del_Unicast` (0x980), cleared by select-all + Backspace: **nothing selected** → the view stayed (`0x7E0 SpawnedHolders` first, `Del_Unicast` last); **▼ selected the match** → the view stayed and `Del_Unicast` stayed selected; **the match stepped to, then `0x8F0 WideGrade` clicked** → `WideGrade` came to the top, still selected. ⚠ Rig note: a click on ▼ right after typing only closes the keyword-memory dropdown; the second click steps. **Second follow-up (same day): the FIRST character typed also threw the grid to the top.** Root cause of every one of these jumps: `ApplySearch` re-set `Fields` "to re-evaluate row styles", a leftover from when the tint was painted per realized row (`26ac1af6`); the tint is a style bound to `IsSearchMatch` since `[LWREFRESH-2026-08-21]`. ✅ **The re-set is gone** — no keyword edit moves the grid or drops the selection now; the only restore left is "a selection without the stepped match → its first row to the top". Tests: `No_keyword_edit_replaces_the_grid_items` (6 cases, red first). ✅ **Live check PASSED 2026-09-27 on build 3573** (AOT `UE5DumpUI.exe` `0cf426959fc9`, DLL `e92600cbc43d`), DumperTest 5.4 Shipping (pid 74552): scrolled to `0x150 Role`, typed `I` → the grid did not move; `I32` → scrolled to its first match `0x468 Map_I64ToI32`, tinted; cleared → the grid did not move and the tint went; `0x310 Str_Even2_OneNull` selected, `Del_Unicast` typed (scrolled away), cleared → `Str_Even2_OneNull` at the top. | `ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` |
| `[USMAP-UDS-MISSING]` | ✅ **FIXED 2026-09-27, live check PASSED on EVERSPACE 2 (build 3587)** (was LOW; found while fixing `[SDK-UDS-MISSING]`) | ✅ **FIXED**: one predicate for both whole-pool exporters, `DumpAllService.IsExportedTypeRow` — the class-like metas plus `ScriptStruct` and `UserDefinedStruct`, never a `Default__` class-default object — so the USMAP collector can no longer drift from the SDK one. Red first: `AuditL11HonestyTests` W8 compared the collector with a COPY of the old rule, so it could not see the drift; it now drives both exporters over one stub. ✅ **Live check PASSED 2026-09-27, EVERSPACE 2** (UE 5.6.1, 79,834 objects at the main menu, name sanity 10/10), the AOT UI's `.usmap` export, builds 3585 and 3587 on the same game state: **UserDefinedStructs 0 → 22 of 22** (`find_instances`, not truncated), **`Default__` structs 5 → 0** (`Default__ScriptStruct`, `Default__Class` and the three Blueprint-generated-class CDOs), **struct references to undefined names 36 → 0**, structs 11,055 → 11,072 (= −5 + 22). With a save loaded (1,154,897 objects): **33 of 33**, 0 undefined. **Independent consumer** — CUE4Parse, `tools/verify/usmap_probe --mode uds` over ES2's own paks (47 cooked `STRCT_*` structs): 3585's mapping has none of them (`MAPPING-MISSING=47`, nothing schema-checked); 3587's has 21 (main menu) / 32 (save), **every one with descriptors identical to the cooked schema**, 17 / 27 of them SAME on a serialized value (the rest carry only zero values). `--mode holders` over 555 Blueprint CDOs: `NESTED-MISSING` 133 → 0, SAME 0 → 132. Its 6 / 9 `SCHEMA-DIFFER` are the same before and after and are all case-only FName spellings (`Duration` vs `duration`: the runtime name pool keeps the first casing registered, and UE and CUE4Parse match names case-insensitively). A UDS still `MAPPING-MISSING` was not loaded when the file was written — a runtime export carries only loaded types; the 22nd main-menu UDS, `STRC_MaterialInfo`, is simply outside the probe's `STRCT_` filter. Artifacts: `out/usmap-live/` (gitignored). *Original row:* The USMAP export uses the same meta filter the SDK export used (`UsmapExportService`: class-like metas + `"ScriptStruct"`), so it writes no Blueprint `UserDefinedStruct` schema — and, by the same shape as the SDK export's CDO defect, probably the metaclasses' `Default__*` objects as empty structs (not measured in a `.usmap` yet). Dumper-7's `MappingGenerator` writes every `IsA(Struct)` that is not a function (`PackageManager::InitDependencies`), so it includes UDS. **Not changed with the SDK fix on purpose:** the `.usmap` acceptance test is a CUE4Parse parse of cooked content (`tools/verify/usmap_probe`, `usmap_compare.py`), not a header. Host: EVERSPACE 2 (22 UDS at the main menu, 36 references). Check first whether CUE4Parse consults a UDS's schema from the mapping at all, and whether a UDS named like a native struct would shadow it in a name-keyed consumer. | `ui/UE5DumpUI/Services/UsmapExportService.cs`, `DumpAllService.cs` |
| `[FLY-EXPORT-EXPERIMENTAL]` | ✅ **FIXED IN SOURCE 2026-09-26** (was LOW, decided by the maintainer) | ✅ **FIXED IN SOURCE 2026-09-26**: `AddActionsToCeAsync` and `SaveCtAsync` add the Fly records only while `ExperimentalEnabled`; the Add status names the groups it actually sent. `FlyExportFollowsExperimentalTests` drives both commands with the switch off and on (red with it off). ✅ **Live check CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (AOT UI 3776, DumperTest Shipping; the saved file read as text, not in CE, which another session was using): Teleport → **Save .CT…** with Experimental features **on** wrote 34 `<CheatEntry>` (372,936 bytes) with a `--- Fly (4 rows) ---` group (Fly WASD / Numpad / Arrows, Fly Noclip) above Movement (4), Teleport (17) and Time (2); with the System tab's checkbox **off**, the same command wrote 29 entries (286,076 bytes) with **no** Fly group and no Fly description, the other groups unchanged. Status *"Saved: fly_exp_off.CT"*. The switch was turned back on afterwards. **DECIDED by the maintainer, 2026-09-26: the Fly records follow the Experimental features switch** (Add action records and Save .CT export them only while it is ON). **Raised by the Wiki re-translation check, batch 3, 2026-09-26 (confirmed against the source).** Add action records and Save .CT always export the 4 Fly records, even with Experimental features OFF, when the Fly card itself is hidden. Intended (a CE table is a separate surface) or not (it should follow the switch)? Not changed. | `ui/UE5DumpUI/ViewModels/TeleportViewModel.cs` |
#### ✅ ROUND 2 — 2026-09-10, builds 3527 → 3534. Rows 4, 6, 7, 8: ALL EIGHT DEFECT ROWS CLOSED

| # | row | commit | what shipped |
|---|---|---|---|
| **4** | `[POSEATTACH]` 🟠 | `242d48c4` | `parent_relative` on the pose reply — the flag `teleport-spec.md:218-220` asked for in 2026-07 |
| **7** | `[TPREL-ZEROPOSE]` 🟡 | `5058e971` | a failed landing re-read publishes `landing_unknown`, not `(0,0,0)` |
| **8** | `[SOLIDE-REFUSAL]` 🟡 | `5058e971` | a refused RE-ARM returns its reason; the erase stays gated on `newlyAdded` |
| **6** | `[B21-DOCROW]` 🟡 | `5058e971` | the tracker row reads "1 of 3 shipped", with the two live holes named |

⭐ **The two teleport fixes share one rule and it is this assessment's own thesis: never
publish the WISH as the FACT.** `[TPREL-ZEROPOSE]` deliberately does **not** fall back to
the requested destination when the landing cannot be read — that would report where we
*told* the pawn to go as where it *is*, which is the exact shape the whole sweep was about.
It publishes nothing and says so.

⚠ **`[POSEATTACH]` is a pipe-only flag, and the CE mailbox is knowingly left short.** Adding
a third value to the mailbox's `[176] source (0=raw, 1=invoke)` byte would change the
MEANING of a contract field — the change `Mimic.h` states the surface hash cannot see. The
CE path therefore still cannot tell a degraded parent-relative pose from a healthy one.
**That is a recorded gap, not an oversight**, and it is the natural next row if the CE
trainer path matters.

⚠ **One publish run reported `FAILED` while still producing binaries**; the re-run reported
SUCCESS, and the artifacts were then checked by **size and hash by hand** rather than
trusting either summary. `UE5DumpUI.exe` 54.8 MB `527F06B6` · `UE5Dumper.dll` 2.8 MB
`3F48A565` · `UE5CEDumper.CT` 44.8 KB `C1239550`, build 3534. Most likely a file lock from
the prior run — recorded because a build summary that disagrees with its own output is
worth not forgetting.

## ✅ The SIXTH D4b site, and two more found with it `[D4B-SITE6-2026-09-09]`

⛔ **`[D4B-DELEGATEPAD]`'s enumeration said "five readers" and it was wrong twice.** The first
correction was the CE exporter (`[D4B-TAIL]`, DLL-only enumeration). This is the second, and it is
inside the DLL, in a file the sweep had already edited three times.

### `Ubel::ReadDelegateArrayElements` carried ALL THREE of this sweep's shapes at once

`TArray<FScriptDelegate>` — and an array's inner `FDelegateProperty` stores the **standalone**
`TScriptDelegate<FNotThreadSafeDelegateMode>`, which **is** padded on a checked build, unlike a
multicast's invocation-list elements. `Grimoire.h`'s ⚠ about two types spelled the same way was
written for exactly this function, and the function was not checked against it.

| shape | what it did |
|---|---|
| ignored parameter | `int32_t /*elemSize*/` — while **both callers already passed `fv.arrayElemSize`**, the engine's own answer. Identical to the defect fixed in `ReadMulticastDelegateArrayElements` hours earlier. |
| baked stride | computed `8 + sizeof(FName)` locally = the **unpadded** size, so on a checked build element [0] read correctly and every index ≥ 1 drifted 8 bytes further — audit A1's fingerprint. |
| dropped pair | both `Macht::ReadSafe` returns discarded → a faulted read published the affirmative `"(unbound)"`. The third copy of that pair in this file. |

### ⭐ And the fixture immediately surfaced a SEVENTH

With `Arr_Delegates` in place, element [0] rendered **`(stale)::None`** — an affirmative claim that
a target *was* bound and has since been collected, over a slot nothing had ever touched. An
untouched `FScriptDelegate` has `Object = {0,0}` and `FunctionName = NAME_None`, and `ReadFName`
resolves index 0 to the **string `"None"`** — which is not empty, so the `!funcName.empty()` arm
claimed staleness. The multicast element loop has an explicit unbound branch; this one did not.

### The fixture — `Arr_Delegates`

`ReadDelegateArrayElements` had **no host at all** on any fixture. `TArray<FDumperTestUnicastSignature>`
with **[1] bound and [0] empty**, for the reason `Arr_MulticastDelegates` documents: with both
elements identical, a right stride and a wrong one print the same string and the row cannot fail.

### Verified live, on both configurations and with a real red-before

| | `Arr_Delegates` elem_size | `[0]` | `[1]` |
|---|---|---|---|
| **Development** | **24** | `(unbound)` | `DumperTestActor_0::D4b_OnPingProbe` |
| **Shipping** (control) | **16** | `(unbound)` | identical |

⭐ **The red-before is a live mutation, not an argument.** Reverting the stride to the pre-fix
unpadded value, rebuilding and re-running on Development gives

```
Arr_Deleg  : elem_size=24  ['(unbound)', '(unbound)']
  - Arr_Delegates[1] does not name D4b_OnPingProbe -- got '(unbound)'.
```

The binding vanishes because [1] is read from inside [0]. `Ubel.cpp` restored byte-identically and
rebuilt afterwards, and the fixed binary re-run green.

⚠ The `(stale)::None` repair got the same treatment for free: it was *observed* wrong on the live
fixture first (`['(stale)::None', ...]`) and green after — red-before on the real thing rather than
on a double.

### ✅ Everything re-run against HEAD's binary, which also un-stales three rows

The reconciliation flagged that D3's live evidence was taken on a build the sweep then rewrote
twice, and never re-run. All four rigs now ran against the same current DLL:

| rig | result |
|---|---|
| `d4b_delegate_pad` | ✅ PASS on Development (pad 8) **and** Shipping (pad 0), 5 shapes each |
| `d5_lazyguid_unread` | ✅ PASS |
| `d3_delegate_array_unread` | ✅ PASS — its **first run since the reader was rewritten**, and its baseline now reads `['(0 bindings)', '(1 binding) [...]']` at `elem_size=24`, so it is a D3b check as well as a D3 one |
| `d1_collision_refusal` | ✅ PASS, both arms still reporting DIFFERENT causes (261 ms → dispatcher refused; 1151 ms → thread unresponsive) |

### ⬜ Still open

* ~~**`Ubel::TMap` geometry** — not verifiable~~ ✅ **CLOSED** by
  `[TMAPGEOM-2026-09-09]` above: the partial-read condition WAS manufacturable, with a page
  edge in our own process rather than inside a game.
* The `(stale)` branch **elsewhere**. Only `ReadDelegateArrayElements` was repaired; the
  single-field `DelegateProperty` handler and the multicast element loop have their own
  `funcName.empty()` tests and were not re-examined for the `"None"` case.

