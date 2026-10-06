# External PRs 539 / 540 — first review and the maintainer's decisions `[EXTPR-539-540-2026-10-02]`

**Status: BUILT — both PRs, 2026-10-06.** PR 540: L1 (fetch limit), L2 (Save .jsonl), L3 (Min calls), L4 and L5, shipped in build 3619 and checked live on Avowed (see "Live check, 2026-10-06" under PR 540). PR 539: D1 (struct lines, build 3620), D2 (enum lines, 3621), D3 (function parameters, 3622, with a DLL flag that tells a parameter from a Blueprint local), the Dump Explorer reading them (E1), D5 (the diff), D4 (the object index) and D8, builds 3625–3626, each reviewed and checked live (see "PR 539 finished" under PR 539). The reply was posted on both PRs on 2026-10-06, and the work reached `main` in #542. The PRs' own three commits were then merged into the history with `git merge -s ours` (no file changed, #543), so the author's own commits are on `main` and GitHub marked both PRs as merged. The contributor list did not need it: the maintainer saw fireundubh listed before #543 merged, so the `Co-authored-by` trailers alone count there. The open question of a diff inside the UI is decided (a C# port, HTML only, a Dump Explorer button; `[DUMPDIFF-UI]`). Three small changes landed earlier and shipped in build 3616, D7 among them — see "Landed ahead of the plan". **Re-checked 2026-10-06**, with four questions for the maintainer (R1–R4), all decided.
First-pass review on 2026-10-02 plus the maintainer's decisions on the same day. ⚠ **The review is a first
reading, not a verdict**: the maintainer will re-read both PRs, and a row below can still change. Close a row by
editing it here AND its line in [todo.md](todo.md) in the same commit.

### Landed ahead of the plan — shipped in build 3616

