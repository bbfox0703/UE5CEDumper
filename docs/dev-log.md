# Dev Log

Append-only milestone history, newest first. Each entry references a
build number from `build_number.txt` so commits can be cross-referenced.
**Reading tip:** grep `^## ` for the index, then read the top (newest-first).
Entries for **builds ≤3261** are archived (update this number on every split: after the third one it
lagged three weeks). Builds 2779–3261 in
[archive/dev-log-2026-08-pre-build-3263.md](archive/dev-log-2026-08-pre-build-3263.md),
builds 2220–2747 in
[archive/dev-log-2026-08-pre-build-2779.md](archive/dev-log-2026-08-pre-build-2779.md),
builds 1800–2168 in
[archive/dev-log-2026-07-pre-build-2200.md](archive/dev-log-2026-07-pre-build-2200.md),
builds 1178–1799 in
[archive/dev-log-2026-06-pre-build-1800.md](archive/dev-log-2026-06-pre-build-1800.md),
builds 939–1177 in
[archive/dev-log-2026-06-pre-build-1180.md](archive/dev-log-2026-06-pre-build-1180.md),
builds 715–937 in
[archive/dev-log-2026-06-pre-build-940.md](archive/dev-log-2026-06-pre-build-940.md),
builds ≤696 in
[archive/dev-log-2026-05-pre-build-700.md](archive/dev-log-2026-05-pre-build-700.md).

> **Looking for current state?** See [roadmap.md](roadmap.md) for the
> capability matrix / per-game configuration / tested games, and
> [todo.md](todo.md) for the prioritized next-work list. This file
> records *what shipped* — the other two record *what works now* and
> *what's next*.

-----

## 2026-10-08 (no build change) — Correction to the entry below `[LIVEFUNCS-STEP3]`

- The integrated GPU's ~100 % was the desktop's, not the fixture's: with no game running it stayed there, `dwm.exe`
  54 % and the Claude desktop app (computer use's screen effect) 45 % of its 3D engine. "Copying frames to the
  display" below is wrong. The capture costs below stand.

-----

## 2026-10-08 (no build change) — What a native stack capture costs, and what it does not depend on `[LIVEFUNCS-STEP3]`

- **Measured on the maintainer's PC (Ryzen 9 9955HX3D laptop, Radeon iGPU + RTX 5090):** a 16-frame capture costs
  about 3.25 µs on Avowed (6,200 captures, the 64 busiest functions) and 20-31 µs on the DumperTest58 fixture. The
  fixture's cost is per walked frame (about 0.75 µs), and it is not the hybrid GPU or the frame rate:
  - on the integrated Radeon, uncapped: median 20-22 µs (the iGPU at 98 %, 90 °C);
  - on the RTX (`-preferNvidia`), uncapped: 21-22.5 µs -- the iGPU still at 100 % copying frames to the display, the
    CPU at 85-94 % of nominal, not boosting;
  - on the RTX at 30 fps: 31 µs -- dearer, the game thread's caches cold between frames.
- The budget the maintainer chose (50 a second in all) rests on the fixture's figure, so it is the conservative one;
  on a game like Avowed it costs about a seventh of what it allows for.
- The native-entry index (build 3643's S3-A1) first took 3.9 s on the fixture, one VirtualQuery per function at about
  300 µs each while the game churned its address space; a per-region cache brought it to 11 ms.

-----

## 2026-10-08 (build 3642) — A smaller stack budget; the Object Tree stays collapsed `[LIVEFUNCS-STEP3]` `[OT-COLLAPSE-PERSIST]`

- **The native-stack budget is smaller:** Standard takes at most 25 stacks a second per function and 50 in all; Low
  12 and 25. A capture measured 20-28 µs on the test fixture, so 50 a second keeps it near 1.4 ms of the game's time a
  second. (On Avowed a capture measured 3.25 µs.)
- **The Object Tree remembers that it was collapsed.** Collapse it with its arrow and the next start opens it
  collapsed; expand it and the next start opens it expanded.

-----

## 2026-10-08 (build 3641) — Live Funcs takes a function's native call stack `[LIVEFUNCS-STEP3]`

- **Native call stacks (experimental, with Trace).** A "Stack?" column (header "S") chooses functions. Each traced call
  of a chosen function keeps the native return addresses above it, the game's caller first, 16 frames deep. Any
  function can be chosen, with or without parameters, and a function can be chosen for both.
  - A per-second budget caps how many stacks are taken: **Standard** (100 a second per function, 200 in all) or
    **Low** (half of that). The first calls of each second are kept.
  - **A warning shows while stacks are chosen.** A stack is read in software inside the hooked call, on the game's own
    thread, not by a debugger as Cheat Engine does. A capture measured 20-28 µs in a 160 MB game on a fast PC, and
    that time is added to the game's frame. The walk is guarded (the stack's bounds and reserve, faults), but an
    unforeseen case could still stall or crash the game: save first, and choose few functions.
  - Stop says how many stacks were kept, how many calls the budget left without one, and what a capture cost.
- **Call Trace has a "Call stack" tab.** Each frame shows its address (following the Address setting) and where it
  is: "+0x… into the function at "Game.exe"+RVA", "native entry of Class::Func" for a traced function, "UObject::
  ProcessEvent", or "the dumper's ProcessEvent hook". Notes say when the stack was cut, recorded alone, over the
  budget, or untrustworthy below a frame without unwind data.
  - **Copy** puts the frame in Cheat Engine's own form, `"Game.exe"+RVA`, whatever the Address setting, so it
    survives a relaunch. **ASM** moves Cheat Engine's disassembler there (with the AOBMaker plugin).
  - Rows with a stack carry `(s)`, and the summary counts them.
- A call chosen only for its stack no longer reads "overwritten" in the Parameters tab: it says it was not chosen.
- Checked live on DumperTest58 (UE 5.8 Shipping): every stack taken inside a traced call shows ProcessEvent and the
  hook above it, and none taken alone does; the budget kept 30 a second when asked for 30.

-----

## 2026-10-08 (builds 3639-3640) — Live Funcs follows functions by name and copies their parameters `[LIVEFUNCS-STEP2]`

- **Ticks follow a function by name, not by address.** A function a closed menu unloaded can be ticked from its
  "(unloaded)" row, and the trace scopes on it wherever it loads next. A name the recording never saw called is
  listed after Stop.
- **Parameter snapshots (experimental, with Trace).** A "Params?" column chooses functions. Each traced call of a
  chosen function keeps a copy of its parameters, and functions with outputs keep a second copy after the call.
  - The snapshots have their own buffer of 8 to 128 MB (default 32), remembered.
  - An estimate line says how much a minute they take and how many calls each function keeps. It turns orange when
    the busiest choice keeps less time than the trace; a grey note says what the per-function budget will skip.
  - "Choose shown rows" chooses every row on screen.
  - Choices with nothing ticked record only the chosen calls.
- **Call Trace has a Parameters tab.** Each parameter's value at the call and after it is shown, struct members
  nested, with a mark when a value names what is there now, is gone, or is a header only. "≠" flags bytes that
  changed during the call, and the raw copies are shown too.
  - The tree marks calls with a copy, and "Only calls with parameters" lists them, even with the filter box empty.
  - A call without values says why: not chosen, over the budget, overwritten since, or recorded alone.
- **The Call Trace list's columns and its detail pane can be dragged wider and are remembered.** The detail says that
  the address it shows is the UFunction object's (data, not code) and follows the Address setting. A native
  function's entry is given as a Cheat Engine address, `"Game.exe"+RVA`; a Blueprint function is marked as running
  in the interpreter.
- **Exports:** the JSONL carries each chosen call's parameters and one layout line per function load. A new
  "Export parameters CSV" writes one row per parameter.
- Checked live on Avowed: 64 inventory functions chosen by name, half of them unloaded by the time the trace was
  read, and every copy still decodes.
- 3640 makes the column handles visible: on 3639 they had no template and drew nothing.

-----

## 2026-10-07 (build 3638) — The call trace names every function, and the slider says what it costs in memory `[TRACE-UNLOADED-NAMES]` `[TRACE-UI-LOAD-MEMORY]`

- A function the game unloads during a recording (a closed inventory's widgets, content it streams out) keeps the
  name it had at its first call. Live Funcs lists it with a grey "(unloaded)" and no tick or ASM, since its address
  is gone. The Call Trace tab marks it too, and its summary always says what share of the calls has no name. An
  address that a second function took during the recording is marked "(address reused)". Both marks are in the
  JSONL / CSV export.
- A traced Start leaves out ticks whose function has been unloaded since, and says how many; if every tick has, it
  refuses.
- Loading a full trace takes far less memory. On Avowed a full 128 MB buffer now adds about 0.3 GB to the UI while it
  loads (about 0.95 GB on 3633), and a full 512 MB one about 1.05 GB (about 2.9 GB on 3633). Most of it is given back
  afterwards. With less memory free, the load collects more often.
- Beside the buffer slider: what a buffer that fills costs. That is the game's N MB from Start, then this UI's peak
  while it loads and what it keeps afterwards. The UI's figures are a reference measured on the developer's PC (a
  Ryzen 9 9955HX3D laptop with 64 GB), and the line says so. Above the computer's free memory the line turns orange
  and Start's status warns; it never stops you.
- Builds 3634–3637 were steps measured live on Avowed on the way here; their numbers are in
  [live-funcs-timeline-plan.md](live-funcs-timeline-plan.md), "D1–D4 built".
- Build 3638: AOT `dist\UE5DumpUI.exe` 60,392,960 B, sha256 `3d887e402790`; `dist\UE5Dumper.dll` `eeb84214d755`.
  C# 6335/6335 (+1 env-gated skip), headless 19/19, dll_core 738 checks, 32 gates. The DLL's code is 3634's; the
  UI needs it (3634 or later) for the unloaded marks.

## 2026-10-07 (no build change) — The call trace on Avowed: full 128 and 512 MB rings `[LIVEFUNCS-TIMELINE-2026-10-04]`

- Build 3633's call trace checked on Avowed (UE 5.3), a busy game: 9,800–14,500 ProcessEvent calls a second, so
  the 128 MB buffer keeps about 2 minutes and 512 MB about 8, as the slider's estimate said.
- A full 128 MB trace (1.7 million calls) opens in the Call Trace tab in about 11 seconds; a full 512 MB one
  (6.7 million calls) in about 44 seconds, and the UI then uses about 3.2 GB of memory. The filter and the tree
  stay responsive at that size.
- Known limits found there, to be fixed next: a function the game unloads during a long recording (content it
  streams out as you play) shows as a bare address in the Call Trace tab and is left out of the Live Funcs table;
  and loading a full trace briefly takes about six times the buffer's size in the UI.
- `tools/verify/livefuncs_trace_live.py` gained `--traced-only` / `--plain-s` for one long recording that fills
  the ring, and reports what the functions that no longer resolve account for.

## 2026-10-07 (build 3633) — Live Funcs records a call trace, read in the new Call Trace tab (experimental) `[LIVEFUNCS-TIMELINE-2026-10-04]`

- With the experimental tabs on, Live Funcs has a **Trace** row: tick it and the same Start also records every call
  in order, with what called it and how long it took, into a ring buffer in the game. The buffer is a slider from
  32 to 512 MB, with the seconds it would keep estimated beside it from your last recording; Stop keeps the calls
  just before it.
- **Tick functions in the table** to trace only inside them: tick the action's opener and get everything it called.
  With nothing ticked the trace records every call; once the table has rows to tick from, that is asked once per
  session. **Leave out per-frame** drops the previous recording's Tick-like functions.
- The **Call Trace** tab (beside Live Funcs; it cannot record on its own) shows the trace as a call tree: expand and
  collapse, a filter, Show in tree, the chain of callers of any call, and JSONL / CSV export.
- About 130 ns per traced call on the test PC; nothing when the trace is off. Needs this build's UE5Dumper.dll.
- Checked on DumperTest 5.4 and DumperTest58 (UE 5.8); DumperTest58 now calls a nested chain every 0.5 s
  (`TraceNest_*`) so a ticked scope has something to show.
- Build 3633: AOT `dist\UE5DumpUI.exe` 60,308,992 B, sha256 `886364b80ba0`; `dist\UE5Dumper.dll` `2df6a1c68baf`.
  C# 6286/6286 (+1 env-gated skip), headless 19/19, dll_core 702 checks, 32 gates. Build 3631 was spent by a failed
  AOT publish and not reused; 3632 was the build before the review's fixes.

## 2026-10-06 (no build change) — Live Funcs call trace: a ring buffer, record-time filters, no register capture `[LIVEFUNCS-TIMELINE-2026-10-04]`

- A design review of [live-funcs-timeline-plan.md](live-funcs-timeline-plan.md) and the maintainer's decisions.
  Nothing is built or measured yet.
- The trace buffer becomes a **ring** of 64 or 128 MB that keeps the calls just before Stop, instead of filling up
  and stopping: Tick and the other per-frame calls could fill it before the action you are recording. Its size is
  a cap you pick, never derived from a time target.
- Two opt-in filters at record time: record only inside the ticked functions' calls, or leave out the functions
  the previous recording found firing every frame.
- No register capture: at the ProcessEvent hook only the object, the function and the parameter block mean
  anything. The parameters are decoded by name instead, and registers inside native code stay Cheat Engine's job.
- The trace options and the Call Trace tab show only with the experimental tabs on.
- What the review requires of the build: freeing the buffer safely while calls are in flight, a separate return
  record per call, a binary transfer after Stop, a separate buffer for snapshots, and a cap on native stack
  captures.

## 2026-10-06 (build 3630) — Live Funcs can leave out the functions that fire every frame `[LIVEFUNCS-HIDE-PERFRAME]`

- **Live Funcs ▸ Hide per-frame** (off by default, remembered): the DLL leaves out the functions that fire every
  frame through the recording — Tick, animation and camera updates — **before** the Fetch limit, so the limit's
  rows go to the rare functions you are looking for instead of cutting them. The status line says how many were
  left out.
- Per-frame means a gap of 40 ms or less, kept up for at least half the time the game was recorded; your action's
  short bursts stay, even repeated, and an action you hold for most of the recording counts as per-frame. Below
  25 fps nothing is per-frame.
- With a baseline, record it the same way: a function left out of the baseline is not shown as NEW, and a baseline
  recorded the other way is flagged. Save .jsonl records the option.
- Needs this build's UE5Dumper.dll; an older one leaves nothing out, and the status line says so.
- Build 3629 was the first live check; 3630 adds the review's fixes (the kept-up time instead of the first-to-last
  span, the window over the recorded activity, the baseline's left-out functions).
- Build 3630: AOT `dist\UE5DumpUI.exe` 59,966,976 B, sha256 `180cd772c234`; `dist\UE5Dumper.dll` `cf7a2cc4d970`.
  C# 6223/6223, headless 19/19, dll_core 634 checks, 32 gates.

## 2026-10-06 (build 3628) — Dump Explorer compares two dumps and writes the diff as an HTML report `[DUMPDIFF-UI]`

- **Dump Explorer ▸ Compare…**: compare the loaded Dump All file with another dump of the same game and get an
  **HTML report** of what a patch changed — classes, structs and enums added or removed, fields that moved or changed
  type, function signatures, enum values — each under its owner. The dump taken earlier is the old one. The ▾ holds
  **Include engine types** and **Breaking changes only**, both remembered.
- The report says when the two dumps come from different games, and what it could not compare and why (a dump from
  before struct / enum lines, a cut-off file, unreadable lines).
- It is `scripts/analysis/diff_dumps.py`, which release builds do not include, ported to C#; the two are held to
  the same results by shared test cases. The script now skips lines it cannot read the way the UI does.
- From fireundubh's PR 539 (its diff of structs and enums), built our way.
- Build 3627 was the first live check; 3628 adds the review's fixes and a clearer status line ("not compared"
  instead of 0).
- Build 3628: AOT `dist\UE5DumpUI.exe` 59,950,080 B, sha256 `d2a50aec0fff`; `dist\UE5Dumper.dll` `d4a3588ccb5f`
  (no DLL change). C# 6211/6211, headless 19/19, 32 gates.

## 2026-10-06 (build 3626) — Dump Explorer reads structs, enums and parameters; the diff compares them; an optional object index `[EXTPR-539-540-2026-10-02]`

- **Dump Explorer** shows structs, enums and each enum's values beside the classes, and a function's arguments
  and return type; a parameter name now finds its functions. The category picker reaches every kind, and a
  struct or enum matches the live game by kind, so it no longer borrows a class's address of the same name.
- **`diff_dumps.py`** compares structs, enums and function parameters between two dumps and names each change.
  It skips what a file cannot tell it (a dump from before these lines, an enum list that could not be read) and
  says so, instead of reporting everything as added.
- **Object index (optional):** tick Export ▸ "Dump All also writes the object index" and Dump All also writes
  `<name>.objects.jsonl`, every loaded object with its GObjects index. It shows the estimated size and time and
  asks first; "Class dump only" skips it.
- **Invoke, the SDK header and Find Func** (build 3624) no longer take a Blueprint function's local variables
  for parameters; review fixes here keep the CE Invoke script clearing the whole parameter buffer and make the
  DLL clear an invoke's return slot, which it had never done.
- From fireundubh's PR 539, built our way.
- Measured on build 3625: DumperTest 5.4 Shipping, a 15.6 MB dump in 5.6 s (3,868 classes, 3,820 structs, 1,568
  enums); Avowed's main menu, 39.9 MB in 13.4 s (7,404 classes, 5,562 structs, 2,142 enums), and its object index
  19.5 MB in 1.7 s.
- Build 3626: AOT `dist\UE5DumpUI.exe` 59,476,480 B, sha256 `a78a0ba980fb`; `dist\UE5Dumper.dll` `1e2f3c3fbcb9`
  (`get_object_list`'s `include_index`, Mimic's return-slot clear). C# 6153/6153, headless 17/17, dll_core 608
  checks, 32 gates. Build 3625 was the live-check build; 3626 adds two fixes it found (the Explorer's Kind column,
  a stale header over an object index).

## 2026-10-06 (build 3624) — Invoke, the SDK header and Find Func stop taking a Blueprint function's locals for parameters `[FUNCPARM-CONSUMERS]`

- A Blueprint function keeps its local variables in the same list as its parameters in the game's data, and since
  build 3622 only Dump All told them apart. Now the rest do too: the **Invoke** dialog (Live Walker, Interesting
  Functions, Console) and the CE Invoke script offer only the function's arguments as inputs, and the dialog's
  post-call readout lists only its parameters. The **SDK header** signature lists only the arguments.
  **Find Func** (functions taking a class as a parameter) no longer lists a Blueprint function that only casts to
  the class.
- A Blueprint function whose parameters fit the mailbox is no longer refused because one of its locals lies past it.
- With a DLL older than build 3622, the parameters are taken from the function's parameter count, as Dump All does.
- Build 3624: AOT `dist\UE5DumpUI.exe` 59,264,512 B, sha256 `70fc4054301e`; `dist\UE5Dumper.dll` `5527995e0ba9`
  (Find Func's matcher). C# 6126/6126, headless 15/15, dll_core 603 checks, 30 gates run (2 skipped in this
  worktree: no vendored RE-UE4SS templates, no CE Lua host). Build 3623 was this worktree's configure build and
  was never handed over. Not yet checked on a game: `verification-register.md` `[FUNCPARM-CONSUMERS]`.

## 2026-10-06 (build 3622) — Dump All writes function parameters `[EXTPR-539-540-2026-10-02]`

- **Dump All** now writes each function's parameters on the class lines: name, type, offset, size, and
  whether it is an out parameter or the return value.
- A Blueprint function keeps its local variables in the same list as its parameters in the game's data. The DLL
  now tells the two apart, and only real parameters are written: on Avowed that left out 15,820 locals in
  1,126 Blueprint functions. The Invoke form and the SDK header still show such locals as parameters; that is
  a separate open item.
- With a DLL older than this build (for example an old proxy left in a game folder), the parameters are taken
  from the function's parameter count instead, and the file's summary line counts those functions.
- Dump Explorer and the analysis scripts do not read the parameters yet.
- From fireundubh's PR 539, built our way.
- Build 3622: AOT `dist\UE5DumpUI.exe` 59,266,048 B, sha256 `349cba66cb93`; `dist\UE5Dumper.dll` `c67e6ad2a6d4`
  (`walk_functions` sends `parm`). C# 6109/6109, headless 15/15, dll_core 602 checks, 32 gates. Live,
  `tools/verify/d3_parm_flags.py`: PASS on Avowed (UE 5.3) and UE423_Flying Shipping (UE 4.23, the UProperty
  path); DumperTest 5.4 Shipping agreed on all 8,001 functions with parameters but has only one function with
  locals. Dump All itself is not yet measured on a game.

## 2026-10-06 (build 3621) — Dump All writes enums `[EXTPR-539-540-2026-10-02]`

- **Dump All** now writes a line for every enum with its members and values, after the classes and structs, and
  its summary and completion message count the enums. "Game classes only" leaves the engine's enums out too.
- When the enums could not be read in full, the completion message says so: the list failed, it was cut short,
  or the members' names cannot be located on this game, which leaves every enum empty. The file's summary line
  records the same, so a later comparison can tell an empty enum from an unreadable one.
- The Dump Explorer and the analysis scripts do not read enum lines yet.
- From fireundubh's PR 539, built our way.
- Build 3621: AOT `dist\UE5DumpUI.exe` 59,252,736 B, sha256 `2eca0528045c`; `dist\UE5Dumper.dll` `254ba0c2e250`
  (no DLL source change; the build stamp moved). C# 6099/6099, headless 15/15, dll_core 593 checks, 32 gates.
  Not yet checked on a game, as for build 3620.

## 2026-10-06 (build 3620) — Dump All writes structs `[EXTPR-539-540-2026-10-02]`

- **Dump All** now writes a line for every struct (native and Blueprint user-defined) with its properties, beside
  the class lines, and its summary and completion message count the structs. A struct the DLL could not read is
  an error line instead of an empty struct.
- The Dump Explorer and the analysis scripts read class lines only, for now.
- From fireundubh's PR 539, built our way.
- Build 3620: AOT `dist\UE5DumpUI.exe` 59,237,376 B, sha256 `1488b5154285`; `dist\UE5Dumper.dll` `86bfcda310ce`.
  C# 6085/6085, headless 15/15, dll_core 593 checks, 32 gates. Not yet checked on a game: the plan measures Dump
  All's time and size on a game once structs, enums and parameters are all in.

## 2026-10-06 (no build change) — Live Funcs' Fetch limit, Save .jsonl and Min calls checked live on Avowed `[EXTPR-539-540-2026-10-02]`

- Build 3619 on Avowed, through the UI: a cut page says so and suggests a higher Fetch limit; at 32768 the same
  recording showed 722 of 906 functions with no such suggestion (the rest cannot be read). Min calls 8 showed and
  saved 220 of 475 rows, all with 8 calls or more, and moving the slider changed neither table. A peek left on
  screen after leaving the tab was saved with `recording_at_fetch` true. The controls are disabled while recording,
  and a file name typed without an extension is saved as `.jsonl`.
- PR 540's three features are done.

## 2026-10-06 (build 3619) — Live Funcs: a Min calls slider; file dialogs keep their extension `[EXTPR-539-540-2026-10-02]`

- Live Funcs has a **Min calls** slider (1 to 32, default 1, which hides nothing) beside Fetch limit. It hides
  functions called fewer times than that. It applies to the next recording: the value is read at Start, and moving
  it later does not change the table on screen. Set Baseline still uses every fetched row, and Save .jsonl records
  the value. Disabled while recording; remembered across restarts.
- With Min calls set, the status line no longer suggests a higher Fetch limit when every row it would add is below
  the minimum, and no longer says the action's function is surely among the NEW rows when the slider hid one.
- Teleport's CSV and Lua export / import dialogs add the extension when you type a name without one (the fix in
  the "(no build change)" entry below ships in this build).
- From fireundubh's PR 540, built our way.
- Build 3619: AOT `dist\UE5DumpUI.exe` 59,228,160 B, sha256 `23dc3b7d3db5`; `dist\UE5Dumper.dll` `198c35caf8d5`.
  C# 6077/6077, headless 15/15, dll_core 593 checks, 32 gates. Checked in the AOT UI without a game (the slider
  reaches 32, is saved, and was set back to 1); not yet checked live.

## 2026-10-06 (no build change) — the file dialogs' file type always has its dot `[PICKER-EXT-DOT-2026-10-06]`

- Teleport's coordinate library passed `csv` / `lua` to the file dialogs, which build their filter as `*` plus
  the extension, so they offered `*csv` / `*lua`. A name typed without an extension in Export CSV, Sample CSV or
  Save .lua was saved with no extension, and Import CSV… / Import .lua… also listed files whose name merely ends
  in "csv" / "lua".
- Measured on Windows 11 with a probe that makes Avalonia 12.1.3's `IFileDialog` calls in its order: with `*csv`
  a typed "test" is saved as `test`, with `*.csv` as `test.csv`; the open dialog lists 2 of a.csv, b.txt, dcsv,
  e.lua with `*csv` and 1 with `*.csv`.
- `WindowsPlatformService.FilePickerPattern` adds a missing dot, for every caller (`ba16c035`, test `92bb2ad0`).
  The Teleport dialogs name their type "CSV (*.csv)" / "Lua script (*.lua)" from en.axaml (`6c419495`).
- Not in a build yet: it needs an AOT publish (`build.ps1 -Mode Publish`). C# 6045 passed, 0 failed, 15 skipped;
  headless 15/15; 30 gates. Not yet checked in the app.

-----

## 2026-10-06 (build 3618) — Live Funcs: Save .jsonl `[EXTPR-539-540-2026-10-02]`

- Live Funcs has a **Save .jsonl** button. It saves the rows on screen to a JSON Lines file: a summary line, then
  one line per function in the order the game first called it, which a table ranked by count loses.
- The summary records what decided the rows: the filter, the check boxes, the fetch limit, and in diff mode
  whether the baseline was partial (NEW is then not reliable). It also says when the rows came from a Refresh
  made during the recording.
- Disabled while recording.
- Fixed on the way: pressing Set Baseline a second time with Diff already on kept every row's Δ and NEW against
  the old baseline.
- From fireundubh's PR 540, built our way.
- Build 3618: AOT `dist\UE5DumpUI.exe` 59,215,360 B, sha256 `a0b29b4c3c0c`; `dist\UE5Dumper.dll` `b6d055c828b5`.
  C# 6054/6054, headless 15/15, dll_core 593 checks, 32 gates. Checked in the AOT UI without a game (the button,
  and "Nothing to save" on an empty table); not yet checked live.

## 2026-10-06 (build 3617) — Live Funcs: a Fetch limit slider `[EXTPR-539-540-2026-10-02]`

- Live Funcs has a **Fetch limit** slider (64 to 32768, default 512) in place of the fixed 300. The table ranks
  functions by call count and the limit cuts the lowest counts first, which is where the function you are
  looking for usually is. Measured on Avowed: an active 75-second recording had 543 functions, and 300 came back.
- The slider is disabled while recording; a recording uses the value it started with. To see more after Stop,
  raise it and press Refresh. The value is remembered across restarts.
- When the limit cut the table, the status line now says a higher Fetch limit shows more; it no longer says that
  when the rows were missing for another reason, or when the slider is already at its maximum.
- The baseline's status has a line of its own and wraps, instead of running off the right edge.
- From fireundubh's PR 540, re-implemented.
- Build 3617: AOT `dist\UE5DumpUI.exe` 59,189,760 B, sha256 `cf10dbc1656e`; `dist\UE5Dumper.dll` `89d3f799c088`.
  C# 6042/6042, headless 15/15, dll_core 593 checks, 32 gates. Not yet checked live.

## 2026-10-06 (no build change) — `analyze_dumps.py` counts the game's own C++ classes too `[EXTPR-539-540-2026-10-02]`

- `scripts/analysis/analyze_dumps.py`, which suggests keywords for the Interesting Properties / Funcs tables from
  several games' dumps, had the same "`/Script/` is engine" test as `diff_dumps.py`. Its game-only statistics now
  include the game's native classes, where many games keep stats like Health and Mana (`74dc127e`). Reports made
  before this are not comparable with new ones.
- Both scripts take the engine-module list from the new `scripts/analysis/engine_paths.py`; the gate compares
  that file with the DLL's and Dump All's lists. `analyze_dumps.py` has a `--self-test` now, run by the gates.

## 2026-10-06 (no build change) — `diff_dumps.py` reports the game's own C++ classes; two new gates `[EXTPR-539-540-2026-10-02]`

- `scripts/analysis/diff_dumps.py` treated every `/Script/` path as engine, so a default patch diff left out the
  game's own native classes. It now skips only the engine's modules, from the same list the DLL and Dump All use;
  `--include-engine` adds them (`6ade4435`, with fireundubh as co-author; the test is `80e7bfbf`).
- New gates (32 now): `check_engine_prefixes` keeps the three copies of that list equal, and
  `check_analysis_selftests` runs the self-tests of the scripts in `scripts/analysis/`, which nothing ran before.
- `analyze_dumps.py` has the same old test; left for the maintainer to decide.

## 2026-10-06 (no build change) — Live Funcs: fetching 32768 rows measured on a game; R1–R4 decided `[EXTPR-539-540-2026-10-02]`

- On Avowed, build 3616: 75 s of walking and opening menus recorded 648 distinct functions; a fetch at the
  planned maximum of 32768 returned 543 rows, 144 KB, in 10–15 ms. Standing still for 60 s recorded only 67.
  The fetch stays on the interactive lane. Today's fixed cap of 300 did cut the active recording.
- New rig `tools/verify/livefuncs_fetch_measure.py`; `pipe_client` now keeps each reply's size on the wire.
- The maintainer decided the re-check's four questions (R1–R4); the plan has them. Next: `diff_dumps.py`'s engine
  test (R4).

## 2026-10-06 (no build change) — the PR 539 / 540 plan re-checked; build 3616's early changes verified on Windows `[EXTPR-539-540-2026-10-02]`

- Every claim in `docs/ext-pr-539-540-plan.md` was re-checked against the two PRs, `dev` and the two reference
  dumpers: 77 of 100 held, 21 needed a qualifier, 2 could not be checked, none was wrong outright. The plan's new
  "Re-check 2026-10-06" section has the corrections, what the first review missed, and four questions for the
  maintainer (R1–R4) before the work starts.
- Build 3616's early changes, on Windows: C# 6025/6025, headless 15/15 at `bb651f08`. `dist\UE5DumpUI.exe`
  59,165,184 B, sha256 `83e8b518119c`.
- Correction to the 3616 entry below: the status line updates **at most** twice a second, after each class is
  written; one slow class walk still leaves it quiet.
- D7's tests now cross the 200-class chunk boundary (`8358bdd9`). A chunk-local `Done` passed every earlier test.
- Comments in `DumpAllService` that no longer matched the code (`6897e913`), and the "50–500 MB" figure left in
  `scripts/analysis/README.md` (`76556244`). Neither changes the program.

## 2026-10-04 (no build change) — feasibility of a Live Funcs call timeline and stack snapshots `[LIVEFUNCS-TIMELINE-2026-10-04]`

Written down, not built: [live-funcs-timeline-plan.md](live-funcs-timeline-plan.md). Live Funcs could keep one
record per `ProcessEvent` call in a buffer of fixed size that stops when full, with the nesting depth that turns it
into a call tree; then, for functions the user ticks, a copy of the parameters and of the native stack. The stack
would get a few views of its own (frames named by the UFunction they fall in, an annotated stack copy, the decoded
parameters) and hand disassembly to Cheat Engine. Nothing is measured yet. This is our own feature, not part of
PRs 539 / 540, so its commits carry no co-author trailer.

## 2026-10-04 (no build change) — Live Funcs fetch limit: the planned max is now 32768 `[EXTPR-539-540-2026-10-02]`

The plan for PR 540 had the fetch-limit slider top out at 8192. It now tops out at 32768 (2^15). On the PR, the
contributor replied that they feed Live Funcs output to an AI assistant to write UE4SS mods and wanted a fuller
picture of what fired; they agreed 50,000 is too much. The default stays 512, because the table is mainly for
tracing what one in-game action calls. The DLL needs no change. The plan now asks for the reply size and time to
be measured at 32768 before deciding whether the command stays on the interactive pipe lane. Nothing is built yet:
[ext-pr-539-540-plan.md](ext-pr-539-540-plan.md), row L1.

## 2026-10-02 (build 3616) — Dump All: progress twice a second; no time or size in its tooltip `[EXTPR-539-540-2026-10-02]`

- While Dump All walks the classes, the status line now updates every half second. It used to update once every
  50 classes, which could leave it still for a long time on slow walks. The first class shows at once, and the
  "Counting instances" step still updates once per page of objects.
- The Dump All tooltip no longer promises "~30-60s per game" or "50-500 MB". It says that time and file size
  vary with the game and the export settings.
- The first change is item D7 of the plan for external PR 539
  ([ext-pr-539-540-plan.md](ext-pr-539-540-plan.md)), done before the rest of it. Its commit (`14ecb189`)
  credits fireundubh as co-author. The commits are `42217904` (tooltip), `cc254fd1` (a matching code comment)
  and `14ecb189` (progress).
- Build 3616, published AOT by the maintainer with `build.cmd publish` on 2026-10-02 and reported OK. That run
  includes the C# tests. The exe size, SHA and test count were not recorded in this entry. Before the publish, the
  C# suite also ran on Linux (.NET SDK 10.0.112, RID linux-x64): 5896 passed, the same 112 Windows-only failures
  as the unchanged tree, 17 skipped.

## 2026-10-02 (no build change) — the object index from PR 539 will be built after all, as an opt-in `[EXTPR-539-540-2026-10-02]`

The entry below says the object index is not kept. That changed the same day. Dumper-7 writes the same kind of
list (`GObjects-Dump.txt`) on every SDK generation, and RE-UE4SS writes one (`UE4SS_ObjectDump.txt`) on a
keybind. Users of either tool will expect it, and our UI has no way to save the whole object list today. It will
be a checkbox, off by default. When ticked, the export first estimates the object count, file size and time, and
asks before writing anything. Plan: [ext-pr-539-540-plan.md](ext-pr-539-540-plan.md), section D4.

## 2026-10-02 (no build change) — external PRs 539 / 540 reviewed; what we take is planned, not built `[EXTPR-539-540-2026-10-02]`

Two pull requests from fireundubh were read for the first time: PR 540 (Live Funcs: fetch limit, min calls,
save `.jsonl`) and PR 539 (Dump All: structs, enums, function params, an object index). Neither is merged. We
re-implement the parts we keep, with the contributor as co-author on each commit.

- **Kept from 540**, rebuilt as sliders over powers of two: the fetch limit (default 512, max 8192, replacing the
  fixed 300), Save `.jsonl`, and Min calls (default 1, max 32, applies to the next capture only). All three are
  disabled while recording and remembered across restarts.
- **Kept from 539:** struct lines, enum lines and function parameters in Dump All. **Not kept:** the object index.
  Its addresses mean nothing once the game closes, and it adds a line for every live object.
- **The review found** that PR 540 fails the `check_vm_status_literals` gate, that 50,000 as a fetch ceiling means
  "fetch everything" on the interactive pipe lane, and that PR 539's progress reports fire once per struct line
  and its Dump Explorer index can send a class jump to a struct of the same name.

The plan and every finding are in [ext-pr-539-540-plan.md](ext-pr-539-540-plan.md); the open rows are in
[todo.md](todo.md).

## 2026-10-02 (no build change) — README: the built-in pointer scan leads the highlights

`README.md` and `README_zh-TW.md` now open their highlights with what most UE dumpers lack: GObjects, GNames
and GWorld are found automatically on most UE games. The scanner ships its own AOB signatures and symbol-export
lookups for GObjects, GNames, GWorld, GEngine and SparseDelegates, checked against UE 4.11–5.8 binaries. When no
pattern matches it falls back: a data-section scan for GObjects, string-reference and pointer scans for GNames,
and for GWorld a search for a UWorld instance, then `GEngine → GameViewport → World`.

-----

## 2026-10-01 (no build change) — corrections to the entries for builds 3599–3614

The entries below stay as written (this log is append-only). Reading the v3598…3615 code against them found
nothing they describe that the program does not do, but several say less than it does, or say it loosely.
What they should have said:

