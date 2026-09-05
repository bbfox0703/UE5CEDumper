# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> For detailed specs, implementation history, and debugging notes, see the **[docs/](docs/)** directory.

-----

## Build & Deploy
- After making code changes, always do a full rebuild and verify the build output is actually updated before testing. Never assume a build succeeded without checking.
- **Hand over an AOT-TRIMMED build, not the plain one.** `build.ps1` with no `-Mode` produces a
  self-contained **non-trimmed** exe (~107 MB); `-Mode Publish` produces the Native-AOT **trimmed**
  binary that ships (~54 MB). They are not the same program: reflection-shaped code — JSON without a
  source-generated context, MVVM bindings, `ComboBox.SelectedItem` bound to a boxed value, the
  reflection-based DataGrid column sort — compiles and runs fine untrimmed and fails **only** after
  trimming. **Every AOT bug in this repo's history was found by the maintainer re-compiling with AOT
  after being handed a non-trimmed build**, which costs a round trip every time.
  Use plain `build.ps1` / `-Target DLL` / `-Target Test` for fast iteration, then run
  `-Mode Publish` **before saying a UI change is ready to test**. It enters the VS DevShell itself
  (Native AOT links with MSVC's `link.exe`; without it ILCompiler dies with `exited with code 9009`,
  which reads like a broken toolchain rather than a missing environment).

-----

## Code Changes
- When asked to refactor or rename modules/files, make actual code changes (move files, update imports, rename classes) — not just documentation updates. Confirm structural changes before proceeding to docs.

-----

## Debugging
- When fixing bugs, verify the fix against the actual memory layout or data structure rather than assuming. If the first fix doesn't work, re-examine fundamental assumptions about the data format before iterating.

-----

## Git Operations
- When creating PRs, check for branch divergence and resolve merge conflicts before attempting `gh pr create`. Run `git status` and `git log --oneline -5` first.
- **Line endings are pinned by `.gitattributes`, NOT by your git config — do not "fix" them with
  `core.autocrlf`.** `* text=auto eol=lf` is set repo-wide (2026-08-23). This exists because
  `core.autocrlf` is **machine-local**: on this PC it is unset at `--local`/`--global` and **`true`
  at `--system`** (the Git-for-Windows installer default in `C:\Program Files\Git\etc\gitconfig`),
  which does not travel between the two development PCs. `.gitattributes` does.
  ⚠⚠ **What it prevents, and it is not cosmetic:** before the pin, a file that was `i/lf w/lf` got
  **rewritten to CRLF by `git checkout`**, and `git status` stayed **CLEAN** afterwards because the
  index never changed — so `git checkout -- <file>` could not be trusted to revert a staged
  experiment byte-for-byte. Every staged A* row on 2026-08-23 reverted from a `cp` byte snapshot
  for that reason. Measured before the pin: index `i/lf` on **1,021 of 1,035** tracked files, but
  the working tree was **339 `w/lf` / 679 `w/crlf` / 3 `w/mixed`**. Verified after: deleting
  `Genau.cpp` and re-checking it out returns it **byte-identical** (LF 5372, 0 CRLF).
  ℹ️ `text=auto`, never a bare `text`, so git keeps auto-detecting binaries — `VersionNeedleScan.h`
  carries a deliberate NUL inside a comment about NUL-terminated needles and must stay untouched.
- ⚠ **A whole-file diff on a small edit means the file was CORRUPTED, not reformatted.** Twice on
  2026-08-23 a patch script run through a shell heredoc had its `\\0` collapsed to `\0`, so Python
  wrote a **literal NUL byte** into a source file (`Mimic.cpp`) and then into `docs/todo.md`. Git
  and grep treat a NUL-bearing file as **binary**: the tells are `1483 insertions / 1470 deletions`
  on a 13-line edit, and `grep` answering `Binary file docs/todo.md matches`. **Check for NUL before
  blaming line endings**, and build a backslash numerically (`bytes([92])`) when patching through a
  heredoc.

-----

## Cheat Engine
- When working with CE Lua APIs, verify that functions/methods actually exist in the Cheat Engine Lua API before using them. Do not invent API calls.

-----

## Build & Dev Commands

### Unified Build Script (PREFERRED — always use this)

The `build.ps1` script handles VS DevShell setup, CMake, dotnet, and test execution automatically. **Always use this for building** — bare `cmake` / `dotnet build` commands will fail without the VS DevShell environment.

