# External PRs 539 / 540 — first review and the maintainer's decisions `[EXTPR-539-540-2026-10-02]`

**Status: PLAN — no PR feature is built yet** (three small changes landed early and shipped in build 3616, D7 among them — see "Landed ahead of the plan"). **Re-checked 2026-10-06**, with four questions for the maintainer (R1–R4).
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

**Maintainer decisions the re-check raises** (details in the lists below):

| # | Question |
|---|---|
| R1 | Min calls in diff mode: exempt NEW rows, or only warn in the tooltip? |
| R2 | D4's separate file: every object (as Dumper-7 and RE-UE4SS), or keep the `IsLiveInstanceRow` filter and say so? |
| R3 | D4's GObjects index: accept the DLL change it needs (a new build of the DLL, not only the UI)? |
| R4 | `diff_dumps.py`'s engine test (`/Script/` anywhere in the path) also drops the game's own native classes. Change it before D1/D5 and the C# port build on it? |

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
  properties are its parameters). A property line puts the offset where an object line puts the index.
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
**Every commit that carries one of these features ends with**

```
Co-authored-by: fireundubh <1261664+fireundubh@users.noreply.github.com>
```

(the address is GitHub's noreply form of the contributor's account id, read from the PR data). The reply on the
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
- **First-review recommendation:** the C# port, after D1–D7 land. Not decided — the maintainer will choose.

-----

## Before calling any of this done

- Windows: `dotnet test ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj -c Release`, then `build.ps1 -Mode Publish` and
  check the AOT-trimmed size — both PRs change the UI, and every AOT bug in this repo's history was found only after
  trimming.
- A live check on a fixture: a Live Funcs recording with the slider at 32768, and a Dump All on a UE5 game with the
  time and size measured for the dev-log entry. With the object index ticked: the estimate shown before the export
  against the real file size and time.
- Then write the reply on both PRs, and record the shipped build in [dev-log.md](dev-log.md).