- **HEX reaches further than the 3599 entry lists.** Instance Finder has HEX on each instance row and each
  container-lookup row, as well as HEX and +CE on its fields. Value Search has both buttons on its group slots
  too; a slot with no address of its own shows neither.
- **Object Tree has no HEX button.** It is a right-click item, "Show in CE hex view (AOBMaker)". In the
  function-properties dialog the button reads "Disassemble in CE", not ASM.
- **ASM on a function also adds a record** named `<function> (code)` to the Cheat Engine table. A
  Blueprint-only function has no native code and the status line says so.
- **System tab:** the &GEngine scan-hit row also got a Copy button beside ASM.
- **Instance Finder's AA (3599)** arrives unticked. If Cheat Engine does not take it, it is copied to the
  clipboard instead, and the status line says which happened; a plain copy used to say nothing.
- **SYM for GObjects / GNames (3599, 3614):** the AOBMaker app must run at the same elevation as UE5DumpUI, or
  it refuses the request. SYM still registers nothing when the result would not land on the address the DLL
  found, and then says "Not pushed" with the reason.
- **"CE is not on this game" (3600–3602)** is not checked on a timer; only the UI dot is. It is re-checked on
  connect, by the refresh button beside the chip, when the plugin comes back and after a refused +CE. So
  after you open the right game in Cheat Engine it stays until one of those.
- **Builds 3609–3611 and 3614 need their own DLL in the game.** SYM from a game's exports, Live Walker's "AOB"
  option on such games and SYM through an adjusted signature read five new fields of the DLL's reply. A proxy
  DLL left from an older build does not send them, and those features then behave as before; the existing
  "DLL outdated" warning is the only hint. Redeploy the proxy.