```bash
# Build everything (DLL + UI + Tests) — Release
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1"

# Build DLL + all 4 proxy DLLs (version/dinput8/dxgi/winmm — the injected artifacts)
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Target DLL

# Build UI only
# ⚠⚠ -Target UI ALSO OVERWRITES dist\UE5DumpUI.exe WITH THE NON-TRIMMED (~107 MB) BUILD.
#   Measured 2026-08-22: a plain `-Target UI` run ended with "[OK] UE5DumpUI.exe (106.8 MB)"
#   in dist\. The warning below used to name only -Target Test; BOTH do it, and so does any
#   run that reaches the publish step. The rule is not about which -Target you picked — it is
#   that ONLY `-Mode Publish` leaves an AOT-trimmed dist\. After ANY build that touches the UI,
#   re-run `-Mode Publish -NoBumpBuildNumber` before handing dist\ over, and check the SHA.
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Target UI

# Run tests only
# ⚠ -Target Test does NOT compile the DLL. It builds only the two test executables
#   (`--target utf8_helpers_test` / `dll_helpers_test`), which link HEADERS — so a syntax
#   error in Fern.cpp / Stark.cpp / any other .cpp passes it clean. A green -Target Test
#   after editing a .cpp measures nothing about that file. Use -Target DLL (or no -Target)
#   before claiming a C++ change builds. Learned the hard way 2026-08-04: "959 dll green"
#   was reported over a Fern.cpp that had never been compiled and did not parse.
# ⚠⚠ -Target Test IS NOT READ-ONLY: it PUBLISHES THE UI AND OVERWRITES dist\UE5DumpUI.exe
#   WITH THE NON-TRIMMED (~107 MB) BUILD. Measured 2026-08-20 with a before/after hash:
#   54.7 MB sha 3ebf02e7 -> 106.8 MB sha fa1e3f19, and the run says so out loud
#   (">> Publishing UE5DumpUI (Release, self-contained single-file)... [OK] 106.8 MB").
#   "builds only the two test executables" above is about the C++ side ONLY — do not read
#   it as "touches nothing else". This is the cheapest way to destroy the shippable binary:
#   the fast, safe-looking option silently replaces the AOT build the hand-over rule at the
#   top of this file exists to protect. After ANY -Target Test, re-run
#   `-Mode Publish -NoBumpBuildNumber` before handing dist\ over, and check the size.
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Target Test

# Debug build
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Mode Debug

# Clean + publish (distribution)
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Mode Publish -Clean
```

> **Why not bare cmake/dotnet?** The C++ DLL requires MSVC x64 environment (include paths, linker). `build.ps1` loads this via `Enter-VsDevShell` automatically. Running `cmake --build` without it causes `fatal error C1083: No such file or directory` for standard headers.

> ⚠ **CONFIGURE through `build.ps1` too, not bare `cmake`.** On a localized MSVC the `/showIncludes` prefix is localized (zh-TW: `注意: 包含檔案: `; `VSLANG=1033` does **not** change it — only the installed language pack does), and CMake bakes whatever bytes it observed into `build/CMakeFiles/rules.ninja` as `msvc_deps_prefix`. `build.ps1` pins the console to UTF-8 **before** configuring, so configure and build agree. Configure from a stock cmd/Git Bash shell and the bytes differ, Ninja matches nothing, and **a `.h` edit silently stops triggering a rebuild** — header-pinned tests then go green against objects that were never recompiled. `build.ps1` detects such a tree and re-configures it from scratch (`Repair-NinjaHeaderDeps`); to check by hand run `py tools/verify/build_dll.py --deps-check`, or `ninja -C build -t deps` directly, where a **C++ object** sitting at `#deps 0` is the bad state. ⚠ **Three kinds legitimately have none**, and the third cost a whole spurious finding (`[PROXYDEPS]`): `.rc.res`, `.asm.obj`, and an **empty translation unit**. Six proxy objects are the last kind — every `Lugner*.cpp` is wrapped head-to-toe in `#ifdef UE5_PROXY_<FLAVOUR>_BUILD` with its `#include`s *inside* the guard, and all four are compiled into all four proxy targets, so in the three that do not define a given flavour the file preprocesses to nothing and `/showIncludes` correctly prints nothing. **The discriminator is the object's CONTENT, not its dep count** — empty is ~530 bytes, the smallest real one is ~11 KB, and `--deps-check` classifies on exactly that.

### Manual Commands (reference only — prefer build.ps1)

```bash
# C++ DLL (requires VS DevShell loaded first)
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release

# C# UI
dotnet build "ui/UE5DumpUI/UE5DumpUI.csproj" -c Release
dotnet test "ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj"
```

### Git Submodules

```bash
git submodule update --init --recursive
```

-----

## Rules (MUST follow)