All three are in **build 3616**: the maintainer published it AOT with `build.cmd publish` on 2026-10-02 and
reported it OK (that run includes the C# tests); [dev-log.md](dev-log.md) has the entry. The exe size, SHA and
Windows test count were not recorded.

| Commit | What | Still owed |
|---|---|---|
| `42217904` (2026-10-02, `dev`) | `en.axaml` `str.Tip.Export.DumpAll`: the sentence "~30-60s per game; output is 50-500 MB depending on game size." became "Time and file size vary with the game and the export settings." Nothing else in the tooltip changed. | Shipped in 3616. |
| `cc254fd1` (2026-10-02, `dev`) | `DumpAllService` header comment: "~30-60 seconds for a 3-5k-class game" became "the run time grows with the game's class count". Comment only. | Shipped in 3616. |
| `14ecb189` (2026-10-02, `dev`) | **D7 done.** The class walk reports "Walking classes" every `DumpAllService.ProgressReportInterval` (500 ms) through `ProgressThrottle`, not every 50 classes; the first class is reported at once; pass 1 still reports per 5000-object page; the final "Done" report is unchanged. `GenerateAsync` gained an optional `TimeProvider` for tests. Carries the `Co-authored-by: fireundubh` trailer. | Shipped in 3616; also tested on Linux before the publish (below). `SdkExportService` and `UsmapExportService` still report every 50 walked classes: not part of D7, and they write no non-class lines between counts, so they do not misfire — left as they are. |

**How these were tested (2026-10-02).** .NET SDK 10.0.112 from Ubuntu's archive (`apt-get install dotnet-sdk-10.0`;
the `dot.net` installer host is blocked by the container's proxy). The projects are `net10.0-windows` / `win-x64`,
so they were built and run with `-p:EnableWindowsTargeting=true -p:RuntimeIdentifier=linux-x64` and
`dotnet test --project ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj`. Baseline on the unchanged tree: 6021 total,
5892 passed, **112 failed**, 17 skipped. The 112 are Windows-only (proxy deploy and orphan scan, DLL path store,
CE inject paths, volume roots, and similar); none is in Dump All. After `14ecb189`: 6025 total, 5896 passed, the
**same 112** failed (compared by name), 17 skipped — the 4 new D7 tests pass. A Linux run is not a Windows run:
AOT trimming and the Windows-only tests were covered only by the maintainer's build 3616 publish.

⚠ **`42217904`'s message says the old figures were "never measured against the games this runs on". That is
unverified** — nobody checked whether they were measured. The reason for the change is the maintainer's decision
that the tooltip states no numbers, not that the numbers were wrong. Read the commit message with this note.

### Re-check 2026-10-06

The maintainer asked for this review to be re-checked before work starts. Four reviewers read every claim in
this file against the PR heads (`439387fa`, `facbdc96`, unchanged since 2026-10-02), `dev` @ `bb651f08`, and the
two reference dumpers on GitHub. Of 100 claims, 77 held, 21 held only in part or needed a qualifier, 2 could not
be checked (the Linux test run, and the typical number of distinct functions), and none was wrong outright. The text further down is left as written; read it with this section.

**Maintainer decisions the re-check raised** (details in the lists below; decided 2026-10-06):

| # | Question | Decision (2026-10-06) |
|---|---|---|
| R1 | Min calls in diff mode: exempt NEW rows, or only warn in the tooltip? | **Neither.** The filter is `Count < MinCalls` (strictly less), so the default 1 filters nothing. NEW rows are filtered like any other row; the filter is not needed in normal use. |
| R2 | D4's separate file: every object (as Dumper-7 and RE-UE4SS), or keep the `IsLiveInstanceRow` filter and say so? | **Every object**, packages and type objects included, in the separate `<name>.objects.jsonl` of D4.3 (unchanged: the Dump All file never carries it). No `IsLiveInstanceRow` filter for this file. |
| R3 | D4's GObjects index: accept the DLL change it needs (a new build of the DLL, not only the UI)? | **Change the DLL**, design left to us: see "R3 design" below. |
| R4 | `diff_dumps.py`'s engine test (`/Script/` anywhere in the path) also drops the game's own native classes. Change it before D1/D5 and the C# port build on it? | **Change `diff_dumps.py` first.** ✅ **Done 2026-10-06:** red `80e7bfbf`, fix `6ade4435`, gates `15d566e8` (see "R4 done" below). |

**R4 done (2026-10-06).** `diff_dumps.py` now skips only the engine's own modules, with a copy of the DLL's
`Aura::IsEnginePackage` list (`ENGINE_PATH_PREFIXES`) and its rule (collapse the leading slashes, then a prefix
must be followed by the end, `/` or `.`). The copy lives in `scripts/analysis/engine_paths.py`, which
`analyze_dumps.py` uses too (see below). A default diff therefore reports the game's own C++ classes
(`/Script/<GameModule>/…`), and with D1 its native structs and enums; `--include-engine` adds the engine's. A
module that is neither in the list nor the game's (an engine plugin such as `/Script/CommonUI`) is treated as the
game's, as the DLL's GameOnly filter already does. Two new gates: `check_engine_prefixes` keeps the three copies
of the list equal (Aura.h, `DumpAllService.cs`, `engine_paths.py`), and `check_analysis_selftests` runs the
`--self-test` of every script under `scripts/analysis/`, which nothing ran before. Negative controls: putting
back the old predicate fails only `check_analysis_selftests`; dropping one module from the list fails only
`check_engine_prefixes`.

**`analyze_dumps.py`, same fix (maintainer, 2026-10-06; items 1, 2 and 4 of the proposal).** It had the same
"`/Script/` anywhere" test, so its game-only statistics for the Interesting Properties / Funcs tables never saw
the game's native classes, and its header claimed a match with the DLL's list that it never had. Commits: red
`9fc98105` (a new `--self-test`), the shared `engine_paths.py` `315f9732`, `diff_dumps.py` switched to it
`9b83ffee` (co-author trailer: it changes R4's code), the fix `74dc127e` (no trailer: not from the PRs). The
unused `is_game_class()` is gone. Reports made before the fix count fewer game classes and are not comparable.
Not taken: item 3, a report section listing the `/Script/` modules outside the list (engine plugins such as
CommonUI now count as the game's, which can push their property names into the cross-game candidates).

**R3 design (ours, 2026-10-06).** `get_object_list` takes one more optional request field, `include_index`
(default false), the way `include_path` already works: with it set, each item also carries `index`, the GObjects
slot the handler read it from. Without it the reply is byte-for-byte what it is today, so Object Tree's paging and
every older UI are untouched. A newer UI talking to an older DLL gets no `index` field and writes the object line
without one; it does not guess the slot from the page position, because the handler skips null and unnamed slots.
`UObjectNode` gains a nullable index. `snapshot_chunk` already sends `index`; use the same key name.

**The early commits, verified on Windows:**

- `dotnet test` at `bb651f08`: 6025/6025 passed, headless 15/15 — the Windows count build 3616 did not record.
- Build 3616 as published on this machine: `dist\UE5DumpUI.exe` 59,165,184 B, SHA-256 `83e8b518119c`, FileVersion
  1.0.0.3616, 2026-10-02 16:36; `dist\UE5Dumper.dll` 3,040,768 B.
- D7's four tests all stayed inside one 200-class chunk. `8358bdd9` adds two that walk three chunks. Mutation
  check: a throttle made per chunk fails four tests, one of them new; a chunk-local `Done` fails only the new
  `Generate_WalkProgress_DoneCountsOnAcrossChunks`, so before `8358bdd9` it passed every test. The misnamed
  `…SlowerThanTheIntervalStillLeavesGaps` (its classes were faster than the interval) is now
  `…FasterThanTheInterval_ReportsEverySecondClass`.
- `6897e913` fixes `DumpAllService` comments that no longer matched the code: a plan-phase "will", "obj.FullPath is
  always empty" (it is not with `include_path`), and the header's `walk_class` (the walk uses `walk_class_batch`).
- `76556244`: `scripts/analysis/README.md` no longer says the file is 50–500 MB. `docs/review7-live-recipes.json`
  still tells a rig operator a run takes "about 30-60 s"; left as an internal note.
- The 3616 dev-log entry says the status line "updates every half second". It updates **at most** twice a second,
  and only after a class is written, so one slow class walk still leaves it quiet.
- The tooltip's "vary with … the export settings" anticipates D4's checkbox: Dump All has no user setting today.

**PR 540 — corrections:**

- The PARTIAL baseline line gives no advice. "Only a shorter recording window brings it back" is in the diff-mode
  status line, which also repeats the capped warning until Copy or Save overwrites it. So the baseline line is not
  the only warning, but it is the only one that **stays** on screen. It was clipped before the PR as well (no
  wrapping, about 175 characters): a second row or a `WrapPanel` also needs a bounded width and `TextWrapping` on
  that `TextBlock`. The timeline plan adds a Trace checkbox and a buffer slider to the same toolbar.
- "Hundreds to a few thousand distinct functions" is not measured anywhere (the only recorded figures are 67 and 6).
  The 32768 measurement in the implementation notes is what settles it.
- L1a: names are also resolved for rows the handler then drops as stale, and every fetch copies and sorts the whole
  table whatever the limit. Per row it is several FName reads plus an outer-chain walk (`GetFullName`, which this
  handler never uses) and a super-chain walk (`is_widget`). If 32768 stalls the lane, try those two first (skip the
  unused full name, cache `is_widget` per class) before moving the command to the bulk lane.
- L2: dropping the `recording` field loses nothing. The PR's field was the UI flag at save time, never whether the
  rows came from a mid-recording peek; if that matters, save the fetch's own `recording` value.
- L3: the PR's Min calls is a view filter and is reversible (its own test lowers it again). The "next capture only"
  decision stands on its own grounds.
- Problem 6: the number boxes use `Clamp(KeepCurrentIfEmpty)` where `NumericInput`'s own doc says to use `Coerce`.
  Moot once L1 and L3 are sliders.

**PR 540 — missed by the first review:**

- ⚠ **Fixing problem 4 turns a test red.** `LiveFuncsCapAdviceTests` reads the view-model source and requires
  "shorter recording window". The `[LIVEFUNCS-CAP-ADVICE]` row and the Wiki quote the same warning: change all three
  together.
- ⚠ **Min calls can hide the panel's own target in diff mode** (R1). The action's function is a NEW row with a low
  count, and the PR's filter has no NEW exemption, so any value of 4 or more can hide it.
- L5: the repo's power-of-two sliders persist the **exponent** (`ArrayLimitExponent`, `DropDownLimitExponent`, …).
  Persisting the exponent for both new sliders removes the "snap to the nearest power of two" step.
- Comments in `LiveFuncsViewModel` and the PR's own test hard-code 300; they go stale with a configurable cap.

**PR 539 — corrections:**

- A struct line is a subset of the class shape: no `is_bpgc`, `instance_count` or `funcs`, and `walk_functions` is
  never called for a struct.
- The PR's `diff_dumps.py` compares structs and enums, but its report prints only two **counts**: it never names
  which struct, field or enum changed, and `--minimal` ignores both. D1's "report a struct field that moved" needs a
  detail section, sorted like the class buckets. Plan it with D5.
- "More than ten thousand extra struct walks on UE5" is not measured. Each struct costs one slot of a batched walk
  and no `walk_functions`; `list_enums` is on the bulk lane.
- Open question: `diff_dumps.py` is 889 lines on `dev` but 1045 on the PR head, and a port would follow the
  post-D1–D5 script. Its self-test builds synthetic dumps in code (`_make_dump`), so a C# port rewrites them rather
  than sharing fixture files.

**PR 539 — missed by the first review:**

- ⚠ **D4's GObjects index needs a DLL change** (R3). `get_object_list` items carry only `addr`, `name`, `class`,
  `outer` and `full_path`, and the handler skips null and unnamed slots, so the client cannot work the index out from
  a row's page position. It needs an `index` field (as `snapshot_chunk` already sends), ideally only with
  `include_path`, plus a `UObjectNode` field. The index matches lines within **one** session only: slots depend on
  load order and are reused after garbage collection.
- **D4's filter is not the reference dumpers' list** (R2). `IsLiveInstanceRow` drops UClass, UFunction,
  UScriptStruct, UEnum, UPackage and the UE4 property classes; Dumper-7 and RE-UE4SS write every object, packages
  included.
- **`diff_dumps.py` treats any path containing `/Script/` as engine** (R4), so by default it also drops the game's
  own native classes (`/Script/<GameModule>/…`), and with D1 its native structs and enums. `DumpAllService` uses a
  36-prefix list instead.
- The PR's enum diff ignores `enum_names_failed` and a failed `list_enums`: against such a dump every enum shows as
  changed, or as added.
- **Re-implementation map.** A 3-way merge of the PR onto `dev` conflicts in one hunk, the progress block in
  `FlushClassChunkAsync`. Carry by hand the 4-int return tuple and both call sites, and report `Done` as classes
  **plus** structs (the callers on `dev` pass only `classesEmitted`). D2 adds a `list_enums` call to every run, so
  test fakes need a `ListEnumsDetailedAsync` override; the PR added one to `StubDumpService` and three
  `WalkClassBatchEquivalenceTests` fakes.
- Dump Explorer gaps the PR leaves: the category picker reaches struct, enum and enumerator rows only under "All";
  the class count leaves structs out while the property and function counts include their members; Find Instances
  on a struct or enum row does nothing and says nothing.
- Stale text in the PR's `DumpAllService`: the schema doc still lists meta / class / error / summary only, the enum
  phase reports no progress, and the final report counts only classes.
- A precedent for D4.2: Snapshot's "Estimate size" pre-flight (`SnapshotSizeEstimate`) and `SnapshotDiskGuard`.

**Reference dumpers — corrections** (re-read on GitHub at the cited commits):

- Dumper-7's `-WithProperties` file lists properties under every UStruct, functions included (a function's
  properties are its parameters and, on a Blueprint function, its locals after them; D3 found this). A property
  line puts the offset where an object line puts the index.
- RE-UE4SS `[ObjectDumper]` has a second option, `UseModuleOffsets` (default 0). Its crash warning is conditional
  (loading past the main menu after a dump), force-loaded assets are freed afterwards, and the option is ignored
  below UE 4.17.
- RE-UE4SS's comment that `wchar_t` doubles the file size is stale: the writer converts to UTF-8 before writing.
  The doubling is in memory (the 200,000,000-character reserve is about 400 MB).
- "Users of those tools expect it" is an opinion, not something the source shows.

| PR | Branch (fork `fireundubh/UE5CEDumper`) | Head reviewed | Base |
|---|---|---|---|
| [#540](https://github.com/bbfox0703/UE5CEDumper/pull/540) Live Funcs: fetch limit, min calls, JSONL save | `pr/live-funcs-table` | `facbdc96` | `main` @ `c74daa4b` |
| [#539](https://github.com/bbfox0703/UE5CEDumper/pull/539) Dump All: structs, enums, function params, object index | `pr/dump-all-schema` | `439387fa` | `main` @ `c74daa4b` |

**How both are taken in:** we re-implement the parts we keep on `dev` ourselves (not a merge of either PR).
**Every commit that carries code from PR 539 or 540, or logic taken from them, ends with**

```
Co-authored-by: fireundubh <1261664+fireundubh@users.noreply.github.com>
```

(the address is GitHub's noreply form of the contributor's account id, read from the PR data). **Which commits
(maintainer, 2026-10-06):** the code and the logic that come from the two PRs get it — for example the
`diff_dumps.py` fix (R4, `6ade4435`). Our own work around them does not: measurements, UI adjustments, tests,
gates and these docs. A red test commit therefore has no trailer and its green fix does.
**How the code is written (maintainer, 2026-10-06):** the PRs' code need not be reused. Their commits do not say
whether a model or a person wrote them, so each feature is built for the same FUNCTION in this repo's way (its
helpers, string rules, AOT patterns, tests), and the PR's code is followed only where it is close to what we would
write anyway. The trailer goes on the commit that carries the code for the PR's feature, whether or not any of
the PR's lines survive in it. The reply on the
two PRs — what was taken, what was not and why, links to our commits — is written **after** the work is done, not
before.

**What the review could and could not check.** The review container has no .NET SDK, so **neither PR's C# was
compiled and neither test suite ran.** The Python gates ran on both heads (`check_all.py`); the only failures
besides the two that always fail on Linux (`crc_oracle_selftest` needs `ctypes.WinDLL`, `check_no_local_paths`
matches the user name `root`) are the ones named below. GitHub had run **no** CI on either PR (fork PRs, zero
check runs).

-----

## PR 540 — Live Funcs

### What the PR does

- A number box sets how many functions `pe_profile_get` returns (was a fixed 300; PR max 50,000).
- A "Min calls" number box hides fetched rows with fewer calls (PR max 1,000,000).
- "Save .jsonl" writes the visible rows to a JSON Lines file, ordered by first call, with a header line.

### Why it is worth having (first review)

The DLL sorts the whole recording by call count, **descending**, and emits only the first `limit` rows. The rows
the cap drops are exactly the low-count functions this panel exists to find. The panel already knows: a capped
baseline shows the PARTIAL warning (`_baselineTruncated`), but its only advice is "record a shorter window". A
user-set cap is the direct fix. Min calls is de-clutter only. Save `.jsonl` is a nice-to-have (keeps the
first-call order of one action for later reading, and lets two game versions be compared as text).

### Problems found in the PR as written

1. ❌ **Fails a CI gate.** `check_vm_status_literals`: `LiveFuncsViewModel` goes from 14 to 18 hard-coded
   `StatusText` literals ([VM-INLINE-STRINGS]). New status text goes to `en.axaml` and is read with
   `Res.Get` / `Res.Format`.
2. ⚠ **50,000 is not a usable ceiling, it is "everything".** `Linie` keeps an unbounded table (it only reserves
   4096 slots), and one recording typically holds hundreds to a few thousand distinct functions. For every
   emitted row the DLL resolves the function, reads its class name and walks the class's super chain
   (`ClassDerivesFromAny`, for `is_widget`). `pe_profile_get` is on the **interactive** pipe lane
   (`LaneRoutingPipeClient.BulkCommands` does not list it), so a large fetch queues Live Walker and every other
   interactive command behind it.
3. ⚠ **Layout.** The two 160 px number boxes are added to a horizontal `StackPanel`, which does not wrap. On a
   narrow window they push **Clear Baseline** and the **PARTIAL baseline warning** off the right edge, and that
   warning is the only thing that tells the user the cap has made NEW rows unreliable.
4. ⚠ **Stale status text.** The diff-mode status line still says "Only a shorter recording window brings it back".
   With a user-set cap, raising the cap also brings it back.
5. Min calls and the fetch cap cut the **same end** of the ranking (both drop the lowest counts), so Min calls
   never makes room for more rows. It only hides rows that were already fetched.
6. OK as written: the JSONL writer uses a source-generated `JsonSerializerContext` (AOT-safe), and the
   number boxes follow the existing `decimal?` + `NumericInput.KeepCurrentIfEmpty` pattern.

### Maintainer decisions (2026-10-02)

Build in this order: **fetch limit → Save .jsonl → Min calls.**

| # | Item | Decision |
|---|---|---|
| L1 | **Fetch limit** | A **slider over powers of two**: value = 2^x, step one power (2^n). **Default 2^9 = 512, max 2^15 = 32768** (raised from 2^13 = 8192 on 2026-10-04, see below). The minimum was not stated; proposal 2^6 = 64, which gives ten slider positions. Replaces the fixed 300. |
| L2 | **Save .jsonl** | Keep. Disabled while recording, so the header's `recording` field is always false and can be dropped. |
| L3 | **Min calls** | A **slider: default 1, max 32, step 2^n** (1, 2, 4, 8, 16, 32). **It affects only the NEXT capture, never data already on screen**: the value is read at Start and used for that capture; moving the slider afterwards changes nothing until the next Start. The tooltip must say so. Chosen over "filter the current table at once" because it needs no answer to "is a raised minimum reversible?". |
| L4 | **Recording lock** | All three controls (both sliders and the Save button) are **disabled (greyed out) while a recording runs**, and usable before and after it. Refresh during a recording then always uses the cap fixed at Start. |
| L1a | **Why the max is 32768 (2026-10-04)** | On PR 540 the contributor replied that they use the tool with an AI assistant to write UE4SS mods, and wanted more rows to give it a fuller picture of what fired; they agreed 50,000 is too much. The maintainer raised the max to 2^15 = 32768. The default stays 512: the fixed 300 was meant for tracing what one in-game action calls, and a small table still serves that best. The DLL needs no change: `pe_profile_get` takes any `limit` and only resolves names for the rows it sends. A per-call timeline and stack snapshots came up in the same discussion; they are a separate feature of ours with no co-author trailer, in [live-funcs-timeline-plan.md](live-funcs-timeline-plan.md). |
| L5 | **Persisted** | Fetch limit and Min calls survive a UI restart: a new `LiveFuncs` sub-object in `UiOptionsSettings` (`ui-options.json`), defaults equal to the VM initializers (that file's own rule), every field written (`check_json_default_ignore`). A loaded value snaps to the nearest power of two and clamps to the range. |

### Live check, 2026-10-06 — PASS (build 3619, Avowed UE 5.3 through its `dxgi.dll` proxy)

Driven through the AOT UI with computer-use; the saved files (under `out/livefuncs-live/`, not committed) were read
back with a script.

| # | Check | Result |
|---|---|---|
| A | Fetch limit 64, record while walking and opening the inventory / map / journal, Stop | Status: "906 distinct functions, 1,086,524 total calls (showing top 64 of 906 by count; a higher Fetch limit shows more)". Save .jsonl: 64 rows, `fetch_limit` 64, `distinct` 906, rows in first-call order, no BOM. |
| B | Raise to 32768, Refresh (same capture) | "showing top 722 of 906 by count", with **no** raise advice: 722 is below the limit, the other 184 were dropped by the DLL as unresolvable. Saved: 722 rows, `fetch_limit` 32768. |
| C | Min calls 8 set before Start; record; Stop | 480 distinct, 475 fetched, 220 rows shown and saved, lowest `calls` exactly 8 (`Count < MinCalls`). Moving the slider to 8 before this Start left the previous table at 722 rows; moving it back to 1 afterwards left this one at 220 rows, byte-identical when saved again with `min_calls` 8. |
| D | Start, Refresh during the recording (71 functions, "still recording"), switch tab (auto-stop), come back, Save | Save enabled; 71 rows, `recording_at_fetch` true. |
| E | During a recording | Save .jsonl and both sliders greyed out; Start greyed, Stop live. |
| — | The save dialog | Default name `live-funcs-<date>-<time>.jsonl`, type "JSON Lines (*.jsonl)"; a name typed without an extension was saved as `.jsonl` (the picker fix from `[PICKER-EXT-DOT-2026-10-06]`). |

Not covered live: diff mode with Min calls hiding a NEW row (unit-tested), and a partial baseline in a saved
file (unit-tested). Both sliders were set back to 512 / 1 afterwards.

### L3 built (2026-10-06)

| Commit | What | Co-author |
|---|---|---|
| `92007713` red, `f45dca96` | Min calls slider 1..32 (exponent 0..5), default 1, beside Fetch limit; read at Start; `Count < MinCalls` with no NEW exemption (R1); a view filter, the baseline reads every fetched row; disabled while recording; `min_calls` in Save .jsonl's summary | `f45dca96` |
| `63ce5638` red, `71be0e25` | Persisted as `LiveFuncs.MinCallsExponent`; slider, clamp, default and tooltip pinned to each other | no |
| `b5538086` red, `3d278c75` | Review fixes: the rows are filtered by the minimum they were FETCHED under (a Start no longer re-filters the previous capture's rows, and Save records the shown rows' minimum); the higher-Fetch-limit advice needs the page's lowest count to reach Min calls; the diff line drops "almost certainly among the NEW rows" when Min calls hid a NEW row; the tooltip carries no NEW-row warning (R1: neither) | `3d278c75` |

Mutation-checked: filtering by the Start value again, dropping the Min-calls condition from the advice, and
always making the diff claim each fail exactly one of the new tests. C# 6077/6077. **Published as build 3619**
(AOT `UE5DumpUI.exe` 59,228,160 B, sha256 `23dc3b7d3db5`); in the AOT UI without a game the slider sits beside Fetch
limit, reaches 32, is written to `ui-options.json` (`minCallsExponent` 5) and was set back to 1. ✅ **Live check
PASS** (see "Live check, 2026-10-06").

### L2 built (2026-10-06)

Built for the PR's function in this repo's way, not from its code (see "How the code is written").

| Commit | What | Co-author |
|---|---|---|
| `c6c8af2e` red, `a20af931` | Save .jsonl beside Clear, disabled while recording; the platform save dialog; `Helpers/LiveFuncsJsonl` writes a summary line, then the rows on screen in first-call order (unknown last), by hand like Dump All's lines, so no serializer metadata under AOT | `a20af931` |
| `161c4313` red, `feee40cf` | Review fixes: the summary records the check boxes that hide rows, a partial baseline (`baseline_partial`, `baseline_distinct`) and `recording_at_fetch` (a peek left on screen when a recording ends without a fetch); exact `period_ms` / `cv` and the panel's `periodic` / `badge` per row; the dialog's extension `.jsonl` | `feee40cf` |
| `a3c71df2` red, `bc3d052e` | Older bug the review found: a second Set Baseline while diff was on kept every row's Delta / IsNew against the old baseline | no |

The summary's `recording` field of the PR is not written: Save is disabled while recording, so the UI flag at
save time is always false; `recording_at_fetch` is the fetch's own state instead. C# 6054/6054.
**Published as build 3618** (AOT `UE5DumpUI.exe` 59,215,360 B, sha256 `a0b29b4c3c0c`); in the AOT UI without a
game the button sits beside Clear and an empty table answers "Nothing to save: the table is empty." ✅ **Live
check PASS** on build 3619 (see "Live check, 2026-10-06").

### L1 and L5 built (2026-10-06)

| Commit | What | Co-author |
|---|---|---|
| `8d7d7e3c` red, `51cb4f69` | Fetch-limit slider 2^6..2^15, default 2^9, on its own row under Start / Stop; disabled while recording, and a recording fetches with the value from Start (L1, L4) | `51cb4f69` |
| `34ac0206` red, `baa6dcef` | Persisted as `LiveFuncs.FetchLimitExponent` in `ui-options.json` (L5) | no |
| `a698b129` red, `cbf4ec36`, then `9ca4e7cf` red, `a1eab927` | Problem 4: the "rows were cut" messages offer a higher Fetch limit, but only when the limit cut the page and is below the maximum (`RaiseFetchLimitHelps`); the remedies are `en.axaml` strings | no |
| `46a4c364`, then `bb2db0b3` | Problem 3: the baseline status on a full-width wrapping line of its own (the first attempt squeezed it beside the controls) | no |
| `4baf6fe7` | The slider, the VM clamp, the persisted default and the tooltip pinned to each other | no |

A two-reviewer pass before the publish found the advice and layout defects fixed in `a1eab927` and `bb2db0b3`.
C# 6042/6042. **Published as build 3617** (AOT `UE5DumpUI.exe` 59,189,760 B, sha256 `cf10dbc1656e`).
**Checked on 3617 without a game (2026-10-06):** the slider row renders under Start / Stop with 512; dragged to
the end it reads 32768, `ui-options.json` holds `liveFuncs.fetchLimitExponent = 15`, and after closing and
starting the UI again the slider still reads 32768; set back to 512 (exponent 9 on disk). The baseline status
sits on its own line. ✅ **Live check PASS** on build 3619 (see "Live check, 2026-10-06"). The Wiki's Live Funcs pages (en, zh-TW, ja-JP) still describe a fixed 300 and
"only a shorter window"; the Wiki is a separate repository and is not changed from here.

### Implementation notes (from the review — confirm while building)

- **Min calls must stay a VIEW filter.** `SetBaseline` builds the baseline from the unfiltered `_allEntries`. If
  Min calls filtered `_allEntries` itself, an idle function with one call would be missing from the baseline and
  come back as a false NEW. Apply it in `ApplyFilter` with the value captured at Start.
- Fix problem 1 (status strings to `en.axaml`), problem 3 (the row must keep Clear Baseline and the baseline
  warning visible — a second row or a `WrapPanel`), and problem 4 (the stale "shorter window" sentence).
- **Measure** `pe_profile_get` at 32768 on a busy game (the DLL logs `emitted (limit N)` in `PIPE:profile`). This
  matters more at 32768 than it did at 8192: the reply is one JSON line on the interactive lane, and every row
  costs a name lookup in the DLL. Record the reply size and the time. If the interactive lane stalls noticeably,
  decide then whether the command moves to the bulk lane; do not move it on a guess. A game that fired fewer
  distinct functions than the limit sends only what it has, so measure on one that fires many.
  ✅ **Measured 2026-10-06, build 3616, Avowed (UE 5.4) through its `dxgi.dll` proxy**, with
  `tools/verify/livefuncs_fetch_measure.py` (raw results in `out/livefuncs-fetch-measure/`, not committed):

  | Recording | Distinct functions | Calls | Limit 300 | Limit 512 | Limit 8192 / 32768 |
  |---|---|---|---|---|---|
  | 60 s standing still in the world | 67 | 534,692 | 67 rows, 19.6 KB, 1 ms | same | same |
  | 75 s walking, jumping, one swing, inventory / map / journal opened and closed | 648 | 593,676 | 300 rows, 83 KB, 4–6 ms | 512 rows, 136 KB, 6–12 ms | 543 rows, 144 KB, 10–15 ms |

  Times are on the client, from sending the request to the last byte of the reply, three runs each. A cheap
  command (`get_pointers`) took 0.2–0.4 ms before and right after the largest fetch. The fetch while still
  recording took 16 ms. The DLL's own log lines (`PIPE:profile`) match every row.
  **Verdict: the fetch stays on the interactive lane.** At this game's size the largest fetch holds the lane for
  about 15 ms. Two more facts from the run: the fixed cap of 300 did cut this recording (543 resolvable rows), and
  105 of the 648 functions were dropped by the handler as no longer resolvable, so `distinct_funcs` overstates
  what a fetch can return. Not measured: a recording with combat or dialogue, which may fire more functions. At
  about 265 bytes and 0.025 ms per row, a table of 32,768 rows would be roughly 9 MB and 0.8 s, an
  extrapolation, not a measurement; measure again if a game ever comes near it.

-----

## PR 539 — Dump All schema

### What the PR does

- Dump All also writes `kind:"struct"` lines (ScriptStruct and UserDefinedStruct, same shape as a class line),
  `kind:"enum"` lines with `entries[{n,v}]`, and a `params` array on every function.
- A second menu item, "Dump All + object index", adds one `kind:"instance"` line per live object.
- Dump Explorer reads the new kinds and skips instance lines; `diff_dumps.py` compares structs, enums and params,
  and does not report a change when the older file simply predates the key. Its self-test passes on the PR head.

### Maintainer decisions (2026-10-02)

| # | Item | Decision |
|---|---|---|
| D1 | **struct lines** | **Take.** `diff_dumps.py` can then report a struct field that moved after a game patch, and SDK / USMAP already export structs while the `.jsonl` did not. |
| D2 | **enum lines** | **Take.** |
| D3 | **Function `params`** | **Take.** Dump Explorer can search by parameter name. |
| D4 | **Object index** | **Take, as an opt-in that is OFF by default, with a size estimate and a confirmation before it runs** — see "D4 — the object index" below. *(Reversed the same day: the first decision was "do not take", see the history note there.)* |
| D5 | **`diff_dumps.py`: struct added / removed** | **Add.** The PR reports only changed structs; classes report added, removed and changed. |
| D6 | **`DumpJsonlContext` doc comment** | **Move it back above the attributes.** The PR placed `/// <summary>` after the `[JsonSerializable]` attributes. Probably no compile error, but it is in the wrong place. **Only the PR has this:** `dev` is correct (checked 2026-10-02), so the rule is just "do not copy the PR's placement". |
| D7 | **Progress reporting** | ✅ **Done ahead of D1–D3 in `14ecb189`** (500 ms, see "Landed ahead of the plan"); when structs land, they report through the same throttle. **On a timer, not on the class count.** Report every 0.5–1 s (a constant defined in the code), counting classes **and** structs. The PR keys it on `classes % 50 == 0` while counting only classes, so once the class count sits on a multiple of 50 (including 0) **every struct line** posts a report — potentially thousands of UI-thread posts — and the comment "Matches old behaviour exactly" is no longer true. |
| D8 | **Settings persisted** | Any new option (the diff settings below included) survives a UI restart through `UiOptionsSettings`, same rules as L5. |

### D4 — the object index (opt-in)

**Decision history.** First decided "do not take": the addresses are valid only for that run of the game, it is
one line per live object (often hundreds of thousands to over a million, each needing its full path through
`get_object_list` with `include_path`), and Instance Finder / Object Tree already answer the question live.
**Reversed on 2026-10-02** after checking what the two reference dumpers do (their current `main`, read from
source):

#### How the two reference dumpers do it

Read from source on 2026-10-02: Dumper-7 `Encryqed/Dumper-7` @ `dd8fe34d` (2026-09-12), RE-UE4SS
`UE4SS-RE/RE-UE4SS` @ `e3ba1016` (2026-09-29). Neither repository is vendored here, so re-read them before
quoting either one as current.

| | **Dumper-7** | **RE-UE4SS** |
|---|---|---|
| File | `GObjects-Dump.txt`; also `GObjects-Dump-WithProperties.txt` on games that use FProperty (4.25+) | `UE4SS_ObjectDump.txt` |
| Code | `ObjectArray::DumpObjects` / `DumpObjectsWithProperties`, called from `Generator::Generate` | `UE4SSProgram::dump_all_objects_and_properties` |
| When | **Automatically**, once per injection, the first time the SDK is generated (`bDumpedGObjects`). There is no separate switch: generating the SDK writes it. | **On request**: keybind (default `Ctrl`+`J`, set in `Mods/Keybinds/Scripts/main.lua`), the "Dump Objects & Properties" button in the GUI console, or Lua `DumpAllObjects()` |
| Which objects | Every non-null slot of GObjects, in index order: types, CDOs and instances alike | Every object in GUObjectArray (`UObjectGlobals::ForEachUObject`): types, CDOs and instances alike |
| One object line | `[index] {address} full name` — format string `"[{:08X}] {{{}}} {}\n"`: index in 8 hex digits, the address, then the full name (`Class Outer.Name`) | `[address] Class path [n: name index] [c: class address] [or: outer address]` (the example in its `docs/feature-overview/dumpers.md`) |
| Properties | Only in the `-WithProperties` file: under each struct or class, one line per property — offset, address, property class, name | Under each struct, class and function: one line per property with its offset and the related type pointers (struct, enum, inner, key / value, …); an enum lists its values |
| Property values | No | No |
| Header | `Object dump by Dumper-7`, the game version and name, `Count: N` | None |
| Where | `<SDKGenerationPath>\<GameVersion>-<GameName>\` (default `C:\Dumper-7`); an existing folder is renamed to `_OLD`, or to a timestamped backup when `bCreateUniqueBackups` is set | The UE4SS working folder |
| Options | None for this file | `[ObjectDumper] LoadAllAssetsBeforeDumpingObjects` (default 0) force-loads every asset first; the ini warns it can take gigabytes of memory and can crash the game |
| Size handling | None: streams straight to the file | Builds the whole dump in one wide-character string (it reserves 200,000,000 characters) and writes it at the end. Its own comment notes that using `wchar_t` doubles the file size. |
| Estimate or confirmation | No | No |

What this tells us:

- Both ship the object list although its addresses die with the session too. Users of those tools expect it.
  Our UI has **no** export of the whole object list today: Object Tree and Instance Finder answer live, but
  nothing can be grepped later for "what was loaded at that moment".
- Neither warns about size. Dumper-7 sidesteps it by making the file part of a step that is slow anyway;
  RE-UE4SS makes it an explicit action. Our D4.2 (estimate, then confirm) goes further than either.
- Dumper-7's `[index]` is worth copying: with the index, two lines in two files can be matched by GObjects slot,
  not only by name.
- Both write plain text, not JSON. Our index is a separate file (D4.3); JSON Lines keeps it machine-readable and
  still greppable.

**Maintainer decision, 2026-10-02:**

| # | Rule |
|---|---|
| D4.1 | **Opt-in, OFF by default.** A checkbox for the object index; the plain Dump All never writes it. |
| D4.2 | **Estimate before export.** When the box is ticked, the export first shows an estimate — object count, file size, roughly how long — and asks the user to confirm. No confirmation, no index. |
| D4.3 | **A separate file**, `<name>.objects.jsonl`, written next to the Dump All file — **one extra file per export**, every object one line in it, never a file per object. Ticked: two files (`<name>.jsonl` + `<name>.objects.jsonl`); unticked: one. The Dump All `.jsonl` never carries `kind:"instance"` lines. |

**Design notes (first review — confirm while building):**

- **Estimating.** The object count is known before the export (`EngineState.ObjectCount`, or the first
  `get_object_list` page's `Total`). Fetch ONE page with `include_path`, apply the same row filter as the export
  (`ReflectionMetaClassifier.IsLiveInstanceRow`), serialize those rows exactly as the export will, and extrapolate
  bytes per scanned object × object count. The same page's round-trip time × the number of pages gives the time.
  Show it as an approximation ("≈ 180 MB, ≈ 2 min"), because the pool's later pages are not the first page.
  Record in the log how far the estimate was off on the real export, so the sample size can be tuned.
- **Why a separate file (D4.3).** The Dump All file stays the shareable class dump (the PR's own tooltip warned
  not to share the instance version), and `diff_dumps.py`, `analyze_dumps.py` and Dump Explorer never have to
  skip a million lines — so the PR's "skip `kind:"instance"`" change to `DumpJsonlReader` is not needed. Dumper-7
  does the same (`GObjects-Dump.txt` beside its SDK folder). The PR's in-file form was the alternative and was
  not chosen.
- **The file.** A first line with the same identity as the Dump All meta line (game, module, UE version, dumper
  build, object count) so the two files can be paired, then one line per object. Carry the GObjects **index**
  (Dumper-7's lesson) next to the address, class, outer and full path. The PR wrote instance lines during
  Dump All's pass 1; with a separate file the index needs its own writer, and it can reuse that pass's page walk
  only if both files are open at once.
- **Persistence.** Follows D8: the checkbox state is remembered like every other option. Because the confirmation
  appears on every export, a remembered ON can never run the big export silently.
- The export stays cancellable through the existing Dump All cancellation; a cancelled index leaves the class dump
  intact.

### PR 539 finished (2026-10-06): E1, D5, D4, D8, the reviews and the live check

**Built** (red before green each; the trailer on the commits that carry the PR's functionality):

| Item | Red | Feature / fix | What |
|---|---|---|---|
| E1 — Dump Explorer reads the new lines | `06f3b9ab` | `ea9c94fa` | struct, enum and enumerator rows (enumerator labelled "Enumerator"); a function row shows its arguments and is found by a parameter name; the live match keyed by kind and short name (a class and a struct share names; PR 539 kept one dictionary); the category picker reaches every kind, appended so old indices hold; Find Instances says why it does nothing on a struct or enum; the header counts structs and enums |
| D5 — `diff_dumps.py` | `5d0e4b0f` | `513fdd16` | structs diffed as classes, with detail; enums by path with their enumerators; parameters when both files carry them; no comparison of what a file cannot say (an older dump, an unread or cut-short enum list, enums without names) |
| D4 — the object index | `344cc1cb` | `41cf418d` | opt-in "Dump All also writes the object index" (Export menu, OFF by default, persisted); one page's estimate and a confirmation before the class dump; `<name>.objects.jsonl` written after the class dump is published, every object with its GObjects index; the DLL's `get_object_list` gained `include_index` (R3) |
| D8 — persistence | (in D4's red) | `41cf418d` | the only new option, the object-index tick, is in `UiOptionsSettings.Main` |

**Reviewed** (3 reviewers + 3 adversarial verifiers; 24 findings, 1 MED, 1 refuted), each fixed red before green:
D5 `bbfccdbe` → `fded39a0` (the report's warning sign crashed a redirected cp950 stdout — MED; a file cut off
before its summary; walks that failed; the return entry; a struct that only grew is breaking; the `--minimal`
banner), D4 `6a3c02bf` → `4e0a2f4e` (objects vs slots in the estimate; `analyze_dumps.py` skips the index; a
stale sibling index is called out; the duration wording), E1 `bf91f6af` → `0c108ef5` + `49b092fc` (the
summary's enum flags; a return's struct or class; the object index file recognised; comments), docs `d818fe5c`.
Also: Dump All's log line times the class dump (`86945805`).

**Live, build 3625, 2026-10-06** (one game at a time):

| Check | Host | Result |
|---|---|---|
| Dump All, class dump | DumperTest 5.4 Shipping | 15.6 MB in 5.6 s (4.4 s the second time): 3,868 classes, 3,820 structs, 1,568 enums, 0 errors, `params_from_num_parms` 0. The build-3550 class-only dump of the same game was 10.3 MB |
| Dump All, class dump | Avowed (UE 5.3, main menu, 92,036 objects) | 39.9 MB in 13.4 s: 7,404 classes, 5,562 structs, 2,142 enums, 0 errors |
| Object index | DumperTest | estimate "about 24,518 objects, roughly 4.3 MB, less than a second"; wrote 24,511 objects (24,518 slots), 4.9 MB in 0.4 s |
| Object index | Avowed | estimate "about 92,036 objects, roughly 15.7 MB, about 3 s"; wrote 86,036 objects (92,036 slots), 19.5 MB in 1.7 s — the first page had no hole and shorter paths than the rest, so the objects were 7% over and the bytes 15% under |
| `include_index` read back (`d4_object_index.py`) | DumperTest (5 pages), Avowed (6 pages) | PASS: every row indexed only when asked, indexes rise inside each window, 400 slots read back to the same address each, pages with skipped slots met (1 and 3) |
| Dump Explorer on the new dump | DumperTest | header "3,868 classes · 3,820 structs · 1,568 enums"; live match 112,115 of 112,115 rows, the structs and enums too; "OtherActor" finds 18 functions by a parameter name; Enumerators 9,299 rows ("= 0" values); Structs 3,820; Find Instances on a struct row says why; the object index opened says to open its class dump |
| `diff_dumps.py` on real files | DumperTest | two 3625 dumps: 2,011 structs and 561 enums compared, 0 changes; the 3550 dump against a 3625 one: structs and enums "not compared" with the reason; the object index refused by `diff_dumps.py` and skipped by `analyze_dumps.py` |

Found by the live check and fixed: the Kind column cut "Enumerator", and the previous file's header and counts
stayed over an object index (`c8310c69` → `ca63acd9`), published as build 3626 (AOT `UE5DumpUI.exe` 59,476,480 B,
sha256 `a78a0ba980fb`). A false alarm is recorded in `5eff8e84`: a menu tick sent in a separate computer-use call
after the one that opened the flyout landed once the flyout had closed; the option works.

### D3 built (2026-10-06)

The PR writes every entry walk_functions returns as a parameter. That list is the UFunction's whole property
chain, and on a Blueprint function the chain holds the locals after the parameters (the 2026-08 Y1 trap). The
maintainer chose a DLL flag (2026-10-06): walk_functions marks each entry with `parm` (CPF_Parm), Dump All
writes only the parameters, and the other consumers are left as they are and tracked in todo.md
`[FUNCPARM-CONSUMERS]`.

| Commit | What | Co-author |
|---|---|---|
| `211d8d78` red, `0fa23e3f` | DLL: `FunctionParam::isParm` from CPF_Parm in both property models; `walk_functions` always sends `parm` | no (our design) |
| `e7fa096c` | Review: UFUNCPARM's entries carry real non-Parm bits (`isParm = propFlags != 0` passed the old fixture) | no |
| `b075059a` red, `111b3d43` | The parser reads `parm` into a nullable `FunctionParamModel.IsParm` (null: an older DLL) | no |
| `e3ad5508` red, `6847b080` | Each function on a class line carries `params` (the walker's keys; `out` / `ret` only when set; no `struct_fields`); only flagged entries; an older DLL gets the leading `num_parms` entries and the summary's `params_from_num_parms` counts those functions; the tooltip names parameters. A later test pins that the flag wins over a misread `num_parms` | `6847b080` |
| `c3d4f348` red, `0c1d5d4b` | Review fixes: every number the writers emit is culture-invariant (a `-1` offset was invalid JSON under a U+2212 minus); `DumpResult.ParamsFromNumParms`; comments that called the chain "parameters" | `0c1d5d4b` |
| `fdeb7dfb` | `tools/verify/d3_parm_flags.py`, the live rig: the flag against `num_parms` / `parms_size` | no |
| `62bba6fa` | `parm` in `pipe-protocol.md`, `params` in the `.jsonl` schema, the diff's docstring, the Dumper-7 aside above | no |

Published as build 3622 (AOT `UE5DumpUI.exe` 59,266,048 B, sha256 `349cba66cb93`; `UE5Dumper.dll`
`c67e6ad2a6d4`). C# 6109/6109, dll_core 602/602. Review: 3 reviewers and 3 adversarial verifiers, 17 findings
(16 held, 1 refuted), all LOW / INFO / docs.

**Live, build 3622** (`d3_parm_flags.py`, one game at a time, results in `out/d3/`):

| Host | Property model | Functions with parameters, `parm` count = `num_parms` | Functions with locals / locals | Verdict |
|---|---|---|---|---|
| Avowed (UE 5.3, `dxgi.dll` proxy refreshed to 3622) | FField | 5,561 of 5,561 | 1,126 / 15,820, all `false`, none inside the parameter block | PASS |
| UE423_Flying Shipping (injected) | UProperty (`use_fproperty` false) | 4,822 of 4,822 | 4 / 102 | PASS (`--min-locals 1`) |
| DumperTest 5.4 Shipping (injected) | FField | 8,001 of 8,001 | 1 / 16 (`ExecuteUbergraph_ABP_Manny`) | below the rig's 10-function floor |

No parameter followed a local on any host, so the older-DLL fallback's assumption held too.

**Owed:** Dump Explorer's parameter search, the reason D3 was taken (PR 539's `DumpFuncParamLine` /
`ParamHaystack` are the reference), with the Explorer item; `diff_dumps.py` comparing `params` (D5); the other
consumers (`[FUNCPARM-CONSUMERS]`, fixed in build 3624, its live check owed); the live Dump All time and size measurement, now that D1–D3 are in.

### D2 built (2026-10-06)

| Commit | What | Co-author |
|---|---|---|
| `268bd42d` red, `0c43deae` | One `list_enums` call (`ListEnumsDetailedAsync`) after the type walk; each UEnum is a `kind:"enum"` line with `name` / `addr` / `path` / `entries[]` (`{name, value}`); "Game classes only" skips engine enums by path; a list that fails is an error line named `list_enums` and the dump completes; summary `enums_emitted` / `enums_skipped_engine` / `enums_listed` / `enum_names_failed` / `enums_truncated`; progress "Listing enums"; `DumpResult.EnumsEmitted` / `EnumsSkippedEngine`; the completion message and the tooltip name enums | `0c43deae` |
| `dc4462ce` red, `ed9a3da7` | Review fixes: `DumpResult` carries the enum list's three flags and the completion status names the worst one, as USMAP does `[P1-ENUMNAMES]`; "Listing enums" no longer shows the type count, which read as an enum count; the log line counts structs and enums; stale comments | `ed9a3da7` |
| `c2b04a2e` | The `.jsonl` schema in `scripts/analysis/README.md` (the enum row, the `list_enums` error line, the summary's enum keys), the analysis scripts' docstrings, the export coverage table, the closed R7-D-02 recipe's expected message | no |

Published as build 3621 (AOT `UE5DumpUI.exe` 59,252,736 B, sha256 `2eca0528045c`). C# 6099/6099. The enum
compare in `diff_dumps.py` is D5, which must honour `enums_listed` and `enum_names_failed`. **Owed:** the same
live measurement as D1, after D3.

### D1 built (2026-10-06)

| Commit | What | Co-author |
|---|---|---|
| `0ecb9349` red (+ test fixes `e7127326`, `bc2f66b2`), `e68aa754` | The type walk admits `IsExportedTypeRow` rows; a ScriptStruct / UserDefinedStruct is walked in the same batch and written as `kind:"struct"` (a class line's identity, super, `props_size`, `props`; no `funcs`, `instance_count`, `is_bpgc`; no `walk_functions`); summary `structs_emitted` / `structs_skipped_engine`; progress "Walking classes and structs", Done counting both; `DumpResult.StructsEmitted`; the completion message names structs; the tooltip names structs | `e68aa754` |
| `6ee5e93c` red, `3d8643f1` | Review fixes: a struct walk the DLL refused (empty result) is an error line, not a struct named ""; `DumpResult.StructsSkippedEngine`; comments that listed two callers or only classes | `3d8643f1` |
| `24407c2f` | The `.jsonl` schema in `scripts/analysis/README.md`, the export coverage table, the closed R7-D-02 recipe's expected message, stale test comments | no |

Published as build 3620 (AOT `UE5DumpUI.exe` 59,237,376 B, sha256 `1488b5154285`). C# 6085/6085. Class lines keep
writing an empty walk as before (a pre-existing behaviour the review noted, not changed here). **Owed:** the live
measurement of Dump All's time and size on a game, after D2 and D3.

### Other problems found (first review — fix while taking D1–D3)

- **Dump Explorer can jump to the wrong object** (a small regression of an existing feature). Its live
  name-to-address index was class-only; the PR puts classes, structs and enums in **one** dictionary keyed by
  short name, and the last write wins. A struct with the same short name as a class overwrites the class's
  address, so a class jump that used to be right can land on the struct. Key it by kind (or keep three indexes).
- **Enum and enumerator rows share the label "Enum"** in the Explorer's kind column. Give the enumerator its own
  label.
- **The Dump All tooltip.** ✅ **Its numbers were fixed 2026-10-02, ahead of the rest:** the maintainer decided
  the tooltip states no time or size at all ("~30-60s … 50-500 MB" became "Time and file size vary with the game
  and the export settings"). Still owed when D1–D3 land: its list of contents ("class + property + function")
  must name structs, enums and parameters. Do not put numbers back.
- **The default Dump All grows** (every struct walked, `list_enums`, params on every function). Decided: these
  stay in the default dump (D1–D3). Measure the time and size on a real game for the dev-log entry — structs
  alone are often more than ten thousand extra walks on UE5 — but not for the tooltip.
- Fine as written: older dumps still load; `analyze_dumps.py` reads only class lines; enum paths come from
  `list_enums`' `full_path`, so a game-only dump does drop engine enums.

### Open question — a diff that users can actually run

✅ **Decided by the maintainer, 2026-10-06:** the C# port inside the UI, **HTML only** (no CSV), started from
**a button in Dump Explorer**, the page that already opens a dump. Built as `[DUMPDIFF-UI]` in `todo.md`.
✅ **Built, reviewed and checked live the same day, build 3628** — see "[DUMPDIFF-UI] built" below.

#### [DUMPDIFF-UI] built (2026-10-06, builds 3627–3628)

**What it is.** Dump Explorer ▸ **Compare…** (a SplitButton, the repository's first): the loaded class dump
against one the user picks; the dump taken earlier (`dumped_at`) is the old one, the loaded one when that cannot
be told. The report is one self-contained HTML page with the sections and wording of `diff_dumps.py`'s Markdown
report, saved where the user says and opened. The ▾ holds **Include engine types** and **Breaking changes only**,
OFF by default and remembered (`UiOptionsSettings.DumpExplorer`). Its footer says, as the maintainer asked, that the
same diff is `scripts/analysis/diff_dumps.py`, a CLI only a clone of the repository has.

**Held to the script.** `DumpDiffService` is the script's diff rule for rule, its quirks included (a duplicate path
keeps the first record, a duplicate member its first place and last value, ties keep file order, paths sort by code
point). `diff_dumps.py --write-fixtures` writes `scripts/analysis/fixtures/diff_dumps/` (30 cases: two dumps and the
script's `canonical()` for them); the script's self-test fails when they no longer match it, and
`DumpDiffParityTests` fails when the port no longer matches them. `tools/verify/dumpdiff_real_parity.py` runs the
same comparison on a pair of real dumps (env-gated test, skipped elsewhere).

| Commit | What | Co-author |
|---|---|---|
| `af5fc321` | `canonical()`, `--write-fixtures`, 24 cases, the self-test checks them | no |
| `b5ee1538` | red: parity, the HTML report, the panel | no |
| `3253d6c7` | two cases the mutation check needed (one parameter field at a time; 40 equal sort keys) | no |
| `037e5ac1` | `DumpDiffService` (the port) and `DumpDiffHtmlRenderer` | yes |
| `31532d3c` | the Compare SplitButton, its options, persistence, a headless test of the SplitButton | no |
| `9315b461` | the real-pair rig | no |
| `a5771ad8` | build 3627 (AOT, first live check) | no |
| `de835692` | review: one loading rule for script and port (types Dump All never writes skipped and counted by both, null = absent, BOM stripped, flags as text), 4 cases | no |
| `07e95398` | review: the port skips a line with a null inside a list; the unreadable-lines note on both sides | yes |
| `beaaa4d2` | red: the review's report and panel findings | no |
| `ee491f88` | review: invariant numbers; the different-games banner | yes |
| `a16705bc` | review: a non-dump refused, "another game" by the live match's rule, the status counts, Cancel before the save dialog, the error line, the filter string, the tooltip while disabled | no |

**Checked.** 30 of 30 cases; 10 + 9 + 16 mutations of the port, the report and the review fixes each fail a test
(the first two rounds found three holes, now cases or asserts). Real pairs, all exact: two DumperTest 3625 dumps
(0 changes); the 09-12 DumperTest dump (build 3546) against 3625, game-only and with engine types (structs and enums
"not compared" with the reason); DumperTest against Avowed with engine types (4,064 classes added, 528 removed, 1,004
changed, plus structs and enums) — about 4 s for that pair. The review (3 reviewers, 3 verifiers; findings merged
across them): 1 MED, 6 LOW and 9 INFO, all confirmed and fixed; one more judged by design (the SplitButton greys
its arrow with the command, so its options wait for a loaded dump).

**Live, build 3627 and 3628** (no game needed): the button greyed until a dump is loaded; 09-12 → 3625 written,
opened in the browser and read (2 classes added, `DumperTestActor` +36 properties, structs and enums "not compared",
the footer line); both options ticked, saved to `ui-options.json` and back after a restart; DumperTest → Avowed with
both options: the minimal report (821 / 397 / 41) and the different-games status; the object index refused by
name; the 3627 check found the status's "0 structs" for a kind not compared, fixed in 3628. Not checked: the
report's dark colours (the preview pane renders local files light only).

`diff_dumps.py` has to be run by hand, and the maintainer doubts many users will. Facts that bear on it:

- **A release user does not have the script.** `build.ps1` copies the `.CT`, `ue5_dissect.lua`, `inject-ue.ps1`
  and the two HTML guides into `dist\`, nothing under `scripts/analysis/`. The UI never starts Python today, and
  Python is not a prerequisite of the tool.
- So **"UI starts the .py"** and **"UI shows a command line to copy"** both work only for someone with a repo
  checkout and Python installed. They are cheap (S), but they reach almost no one who downloads a release.
  Starting a process from the UI would also need a `Core` interface (platform-abstraction rule).
- **A C# port inside the UI** is the option that reaches release users. Estimate **M (one session), low risk**:
  `diff_dumps.py` is about 890 lines including its self-test and report renderer; the diff core is a few hundred.
  No new grid view. The Python script stays as the reference, and the C# port is tested against the same
  fixtures as its `--self-test`.
- **Output format — maintainer, 2026-10-02: HTML or CSV, not Markdown.** Most users never open a `.md` file.
  The two suit different readers, and both can be rendered from the same diff result:
  - **HTML** — the readable report: summary counts, then added / removed / changed classes and structs, each
    change under its owner. Opens in any browser; `dist\` already ships two HTML guides. Every name must be
    HTML-escaped.
  - **CSV** — one row per change (kind, owner, path, member, change, old / new offset, old / new type), for
    sorting and filtering in a spreadsheet. Write UTF-8 **with** a BOM so Excel reads non-ASCII names, quote
    every field, and prefix a cell that starts with `=`, `+`, `-` or `@` so a spreadsheet does not run it as a
    formula.
  - First-review suggestion: HTML as the report, CSV as an extra save. Which one, or both, is the maintainer's
    call. Python's `--output` stays Markdown for repo users.
- Settings to persist either way (D8): include-engine and minimal-report flags, the output format, and the last
  folder used.
- **First-review recommendation:** the C# port, after D1–D7 land. Decided as above: the port, HTML only.

-----

## Before calling any of this done

- Windows: `dotnet test ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj -c Release`, then `build.ps1 -Mode Publish` and
  check the AOT-trimmed size — both PRs change the UI, and every AOT bug in this repo's history was found only after
  trimming.
- A live check on a fixture: a Live Funcs recording with the slider at 32768, and a Dump All on a UE5 game with the
  time and size measured for the dev-log entry. With the object index ticked: the estimate shown before the export
  against the real file size and time.
- Then write the reply on both PRs, and record the shipped build in [dev-log.md](dev-log.md).