- **Auto Structure Dissect (3599, 3604):**
  - Tools → Add Auto Structure Dissect needs the AOBMaker Cheat Engine plugin, not the AOBMaker app, and the
    record arrives unticked. Once the script and the record are in the table they need neither.
  - The record is followed by a 2-second watch, so after Cheat Engine unticks or deletes it, ours can stay on
    for about two seconds more.
  - "Turning Cheat Engine's back on yourself while ours is ticked is respected" means only that the script
    leaves it on. Both dissectors are then registered, and what Define new structure gives in that state was
    not measured. The one measurement with both on (build 3602, Cheat Engine's registered first) got Cheat
    Engine's answer and nothing of ours.
  - A Cheat Engine with no Unreal Engine extension (7.6 and older) has nothing to suspend and nothing is
    touched. That path has a unit test; it was not checked on a 7.6 install.
- **Push to CE Structure Dissect (3613)** needs the AOBMaker Cheat Engine plugin. The record it adds is named
  "UE5CEDumper: Structure `<name>`".
- **Copy CE XML (3607):** two more texts changed with the limit. The Fabricate readout above 256 reads "CE may
  lag", and Instance Finder's truncation warning names the 256 M-character guard.

## 2026-10-01 (no build change) — the bundled Quick Start no longer ties Structure Dissect to Cheat Engine injection

- `README.html` in the release zip (its source is `scripts/DEPLOY_README.html`) said Structure Dissect and
  memory editing in Cheat Engine were for the Cheat Engine injection method only. They work with a proxy DLL
  and with "Inject into running game" too, once Cheat Engine has the game open. The comparison table, the
  Option A notes and the script section now say so.
- New section "Structure Dissect in Cheat Engine" for what was added after v3598: Tools → Add Auto Structure
  Dissect to Current CE Table, Live Walker → Export CSX → Push to CE Structure Dissect, and that auto dissect
  stands in for Cheat Engine 7.7's own Unreal Engine dissector while it is on and puts it back when turned off.
- Three troubleshooting rows now name Option C beside Option A: it also scans when it injects.
- `Feature-Guide.html` is unchanged. It carries no such statement; it is stamped build 2375 and needs its own
  pass (noted in the commit message).

## 2026-10-01 (no build change) — a gate for corrupted text files, and a `ship` skill

- New gate `check_text_integrity`: no tracked text file may contain a control character. Patch scripts
  run through a shell heredoc have written such characters in place of a backslash sequence, and
  nothing noticed. Its first run found one that had sat in a test file since 2026-07-30; that is fixed.
- `CLAUDE.md` gains four short rules that sessions kept re-learning: write patch scripts as files, no
  new PowerShell, read a command's own exit code, and fetch before stating what is pushed.
- New project skill `ship` (`.claude/skills/ship`): the repo's push, pull request and merge routine in
  one place, so "push and merge" follows the same steps on either PC.
- C# 6021/6021, 30 gates.

## 2026-10-01 (no build change) — folder-only rules moved out of the root `CLAUDE.md`

- `CLAUDE.md` is read at the start of every session. Nine of its rules apply to one folder only, so they
  moved beside that folder and are read when work happens there: seven to the new `ui/CLAUDE.md` (single
  instance, async, platform abstraction, app-data layout, UI strings, keyword search boxes, AOT) and two to
  the new `dll/CLAUDE.md` (UE offsets, Frieren module naming). The wording of each rule is unchanged.
- The root file keeps one "Folder rules" line pointing at both, and lost two blocks of standard commands
  (plain `cmake` / `dotnet`, and `git submodule update`). It went from 21,918 to 18,700 characters.
- Rules that span folders (CE Lua output, the contract version, logging, code comments) stay in the root.

## 2026-10-01 (build 3615) — the build makes the Cheat Engine Lua test host itself

- Some tests run the scripts this program generates on Cheat Engine's own Lua engine. They need a small
  helper program that each PC has to build once. On a PC that never built it those tests were skipped, even
  with Cheat Engine installed, and the only sign was a list of "skipped" lines.
- `build.ps1` now builds that helper before the tests whenever Cheat Engine is installed, and rebuilds it only
  when Cheat Engine or the helper's source changed. Without Cheat Engine it says so in one line and the tests
  skip as before. If Cheat Engine is installed and the helper cannot be built, the build fails.
- The helper no longer needs a copy of Cheat Engine's source code, only the installed program. It is a small
  program of our own now; on every test it gives exactly the output the old one did.
- No change to the dumper or the UI. Build 3615: AOT `dist\UE5DumpUI.exe` 59,163,136 B, sha256
  `eac4f3c9fac8`; `dist\UE5Dumper.dll` `b3b1ae536328`. C# 6021/6021 with 0 skipped, headless 15/15, 29 gates.

## 2026-10-01 (no build change) — the Ghidra projects' backup is on the NAS, `Y:\GHIDRA_Projs`

- All 63 Ghidra projects are copied to the NAS, which is mapped as drive `Y:`. The folder is
  `Y:\GHIDRA_Projs` (63 `.rep` and 63 `.gpr`, listed and counted that day). This is the backup
  "kept elsewhere" that the cleanup entry below mentions.
- If `Y:` does not exist, or `GHIDRA_Projs` is not under it, the NAS is not mounted. It does not
  mean the backup is gone.

## 2026-10-01 (no build change) — the AOB specificity index is built from far more programs

- The index that tells how noisy an AOB pattern can be was built from 11 engine templates. It is now built
  from every UE program on the build machine (126 binaries, all build configs). The file does not record
  which programs, only where the builder looked and how much code it read.
- Measured on programs the index had not seen: patterns that take more hits than the index allows fell
  from 21 to 8 (of 3,450 pattern-and-developer pairs), and wrongly certified "quiet" patterns from 4 to 1.
  The two known wrong entries (`GNAM_UD2`, `GOBJ_AV2`) are now bounded correctly.
- The price: 77 pattern bounds rose and 16 patterns lost their "quiet" verdict, because the index now
  knows more code; none got tighter. The file grew from 10.3 MB to 26.5 MB, and would be 51.4 MB without
  the new column layout.
- The two non-Shipping GNames patterns added on 2026-09-26 were being scored by an index with no
  non-Shipping code in it. They are scored against all build configs now.
- New tool `tools/pe/ngram_corpus_eval.py` re-derives these numbers; the write-up is
  `docs/aob-block-library-eval.md` §8. C# 6021/6021, 29 gates.

## 2026-10-01 (no build change) — the Ghidra projects were cleaned up: five kept, the rest removed from this machine

- `D:\Tools\GHIDRA_Projs` held 63 Ghidra projects, 171 GB. 58 of them (146 GB) were removed from this
  machine. The five UE 5.8 self-built projects (19 GB) stay, because their game binaries are no longer on disk.
- Before the cleanup the maintainer shrank the project files and kept a backup of them elsewhere, so the
  removed projects are not lost: this frees space here, nothing more.
- Nothing in daily use needed them. The pattern regression sweep and the n-gram index are both built from the
  game binaries under `D:\UE_Analyze_data`. Ghidra was only needed to read code when writing a new pattern,
  and every pattern in use is recorded in `Himmel.h`.
- For a new engine (UE6 is the likely next case) the plan is to package a new DumperTest for it and compare
  against `Himmel.h`, not to analyse a shipped game.
- Details and what the Ghidra-based tools will now report: the top note of `docs/corpus-preservation.md`.

## 2026-10-01 (build 3614) — GObjects symbol in Cheat Engine for games like Avowed `[AOBM-GWORLD-GENAOB]`

- The System tab's GObjects / GNames SYM button now works on games whose signature points a little beside the
  real address (Avowed's GObjects is one). It used to refuse there; it now registers the right address in
  Cheat Engine with a script that finds it again every time it is ticked, so it survives a game restart.
- Checked live on Avowed with Cheat Engine 7.7 and AOBMaker.UI: `gobjects_addr` landed exactly on the dumper's
  GObjects, and re-ticking the record found it again.
- Build 3614: AOT `dist\UE5DumpUI.exe` 59,163,136 B, sha256 `1053af5b20cd`; `dist\UE5Dumper.dll` `87dc24552e2f`.
  C# 6021/6021, 29 gates.

## 2026-10-01 (build 3613) — Push a Live Walker structure straight into CE's Structure Dissect `[AOBM-DISSECT-INJECT]`

- Live Walker's Export CSX menu has a new item, "Push to CE Structure Dissect". Instead of saving a .CSX file and
  importing it in Cheat Engine by hand, it builds the same structure (with its nested child structures, bit
  fields and strings) straight in Cheat Engine, through the AOBMaker CE plugin. Pushing again replaces it, and the
  record it adds can be ticked to rebuild it. On Cheat Engine before 7.7 a bit field is shown as its byte.
- Checked live on DumperTest with Cheat Engine 7.7: a 366-structure, 3,959-element structure arrived exactly as
  exported.
- Build 3613: AOT `dist\UE5DumpUI.exe` 59,153,408 B, sha256 `a37a918570cb`; `dist\UE5Dumper.dll` `f52edf4944c8`.
  C# 6012/6012, 29 gates.

## 2026-10-01 (build 3612) — first cut of the above, superseded by 3613 `[AOBM-DISSECT-INJECT]`

- Build 3612 read the CSX with .NET's XDocument, which added 3.3 MB to the program (62,412,800 B). 3613 reads it
  with a small reader of its own and is back to the usual size. 3612 was never handed over.
- Build 3612: AOT `dist\UE5DumpUI.exe` 62,412,800 B, sha256 `111ef389be0e`; `dist\UE5Dumper.dll` `660a6cb27bde`.

## 2026-10-01 (build 3611) — Exports anchor GObjects' SYM and Live Walker's "AOB" option too `[AOBM-EXPORT-SYM-REST]`

- On games where UE5CEDumper finds its pointers through the game's exports (Satisfactory and other modular builds):
  - the System tab's SYM for GObjects now works without the AOBMaker app, from the game's `GUObjectArray` export,
    and its button only shows the pink "UI" mark when it really needs the app;
  - Live Walker's "AOB" export option is no longer greyed out: Copy CE XML and Copy CE Field root the table at
    the exported GWorld, and Copy CE AA Script walks from it, so the tables survive game restarts and patches.
- A symbol made from an export now says so (it is re-resolved by name, not re-scanned).
- Checked live on Satisfactory with Cheat Engine 7.7: the pasted table and the walked symbol land on the same
  objects Live Walker shows.
- Build 3611: AOT `dist\UE5DumpUI.exe` 59,076,608 B, sha256 `1c21858d1755`; `dist\UE5Dumper.dll` `d734e54ec3c5`.
  C# 6001/6001, 29 gates.

## 2026-10-01 (build 3610) — first cut of the above, superseded by 3611 `[AOBM-EXPORT-SYM-REST]`

- Build 3610 rooted an export-anchored Copy CE XML / Copy CE Field at the GWorld slot instead of the UWorld it
  holds, so the table read one level off; found by its live check and fixed in 3611 before any hand-over.
- Build 3610: AOT `dist\UE5DumpUI.exe` 59,076,608 B, sha256 `7ae5645dc4a1`; `dist\UE5Dumper.dll` `56af9ebd46e8`.

## 2026-10-01 (build 3609) — SYM for GWorld and &GEngine on games that export them `[AOBM-EXPORT-GWORLD-AOB]`

- On games where UE5CEDumper finds GWorld and &GEngine through the game's exports (Satisfactory and other modular
  builds), the System tab's SYM buttons for them were disabled, because there is no AOB to scan for. They now
  register `gworld_addr` / `gengine_addr` from the export itself, which Cheat Engine resolves by name every time,
  so the symbol survives game restarts and patches.
- Checked live on Satisfactory with Cheat Engine 7.7: both symbols land exactly on the addresses the DLL found.
- Build 3609: AOT `dist\UE5DumpUI.exe` 59,069,440 B, sha256 `7535a70e9339`; `dist\UE5Dumper.dll` `92071b3a04cd`.
  C# 5993/5993, 29 gates.

## 2026-10-01 (build 3608) — +CE adds bit-field bools and strings as they are `[AOBM-PLUSCE-FIDELITY]`

- +CE (Live Walker, its "+CE Field (flat)" batch, Instance Finder, and the Value Search / Snapshot / SPC rows) now
  adds a bit-field bool as a Binary record at its own bit, and an FString as a real String record (it follows the
  string's pointer and uses the String Len setting). Before, a bool arrived as its whole byte (96 instead of 1)
  and an FString as an 8-byte pointer. This needs the AOBMaker plugin from v20260930 or later for bits; with an
  older plugin the record arrives the old way and the status line says why.
- Enums and FNames are still added as numbers.
- Checked live on DumperTest with Cheat Engine 7.7 and AOBMaker v20260930: two bit-field bools and two Japanese /
  Chinese FStrings came out right.
- Build 3608: AOT `dist\UE5DumpUI.exe` 59,054,592 B, sha256 `0209b0cf739c`; `dist\UE5Dumper.dll` `0acf0977af57`.
  C# 5978/5978, 29 gates.

## 2026-10-01 (build 3607) — Copy CE XML no longer stops at 60,000 entries `[CEXML-CAP-60K]`

- Copy CE XML, Copy CE Field and Instance Finder's Copy CE XML stopped at 60,000 entries, and real tables go past
  that: a big TMap or TArray is enough. Cheat Engine itself has no such limit. The only stop now is a guard against
  a runaway export running the app out of memory, at 256 million characters of XML (800,000 entries or more).
- Checked live on DumperTest: a 98,890-entry export (30 MB) is copied whole, and Cheat Engine 7.7 pasted all of it
  in under four minutes.
- The Fabricate slider no longer warns that a large value can truncate the export.
- Build 3607: AOT `dist\UE5DumpUI.exe` 58,923,008 B, sha256 `d41d5ffcf63e`; `dist\UE5Dumper.dll` `fa379822dd55`.
  C# 5955/5955, 29 gates.

## 2026-09-30 (build 3606) — SYM says whether Cheat Engine actually enabled the symbol `[AOBM-ACTIVATE-RESULT]`

- SYM on GWorld, &GEngine, GObjects and GNames used to say "Registered" whenever the record was created, even when
  Cheat Engine could not enable it (for example when its AOB scan found nothing). With AOBMaker v20260930 or later
  it now says so in red, with Cheat Engine's own reason, and also when the script is on but the symbol is not usable.
- With an older AOBMaker plugin, which does not report this, the message says it cannot tell and asks you to check
  that the record is ticked.
- If the plugin does not answer in time, SYM no longer says it failed: the script may still have been added, so
  check Cheat Engine's address list before pressing SYM again.
- Checked live on DumperTest with Cheat Engine 7.7 and AOBMaker v20260930 (2026-09-30).
- Build 3606: AOT `dist\UE5DumpUI.exe` 58,920,960 B, sha256 `6a2e9d35df4e`; `dist\UE5Dumper.dll` `683008c80e10`.
  C# 5953/5953, headless 15/15, dll_core 593 checks.

## 2026-09-30 (build 3605) — buttons that need the AOBMaker app look different from those that need only its Cheat Engine plugin `[AOBM-UI-FUNC-VISUAL]`

- The AOBMaker app (AOBMaker.UI) has a colour of its own: the toolbar's "UI" label, and the buttons that need the
  app, are pink. Today those are SYM on GObjects and GNames in the System tab; every other AOBMaker button needs only
  the Cheat Engine plugin and looks as before.
- Those buttons carry a small "UI" tag that turns grey with the toolbar's UI dot, so a SYM that will fail because
  the AOBMaker app is closed says so before you press it.
- Docs follow AOBMaker release v20260930 (build 157), which fixes most of what UE5CEDumper asked for; the rows it
  unblocks are ready to be picked up one by one.
- Checked live on DumperTest with Cheat Engine 7.7 and AOBMaker v20260930 (2026-09-30).
- Build 3605: AOT `dist\UE5DumpUI.exe` 58,896,896 B, sha256 `490e3965a076`; `dist\UE5Dumper.dll` `547fb83acf2a`.
  C# 5943/5943, headless 15/15, dll_core 593 checks.

## 2026-09-30 (build 3604) — Auto Structure Dissect works beside Cheat Engine 7.7's own Unreal Engine dissector `[AOBM-DISSECT-UETOOLS]`

- Cheat Engine 7.7 has its own Unreal Engine dissector (Unreal Engine → Use when dissecting structures), and it
  answered Define new structure before ours could. While "UE5CEDumper: Auto Structure Dissect (UObjects)" is
  ticked, ours now stands in for it; untick, and Cheat Engine's is back exactly as it was. Turning Cheat Engine's
  back on yourself while ours is ticked is respected.
- Restarting the game: if Cheat Engine unticks the record for you (answering Yes to disabling its entries), ours
  really turns off too, and Cheat Engine's dissector comes back. Deleting the ticked record does the same.
- If UE5Dumper.dll is gone, the warning now names the record to untick and tick once the DLL is back.
- Checked live on DumperTest with Cheat Engine 7.7 (2026-09-29/30).
- Build 3604: AOT `dist\UE5DumpUI.exe` 58,889,216 B, sha256 `c465a08a8256`; `dist\UE5Dumper.dll` 3,039,744 B,
  `a61abc114a7e`. C# 5941/5941, headless 15/15, dll_core 593 checks, the Lua suites 11/11 (dissect 351 checks).

## 2026-09-29 (build 3603) — AOBMaker: the trainer's Setup is left for you to tick; the System tab and Live Walker say where Cheat Engine went `[AOBM-TRAINER-SETUP-MODAL]` `[AOBM-SYSTAB-ASM-SILENT]` `[AOBM-LIVEWALKER-HEX-SILENT]`

- Teleport → Standalone Trainer → Export to CE: every entry arrives unticked, Setup included (now named "tick this
  first"), and the status says to tick Setup first. A Setup that fails no longer holds up the AOBMaker plugin for
  UE5DumpUI's other buttons while its message is open.
- System tab: ASM and HEX now say where Cheat Engine's view went, or why it did not; if the plugin has gone, the
  buttons switch off.
- Live Walker: the four HEX buttons (field, pointer target, object, Outer) do the same.
- Checked live on DumperTest and Avowed with Cheat Engine and the AOBMaker plugin (2026-09-29), together with the
  AOBMaker items the earlier check left to unit tests, and SYM's refusal of an adjusted GObjects signature on Avowed.
- Build 3603: AOT `dist\UE5DumpUI.exe` 58,872,320 B, sha256 `2222c0cd4261`; `dist\UE5Dumper.dll` 3,039,744 B,
  `61d0f065c75a`. C# 5938/5938, headless 15/15, dll_core 593 checks.

## 2026-09-29 (not built yet — the next build carries it) — AOBMaker where the app only copied: +CE / HEX / ASM on nine more panels, GObjects and GNames symbols, Auto Structure Dissect `[AOBMAKER-EVAL-2026-09-29]`

- Value Search, Snapshot, SPC and Instance Finder rows get **+CE** (a typed record in Cheat Engine's address list) and
  **HEX** (CE's hex view) beside Copy. Snapshot and SPC keep their "this launch only" rule; a snapshot row for an
  array element is refused, because the snapshot kept only its owner's address.
- **HEX** in Object Tree, Class Pivot and Related Objects; **ASM** (disassembler) on the FSparseDelegateStorage and
  &GEngine scan hits, and on native functions in Live Walker, Interesting Functions, Live Funcs and the
  function-properties dialog.
- Instance Finder's **AA** goes straight into CE when the AOBMaker plugin is up, like Live Walker's.
- The toolbar warns **"⚠ CE is not on this game"** when Cheat Engine has another process open, or none.
- **SYM** on GObjects and GNames registers `gobjects_addr` / `gnames_addr`, with an AOB from AOBMaker.UI (the app must
  be open). Nothing is registered unless the AOB lands on the address the DLL found; games whose signature adjusts
  the address are refused for now (AOBMaker request R17).
- **Tools → Add Auto Structure Dissect** puts `ue5_dissect.lua` into the open CE table with a record that turns its
  automatic Structure Dissect on and off.
- Not built on Windows and not checked on a game yet: `verification-register.md` `[AOBMAKER-A1-A9-LIVE]`. Commit
  `90c732e5`; C# 5895 on linux-x64 with the 107 Windows-only failures the tree already had, headless 15/15.
- The AOT and trim analyzers (a Release build with `-p:PublishAot=true`, warnings as errors) report nothing on the
  new code; a reflection-based `JsonSerializer` call dropped in as a control fails that build with IL2026 / IL3050.
  The analyzers do not see Avalonia's own reflection, so the trimmed publish itself is still owed.

## 2026-09-29 (builds 3600–3602) — AOBMaker status for both programs on the toolbar; a busy AOBMaker app is not "not running" `[AOBM-UI-INDICATOR]` `[AOBM-UI-BUSY]`

- The toolbar's AOBMaker chip now reads `AOBMaker DLL ● / UI ●`: one dot for the plugin inside Cheat Engine, one for
  the AOBMaker app. Hover a dot for what it is for, or why it is off. The chip is no wider than before and keeps one
  width. The app's dot checks every 3 seconds without connecting to it, so AOBMaker logs nothing and is never held up.
- SYM for GObjects / GNames: when the AOBMaker app is busy with another request it now says so, instead of "not
  running".
- "CE is not on this game" no longer stays on the toolbar after Cheat Engine closes, and comes back by itself when
  Cheat Engine returns on the wrong process.
- Auto Structure Dissect works when Cheat Engine opened the game before the DLL was injected (it said the DLL was not
  loaded).
- Checked live on DumperTest with Cheat Engine and the AOBMaker app (2026-09-29).
- Build 3601 was spent on a publish that failed (the game had `dist\UE5Dumper.dll` open); nothing shipped under it.
- Build 3602: AOT `dist\UE5DumpUI.exe` 58,887,680 B, sha256 `44fb4abea218`; `dist\UE5Dumper.dll` 3,039,744 B,
  `fb98205e8dc2`. C# 5921/5921, headless 15/15, dll_core 593 checks, dll_helpers 3073.

## 2026-09-29 (build 3599) — AOBMaker: push to Cheat Engine where the app only copied `[AOBMAKER-EVAL-2026-09-29]`

- Instance Finder's AA goes straight into Cheat Engine's address list.
- HEX and +CE on Value Search, Snapshot and SPC rows and on Instance Finder's fields; HEX in Object Tree, Class Pivot
  and Related Objects; ASM on the two new scan-hit rows and on functions (Live Walker, Interesting Functions, Live
  Funcs, the function-properties dialog).
- The toolbar warns when Cheat Engine has another process open, or none.
- SYM on GObjects / GNames registers a Cheat Engine symbol that finds them again after a game restart (needs the
  AOBMaker app).
- Tools → Add Auto Structure Dissect to Current CE Table.
- Build 3599: AOT `dist\UE5DumpUI.exe` 58,864,640 B, sha256 `e8b48efa6f02`; `dist\UE5Dumper.dll` 3,039,744 B,
  `983164989511`. C# 5895/5895, headless 15/15.

## 2026-09-28 (build 3598) — Cheat Engine's struct and class getters no longer answer with an object from outside the property `[FRIEREN-PROBE-OVERRUN]`

- `UE5_GetFieldStructClass` / `UE5_GetFieldPropertyClass` (the exports CE Lua calls): when a property's slot does
  not hold the kind asked for, or holds nothing, they return 0 instead of an unrelated class read from the memory
  after the property. Found on EVERSPACE 2, where seven Blueprint members answered with a component class.
- UE4 games: the dumper now checks the property slot it derived against a real struct instead of assuming it. What
  is dumped does not change.
- Checked on the games: EVERSPACE 2 7,506 / 7,506 properties, DumperTest 5.4 81 / 81, UE 4.23 2,265 / 2,265; the
  UE 4.23 whole-pool walk is identical to build 3597.
- Build 3598: AOT `dist\UE5DumpUI.exe` 58,445,824 B, sha256 `14c0d797fa8b`; `dist\UE5Dumper.dll` 3,039,744 B,
  `2fbdffa8f424`. C# 5800/5800, headless 15/15, dll_core 593 checks, dll_helpers 3073.

## 2026-09-28 (build 3597) — Blueprint structs, classes and enums stay typed when the engine layout was only partly detected `[STRUCTPROBE-ANY-NAME]` `[UPROP-SUBCLASS-SLOT]`

- When the dumper cannot measure the whole engine layout, members typed with a Blueprint struct, Blueprint class
  or Blueprint enum keep their types. Builds 3595–3596 checked those through a part of the layout that is not
  measured in that case, and could drop them there.
- UE4 function parameters take their struct / class type only from a real struct or class, like members do.
- UE 4.11–4.17 when the layout could not be measured: class-valued members and function parameters read the same
  slot as everything else.
- No change on any fixture: every member of every class (UE 4.11, 4.23, 4.27, 5.1, 5.8, DQ XI S) and every
  function parameter (UE 4.23, DQ XI S) compares identical to build 3596.
- Correction to build 3596's note: `TEnumAsByte` columns of DataTable rows never showed their enumerator names
  before — "again" was wrong — and that part is fixed in the code but not checked on a game (no test table has
  such a column).
- Build 3597: AOT `dist\UE5DumpUI.exe` 58,445,824 B, sha256 `581a75c206f6`; `dist\UE5Dumper.dll` 3,039,232 B,
  `e3695d12cd0a`. C# 5800/5800, headless 15/15, dll_core 576 checks, dll_helpers 3073.

## 2026-09-28 (build 3596) — enum types, optional structs and UE4 class-valued members get the same checks `[ENUMSLOT-ANY-NAME]` `[OPTSTRUCT-ANY-NAME]` `[UPROP-SUBCLASS-SLOT]`

- The check build 3595 added for struct and object members now also guards enum members: the enum a member is
  exported with must really be an enum (Blueprint and other enum subclasses included). DataTable rows show the
  enumerator names of their `TEnumAsByte` columns again; they were looked up one pointer off.
- `TOptional` members that hold a struct take that struct's real alignment, so the dumper finds where the optional
  keeps its is-set flag.
- UE 4.11–4.17 titles whose engine version is not recognised: class-valued members keep their base class and
  function parameters their struct / class types (build 3594 fixed the member types themselves).
- No change on any fixture: every member of every class compares identical to build 3595 on UE 4.11, 4.23, 4.27,
  5.1, 5.8 and DQ XI S.
- Build 3596: AOT `dist\UE5DumpUI.exe` 58,445,824 B, sha256 `0d71c98af627`; `dist\UE5Dumper.dll` 3,038,208 B,
  `2618dae26829`. C# 5800/5800, headless 15/15, dll_core 563 checks, dll_helpers 3073.

## 2026-09-28 (build 3595) — struct and object types are taken only from a real struct / class `[STRUCTPROBE-ANY-NAME]` `[STRUCTCACHE-ENUM-UNCHECKED]`

- When the dumper reads which struct a struct member holds, or which class an object member points to, it now
  checks that what it found IS a struct or a class. It used to accept any object with a name, so on a game whose
  layout it had derived slightly off, a member could be typed by an unrelated object's name, and Live Walker could
  settle on that object instead of the struct next to it. The same kind of check now guards the enum a byte member
  of a struct array is shown with.
- No change on any fixture: every struct and object member of every class compares identical to build 3594 on
  UE 4.11, 4.23, 4.27, 5.1, 5.8 and DQ XI S (about 445,000 members).
- Build 3595: AOT `dist\UE5DumpUI.exe` 58,445,824 B, sha256 `e4993cc5ea4e`; `dist\UE5Dumper.dll` 3,038,720 B,
  `1ec9e12edce0`. C# 5800/5800, headless 15/15, dll_core 548 checks, dll_helpers 3073.

## 2026-09-28 (build 3594) — the class list no longer shows the metaclasses' default objects; UE 4.11–4.17 titles with an unrecognised version read their struct and object types right `[LISTCLASSES-METACLASS-CDO]` `[UPROP-SUBCLASS-SLOT]` `[FPROP-FAMILY-ALIGN]` `[FAMILY-EPOCH]`

- **Class list, Property Search, Interesting Functions:** with "Game classes only" unticked, the class list
  showed five engine objects named `Default__Class`, `Default__BlueprintGeneratedClass`, … as classes with no
  properties, and every class count was five too high. They are gone (UE 4.23 fixture: 1,869 → 1,864 classes).
- **UE 4.11–4.17:** when the engine version is not recognised, or the game keeps case-preserving names, the
  struct / object / enum type slot is now read from the property layout the dumper measures instead of the
  version, which was 4 or 8 bytes off there. (Build 3592's note on case-preserving UE4 builds held for 4.18–4.24
  only; 4.11–4.17 are covered now.) The fixtures keep their values: UE 4.23 0x70, UE 4.11 0x78, DQ XI S 0x80.
- **UE 5.3+ builds that keep editor data and case-preserving names:** the same slot is 8 bytes further on than
  build 3591 derived; fixed. No fixture has this shape; UE 4.27 and UE 5.8 checked identical.
- **Safety net:** a class read while the layout is being corrected on another thread is read again afterwards,
  instead of keeping the old answer.
- Build 3594: AOT `dist\UE5DumpUI.exe` 58,445,824 B, sha256 `417e59b9d2c6`; `dist\UE5Dumper.dll` 3,037,696 B,
  `a4d36493e7a8`. C# 5800/5800, headless 15/15, dll_core 538 checks, dll_helpers 3073.

## 2026-09-28 (build 3593) — UE 5.0 / 5.1: `TObjectPtr<UClass>` members are typed, and the `.usmap` no longer carries unreadable types `[UE51-CLASSPTRPROP]`

- UE 5.0 and 5.1 give every `TObjectPtr<UClass-derived>` member a property kind of their own that nothing in the
  dumper recognised: the SDK export wrote them as raw bytes and the USMAP export wrote a type no reader can size
  (the UE 5.1 fixture: 29 members, 30 USMAP slots). They are treated as the class references they are now — 0
  raw members, 0 unreadable slots, and every class-valued member of the fixture's engine classes matches UE 5.1's
  own source (223 of 223).
- Build 3593: AOT `dist\UE5DumpUI.exe` 58,445,824 B, sha256 `5b7c48c53ef6`; `dist\UE5Dumper.dll` 3,037,184 B,
  `a77169fd1c5a`. C# 5800/5800, headless 15/15, dll_core 517 checks, dll_helpers 3050.

## 2026-09-28 (build 3592) — map and set members name their object classes; UE 4.11–4.17 struct arrays show their element type; walk caches follow a late layout correction `[SDK-CONTAINER-OBJCLASS]` `[UPROP-CONTAINER-FLAT-2C]` `[FAMILY-EPOCH]`

- **SDK export:** a TMap key or value and a TSet element that holds objects now says which class — EVERSPACE 2's
  header had 125 map sides, 28 set elements and 69 soft / weak pointers written as `UObject`; now none, and every
  class-valued member of its engine classes matches UE 5.6's own source (304 of 304).
- **UE 4.11–4.17 (and case-preserving UE4 builds):** Live Walker named no element struct of a struct array
  (NEKOPALIVE: 0 of 13); it names all 13 now.
- **Safety net:** if the engine layout the dumper derived is corrected at runtime, every class read before the
  correction is read again instead of keeping the old answer.
- Build 3592: AOT `dist\UE5DumpUI.exe` 58,445,824 B, sha256 `97172b4c7d87`; `dist\UE5Dumper.dll` 3,036,672 B,
  `296053b6c66a`. C# 5800/5800, headless 15/15, dll_core 515 checks, dll_helpers 3050.

## 2026-09-28 (build 3591) — the property type slot is derived right on UE 5.7 builds that keep editor data `[FPROP-FAMILY-ALIGN]`

- A UE 5.7 build that keeps its editor data (Titan Quest II) moves one field of the engine's property layout, and
  the dumper derived the struct / object type slot 4 bytes short there, only correcting itself later at runtime.
  It is now computed from the layout's alignment and is right at init. Other engines keep exactly the value they
  had (checked live on UE 4.27 and UE 5.8 fixtures: identical). The Titan Quest II check itself waits on its
  deployed proxy DLL (build 3553) being updated.
- Build 3591: AOT `dist\UE5DumpUI.exe` 58,443,776 B, sha256 `d0f6c39d4ea6`; `dist\UE5Dumper.dll` 3,031,552 B,
  `46d028df7a51`. C# 5798/5798, headless 15/15, dll_core 499 checks, dll_helpers 3050.

## 2026-09-27 (build 3590) — UE 4.18–4.24 containers name their element types; Dump All drops the metaclasses' default objects `[UPROP-INNER-TYPENAME]` `[DUMPALL-METACLASS-CDO]`

- **UE 4.18–4.24:** a TArray / TMap / TSet member now says what it holds. The UE 4.23 fixture's SDK export went
  from 1,016 `TArray<uint8_t>` and 52 `TMap<uint8_t, uint8_t>` to 28 (byte arrays, and delegate elements the SDK
  writes as bytes on every engine) and 0, and every
  class-valued member of its engine classes now matches UE 4.23's own source (174 of 174). DQ XI S and
  NEKOPALIVE name all their array elements; a UE 4.27 control is unchanged.
- **Dump All:** `Default__Class`, `Default__BlueprintGeneratedClass` and their siblings are no longer written
  as classes (checked live on the UE 4.23 fixture: five in GObjects, none in the `.jsonl`).
- Build 3590: AOT `dist\UE5DumpUI.exe` 58,443,776 B, sha256 `8d112f81707e`; `dist\UE5Dumper.dll` 3,031,552 B,
  `37353b2cf2fc`. C# 5798/5798, headless 15/15, dll_core 499 checks, dll_helpers 3015.

## 2026-09-27 (build 3589) — UE 4.18–4.24 games get struct, object and enum types back in every export `[UPROP-SUBCLASS-SLOT]`

- **What was wrong:** on UE 4.18–4.24 (UProperty engines) the dumper read every struct / object / enum type from
  the wrong slot straight after connecting. Only a Live Walker walk happened to repair it, and every class read
  before that stayed wrong for the session. UE 4.23 fixture, SDK export after a fresh connect: 2,624 of 2,624
  struct members were raw bytes, 1,038 of 1,038 object pointers `UObject*`, and the `.usmap` named none of its
  2,628 struct slots. By the code, the same slot also fed the Class Struct panel, Property / Value Search struct
  fields, snapshots and Dump All on those engines (not measured one by one).
- **Now:** the slot is derived at init from the measured layout. Same fixture, same order: 0 raw struct members,
  0 untyped object pointers, 774 enums typed, 2,628 of 2,628 struct slots named in the `.usmap`. DQ XI S (4.18,
  shifted layout) gets its slot right at init too; NEKOPALIVE (4.11) is unchanged and right; a UE 4.27 control
  is byte-for-byte the same.
- Still open on those engines: container element types (`[UPROP-INNER-TYPENAME]`).
- Build 3589: AOT `dist\UE5DumpUI.exe` 58,443,264 B, sha256 `374b78673b45`; `dist\UE5Dumper.dll` 3,031,552 B,
  `554d58dad936`. C# 5797/5797, headless 15/15, dll_core 494 checks, dll_helpers 3015.

## 2026-09-27 (builds 3586–3588) — the USMAP export carries Blueprint structs and enums; a class-valued SDK member says which class it holds `[USMAP-UDS-MISSING]` `[USMAP-UDE-MISSING]` `[SDK-METACLASS]`

- **USMAP export:** Blueprint user-defined structs and enums are written, and class-default objects
  (`Default__Class`, `Default__Enum`, …) are not. Verified live on EVERSPACE 2, same game state before and after:
  22 of 22 structs and 45 of 45 enums (33 / 55 with a save loaded), 0 undefined references (was 36 struct, 75
  enum). An independent reader, CUE4Parse, reads every one of them through the new file exactly as the game's own
  cooked schema describes it.
- **SDK export:** a class-valued member is declared from the class it holds — `TSubclassOf<class Pawn>
  DefaultPawnClass` (was `UClass*`), `TSoftClassPtr<class PlayerInput>`, `class BlueprintGeneratedClass*` for a
  `TObjectPtr<UBlueprintGeneratedClass>`. Checked against the ENGINE SOURCE, not another dumper: 303 of 304
  class-valued members of EVERSPACE 2's engine classes match UE 5.6 (was 64), 168 of 174 on the UE 4.23 fixture
  (was 3). The one left on 5.6 and the six on 4.23 are container slots, filed.
- **Review round (7 agents)** caught a regression before release: on UE 4.18–4.24 the new rule turned every
  `TSubclassOf` into a plain pointer — fixed red-first in 3588. It also made `list_enums` cheap again on large
  games and hardened the new oracle.
- **New rig:** `tools/verify/sdk_source_oracle.py` checks an SDK header's class-valued members against the engine
  source it came from; its self-test is a gate (ten mutants, all killed).
- **Found and filed:** `[UPROP-SUBCLASS-SLOT]` (HIGH) — on UE 4.18–4.24 `walk_class` reads struct, object and
  enum types from the wrong slot after a fresh connect (UE 4.23 fixture: 2,624 of 2,624 struct members raw
  bytes) — and nine related rows.
- Build 3586's tests failed on a test-order flake (a freed heap block's address reused a cached class answer),
  fixed in the test; 3587 carried the three fixes, 3588 the review's. AOT `dist\UE5DumpUI.exe` 58,443,264 B,
  sha256 `419b93cfa9f3`; `dist\UE5Dumper.dll` `13687eeaa9b3`. C# 5797/5797, headless 15/15, dll_core 483 checks,
  dll_helpers 2992.

## 2026-09-27 (builds 3583–3585) — the SDK export defines Blueprint user-defined structs, and Live Walker's Export .h declares the declared types `[SDK-UDS-MISSING]` `[SDK-LIVE-VALUE-TYPES]`

- **Whole-pool SDK export:** Blueprint `UserDefinedStruct`s are now defined, so a Blueprint member of such a type
  names a struct the header contains. Verified live on EVERSPACE 2 at the main menu, same game state before and
  after: 22 of 22 user-defined structs defined (was 0), 36 of 36 references resolved (was 0). The metaclasses'
  class-default objects (`Default__ScriptStruct`, `Default__Class`, …) are no longer emitted as empty structs —
  found by that same live check.
- **Live Walker → Export .h:** a member's type is its DECLARED type, not what the instance holds right now — an enum
  was declared as its current value's name (`EDumperTestGrade__Elite Grade;`) and a pointer as the class it happened
  to point at. An object's header now also names its super and leaves inherited members to it (220 → 129 lines on
  DumperTestActor). Verified live on DumperTest 5.4 Shipping, before and after in one session.
- **Every SDK export:** `TSubclassOf<class Class>` (320 of 324 in EVERSPACE 2's export) is now `UClass*` — the
  metaclass is not on the wire yet (`[SDK-METACLASS]`); an enum of unknown type is an integer of its real size.
- Filed: `[USMAP-UDS-MISSING]` (the USMAP export shares the old filter) and `[SDK-METACLASS]`.
- Build 3583 carried both fixes for the live checks; **3584 was consumed by a failed publish** (the running game held
  `dist\UE5Dumper.dll`, so the copy step failed — no artifact); **3585** adds the CDO fix. AOT `dist\UE5DumpUI.exe`
  58,439,680 B, sha256 `66a02654acb9`; `dist\UE5Dumper.dll` sha256 `42c2781e3fb8`. C# 5788/5788, headless 15/15.

## 2026-09-27 (build 3582) — the SDK header's TYPE names: valid, unique across the whole export, and never inheriting from themselves `[SDK-TYPE-NAMES]`

- **Measured first, on a real whole-pool export** (DumperTest 5.4, 7,894 structs): every AnimBlueprint carries its
  own `AnimBlueprintGeneratedConstantData`, so two AnimBPs defined one struct twice, and the child ABP_Quinn's came
  out as `struct AnimBlueprintGeneratedConstantData : public AnimBlueprintGeneratedConstantData`. The same
  measurement showed the whole-pool header is not a compilable unit anyway (2,719 members name a struct defined
  later; 1,152 enum types are never defined). The maintainer chose: fix the names only, UI only.
- **Now:** every type spelling goes through one sanitiser (a `-` in an asset name, a keyword, or a type named like
  the header's own `TArray` / `FName` no longer breaks it). A name held by several types gets the holder's outer
  (`AnimBlueprintGeneratedConstantData_ABP_Quinn_C`), chosen so the result never depends on load order; the super
  follows its address and is never the struct itself; a member's type goes to the nearest holder of the right
  kind. A class the DLL refuses gets an `// ERROR` line instead of an empty struct.
- **An adversarial review** (8 agents) found 19 issues, 12 confirmed: seven code fixes made red-first, three test
  gaps closed, the compile rig's staleness check widened to the name services, and two rows filed —
  `[SDK-UDS-MISSING]` (Blueprint user-defined structs never reach the export) and `[SDK-LIVE-VALUE-TYPES]` (MED,
  pre-existing: Live Walker's SDK export takes a member's type from its current value). 17/17 mutants killed.
- AOT `dist\UE5DumpUI.exe` 55.7 MB (58,436,608 B), sha256 `db57895beea8`; `dist\UE5Dumper.dll` sha256 `f7c9e802821a`.

## 2026-09-27 (build 3581) — the SDK header renames member names C++ would reject; nine docs brought back in line with the code `[SDK-MEMBER-NAMES]`

Both halves came from the other PC's memory-refresh audit of build 3580; every item was checked against the
code or the record before it was changed.

- **SDK header export:** a UE property name went into the header verbatim, so a property called `class` or
  `default`, two properties with one name, a Blueprint variable with a space in it, a member named after a type
  the struct uses, or one named like the generated padding made `cl.exe` reject the whole header (measured with
  `tools/verify/compile_sdk_header.py`: C2236, C2321, C2086, C3646, C2327, C2040, C2059). Such members are now
  renamed the way Dumper-7 does it (the second `Value` becomes `Value_0`), and the comment keeps the real name
  (`[UE name: class]`). `StaticClass` / `StaticName` / `GetDefaultObj` and the `<windows.h>` macros that erase
  a declaration are renamed too. `Name` / `Class` / `Flags` / `Outer` are left alone on purpose. Red first (11
  tests and the compile rig), 5/5 mutants killed. The same problem in TYPE names is filed as `[SDK-TYPE-NAMES]`.
- **Nine repo texts that contradicted the code or the record, one commit each:** Octopath's dxgi proxy works
  since 3366 (test-games, handover); the sparse-delegate key is PDB-confirmed at 4.23–4.26 too (Aura, Ubel,
  Genau, technical-notes); `GWLD_FD_1` sits at priority 102 (GROUND-TRUTH); `build.ps1 -Target DLL` builds every
  proxy, winmm included (help text); Native-C P3 is fully verified (spec header); Locate-in-GWorld's
  `ok_via_level` has fired live on Titan Quest II, only the drill to HP is unchecked (todo); the live-verification
  checker and CI now name `docs/verification-register.md`; Class Pivot's Discover has run live
  (`[AOTSORT-4-2026-08-20]`, todo reconciled); AOBMaker's CreateAAScript level is no longer hardcoded since its
  a5aba68 (comments now say "older deployed plugins").
- AOT `dist\UE5DumpUI.exe` 55.6 MB (58,330,112 B), sha256 `e86c5fa22c92`; `dist\UE5Dumper.dll` sha256 `2a32b94d66a3`.

## 2026-09-27 (builds 3578–3580) — Home / End / page keys with text selected keep the caret where it belongs; UI tests on real controls `[TEXTBOX-HOMEEND-CARET]`

- **Every text field:** with text selected, End could put the caret at the START (and Home at the end) — e.g. a
  click on a keyword box's padding selects its text, End, then typing went in front: `spawn` + ` act` became
  ` actspawn`. PageUp / PageDown did the same, and in the multi-line Lua paste box Home / End after a selection
  made across lines acted on the wrong line. A defect in Avalonia's TextBox, worked around for every text field
  in the app; Shift+Home / Shift+End are unchanged.
- **A second test project drives real Avalonia controls** (`ui/UE5DumpUI.HeadlessTests`, Avalonia.Headless), run
  by `build.ps1` after the main suite. It pins Avalonia's own defect too, so the day an upgrade fixes it the
  workaround can go.
- An adversarial review of the first version (six agents, against Avalonia's decompiled source) found the page
  keys and the cross-line case; both fixed red-first.
- Live check PASSED on 3578 / 3579 (Interesting Funcs keyword box).

## 2026-09-27 (build 3577) — keyword boxes: a selection hidden by a typo comes back `[KEYWORD-BOX-VIEW-KEEP]`

- **The maintainer's call: keep the last non-empty selection.** Before, a keyword that hid every selected row
  (a typo, zero rows) dropped the selection for good, so clearing the typo brought nothing back. Now the hidden
  selection is kept: clearing the keyword brings its first row to the top, and a Backspace that shows the rows
  again selects them again. Picking another row yourself, or a reload that resets the box, lets it go. A
  selection that is only partly hidden keeps the rows still shown.
- Live check PASSED on 3577 (DumperTest 5.4 Shipping): Interesting Funcs with two rows picked (zero-row typo then
  clear, typo then Backspace, a pick of your own in between) and the Object Tree.

## 2026-09-27 (builds 3575, 3576) — keyword boxes: a clear typed faster than the view restores still lands right `[KEYWORD-BOX-VIEW-KEEP]`

- **A race behind 3574's filter-box rules, found while retrying an unexplained live run.** A rebuild detaches the
  selection and its view restore is queued behind keyboard input, so a key that arrives before the restore runs
  captured an empty selection. Measured on 3574: a row picked, keys sent at once, and the clear left the row at
  the bottom edge instead of the top. Now a capture taken while a restore is still queued carries that restore's
  selection (a row picked after the rebuild still wins), and only the newest restore of a burst runs.
- **Every restore decision is logged** at Debug in the `view` log (capture source, queued / dropped / ran,
  rows found, selected afterwards), because the outcome depends on timing and is only visible on a live UI.
- The unexplained run itself was not a bug: clicking the box's right-hand padding selects its text, End then put
  the caret at the start, and the keyword became `actspawn` — no rows, so the pick was filtered out and the clear
  had nothing to bring back, as the rules say. Whether a pick hidden by a typo should come back is open.
- Live check PASSED on 3576 (DumperTest 5.4 Shipping, Interesting Funcs).

## 2026-09-27 (builds 3573, 3574) — every keyword filter box keeps your place `[KEYWORD-BOX-VIEW-KEEP]` `[LW-SEARCH-CLEAR-KEEP]`

- **3573 — Live Walker's field search no longer jumps on the FIRST character either** (the maintainer's second
  follow-up to `[LW-SEARCH-CLEAR-KEEP]`). Every keyword edit re-set the grid's items to repaint the match tint,
  a leftover from when the tint was painted per realized row; the tint has been a style bound to
  `IsSearchMatch` since `[LWREFRESH-2026-08-21]`, so the re-set is simply gone.
- **3574 — the same rules for every other keyword box** (the maintainer asked whether the other boxes had the
  problem; a survey found all of them did, and nothing shared to fix it with). One shared helper now drives
  them: `Helpers/FilterViewKeeper` decides, `Views/FilterViewBinding` does the selecting and scrolling.
  - An edit that shows **the same rows** (a trailing space, one more letter every row already matches) does
    not rebuild the list at all: nothing moves, the selection stays.
  - A real rebuild **keeps the rows that are still selected** (multi-selection too) and keeps the first of
    them visible.
  - A keyword cleared **from 2+ characters to empty** brings the first selected row to the top; with nothing
    selected, the row that was at the top stays at the top. In a list with several boxes (Game Classes,
    Snapshot diff, SPC results) clearing one box while the others stay counts.
  - Boxes: Object Tree, Class/Struct fields, Instances, Game Classes (3 boxes), Console, Live Walker
    functions, Interesting Funcs / Props, Property Search, Live Funcs, Detect Stats, Dump Explorer (both
    groups), Snapshot diff, SPC results, Class Pivot results, Teleport coordinates, and Value Search (server-
    side: it always reloads, and finds your pick again by address).
  - Side fixes on the way: Live Walker's Functions grid no longer jumps to the top on every (Auto) Refresh
    tick — a walk that lists the same UFunctions keeps the rows; Teleport's coordinate filter detaches its
    selection before clearing, inside the edit-sync suppression (an in-progress label edit survives typing in
    the filter); Detect Stats and Live Walker's disconnect detach before clearing.
  - Deliberately changed: Class/Struct's "the detach is unconditional" pin (AE14) — a surviving field is now
    re-selected, which is safe because selecting a field starts nothing.
- Live check PASSED on 3574 (DumperTest 5.4 Shipping): Object Tree, Console, Live Walker functions under Auto
  Refresh, Interesting Funcs with a two-row selection, Teleport coordinates with an edit in progress.

## 2026-09-27 (builds 3571, 3572) — Live Walker: clearing the search keyword no longer throws you back to the first row `[LW-SEARCH-CLEAR-KEEP]`

- **The maintainer's request:** find a field with the search (say at 0x1138), clear the keyword to look for
  something else, and the grid jumped to its first row — scroll all the way back by hand. `ApplySearch` re-sets
  the grid's items on every change (so the highlight styles re-evaluate), which returns the grid to the top and
  drops the selection; a non-empty keyword then scrolls to its first match, an empty one never did.
- **Now**, only when the keyword goes from 2+ characters to empty in one edit (select all, Delete): the view
  stays exactly where it is — whether nothing is selected (3572, the maintainer's follow-up) or the selection
  holds the match ▲/▼ landed on — and a selection that does not hold it brings its first row to the top.
  Typing, pasting, shortening, a partial delete, 1 → 0 characters and a navigation clearing the box behave as
  before. It reuses the exact-position view restore of `[LW-BACK-SCROLL]`.
- Tests: `LiveWalkerSearchClearKeepsViewTests` (3 red first, 6 controls). UI suite 5,656 run, 0 failed. Gates 28
  run, 0 failed.
- **AOT publish 3572:** `UE5DumpUI.exe` 58,225,152 B `450684c436e8`, `UE5Dumper.dll` `63a87743b364` (unchanged
  source); proxies version `299740b87005`, dinput8 `549e780d6586`, dxgi `32862b0cd120`, winmm `e90d42ce4291`.
  (3571 `998d6c537fc8` — superseded by the follow-up.) ✅ Live-checked on DumperTest 5.4 Shipping: all three cases.

## 2026-09-27 (builds 3569, 3570) — Live Walker: retyping the value on screen wrote a truncated number `[LW-EDIT-RETYPE-DROP]`

- **Found while answering the maintainer's edit-vs-Auto-Refresh question**, once typing actually reached the
  editor (a computer-use double-click leaves focus on the grid; `send_key.py --post` after a click INSIDE the
  box does reach it): `I32` showing `1234568`, retype `1234568`, Enter → *"Written: I32 = 123456"*. The editor's
  TwoWay binding re-reads `EditableValue` after each value it writes and skips a keystroke equal to what it last
  read; the getter returned the live value, so a retype's final keystroke was lost. A wrong value in the game.
- **3569** made the getter return the pending text — only when non-empty, which brought its own fault, caught
  in the live check: deleting the last character fell back to the live value and the binding refilled the box
  (`567` → Delete ×3 → `1234567`). **3570** keys it on "the editor has written in this edit" instead, so a cleared
  box stays empty; the editor still opens on the live value (`ResetPendingEdit` at edit begin).
- **The edit-vs-refresh question itself (`[LW-EDIT-UNDER-REFRESH]`)**: an open editor pauses Auto; Refresh while
  typing commits the typed value first, as focus loss does.
- Tests: `LiveWalkerEditPendingTests` (two new, each red first). UI suite 5,647 run, 0 failed. Gates 28 run, 0 failed.
- **AOT publish 3570:** `UE5DumpUI.exe` 58,208,768 B `79830ceb39f7`, `UE5Dumper.dll` `dcec8433c030` (unchanged
  source); proxies version `9c3f591470d2`, dinput8 `3a367186c67a`, dxgi `0c92a2c25178`, winmm `f7a4f53bbcaf`.
  (3569: `UE5DumpUI.exe` `3afd4b2b8559` — superseded, do not hand over.) ✅ Live-checked on DumperTest 5.4
  Shipping: opens on the live value, a cleared box stays empty, a retype writes the exact value, a different
  value writes.
- Also: `send_key.py` sends sequences (keys, `key*N`, `text=`), and the handover lists the non-game grants
  (two ready batches, `nvidia overlay.exe` among them) and the game grants as optional — the user decides.

## 2026-09-27 (build 3568) — Live Walker: Back / Forward / breadcrumbs / bookmarks restore the view you left `[LW-BACK-SCROLL]`

- **The maintainer's report:** after drilling into a row and pressing Back, the row was not on screen — you had
  to scroll down to find it. Reproduced on 3567. The restore replayed the saved TOP row with `ScrollIntoView`,
  which from the top of a rebuilt grid lands a row on the BOTTOM edge, so the view never came back and the
  drilled row sat below it — since `7cc3d5e2` (build 2550), and before that a dead View callback (fixed in 3034)
  restored nothing at all.
- **Fix:** scroll to the end first, so the saved top row is above the viewport and returns as the FIRST row —
  the exact view — then make sure the drilled row is on screen (Back passes its popped crumb, a breadcrumb jump
  the target's child on the old spine). Forward and bookmark loads take the same exact-position restore. A
  bookmark whose rows are gone later falls back to the first selected row still there, or stays at the top.
- **The maintainer's other question — Auto Refresh while a cell is being edited (`[LW-EDIT-UNDER-REFRESH]`,
  recorded, no code change):** measured, the edit pauses Auto (*paused (editing)*, the value held for 15 s), and
  Refresh with the editor open closes it, writes nothing and resumes Auto. The narrower races a code-read
  predicted need a real keyboard to reproduce: on this rig computer-use typing never reaches the editor binding.
- Tests: `LiveWalkerForwardNavTests` pin the row the VM hands the View. UI suite 5,645 run, 0 failed. Gates 28
  run, 0 failed.
- **AOT publish:** `UE5DumpUI.exe` 58,208,768 B `83e699018eda`, `UE5Dumper.dll` `355af731e422` (unchanged
  source); proxies version `8c402112eee6`, dinput8 `a798e4845a2e`, dxgi `97e4b2610f32`, winmm `437663780d7d`.
  ✅ Live-checked on DumperTest 5.4 Shipping: Back, Back into the GWorld list, Forward, a two-level breadcrumb
  jump and a bookmark load each returned the exact view.

## 2026-09-26 (build 3567) — Auto snapshot stopped by hand says so `[AUTOSNAP-STOP-STALE-STATUS]`

- Found by 3566's live check: turning Auto snapshot OFF left its status frozen on *"Auto: next snapshot in 28s ·
  captured 1"* — the loop left on the cancellation without writing it, promising a capture that never came. Both
  cancellation exits (the toggle, a disconnect) now write *"Auto snapshot stopped · captured N"*; the self-stops
  keep their own reasons. A new view-model status string, so it is an en.axaml key (`str.Snapshot.Auto.Stopped`,
  via `Res.Format`), per `[VM-INLINE-STRINGS]`.
- Test: `SnapshotViewModelTests.AutoSnapshot_StoppedByHand_DoesNotKeepPromisingTheNextSnapshot` (red first). UI
  suite 5,643 run, 0 failed. Gates 28 run, 0 failed.
- **AOT publish:** `UE5DumpUI.exe` 58,206,720 B `97085fcb70e6`, `UE5Dumper.dll` 3,016,192 B `0c2a680e4ec3`
  (unchanged source; rebuilt with the build number); proxies version `51cfc279fedf`, dinput8 `46e21220e534`, dxgi
  `06803b971f27`, winmm `71eae26c0cbe`. ✅ Live-checked on DumperTest 5.4 Shipping: OFF mid-countdown shows the
  stopped line, and it stays.

## 2026-09-26 (build 3566) — the Wiki check's last batch, two behaviour fixes, and what an adversarial review found in the first two `[WIKI-TIPS-B3]` `[SNAPSHOT-MANUAL-GATE]` `[FLY-EXPORT-EXPERIMENTAL]`

- **Behaviour:** manual Capture / Estimate stand down while Auto Snapshot runs — `CanManualCapture` existed and
  was raised, but the panel had bound `CanCapture` since Auto Snapshot shipped (`[SNAPSHOT-MANUAL-GATE]`). The
  Fly CE records follow the Experimental switch in Add action records and Save .CT, the maintainer's call
  (`[FLY-EXPORT-EXPERIMENTAL]`).
- **Text that contradicted the code (`[WIKI-TIPS-B3]`):** Teleport's export tips now count 17 teleport + 4
  movement + 2 time (+ 4 Fly); Gravity Direction is UE5.3+ everywhere, as `Laufen.h` says; hotkey hints name
  the section that holds each row; Run BugItGo runs the field; God Mode's refresh names its six badge states; no
  UI string cites `docs/` or carries CJK text; each tab keeps its own noise denylist; Discover is a local
  database query; Detect's filter is ANDed. Also `[LIVEFUNCS-CAP-ADVICE]` (the filter cannot recover rows below
  the fetch cap) and `[TP-CURSORHK-TIP]` (the hotkey fallback reads as a range, not a key sequence).
- **An adversarial review of builds 3565's fixes** (a 12-agent workflow; 9 of 13 findings skeptic-verified, the
  rest checked by hand) found text half-fixed or missed — two more AND filters, seven `pre-5.4` comments, the
  search floor on the whole query, Batch Props, IP's Timing category, the Add status legend
  (`[WIKI-REVIEW-TEXT]`); two tests deriving from a proxy (`[WIKI-REVIEW-TESTS]`); and two gate blind spots
  (`[WIKI-REVIEW-GATES]`): the view gate now reads binding text (three `StringFormat`s allow-listed rather than
  moved, a cost/benefit call), and the status ratchet counts every status assignment carrying a literal —
  558 grandfathered in 22 view models, where the one-line match had seen 388.
- Tests: UI suite 5,642 run, 0 failed. Gates 28 run, 0 failed.
- **AOT publish:** `UE5DumpUI.exe` 58,206,720 B `2e641eb08d76` (the new strings are in it), `UE5Dumper.dll`
  3,016,192 B `ee31f4023b5d` (only a comment changed in `Solide.h`); proxies version `a23a3e00e927`, dinput8
  `9bbe307aa01a`, dxgi `74235e067adb`, winmm `c154df98bf5f`.

## 2026-09-26 (build 3565) — UI text checked against behaviour: 123 view strings move to en.axaml, nine tooltips corrected, a gate for inline strings `[AXAML-INLINE-STRINGS]` `[WIKI-TIPS-B2]`

The Wiki re-translation pass checks every English page against the code and forwards what the UI text
gets wrong; each item was confirmed against the source before it was fixed. Red before green throughout.

- **`[AXAML-INLINE-STRINGS]`:** 123 user-visible attribute values were English literals in 15 views
  (column headers, copy-button labels, range placeholders, Live Walker's tooltips, Class Struct's empty-class
  banner). They now bind 111 new `en.axaml` keys, verbatim. `check_axaml_strings` checked keys only, which
  is how they accumulated; it gains a third direction, INLINE, that fails on any such literal (a glyph-only
  value passes).
- **Tooltips that described behaviour the code does not have:** the Object Tree search suggests a fixed list
  of class names, not recent searches (`[OBJTREE-SEARCH-TIP]`); Live Walker's field search matches name,
  type, value and the class or struct behind a field, with a 2-character floor (`[LW-SEARCH-TIP]`); and
  `[WIKI-TIPS-B2]`: three result filters are space = AND, not substring; Value Search's timeout is 10–90 s,
  default 25 (was quoted as 10–60 / 15); native functions' Props are a disassembly heuristic, not empty;
  Locate in GWorld has no client gate (audit #5 AE10); Clear also turns off BP/Exec only; the category
  filter includes Gameplay and Other. The timeout text and the category list are now derived from the
  slider, the VM default and `KeywordScoringTable` in the tests, so they cannot drift silently again.
- **`[GENCT-ONE-ROW]`:** Generate CT's empty-selection refusal asked for "2+ rows"; one row works.
- **`[UINT8PROP-DEAD]` (DLL + UI):** the Force-value gate accepted `UInt8Property`, a class no engine emits
  (uint8 is `ByteProperty`). Dropped from `Solide::IntWidthOf` and `PropertySearchMatch.CanForceNumeric`;
  a UI test reads the DLL's list out of `Solide.h` / `Solide.cpp` and requires the two to agree. No behaviour
  change.
- **Open, needs the maintainer:** `[VM-INLINE-STRINGS]`. 458 status literals in 22 view models; moving them
  needs a test-time resource loader first.
- Tests: UI suite 5,624 run, 0 failed; `dll_helpers_test` 0 failed. Gates 27 run, 0 failed.
- **AOT publish:** `UE5DumpUI.exe` 58,203,136 B `383e5d8b9f97` (the new strings are in it), `UE5Dumper.dll`
  3,016,192 B `49dba05e4013`; proxies version `3f12016e688c`, dinput8 `b810136d0ff9`, dxgi `eee40f37f75d`,
  winmm `6d2d9aed918e`. ⬜ Live checks owed (the `todo.md` rows carry them): hover the corrected tooltips,
  and open each of the 15 views to read the moved text.

## 2026-09-26 (build 3564) — the Console's CheatManager warning names the real cause: no live instance, not a compiled-out body `[CONSOLE-CHEATMGR-HINT]`

- **3563's wording was wrong, and this build replaces it.** It said the engine's CheatManager execs are
  "usually compiled out" on Shipping — the mechanism `lessons-learned.md` (the UCheatManager entry)
  retracted on 2026-07-29 by measurement: a stock 4.27.2 Shipping EXE keeps the bodies, and the invoke does
  nothing because no CheatManager instance exists, so it lands on the CDO. The banner now reads *"On a
  Shipping build there is usually no live CheatManager, only its default object, so the engine's cheat
  commands report success (Result=0) but have no effect in game. …"*.
- "Strip" left the names with the theory: `ShowCheatManagerHint`, `str.Con.CheatManagerHint`.
  `lessons-learned.md`'s scope line and `roadmap.md` stopped calling the no-effect case a cooker strip.
- Tests: `ConsoleViewModelTests` also pin `compiled out` / `stripped` out of the wording (red on 3563's text,
  then green). UI suite 5,612 run, 0 failed. Gates 27 run, 0 failed.
- **AOT publish** (`-Mode Publish`): `UE5DumpUI.exe` 58,180,608 B `38431c284f85` (carries the new wording;
  the old text and the memory name are absent from it), `UE5Dumper.dll` 3,016,704 B `b5ce88b01e55`; proxies
  version `e6a5ad898696`, dinput8 `835c4aff1448`, dxgi `a1ef7f11f701`, winmm `2965a7bcf735`. ⬜ The live
  check moves to this build: 3563's binary still shows the retracted wording.

## 2026-09-26 (build 3563) — the Console's CheatManager warning moves to en.axaml and stops citing a private note `[CONSOLE-CHEATMGR-HINT]`

- **The Console footer warning** for a selected CheatManager exec was a C# literal ending *"See memory
  feedback_ucheatmanager_stripped."*, a developer's Claude memory note that no user has. Its text is now
  `str.Con.CheatManagerStripHint` in `en.axaml`, bound by `ConsolePanel.axaml` with `StaticResource`, and
  the view model keeps only the decision (`ShowCheatManagerStripHint`). The wording is a Shipping caveat:
  engine CheatManager execs are usually compiled out there (they report success and do nothing), while the
  game's own exec commands usually still work. Detection is unchanged, so it still shows on Development.
- Tests: `ConsoleViewModelTests` pin the key's wording, the panel's bindings, and no `See memory` in the VM.
  UI suite 5,612 run, 0 failed. Gates 25 run, 0 failed.
- Build 3562 was consumed by the `-Target Test` run; **3563 is the published AOT build** (`UE5DumpUI.exe`
  55.5 MB `c8c381e5`, `UE5Dumper.dll` `f4bab95a`). ⬜ Live check owed: select a CheatManager exec on the
  AOT build and read the banner (the `todo.md` row carries it).

## 2026-09-26 (build 3561, tools only — no rebuild) — the local-LLM helper: 23 review findings fixed, the free-VRAM need computed from the model, live-checked

**No binary changed.** Everything here is `tools/llm/ollama_local.py`, its skill and its docs, after the
3561 publish (`ffdf203d` .. `0ba57795`). `dist\` is still the 3561 build.

- **Review of the peer session's and this session's LLM work** (workflow `wf_b18865b3-513`: a runtime lens,
  a cooperative lens, and one skeptic). The skeptic reproduced 23 of 24 findings; all 23 are fixed, red
  `ffdf203d` -> green `ee32aac5`, selftest 152/152.
  - **HIGH:** `CLAUDE_LOCAL_LLM=off` disabled the machine-wide hook for that session, so a game it
    launched booted beside another session's model. The hook now ignores the per-shell switch.
  - **Guard:**
    - `guard` re-reads `/api/ps`, so a refusal unloads;
    - a model resident under a reservation is evicted;
    - the post-exit grace runs from the last game seen (it was 0 s);
    - computer-use actions that can launch now run the hook;
    - no retry past a refusal;
    - the hook's unload fits its deadline;
    - state files are written by write-then-rename.
  - **Cooperative:**
    - the skill always comes from the same checkout as the helper;
    - an older checkout cannot downgrade the install (`--force`);
    - `status` flags a missing interpreter;
    - `join` refuses the source repo, a missing directory, home, and a foreign skill;
    - a CJK repo path resolves correctly.
  - **Privacy:** the selftest no longer carries this machine's model tag or paths.
- **Free VRAM (the maintainer's ask).**
  - **Need:** weights + KV cache from the model's own GGUF metadata (any architecture) + compute
    overhead + a 512 MiB buffer, floored at the machine's `--min-free-vram-mb`.
  - **Checked against the measured table:** at 8k / 32k / 64k the estimate sits the buffer above the
    measurement.
  - **Which GPU:** NVIDIA only, so an integrated GPU is never judged; `CUDA_VISIBLE_DEVICES` is honoured.
  - **This PC:** need 15,317 MiB for the installed model at 32k; its floor lives only in the machine config.
- **Live check** (a fresh session after a Claude Code restart), recorded in the helper header (`0ba57795`):
  - a launch-shaped Bash command reserved the GPU;
  - with the model resident and a reservation written outside the hook, a computer-use
    `open_application` alone unloaded it.

## 2026-09-26 (build 3561) — comment integrity (gates + cleanup), a bool Freeze that could stamp packed siblings, the local LLM installed once per machine; published AOT

**3561 = 3560 plus two DLL changes and one UI fix, up to `80764dcf`.**
- **`[COMMENT-INTEGRITY-2026-09-26]`** (the maintainer's request: comments drift from the code). Items 1 + 2 of
  `comment-integrity-eval.md`, recorded in its §5.
  - **Gate `check_comment_refs`** (check_all + CI, 27 gates): no in-repo `File:NNN` in a comment, no false
    "no test target compiles X", every `.md` / `§` / `[TAG]` must resolve, no stacked C# `<summary>`.
  - **Cleanup:** 151 line references re-anchored on the symbol they meant (git blame -> the target as of that
    commit -> the enclosing function), 52 dead references, 13 stacked docs. All 23 sampled stale blocks fixed
    (workflow `wf_8a72cf58-837`), plus 16 found in passing. Two gate holes closed.
  - **Prevention:** `tools/verify/comment_impact.py` lists the comments elsewhere that name a symbol a diff
    touches; working-lessons §8 is the comment-style rule, and CLAUDE.md points at it.
  - **working-lessons.md:** 28 outdated or incorrect passages fixed (workflow `wf_cf7c8629-2ba`). Its line
    references now point at symbols.
- **`[BOOL-NATIVE-SEARCH]` (DLL + UI, MED)**, found as the eval's stale-comment lead. Search rows never carried
  `bool_native`, so a Freeze from Property Search or Interesting Properties treated an UNRESOLVED packed bool as
  native. The helper then stamped the whole byte every 50 ms over up to 7 siblings.
  - **Fix:** `PropertyMatch.boolNative` and both search encoders emit it. `FreezeScriptGenerator.IsUnresolvedBool`
    refuses it at all three entry points.
  - Red `85c9e8ed` (10) -> green `2093ea91`. The live check is in the verification register.
- **Sein.cpp `s_catMap` (DLL):** "sorted longest-first" was false and three prefix lengths were wrong. A
  `static_assert` now enforces both rules; both negative controls fire.
- **Local LLM helper.** A peer session added cross-session leases, a machine-wide GPU reservation, unload
  PENDING, a hook that cannot block, and settings writes that keep key order and line endings.
  - This session re-reviewed it and made it one install per machine (version 2):
    `%LOCALAPPDATA%\claude-local-llm\` + one user-level hook.
  - **Any repo joins or leaves with one command** and receives only a skill file that holds no machine or
    personal data. The adoption guide is `tools/llm/README.md`, pointed at from the README.
  - This PC was migrated: the hook path was the only change in the user settings.
- **`Readme*.md` -> `README*.md`**, the maintainer's call; the three links follow.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3560 -> 3561.
- `dist\UE5DumpUI.exe`: AOT, 58,183,168 bytes (sha `2cd744571c08`).
- `UE5Dumper.dll`: 3,016,704 bytes (sha `d5e31eb1dfda`), FileVersion `1.0.0.3561`.
- Proxies: version `1b7da35c4095`, dinput8 `9c002a451447`, dxgi `a63bd5072d6b`, winmm `e34d8daf02ff`.
- Tests: UI 5610/5610. `dll_helpers_test` 2984, `utf8_helpers_test` 273, `dll_core_test` 467,
  `sein_retention_test` 30, `grausam_window_test` 22. `ollama_local --selftest` 112/112.
- Gates: 27/27.

## 2026-09-26 (build 3560) — GNames on non-Shipping UE 5.4+ gets its own pattern; the docs archived; published AOT, released as the v3560 draft

**3560 = 3559 plus one DLL change (two AOB rows), up to `a0047064`.** Everything else since 3559 is docs, tests and rigs.
- **`[GNAMES-NONSHIP-LLM54]` (DLL).** The maintainer asked whether 5.8 offers new AOBs now that the
  fixtures ship PDBs. They all do (DumperTest 5.1 / 5.4 / 5.6 / 5.8.3, Shipping included, every pair
  GUID+age matched), and 5.8.3 Shipping needed none. The real gap was non-Shipping.
  - **Cause** (UE source + PDBs): UE 5.4 added `LLM(FLowLevelMemTracker::Get().FinishInitialise())` to
    `GetNamePool()`'s one-time init. LLM is compiled out of Shipping, so only Development / DebugGame
    codegen changed, and every GNames pattern but the 4-byte `GNAM_V1` stopped reaching NamePoolData
    there.
  - **Fix:** `GNAM_LLM54_1` (695) on that init, plus `GNAM_IWB_1` (725) on `FName::IsWithinBounds` as
    insurance. The priority is the maintainer's rule: below every band a 4.23+ Shipping build resolves
    in. Mined and adversarially verified by workflow `wf_8c5dd600-b4f`: every hit truth on 8
    non-Shipping builds 5.4-5.8.3, 0 hits on Shipping, 0 decoys over the 65-program corpus.
  - **Live red -> green**, hint-free: DumperTest 5.4 Dev and 5.8.3 Dev `V1` -> `LLM54_1` at the same
    address, GNames step 7.58 -> 5.02 s and 8.38 -> 6.49 s. Shipping 5.4 / 5.8.3: 0 diff. Repeated on
    this published DLL: `build 3560`, `LLM54_1` wins.
  - The remaining time is 15-16 Pass-2 scans, left by the low priority; 101 would remove them.
- **Docs archived** (the maintainer's request), byte-identical and re-checked:
  - `dev-log.md` 457 -> 176 KB, builds 2779-3261 to `archive/dev-log-2026-08-pre-build-3263.md`;
  - `todo.md` 1.68 -> 1.19 MB, 1,516 lines to `archive/todo-closed-2026-09-26-build-3559.md`;
  - `verification-register.md` 940 -> 904 KB, 4 sections. The rest is open work.
  - An adversarial verifier per cut sent 14 units back; they stay live.
- **Live, closed:** `[PATH-METHODE-NO8DOT3]`. A plugin at an ASCII D: path injects. The refusal is
  unreachable, because CE cannot load a plugin from a non-ASCII path (error 126).
- **Closed:** `[UI-TOOLTIP-RIGHT-THIRD]`. The real mouse shows the tooltip, so it was synthetic input.
- **New lesson:** working-lessons 3.wc. A registry write from the shell may never reach CE, so
  `ce_plugin_register.py` now warns.
- **Readme:** a "Local LLM: Google Gemma 4" badge.
- **Release:** v3559 was tagged and its draft built on 2026-09-26 but never published. 3560 supersedes it.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3559 -> 3560.
- `dist\UE5DumpUI.exe`: AOT, 58,177,024 bytes (sha `57b1ee656f1d`).
- `UE5Dumper.dll`: 3,016,192 bytes (sha `909538a51d10`), FileVersion `1.0.0.3560`.
- Proxies: version `a04a7f784e43`, dinput8 `2baebdc0fece`, dxgi `a9fbfe74949b`, winmm `33f3b43b764d`.
- Tests: UI 5596/5596. `dll_helpers_test` 2984, `utf8_helpers_test` 273, `dll_core_test` 467,
  `sein_retention_test` 30, `grausam_window_test` 22.
- Gates: 26/26.

## 2026-09-26 (build 3559) — SCAN-EARLY: the multi-module scan pins each module; skeptic rounds 9-11; S5 closed, published AOT

**3559 = 3558 plus one DLL fix, three review rounds' fixes and the S5 live pass, up to `7d2b2fcb`.**
- **`[SCAN-EARLY-TRIGGER-CONTAINED]`, found during tonight's live pass, now fixed (DLL).**
  - **Cause.** A `trigger_scan` sent ~1 s after launch died with `RunScan: UNCAUGHT non-standard exception —
    contained`. `Macht::AOBScanAllModules` snapshots `EnumProcessModules`, then read each module's headers and code
    with no reference on it. A DLL the booting engine freed in between was read after it was unmapped.
  - **Fix.** `AOBScanAll` now pins any non-exe module with `GetModuleHandleExW` for the length of its scan, and skips
    a module that is already gone.
  - **Tests.** Red→green in `dll_core_test` (a freed System32 DLL). A test-only seam frees a module mid-scan to prove
    the pin holds the image and is released exactly once. Rounds 10-11 hardened this case against three pin mutations.
  - **Live** (`tools/verify/scan_early_live.py`, five launches each, trigger at 0.8 s):
    - red on 3558: 0/5 clean;
    - green on a tree-built proxy: 5/5;
    - green on this published build's proxy (`618fcc388e66`): 5/5.
    - GObjects is now found on every early launch, so the row's "the scan finds nothing that early" half was the fault
      itself.
- **Skeptic round 9** (6 findings) — **1 MED:** R9-01, the Snapshot row floors pushed an opened Noise picker below the
  window (measured: 299 DIP of overflow). They are now capped at the room the Auto rows leave, on every layout pass.
  - **1 LOW:** the twin rig refused relative paths.
  - **4 INFO:**
    - the Load column is 280 px for its longest text, "shared · stale · loaded <date>";
    - a stale comment;
    - the layout pins strengthened;
    - the TextBox clipboard read re-taken against Avalonia 12.1.3.
- **Round 10** (7 raised, 5 confirmed, 2 refuted) and **round 11** (4 confirmed): all fixes in tests, comments and the
  SCAN-EARLY rig. The rig now checks the proxy's SHA before every launch.
- **S5 live (Proxy Deploy, 3558):** rows 14-18 all PASS. The fixture was hosted in the repo's `EVERSPACE™ 2` shape and
  found by Scan drives; row 18's second folder came from `tools/verify/shared_exe_twin.py`. **All 18 path-shape rows
  are closed.** Also live PASS: `[PATH-UI-LEGACY-QMARK]` (the 3558 UI against a 3553 proxy that sends `?` names).
- **Found and handed to the maintainer:**
  - `[UI-TOOLTIP-RIGHT-THIRD]`: no tooltip past ~1138 DIP on the 4K-at-225 % screen, on 3557 and 3558 alike. It needs a
    real-mouse check.
  - `[PATH-METHODE-NO8DOT3]`'s live step: registering a CE plugin was not done unattended.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3558 → 3559.
- `dist\UE5DumpUI.exe`: AOT, 58,177,024 bytes (sha `58dfbe42ceaf`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `c55a879b5b01`), FileVersion `1.0.0.3559`.
- Proxies: version `618fcc388e66`, dinput8 `cbe1dbde4ed6`, dxgi `71ae9a48a21b`, winmm `69d05ae3164e`.
- Tests: UI 5596/5596. `dll_core_test` 467, `dll_helpers_test` 2962, `utf8_helpers_test` 273,
  `sein_retention_test` 30, `grausam_window_test` 22.
- Gates: 26/26.
- Checked on screen (AOT, 225 %): with the Snapshot Noise picker open, its buttons are inside the window.

## 2026-09-25 (build 3558) — R8-01, NuGet upgrades, and the 4K-at-225 % layout pass, published AOT

**3558 = 3557 plus four things, up to `5191b39c`.** It has no DLL or `.CT` code change; the DLL differs only in the
build stamp.
- **Round 8 (`wf_41d4c1ab-5e4`): 1 finding, INFO.** `[R8-01]`: the seventh round widened the Load column for a font it
  never gets. A DataGrid cell is Fluent's 15 px Inter with 12 px margins, not the grid's 12 px, so 210 px still cut
  `shared · loaded 2026-09-25`, and a trailing `(stale)` never showed at all. Now the marks lead
  (`stale · loaded <date>`), the column is 240 px, and the header tooltip explains both marks.
- **NuGet (maintainer's request, evaluated one by one):**
  - **Avalonia 12.1.1 → 12.1.3** (Win32, Skia, HarfBuzz, Themes.Fluent, Fonts.Inter). Avalonia.Skia 12.1.3 and
    Avalonia.HarfBuzz 12.1.3 still declare **SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3** (read from their nuspecs).
    So `<SkiaSharpVersion>` / `<HarfBuzzSharpVersion>` do not move: SkiaSharp 4.x / HarfBuzzSharp 14.x are not what
    this Avalonia is built against (the ABI crash of the archived todo). `Avalonia.Controls.DataGrid` stays at 12.1.2,
    the newest published.
  - **xunit.v3 3.2.2 → 4.0.1, xunit.runner.visualstudio 3.1.5 → 4.0.0, Microsoft.NET.Test.Sdk 18.8.1 → 18.10.1.**
    xunit.v3 4 drops Microsoft Testing Platform v1, so the graph moves from `mtp-v1` / MTP 1.9.1 to `mtp-v2` /
    MTP 2.4.0, still transitive. The forbidden-pin guard is unchanged. Nothing here uses what 4.0 broke: no orderers,
    no `CollectionBehavior` parallel properties, no `-report-*` switches. xunit.analyzers 2.1.0 raised no new
    warning, and `--filter-class` still works.
- **`[UI-SPACE-2026-09-25]` (maintainer's request).** Measured on the maintainer's 3840x2400 laptop at 225 %
  (1707x1067 DIP), build 3557:
  - **Main tab strip.** It wrapped to three 48 DIP rows. It is now 18 px headers in 36 DIP rows: two rows.
  - **Snapshot.** The diff grid had only its header. Both grid rows now carry a MinHeight, the auto-snapshot hint is
    a tooltip, and the saved header and the usage row share one wrapping row. On screen: the saved list ~4 rows, the
    diff grid ~3.
  - **Class Pivot.** The field picker and the results were below the window. Now the three paragraphs are one trimmed
    line each, with the full text on hover. The limits box opens from a toggle. Discover and the pivot target scroll
    under a cap of half the panel's height. On screen, both work grids show.
  - Pinned by `PanelSpaceBudgetTests` (red `f0df71a8`). Checked on screen from a staged build before publishing.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3557 → 3558.
- `dist\UE5DumpUI.exe`: AOT, 58,174,976 bytes (sha `a6ca83e47cad`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `c5fe3f2fbea6`), FileVersion `1.0.0.3558`.
- Proxies: version `33c4372d300d`, dinput8 `62c2086159f4`, dxgi `b9b263781dcb`, winmm `1f4750d7e27b`.
- Tests: UI 5594/5594. `utf8_helpers_test`, `dll_helpers_test` passed; `dll_core_test` 455, `sein_retention_test` 30,
  `grausam_window_test` 22.
- Gates: 26/26.

## 2026-09-25 (build 3557) — PATH-SHAPE: the seventh skeptic round's fixes, published AOT

**3557 = 3556 plus the seventh skeptic round's fixes, up to `45fd9a9c`.** It has no DLL or `.CT` code change; its
DLL differs only in the build stamp.
- **Round 7:** 6 findings, all INFO, nothing LOW or above.
  - **R7-01 / R7-02, R7-03 / R7-04:** test pins, each proven by mutation. The breadcrumb call is pinned
    comment-proof. The older-entry loop and a comment-only `dll-path.txt` are pinned. The drive-root fix is pinned on
    the writer's own line and in lower case, on both sides.
  - **R7-05:** the Proxy Deploy panel's Load column widened from 150 to 210 px, so a shared row shows its date.
  - **R7-06:** the Suggested column's tooltip now follows `ProxyImportAnalyzer.Recommend`'s real order.
- **Live so far (plan `docs/path-shape-live-plan.md`):**
  - S1: 15/15 on 3555.
  - S2: `[PATH-ES2-CE-SYMBOLS]` answered on 3556, not a finding.
  - S3 #6: the AOBMaker plugin matches the `遊戲` exe by name.
  - S3 #10: the confirmed record uses the real name.
  - Still open: S3's UI-side items and S5.
- **Also:**
  - A peer session added the `local-llm` skill and `tools/llm/ollama_local.py`. Its selftest is gate 26, in
    `check_all` and CI.
  - This session reviewed it (sound) and used it. It summarised the 63 KB archived scan log of an early
    `trigger_scan` for `[SCAN-EARLY-TRIGGER-CONTAINED]`, and the lead was checked against the raw log.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3556 → 3557.
- `dist\UE5DumpUI.exe`: AOT, 58,004,992 bytes (sha `ffcb286e1abd`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `2f70377c3aaa`), FileVersion `1.0.0.3557`, from `45fd9a9c-dirty`.
- Proxies: version `c965b79bc80b`, dinput8 `918c80f4392a`, dxgi `e2ccfe7656c7`, winmm `e9ad326eddd4`.
- Tests: UI 5588/5588. `dll_helpers_test` 2962/0, `utf8_helpers_test` 273/0, `dll_core_test` 455, `sein_retention_test`
  30, `grausam_window_test` 22. All 11 Lua suites pass on CE's VM.
- Gates: 26/26.

## 2026-09-25 (build 3556) — PATH-SHAPE: the sixth skeptic round's fixes, published AOT

**3556 = 3555 plus the sixth skeptic round's fixes, up to `9aa99ca2`.** The DLL's code is unchanged from 3555; only its
build stamp differs. So the live checks S1 passed on 3555 (`module_name`, `ce_base`, the log folder) stand for the DLL.
The changes are to the `.CT` and the UI:
- **`[R6-01]`, LOW** (a regression from R5-04, measured by the reviewer on CE's VM): a DLL folder at a drive root
  (`E:\`) was written to `dll-path.txt` as a bare `E:`, which every reader dropped as relative. Both writers now keep
  the root separator, and both readers read a legacy bare `X:` as that drive's root.
- **`[R6-02]` / `[R6-06]`:** the breadcrumb slot is now a function the Lua suite runs. It says "holds only relative
  folders" when every recorded line was dropped.
- **`[R6-03]` / `[R6-05]`:** the `shared · ` ambiguity tag now LEADS the Proxy Deploy panel's Load and Suggested texts.
  A trailing mark was clipped by the columns' width. The two column headers carry a tooltip saying what the tag means.
  The Load tag follows the log folder, not only the exe name.
- **`[R6-04]`:** a negative control.
- Also since 3555: `[SCAN-EARLY-TRIGGER-CONTAINED]` is recorded (a `trigger_scan` during engine boot is contained but
  not retried). The live-check rig now waits for the engine to boot.
- A seventh skeptic round, over these fixes, is running.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3555 → 3556.
- `dist\UE5DumpUI.exe`: AOT, 58,004,992 bytes (sha `7d6dfbcbe87d`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `cb4031cc0917`), FileVersion `1.0.0.3556`, from `9aa99ca2-dirty`.
- Proxies: version `bc599b640d6b`, dinput8 `1cb6ed3486b3`, dxgi `1ddf3d8b803a`, winmm `bb659306e0af`.
- Tests: UI 5587/5587. `dll_helpers_test` 2962/0, `utf8_helpers_test` 273/0, `dll_core_test` 455, `sein_retention_test`
  30, `grausam_window_test` 22. All 11 Lua suites pass on CE's VM.
- Gates: 25/25.

## 2026-09-25 (build 3555) — PATH-SHAPE: non-ASCII / multi-space / special-character paths and exe names, five skeptic rounds, published AOT

**3555 = 3554 plus 49 product commits, up to `7bcab1ba`** (`git log --oneline 641ea268..7bcab1ba -- dll/src
ui/UE5DumpUI scripts`; 131 commits in all). The rows are in `docs/todo.md` under `[PATH-SHAPE-2026-09-25]`.
- **Maintainer request.** While fixing `[PROXY-CONFIRM-EXE-KEY]` (a non-ASCII exe name), also check the path shapes on
  this PC: a non-ASCII folder (`EVERSPACE™ 2`), two spaces in a row (`DragonSword  Awakening`), special-but-legal
  characters (`No Man's Sky`). A 6-agent read-only workflow mapped them into 18 rows. Each row was fixed red → green,
  one commit per row.
- **The two measured facts behind the fixes.**
  - CE never sees a module's Unicode name. It sees the ANSI `Module32First` name: best-fit narrowing, then cut after
    the last 0x5C byte, so Big5 功 = A5 5C and `功夫-…` is `夫-…` to CE. CE-facing strings now use that view (the UI's
    `ISystemCodePage.AnsiModuleName`, the DLL's `Methode::CeModuleNameUtf8`), while keys use the real UTF-8 name
    (`module_name`).
  - CE's `injectDLL` hands the path's bytes to `LoadLibraryA`. The generated scripts, the `.CT` and the CE plugin now
    use, in order:
    1. an ASCII path as it is;
    2. an ASCII 8.3 alias of the FOLDER, keeping the DLL's own name (a file alias renames the loaded module to
       `UE5DUM~1.DLL`);
    3. the exact ANSI narrowing (never best fit);
    4. the alias's exact narrowing;
    5. otherwise a refusal that says why.
    Before, CE silently manual-mapped the DLL (no TLS, no `.pdata`).
- **Also fixed:**
  - a log folder for a stem ending in a space or dots;
  - an apostrophe in the trainer's Lua;
  - `&` in CE XML;
  - braces in Serilog output;
  - the PS1's elevation quoting and `-LiteralPath`;
  - the `.CT`'s recent-files split on `\0`, which read reg.exe output as UTF-8;
  - a proxy-named file we cannot read is now a third state, UNREADABLE (maintainer's call): never written or
    deleted, and said once;
  - the one-shot import-risk note outlives the refresh;
  - `[CI-GATE-DRIFT-2026-09-25]`: nine gates never reached CI; a parity gate, `check_ci_gate_parity`, now requires
    each CI gate line's exit check;
  - gate 25, `check_lua_suites`, runs the Lua suites on CE's own VM, and a skipped gate is counted as skipped.
- **Maintainer's decisions, 2026-09-25:**
  - `[PROXY-CONFIRM-SHARED-EXE]`: mark it ambiguous. An exe name that games in different folders ship uses neither
    exe-keyed record (confirmed, injected), and the Load column says the load may be another game's.
  - `[PATH-AOBMAKER-ANSI-MATCH]`: fixed in AOBMaker's own session (`ce8247c`); the deployed plugin is that build.
  - Path shapes come from a committed script inside the repo: `tools/verify/path_shape_folders.py` builds 10 folders
    under `out/pathshape/`, including the maintainer's letterlike-symbol folder in both spellings, and the unit tests
    pin them.
- **Five skeptic rounds over the fixes:** 24, 27, 9, 7 and 13 findings. Every one was fixed, a test gap's red measured
  by mutation. One HIGH, a regression from our own fix: the 8.3 FILE alias renamed the loaded DLL (`bc21def8`). A
  sixth round, over the fifth's fixes, is running.
- **Not yet run on a game:** the live checks, in `docs/path-shape-live-plan.md` (5 sessions, 18 checks), together with
  build 3554's `[PROXY-DEPLOY-UX]` checks.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3554 → 3555.
- `dist\UE5DumpUI.exe`: AOT, 55.3 MB (58,001,920 bytes, sha `9654a38bf11a`).
- `UE5Dumper.dll`: 3,015,680 bytes, sha `0689f44d3e62`, FileVersion `1.0.0.3555`, built from `7bcab1ba-dirty` (the
  dirty suffix is the bumped `build_number.txt`).
- Proxies in `dist\proxy\`: version `80c7ec21eb6a`, dinput8 `33baedb17bfb`, dxgi `cda5136f3043`, winmm `920ac9b05a08`.
- Tests: UI **5585/5585**. `dll_helpers_test` 2962/0, `utf8_helpers_test` 273/0, `dll_core_test` 455 checks, `sein_retention_test` 30,
  `grausam_window_test` 22. All 11 Lua suites pass on CE's VM.
- Gates: 25/25.

## 2026-09-25 (build 3554) — Proxy Deploy: never a second of our proxies; "Use confirmed-working proxy", published AOT

**3554 = 3553 plus 4 product commits, up to `64dcd886`** (`git log --oneline 55327e9d..64dcd886 -- dll/src
ui/UE5DumpUI scripts`). The rows are in `docs/todo.md` under `[PROXY-DEPLOY-UX-2026-09-25]`.
- **Maintainer requests, two.** (1) Deploying one type across many games put a SECOND proxy of ours into every game
  that already carried another type. (2) A game whose confirmed-working proxy is known, but whose folder is clean
  again (a reinstall), should get that type whatever the radio says.
- **Evaluation.** A 5-agent read-only workflow (`wf_b499f5ad-c49`: two maps, two designs, a judge that checked both
  against the source) found both worth building, as one design. Two things weighed:
  - At runtime the first-loaded proxy wins Heiter's mutex, and a double leaves no log trace.
  - Undeploy removes BOTH proxies, so a user repairing a double can delete the one that worked.
- **`[PROXY-DOUBLE-GUARD]` (`f4bf14ed`, `22083ab8`).** An always-on guard, with no checkbox.
  - A folder that already holds another of OUR proxies (by PE ProductName) is skipped by Deploy, even with Force
    Overwrite and even with foreign consent.
  - The same type still follows the Force rules.
  - The row says why: "Skipped: winmm.dll (ours) is already deployed here — Deploy never adds a second of our
    proxies. Undeploy first to switch type." The result line counts `skipped: N`.
  - `PlanDeploy` has a new `OtherProxyOfOurs` verdict, and `DeployAsync` refuses too, for any caller.
  - Update All is unchanged: it only rewrites types already there.
- **`[PROXY-USE-CONFIRMED]` (`7376ae05`).** A new checkbox, "Use confirmed-working proxy", which is not persisted.
  - For a ticked game with NONE of our proxies and a confirmed record, Deploy writes the recorded type.
  - Foreign consent is never used for it.
  - The row says "Deployed winmm.dll (confirmed working) instead of version.dll". The result line counts `confirmed
    type used: N`.
- **Review (`wf_c116385e-ad2`, 4 lenses; file safety found nothing), fixed in `0ef097ec`, red `3f306407`.**
  - Skipping an already-doubled folder had wiped the refresh's "Multiple proxy DLLs deployed" warning. It is kept now.
  - A foreign file at the confirmed type's name failed with a message that named neither type. It now says so.
  - A switched row's failure now says the confirmed type failed.
  - The checkbox tooltip over-claimed, and is now narrower.
  - Five test gaps closed, the service backstop through the real `DeployAsync` among them.
- **Not yet run on a game:** the live check, recorded as pending on both rows.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3553 → 3554.
- `dist\UE5DumpUI.exe`: AOT, 55.2 MB (57,897,984 bytes, sha `767e52a6098b`).
- `UE5Dumper.dll`: 3,008,000 bytes, sha `8c3a7fbd6b24`, FileVersion `1.0.0.3554`, built from `64dcd886-dirty` (the
  dirty suffix is the bumped `build_number.txt`).
- Four proxies went to `dist\proxy\`.
- Tests: UI **5430/5430**. `dll_helpers_test` and `utf8_helpers_test` pass. `dll_core_test` 455 checks,
  `grausam_window_test` 22 and `sein_retention_test` 14 checks.
- Gates: 23/23.

-----

## 2026-09-25 (build 3553) — Proxy Deploy: Update All honours Force Overwrite; Deploy stops claiming no-op writes, published AOT

**3553 = 3552 plus 3 product commits, up to `55327e9d`** (`git log --oneline 9019eada..55327e9d -- dll/src
ui/UE5DumpUI scripts`). The rows are in `docs/todo.md` under `[PROXY-FORCE-UPDATEALL-2026-09-25]`.
- **Reported by the maintainer.** With Force Overwrite ticked, Update All said "All N deployed proxy DLL(s) already
  up-to-date" and wrote nothing. So a rebuild that kept its build number (the proxy's FileVersion is only
  `1.0.0.<build_number>`) was never redeployed, which is exactly what happened on the other PC.
- **Cause.** `UpdateAllAsync` ran its own FileVersion check and never read the checkbox. The Deploy button already
  passed it.
- **The fix (`d5f60f09`).** Update All skips a same-version proxy only when Force is off. The checkbox is read once per
  run. The result line says what Force did: `Updated: N (M rewritten at the same version — Force Overwrite)`.
  - There is no hash or timestamp check: the maintainer's call, now settled in working-lessons §6.
  - AC1 is untouched: only a proxy already deployed AND ours is written, and foreign consent is never passed.
  - Both tooltips state the rule.
- **Mapping.** A 3-agent read-only workflow mapped every same-version skip and everything that pins or describes one.
  Only this one path broke the rule. The archived AC1 live check had recorded this very symptom as a PASS.
- **Three skeptic lenses over the fix** found no defect. Their test gap, pinning the `ForceSameVersion: true` the forced
  path now relies on, is closed in `8039ab4b`.
- **The adjacent row they found, `[PROXY-DEPLOY-NOOP-COUNT]` (`0aee3925`, `bd8869cf`).** With Force off, Deploy
  onto our proxy already at the source's version wrote nothing (the service answers AlreadyCurrent), yet showed a
  green `Deployed: 1 success`. It now counts that as `already current: N (tick Force Overwrite to rewrite them)`, in
  neutral colour, as Update All does. Two more skeptics found behaviour parity with the service case by case.
- ⚠ **Side effect, intended.** Force Overwrite is persisted, so a tick left from an earlier session now makes every
  Update All rewrite every deployed proxy of ours. A running game's proxy then fails as "Target in use" instead of
  being skipped silently.
- **Not yet run on a game:** the live check, recorded as pending on both rows.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3552 → 3553: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,810,432 bytes, sha `8ba915f779ed`), `UE5Dumper.dll` 3,008,000 bytes, sha `cef9994d70f5`, stamp `1.0.0.3553
55327e9d-dirty` (the dirty suffix is the bumped `build_number.txt`). Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5397/5397**, `dll_helpers_test` 2921/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, `grausam_window_test` 22 and `sein_retention_test` 14 checks. Gates 23/23.

-----

## 2026-09-25 (build 3552) — R7-X8: Instance Finder's truncation advice follows every container, published AOT

**3552 = 3551 plus 2 product commits, up to `9019eada`** (`git log --oneline c6d667ec..9019eada -- dll/src
ui/UE5DumpUI scripts`). The row is `docs/todo.md` `[R7-X8]`.
- **Found live on 3551, by R7-S11's finding probe.** DumperTest's NestedBag had 16,000 pairs at Array Limit 16384.
  The copy truncated with "no toolbar setting shrinks this export", yet the same copy at 8192 was complete (49,738
  entries). R7-S6 named the limit only for a container bound by the CURRENT limit. But lowering the limit shrinks
  every container longer than the new one.
- **The advice now names the Array Limit** whenever a walked container has more elements than the slider's floor
  (`Constants.MinArrayLimit` = 2), largest first. It keeps "… or use Open in Live Walker → Copy CE Field for the part
  you need" beside it, because shrinking is not fitting.
- **Two more containers stop counting:** a sparse delegate and a `TArray<TFieldPath>`. Each is written as one entry
  at any length, so it is neither a lever nor a clipped export.
- **One skeptic reviewed the fix.** It found no defect; its two notes are the follow-up `abf9991e`.
- **Not yet run on a game:** the NestedBag copy at 16,000 / 16384 must now name the Array Limit (the row's live
  check).
- Also since 3551, docs only: the live PASS records for X5 and X6 on 3551. X6's NestedBag export stops at 60,001
  entries, marked TRUNCATED (3550 copied 98,890 unflagged). X5: a 1.4 invoke helper replaces a resident 1.3 in the same
  CE process, and R7-S3's re-inject then passes.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3551 → 3552: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,807,360 bytes, sha `479735bbe97c`), `UE5Dumper.dll` 3,008,000 bytes, sha `05ce27367fca`, stamp `1.0.0.3552
9019eada-dirty` (the dirty suffix is the bumped `build_number.txt`). Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5389/5389**, `dll_helpers_test` 2921/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, `grausam_window_test` 22 and `sein_retention_test` 14 checks. Gates 23/23.

-----

## 2026-09-25 (build 3551) — Review 7's live pass: every live row PASS, and four more rows fixed, published AOT

**3551 = 3550 plus 6 product commits, up to `c6d667ec`.** Derive them: `git log --oneline 1c514220..c6d667ec
-- dll/src ui/UE5DumpUI scripts`. Rows, recipes and evidence: `docs/todo.md` `[REVIEW7-2026-09-24]` and
`docs/review7-live-plan.md` (sessions 1-26).
- **The live pass.** Every Review 7 row with a live check is a live PASS red→green, recorded on its own row. The
  reds came from staged pre-fix DLLs and pre-fix UIs identified by SHA. A few are partial, and each says which arm
  stays unit-only. The plan's own errors were corrected in place: D-07 / S10 need a pointer array; S12's own-probe
  arm does not discriminate; S11's truncated arms wait on X6.
- **Four more rows, all fixed here** (X4-X6 from the live pass, X7 from its skeptic):
  - `[R7-X4]` (MED). A stock UE 5.3 title was reported as 5.4. The marker is now CMC's `SetGravityDirection`
    UFUNCTION, not the `GravityDirection` property that 5.3 already reflects. Live on stock 5.3: 503, and
    R7-B-01's `[garbage]` tag now reaches it. DragonSword and Avowed are 5.3.
  - `[R7-X5]`. The CE invoke helper kept the first copy a CE session loaded; R7-S3's latch fix never reached a
    table opened earlier. It is now version-gated like the freeze helper (1.4).
  - `[R7-X6]`. Eight container element loops ignored the CE XML export's 60,000-entry ceiling. A map emitted
    last copied 98,890 entries unflagged.
  - `[R7-X7]`, text only. The Gravity Direction card said "UE5.4+". Measured, stock 5.3 honours the field.
- **One skeptic over X4-X6:** no defect in the fixes. Its X4-adjacent note became X7 after the measurement, and its
  three stale comments were fixed.
- **Not yet run on a game:** X5 (a 1.3 helper, then 1.4, in one CE process) and X6 (the NestedBag export must now read
  TRUNCATED, which is also R7-S11's F-20000 arm). Both are recorded as pending on their rows.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3550 → 3551: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,773,568 bytes, sha `8d0322b5a355`), `UE5Dumper.dll` 3,008,000 bytes, sha `25bb1f2878fe`, stamp `1.0.0.3551
c6d667ec-dirty`. The suffix is the bumped `build_number.txt` and nothing else. Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5385/5385**, `dll_helpers_test` 2921/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, `grausam_window_test` 22 and `sein_retention_test` 14 checks. The Lua suites
pass 10/10 on CE's own Lua 5.3 VM. Gates 23/23.

-----

## 2026-09-25 (build 3550) — Review 7: the code since Review 6, fixed at every tier, published AOT

**3550 = 3549 plus 32 product commits, up to `1c514220`.** Derive them: `git log --oneline c88ca562..1c514220
-- dll/src ui/UE5DumpUI scripts`. Every row, test and piece of live evidence is in `docs/todo.md`
`[REVIEW7-2026-09-24]`: 35 fixed rows, one per commit, red before green where testable.
- **The review.** Four area finders and four skeptics over the code added since Review 6 raised 20 rows and
  refuted one: 2 MED, 12 LOW, 4 INFO, all fixed. Among them: UE 5.0-5.3's default PendingKill state is tagged
  `[garbage]` like 5.4's; the compact-set guard reaches the sparse-delegate readers; `apply_rescan` re-initialises
  under the init fence; the Fly / KeepForeground / SeeThrough scripts no longer pop a modal on an untick; a CSX
  FString child's byte size is in bytes.
- **Seven skeptic rounds over the fixes themselves** added R7-S1..S14. The Lua freeze helper no longer wedges the
  shared mailbox latch after a re-inject, and the mailbox init check reads the fence flag last. Instance Finder's
  truncation notice is now derived from the walk; this closes `[INSTEXPORT-TRUNC-ADVICE]`, which R7-D-03 had first
  fixed the wrong way. Its export also no longer pairs one walk's rows with another instance or limit. Every
  AOBMaker "not reachable" line follows the latest probe's reason. The last round found the final fix correct;
  its two small notes (an unexercised guard, a refusal's wording) were closed in a follow-up.
- **The live regression found two more** (R7-X2, R7-X3). Find References now reports an unlocated sparse storage
  as skipped. The storage validator accepts a vtable in any mapped module, not only the main exe. Live on the UE
  4.27 editor `-game`: before the fix, `sparse_delegates` was 0x0 and the storage was rejected. After it, the storage
  is located, the `CollisionCylinder` bindings decode as `[CharMoveComp::PhysicsVolumeChanged]` /
  `[CharMoveComp::CapsuleTouched]`, and Find References on the live `CharMoveComp` returns both. DumperTest 5.4
  Shipping `fixture_smoke` against 3549, on the Review 7 DLL before X2/X3: 0 differences.
- **Not yet run on a game:** the Instance Finder advice (R7-S6 / S11 / S13 / S14; the L63 rig). The DumperTest
  "±2 GB" comments wait for its next repackage.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3549 → 3550: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,771,520 bytes, sha `133c75b34b1b`), `UE5Dumper.dll` sha `a32cb03df296`, stamp `1.0.0.3550 1c514220-dirty`.
The suffix is the bumped `build_number.txt` and nothing else. Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5375/5375**, `dll_helpers_test` 2913/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, and `grausam_window_test` / `sein_retention_test` passed. Gates 23/23.

-----

## 2026-09-24 (build 3549) — weak Force-null, and the case-preserving-name bundle (VND583-07) measured on two editor hosts

**3549 = 3548 plus four product commits, up to `c88ca562`.** Derive them: `git log --oneline 26993098..c88ca562
-- dll ui`. The row text, tests and live evidence are in `docs/todo.md` (`[VND583-DOC]` D7-04 and `[VND583-07]`).
- **Force-null holds a weak pointer** (`c181d742`, D7-04). `{0, 0}` is UE's own null, not "GObjects[0]", so an
  8-byte WeakObjectProperty is now held at UE's reset value: `{INDEX_NONE, 0}` up to 5.0, `{0, 0}` from 5.1.
  Soft and lazy pointers, and the 16-byte remote-handle weak pointer, are still refused. The UI's Force-null
  button covers the weak type. Live on DumperTest 5.4 Shipping with `-DumperTestWeakGarbage`: 3548 answers -12;
  the new build holds `WeakToGarbage` at null while the fixture re-points it every 5 s.
- **A case-preserving host that resolves** (`d936681f`). The UE 5.4 editor running DumperTest `-game`: the
  exported `FName::ToString` reaches NamePoolData only through a call, so `SymbolCallFollow` now follows up to
  four calls. Before this, EOSSDK's own name pool won the AOB fallback and 0 of 10 names resolved.
- **FName::Number is measured** (`937c216c`, A9 step 11). A case-preserving UE4 / 5.0 build keeps Number at +8,
  after the DisplayIndex. On the UE 4.27 editor running UE427_3rdPerson `-game`: the pre-fix decode (`d936681f`)
  put the DisplayIndex in the number on 3145 of 3145 names; the fixed one is right on 3300 of 3300.
- **sizeof(FName) is measured** (`c88ca562`, A9 steps 1 and 7). It is the modal NameProperty ElementSize. The
  rule no longer overrides a plausible engine size. Live: 12 on the 4.27 editor, 8 on DumperTest 5.4 Shipping, both
  64 of 64. `fixture_smoke shipping` against 3548 differs in 0 offset verdicts and 0 AOB winners.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3548 → 3549: `dist\UE5DumpUI.exe` AOT 55.0 MB
(57,723,392 bytes, sha `79bb951b2de9`), `UE5Dumper.dll` sha `31126a429fed`, stamp `1.0.0.3549 9d4fdb69-dirty`.
The suffix is the bumped `build_number.txt` and nothing else (see the correction in the build 3368 entry, 2026-09-03).
Four proxies went to `dist\proxy\`. Tests: UI **5329/5329**, `dll_helpers_test` 2903/0,
`utf8_helpers_test` 265/0, `dll_core_test` 430 checks, and `grausam_window_test` / `sein_retention_test` passed.
Gates 23/23.

-----

## 2026-09-24 (build 3548) — vendor audit #7 fixed: VND583-01..17 and the DOC bundle, published AOT

**3548 = 3547 plus the `[VND583-*]` rows, up to `26993098`.** Derive the list, do not copy it:
`git log --oneline b9e6aff0..26993098 -- dll ui` (20 commits, 14 row tags). Every row's commit, test and
live evidence is in `docs/todo.md` `[VENDOR-UE583-2026-09-24]`; the source-side facts are in
`docs/audit-2026-09-24-vendor-ue583.md`. In short:
- **Measured instead of keyed on version**: UFunction::FunctionFlags by a vote over parameter chains (01);
  UField::Next in FProperty mode (02); alignof(FName) on a single-FName ScriptStruct (03); the UEnum value
  column on ENetRole (04, uint8 on 4.9–4.14); FSoftObjectPath's shape on the SoftObjectPath struct (14).
- **Weak pointers**: a top-level weak says null / null (stale) / unreadable (05); a target UE's `Get()` would
  refuse is labelled `[garbage]` (06); serial 0 is UE's explicit null (08).
- **Layout and version**: UE 5.2 keeps the 16-byte FFieldVariant defaults (09); the static-struct GObjects
  resolver reads 5.8's array and the 5.7+ item (10); the CPN property family starts at +0x34 (11); FNameData
  enums mean 5.7+ (12); compact TSet/TMap builds are guarded (13).
- **Tools**: the CRC oracle merges instead of overwriting, with a new gate (16); `find_gobjects.java` finds 5.8's
  narrow anchor string (17). 07 (A9) and 15 are filed without code by the audit's own instruction; 18 (minhook)
  stays on HOLD.

**Live, red → green where a host exists**: DQ XI S (01, 03), NEKOPALIVE (04), DumperTest 5.4 (05, 06, 08),
forced static recovery on DumperTest58 5.8 (10, whose live arm found a second defect, fixed in `de3d397a`), and
Ghidra on StackOBot 5.8 (17). Regressions: DumperTest 5.1 / 5.4 / 5.8 and DQ I & II HD-2D (09, 11–14).
**New hosts**: `-DumperTestWeakGarbage` (DumperTest repackaged: Development, Shipping and DebugGame; its
`pe_hash` changed again), and the UE 5.4 editor running DumperTest `-game`, the first case-preserving host.
CPN is detected there, but its name pool does not resolve yet (`[VND583-07]`).

**The build.** `build.ps1 -Mode Publish` bumped 3547 → 3548 but failed its UI tests. A source-pinning test
still expected the old `FindGObjectsStaticStruct` call; it was fixed in `26993098`. It then re-ran with
`-NoBumpBuildNumber`: `dist\UE5DumpUI.exe` AOT 55.0 MB (sha `d2c240608c52`), `UE5Dumper.dll` sha
`318682738eeb`, stamp `1.0.0.3548 26993098`, and four proxies to `dist\proxy\`. Tests: UI
**5327/5327**, `dll_helpers_test` 2877/0, `utf8_helpers_test` 265/0, `dll_core_test` 414 checks, and
`grausam_window_test` / `sein_retention_test` passed. Gates 23/23 (`crc_oracle_selftest` is new).

-----

## 2026-09-24 (build 3547) — the first bump since 3546: the whole fix pass, published AOT

**Why an entry now.** Build 3546 was stamped at `567b9afc` (2026-09-12). Every product change of the
fix pass since then shipped as "3546, no bump", so no build number could map a binary to its source.
The maintainer asked on 2026-09-24 for the number to move again, so that this log can map builds to
changes. **3547 = the fix pass up to `0f1be99f`.**

**What 3547 contains that 3546 did not.** 139 commits touch `dll/` or `ui/` (395 in all), with 127
distinct row tags. Derive the list, do not copy it:
`git log --oneline 567b9afc..0f1be99f -- dll ui`. The per-row ledger is `docs/todo.md`
`[FIXPASS-2026-09-10]`, and the live checks are `docs/fixpass-low-live-plan.md` plus the todo.md
Live-check backlog. Two points matter to anyone holding an old binary or table:
- the CE Lua ↔ DLL **mailbox contract is v5** (`[W5-OFFSETS-MAILBOX]`, min 1);
- the See-through producer probe fix (`[SEETHRU-PROBE-SUBSTRING]`, `39ccb2a4`) and the export-status fix
  (`[EXPORT-STATUS-LATE-PROGRESS]`) are the last product changes before the bump.

**The build.** `build.ps1 -Mode Publish`: `dist\UE5DumpUI.exe` AOT 55.0 MB (sha `24b6ce92b330`),
`UE5Dumper.dll` sha `7b1a26a20950`, stamp `1.0.0.3547 b9e6aff0` (the tree then differed from
`0f1be99f` only in docs). Four proxies to `dist\proxy\`. Tests: UI **5327/5327**, `dll_helpers_test`
2752/0, `utf8_helpers_test` 265/0, `dll_core_test` 356 checks, and `grausam_window_test` and
`sein_retention_test` passed. Gates 22/22.

**The same day, no product change:**
- **Vendor sync + audit #7** (`docs/audit-2026-09-24-vendor-ue583.md`): UE 5.8.3, RE-UE4SS `f58e8f84`
  with UEPseudo and patternsleuth initialised for the first time, Dumper-7 `dd8fe34`, and minhook HELD.
  UE 5.8.3 changes nothing for us. Reading the new sources filed `[VND583-01..18]`, led by FunctionFlags
  keyed on version (breaking on FF7R / DQ XI S).
- **DumperTest58 repackaged with the UE 5.8.3 editor** (Shipping + Development). Its `pe_hash` changed,
  which orphans its old per-game app-data folders. `tools/verify/fixture_smoke.py` found the DLL's 57
  verdict lines and every AOB winner identical to the 5.8.2 runs.
- **Live checks closed**:
  - L6 step 2: an injected Escape never reaches the app on this rig, so `send_key.py --post` is used (lesson 1.af).
  - L7 and L21: through the slow-walk staging.
  - L13 steps 2-3: through a UI seam.
  - L33 step 2: on Avowed through a staging, with a correction.
- **New rigs**: `fixture_smoke.py`, `proxy_swap.py`, `send_key.py`, and `staging/slow-walk.json`.

-----

## 2026-09-12 (build 3546, no bump) — the three HIGH live checks, run on a game

No product change: three verification runs and their evidence. `[FIXPASS-2026-09-10]`'s ledger has
exactly **three HIGH rows** — 1 `[W1-QUOTA-UNLIMITED]`, 2 `[W1-SNAP-FAULT]`, 3
`[P4-OTHER-INSTANCE]` — and L1/L2/L3 in the live-check backlog are their checks. All three now
pass, so **HIGH is closed** and the backlog resumes at L4, the first MED.

Fixture throughout: DumperTest Shipping, DLL `1.0.0.3546`, 24,497 objects, UE 504, driven by the
**AOT-trimmed** `dist\UE5DumpUI.exe` (55.0 MB, sha `e278e02a`). The build matters for L1 in
particular: a `ComboBox.SelectedItem` bound to a boxed value plus a JSON round trip is exactly the
shape that works untrimmed and fails only after trimming.

### L1 `[W1-QUOTA-UNLIMITED]` — `df7681d4`

Picking *Unlimited* rewrote `experimental.json` to `"snapshotQuotaMb": 0` immediately rather than on
exit, and the setting survived a clean `WM_CLOSE` + relaunch — and then, unplanned, a full machine
reboot. Nothing was FIFO-deleted, the DB kept its row, usage read "4.2 MB (no limit)".

### L3 `[P4-OTHER-INSTANCE]` — `5b5971f3`

Two `StaticMeshActor` instances opened through Instance Finder with no GWorld click between them:
A = `0x29254B67C00`, B = `0x29254B67EC0`.

  * **addressing** — every one of 16 `walk_instance` replies echoed B's address and B's
    `struct_data_addr`, so it holds ACROSS Auto ticks, not merely on the initial open;
  * **write** — the half a read-only check cannot cover, because a base mix-up that READS right can
    still WRITE wrong. Editing `bCanBeDamaged` produced exactly ONE `write_mem` in the whole
    session, `{"addr":"0x29254B67F1A","bytes":"24"}`, and A's corresponding byte address
    `0x29254B67C5A` appears ZERO times in the log. An independent pipe re-read — deliberately not
    the UI's own grid — gave B `24`/true and A `20`/false, with B's sibling bits in that same packed
    byte untouched, so the read-modify-write set bit 2 and nothing else;
  * **scroll** — 7 Auto ticks before the write and 9 after, the grid held at 0x59/0x5A throughout,
    which also means the value read back after the write was a fresh memory read and not a stale
    cell. `IsEditing` visibly suppressed the tick ("paused (editing)") during the edit.

### L2 `[W1-SNAP-FAULT]` — `4715dbe5`

`9f86f7b6` fixed this in source and said so itself: *"the DLL half has no unit seam"*, because
`dll_core_test` can only fault `ParallelGObjectsScan` through its own stub. This is the run that
exercises the real one — a hardware access violation inside a scan worker, reaching the `/EHa`
`catch (...)` at `Aura.cpp:285` — by pointing `UE5_SetObjectDecryption` at `0x1000` and restoring it.

A control ran first, because "everything is marked unusable" would satisfy the assertion while
proving nothing: an unarmed capture finalised `is_usable=1`, 1700 objects. Armed, the capture
finalised `is_usable=0` with `partial_reason=''` — correctly NOT `cap`/`disklow`, since a fault
lands in `is_usable`; the status named a worker FAULT and never a deadline; `offsets-0.log` carried
16 `ParallelGObjectsScan: worker tid=N [a,b) faulted` lines covering `[0,8192)` with no gap, i.e.
one whole chunk and only that chunk, so the capture genuinely STOPPED there; and the snapshot was
excluded from the SPC Query, Class Pivot and Diff pickers alike.

**Two limits, recorded rather than glossed.** The capture came back EMPTY, not partial: the window
is ~150 ms — 4 chunks, because `SnapshotChunkSize >= ScanThreadCount`'s 8192 threshold — and cannot
be entered from outside the process, so the arm must precede the capture and then all 16 workers of
chunk 1 fault. The "some rows survived" variant is **not** exercised and is not claimed. A game with
~1M objects (EVERSPACE 2 with a save loaded) would give ~122 chunks and a window wide enough to arm
mid-capture; that is how to close it. Second, the row's "the armed scan must be the process's FIRST
parallel scan" was inherited from the value-scan rig, where the no-fault-on-a-second-scan effect was
pinned on `ScanForValue`'s index builder; `CaptureSnapshotChunk` has no such reuse, so the
constraint probably does not apply to this path at all.

### Method notes worth keeping

  * **the only `ParallelGObjectsScan` log line is the fault `LOG_ERROR`**, so its ABSENCE says
    nothing about whether a parallel scan has run — a reading that briefly sent this session the
    wrong way. Seven entry points reach that function (`FindInContainers`, `FindInContainersDeep`,
    `FindReferencesToUObject`, `FindPropertyXrefs`, `FindFunctionsByClassParam`, `ScanForValue`,
    `CaptureSnapshotChunk`); the object tree, ListClasses and FindInstances are NOT among them, so
    connecting the UI does not spend a "first scan";
  * `sw1_worker_fault.py` resolves the module BEFORE it starts counting `--delay`, and that
    resolution costs ~4.6 s here — the first L2 attempt therefore armed 6 s after the capture had
    already finished. A rig that must hit a sub-second window has to pre-resolve.

-----

## 2026-09-12 (builds 3508 → 3546) — the fix pass closed, then reviewed against itself, and a flake that was the test all along

184 commits. Every recorded bug row repaired one commit at a time, HIGH → MED → LOW, each one red
before green and each one mutation-checked. Then a 14-agent adversarial review **of that pass**,
whose confirmed findings became four more rows. Then a load-flaky test that turned out to be loose
in precisely the way its own fix commit had claimed to have fixed.

### `[FIXPASS-2026-09-10]` — one row, one commit, forty-five batches

The ledger and the live-check backlog are `docs/todo.md` `[FIXPASS-2026-09-10]`; each row's commit is
`git log --grep <TAG>`. Three of the later batches are worth naming here because they moved a
contract or a wire format:

- **`[W5-OFFSETS-MAILBOX]`** (L45) gave CE Lua a way to ask whether the DynOff offsets were
  MEASURED — `CMD_OFFSETS_VERDICT = 16`, result 1/0 plus the reason in `paramsData`. That is
  `MAILBOX_CONTRACT` **4 → 5**, additive: a new Cmd at an unused number, `MAILBOX_CONTRACT_MIN`
  stays 1, so every saved `.CT` keeps working. The command is the SECOND init-exempt one, because
  its answer *is* the init state — gating it would return `-10` in exactly the case the caller asked
  about.
- **`[A2-TOPTIONAL-STRUCT-DESCENT]`** (L40) and **`[A2-TOPTIONAL-VALUESCAN]`** (L41) stopped Find
  Refs, the Address Finder and Value Scan from reading a reset `TOptional` as live data. UE's
  `MarkUnset` clears the flag and zeroes **nothing**, so the old "an unset slot is zero" comment was
  false in both places.
- **`[A4-AB4-BETWEEN]`** (L42) builds a Between scan's two bounds jointly, clamped per width, in
  both matchers.

### Review 6 — the pass, read adversarially

A read-only 14-agent review of `76f93b94..HEAD` (24 commits, 126 files, +6844 / −631): six area
finders, then one skeptic per finding, each defaulting to REFUTED. **11 raw findings, 8 verified, 4
confirmed, 4 refuted**, and 3 lower-ranked LOWs checked by hand. All four confirmed rows are fixed:

- **`[A2-TOPTIONAL-REFINE]`** (MED) — the lead L41 recorded but did not trace, now traced and closed:
  `RefineCandidates` re-read every candidate by absolute address with no optional gate, so a Next
  Scan with **Unchanged** kept a reset optional's stale value forever. ⚠ The finding's own verifier
  corrected two overstatements, and both are recorded in the row rather than smoothed away.
- **`[A2-SENTINEL-OVERREAD]`** (LOW) — L41's intrusive gate read a flat 16 bytes from a field that is
  `sizeof(T)`; an 8-byte `TOptional<FName>` at a page edge therefore dropped a **set** optional.
- **`[A1-VERDICT-STALEMB]`** (MED) — L45's new CE wrapper cleared the busy flag after a timeout
  without the AA19 stale-mailbox latch, so the next invoke could overwrite a command the DLL still
  owned. Fixed for **both** small wrappers through one shared guard.
- **`[A1-REVIEW6-PINS]`** (LOW) — two decision comments that still claimed one init exemption after
  there were two, and a `%ls` gate with no guard against its own scan matching nothing.

The four refuted findings are recorded with their refutations under `#### ⛔ REFUTED — do not
re-raise (review 6)`, including one whose *test-strength* half stands as a lead even though the
shipped behaviour was proven correct.

### `[TESTFLAKE-2026-09-12]` — the flake was the test, and its own fix commit had said otherwise

`ConcurrentRescores_SettleOnTheNewestMode_NotTheLastToFinish` failed 1 in a 5204-test run and 1 of 3
class-isolated runs. ⭐ `3ad4f524` parked this test's two siblings with `GatedEntryList` and its
message says it parked this one too — **it did not**. Both toggles still fired straight after
`ExecuteAsync`, and since the fake dump service returns `Task.FromResult`, `LoadAsync`'s tail resumes
on a pool thread: the toggle had FOUR landing zones, and in three of them the scenario never formed
(`PendingRescore` null, both `if (… != null)` drains skipped, the assertions proving nothing) while
the single-slot `PendingToggleRescore` orphaned the first re-score, which could still be rebuilding
`Results` under the assertion's own enumeration.

Measured before concluding, because "it passes in isolation" is exactly what got this filed as a
test-suite flake once before and was wrong then: the OLD test passed **15/15 idle and 25/25 under
deliberate CPU contention** — its end state was never wrong, only which zone it hit. The fix enforces
the interleaving with a multi-pass `PhasedEntryList`, releasing the NEWER request first and the OLDER
request's scoring LAST, and pins both re-scores in flight at once. Shown able to fail: deleting the
generation guard reds it in **50 ms** with the original `Assert.DoesNotContain() Failure: Filter
matched in collection`. **22/22** consecutive isolated runs green. No retries anywhere.

⬜ **Recorded, not claimed:** `RescoreAsync`'s generation check is still not atomic with its publish,
and `ApplyFilter` rebuilds `Results` unsynchronised. Avalonia's dispatcher serialises both in the
shipped app, and no seam can suspend a run between its guard and its publish — so that window has
**no reproduction** and was deliberately left unfixed rather than passed off as the cause.

### ⚠ What the mutation step caught that reading did not

Three pins written this session were **vacuous**, and none was caught by review:

- two in L48, asserting strings that also exist elsewhere in the same file — `invokeUFunction`'s
  pre-existing AA19 latch, and my own doc comment — so deleting the code they claimed to pin left
  them green. Fixed by counting occurrences and by asserting the executable line;
- one in L49, where the guard-the-guard added *against* a vacuous scan was itself cleared by half the
  scan: `scannedFiles >= 25` is met by `dll/src`'s ~31 `.cpp` files alone once `.h` is dropped.

Each was found by a mutant SURVIVING. That is the mutation step earning its cost, and the reason a
"red: 0" line is never to be waved through.

### Evidence

- UI suite **5298 / 0 failed**; `dll_core_test` **332 checks / 0**; `dll_helpers_test` **2752 / 0**;
  gates **22 run, 0 failed** (final close-out, batch L49).
- `build.ps1 -NoBumpBuildNumber` SUCCESS, then `-Mode Publish -NoBumpBuildNumber` SUCCESS.
  `dist\UE5DumpUI.exe` **55.0 MB, sha256 `e278e02a`** — AOT-trimmed, which is the binary to hand
  over. (Before: 54.8 MB / `628552a1`.)
- ⬜ **Live checks are DEFERRED by the maintainer's direction**, not skipped: the backlog is
  `[FIXPASS-2026-09-10]` rows L2…L89 in `todo.md`, and **nothing above is closed until its check
  passes on a running game**. Several need Cheat Engine, which is announced before use.

## 2026-09-09 (builds 3462 → 3508) — a delegate layout that only holds in Shipping, nine register rows driven to a verdict, and 166 claims sliced down to 30 hand-reads

43 commits. Two engine versions of UE4 exercised for the first time, a wire field that never left
the process, six more defects out of a population three agent sweeps had produced but not
adjudicated — and one correction of my own claim, which had been refuted the day before.

### `[D4B-DELEGATEPAD-2026-09-09]` — the delegate readers assumed a layout only a Shipping build has

`FScriptDelegate` / `FMulticastScriptDelegate` carry an **access detector** in non-Shipping builds,
so every delegate read was off by a pad the code never accounted for. The fingerprint is nasty
because it is *not* a crash: on a Development build the reads land on neighbouring bytes and
publish plausible-looking bindings. `D3b` turned out to be the same root cause.

⭐ **Surveyed rather than patched to the fixture.** The pad derivation was re-run on a **second
engine version**, then on a **real title** — Titan Quest II, **279,587 objects**
(`[D4B-PADSURVEY-TQ2-2026-09-09]`). ⛔ Two things that run corrected are written down in the row;
read them before quoting the numbers.

**A sixth site, and then a seventh defect underneath it.** `Ubel::ReadDelegateArrayElements`
carried **all three** of this sweep's shapes at once, and the fixture built to prove the sixth
immediately surfaced a seventh (`[D4B-SITE6-2026-09-09]`). Everything was re-run against HEAD's
binary afterwards, which also un-staled three rows.

**`(stale)` — five sites, one definition.** `Ubel::DescribeScriptDelegate` now owns the string that
five call sites had each spelled for themselves (`[STALE-NONE-2026-09-09]`).

### `[D4B-UE4-2026-09-09]` — the UProperty path, finally exercised

A portable delegate fixture (`tools/ue-sample/ue4-delegate-fixture/`) packaged and injected on
**UE 4.23 and 4.27**. ⚠ It was re-run in **Shipping** because the first UE4 pass had the population
backwards — the same mistake that later became a house rule this same day. ⛔ **4.15 and 4.18
cannot build on this machine and the reason has no override**: it is the Windows SDK, not
permissions. Three UE4 blockers are recorded, all measured, two still open.

### `[D4B-TAIL-2026-09-09]` / `[RECON-REGISTER-2026-09-09]` — the reconciliation

`git log -- docs/verification-register.md` returned **nothing** for this entire work stream while
todo.md said the live rows belonged there. Nine `SW` rows written; four already-closed names
removed from the long-tail heading (the sweep had found two and written *"fix when next editing the
register"*, then never edited it — the file had four). Every row names the observable on **both**
sides, per the register's own charter.

⛔ **Then all nine were driven to a verdict in one stream, and none was blocked** — against my own
written prediction that several were unreachable. **Five of my own claims were wrong.** SW1 (a
faulted worker yields a *partial* result — 1,649 of 4,115, not zero), SW2 (a failed clipboard
delivery, proven from both sides *and* from outside the process), SW3 (`onUnreadable` in real Cheat
Engine — a dead process, then the busy-mailbox half), SW4 (CE resolves a delegate record to the
InvocationList, not to address 0), SW5 (the AMBER and RED push branches produced for the first
time), SW6 (both ARRAY refusal arms, manufactured at ElementSize 32), SW8 (`GetMapPairLayout` does
**not** refuse on a real title — TQ2, 1,685 map leaves), SW9 (a delegate BINDING read on 4.27 and
4.23, off an actor the engine spawned).

### ⛔ `[SW7]` found a defect nothing else could have: a wire field that never left the process

A scalar `DelegateProperty`'s `delegate_pad` was computed correctly and then **never serialised**,
so the UI and every CE path built chains without it. Fixed on the wire (`486c70a5`), and Live
Walker's CE paths now carry it (`[CEPATHS-UNPADDED-2026-09-09]`) — as a **payload** address,
distinct from the field address.

⚠ **And the rig itself was the bug once**: `l12` sent `addr=` where `Fern` reads `instance_addr`,
so 2,000 requests measured **nothing** and reported cleanly (`7186f1c8`). That is the same
send/receive shape as SW7, on our own side of the pipe.

### Five more defects the rigs found while measuring something else (`e16d2052`)

Two verified live, three code-level. All one shape: **a failure that happened correctly and said
nothing, or said the wrong thing.** [A] the delegate-array refusal reached the UI as *nothing* —
an `ArrayProperty` never sets `typedValue`, so a refused array was indistinguishable from an empty
one. [B] the two scalar stride refusals emitted no log line at all. [C] `Aura`'s worker-fault line
claimed *"results are partial"* — a claim about the whole scan one worker cannot make, and false in
the commonest case (`parallel=false`: one chunk, `total=0`, `scanned_objects=0`). [D]
`[TMAPGEOM-TWIN]` — both inlined copies of the map geometry guessed silently where
`GetMapPairLayout` refuses; behaviour deliberately unchanged, the silence was the defect.

`[TMAPGEOM-2026-09-09]` itself is the fixture that looked impossible: a faulted `Struct` read,
manufactured at a **page edge** in our own process, with the control and the number the mutation
produced both recorded.

### `[CLAIMS-SLICE-2026-09-09]` — the ~166 unadjudicated claims, sliced and closed

⛔ **First, what they are not: a list.** Three agent sweeps filed **209 claims and confirmed 23**;
what remained was a *population*, not a queue. Sliced four ways and adjudicated by hand:

| slice | outcome |
|---|---|
| **A** CE Lua emission layer | 8 read → **1 defect**, fixed and live-verified (`[TG1-CLEARALL-RESULT]`, in real CE) |
| **B** discarded effect-appliers | 7 read → **2 defects** + 1 hygiene; the proposed gate re-scored and still refused |
| **C** discarded `Read*Safe` | 15 read → **3 defects**, two manufactured fixtures (`[IFACEREAD]`, `[UNREADVAL]`) |
| **D** the 30-item tail | **closed by decision** — a round 4 the sweep forbade, measured yield 0 |

**30 sites read by hand → 6 defects and 1 hygiene fix.** ⚠ That is not a claim that reading beats
sweeping: the sweeps produced the populations these slices walked. The narrower claim the rounds
themselves kept making is that **the candidate rows were worthless and the populations were not.**

- **Slice C** — three walker handlers (`EnumProperty`, `ByteProperty`-with-`UEnum`,
  `OptionalProperty`) published an un-set 0 dressed as a value; now one shared refusal formatter,
  `Ubel::DescribeUnreadableField`. `[IFACEREAD]`'s manufactured fixture **found a defect in the
  fix** before it shipped.
- **Slice B** — both `MovementMode` writes in `Dunste::SetEnabled` dropped `Macht::WriteBytes`'
  result, so ENABLE logged *"Fly: ENABLED"* over a pawn that never left its old mode and DISABLE
  logged *"Fly: DISABLED"* over a pawn still in `MOVE_Flying` with nothing tracking it. Both UI
  status lines were the same lie in C#.
- ⚠ **CORRECTION, same day**: `Wirbel.cpp:618` was **refuted in round 2** and I re-raised it as a
  defect without checking the refuted list. The refutation was re-derived and found sound; the
  change was kept as **hygiene**, slice B's count corrected 3 → 2, and the process lesson written
  as working-lessons §1.w2.

**Live arms on DumperTest, on Shipping *and* Development**: `unreadval_live_arm.py` walked **2,391
fields across 2,105 live instances with not one refusal**, 1,466 of them genuinely reading 0 — the
state the refusal must never be confused with. `sliceb_fly_arm.py` is 10/10 across two full
enable/disable cycles with three independently-computed witnesses.

⛔ **And one measured null result, shipped as evidence.** The `FR_ERR_WRITE` arm is **unreachable
from outside the process** — `sliceb_fly_fail_probe.py` is the probe that says so. ⭐⭐ It also
reproduces `[CLASSCACHE-FRONTED-2026-09-09]` from a second, unrelated experiment: `FindField` →
`WalkClass` is memoised in a 2048-entry LRU that nothing invalidates, so a patched `FField` is
invisible to both `Dunste` and the walker. That open row now blocks **four** verification arms, not
two. ⚠ The probe's own sanity guard is what kept it safe: `FField+0x4C` read **675**, not 513 —
`Grimoire.h`'s `FPROPERTY_OFFSET` is a *compile-time default* and Genau derives the real one per
title (68 here). Without the guard that was a wild 4-byte write into a live game object.

### 📐 Rules and lessons this day added

- ⭐ **SHIPPING is the DumperTest fixture; Development is the second opinion** — a Development
  build's offsets are a *minority shape in real games*, so a row closed on `dev` alone is a
  narrower claim and must say so. `docs/handover-2026-08-22.md` §4 rule 5, and
  `launch_dumpertest.py`'s docstring. Both live arms above were re-run on Shipping for this.
- working-lessons **§1.w2** (a mechanical scanner surfaces *refuted* rows as fresh hits — check the
  refuted list before raising), **§1.v** (targeted **and** broad sampling: the first UNREADVAL run
  strided the pool and found **zero** `OptionalProperty`, because two of 25,231 objects declare
  one), **§1.v2** (a valueless field on a metaobject is a *branch*, not a regression — read the
  ratio; 77 of 96 fields across fourteen type families, of which the fix touched three).

-----

## 2026-09-08 (builds 3423 → 3457) — audit #3 re-checked and deliberately NOT re-run, a sweep that finished by grep, and 29 copies that claimed a write which did nothing

20 commits. The question was whether audit #3 — the only audit on this repo not produced by Opus 5,
and its own doc never records that — should be re-run. **The answer is no, and the measurement is
why.**

### 🔎 audit #3 re-check — 23 of 23 fixes still live, 0 reopens, and 5 confirmed misses

Re-checked by a 12-agent workflow (4 evidence + 2 miss-hunts + 6 **refute-mandated** skeptics),
every `file:line` re-read at HEAD because audit-doc line numbers have drifted hard.

- **23 of 23 shipped fixes are still live**, across ~1,250 builds. **All 9 dropped items re-derived
  → 0 reopens.** ⭐ Fable 5's adversarial judgement was **sound** — the refutations were right. The
  gap is in *coverage*, not in reasoning.
- **5 confirmed misses** (1 further claim refuted), and they share one shape: three of them were
  **inside functions audit #3 itself rewrote that day**. The 🔴 headline — `Schlacht::Tick` recorded
  **intent, not effect**; See-through was a no-op on non-Actors while `hiddenCount`, the pipe's
  `hidden_count` and the UI status string all reported success — was found **six weeks later** by
  `[SEETHRUTALLY]`.
- ⭐ **The audit's own cross-cutting root cause rested on a false premise** (`H1` asserted in writing
  that an unexpected pipe death is safe, falsified by two catch filters 30 lines away), and the
  shared helper it recommended was **never built** — so the same shape was re-found later as `AC10`.

### 📐 The structural coverage gap, and no later audit touches it

audit #3's window (`88ee170..af2ce50`, 2026-07-03..07-15) has **22,853 lines still alive at HEAD,
of which 100% have never been re-swept at line level by any later audit** — tautological under
`git blame`, since audit #4's baseline *is* audit #3's endpoint. ⛔ **Hard core: 80 files / 4,648
lines unchanged since `af2ce50` and cited by no later audit document at all.** `dll/src/Grausam.cpp`
is 279/279 lines from that window, byte-identical since, and appears in audit #5 only as a
*precedent* and inside a refutation. The derivation recipe is in todo.md — derive it, never quote it.

⚠ **Verification asymmetry**: 13 of 23 fixes have a real headless rig; **10 have a unit test only
and appear nowhere in `verification-register.md`** — all UI-side, `H1` included, which violates the
register's own single-owner rule.

### 🔎 Blind-spot sweep, three rounds — 23 confirmed, 20 refuted, and it ended by grep

A **pattern** sweep for the named blind spot, not an area audit: a function returning `bool`/an
error code whose call sites drop it, and any count/status/log reported from *intent* rather than
from re-reading the effect.

- ⛔ **The instrument was 76% blind in round 1, and the negative control caught it every time** — it
  was wrong three times. Round 2's *"68 Services files / 24,651 lines"* framing was the **wrong
  predictor**, and cost nothing only because it was checked before being spent.
- ⭐ **The load-bearing finding is why two families in the same layer diverge 51% → 0%.**
  `IAobMakerBridge` callers were written *against a fallback*, so the bool **had** to be tested to
  pick a branch — 42/55 consumed, **0 defects**. `CopyToClipboardAsync` is the **last link with
  nothing to fall back to**, so testing it buys the author only an honest message — **55 of 57
  dropped, 29 defects.** Line count predicted nothing.
- ⛔ **The sweep is finished, and that is one grep, not a judgement**: all 17 `bool`-returning
  methods on every `Core/I*.cs` interface were enumerated and every call site of the other nine
  checked. Outside the two families the entire C# population is **1 site, 0 defects**. There is no
  third family. **Do not run a round 4.**
- ⚠ Three independent counts gave three different answers (56/17, 57/53, **57/55**); only the
  clipboard number survived. That is audit #4's B34 lesson landing again.

### ✅ Fixed: 14 delivery copies, 5 DLL rows, and the CE Lua idle wait

**C# side** — all 14 *delivery* copies routed through a shared `Helpers/ClipboardDelivery`, in three
batches; `RequestCopyText` became `Func<string,Task<bool>>` so four cross-file claims stop lying.

**DLL side** — fixes were **derived and adversarially checked before being written**, because this
repo's history says a verdict is not authority on the repair. All five came back
`sound-with-corrections`; none was an AB4.

| row | what it stopped claiming |
|---|---|
| **D1** `Dunste.cpp` 🟠 | `InvokeSetCollision` returned *"the setter was FOUND"*; three call sites read it as *"collision CHANGED"* |
| **D2** `Aura.cpp` | a scan worker that **threw** let the run report **complete** |
| **D3+D5** `Ubel.cpp` | an unreadable delegate array published the affirmative `"(0 bindings)"`; a faulted `FGuid` published a fabricated all-zero GUID |
| **D4** `Aura.cpp` | a sparse delegate whose `bIsBound` byte **read 1** reported `"(0 bindings, sparse)"` |
| CE Lua | Invoke's idle wait reported a **dead game process** as a **busy mailbox** |

⭐ **D1 was bigger than filed**: the dropped `int32_t` re-opened the hole audit #4 **B8** was written
to close — a refused restore left the pawn ghosted *and* wiped the record that would have started
`PendingRestoreLoop`. It reaches that through the **dispatcher**, which is why B8 did not cover it;
hence a tri-state `CollisionApply` rather than a bool. ⭐ **D4 found a fourth site nobody had
filed** — `FindReferencesToUObject`'s sparse pass `continue`s on a fault, so the binding is silently
**absent** from Find Refs, and an absence is a *stronger* wrong claim than a wrong count.

**Two gates shipped**, both on the rule that earns them — *pick a predicate whose legitimate
population is EMPTY*: `check_ce_untick_placement` (17a) and `check_clipboard_delivery` (17b).
⛔ Gate #17 **as proposed in round 1 was refuted**; 17b as first scoped was refuted too and shipped
narrower. ⚠ **Three gate bugs were caught by their own negative controls, each after the check was
already green** (working-lessons §1.2a).

### ⬜ Filed, not fixed — the Fly report publishes the WISH, not the FACT

Found by D1's checker and deliberately left out of the fix, because it is a pipe-contract + UI
change: `Fern.cpp` publishes `data["noclip"]` from **what was asked for**, and the fact reaches no
channel at all. ⚠ `collisionOff` cannot simply be published as the fact — B8 gives it *"intended,
and retrying cannot help"* semantics, so publishing it would ship a **new** lie; it needs a
tri-state. ⭐ `FlyStatus.Noclip` is **already dead on the C# side**: `ApplyFlyReadout` never reads
it and the `✈ Fly ON (noclip)` badge is built from the local checkbox — **it never asks the DLL.**

Live verification round 1 ran the same evening on DumperTest (UE 5.4, DLL 3457): **25,229 objects**,
offsets `validated: true` — checked before anything was believed, because a dead engine reports
coherent zeros. D5 and D3 confirmed; D4's fix fires and surfaced a real defect underneath it.

-----

## 2026-09-07 (build 3423) — a cancel that crossed connections, a Guess? that invented rows, and a queue that lied

38 commits. Four shipped defects, two verification rows closed on real hosts, one fixture that had
never worked, and a queue audit whose most useful output was proving my own hypothesis wrong.

### `[MULTIPIPE-CANCEL-2026-09-07]` — a foreign client's death truncated your scan and said `ok:true`

todo.md recorded multipipe-eval §9.6 item 5 as closed. The evidence on file described the **opposite
experiment**: the item asks for "disconnect only ONE lane, the other keeps working", and the run
logged had closed the whole GAME mid-snapshot, so both lanes died together. §9.3's mitigation
(`inFlightHeavy`) had never shipped — `Fern::MonitorLoop` tripped the process-wide
`Tot::g_perCommand` for **any** broken in-flight connection.

Measured (`tools/verify/multipipe_cancel_isolation.py`): 5,157 of 5,264 replies collapsed to **77
bytes against a 720,793-byte baseline**, every one reporting `ok: true`. ⛔ The severity was never
the truncation — it was that a caller could not tell it from a complete answer.

Fixed in three parts: the loss is now **flagged** (`truncated: true`, joining a convention 10 other
commands already used), pipe handlers got a **per-connection** cancel, and Aura's **parallel
workers** — which do the actual work on any real game — inherit their caller's cancellation context.

⭐ **Verified with a true before/after on ONE real host.** Octopath Traveler (UE 4.18, 406,060
objects) still carried a build-3380 proxy from an older session, so the pre-fix state was directly
measurable: **10 of 125 replies truncated to 238 bytes before, 0 of 115 across three runs after**,
with `client gone mid-command` logged exactly once in *both* directions — so the AFTER is a real
negative, not a missed trigger.

⛔ **The obvious design was wrong and is documented as rejected in `Tot.h`.**
`return (t_connCancel && t_connCancel->load()) || g_shutdown` makes *unbound* identical to
*cancel-immune*, inverting the module's default from fail-safe to **fail-silent**. Three of four
review lenses returned FATAL on it before a line was written.

### Guess? invented a phantom row over every static C-array element

The row said "RESOLVED — working as designed". It was resolved for *one* footprint family: its
evidence was a TArray + TMap, and for a **dynamic** container `ElementSize` is the entire inline
footprint, so the defect is structurally invisible there. A static `UPROPERTY Type Foo[N]` renders
as ONE element, so elements 1..N-1 looked unclaimed. Measured on the fixture's `int32 FixedArr[8]`:
**7 fake rows before, 0 after**, 34 legitimate guessed rows still emitted elsewhere.
⛔ A comment had asserted the bug was fine ("its output is unchanged by the new ArrayDim field").
The fix was a **deletion** — `ComputeClassHoles` already had the right formula.

### The FProperty family had a THIRD writer, and it split the family

`Grimoire.h` says "Never assign a member of this family directly"; audit #5 G12 unified the writers
and recorded "Both writers now go through here". There were three. The failure is silent and
half-correct: struct reads stay right while TArray element descriptors and every enum name read 8
bytes off. Now gated (`tools/check_property_family.py`, 16th gate) because a prose invariant plus a
hand-counted writer list is exactly what failed here twice.

### `[MHPOISON-STARVE-2026-09-07]` — the retry machinery had never run on a live game

Build 2358's fix shipped; its in-game re-check never happened, because the hook kept installing on
the first try. ⛔ And the fixture built to force a failure **was itself broken in a way that read as
success** — it reserved 1 MB at a time stepping 1 MB, and `VirtualAlloc(MEM_RESERVE)` fails for the
*whole* request on any overlap, so it reported "reserved 3824 block(s)" (93%, looks fine) while
leaving ~960 KB free in each of 272 gaps. MinHook needs a few KB. Now walks the window with
`VirtualQuery`: **2 blocks instead of 3,824, and the count going down is the fix.**

All four acceptance criteria then landed in one run: six retries at the configured 5 s cooldown
(not one — the permanent latch is gone), **one** fallback WARN for 61 fallback invokes (against a
historical 552 unsafe-call lines in 19 s), See-through refusing with `hook_active:false` in its
reply, and `hook RECOVERED on attempt 7`.

### `[G2-TIER1-UE5-2026-09-07]` — the first UE5 Tier-1 detection on this machine

The register's own sweep found 6 Tier-1 lines across every archived scan log and **all were UE4**.
Two `.rc` fixtures (5.4 → `1.2`, 5.8 → `1.8`) make the packaged samples fall through Tier 0 while
keeping their `++UE5+Release-N.N` needle. Both engines now produce a live `Tier 1` line — and they
report **different** dummy versions deliberately, so a constant could not fake it.

### Smaller, but user-visible

* **dxgi's "late-load games only" restriction was lifted three days after it was written, and
  shipping C# never heard.** `ProxyDeployViewModel.cs` still steered users away from a proxy that
  works, as the documented selection rule for the Proxy Deploy radio buttons.
* **A cancelled bulk reply no longer claims to be a complete one** (`truncated: true` on
  `list_enums` / `walk_instance_batch` / `pe_profile_get`).
* **A real data race**: `FindSparseDelegateStorage` is the one scan reachable from pipe-command
  threads, so two clients could enter its slow path and both write the same file-static
  `ScanReport` — which owns a `std::vector`. Now serialised. ⛔ Not `call_once`: MA1 deliberately
  does not latch a cancelled scan.
* **Native-C P3's SPC-Query arm** closed offline against the 2026-09-06 capture still on disk — all
  8,556 `<raw@0x..>` rows join under the product's Strict key, none with a vacuous offset.
* **The `0x100000` ceilings** split five ways by tracing each value's producing read and consuming
  arithmetic.

### ⚠ What this session got wrong, because the pattern repeats

* **A todo audit over 12 open bullets expected to find stale "already done" rows** — 5 of 12 had
  bodies contradicting their headers, so the hypothesis felt safe. Two adversarial refuters per
  verdict, told to default to "still open", **demoted every single one**. Zero were stale. Had the
  first pass been trusted, the row containing a live bug would have been deleted.
* **Two proposed fixes in todo.md were both wrong** (the DynOff-atomics row): dropping the
  "redundant" write would have removed the only probe that can land a ±0x10 layout, and making the
  offsets atomic would have fixed the race and left the actual bug.
* **I deleted a bullet and left a header stale** — a patch anchored on a line it did not mean to
  remove, and a header still reading "REOPENED with a REPRODUCED DEFECT" hours after it was fixed.
  Both found by re-scanning the queue mechanically instead of answering from memory.
* **A rig produced a clean, plausible, meaningless green** at 2 FPS: the arithmetic said the window
  was wide enough, and it wasn't. Rigs now treat "trigger never observed" as INCONCLUSIVE, never a
  pass.

-----

## 2026-09-06 - RELEASED as v3397 -- the first release since v3362, 97 commits

Tag `v3397` on `1b55cce3`, published from the draft the release workflow builds. **This entry is a
POINTER, not a summary**: every fix it covers already has its own entry below, and a second copy
would go stale independently -- which is exactly what `roadmap.md`'s banner describes happening to
it.

**[v3362...v3397](https://github.com/bbfox0703/UE5CEDumper/compare/v3362...v3397)** -- 97 commits,
33 DLL/UI files, +1755/-415.

### What the release notes say, and what they deliberately do NOT

The notes are written for someone who only uses the tool: ten one-line bullets, no internals. Two
things were CUT after review, and the reasoning is worth keeping because it applies to every future
release:

  * ⛔ **Documentation and tooling changes are not release notes.** A user does not care that a
    filename's case changed.
  * ⛔ **A defect introduced AND fixed inside the release window never shipped, so it is not a fix.**
    The `_wfopen_s` logging regression (introduced build 3370, fixed 3371) never reached a user --
    `git show v3362:dll/src/Sein.cpp` still had the correct `_wfopen`. The proxy SRWLOCK deadlock is
    the same shape: introduced by the 3363 crash fix, gone by 3365, so the two commits are ONE
    user-visible item ("the dxgi proxy used to stop some games launching").

⚠ The test for this is not "is the commit newer than the tag" -- that is true of every commit in the
window, and conflating a FIX commit with an INTRODUCING one gives the wrong answer both ways. Read
the code at the old tag: `git show v3362:<file>`.

### Verify-then-publish

`release.yml` builds a DRAFT deliberately so the artifact can be checked before anything is public.
Done, and worth doing every time:

    sha256   recorded == actual                    615b3d6c...df38
    zip      16 entries
    UE5DumpUI.exe   54.7 MB  <- the AOT-TRIMMED build, not the ~107 MB one
    build_number.txt embeds 3397 == the tag

⭐ That 54.7 MB check is the one that matters. CLAUDE.md records that `-Target Test` / `-Target UI`
silently replace `dist/UE5DumpUI.exe` with the non-trimmed binary, and shipping that would hand
users a build in which every AOT/trimming defect is invisible.

-----

## 2026-09-06 - CrashReportClient.exe becomes a second version source, and a file-hash allow-list was measured to reject everything (build 3393)

`Genau::kVersionDetectLogicRev` **6 -> 7**. Evidence:
[`evidence/2026-09/crc-version-source/`](evidence/2026-09/crc-version-source/), claim tag
`[CRCSOURCE-2026-09-06]`. Oracle: `tools/ue-crc-oracle.json`.

### Why a second source at all

`CrashReportClient.exe` ships **with the engine** and is never authored by the game team, so unlike
the game exe its version can never be the GAME's version -- and the game exe's field frequently is
(measured: OCTOPATH 1.0, DQ7R 1.1, Elliot 1.2, Titan Quest II 63339.64744).

⭐ Its value is not the hit rate but WHERE it hits. DQ I&II HD-2D carried a misdetected `UE5.05` in
this repo's own `docs/test-games.md` for months -- an error **across the UE4/UE5 boundary** -- and
its CrashReportClient says `4.27.2.0`. P3R (a 4.27 fork) is the same shape. Those are exactly the
titles where the memory-string tiers struggle.

⭐ And it is INDEPENDENT, which is the real argument (working-lessons.md 1.4). Both poisoned
hint-cache entries this repo has found -- Avowed and DragonSword, each holding a persisted runtime
raise of 504 -- would have been caught by it: CrashReportClient reports 503 for both, agreeing with
detection and disagreeing with the cache.

### What was measured and REFUSED

The proposal included a conservative form: accept a game's CRC only when its version **and file
hash** match a known-official binary. Measured over 8 installed Editors and 66 game folders, that
rule **accepts nothing** -- 0 of 8 game copies match an Editor copy, because CrashReportClient is
rebuilt as part of each game's engine build. The version string is stock; the bytes are not:

    4.27.2.0   Editor 19,469,792 B   DQ I&II 19,496,448 B   P3R 19,487,232 B
    5.7.4.0    Editor 26,711,480 B   TQ2     26,725,376 B

So `tools/ue-crc-oracle.json` pins the ProductVersion -> code ARITHMETIC against 8 real Epic
binaries (4.11-5.8, all mapping cleanly) and is explicitly **not** an allow-list.

⚠ Coverage is a per-machine sample, not a rate: 8 of 66 folders here ship one. Avowed, DQ XI S,
OCTOPATH and DumperTest ship none, so "absent -> skip" is the common path and a first-class branch.

### Priority, and the question it answers

CrashReportClient is a **build-provenance** signal, so it ranks with the static detectors and NOT
above the runtime ladder in `Frieren.cpp`. Measured on DragonSword: CRC 5.3.2.0 -> 503, detection
503, and `CMC::GravityDirection` then raises to 504 at init. That is not a conflict -- the two
answer different questions ("built from which engine" vs "has which engine features"), and a
licensee fork backports features. The ladder keeps the last word.

Where both resource sources exist and disagree, CRC wins and a WARN says so -- the disagreement is
itself the finding.

### Implementation notes

`DetectVersionFromPEResource` was split into `ReadUeVersionFromFile(path, productLabel, fileLabel)`
so both files are parsed by the SAME code; a second reader with its own copy of the parse and its
own bounds is the defect shape 1.12 catalogues. The version arithmetic -- written out **four times**
in that one function -- is now `Grimoire::UeVersionCode` once, with the 4.0-4.27 / 5.0-5.9 bounds
that reject a game's own version.

⚠ Every existing log line is preserved BYTE-FOR-BYTE, including the UE4 branch's different wording:
`tools/verify/sweep_title.py` greps the literal `PE VERSIONINFO` and a committed evidence README
quotes the UE5 line verbatim.

Rev 7 is mandatory under `Genau.h`'s own rule: a title cached under rev 6 holds a verdict reached
without this source and would never consult it.

Tests: `dll_helpers_test` 2337 -> **2365** (+28) -- the version-code bounds, including every real
game-version literal that must be rejected and every oracle entry that must map, plus the
walk-up path derivation. Live: both branches on a manufactured fixture, the DISAGREE run being the
negative control that proves the code runs at all.

-----

## 2026-09-06 - the version cache held a value detection cannot produce: `kVersionDetectLogicRev` 5 -> 6 (build 3390, measured live on DumperTest)

`Genau::kVersionDetectLogicRev` **5 -> 6**. Evidence:
[`evidence/2026-09/revbump6-cache-restamp/`](evidence/2026-09/revbump6-cache-restamp/),
claim tag `[REVBUMP6-2026-09-06]`.

### The first bump that is NOT a logic change

Every previous bump followed `Genau.h`'s own rule -- *the detection logic changed, so re-derive
every cached verdict under it*. Rev 6 does not: `DetectVersionDetailed` is byte-identical. What
changed is that a cached **value** was found which the current detection cannot produce.

Measured while settling Avowed's row: its record held `ueVersion=504`, and a forced fresh
detection re-stamped it **503**. 504 for that binary is the runtime `CMC::GravityDirection`
raise (`Frieren.cpp`) -- and that ladder runs in `UE5_Init`, **after** `Flamme::SaveResults` at
the end of `FindAll`, so today it cannot write back. The 504 is residue of a write path that no
longer exists.

⭐ **And nothing would ever have re-derived it.** The cache-reuse branch skips detection entirely
for any record stamped with the current rev, *however wrong the value is*. The stamp is not one of
several ways to reach a poisoned entry -- it is the only one. So `Genau.h`'s bump rule gained a
second clause: **also bump when a cached VALUE is found that the current detection cannot
produce.** The rule as written covered the logic and said nothing about the data.

### What it costs, and what it does not

Live on DumperTest Development (cached 504 / rev 5):

```
FindAll: Cached version 504 was stamped by logic rev 5 (current 6) - re-detecting once and re-stamping.
DetectVersion: PE VERSIONINFO -> UE 5.4 -> 504
FindAll: UE Version = 504 (tier=1, detected=yes, lowConfidence=no, publisher=-)
```

The invalidation fires and the verdict is **unchanged** -- which is the point. Cost on this binary
was **2 ms**, not rev 4's ~0.35 s; that older figure is the memory string-scan path, which a Tier-1
VERSIONINFO hit never reaches. A stripped title (SquareEnix) still pays the scan. Locally this
re-derives 44 cached records once each; Avowed re-detects 503 and is then raised to 504 at runtime
exactly as before, so its badge does not move.

⚠ **This does not overturn the A4 decision.** `audit-2026-09-05-vendor-ue582.md` and
`verification-register.md` both say *do not bump for the 507/508 ladder*, and that still holds --
the ladder is runtime-only and re-applied on every init, so it needs no invalidation. Rev 6 is for
the residue of an older write path, not for the ladder.

-----

## 2026-09-03 - FName index 1 is INTERIOR to "None": a probe that could never succeed, warning on every session (build 3368, verified live on DumperTest; re-verified 3369)

Two fixes, both found by asking why a log line said something odd, and neither was the thing that
was actually asked about.

### 1. The FNamePool `BlockOffsetBits` WARN was a false alarm BY CONSTRUCTION

`FNamePool: BlockOffsetBits = 16 (stock default kept; index 1 did NOT decode cleanly at this
width — name resolution may be degraded)` had fired on **6 of 6 sessions** since audit #5 G4
introduced it, across **four games and three engine versions** (ES2 UE5.06, Elliot UE5.04, P3R
UE4.27 fork). It has never once been about the game it fired on. `FName[1]=''` is in **every log
this repo has ever produced**, including the pre-G4 ones that did not probe at all.

**An FName index is a stride-quantised BYTE OFFSET inside its block, not an entry ordinal:**

```
vendor .../Core/Private/UObject/UnrealNames.cpp:592
    return FNameEntryHandle(CurrentBlock, ByteOffset / Stride);
:679  return *reinterpret_cast<FNameEntry*>(Blocks[Block] + Stride * Offset);   // no validity check
:518  enum { Stride = alignof(FNameEntry) };
:253  static constexpr uint32 FNameBlockOffsetBits = 16;   // compile-time, no #ifndef
UnrealNames.inl:12  REGISTER_NAME(0,None)
```

Entry 0 is always `"None"` and occupies `prefix + 4` bytes — always **more than one stride unit**.
So index 1 addresses byte 2 of block 0: the `'N','o'` **inside the "None" string**. Not inference —
our own `ValidateGNames` DEBUG dump had been printing the proof all along, byte-identical on five
games across two engine generations:

```
1E 01 | 4E 6F 6E 65 | 10 03 | 42 79 74 65 50 72 6F 70 65 72 74 79 | C0 02 | 49 6E 74…
hdr=4 |  N  o  n  e | hdr=12|  B  y  t  e  P  r  o  p  e  r  t  y | hdr=11|  I  n  t
idx 0 --------------- idx 3 --------------------------------------- idx 10 ----------
```

The probe read `0x6F4E` as a header: lenA `(0x6F4E>>6)&0x3FF` = 445, lenB `(0x6F4E>>1)&0x7FF` =
1959, both outside the accepted `1..256` → `len = 0` → WARN, unconditionally, forever. The
hash-prefixed layout fails identically (byte 4 → `'n','e'` → 405 / 695). **In both stock layouts
the first valid non-zero index is 3.**

**DELETED, not moved to index 3** — that is the tempting fix and it is wrong.
`BlockBitsAreIndistinguishable(3, 16, 14, 2)` is still **true**, so index 3 measures the width no
better than index 1 did; it would merely start *succeeding*, printing a confirmation beside a
number nobody measured. That is precisely what G4 removed. A regression guard pins it. G4's
conclusion stands — there is no reliable offline discriminator — so the width is now logged as the
engine constant it is.

**The second copy is why this was a code change and not a one-line deletion.** `Init` and
`InitObfuscated` each hand-wrote the *same* wrong index in their verification line — that is where
`FName[1]=''` came from — and fixing only the WARN would have left the defect visible in the line
directly below it (working-lessons §2.17). Both now call one helper sampling the **derived** first
boundary, `Serie::FirstEntryAfterNone(noneOffset, stride)`; `DetectStride` already computed
`noneOffset` and threw it away. `GetString(0)` is dropped from the line too — it short-circuits
`nameIndex <= 0` to the literal `"None"` without touching memory, so it verified nothing.

⚠ **Derived, never a literal 3.** Both stock layouts give 3, which is exactly what makes a
hardcoded 3 look safe — but MindsEye's obfuscated fork inserts a 2-byte tag (`payloadGap = 2`),
giving **4**. A literal there would be this bug again with a new number. That is the load-bearing
test. `InitUE4` is deliberately untouched: in `TNameEntryArray` mode an index *is* a pointer-array
ordinal, so its index 1 is a genuine second entry — said so in the header so nobody "fixes" it.

**Name resolution was never degraded, and the logs said so one millisecond later**:
`UE5_Init: Name sanity: 10/10 objects resolved` on 9 of 9 FNamePool sessions, 38 whole-pool
censuses with 0 `named`/`nonNull` mismatches. Three direct 16-bit proofs the WARN made everyone
doubt — DQ7R resolved `/Script/Engine` at `idx=387225`, Avowed at `idx=24315`, DumperTest at
`idx=200623`, all far above 2¹⁴ and impossible to decode correctly at 14 bits.

**Verified live on DumperTest (UE504 Development config, 25,213 objects), TWICE — build 3368
(`bdb1f613`) and again after a clean recompile at build 3369 (`cbe02dfd`), byte-identical results:**
`BlockOffsetBits = 16 (… not detectable offline — audit #5 G4)` as **INFO**, **zero** FNAM
WARN/ERROR in either session, and `FName[3]='ByteProperty'` — the derived boundary resolving
in-process to exactly what the block-0 hex predicted. Name health `Loaded 25,213 named` of
25,213 = **100%**, `scanned=25213, nonNull=25213`, 154 `WalkClass:` lines with **0** empty names
(game-specific `BP_ThirdPersonCharacter_C` included). The only WARNs in either run are the ordinary
AOB candidate-rejection chatter (`ValidateGNames` / `ValidateGObjects` refusing bad candidates, and
the cross-module guard refusing `EOSSDK-Win64-Shipping.dll`). These runs also exercised the
`UE5-Extended` FUObjectItem layout at ItemSize 32 — a different one from the `Default`/24 the
reporting session used — and the second run's scan log is 12.7 KB against the first's 357 KB
because the hint cache hit (`GOBJ_ES53_1` / `GNAM_V1` / `GWLD_TQ_1`).

⚠ **Correction to this entry's first draft, because the wrong version would teach a future reader
to distrust every test binary.** It said the `-dirty` build stamp meant the fix was uncommitted and
"not reproducible from a clean tag". That is wrong. **The `-dirty` suffix is STRUCTURAL: any build
that bumps the build number stamps itself dirty.** `dll/CMakeLists.txt:43-52` decides it with
`git diff --quiet HEAD` over the *whole* tree, and the build has by then already written the bumped
`build_number.txt` — on both runs `git status` showed that file and nothing else. So `cbe02dfd-dirty`
means "the source of `cbe02dfd`, built with the counter one ahead", not "someone left changes lying
around". A genuinely clean stamp requires `-NoBumpBuildNumber` **and** an already-clean tree.

9 new assertions, `dll_helpers_test` 1684 pass / 0 fail. Controls: the `payloadGap=2` case fails if
the helper is constant-folded to 3; the idx-3 assertion fails if a probe is re-added there.
13/13 gates, `check_proxy_exports --artifacts` green, C# suite green.
⚠ `-Target Test` clobbered `dist/` with the 112,052,516 B non-trimmed build exactly as CLAUDE.md
warns; restored via `-Mode Publish -NoBumpBuildNumber` (54.7 MiB / 57,398,784 B, sha `bc91f375`).

### 2. `docs/pipe-protocol.md` said a synthetic hop was a real pointer edge

Found while clarifying why Locate-in-GWorld returned `AuthorityGameMode` rather than
`OwningGameInstance` on ES2 — that question was a non-issue (both are stock `UWorld` UPROPERTYs,
`World.h:1424` / `:1482`; both routes are co-optimal 2-hop chains and the winner is decided by
ascending field offset, 0x1A8 before 0x228, in `Ubel.cpp`'s "sort by offset **for clean display**").

The spec, however, described the `ok_via_level` recovery as *"finds the actor in `ULevel::Actors`"*
with *"The remaining steps are real edges"*. Both false since audit #5 F8 (build 3220):
`Level.h:429` declares `TArray<TObjectPtr<AActor>> Actors;` with **no `UPROPERTY`** (control:
`DestroyedReplicatedStaticActors` at `:886` has one), so the lookup was deleted outright and
`Aura.cpp:4085-4105` builds **both** leading hops as synthetic (`fieldOffset -1`, `WorldLevel` /
`LevelActor`, `element_index -1`). Not cosmetic: telling a reader an offset-less back-reference is
a real pointer edge is the exact premise behind `e88190ba`, where a hop believed to have a real
offset produced CE tables resolving into the world's **vtable**.

The sample response was the worse half and was not in the report — it showed
`PersistentLevel → Actors[12]` as an `ArrayProperty` at `field_offset 0x98` with `element_index 12`,
a step the forward BFS *cannot* produce since it enumerates `GetClassRefMeta`. Replaced with
`GameState → PlayerArray[0]` (both reflected UPROPERTYs, a real logged path) and an explicit
PLACEHOLDER warning on the numbers. `todo.md`'s still-open `ok_via_level` row carried the same
stale `Actors[k]`; the preserved *Original note* is left verbatim per convention with a warning
above it.

-----

## 2026-08-27 (later) - The crash fix turned into a hang, because a deferral said the second half was out of scope (build 3365, verified in-game 3366)

Build 3363 (previous entry) stopped OCTOPATH TRAVELER crashing with our `dxgi.dll` proxy. The game
then **hung with no window**, and its log stopped at `DllMain: auto-start thread created OK`.

That line being last is diagnostic by itself: a thread created inside DllMain cannot run until the
loader lock is released, so the auto-start thread never printing its own first line means **the
loader lock was never released**. `tools/pe/minidump_triage.py` walks the FAULTING thread and a
hang has none, so `tools/verify/hang_dump.py` was written to dump a live process and census every
thread's stack by module.

The dump named it exactly:

```
ntdll+0x6019B                  <- RtlAcquireSRWLockExclusive's wait (ZwWaitForAlertByThreadId)
dxgi.dll+0x2ACC90              <- our own .data: the SRWLOCK object itself
dxgi.dll+0x18993F              <- our thunk, SECOND pass
AcGenral+0x5D78/5C00/22E2/5420, apphelp+0x1E696/1F81F     <- this block appears TWICE
dxgi.dll+0x1889CC              <- SetAppCompatStringPointer thunk, FIRST pass
```

with all three DllMain-created threads parked in `ZwWaitForSingleObject` behind the loader lock.

**Our own `LoadLibraryW` re-enters us on the same thread.** Loading the real `dxgi.dll` makes the
loader raise `apphelp!SE_DllLoaded`; `AcGenral`'s DXGICompat hookset does
`GetModuleHandleW(L"dxgi.dll")` — which resolves back to **us**, because we are the module
registered under that name — and calls our thunk again while the resolver is still inside
`LoadLibraryW`. **SRWLOCK is documented non-recursive.** Self-deadlock.

### The part worth keeping

That lock is audit #4 **B43**, which removed it from the winmm twin for the sibling lock-order
reason and left dxgi alone, noting the dxgi original's safety argument was *"explicitly CONDITIONAL
on RHI init being the only entry point"*. Build 3363's own audit doc recorded finishing it as
**"NOT done — deliberately out of scope"**. It was not out of scope; it was the live defect, and
the deferral cost a full extra round trip through a real game. Logged as a new row in
[working-lessons.md §2.13](working-lessons.md) — *a deferral reason ages worse than the finding it
defers* — because the specific mistake was rating a **sibling's already-proven finding** as
theoretical in the flavour it had not been applied to.

### What shipped

- **The SRWLOCK is gone.** Correctness without one is B43's argument verbatim: `mProcs[]` stores
  are aligned and pointer-sized so they cannot tear, racing resolvers write identical values, and
  "nobody observes a half-populated table" is preserved by the thunk's own null test. The log line
  is claimed once with `InterlockedCompareExchange`, after the work — the winmm shape.
- **`Lugner::ResolveReentry`**, in all four flavours. A nested call returns at once; its thunk then
  sees an unresolved slot with `mResolveAttempted` still 0 and answers 0 **without forwarding** —
  exactly what ReShade's compat exports do. The **outer** call completes the resolve and forwards
  for real. ⚠ Deliberately not a "first caller wins" mutual exclusion: a loser returning with a
  null slot would hand the *game* a null `CreateDXGIFactory1`. Removing the lock is the cure; the
  guard is defence.

### Verified in-game (build 3366, 2026-08-27)

```
10:17:32.450 [WARN]  Proxy: 1 forwarded call(s) arrived BEFORE our CRT was initialised …
10:17:32.452 [INFO]  DllMain: auto-start thread created OK
10:17:32.456 [INFO]  dxgi proxy: lazily forwarded 20/20 exports to real System32 dxgi.dll
10:17:32.475 [INFO]  DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)
10:17:32.984 [INFO]  DllMain ProxyStart: pipe server started
10:18:13.670 [SUMMARY] GObjects=0x7FF659775C10 GNames=0x20440C60010 GWorld=0x7FF6598590F8 Objects=406060
```

Two lines to actually read. The **pre-CRT warning is still there and that is the point** — it is
the shim engine's direct fingerprint, so the root-cause analysis is no longer inference. And
**`20/20` lands at `.456`, before `proxy DLL mode` at `.475`**: the resolve completed on the
*loader thread*, inside the shim's outer call, and only then did the loader release and let our
threads run. Under 3363 that resolve never returned.

⚠ `version.dll` / `dinput8.dll` have had no in-game regression run since 3363 — and they are also
the two flavours the offline rig provably cannot discriminate pre/post fix on. Queued in todo.md.

-----

## 2026-08-27 - The AppCompat shim calls our export before our CRT exists; four proxies fixed, and one "fix" that would have killed the export (build 3363)

OCTOPATH TRAVELER would not start with our `dxgi.dll` proxy deployed, while the **same 2.9 MB
binary** as `winmm.dll` worked in the same game. Root cause confirmed at instruction level from
two crash dumps and the shim engine's own disassembly; full dossier in
[audit-2026-08-26-dxgi-appcompat-crash.md](audit-2026-08-26-dxgi-appcompat-crash.md).

The game carries an AppCompat layer (`HKCU\...\AppCompatFlags\Layers` = `HIGHDPIAWARE`), so
`apphelp.dll` + `AcGenral.dll` load at module [4]/[5] — ahead of `msvcrt`. `AcGenral`'s
`NS_DXGICompat` shim does `GetModuleHandleW(L"dxgi.dll")` →
`GetProcAddress("SetAppCompatStringPointer")` → call, driven by `apphelp!SE_DllLoaded`, which the
loader raises when a module is **MAPPED** — before its init routine runs. Our export for that name
was a lazy asm thunk whose resolver logs; logging allocates; `__acrt_heap` was still NULL;
`HeapAlloc(NULL, 0, 0x20)` faulted in `ntdll!RtlAllocateHeap+0x54` and the process died before the
EXE entry point.

`winmm` survives because `AcGenral` names `dxgi.dll` / `ninput.dll` / `d3d9.dll` and **not**
`winmm.dll`: nothing enters our winmm thunks until the game's own code runs, ~2.0 s after DllMain.

### What shipped, across all four flavours

- **`Lugner::g_crtReady`** — a plain `volatile LONG` with a *constant* initialiser, so it lives in
  zero-filled `.data` with no dynamic initialiser and is readable before the CRT exists. Set by
  `Lugner::MarkCrtReady()` as the **first statement** of `DLL_PROCESS_ATTACH`.
- **A pre-CRT gate on every resolver** — `DxgiProxy_EnsureResolved`, `WinmmProxy_EnsureResolved`,
  `LoadRealVersion`, `LoadRealDinput8`. ⚠ It **refuses outright** rather than doing the
  kernel32-only resolve the plan called for: that would have been allocation-safe but *not*
  loader-lock-safe, and the pre-CRT caller is the loader itself. Nothing is latched, so the host's
  own first call resolves normally. This is what ReShade does.
- **`Lugner::g_preCrtCalls`** — an `InterlockedIncrement` counter reported once from DllMain as a
  `LOG_WARN`. Without it the refusal is completely invisible: no log, no crash, nothing.
- **The thunks now tell two null cases apart**, via a new `mResolveAttempted` data symbol. This is
  the part that needed care: the obvious "return 0 on null" would have silently reversed B44/B48's
  recorded decision, where a stub returning 0 is *worse* because winmm's `0 == TIMERR_NOERROR`
  would make a missing `timeBeginPeriod` silently no-op the 1 ms tick. `mResolveAttempted == 0`
  (resolver refused) → `xor eax,eax / ret`; `== 1` (name genuinely absent) → keep the deliberate
  loud `jmp rax` with `rax == 0`.

### The change the fix forced, which was not in the plan

`Lugner.cpp` (17 sites) and `Lugner_Dinput8.cpp` (6) cached with
`static auto fn = reinterpret_cast<Fn>(RealProc("..."));`. **A magic static evaluates its
initialiser exactly once and latches the result forever** — so adding the gate above without
touching them would have latched `nullptr` on a pre-CRT first call and left the export dead for
the life of the process. That is a *worse* failure than the crash being removed: silent, permanent
and invisible. All 23 became `static Fn fn = nullptr; if (!fn) fn = ...`, which also drops the
CRT's `_Init_thread_header` machinery off a path that must not need the CRT.

### An unrelated defect found on the way

`scripts/gen_proxy_forwarders.py --check` reported **`Lugner_Winmm.cpp STALE`** *before* any of
this work: the AD18 `SystemDllPath` fix had been hand-applied to the generated .cpp and never
back-ported, so re-running the generator would have silently reintroduced the
drive-root-relative-path defect — and that check is **not in CI**. AD18 and the new changes are
both in the generator now; `--check` is clean.

### Verification

`tools/verify/proxy_precrt_gate.py` maps a proxy with
`LoadLibraryExW(..., DONT_RESOLVE_DLL_REFERENCES)` — image mapped, exports callable, **DllMain not
called** — and calls a thunk from a child process so a fault is an exit code. The pre-fix
`out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak` faults `0xC0000005` at **`+0x187A2E`, the
same RVA on the faulting stack of both Octopath minidumps**; `dist/proxy/dxgi.dll` returns cleanly
at `+0x1889CC`. Same result for winmm. 13/13 gates and
`check_proxy_exports --artifacts` pass.

⚠ The rig provably **cannot** discriminate pre/post fix for `version` / `dinput8` (plain C
forwarders, not asm thunks — their pre-fix path does not fault in this harness), and it says so
rather than printing a green tick. Their gate is verified by construction only. **The in-game
Octopath run is still owed** — see todo.md.

-----

## 2026-08-26 (night) - The three log-audit findings, fixed; an adversarial design review changed the shape of two of them (build 3362)

The three open findings from the P3R log audit (previous entry). Each was designed, then faced a
safety lens and an honesty/scope lens before a line was written — and that was not ceremony: five
of six verdicts came back fatal, and two of the three fixes are **not** the shape I would have
shipped from my own analysis.

### `[SANEPROPS]` — one constant answering two questions

`kMaxSanePropertiesSize = 1 MB`, whose comment claimed *"even the largest generated Blueprint /
UClass layouts are at most tens of KB"*. P3R refutes it with `XRD777SaveGame` (3,671,800) and
`AstreaSaveGame` (3,671,816), both of which the same log shows walking their fields cleanly.

Split into `kMaxGapFillBytes` (1 MB, **unchanged** — the byte-sweep work cap the 827 MB Elliot
wedge exists to stop, and also a WIRE bound, since `GuessGapTypes` emits ~N/4 rows for an N-byte
gap) and `kMaxPlausiblePropertiesSize` (**64 MB** — admission control for caches that are never
erased). ⚠ 64 MB is an admission bound, not a fact about UE; it sits ~17× above the largest real
sample and ~13× below the observed garbage, and both samples are in the header so the next person
re-derives rather than re-guesses.

⭐ **My own analysis had two errors, and the review found both.** I concluded "nothing scales with
PropertiesSize after the gate" — wrong: gap-fill emits one row per 4-byte stride, so a 3.67 MB
class is ~918,000 rows serialised with no cap. (The split still holds, because that site uses the
work cap — but I knew that by luck, not by measurement.) And I recorded the cache refusal as
"only costs a re-walk". It does not: `WalkClassEx` refuses to **RETURN**, handing back an empty
`ClassInfo`, and Aura's caches read `WalkClassEx(cls).Address != cls` as the refusal signal — so
both `USaveGame` classes were invisible to **Value Search, Group Scan, snapshot capture, CE export,
Solitar and Solide**, with no log line at all. That refusal now logs.

⚠ **Writing the test reproduced A10 by accident.** One class blob reused across four cases had
every case after the first read the first one's memoised answer, because `s_walkClassExCache` is
keyed by address and nothing erases it. One blob per case, and the comment says why.

### `[FUNCDENOM]` — a denominator that counted classes contributing nothing

*The wire field was not the problem.* `scanned_classes` is correctly named, correctly documented,
and one of the three UI sentences ("N classes scanned") was already reading it correctly. What was
wrong was every sentence phrased as **provenance**.

So the contributing count is **derived** — a computed property over the rows the panel is
displaying — with **no new wire field**. Two reasons, both found independently by the reviewers: a
parsed field would be `0` in every VM fixture (they all override `ListAllFunctionsAsync` and bypass
the parser), and a `??` fallback fires on *absent*, not on *zero*, so a DLL that shipped the key and
missed the assignment would render *"9,760 functions from 0 of 2,293 classes"* — a new wrong report
at the one site the fix exists to protect.

⚠ The DLL's log line is **append-only** on its format string: nothing in `dll/src` carries
`_Printf_format_string_`, so MSVC diagnoses no format/argument mismatch anywhere here, and a
reordered trailing `%s%s` reading an `int` as a `char*` is an access violation on the pipe worker
inside a shipping game.

### `[STALLDEFAULT]` — a default posing as a measurement

Fixed by **withholding** `game_thread_stalled` when it is not a measurement — the cheapest correct
answer, not a compromise: absence is already a live wire state and it costs fewer bytes.

⚠⚠ **The naive version of this fix is a regression, and the review caught it.** The hook can go back
DOWN mid-session (`Frieren.cpp` → `Stark::RemoveHook()` on a validation failure). Today a banner
raised by a real stall is cleared **by the lie** — the next `false` clears it. Withhold the key
without teaching the client that absence *withdraws* the claim and that banner sticks ON for the
rest of the session. DLL half and client half therefore landed in one commit.

A tri-state **string** was rejected on a measured path, not a guess: `GetValue<bool>()` throws
`InvalidOperationException`, `PipeClient`'s read loop catches only `JsonException`, so the throw
escapes, the loop exits, and every in-flight request fails with "Pipe disconnected" — it would
**disconnect** an old client, not degrade it.

`get_diagnostics` had a **second report path** with the identical defect, rendered to the user as
"Responsive" in the System tab. It gains `liveness`; `responsive` stays unchanged so an older UI
does not start rendering "Stalled" on a healthy game.

Three rigs relied on the lying default and were **armed, not weakened**. ⚠ One of them,
`seethrough_arm_b.py`, read the key with `[...]` **between `suspend-tid` and `resume-tid` with no
try/finally** — a `KeyError` there would have left the game thread SUSPENDED and the process
needing a kill.

---

**Method note worth keeping.** The `PERF` denominator fixed in 3361 and `[SANEPROPS]` are the same
sentence in different files: *a fix that landed in one of its two copies*. In 3361 it was `busyMs`
corrected and `dispatches` two lines below it not; here it was `PathStepToBreadcrumbs` marked and
`PopulateFromWorld` not. Three instances in one day, in three unrelated modules. The grep that
finds them is aimed at the CONCEPT, not at the diff.

C++ suite green (265 + 1649 + 22 + 14 + 17), C# **4,750/4,750** with six negative controls across
the three fixes, gates **13/13**.

## 2026-08-26 (evening) - The P3R log confirmed the fix; auditing the same logs found two more reporting defects, and named the title that closes row 5 (build 3361)

The maintainer re-ran the reported game. `Logs\P3R` (build 3360, `e88190ba-dirty`, proxy DLL,
UE427, 65,021 objects) against the 3358 session on the **same actor in the same game**:

| | 3358 (broken) | 3360 (fixed) |
|---|---|---|
| nav | `NAV→ KernelActor addr=0x64AF68C0 off=0x0` | `NAV→ KernelActor addr=0x101646B80 off=none` |
| spine | `KernelActor(P,0x0,68C0)` | `KernelActor(P,none,6B80)` |
| export | `bcCount=2` | `bcCount=1` |
| AOB | `AOB=True` | `AOB=False` + `AOB requested but root is not GWorld` |

⭐ **`AOB=True → False` is the sharpest line in the whole affair**: build 3358 wrapped the bogus
chain in a **GWorld AOB script**, i.e. it made the wrong address *survive a restart*. And the
"before" artifact was on disk the whole time — `Y:\Copy CE XML.xml`, saved 09:47 — which measures
identically to the after: root `gworld_addr_9E30D6`, then `KernelActor (0)` at `+0`, and the
actor's real address `64AF68C0` appearing **0 times in 1,288 entries**. That is precisely what the
unit test asserts when the row marker is removed.

**Then the logs were audited rather than merely read** — five diverse lenses over both sessions, a
refute-mandated skeptic per finding (19 of 40 killed), and a completeness critic. It changed the
conclusion twice, in both directions.

⚠ **Two corrections to what this session had already claimed.**
1. "The two sessions are apples-to-apples" was **too strong**. They differ in injection mode
   (proxy vs auto-start), AOB hint-cache state (`scan #1` vs `#17`), `array_limit`, and whether
   AOBMaker was up. The narrow comparison — same PE `69CE343916376000`, same actor, same class,
   same gesture — holds, and that is what should have been said.
2. The P3R logs witness the **mechanism**, not the **bytes**: no export's XML content is ever
   logged, and a `bcCount=1 / AOB=False` pair is also what a **GameEngine-rooted** export would
   print — and that session rooted one on GameEngine at 10:50:13. Here the same line's `BC=` trace
   names a two-crumb GWorld spine, so it is disambiguated; but the signature alone is not.

**That second point was a real gap, and it is now closed in code.** The re-anchor announced itself
only through `StatusText`, which reaches no log and is overwritten by the next status. `LogReanchor`
now says it outright at both export sites:

```
CEXML re-anchored: dropped 2 offset-less hop(s); root is now (level actor) @ 0x2B8B783A8D0
                   (absolute, session-only — no pointer chain from GWorld exists)
```

**Two reporting defects fixed, and both are the same shape as the bug that started the day.**

⭐ `[PERFDENOM-2026-08-26]` — the `PERF` line printed three numbers over **two denominators**:
`11 dispatches` beside `busy 15.2 ms` beside `per call: dll 1.524`, where 15.2/11 = 1.38. `busyMs`
is summed from the per-command rows, which deliberately skip the probe's own `get_diagnostics` —
there is a comment saying so, added when a 57.7 ms operation reported "161.2%" because the probe
was measuring itself. `dispatches`, **two lines below it**, still read the global counter. The
divisor was always `dispatches − 1`, on two games and two builds. *The fix landed in one of its two
copies* — the same sentence as the GWorld defect, in a different file. The existing test
**pinned** it (`R(3, …, one call)` asserting `"3 dispatches"`), sitting directly beside the test
that made exactly this correction for `busyMs`.

`[PERFPEAK-2026-08-26]` — `max` was a running high-water mark, so it was the worst call *since the
last reset*, not the worst in the operation; two exports minutes apart printed a byte-identical
`max 3.5ms` while their totals moved. `TopDeltas`' own comment said *"let the label carry the
caveat"* and the label said `max`. It now reads `top (peak = session high-water, not this op)`.

⚠ Fixing that label broke `Split_never_goes_negative…`, which asserted **no `-` anywhere in the
line** as a proxy for *no negative number* — and `high-water` has a hyphen. The proxy was corrected
to the actual predicate rather than the wording bent to fit it.

**Row 5 is closed, and the log tree named the title.** Three lenses guessed "needs a
World-Partition game"; the critic found the reproducer already on disk —
`Logs\TQ2-Win64-Shipping\ui-view-20260823-091415.log` from five days earlier, showing
`(world level)(S,0xFFFFFFFF,A960) > (level actor)(S,0xFFFFFFFF,FA60)`. Two `0xFFFFFFFF` hops in a
real spine, and nobody had ever pressed Copy CE XML on one — which is why the third defect stayed
latent. Re-run on **Titan Quest II** (UE507, 279,587 objects; the same `Actor` → 3 results → 🌍
Locate): the hops now render `none`, the export re-roots (`dropped 2 offset-less hop(s)`), and the
emitted table has **777 `<Address>` entries, exactly one absolute** — the level actor's own — with
`FFFFFFFF` appearing **0** times anywhere in the file. AA Script: `define(Actor,2B8B783A8D0)`.

Three findings stay **open** and are recorded in [todo.md](todo.md): `[SANEPROPS-2026-08-26]`
(a 1 MB sanity ceiling rejects two legitimate ~3.5 MB P3R SaveGame classes, and the *instance*
walker uses the same predicate to declare a live object stale), `[FUNCDENOM-2026-08-26]`,
`[STALLDEFAULT-2026-08-26]`.

Suite **4,738/4,738**, gates **13/13**, `dist/` republished AOT-trimmed (54.7 MB, sha `25e65203`,
byte-identical to the native output).

## 2026-08-26 (later) - The GWorld actor-chain fix, driven on a live game; and the sentinel was logged as `0xFFFFFFFF` (build 3360)

**Live half of `[GWORLDACTORCHAIN-2026-08-26]`** (previous entry). Rows 1–4 of its verification
register were run end to end against a running **DumperTest Shipping** (UE504, **24,479 objects**,
`dist/UE5Dumper.dll` injected, GWorld resolved by AOB `GWLD_TQ_1`). Full evidence is in
[todo.md](todo.md); the short form:

- The Offset column shows `—` for every Outer-derived actor/component and `0x30` for
  `PersistentLevel`.
- Copy CE XML from `PlayerStart0`: of **382** `<Address>` entries the copied table carries **exactly
  one** absolute address — the actor's own, as the root. The UWorld address appears **0** times.
- Copy CE AA Script: *"hardcoded address (GWorld path not forward-walkable)"*, script body
  `define(PlayerStart,1872FEDBE00)`, no GWorld walk.
- Control: `GWorld → PersistentLevel → LevelScriptActor` still emits the AOB-wrapped restart-stable
  chain with real offsets and no re-root. The fix did not over-reach.

⭐ **Two things made this cheap, and both are reusable.** The defect lives in `PopulateFromWorld`,
which consumes the DLL's `walk_world` reply and never looks at the engine version — so the
**already-granted DumperTest fixture stands in for the reported P3R (UE4.27) session** and no new
computer-use grant or purchased title was needed. And `clipboardRead` is granted, so the emitted
table was **read back as bytes** instead of being eyeballed in Cheat Engine: every claim above is a
count over the actual XML, not a reading of a screenshot. Most "needs CE" export rows are like this.

⚠ **One real find from the live run, and it was in the LOG.** The first pass printed the no-offset
sentinel through `0x{-1:X}`:

```
NAV→ PlayerStart addr=0x1872FEDBE00 off=0xFFFFFFFF ptr=True
```

That is meaningless as an offset **and** confusable with `+FFFFFFFF` — the emitted-table defect the
sentinel exists to prevent. A future reader grepping the logs for `FFFFFFFF` would have hit a
**correctly marked** hop and read it as the bug still present. `FormatCrumbOffset` now prints
`off=none` at all six sites (nav, struct-nav, synthetic-container rehydrate, both export pre-checks,
and the breadcrumb trace). Logs are this project's primary evidence channel — a sentinel has to be
legible there too.

## 2026-08-26 - "Start from GWorld" published level actors as fields at offset 0; every CE chain through one walked into the world's vtable (build 3359)

**`[GWORLDACTORCHAIN-2026-08-26]`.** Reported on **P3R** (`UE427`, 65,158 objects, build 3358):
Copy CE XML from an object reached through the GWorld actor list produced *completely wrong*
addresses. CE resolved the emitted table to

```
base            P->603BB0A0   8 Bytes  0000000144AF6408
  KernelActor (0)  P->144AF6408
```

`0x144AF6408` is inside the executable image (base `0x140000000`) — it is the value at
`UWorld + 0`, i.e. the **world's vtable pointer**. The actor really lives at `0x64AF68C0`.

**Root cause — a fix that landed in one of its two copies.** Audit #5 F8/F9 established that
`ULevel::Actors` carries **no UPROPERTY**, so the actor list is reconstructed from each actor's
`Outer` (`Aura::FindActorsInLevel`): there is no offset from UWorld to an actor, and no element
index either. `LiveWalkerViewModel.PathStepToBreadcrumbs` **already knew this** — a `LevelActor`
path step is stamped `FieldOffset = -1`, `IsPointerDeref = false`, with a comment citing F8.
`PopulateFromWorld`, which builds the same hop for the *Start from GWorld* list, was never given
the marker: it published every actor and component as `Offset = 0` + `isPointer: true`, which is
a positive claim that the actor sits at `[UWorld + 0]`.

Three consumers believed it, and the third is the one that shows the shape of the bug best:

| | path | what it emitted |
|---|---|---|
| 1 | Copy CE XML / Copy CE Field | `[GWorld] + 0` → the vtable (the report) |
| 2 | Copy CE AA Script | `gworldWalkable` requires `spine.Skip(1).All(bc => bc.FieldOffset >= 0)` — **the gate was already correct**; the crumb lied to it with a `0`, so it emitted a restart-stable walk into the vtable |
| 3 | Locate-in-GWorld → export | that path *does* stamp `-1`, and nothing downstream handled it: `$"+{step.Offset:X}"` formatted it as **`+FFFFFFFF`** |

So the marker existed, the gate that consumes it existed, and the emitter that had to survive it
did not. Row 3 was latent the whole time.

**Fix.**

- `LiveFieldValue.HasNoParentOffset` — "this row is not reachable from the current object by a byte
  offset". `Offset` deliberately **stays 0** (bookmarks and the same-layout row-reuse path key on
  it); the flag is what stops it being read as `+0`. `PopulateFromWorld` sets it on actors and
  components; `PersistentLevel`, which IS a reflected field of UWorld, keeps its real offset.
- Navigation stamps the existing `-1` sentinel rather than inventing a second representation, and
  skips `MapValueDrillOffset` so the sentinel cannot be turned back into a number.
- `CeXmlExportService.AnchorAtLastUnchainableHop` — re-roots the spine at the **deepest** hop with
  no offset and drops everything above it. Idempotent (index 0 is never examined, because a root's
  offset is not applied by any emit path), so the VM and the generator can both call it. Both
  export commands do, and say so: *"⚠ Chain re-rooted at KernelActor (absolute address,
  session-only)"* — the trade is restart-stability for correctness, and the user is told.
- `ProjectBreadcrumb` now **throws** on a negative offset instead of formatting it. That is what
  kills row 3, and it is a reachable invariant, not dead code: with the re-anchor removed, the
  export fails with *"spine still contains an offset-less hop 'KernelActor' … was not applied"*.
- The Offset column renders `—` instead of `0x0` for such a row.

⚠ **The offset column's Binding change broke its sort, and `DataGridSortWiringTests` caught it in
the same run.** `SortMemberPath="Offset"` was rooted by the column's own `Binding`; pointing that
at `OffsetDisplay` un-roots it, and under trimming the header goes inert. This is the **third**
disguise of that trap recorded in `LiveWalkerPanel.axaml.cs` (after a template-column conversion
and an element-syntax `MultiBinding`) — and here sorting on the display string would have been
*worse* than inert: it orders hex text, putting `0x9` after `0x10`. Fixed with a wired
`DataGridSortComparers.Number` entry.

**Verification.** `ui/UE5DumpUI.Tests/LiveWalkerGWorldActorChainTests.cs` — 7 checks driving the
real production path (stubbed `walk_world` → row → breadcrumb → clipboard XML), with **five
negative controls, each isolating one half**: remove the row marker → 4 red (and the copied XML
contains **no trace of the actor's address**, which is the reported defect reproducing); remove the
export re-anchor → 1 red, via the generator guard firing in production; remove the generator guard
→ 1 red; revert the offset display → 1 red. Full suite **4,734/4,734**, gates **13/13**.

⛔ **Not yet confirmed on P3R itself** — see `[GWORLDACTORCHAIN-2026-08-26]` in
[todo.md](todo.md)'s live-verification register.

## 2026-08-24 (later) - The C1 spawner fixture already existed; the repo's copy of the sample source did not know (build 3349)

**`[C1-SPAWNER-EXISTS-2026-08-24]`.** Found by accident while picking a queued-route fixture for
b636: `list_all_functions` on a running DumperTest reports **17** `DumperTestActor` UFunctions, and
they include `Spawn_Holders`, `Spawn_Decoys`, `Spawn_DestroyHolders`, `Spawn_CountHolders`,
`Spawn_Generation`, `Spawn_LateInstance`, `Spawn_RecycleChurn`, `Spawn_LastRecycledAddr` and
`Spawn_ManyComponents` — the entire set the classification doc drafted as **bucket C1, the one
fixture addition it said would unlock seven rows**, plus one it never asked for.

It is not a declaration stub: three `Spawn_LateInstance` calls moved the live
`DumperTestLateSpawn` population **2 → 5**.

⚠ **Two sessions concluded the opposite, including this one.** The check both made was to grep
`tools/ue-sample/DumperTest/Source/DumperTest/DumperTestActor.h` — which is dated **2026-08-19** and
contains none of it, while the packaged `DumperTest.exe` used for every verification run is dated
**2026-08-23** and contains all of it. **The repo's copy of the sample source is stale relative to
the binary that is actually used.** So grepping `tools/ue-sample` is not a valid way to answer "does
this fixture exist"; ask the running game. Earlier today I corrected an agent for saying the spawner
had shipped — the agent was right and the correction was wrong.

ℹ️ Recorded separately so it is not mistaken for a fixture defect: invoking any **parameterised**
`DumperTestActor` UFunction over the pipe returns `ProcessEvent error code -4`, while every
**zero-parameter** one returns 0. The split is exact and hits the pre-existing `AD4_*` functions as
hard as the new `Spawn_*` getters, so it belonged to the parameterised-invoke path, not to this fixture. **DIAGNOSED AND FIXED 2026-08-24** (build 3350): `invoke_function` sized its param buffer from the caller's `parms_size`, which **defaults to 0**, so omitting the field handed ProcessEvent a zero-length heap buffer and it wrote the return value past the end. Half the fault was mine for not sending the field; the other half is that the DLL had `ufuncAddr` and could read `UFunction::ParmsSize` itself, and the protocol doc called the field *optional (default 0)*.

## 2026-08-24 - The b637 return-value fix had a sibling it never covered: int64 (build 3348)

**`[RETINT64-2026-08-24]`.** Found while closing the b637/b644 verification row, not by looking for
it. That row asks a human to confirm a pointer return "shows a `0x` prefix" — a compile-time string
literal already pinned by C# tests, and blind to what build 637 actually fixed, which is a **read
width**: `readUFunctionReturn` has no `'pointer'` type, so the pre-fix spelling fell through to the
signed int32 default and read **four bytes of an eight-byte slot**.

Replacing that check with one that measures the width (`scripts/tests/return_read_test.lua`, the real
helper driven against byte-accurate stub memory) immediately showed the fix was incomplete.
`BakedScriptGenerator.cs:331` is `readType = displayType == "pointer" ? "qword" : displayType` — it
rewrites **only** `"pointer"`. `MapToHelperType` also emits `"int64"`, from two routes
(`Int64Property` at :522 and the size-8 `EnumProperty` case at :519), and that word reached the
helper verbatim where **no `'int64'` branch existed**. Measured: `0x0000000123456789` read back as
**591751049**. `UInt64Property` was unaffected — it maps to `"pointer"` and so rode the fix.

Fix: `'int64'` joins the 8-byte branch. **No sign folding**, and that is a property of the width
rather than an oversight — Lua integers are 64-bit two's complement, so the bytes CE returns already
*are* the signed value (`FF FF FF FF FF FF FF FF` -> `-1`, measured). At 64 bits `'int64'` and
`'uint64'` read the same bits and only the caller's format specifier differs; the 32-bit case is
different precisely because CE widens 4 bytes into a positive Lua number, which is AA20.

Two things worth carrying forward. **`-1` is a bad discriminator** — it passes even with the fix
removed, because a 4-byte signed read of `FF FF FF FF` is also `-1`; the test needs a value wider
than 32 bits. And **a Lua-only fix does not ship until the UI is republished**:
`scripts/ue5_invoke_helper.lua` is an `EmbeddedResource` of the UI (`UE5DumpUI.csproj:146`), served
through `GetManifestResourceStream`, and is not copied into `dist\` as a loose file. Verified after
republishing by byte-searching the AOT exe for the new branch.

## 2026-08-23 (evening) - The proxy advisory hid winmm; found by reading import tables, not code (build 3337)

**`[PROXYALTWINMM-2026-08-23]`.** Working the A6 offline bucket — the "Lushfoil proxy did not load"
row — the useful move was to stop reading our code and parse the games' PE import tables with
`tools/pe/pe_imports_exports.py`. Over every UE shipping `.exe` installed on this machine:

```
16 shipping exes:  14 import winmm   ·   13 import dxgi   ·   4 import version   ·   0 import dinput8
```

`ProxyImportAnalyzer.DescribeImportable` built its `alt:` list from **dxgi and dinput8 only** — so it
recommended the flavour **nothing** imports and suppressed the one **almost everything** does, even
though the analyzer has parsed `ImportsWinmm` since 2026-07-27 (`a2c81a0c`, *"teach the analyzer
winmm"*), winmm is one of the four proxies we build, and the class's own remarks group `dxgi`/`winmm`
as the *"pure static-import hijacks"* — the deterministic pair. On the 13 games importing both, the
user saw `alt: dxgi` and was never told winmm was equally available.

⚠ **Root cause is `working-lessons.md` §2.3, verbatim.** `ImportsWinmm` was appended to the record
**with a default** — the only defaulted member — so all four `Recommend` tests construct three
positional arguments and silently assert the no-winmm case. `DescribeImportable` was edited *again*
on 2026-08-10 (`c28e3a78`), two weeks after winmm was taught, and still not updated. The test file's
comment still read *"none of OUR three"*.

⚠⚠ **And the structural guard I wrote for it was VACUOUS on its first draft.** It asserted
`Display.Contains("winmm")` — but the corrected empty-case sentence is `no dxgi/winmm/dinput8`,
which *contains* `winmm`, so with the fix removed the guard **still passed** while the two
hand-written cases failed. Only the negative control exposed it. It now matches inside the `alt:`
segment. Final control: dropping the winmm line fails **all three**; restoring returns 51/51.

Suite **4,712 / 0 failed**, **13/13** gates, `dist/` republished AOT-trimmed.

ℹ️ **What this did NOT settle.** The Lushfoil row itself stays open. Offline forensics established the
proxy is present, correctly placed beside `LushfoilSim-Win64-Shipping.exe`, the right flavour (78
`version.dll` exports) and dated 2026-08-19, and that the exe was **not** patched (2026-02-22) — but
the exe never imports `version.dll`, which `ProxyImportAnalyzer`'s own remarks already document as
**normal and non-diagnostic** for that flavour (Lushfoil is named there among 11 games running a
working version proxy without the import; the load is a run-time `LoadLibrary`). So the filesystem
cannot say why it failed on 2026-08-21. **Actionable instead:** Lushfoil statically imports both
`dxgi` and `winmm`, either of which loads deterministically rather than riding a run-time
`GetFileVersionInfo` call — which is exactly what the advisory now surfaces.

## 2026-08-23 (later still) - AC13's untested metric gets two tests, and the route that does not work is written down (no build change)

**No product change; `build_number.txt` stays 3335/3336** — this adds tests only, and the final shape
needed **zero** production edits.

`PipeTransportStats` appeared in **no test source at all** — the sibling of the same defect family
(`ClassifySendFailure`) is tested only because it was split out as a pure function, while AC13's fix
is a `try`/`finally` **placement**. `[AC13-2026-08-22]` had already found the live row unobservable,
so the metric had no coverage of any kind.

**Two deterministic tests, each negative-controlled against a deliberately broken build:**

* the not-connected guard sits **above** the timer, so a refusal that sent nothing logs no 0 ms
  sample — control: moved the timer above the guard, **only that test failed**;
* `Snapshot()` is monotonic and converts ticks→ms correctly (record exactly `Frequency/100`, expect
  10 ms) — control: factor 1000.0 → 500.0, **only that test failed**.

Both controls reverted to an empty diff and a green 2/2. Suite: **4,709 tests, 0 failed, 34.7 s.**

⛔ **The positive half is still uncovered, and the attempt is recorded rather than repeated.**
`SendAsync` needs a live connection to reach the timer at all. `Constants.PipeName` is a hardcoded
`const`, so a test server would bind the name a running game's DLL also serves — the hazard behind
*"never run `pipe_client.py` while the UI is connected"*. An injectable `pipeName` ctor parameter was
prototyped and then **reverted with the test**, rather than left in production justifying something
that no longer exists. With it in place, `PipeClient.ConnectAsync` **reproducibly never completes**
against an in-process `NamedPipeServerStream`, while a raw `NamedPipeClientStream` with identical
arguments connects in **0.15 s** — measured at `maxNumberOfServerInstances` 1 and 4, on and off the
xUnit sync context, always with the server's own `WaitForConnectionAsync` reporting completed. The
dialled name was confirmed from the client's own log line, and a `PipeClient` aimed at an unserved
name throws at its 5 s timeout as designed. Not diagnosed; not an AC13 defect.

⚠ **The lesson that cost the most.** The first draft had an unbounded `await` in a helper, so it
**hung with no message** — and that is what made the full suite report `4708 succeeded / error: 1`
with **zero failures listed** after the host was killed. Bounding every await (working-lessons §2.7,
*a hang is not a test result*) turned it into a 10 s failure naming the exact line, which is the only
reason the wall above could be characterised instead of guessed at. The clean suite now runs in
**34.7 s** — the earlier 9m46s was entirely the hang.

## 2026-08-23 (later) - A sibling clip found by sweep, and the sweep became gate 13 (build 3336)

**`[DUMPHDRCLIP-2026-08-23]`.** Closing the classification doc's A6 item *"`[FORCESTATUSCLIP]`
sibling `.axaml` sweep"*. `DumpExplorerPanel.axaml:28` carried `TextTrimming="CharacterEllipsis"` as
the last child of a **horizontal `StackPanel`, behind four fixed-width buttons** — the exact
structure fixed on 2026-08-22 elsewhere. A `StackPanel` hands each child its **desired** width, so
the trimming can never fire and the text is hard-cut with no ellipsis and no tooltip.

The tail is again the part that matters: `BuildHeader` emits
`UE {ver} · {module} · … · {DumpedAt}`, so the first thing lost is the **dump timestamp** — the field
that says whether the dump is stale. Fixed like the precedent: `DockPanel`, buttons `Left`-docked,
`HeaderText` as the fill child, plus `ToolTip.Tip`.

⭐ **The narrowing is the reusable part.** The naive query — bound `TextBlock` in a horizontal
`StackPanel`, no tooltip — returns **138 hits**, nearly all short scalars (`PoseX`, `ArrayLimit`).
That is `working-lessons.md` §2's "~52% wrong" shape in miniature: a real structural pattern with no
severity filter is noise. The discriminator is **the author's own `TextTrimming`** — its presence
says they expected clipping and asked for an ellipsis, and the layout makes it impossible. 138 → 1.

⚠ **A dropout dropped for the wrong reason.** `ValueSearchPanel.axaml:694` has `Width="520"` and is
genuinely fine. But `MainWindow.axaml:338/353` were excluded by a first draft that examined only
**direct** children; their tooltips sit on the wrapping `Border`. The correct rule is *a tooltip
anywhere up the ancestor chain*, and the draft would have hidden a real case nested one level
deeper. The shipped check walks ancestors for `ToolTip.Tip` **and** `Width`/`MaxWidth`.

**New gate:** `tools/check_inert_trimming.py`, wired into `check_all.py` — **13 gates now**. This
defect class has shipped four times (`FORCESTATUSCLIP`, `V8PREVIEWCLIP`, `TYPECOLCLIP`,
`DUMPHDRCLIP`), which is what makes a one-off sweep the wrong deliverable. Negative-controlled
against the pre-fix file via `git show HEAD:…`: it reports the hit there and none on the fixed tree.

⚠ **Stop quoting the gate count** — it has been 4, 12, and now 13. The handover row and the memory
index were changed from a hard number to *"derive it from `N gate(s) run`"*.

## 2026-08-23 - The freeze abandon modal blamed the wrong cause; `ue5_freeze_helper.lua` -> 1.5 (build 3335)

**`[FREEZEFIRSTERR-2026-08-23]` — found by a verification row, not by an audit.** Closing AA3 step 5
(*"a permanent rescan failure must stop the writes"*) on a live suspended DumperTest, the abandon
modal read:

```
[ue5_freeze] DumperTestHolder: 3 consecutive rescans failed -- freeze STOPPED writing
(last error: mailbox busy (concurrent invoke or rescan)). This record has been unticked...
```

The row still PASSED — the freeze did stop, untick and print exactly once. But the **reported cause
was a consequence**, and structurally so:

* `waitDone`'s timeout path is `if not wok then return nil, 0, werr end` and does **not** clear
  `OFF_CMD` — deliberately, because the DLL may still write its reply later;
* so rescan #1 times out with `cmd` left set, and rescans #2 and #3 short-circuit on the in-flight
  guard (`:645-650`) in microseconds;
* `_lastError` was overwritten by each, and the message reported the **last** one.

So for the entire *"the DLL took the command and wedged"* family — a suspended process, a hung game
thread — the modal was **guaranteed** to name a transient concurrency cause for a permanent fault,
in the one place a user ever reads it, and to discard the timeout's actionable hint
(*"stale g_invokeMailbox address? re-inject, or re-enable the table"*). That is the distinction
CLAUDE.md's *"never report a mailbox failure by guessing"* exists to preserve.

**Fix:** track `_firstError` (set only on the 0→1 transition, cleared with `_lastError` on any clean
rescan) and report it, appending a differing latest one:

```
... freeze STOPPED writing (first error: mailbox timeout after 5008ms -- the DLL never picked
this up (stale g_invokeMailbox address? re-inject, or re-enable the table); then: mailbox busy
(concurrent invoke or rescan)).
```

⭐ **The live modal was reproduced in the rig before anything was changed.** `freeze_helper_test.lua`
AA31 stages the real sequence — a healthy start, then the fake DLL is killed by mutating the captured
`installMailbox` opts — and printed the byte-identical `(last error: mailbox busy …)`, failing. Its
control (every failure identical) passed throughout, so the case discriminates.

**Helper version 1.4 → 1.5, and the bump is load-bearing**: `versionLess` makes a same-version
re-load a **no-op**, so a 1.4 chunk already resident in a user's CE table would never have received
this. Two copies of the version exist (the doc block at `:90` and `THIS_HELPER_VERSION` at `:273`);
both moved.

⚠ **The bump broke two AA30 cases and silently weakened a third**, which is the more reusable
lesson: they hard-coded `'1.4'`/`'1.5'`, so with the file at 1.5 the *"an OLDER file does not
downgrade a newer resident"* case was re-loading a **same-version** file and passing on the wrong
branch entirely. AA30 now **derives** `OLDER`/`CUR`/`NEWER` from whatever the helper declares.
Negative-controlled: forcing `OLDER = CUR` makes the replace case fail.

Suite: **159 checks, 0 failures**. Gates: **12/12**. `dist/` republished AOT-trimmed — the helper is
an `EmbeddedResource` (`UE5DumpUI.csproj:150-152`), so the shipped exe carries its own copy and
*"Export Freeze Helper Lua File…"* would otherwise have kept writing 1.4.

## 2026-08-22 (later) - Eleven more register rows, the twelve-gate discovery, and a new single-entry handover (no build change)

**No source change to the product.** `build_number.txt` stays **3315**; the only code added is a
tooling script and two verification rigs. The entry below covers the work done *after* the one above
was written.

### ⚠ The finding that matters most for anyone reading this next

**CI runs TWELVE doc/source gates before it builds, plus a thirteenth over the built proxies — and
this session ran FOUR of them all day**, because four is what the docs named. `tools/check_all.py`
now runs the twelve in CI's order (which is not alphabetical: `aob_specificity` reads the TSV that
`extract_patterns --check` writes) and reprints CI's own failure text, so a local failure reads like
the one that would have appeared on the PR.

⭐ **Its first run failed** — `CeLuaQuotingTests` carried a literal user-home path (`<user home>\O'Brien\UE5Dumper.dll`) — quoting it verbatim here trips the same gate, which is a fair demonstration —
and `check_no_local_paths` rejects a concrete user home in a tracked file. That test had been
committed hours earlier against a green four-gate run. The apostrophe was the point of the fixture;
the home directory never was. Now `D:\O'Brien Studios\…`, with a comment saying why.

### Register rows settled

| row | outcome |
|---|---|
| `A6` step 3 | ✅ the Force walks the **super-chain, not the name** — `StaticMeshActor` (derives from `AActor`, name does *not* start with "Actor") **is** held, while 33 diffable non-derived objects including the genuine same-prefix `ActorSequence` are **untouched**. The pair is the proof; either half alone is consistent with the wrong matcher |
| `A6` step 5 | 🟡 CDO half ✅ (CDO clean through a 256-instance hold, with 12/12 sampled live components **actually** forced as the channel proof); spawn half **blocked**, measured three ways — the debug camera is one-shot per process, cycling it gives 295 → 295 objects, and no `ConsoleCommand`/`RestartLevel` exists in the 3,142 functions listed here |
| `AC13` step 1 | ✅ closing a **connected** UI leaves no `Pipe: ReadLoop error`; non-vacuous because `ui-init-0.log` shows the logger alive and flushing at the exact moment (`UE5DumpUI shutting down...`) |
| `AC13` steps 2–3 | ⛔ **unobservable as written** — there is no IPC figure on the System tab, and step 3's own action destroys its observable: the probe's closing `GetDiagnosticsAsync` sits under `catch { return; }`, so closing the game mid-request suppresses the PERF line entirely |
| `B10` | ✅ CLOSED — capture 644 objects / 12,155 fields, `wall 638.6 ms` recorded as a **new baseline** (the only prior figure is a different target and not comparable), and struct / enum / bool all decode. ⭐ Discriminating because the grid prints the **raw byte beside the decoded name**: `03 → ROLE_Authority` and `01 → DORM_Awake` are two different enums each decoded right |
| `A3` | ✅ CLOSED by step 3, and the 2026-08-19 measurement **reproduced to the digit** across a different build (3,450 candidates · 72 · 34 · 19 · 19) |
| `G11` steps 3–4 | ✅ answered as a measured **negative**: over 66 detection attempts across 23 hosts, `Tier 2` has **never** fired and `Tier 3` has **no subject**. The channel is proven — the ladder reached Tier 1 six times on three games |
| `AF25` step 3 | ✅ teleport opcode still 8 and the mirror **is** guarded — a negative control (`CmdTeleport = 9`) leaves the contract gate `CHECK OK` but fails **6 tests** |
| `V6/U8` step 1 | 🟡 attempted, **not closed**, and deliberately no defect filed — see below |

The 繁中 checklist went **38 → 33**.

### ⚠ Two rows where the *instrument* was the problem, not the product

* `V6/U8` — the Live Walker toolbar **reflows** once an object loads (`Find Refs` / `Related`
  appear), so coordinates captured earlier in the same session go stale silently. Two "press ▼"
  clicks landed on the **"2 matches" label**. The claim "the stepper stopped working after
  auto-refresh" was one sentence from being filed; it is unverified because the actuator was never
  shown to fire in that state. `working-lessons.md` §2.5d.
* `A3` step 1 — following the 繁中 step literally (*Value Search, **Float***) gives **0** vector
  components, a clean-looking FAIL of a working fix, because under LWC an `FVector` is a
  double-precision `FVector3d`. The warning was already in `todo.md` from 2026-08-19. **Read the
  register entry before running the mirror's step.**

### Docs brought to current

* **`docs/handover-2026-08-22.md`** — a new single entry point, replacing both predecessors, which
  moved to `docs/archive/`. It carries what a fresh session needs in its first ten minutes: the exact
  **computer-use grant list** (20, with their `request_access` names), the **≤7 batching rule** and
  the correction that **grants persist across sessions** (measured — a *reboot* is the real
  invalidation), `systemKeyCombos` being ungranted, **which Steam process is which**
  (`steam.exe -applaunch` to launch · `steamwebhelper.exe` to see the library · the
  `*-Win64-Shipping.exe` to inject, with a measured shim table for all nine granted titles), the
  dead-engine trap, the hard rules, the build/test/gate commands with their timeouts, how to drive
  Cheat Engine, what is open with a derivation for every number, and how to close a row.
* `todo.md`'s header was stale in two measurable ways (build **3263**, "30 open batches" against a
  derived **15**) — the file that says *"read this when deciding what to do next"*. Fixed.
* The OPEN FIXES INDEX heading said **3** while the table needed a fourth row
  (`[FORCESTATUSCLIP]`); added, with the observation that **none of the four is a straightforward
  code fix**.
* CLAUDE.md's `Schlacht` capability row still described the pre-fix hit resolution and claimed a
  verification that `[SEETHRUNOOP]` had just invalidated. Rewritten.
* The 繁中 checklist's own derivation instruction said to subtract **the two** preamble headings; a
  third was added on 2026-08-22, so following it literally now yields 34 against a correct 33.
  Reworded to count them rather than name a number.

-----

## 2026-08-22 - Unattended verification session: 8 defects found and fixed while working the register (builds 3309 → 3315, 42 commits)

**Shipped**: `build_number.txt` **3315**, `dist/` republished as the Native-AOT trimmed binary
(54.7 MB, `sha 8CA03D81BAAB`) with the DLL rebuilt alongside. Not a feature session — every fix below
was found by *running a verification row*, not by looking for bugs.

### The defects, in the order they surfaced

- **`[CADENCEGAP]`** — `Linie` dropped every same-millisecond inter-arrival sample (`>` where `>=`
  belonged), so a 17 ms callback reported the same 33 ms period as a 33 ms one. Seen in the UI:
  `CameraModifier` 496 calls / 17 ms against `ABP_Manny_C` 248 calls / 33 ms — twice the calls, half
  the period, where both had read 33 ms.
- **`[PARAMSSORT]`** — three grids sorted the **label** (`"9 (144B)"`) instead of the number, so
  Params ordered lexicographically. Fixed at all three sites, plus an address column that sorted as
  text. Two new AOT-sort guards; the click-through on the trimmed binary showed columns that
  actually discriminate (Params `19,19,19,17,17,16,15,15`; Offset `0x28…0x64`).
- **`[FREEZECFGNAME]`** — the freeze script bakes the class name into its runtime messages while the
  freeze itself reads `CFG.className`, and the product *instructs* the user to edit that CFG. Editing
  it made every message name the class you had just replaced. ⭐ The defect had been **pinned by a
  test** that used the baked literal as a convenient anchor, which is why it looked deliberate.
- **`[INVOKEHINTQUOTE]`** — an unescaped apostrophe (`"read it in CE's memory viewer"`) interpolated
  into a single-quoted Lua literal made the **whole `[ENABLE]` block a syntax error**, and Cheat
  Engine reports that by leaving `Active` at `false` with no dialog and nothing in the log. Any
  invoke with a large by-value struct return was affected. New `CeLuaQuotingTests` runs **19
  generators** through a Lua quote scanner — behavioural, because the offending value came from a
  variable ten lines above its use and a grep of the emitting line found nothing.
- **`[FORCESTATUSCLIP]`** *(filed LOW, not fixed)* — the Force status line is right-clipped at ~30
  characters with no trimming or tooltip, and the clipped tail is the clause whose own code comment
  says it exists because "on 256 instance(s)" reads as "all of them" without it.
- **`[SEETHRUTALLY]`** — `InvokeSetHidden` returns `bool` and **both call sites discarded it**, so
  `hiddenActors` / `hidden_count` / the UI card / the log's `disabled (N restored)` all reported
  **intent**. `Tick()` now records only what was applied.
- **`[SEETHRUNOOP]`** — and what that honesty exposed: on **UE 5.4** the object read out of
  `FHitResult` is a `UStaticMeshComponent`, so `InvokeSetHidden`'s Actor guard rejected every hit and
  **See-through was a complete no-op while every channel said it was working**. Fixed by trying
  `Actor` → `HitObjectHandle` → `Component` and taking the first that resolves to an actor
  (`ResolveToActor` walks `Outer`, bounded). ⭐ Cannot regress a working build: the first two members
  are still tried first, and `ResolveToActor` is the identity at hop 0 for anything already an actor.
- **`[DISTCOPY]`** — `build.ps1` reported a successful publish over a copy that never happened. A
  locked destination left `$exitCode` at 0, `Remove-Item` then deleted the correctly-built binary, and
  the success line printed the **stale** file's size — which is 54.7 MB exactly like a good one. The
  copy is now verified by **SHA256** and `dist/publish/` is kept when it fails.

### Verification closed

`Y10/Y13` (all four steps) · `MB3` (the CE half, through real `.CT` records) · `AA12/AA13` ·
`AE4–AE7` · `M1–M5` steps 2/3/4/5 and step 1's arms (c)+(d) · `B19` · `AF7` · `AF22/AF12/AF13` ·
`AC17` · `AF21` · `A11` · `A12` · `V11` · `Y12` · `W8` · Genau RIP decode (GNames + GWorld).
The 繁中 checklist went **50 → 35** sections.

### Two capabilities added because a row could not be verified without them

- **`seethrough_get_state` now returns `hidden_actors`** — the count alone could not be audited from
  outside, and it cost a full round of guessing (33 candidate actors walked, none hidden, no way to
  tell a wrong candidate set from a failed hide) before the set was exposed and the answer appeared
  immediately.
- **`tools/verify/seethrough_arms.py`** — two independent detectors that refuse to report a pass when
  the count never rises or when the DLL names actors whose own `bHidden` is not set.

### What this session is really about

Six of the eight defects are the same shape, and it is the one audit #4 named: **the report and the
reality computed by different code paths**. `[SEETHRUTALLY]` is the extreme case — the "reality" path
did not exist at all, which is exactly why the feature could be dead for as long as it was. Every one
of them was caught by insisting on a **second, independent witness** for a claim the system makes
about itself.

-----

## 2026-08-20 (evening) - Fourth pass on 3263: the UI and Cheat-Engine modes, 3 more defects, AA12/AA13 opened up

**No source change.** `build_number.txt` is **3263** and the tree is clean. ⚠ The caveat from the
entry below still stands and is worth repeating because it changes what a build means today:
`dist/UE5Dumper.dll` **was rebuilt** for the PEHOOK step-3b experiment (identical source), so its
bytes differ from the handed-over 3263 — and **`proxy_refresh.py` therefore reports all nine
deployed proxies as `*** STALE ***`, which is a FALSE ALARM**. `dist/UE5DumpUI.exe` was **not**
rebuilt and is still the 54.7 MB AOT-trimmed binary, which is what made the AOT-sort work below
possible without a rebuild.

This pass changed **mode** twice rather than finding more headless rows: first driving the real UI
with computer-use, then driving **Cheat Engine**. Both were productive, and the CE half found three
defects in one sitting.

### The UI pass — one batch closed, several completed

* **`PEHOOK` step 1** — the last non-UI-blocked step. Self-Test on a SIB-less build gives
  `✗ Add_IntInt(3,4) expected 7, got 0` with advice naming a **MIS-DETECTED vtable slot**. ⭐ Clicking
  it a second time returns a **completely different** string — *"The DLL REFUSED this call"* — because
  the validator had condemned the slot in between. Two state-appropriate advices from one button
  minutes apart is the row's headline claim (*the advice is chosen from `get_diagnostics`, not
  asserted*) demonstrated rather than argued, and it is the UI face of the `-3` refusal measured
  headlessly in step 3b.
* **`PEHOOKONCE` step 5 — batch CLOSED.** On a fresh Lushfoil the UI genuinely shows
  *"Connected — waiting for scan"* with **Start Scan** unpressed, so the pre-scan window is real in
  the UI and not only over the pipe. Live Funcs → Start before any scan gives the actionable
  *"Run a scan, then Start again"*; after a scan, Start again **without restarting the game** records
  **67 distinct functions / 98,236 calls**. The order-swap that used to poison the PE hook for a whole
  process now recovers inside one process.
* **`PASTECRASH` 4b + 4c** — and they turn out to be **two separate handlers**. The Copy button logs
  *"Clipboard copy FAILED — nothing was copied, the app is unaffected"* while the
  `Input-layer fault swallowed (#N)` counter does **not** move. 4b's safety half passes; its predicted
  undo residue did **not** occur (6 typed characters took exactly 6 `Ctrl+Z`), so the row's
  explanation of that residue should be treated as unconfirmed.
* **AOT sort — 2 grids → 8**, all on the `-Mode Publish` binary because a non-trimmed build passes
  with the bug present: Live Funcs, Detect Stats, Live Walker, Snapshot, Class Pivot, SPC Query.
  ⭐ SPC's is the largest sort exercised anywhere in the item — **12,153 rows** — which is where a
  per-row reflective comparer would be most visible. The **Props/Invoke picker** is the only named
  grid left and has **no fixture here** (it returns zero rows on every function tried).
* **`AF4`**, **`AE2`/`AE3` steps 1-3**, **`AF22`**, and **`L6`'s `X5` auto-snapshot clause** all pass.
  AE2's filter is recorded as the row demands (`DumperTest` → 22 results, 10 class-like then 12
  instance rows) and the three headers it discriminates on are mutually unmistakable — 1760 / 12 /
  928 properties.

### The Cheat-Engine pass — `AA12`/`AA13` finally reachable, and three defects

Launching CE flips the UI to **AOBMaker Connected**, which is what *enables* the per-row **Freeze**
button (`PropertySearchPanel.axaml:285`). That single fact unblocked a batch that had **six steps and
zero evidence**.

* **`AA12`/`AA13` step 1 — PASS**, with the release as its control: `TickCount` held at **9999** across
  10 s on a field that had been climbing ~1 Hz, the Lua window auto-closed, the record stayed ticked,
  and unticking let it resume **9999 → 10039 → 10048**.
* **Step 6 — PASS.** A float and a double freeze coexist (`555.5` / `777.75`); unticking one released
  **only** that one. The differing widths mean a shared write path would have shown up as one
  clobbering the other.
* **Step 2 — HALF.** The message half is exactly right (*"nothing was frozen: g_invokeMailbox symbol
  not found -- is UE5Dumper.dll injected?"*); the untick half fails.
* **Step 3 — the fixture was wrong**, and finding that out was the result: `NiagaraComponent` previews
  as `0 (CDO default)` but the freeze reports **2 live instances**.

**Three defects, all with reproducers:**

* **`[FREEZEUNTICK-2026-08-20]`** — a bail-out that applies nothing leaves the record **`Active=true`**,
  on **both** the helper-missing and DLL-not-injected paths. The generator *does* emit
  `if memrec then memrec.Active = false end`, and assigning the same property externally works — so it
  is the in-ENABLE assignment that does not survive. This is precisely what CLAUDE.md's rule forbids,
  and it makes `AA12`/`AA13` step 3 non-discriminating until fixed.
* **`[FREEZEINJECT-CRLF-2026-08-20]`** — "Inject Freeze Helper" reports
  `wrote 58345, stream has 57208`. The arithmetic is exact: the helper is **58,345 bytes with 1,137
  CRLF endings**, and 58,345 − 1,137 = 57,208. CE stores it LF-normalised; the check compares a CRLF
  byte count to an LF stream. **The write succeeds** (`findTableFile` → FOUND) — only the verification
  is wrong, but it tells the user the setup step failed.
* **`[CDOSCOPE-2026-08-20]`** — the `(CDO default)` marker is decided on an **exact** `ClassPrivate`
  match while Force and Freeze on that same row scope **derived**. A row can read "nothing live" and
  the action then hit two instances. Same exact-vs-derived split as `FREEZESCOPE` / audit #5 `A6`, one
  layer up.

### Method notes worth keeping

* **Read `memrec.Active` from CE's Lua Engine, never from the checkbox** — measured this pass: red ✗
  is **ACTIVE**, empty box is inactive. Reading the ✗ as "failed" would have inverted every finding
  above.
* **Check CE's title bar before ticking.** The process list reorders between openings, and one attach
  landed on `UE5DumpUI.exe`; a freeze against the wrong process fails in a way that looks like a
  product defect.
* **The CE-Lua hygiene contract is confirmed live**: `[Freeze] Started/Stopped` lines are silent by
  default and appear only after `UE5_DEBUG=1`.

-----

## 2026-08-20 (later) - Third verification pass on 3263: ST1 and PEHOOK closed out, 1 new defect, a §4.4 retirement, and one void run caught

**No source change.** `build_number.txt` stays **3263** and the tree is clean.
⚠ **Correction to the entry below it: `dist/` is NOT untouched any more.** PEHOOK step 3b needs a
DLL whose SIB pattern alternates are disabled, and the row sanctions building one; that build and
the restoring rebuild both overwrite `dist/`. Source is identical (git clean, `-NoBumpBuildNumber`
on both), so the binaries differ only by build non-determinism — but they are **not** byte-identical
to the handed-over 3263, and `proxy_refresh.py` now reports all nine deployed proxies `*** STALE ***`
as a result. **That report is a false alarm; do not act on it** (it compares SHA-256, and the sizes
still match exactly).

**Hosts, one at a time, each killed when its rows were done:** DumperTest Development (shipping DLL
*and* a SIB-less variant injected by path), **DQ7R**, **Lushfoil**, **EVERSPACE 2**, **Echoes of
Aincrad Demo**, **Satisfactory**.

### Two batches closed

* **`ST1` — all six steps, headless.** Two techniques made a batch whose table repeatedly says
  *"a paused/menu game"* and *"ordinary gameplay for a few minutes"* runnable with nobody present:
  **suspending the UE game thread** is a scriptable and strictly *stronger* form of every idle-game
  precondition (frozen = exactly 0 ProcessEvent fires, where backgrounding still ticks ~120/s), and
  where a log line structurally could not decide a step, **an observable side effect in memory
  could** — steps 4 and 6 were settled by watching `AActor::bHidden` flip after a resume, not by an
  absent log line.
* **`PEHOOK` — 1b/2/3/3b/4/5/6/7/8.** Only the UI Self-Test *text* (step 1) and the structurally
  unreachable 3c remain. Step 3 reached its **terminal 3/3 + "giving up"** state for the first time;
  step 5's false-positive guard was exercised for the first time (previous runs only showed the
  branch was never entered).

### One new defect

**`[INVOKEINHERIT-2026-08-20]`** — `Ubel::WalkFunctions` never climbs `SuperStruct`, so
`UE5_FindFunctionByName` can only resolve a function a class **declares**. `AActor`'s 140 functions
are unreachable on every subclass; 11 of 42 live objects can invoke *nothing at all*; and
`UE5_SetDebugCamera` resolves `ToggleDebugCamera` off the live CheatManager's class, so the shipped
Debug Camera toggle fails on any game with a derived CheatManager. Reproducer committed
(`invoke_inherited_function.py`, exits 1 while the defect stands). **Not fixed** — and the fix shape
carries a warning: do *not* make `WalkFunctions` inherit, because it is also what LISTS a class's
functions; the change belongs in the resolvers.

### A documented rule retired, and a blast radius measured

* **`working-lessons` §4.4's Everspace 2 evidence is withdrawn.** It recorded a KismetMathLibrary
  no-op diagnosed *while the hook was in the wrong vtable slot*, with the stub hypothesis "never
  re-verified against the corrected hook". It has now been re-verified on that same title:
  `Add_IntInt(3,4)` returns **7** at `vtable+0x278`. The failing pattern the section rested on has no
  surviving instance here.
* **The open `Serie::GetString` dropped-`Number` lead now has a measured blast radius** (it was
  explicitly "not measured"): 40 of 42 live objects are named differently by two pipe commands, and
  6 of 6 are **unfindable by the name the DLL itself reports** — the bare name silently resolving a
  *different* object. 71 call sites, 4 of which pass a Number. Still "do not sed it": the
  discriminator is display/identity vs matching.

### One run caught and voided

**`G3` steps 3+4 on Satisfactory produced a full set of coherent readings from a game that had never
booted.** Launching its shipping exe directly puts up *"Failed to open descriptor file
`../../../FactoryGameSteam/FactoryGameSteam.uproject`"* — UE resolves the `.uproject` relative to the
exe, and this title's exe lives in `Engine\Binaries\Win64\`. Injection, the pipe, and the scan all
succeeded against a dead engine, and the numbers looked *specific*: GNames and GWorld resolved (symbol
exports work as soon as DLLs are mapped), `GObjects=0x0`, and `ExtraScanGObjects: No valid
FUObjectArray found (763 candidates tested)`. Relaunching via `steam.exe -applaunch 526870` resolves
**all four globals, 137,425 objects** — with `gobjects` at **the exact address the failed run had
found and rejected as empty**, which isolates "array not populated" from "wrong address" by holding
the address constant. Satisfactory is exonerated, `test-games.md` was right, and **G3 3+4 have no
fixture on this machine at all**. Written up as `working-lessons` §3.w.

### Also settled

`AA2/AA3` step 3 (mailbox driven from Python, witness checked against `ReadProcessMemory` across 29
classes — and the row's assertion narrowed, because a derived listing's `classWitness=0x0` is
*correct*) · `U8` on a staged fixture (`Number := 8` written and restored; Value Search matched the
same 8 bytes, the bare string matching the CDO instead) · `L4/MB1` both rows (the invoke ROUTE made
observable by freezing the game thread: 5 ms + success vs 5006 ms + timeout) · `L10` step 6's
retention clause (a 30-day `TeleportCoords` file survives while a 30-day `Snapshots` group is deleted
in the same launch — the control that makes it mean anything) · `G11` steps 3 and 4 (Tier 2 has now
failed to fire for three *different* reasons; an offline exe scan proves the silence is correct on a
tag-stripped title) · Solide `capped` and `FREEZESCOPE` step 4 (unlocked by noticing Solide's pool is
**not Actor-only**: an 819-instance `ActorComponent` sweep gives `held=256, truncated=true`) ·
`PEHOOKONCE` step 3 in its literal pre-scan form.

### Rig lessons worth more than the rows

`working-lessons` §1 now carries **four** variants of one mistake, all found in this batch: a log
window coarser than the events it separates *reports a confident wrong answer*. Line-count slicing
across several growing files; a one-second timestamp watermark between cells milliseconds apart
(this one printed `FAIL` on a run whose own `result=-5` proved the opposite); a counter read outside
the timed window; and a byte offset recorded before a process start that **rotates** the log. The
reliable primitive is a before/after **count**. Also §1.y: `find_instances` without `exact_match` is
a *name substring* match, so "the first live instance of `Actor`" was a `UActorSequence` — and a
wrong-type invoke left queued can drain later against a non-actor.

-----

## 2026-08-20 - Second live-verification pass on 3263: 26 register rows settled, 2 new defects, 4 rigs (no build change)

**No source changed.** Everything below is `docs/todo.md` evidence, `tools/verify/` rigs and the
gitignored run ledger; `dist/` and `build_number.txt` are untouched at **1.0.0.3263**. Both CI gates
(`check_live_verification.py`, `check_audit_register.py`) stayed green after every commit.

**Hosts used, one at a time and killed after each group** (`working-lessons` §3.9): DumperTest
Development and Shipping, **The Adventures of Elliot** (UE504, 84,990 objects, `dxgi` proxy) and
**DQ7R** (UE**4.27**, 149,408 objects, `version` proxy) — the first UE4 title driven end to end in
this programme, and the largest install on the machine.

**Two new defects, both reproducers committed.**

* **`[STAGELOCK-2026-08-20]`** — `AC11` step 2's premise is wrong. `CopyProxyStaged` publishes with
  `File.Move(overwrite:true)`, and a target carrying an **image section** refuses the replacing
  rename with `ERROR_ACCESS_DENIED` (5), not the `ERROR_SHARING_VIOLATION` (32) the old direct
  `File.Copy` raised. .NET turns 5 into `UnauthorizedAccessException`, which is not an `IOException`,
  so it misses both arms of `DeployAsync`'s filter and the user is told **"Access to the path is
  denied."** — a message naming no path — instead of "File locked (game running?)". Established three
  independent ways: an OS-level probe (`tools/verify/ac11_locked_rename.py`), a throwaway xunit test
  against the real `CopyProxyStaged`, and finally **a live game** (Elliot running, Force Overwrite →
  `[EROR] … Access to the path is denied.`, status `ErrorOther`). `UndeployAsync` and the orphan
  sweep already carry the `catch (UnauthorizedAccessException)` arm; `DeployAsync` is the only one of
  the three without it, and the only one whose write became a rename.
* **`[ORPHANCANCEL-2026-08-20]`** (LOW) — cancelling a leftover-proxy cleanup mid-row leaves that row
  **untallied, still ticked, and its folder chain half-pruned**: the token is passed *into*
  `RemoveOrphanProxyAsync`, so it recycles the file and *then* throws, skipping `files +=`, `ok++`,
  `row.IsSelected = false` and `DropOrphanRow`. The log showed **three** recycles under a summary
  saying two. Audit #4's root cause verbatim — the report and the reality computed by different code
  paths.

**Batches closed.** Audit **L9** (AE13 / AE20 / AE30) is fully verified and its heading flipped, so
the register's open count went **40 → 39**. **L6** is complete but for the CE-only `X8` and the
maintainer-only `X12`; **`[AUTOREFRESH-2026-08-19]` is complete — all seven steps**; **F5** is
complete (steps 1–3).

**Things that could only be learned by running them**, now recorded next to their rows:

* `mtime` **cannot** witness a re-deploy — `File.Copy` carries the source's timestamp through
  `File.Move`, so the deployed proxy is byte-*and*-timestamp identical after a second deploy. Use
  `ctime`.
* `AUTOREFRESH` step 6's "drill → stays OFF" **expectation is wrong**: a field drill never calls
  `StopAutoRefreshTimer`, and what actually happens (re-target to the new root, 12 consecutive 10.0 s
  ticks) is better.
* `AC13` is **not observable as written** — the IPC figure only exists via `DiagnosticsProbe`, which
  returns early on exactly the disconnect the row prescribes.
* `AE27`'s Package filter is a **prefix** match on values beginning `//`, so `Script` matches nothing
  and reads exactly like the blank-memo defect it is meant to catch.
* `AF8`'s `force_field` takes **`kind`** (defaulting to `"bool"`), not `mode`, and a numeric `value`
  — the string `"-5"` parses to `0.0`, giving `held:0` that looks like a failed fix.
* A game window **steals focus back**, so a computer-use click on the UI behind it silently
  re-activates instead of pressing; `tools/verify/front_window.py` first.

**Rows that cannot be closed on this machine, with the measurement that proves it** — `G7`-style
results rather than "not tried": `Z8` (DQ7R's whole pool is 51,255 UFunctions, 51 % of the cap;
ratio ≈ objects ÷ 3, so ~300k objects are needed), `A7` (a full 149,408-object `FindByAddress` with
the deep cap at its 4,096 maximum takes **152 ms** — no window to disconnect inside), L3 step 1
(**none** of `GWLD_TQ_3/TQ_4/GOBJ_PS1/PS6` has ever won across **170 scan logs / 25 processes**),
`AD18`'s `dinput8` arm (**no** installed title imports the name, all 16 exes read), `Z13`/`Z12`-deep
and `AE30`'s control.

**New rigs:** `ac11_locked_rename.py`, `ac10_kill_midstream.py`, `ae20_orphans.py`,
`af7_af8_pipe.py`.

-----

## 2026-08-19 (night) - The first live-verification pass on build 3263: 13 register items settled, 2 new defects found, 12 rigs added (no build change)

**Nothing shipped and no source changed** — `build_number.txt` stays **3263**, `dist/` is untouched
(54.7 MB AOT UI, 2,879,488-byte DLL). This entry records the *verification* programme the handover
asked for, run unattended against the shipping build.

### Register items settled

| item | outcome |
|---|---|
| `AA38` 1/2/3 | ✅ re-confirmed on **3263** (the ✅ was earned on 3262). Refusal names `atcuf64.dll` — Bitdefender's own filter, present in every run on this machine |
| `B25` | ✅ **both** branches, on two purpose-built marker exes. 3,886 vs 10 log lines separates "swept the tables" from "refused before starting" |
| `Genau RIP decode` | ✅ both halves — candidates **4085 → 4083**, reproduced exactly over 4 runs; GWorld unmoved |
| `F5` 1+2 | ✅ 204,758 wire lines across two concurrent connections, 1,179 interleaved watch events, **zero malformed** |
| `MB3` | ✅ ordinary dispatch: 50 consecutive mailbox commands, 0 failures, 0 `[ERROR]` lines |
| `A3` 1/2/4 | ✅ **151 classes** contribute >1 FVector field. ⚠ the row's "use Float" is wrong on UE5 — LWC makes FVector a double |
| `FL1`/`FL2` | ✅ stale staging file swept, **fresh one survived** — the age guard proven, not assumed |
| `SE1` | ✅ announcement *and* **597 `[SCAN*]` lines** rerouted into `init-0.log` |
| `MB2` | ✅ foreground toggle 0→1→1→0 on a host with all three pointers `not_found` |
| `AB14`/`AB16` | ✅ 834 enum/byte candidates; the Origin filter **partitions 278+94=372 exactly** |
| `A6` step 3 | ✅ derivation, not substring — `Character`=1 vs `CharacterMovementComponent`=7, with a reachability control |
| `A8` | ✅ 7/7 on OCTOPATH. ⚠ the row's "(none available here)" was **wrong** — OCTOPATH is flat and installed |
| `A7` | ⛔ **not observable here, measured**: 0.11 s over 273,956 objects. Needs a pool ~100× larger |
| GROUP 7 | 🟡 **nine titles swept.** `U2` CPN **all-false** across UE4+UE5; **no Tier 2 line anywhere**; `X2`'s >5,000-class title found (Avowed `total_classes` 5,102) |

### Two new defects, both found in passing

* **`[RELAUNCHPIPE-2026-08-19]`** — a game that **relaunches itself** ends up with our DLL mapped and
  **no pipe server at all**. `UE5_StartPipeServer`'s one-shot `CreateFileW(OPEN_EXISTING)` guard
  races the dying first process. Reproduced 3/3 on OCTOPATH and **proven by repair**.
* **`[PROXYDEPS-2026-08-19]`** — six proxy objects carry no recorded header dependencies, so a `.h`
  edit may not rebuild the four shipped proxy DLLs.

Plus a blocking machine-state fact: **all nine deployed game proxies were stale** (pre-3263).

### Rigs added under `tools/verify/` (Python only — ad-hoc PowerShell is blocked here)

`inject.py` (with a stale-module guard) · `launch_dumpertest.py` · `build_dll.py` · `call_export.py` ·
`proxy_refresh.py` · `title_sweep.py` · `b25_marker_exes.py` · `genau_rip_ab.py` · `f5_envelope.py` ·
`mailbox_poke.py` · `fl_staging_sweep.py` · `se1_log_reroute.py` · `ab_radar_batch.py` ·
`a3_struct_path.py` · `a6_derivation.py` · `a8_flat_layout.py`.

### Method notes that cost real time and are now written down

**`working-lessons.md` §3.8** — this machine has **two Visual Studios**; `build.ps1` takes the newer
(MSVC 14.51), so pointing a builder at 2022's `vcvars64` mixes toolsets and fails at LINK with
unresolved `__std_rotate` in a file you never edited. **§3.9** — "one game at a time" is a
**correctness** rule: a second injected host logs `pipe already exists … skipping auto-start` and
never scans, so every reading from it is an absence the injection itself caused.

⚠ **Three quantities are all called "the UE version"** — the cached `ueVersion`, the
`FindAll: UE Version = N` log line, and `get_pointers.ue_version` (which is **after** any runtime
raise). Avowed detects 503 then raises to 504; comparing the wrong pair manufactures a G11 false
alarm, and did.

-----

## 2026-08-19 - Audit #5 closed out (166 → 4 of 297) + the field-reported defect queue (12 → 1), in 32 commits (build 3263)

**One unattended programme, one entry.** This is the rollup for the 32 commits between `9062f08f`
(build 3261, the A12 fix) and `10b00cf8`. Two queues were emptied in parallel:

| queue | before | after | what is left, and why |
|---|---|---|---|
| audit #5 register (`check_audit_register.py`) | **166 open of 297** | **4 open of 297** — 0 HIGH · 0 MED · 3 LOW · 1 INFO | `AB9`, `A10`, `AA39`, `AB23` — each left open **deliberately**; reasons below |
| the field-found OPEN FIXES INDEX in [todo.md](todo.md) | **12** | **1** of the original twelve (`[STALEDLL]`(a), a maintainer-only file deletion) | the audit itself then surfaced **3 new deferred items**, so the index reads **4 rows** — see "the index is 4, not 1" below |

⚠ **NOTHING in this entry has been verified on a running game.** Every fix is unit-pinned,
negative-controlled, or rig-covered offline; the live checks are queued in todo.md's
`## Pending live-game verification`, which grew to **40 open batches** as a direct result. Treat the
whole programme as *shipped and unproven* until that register moves.

### The twelve audit layers (L1–L12)

Audit #5 scanned the **48,950 lines authored before 2026-06-01** that audits #3 and #4 structurally
could not reach. Closing it ran as twelve batches, one per segment, each ending in a negative control:

- **L1** `9270046c` — DLL engine (`Ubel`/`Serie`/`Genau`/`Aura`). Enum reads of size 1 stopped
  sign-extending the UHT `MAX=255` sentinel into a miss; the `FString` count cap went 256 → 8192
  after re-deriving that it bounds only a *garbage* count's allocation, not the hot path;
  `TOptional<FText>` decodes through `ReadFTextString` instead of reading `FText+0x10`, which is the
  `uint32` Flags.
- **L2** `ab0fd6a6` — `Radar` value scan. `EnumProperty` is now scannable (mapped to `UInt8` and
  added to the `NumericAll` union); integer parsing is base 10 unless `0x`-prefixed, so a leading
  zero no longer means octal-for-ints and decimal-for-floats inside one meta scan; the group witness
  assignment gained an augmenting-path repair so one leaf can no longer appear in two slots.
- **L3** `cda2f720` — headers / `Himmel`. **The durable one:** nothing validated a signature's
  `(instrOffset, opcodeLen, totalLen)` against its own pattern bytes. Now enforced twice — by
  `extract_patterns.py --check` *and* by a compile-time `ASSERT_RIP_GEOMETRY` over all five tables —
  and negative-controlled 6 for 6 against four historical defects plus two invented slips. Four real
  RIP-decode offsets were wrong and are corrected (PS1 23→21, PS6 14→12, TQ_3/TQ_4 3→0).
- **L4** `569a1d59` — `Mimic`/`Sein`/`Flamme`. `HandleInvoke` routed on `functionFlags`, a
  DLL-filled **output** field, so a second CE FIRE routed on whatever the previous command left
  behind. Fixed by re-reading the flags from the `UFunction` rather than by promoting the field to
  an input — the latter would have been a **meaning** change the contract hash cannot see.
- **L5** `e8893e5a` — the three standalone CE Lua scripts, all covered by the real `lua` 5.4.6 rigs
  in `scripts/tests/` (dissect 50→83, freeze 132→154, invoke 63→91 checks).
- **L6** `6fc00e4d` — `MainWindowViewModel`. Disconnect now resets **all** process-scoped panels
  (was 3 of ~15); the three long exports thread a real `CancellationToken`; the Dump All completion
  line is composed from the actual `DumpResult` counts instead of the file's byte length.
- **L7** `9ef5b8ca` — UI services: data durability, honest failure levels, VDF parsing.
- **L8** `a87706c7` — VMs + scoring. Four findings turned out to be one theme — *a truncation or
  deadline signal exists and is discarded before the user sees it* — so the wording lives in one new
  `Core/PartialResultNotice.cs` rather than in four new spellings.
- **L9** `d4fdd418` — ViewModels/Core/DTOs. Two rows were closed by **re-deriving against current
  code** and finding them already fixed, rather than by re-fixing them.
- **L10** `ec72d7c0` — Views / app root. **A user-sortable DataGrid column is AOT-safe only if its
  Binding path equals its `SortMemberPath`, or a comparer is wired.** Swept the whole tree instead of
  trusting the list: 34 grids / 162 sortable columns / **30 unsafe across 10 sites**, of which the
  findings named six. `DataGridSortWiringTests` now machine-enforces the pairing offline.
- **L11** `179d2f80` — the last LOW batch. One theme runs through all twelve rows: **the report and
  the thing it reports on were produced by different code.** The worst was the DataTable RowMap
  drill, whose crumb printed the DLL's true row total over a grid holding a fixed 64-row page.
- **L12** `5374e662` — 25 of the 26 INFO rows. See the gate work below.

### Mailbox contract 2 → 3 (`2c2a950c`) — additive, `MAILBOX_CONTRACT_MIN` stays 1

`[FREEZESCOPE]`: a Property Search row for an **inherited** field is keyed to the class that
*declares* it, so freezing a pawn's `bCanBeDamaged` submitted `Actor` and the exact-name pool
returned one incidental `ChaosDebugDrawActor` out of a 25,179-object level. `CMD_LIST_INSTANCES`
gained an opt-in `LI_IN_DERIVED` flag routed to `Aura::FindInstancesDerivedFrom`, plus
`LI_OUT_TRUNCATED` when the pool is capped.

**Why this is additive:** `MailboxData` grew only at its tail (`cmdFlags` in, `cmdOutFlags` out), the
flag defaults to `0` = the old exact match, the handler clears it after every use so it cannot be
inherited, and the 16-byte derived-page format is unreachable without it. A pre-contract-3 `.CT`
keeps the exact-match pool byte for byte. `check_mailbox_contract.py`'s golden block records why.

The same commit fixed `[FREEZESTUCK]`: an abandoned freeze reported its failure with a `print()` into
a Lua Engine window hygiene had already closed, while CE still showed a ticked record — so the user
was told a cheat was applied while nothing was written. The record now unticks from a one-shot timer
(deferred, because `Active = false` runs `[DISABLE]` synchronously and that block calls `stop()`).

### Gate work — the part that outlives the fixes

Five gates got stronger, and one had a hole big enough to matter:

- **`check_mailbox_contract.py` could not see five of the six copies of the layout** (`AA36`). It
  hashed `Mimic.h` and read `CeMailboxLayout.ContractVersion`, and was blind to both standalone CE
  helpers, the shipped `.CT`, and both offline Lua rigs. **A gate with a hole reads as covered.** It
  now compares **67 literals across those 5 mirrors** against offsets *computed* from the packed
  struct, and the registry is **closed** — an unregistered layout-shaped constant is a hard failure.
- **`check_audit_register.py` did not enforce ID uniqueness** (`69fa412a`). `AE11`/`AE12` each sat on
  two rows, so marking one closed closed both in every derived count.
- **`check_axaml_strings.py` was red for four commits** and was wrongly written off as pre-existing
  and a false positive. It was neither: `ec72d7c0` had resolved a `StaticResource` key by
  interpolation, so eight real keys became invisible to static inspection — **exactly the property
  the gate exists to defend.** Fixed at the call sites, not by exempting the checker (`a1bdd205`).
- **New rigs:** `tools/verify/compile_sdk_header.py` puts the real emitter's output in front of
  `cl.exe` (nothing in this repo had ever *compiled* a generated header — they were only read), and
  `tools/verify/pe_pattern_regression.py` pins the ProcessEvent pattern set across 22 shipped games.

### Field-reported defects (the OPEN FIXES INDEX)

`[PASTECRASH]` took three commits and is the one worth reading: a clipboard read that failed inside
Avalonia's `TextBox.Paste()` surfaced as an unobserved dispatcher exception and **terminated the UI
31 minutes into a connected session**. The guard swallows only what a pure, narrow classifier
positively identifies as a platform input-layer fault — and `4365a1eb` reverses part of its own
predecessor on a weighing, not a fact: a swallowed `Ctrl+X` orphans an undo snapshot, but that is a
no-op `Ctrl+Z` against a terminated process taking a loaded object tree with it.

Also closed: `[SDKHDR]` (the array extent was baked into the *type* string, so 5 of 75,342 emitted
lines were not valid C++), `[PEHOOK]`/`[PEHOOKONCE]` (a failed ProcessEvent **detection** was
permanent; three sentinels now distinguish re-armable from terminal), `[PIPEBUSY]` (at-capacity
logged an ERROR every second — 1,826 lines in 31.5 min, evicting real diagnostics as the log
rotated), `[CLASSTOTAL]`, `[CONTAINERCAP]`, `[SLOTSYM]`, `[STALEDLL]`(b), `[PROXYLOAD]` and
`[AUTOREFRESH]`.

**The index is 4 rows, not 1.** The original twelve went to one — `[STALEDLL]`(a), deleting a
6-month-old `UE5Dumper.dll` from CE's install folder, which is maintainer-only. But the audit work
itself surfaced three new **deferred** items that now sit in the same table: `[PROPSEARCHCAP]` (a
feature — Property Search has no Max control), `[VOLUMEROOT]` (three sites ask `DriveInfo` about a
mount point and answer about the host volume) and `[SCANIDENTITY]` (needs a product decision plus a
live game with mid-scan object churn). None is a regression from tonight.

### The four audit findings left open on purpose

`AB9` — DllMain does filesystem work under the loader lock; a half-fix is worse than the defect, so
it needs its own session. `A10` — needs the by-reference→by-value restructuring `U5` deferred; a
partial invalidation would dangle held references. `AA39` — its prescribed fix was **measured to be a
no-op**; do not re-raise. `AB23` — interning `GroupSlotMatch::ownerClass` touches `Aura.cpp` and
`Fern.cpp`, which **no test target compiles**, so it is in-game-only work.

### Method notes worth keeping

- **No test target compiles most of `dll/src`.** Every pure decision rule fixed in a `.cpp` this
  programme was *moved into a header* `dll_helpers_test` includes, and given a negative control.
  That is now the standard move, not an improvisation.
- **Refutations are results.** `bd9b6d1b` refutes "ninja records no header deps" — it was a
  measurement artifact (CMake emits rules into `CMakeFiles/rules.ninja`, so grepping `build.ninja`
  returns 0 and looks like breakage). `AA39`, `Z11`, `AD16` and halves of `MB2`/`SE1` were also
  refuted and are recorded as do-not-re-raise.
- **A PASS can carry a wrong procedure.** `141e8119` corrects verification records whose evidence
  never covered the step they closed — `V6`'s auto-refresh half could not have been performed on the
  build it was recorded against. Recorded as a half-pass in place, with the reason inline.
- **`9a8ddd24`**: two ViewModel tests set an `[ObservableProperty]` whose generated hook is
  fire-and-forget, then awaited a *second* call that raced the first. Measured in-process at 4,000
  iterations — 1.25% / 2.275% / 0.625% flake. Whole-process runs cannot sample this, which is why
  160 clean cold runs had proved nothing.

### Build

Native AOT publish clean at **build 3263**. ⚠ **3262 was deliberately skipped**: `dist/` already
carried a 3262 and so does the maintainer's second machine, and a plain `build.ps1` had since
overwritten local `dist/` with a **non-trimmed** 106.8 MB exe under that same number. Reusing it
would have made one build number mean three different binaries. Note that **every** `build.ps1`
invocation bumps `build_number.txt`, not just `-Mode Publish` (use `-NoBumpBuildNumber` to suppress).

-----

## Older entries

Builds **2779–3261** (2026-08-10 … 2026-08-17, 75 entries) were moved to
[`archive/dev-log-2026-08-pre-build-3263.md`](archive/dev-log-2026-08-pre-build-3263.md)
on 2026-09-26 (nothing edited, only moved).
Builds **2220–2747** were moved to
[`archive/dev-log-2026-08-pre-build-2779.md`](archive/dev-log-2026-08-pre-build-2779.md)
on 2026-08-25 (nothing edited, only moved). Everything before that is in the four older
archives that file links.