- **Language**: Code comments and UI strings in English
- **Single Instance**: UI app uses Mutex to ensure only one instance runs
- **Async everywhere**: All I/O, pipe operations, and alert actions must be async
- **Platform Abstraction**: Any system/OS-dependent call (P/Invoke, Registry, OS commands) MUST go through an interface in `Core` project. `Core` must NEVER contain direct platform-specific code
- **Log output**: All logs go to `%LOCALAPPDATA%\UE5CEDumper\Logs`, split by category into per-process subfolders. DLL: 5 files (init, scan, offsets, pipe, walk). UI: 3 files (init, pipe, view). Root `Logs/` has no loose files — only subfolders. **Retention is by AGE, not generation count**: `<cat>-0.log` is always the current run, the previous run is archived to `<cat>-YYYYMMDD-HHMMSS.log` stamped from its *own* mtime, and archives + whole per-process folders older than 21 days (`Grimoire::LOG_RETENTION_DAYS` / `Constants.LogMaxAgeDays`) are deleted at startup. 8 MB size cap per file still rotates mid-session. A file count could not express this — rotation runs on every process start, so N launches evicted everything earlier regardless of date
- **App-data layout**: `%LOCALAPPDATA%\UE5CEDumper` root holds only files that are **app-wide and fixed in number** (`dll-path.txt`, `ui-options.json`, `experimental.json`, `teleport-hotkeys.txt`, `window-state.txt`, `UE5CEDumper.{Machine}.json`). Anything **per-game** — one file per PE hash, so one more set on every game patch, forever — goes in its own PascalCase subfolder beside `Logs\` / `Reports\`: `Snapshots\` (`snapshots.<hash>.db` + `-wal`/`-shm`/`.denylist.json`), `Bookmarks\` (`bookmarks.<hash>.json`). A new per-game family MUST add a subfolder, not a root file. Folder creation, the one-time move of files left at the old root, and the age sweep are all `Services/AppDataFolderMaintenance.Prepare`, called **from the store's constructor** so nothing can read the folder before it is migrated (a composition-root call site is one reorder away from silently reading an empty folder). Three invariants: a game's files **move and expire as a GROUP** (a `.db` migrated without its `-wal` has silently dropped every transaction the WAL held — a blocked move rolls the whole group back); **"unused" = `LastWriteTimeUtc`, STAMPED by the store on use** — never last-access, whose NTFS auto-updates are ON by default (`fsutil behavior query DisableLastAccess` = 2), so AV/backup/indexer reads keep every file looking like today and the sweep silently never fires; and **retention is the STORE's call, not the folder's** — `Snapshots\` passes `Constants.DataMaxAgeDays` (21), `Bookmarks\` passes `0` (sweep off, deliberately: hand-placed navigation nobody can regenerate ≠ a regenerable multi-GB capture; see docs/todo.md, do not "finish" it by enabling the sweep)
- **Magic words management**: All magic strings kept in one file per project with proper comments
- **UI Strings**: English only. All UI strings in `Resources/Strings/en.axaml`, referenced via `StaticResource` bindings
- **Keyword search boxes (space = AND + per-keyword memory)**: Every client-side keyword/filter box in the UI MUST behave identically:
  - **space = AND**: whitespace-separated terms are combined with **AND** (term-level AND, field-level OR) via `Helpers/ObjectTreeFilter.MatchesAllTerms(string[] terms, params string?[] fields)` — never a single `.Contains`/`IndexOf` over one concatenated string. Split raw input with `ObjectTreeFilter.SplitTerms`. The 3-string overload is the allocation-free Object-Tree hot path (486K nodes); every other panel uses the `params` form. Server-side matchers that can't AND client-side (ValueSearch, SPC query-time) are the only exemption.
  - **per-keyword memory**: remember keywords the user actually typed *and that produced matches* via `Helpers/KeywordSearchMemory` (LRU `History` bound to an `AutoCompleteBox.ItemsSource`; `AutoCompleteBox` uses `PlaceholderText` not `Watermark`, and `Text` must be `Mode=TwoWay`). VM wiring = field + `XxxHistory` property + ctor probe `() => (FilterText, Results.Count > 0)` + `_mem.Schedule(value)` in the `OnXxxChanged` handler; `Flush()` before clearing the box on tab-switch/navigation.
  - **async/server-side count**: filters whose match count only lands after a pipe round-trip call `KeywordSearchMemory.Commit()` from the reload-completion path, NOT `Schedule` — a debounce probe races the async reload and reads a stale count.
- **UE offsets**: All UObject/UStruct offsets must be dynamically verified via OffsetFinder, never hardcoded
- **AOT compatible**: All C# code must be Native AOT / trimming compatible. No reflection-based APIs — use source generators instead (e.g. `[JsonSerializable]` context for `System.Text.Json`, `[ObservableProperty]` for MVVM). The UI is published as a self-contained trimmed binary
- **Module naming (Frieren convention)**: Every new C++ DLL module (a file with its own namespace) MUST take an unused character name from the **Frieren roster** in [docs/naming-convention.md](docs/naming-convention.md) — never a plain-English name (the historical plain-English modules have all been migrated: `Cancel`→`Tot`, `PackedItem`→`Lineal`, `ValueScan`→`Radar`). Add the required header comment (`// {Name} — {中文名} ({meaning})` + a one-line function description) and flip that name's status to 🟢 in use in the roster. Kept English by design (the only exceptions): generic leaf utilities (`Utf8Helpers.h`) and algorithm helpers that live inside an existing namespace (`GraphPath.h` under `Aura::`)
- **CE Lua output hygiene**: Every CE Lua script we emit — the C# script generators (`*ScriptGenerator.cs`, `CeXmlExportService`), the standalone `scripts/*.lua`, and `scripts/UE5CEDumper.CT` — MUST be quiet by default so the CE Lua Engine window never covers Cheat Engine.
  - **DEBUG flag**: gate every diagnostic/progress `print()` behind a flag. Global master switch with per-script override: `local DEBUG = UE5_DEBUG or 0` (unset = off; set `UE5_DEBUG=1` in CE's Lua console to make everything verbose). Print via a `dbg()` wrapper: `local function dbg(...) if DEBUG ~= 0 then print(...) end end`. Standalone `.lua`/`.CT` gate inline on `(UE5_DEBUG or 0) ~= 0`.
  - **Errors always surface**: real failures keep bare `print()` / `showMessage()` (ungated), and warnings that flag a genuine problem (e.g. dissect `warn`, `ue5_logError`) stay unconditional.
  - **Auto-close on clean success**: when `DEBUG == 0` and the block finished with no error, close the window via `synchronize(function() getLuaEngine().Close() end)`. On ANY error path (mailbox-not-found, timeout, `state<0` / `code==-7` / `result~=0`, null mid-walk, AOB-scan-fail) the close MUST be unreachable — `return` before it or guard it so the window stays open. A timeout is an error, so `return` (not `break`) out of a mailbox-wait loop that precedes a close.
  - **Reuse the shared emitter**: generators call `CeLuaHygiene.AppendDebugPreamble` / `AppendCloseOnSuccess` / `AppendMailboxWait` / `AppendContractCheck` / `AppendIdleWait` (and `CeLuaHygiene.CloseCall`) so the emitted text is byte-identical everywhere and can't drift. Each `{$lua}` block is its own Lua chunk — locals don't cross `[ENABLE]`/`[DISABLE]`, so emit the preamble in every block that uses `DEBUG`/`dbg`. **Hand-rolling any of these is how build 2743's three defects reached all seven copies of the mailbox wait at once.**
  - **A bail-out that applied NOTHING must untick the record** (`if memrec then memrec.Active = false end`) before returning — otherwise CE leaves the row ticked and the user is told a cheat is active when nothing was set. Two script shapes, and they are **not interchangeable**: *stateful toggles* (Movement / GodMode / Fly / SeeThrough / Foreground / DebugCamera / TimeDilation) untick-and-return, while *momentary actions* (Teleport's rows) flag `hadError` and leave the loop so their **deferred** untick timer still runs — an early `return` there skips it. Pass the right `MailboxTimeout` mode; `CeMailboxBailoutTests` asserts both shapes.
  - **Never report a mailbox failure by guessing.** `status` already distinguishes them: `0` = the DLL never picked the command up (usually a **stale** `g_invokeMailbox` address), `0xFF` = it took the command and wedged. And a timeout must be a REAL deadline via `getTickCount()` — `sleep(1)` measures **15.47 ms** in CE (the ~64 Hz Windows tick, identical on very different CPUs), so counting iterations against a millisecond constant made a "10 s" timeout ~155 s.
- **CE Lua ↔ DLL contract version**: versioned on the **CONTRACT**, never the build number — a `.CT` saved months ago stays valid against a newer DLL as long as nothing it depends on moved. `Mimic::MAILBOX_CONTRACT` + `MAILBOX_CONTRACT_MIN` publish a **range** via the exported `g_mailboxContract`; scripts bake `CeMailboxLayout.ContractVersion` and check `MIN ≤ script ≤ CONTRACT` **before the first write** (the layout is what is in question, so a write can't come first). Bump rules and what counts as the contract live in [`dll/src/Mimic.h`](dll/src/Mimic.h) — additive changes (new `Cmd`, new op at an unused number, new knobId) move `MAILBOX_CONTRACT` only; breaking ones also move `MAILBOX_CONTRACT_MIN`. `tools/check_mailbox_contract.py` hashes the surface and fails CI on a forgotten bump, which is **worse than no versioning** (every old script then asserts compatibility while writing to moved offsets). ⚠ **The hash covers field LAYOUT, not field MEANING** — a command that starts reading or writing a field it never touched before is a real contract change the hash cannot see (that is how contract 2 moved without the hash moving). Its "bumped but surface unchanged" branch is what surfaces those: it refuses the bump until you record WHY in the golden block.

-----

## Project Goal

Develop a **C++ DLL + Cheat Engine Lua Bridge** Unreal Engine Dumper.
Target: Support UE4 (4.18+) and UE5 (5.0 ~ 5.8+), find global pointers (GObjects / GNames / GWorld), build complete object/struct hierarchy, integrate with CE. Despite the "UE5" name, UE4 games are a priority target — many popular games use UE4, and RE-UE4SS demonstrates broad UE4 support is achievable.

-----

## Architecture Overview

A **C++ DLL injected into the game** + a **Cheat Engine Lua bridge** + a **standalone Avalonia UI**
that talks to the DLL over a named pipe.

⚠ **This is a map, not a specification.** One line per module, and deliberately no detail: every
module's own header comment carries its contract, [docs/architecture.md](docs/architecture.md) has
the file tree, [docs/dll-spec.md](docs/dll-spec.md) the interface, and
[docs/naming-convention.md](docs/naming-convention.md) the Frieren-name roster. **Counts drift —
derive them, never hand-edit** (`tools/check_derived_counts.py` pins the ones written here).

```
+------------------------------------------------------+
|  Game Process                                        |
|                                                      |
|  CE Lua:  UE5CEDumper.CT  ue5_dissect.lua            |
|           ue5_invoke_helper.lua                      |
|      |  loadLibrary / callFunction (59 C ABI exports)|
|      v                                               |
|  UE5Dumper.dll  [injected, or one of 4 proxy DLLs]   |
|                                                      |
|   -- core ------------------------------------------ |
|      +-- Macht    (Memory)        AOB scan, SEH r/w   |
|      +-- Himmel   (Signatures)    the AOB tables      |
|      +-- Genau    (OffsetFinder)  GObjects/GNames/GWorld/GEngine |
|      +-- Aura     (ObjectArray)   object pool, refs, graph paths  |
|      +-- Serie    (FNamePool)     UE5 pool / UE4 TNameEntry       |
|      +-- Ubel     (UStructWalker) FField + UProperty walk         |
|      +-- Denken   (NativeDisasm)  Zydis property xref             |
|      +-- Sein     (Logger)        5 categories, per process       |
|      +-- Tot      (Cancellation)  cooperative abort flag          |
|                                                      |
|   -- interfaces ------------------------------------ |
|      +-- Frieren  (ExportAPI)     C ABI for CE Lua    |
|      +-- Fern     (PipeServer)    JSON over named pipe; the live count is **99** commands |
|      +-- Mimic    (Mailbox)       shared-memory command channel for CE Lua |
|      +-- Stark    (GameThreadDispatch)  MinHook ProcessEvent hook |
|      +-- Lugner   (Proxy)         version/dinput8/dxgi/winmm forwarders |
|                                                      |
|   -- scanning -------------------------------------- |
|      +-- Radar    (ValueScan)     CE-style value scan + group scan |
|      +-- Orden    (GroupMatch)    source-agnostic multi-value matcher |
|                                                      |
|   -- gameplay features ----------------------------- |
|      +-- Wirbel   (Teleport)      markers, coords, POV, cursor    |
|      +-- Solitar  (GodMode)       force AActor::bCanBeDamaged     |
|      +-- Laufen   (MovementTuning) speed / gravity / jump knobs   |
|      +-- Hemmung  (TimeDilation)  world + pawn time levers        |
|      +-- Solide   (ForceField)    hold a field across a class tree |
|      +-- Edel     (CurrentTarget) auto-detect the player's target |
|      +-- Grausam  (ForegroundLock) keep the game "foreground"     |
|      +-- Schlacht (SeeThrough)    hide occluders in the view      |
|      +-- Linie    (LivePEProfiler) which UFunctions actually fired |
+----------------------+-------------------------------+
                       | \\.\pipe\UE5DumpBfx  (newline-delimited JSON)
+----------------------v-------------------------------+
|  UE5DumpUI  (Avalonia + ReactiveUI, standalone exe)  |
|                                                      |
|   -- browse ---------------------------------------- |
|      +-- ObjectTreePanel        UObject hierarchy     |
|      +-- ClassStructPanel       property grid         |
|      +-- PointerPanel           global pointers       |
|      +-- LiveWalkerPanel        instance drill-down   |
|      +-- InstanceFinderPanel    find by class         |
|      +-- RelatedObjectsPanel    an actor's neighbours |
|      +-- DumpExplorerPanel      offline .jsonl browser |
|                                                      |
|   -- search ---------------------------------------- |
|      +-- PropertySearchPanel    by name, + Force      |
|      +-- ValueSearchPanel       by value, single/group |
|      +-- InterestingFunctionsPanel / InterestingPropertiesPanel  scored |
|      +-- ConsolePanel           UFUNCTION(exec) discovery |
|      +-- LiveFuncsPanel         ProcessEvent call profiler |
|                                                      |
|   -- act / export ---------------------------------- |
|      +-- TeleportPanel          teleport + the gameplay cards |
|      +-- ProxyDeployPanel       deploy + clean up proxy DLLs  |
|      +-- CeXmlExport / CsxExport / CheatTableBuilder / StructReturnDecoder |
|                                                      |
|      +-- PipeClient             async pipe mgmt, two lanes |
+------------------------------------------------------+
```

## Documentation Index

⛔ **A row here is a POINTER, not a summary.** One or two sentences answering *"when would I open
this?"* — target ≤300 bytes, and keep a ⛔/⚠ only when it changes whether someone opens the doc at
all. **Findings, counts, `file:line`, derivation commands and traps belong in the document itself**;
if a fact is not there yet, put it there rather than here.
*Why this rule is written down: by 2026-08-27 the table had reached 40 KB across 56 rows — a mean of
716 bytes, i.e. a paragraph per entry — and a row-by-row audit against every target doc found the
prose was near-entirely duplicated, plus **six claims that had gone stale or were refuted by the very
doc they pointed at**. Trimmed to ~11 KB; CLAUDE.md 77.5 KB → 39 KB.*
⚠ The few **bold counts** below are pinned by `tools/check_derived_counts.py` — leave them exactly
as written, or update the registry with them.
⛔ **The WHOLE FILE stays ≤ 40 KB (40,960 bytes)** — it loads on every turn, so these are the most
expensive bytes in the repo. `ls -l CLAUDE.md` before committing; over the line, trim a row, do not grow.

| Document | Contents |
|----------|----------|
| [docs/handover-2026-08-22.md](docs/handover-2026-08-22.md) | 🤝 **START HERE — the single entry point.** A fresh session's first ten minutes: tree state, computer-use grants, launching a fixture, the hard rules, gates/tests/builds, driving CE, what is open, traps, closing a row. |
| [docs/archive/handover-2026-08-20.md](docs/archive/handover-2026-08-20.md) · [docs/archive/handover-2026-08-19.md](docs/archive/handover-2026-08-19.md) | ⚠ **Superseded — history only.** The 2026-08-19/20 verification programme's closing notes; their defect queues and build facts are spent. The [archive README](docs/archive/README.md) says what was carried forward. |
| [docs/toolchain.md](docs/toolchain.md) | **What a machine needs, and why** — the reasoning behind `bootstrap.cmd`. ⚠ Read before installing anything on a new machine: tiers, the VS2026 winget id + `.vsconfig` list, the do-NOT-install pairs, how to prove the env works. |
| [docs/roadmap.md](docs/roadmap.md) | **Current state** — capability matrix, per-game configuration, tested games, long-running concerns. ⚠ Rows are stale past build 797; read its own banner before trusting one. |
| [docs/tips.md](docs/tips.md) | **User-facing how-to recipes** — goal → which panel/button. First recipe: forcing camera rotation in fixed-view (2.5D/45°) games. |
| [docs/todo.md](docs/todo.md) | **What's next** — open work only, with effort/risk tags. **201 KB since the 2026-09-03 split**; it is a todo again. |
| [docs/verification-register.md](docs/verification-register.md) | **What is shipped but not yet proven on a running game** — one row per check, each naming its acceptance test. ⛔ Read its charter before proposing to delete a row: a row exists to say whether the **DLL or the UI** is at fault, which a manual play-test structurally cannot do. |
| [docs/pending-verification_zh-TW.md](docs/pending-verification_zh-TW.md) | 🇹🇼 繁中 operational checklist — steps for the verification items **only a human can close** (act in-game, judge by eye, or no sample exists). Anything Auto + Computer Use can run belongs in verification-register.md. |
| [docs/log-verification-checklist.md](docs/log-verification-checklist.md) | **How to sweep a real session's logs** — the procedure half of verification-register.md's verification register: which file holds which marker, what to grep, what to do in-game first, and which absences prove nothing. |
| [docs/auto-verification-classification-2026-08-23.md](docs/auto-verification-classification-2026-08-23.md) | Which open verification rows Auto + Computer Use can close, and what the rest need (fixture / environment / human). ⚠ Agent-produced and unrefuted — re-derive before acting. |
| [docs/auto-verification-session-plan.md](docs/auto-verification-session-plan.md) | **How to RUN the register unattended.** Still owns grant mechanics (§3) and the authorised out-of-tree writes (§4, ask first). ⛔ **§5's run order is SPENT and §10's classification is RETIRED** — do not plan a batch off either. |
| [docs/audit-2026-08-26-dxgi-appcompat-crash.md](docs/audit-2026-08-26-dxgi-appcompat-crash.md) | ⚠ **Read before changing a proxy DLL export or resolver.** Why dxgi stopped OCTOPATH booting while the same binary as winmm worked: an AppCompat shim calls our export before our CRT exists. Fix, rig, and the WER-reading traps. |
| [docs/audit-2026-09-05-vendor-ue582.md](docs/audit-2026-09-05-vendor-ue582.md) | **Vendor audit #6** — UE 5.8.2 changed nothing for us; the value is the 6 defects of ours it found. ⛔ Read its top block before touching the ProcessEvent vtable table. |
| [docs/audit-2026-08-13-early-code-findings.md](docs/audit-2026-08-13-early-code-findings.md) | Audit #5 register — the pre-June-2026 "early" code. ⛔ Scanning AND fixing are both DONE (the fix queue is SPENT); open it only when the maintainer names a finding, or to record a NEW defect found while verifying. |
| [docs/audit-2026-08-04-findings.md](docs/audit-2026-08-04-findings.md) | **Bug/leak/refactor audit #4** (build 2554) — working tracker, all items shipped. Open for the per-finding detail, the refuted "do not re-raise" list, and the two cross-cutting root causes worth fixing as patterns. |
| [docs/audit-2026-07-14-findings.md](docs/audit-2026-07-14-findings.md) | **Bug/leak audit #3** (build 2168) — working tracker for the Solide/Hemmung/Linie/Schlacht/Grausam + Auto-Snapshot/Dump-Explorer/Live-Funcs findings, each with failure scenario, fix shape and effort/risk. |
| [docs/dev-log.md](docs/dev-log.md) | **What shipped** — append-only, newest-first milestone history per build number. Read when investigating when or why X was added. |
| [docs/architecture.md](docs/architecture.md) | Directory structure (**31 .cpp + 39 .h** DLL files, **186** test files, and what each does) (every DLL/UI source file and what it does), git submodules, build environment, component interaction + startup sequence, and the log-file layout / age-based retention rules. |
| [docs/dll-spec.md](docs/dll-spec.md) | C++ DLL interface — C ABI exports (**59** — derive it, never hand-edit); the public headers (Macht/Genau/Aura/Serie/Ubel/Frieren/Fern), DynOff runtime offset tables, C ABI export list, CE Lua inject-only bridge. ⚠ The headers are ground truth; this doc trails them. |
| [docs/pipe-protocol.md](docs/pipe-protocol.md) | Named Pipe JSON IPC protocol (99 commands incl. Value Search + Group Scan begin/refine/query/end, Request/Response/Event) |
| [docs/multipipe-eval.md](docs/multipipe-eval.md) | **Multi-pipe IPC evaluation — verdict: do NOT add more pipes.** Read before any pipe/IPC concurrency change. §10 measured and refuted the original head-of-line-blocking reason; §8 is the Phase 0/1 revert postmortem. |
| [docs/group-value-scan-spec.md](docs/group-value-scan-spec.md) | **Multiple Values Group Scan** — architecture (`Orden` source-agnostic SDR matcher, block model, deep containers) + the **extension points** for Snapshot/SPC/Pivot reuse + P2/P3/P4 roadmap. Read for "how a future feature plugs into group matching". |
| [docs/snapshot-group-match-spec.md](docs/snapshot-group-match-spec.md) | **Snapshot Group Match** (shipped, in-game verified) — N-value group matching over captured snapshots via a C# `Orden` port: Mode A absolute, Mode B temporal, Deep. Read before touching snapshot multi-value. |
| [docs/native-c-value-scan-spec.md](docs/native-c-value-scan-spec.md) | **Native-C Value Scan** (shipped; P3 in-game verify pending) — opt-in scan of the raw non-`UPROPERTY` bytes in a UObject for native HP/MP, across Value Search / Group Scan / Snapshot→SPC→Pivot. |
| [docs/ui-spec.md](docs/ui-spec.md) | Avalonia UI tech stack (Avalonia 12.1.0 / .NET 10 — version from the .csproj, not this row), AOT compatibility, component specs |
| [docs/export-formats.md](docs/export-formats.md) | CE XML, CSX, SDK Header, USMAP export rules, pointer chain model, type mappings |
| [docs/technical-notes.md](docs/technical-notes.md) | UE version differences, FField vs UProperty, FNamePool, DynOff, Property Type Layouts (Phases B-K), Address Finder layered lookup |
| [docs/lessons-learned.md](docs/lessons-learned.md) | Hard-won lessons from cross-game debugging (20+ games) |
| [docs/working-lessons.md](docs/working-lessons.md) | ⭐ **How to work here — read before an audit, a verification claim, or an Avalonia/CE/SQLite change.** Verification method, audit-agent calibration, traps in our stack, UE/CE facts, §6 settled decisions. Write new lessons here. |
| [docs/reference-builds.md](docs/reference-builds.md) | The stock-engine samples we package ourselves as PDB-bearing AOB oracles: the inventory, why each exists, which are deliberately not swept, and how to make another. Answers "what does the ENGINE do at version X", not what a game does. |
| [docs/test-games.md](docs/test-games.md) | 30+ test games with UE versions, GWorld status, stride info |
| [docs/naming-convention.md](docs/naming-convention.md) | Frieren-themed C++ file / namespace mapping (Macht/Genau/Aura/Serie/Ubel/Frieren/Fern/...) |
| [docs/aobmaker-integration.md](docs/aobmaker-integration.md) | AOBMaker CE Plugin pipe bridge (HEX / ASM / SYM / CreateAAScript) |
| [docs/mindseye-fork-notes.md](docs/mindseye-fork-notes.md) | **Read first if MindsEye breaks after a game update** — the three things this UE 5.4.4 licensee fork changes, which constants a patch can move, and how to re-derive each offline with capstone + `.pdata` (no Ghidra). |
| [docs/reversing-nonstandard-ue-games.md](docs/reversing-nonstandard-ue-games.md) | Playbook for forked/repacked engines where AOB + heuristics fail: patternsleuth → capstone → Ghidra → caller LEA → encode the fix (the Avowed case). Plus why we do not vendor Dumper-7/RE-UE4SS. |
| [docs/corpus-preservation.md](docs/corpus-preservation.md) | ⛔ Read before deleting anything under the Ghidra corpus root or an archive root, or uninstalling a corpus Steam title: what to keep, reinstall or drop, the PDB checklist, the drop order, and the never-drop set. |
| [docs/aob-block-library-eval.md](docs/aob-block-library-eval.md) | ⚠ Not just an eval — the block library and the n-gram specificity index are BUILT and CI-gated, so this doc is load-bearing. Read before touching either, or for the one decision still open. |
| [tools/README.md](tools/README.md) | Offline RE helpers — Ghidra scripts (`find_gobjects`/`decompile_functions`/`find_callers` Java + the pyghidra symbol/AOB exporters) and a capstone PE disassembler (`pe/disasm_function.py`). |
| [scripts/analysis/README.md](scripts/analysis/README.md) | Offline analysis tooling — `analyze_dumps.py` (cross-game keyword calibration) + `diff_dumps.py` (same-game patch diff, build 780). |
| [docs/simd-scanning-notes.md](docs/simd-scanning-notes.md) | AOBMaker SIMD scanning research: AVX2 anchor technique (reference) |
| [docs/CE-Bugs-Minesweeper.md](docs/CE-Bugs-Minesweeper.md) | **CE bugs and undocumented behaviours we actually hit** — open when CE misbehaves, and ⚠ before trusting `celua.txt` or the plugin SDK header: both describe behaviour the shipping binary does not have. |
| [docs/ce-plugin-api-reference.md](docs/ce-plugin-api-reference.md) | CE Plugin SDK C ABI reference — every `ExportedFunctions` member, the plugin types, enums, `pluginsync` threading. ⚠ A mirror: edit the external master first. |
| [docs/ce-plugin-sdk-notes.md](docs/ce-plugin-sdk-notes.md) | **CE pitfalls companion** — what CE's Pascal does that its C header does not admit. Read before emitting or changing a CE artifact: `TVariableType` ordering, opcode-nav return types, Lua-state threading, §13 `executeCodeEx`. |
| [docs/ce-ccode-eval.md](docs/ce-ccode-eval.md) | ⛔ CE `{$CCODE}` — EVALUATED, DO NOT ADOPT: the repo emits no injection hook sites, so there is nothing for it to attach to. Read before re-proposing it, or if a hook site ever appears. |
| [docs/ce-ccode-reference.md](docs/ce-ccode-reference.md) | CE `{$CCODE}` / `{$C}` manual — the native-code alternative to the `{$lua}` blocks this repo emits. Syntax, the parameter/register layout `{$LUACODE}` shares, the LUACODE comparison, and CE's own defects to avoid. |
| [docs/ce-memory-scanning-internals.md](docs/ce-memory-scanning-internals.md) | How CE's scanner actually goes fast — the reference implementation our own scanners (`Radar` / `Aura` / `Macht`) are measured against. Buffers, nibble wildcards, the numeric-scan path, AOBMaker's SIMD anchor scan. |
| [docs/ce-disassembler-navigation.md](docs/ce-disassembler-navigation.md) | Driving CE's Memory Viewer from outside — the verified Lua `SelectedAddress` route (Pascal-property-backed, not just `celua.txt`), reusable from our `{$lua}` blocks; and where the Type 6 pointer-write works. |
| [docs/teleport-spec.md](docs/teleport-spec.md) | **Teleport / Wirbel design contract** — markers, POV, coord TP, cursor forcing, the `CMD_TELEPORT` op table. The most-referenced spec in `docs/` (9 inbound links). |
| [docs/teleport-coord-library-spec.md](docs/teleport-coord-library-spec.md) | **Teleport Coordinate Library** design contract — the coord list, its CE-Lua + CSV export/import, and the locked decisions (file key, Map filter, character policy, size budget). ⚠ Verification status lives in todo.md. |
| [docs/godmode-spec.md](docs/godmode-spec.md) | **GodMode / Solitar design contract** — the invincibility-bool scan + re-assert model, and the **locked Non-Goal** (no universal detection bool; surface per-game via Property Search) that also governs `Solide`. |
| [docs/output-monitor-pin-eval.md](docs/output-monitor-pin-eval.md) | **Pinning a game to one monitor when it has no monitor-select UI — EVALUATED, NOT BUILT.** Read before re-proposing it: UE reflection has no monitor concept, and the hard part is the game drifting back. |
| [docs/ue-perf-counters-eval.md](docs/ue-perf-counters-eval.md) | **Surfacing UE's own `stat` counters in the UI — EVALUATED, tiered.** Why the literal ask is impossible from an injected DLL, what shipped instead, and the dispatch/IPC measurements it produced. |
| [docs/log-compression-eval.md](docs/log-compression-eval.md) | **Log compression — SHIPPED.** Why `compact /c /exe:LZX` in place and not gz/zip, the two triggers, the `-0.log` liveness rule, and the traps a change here must not re-break. |
| [docs/text-translation-eval.md](docs/text-translation-eval.md) | **In-game S2T conversion + local-LLM translation — EVALUATED, in-memory rewrite REJECTED.** Read before re-proposing live text rewrite: the UE-source walls, the font-glyph risk, why offline `.locres` wins. |
| [docs/experimental-snapshot-spc-pivot.md](docs/experimental-snapshot-spc-pivot.md) | Snapshot / SPC / Class Pivot — the experimental-tab family: capture model, intersection queries, pivot aggregation. |
| [docs/ce-export-drilldown-spec.md](docs/ce-export-drilldown-spec.md) | CE-export pointer drill-down spec (depth model, cascade resolution) — the companion to [export-formats.md](docs/export-formats.md). |
| [docs/avowed-gobjects-fix.md](docs/avowed-gobjects-fix.md) | The Avowed case study — static `FUObjectArray` + 20-byte packed `FUObjectItem` + the GWorld decoy. Read alongside [mindseye-fork-notes.md](docs/mindseye-fork-notes.md) for non-standard-layout work. |
| [docs/archive/](docs/archive/) | Superseded docs, the older `dev-log` halves and closed `todo.md` sections, kept byte-identical. Its [README](docs/archive/README.md) says what each file holds, which build ranges, and why it moved. |
