# External PRs 539 / 540 — first review and the maintainer's decisions `[EXTPR-539-540-2026-10-02]`

**Status: PLAN ONLY — nothing here is built.** First-pass review on 2026-10-02 plus the maintainer's decisions
on the same day. ⚠ **The review is a first reading, not a verdict**: the maintainer will re-read both PRs, and a
row below can still change. Close a row by editing it here AND its line in [todo.md](todo.md) in the same commit.

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
| L1 | **Fetch limit** | A **slider over powers of two**: value = 2^x, step one power (2^n). **Default 2^9 = 512, max 2^13 = 8192.** The minimum was not stated; proposal 2^6 = 64. Replaces the fixed 300. |
| L2 | **Save .jsonl** | Keep. Disabled while recording, so the header's `recording` field is always false and can be dropped. |
| L3 | **Min calls** | A **slider: default 1, max 32, step 2^n** (1, 2, 4, 8, 16, 32). **It affects only the NEXT capture, never data already on screen**: the value is read at Start and used for that capture; moving the slider afterwards changes nothing until the next Start. The tooltip must say so. Chosen over "filter the current table at once" because it needs no answer to "is a raised minimum reversible?". |
| L4 | **Recording lock** | All three controls (both sliders and the Save button) are **disabled (greyed out) while a recording runs**, and usable before and after it. Refresh during a recording then always uses the cap fixed at Start. |
| L5 | **Persisted** | Fetch limit and Min calls survive a UI restart: a new `LiveFuncs` sub-object in `UiOptionsSettings` (`ui-options.json`), defaults equal to the VM initializers (that file's own rule), every field written (`check_json_default_ignore`). A loaded value snaps to the nearest power of two and clamps to the range. |

### Implementation notes (from the review — confirm while building)

- **Min calls must stay a VIEW filter.** `SetBaseline` builds the baseline from the unfiltered `_allEntries`. If
  Min calls filtered `_allEntries` itself, an idle function with one call would be missing from the baseline and
  come back as a false NEW. Apply it in `ApplyFilter` with the value captured at Start.
- Fix problem 1 (status strings to `en.axaml`), problem 3 (the row must keep Clear Baseline and the baseline
  warning visible — a second row or a `WrapPanel`), and problem 4 (the stale "shorter window" sentence).
- **Measure** `pe_profile_get` at 8192 on a busy game (the DLL logs `emitted (limit N)` in `PIPE:profile`). If the
  interactive lane stalls noticeably, decide then whether the command moves to the bulk lane; do not move it on
  a guess.

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
| D6 | **`DumpJsonlContext` doc comment** | **Move it back above the attributes.** The PR placed `/// <summary>` after the `[JsonSerializable]` attributes. Probably no compile error, but it is in the wrong place. |
| D7 | **Progress reporting** | **On a timer, not on the class count.** Report every 0.5–1 s (a constant defined in the code), counting classes **and** structs. The PR keys it on `classes % 50 == 0` while counting only classes, so once the class count sits on a multiple of 50 (including 0) **every struct line** posts a report — potentially thousands of UI-thread posts — and the comment "Matches old behaviour exactly" is no longer true. |
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
- Both write plain text, not JSON. If our index is a separate file (below), JSON Lines keeps it machine-readable
  and still greppable.

**Maintainer decision, 2026-10-02:**

| # | Rule |
|---|---|
| D4.1 | **Opt-in, OFF by default.** A checkbox for the object index; the plain Dump All never writes it. |
| D4.2 | **Estimate before export.** When the box is ticked, the export first shows an estimate — object count, file size, roughly how long — and asks the user to confirm. No confirmation, no index. |

**Design notes (first review — confirm while building):**

- **Estimating.** The object count is known before the export (`EngineState.ObjectCount`, or the first
  `get_object_list` page's `Total`). Fetch ONE page with `include_path`, apply the same row filter as the export
  (`ReflectionMetaClassifier.IsLiveInstanceRow`), serialize those rows exactly as the export will, and extrapolate
  bytes per scanned object × object count. The same page's round-trip time × the number of pages gives the time.
  Show it as an approximation ("≈ 180 MB, ≈ 2 min"), because the pool's later pages are not the first page.
  Record in the log how far the estimate was off on the real export, so the sample size can be tuned.
- **Where the lines go — recommendation, not decided.** A **separate file** next to the dump
  (`<name>.objects.jsonl`) rather than `kind:"instance"` lines inside the Dump All file. "Separate" means **one
  extra file per export**, holding every object as one line — never a file per object. An export with the box
  ticked writes two files (`<name>.jsonl` + `<name>.objects.jsonl`); unticked, one. That is how Dumper-7 does it
  too (`GObjects-Dump.txt` beside its SDK folder). Then the Dump All file
  stays the shareable class dump (the PR's own tooltip warned not to share the instance version), and
  `diff_dumps.py`, `analyze_dumps.py` and Dump Explorer never have to skip a million lines. The PR's in-file form
  is the alternative.
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
- **The Dump All tooltip is stale.** It still reads "class + property + function … ~30-60s … 50-500 MB". Update it
  with what the dump now contains and with **measured** time and size on a real game — structs alone are often
  more than ten thousand extra walks on UE5.
- **The default Dump All grows** (every struct walked, `list_enums`, params on every function). Decided: these
  stay in the default dump (D1–D3); the measurement above is what the tooltip and the dev-log entry report.
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
- A live check on a fixture: a Live Funcs recording with the slider at 8192, and a Dump All on a UE5 game with the
  time and size measured for the tooltip. With the object index ticked: the estimate shown before the export
  against the real file size and time.
- Then write the reply on both PRs, and record the shipped build in [dev-log.md](dev-log.md).
