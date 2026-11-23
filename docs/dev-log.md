# Dev Log

Append-only milestone history, newest first. Each entry references a
build number from `build_number.txt` so commits can be cross-referenced.
**Reading tip:** grep `^## ` for the index, then read the top (newest-first).
Entries for **builds ≤3670** are archived (update this number on every split: after the third one it
lagged three weeks). Builds 3263–3670 in
[archive/dev-log-2026-10-pre-build-3671.md](archive/dev-log-2026-10-pre-build-3671.md),
builds 2779–3261 in
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

## 2026-11-23 (no build change) — Every mailbox command is tested, and the polling thread too `[CPP-TEST-TARGET-GAP-2026-11-23]`

- **What changes:** tests only. `mimic_mailbox_test` grows from 41 checks to 255 and now drives every command
  the Cheat Engine mailbox serves. A reflection fixture built in the test's own memory -- one name table, one
  object array, the offsets set once -- lets the real lookup and invoke code run.
- **Covered:** each teleport op's inputs, pose, tier and flags; class lookups that pick a live subclass object
  over a class default object, function lookups through base classes, listings and their pages, a freeze pool
  that never holds a template; CMD_INVOKE refused before anything is converted or called when it cannot be sized
  or the function is not the instance's own, routed by the function's own flags, a template warned; the debug
  camera's own function, invoked, changes the camera as CE's; every command answered in the mailbox it came from;
  the real polling thread serving this copy's mailbox and two it took over, stopped for a hand-off or an unload,
  and restarted.
- **Negative controls:** 46 TELEPORT, 36 FINDLIST and 35 INVOKE and 58 WINDOW / BOX / LIFECYCLE mutants of Mimic.cpp, every one detected.
- Of the 20 C# reads of Mimic.cpp's text, 16 are gone; the 4 that stay say why no behaviour test can see them.
- **Tests:** `mimic_mailbox_test` 255 checks, 0 failures, five runs in a row, and under ASan; C# 8527 (1 skipped).

-----

## 2026-11-23 (no build change) — The Cheat Engine mailbox has a test `[CPP-TEST-TARGET-GAP-2026-11-23]`

- **What changes:** tests and the test build only. `mimic_mailbox_test` #includes `Mimic.cpp`, links
  `ue5_testcore`, and calls `Mimic::ServeOne` (one pass of the polling thread) against a mailbox in its own memory.
  The C ABI and the modules the mailbox dispatches to are faked; the hold ledger is the real one, so "CE now holds
  it" is read back from it. No polling thread runs, so each command is served exactly when the test says.
- **Covered, 41 checks:** an unknown command is answered, never left processing; the offsets verdict needs no init
  and a success carries no earlier error text; a command that walks reflection waits for an init and refuses
  without one, while Foreground and the offsets verdict are exempt; the retry while another init scans; God Mode's
  state bytes; every handler that changes a hold records CE as its holder; the debug camera's queued and blocked
  texts, in each direction, and a pending ON kept as its sender's; a handler that throws loses its command, not the
  mailbox; an adopted mailbox is answered in place; a mailbox another poller is busy with is left to it;
  see-through's count and toggle. Negative controls: the earlier error text kept fails 2 checks, no command exempt
  5, a throw not answered 1, no retry 1, a pending ON taken over 1, a busy mailbox served 1, CE never recorded 13.
- Two C# text pins are gone and four keep only their other files' halves. The 20 reads of `Mimic.cpp` that remain
  pin CMD_INVOKE, CMD_TELEPORT and StopThread's hand-over, which this test does not drive yet; their comments say so
  instead of saying that `Mimic.cpp` reaches no test target. 16 of the 33 `dll/src` .cpp files reach a test target.
- **Tests:** `mimic_mailbox_test` 41 checks, 0 failures, and under ASan (its negative control reported); every
  other self-test green; C# 8532 (1 skipped); 40 gates. build.ps1 runs the new test in its C++ list; that line was
  not run locally (a local run republishes dist\), so the next publish or CI run is its first.

-----

## 2026-11-23 (no build change) — Fly's failed MovementMode write is tested, not only read `[CPP-TEST-TARGET-GAP-2026-11-23]`

- **What changes:** tests only. `dunste_fly_test`'s WRITEFAIL reaches the CharacterMovement through a read-only view
  of a section, so the MovementMode write fails: an enable returns FR_ERR_WRITE and starts nothing, and a disable
  whose restore fails returns FR_ERR_WRITE with the worker stopped. A read-only PAGE would not do: Macht::WriteBytes
  raises a page to PAGE_READWRITE for its write. Negative controls: an enable that ignores the write fails 3 checks,
  a disable that does not report the failed restore 1.
- `tools/verify/sliceb_fly_arm.py` had said this path could only be checked by reading, because Dunste.cpp reached
  no test target; `check_comment_refs` flagged the claim once it did, and the note now points at WRITEFAIL.
- **Tests:** `dunste_fly_test` 57 checks, 0 failures, and under ASan.

-----

## 2026-11-23 (no build change) — Fly / Noclip has a test, on a core library the next module tests share `[CPP-TEST-TARGET-GAP-2026-11-23]`

- **What changes:** tests and the test build only. `Dunste.cpp` keeps file-local copies of the pawn chain that
  `Solitar.cpp` keeps too, so it cannot join `dll_core_test`'s single translation unit. A new static library,
  `ue5_testcore`, builds the core files once (Macht, Serie, Ubel, Radar, Denken, Flamme, Aura, Genau, Linie,
  Solitar), and `dunste_fly_test` compiles `Dunste.cpp` as its own unit against it. The test fakes the world and the
  game thread: `Stark::IsGameThreadResponsive` answers what the test says, and `UE5_CallProcessEventEx` records each
  call and, on success, sets the pawn's bActorEnableCollision as SetActorEnableCollision would.
- **Covered, 51 checks:** refusals (no world, no CharacterMovement); enable, the held MOVE_Flying and the hover;
  Noclip's one SetActorEnableCollision(false), and the status reading the collision that resulted; the disable
  restoring the captured mode, zeroing the velocity and restoring collision; a restore deferred while the game
  thread stalls, landing when it answers; the restore a refused enable re-arms; a queued call recorded so the next
  toggle undoes it; a refused call left unrecorded and retried after its back-off; the speed and preset knobs.
  Negative controls: no re-arm fails 2 checks, every outcome committed 1, a stall not looked at 3. The re-arm's C#
  text pin is gone, and the observed-collision pin keeps only Fern's half. 15 of the 33 `dll/src` .cpp files reach
  a test target.
- **Tests:** `dunste_fly_test` 51 checks, 0 failures (and under ASan, its negative control reported); C# 8540
  (1 skipped); the nodiscard probe checks the new library and test. build.ps1 runs the new test in its C++ list;
  that line was not run locally (a local run republishes dist\), so the next publish or CI run is its first.

-----

## 2026-11-23 (no build change) — God Mode has a test: dll_core_test compiles Solitar.cpp `[CPP-TEST-TARGET-GAP-2026-11-23]`

- **What changes:** tests only. `dll_core_test` #includes `Solitar.cpp` and its GODMODE block drives God Mode over a
  fake world reached by reflection alone (GWorld -> OwningGameInstance -> LocalPlayers[0] -> PlayerController ->
  Pawn): ON clears only bCanBeDamaged's bit in a byte of eight packed bools and writes a native bool 0x01; the
  worker re-asserts a bit the game set back; the debug camera's controller is hopped through
  OriginalControllerRef; targets are kept until the property family's epoch moves, then read again at the new
  offset; OFF restores the bits and stops the worker; the trainer's bits; SetActorBool / GetActorBool; no world.
  22 checks. Negative controls: a whole-byte stamp fails 6, targets keyed by the class alone 1, no hop 2. The three
  C# tests that read Solitar.cpp as text are gone. 14 of the 33 `dll/src` .cpp files now reach a test target.
- **Two failures seen once, found and fixed `[CEPTR-FAIL-ONCE-2026-11-23]`:** the ASan run of `dll_core_test` failed
  the CEPTR MindsEye-Extended checks every time, which made them measurable. Both fixtures were shorter than what the
  code reads: the CEPTR chunk held 64 items of 24 bytes while Aura's item-layout probes read 200 items up to 32 bytes
  apart, and RESCAN's sparse chunk held 1024 slots while the scorer samples 4096. The reads past them met whatever
  the heap put next, so MindsEye-Extended's preset item hint was refused in an ordinary build and taken under ASan.
  Whole chunks now, MindsEye's own items for that shape, and a check of the item layout each stock shape latched.
  Tests only; nothing in the DLL changed.
- **Tests:** `dll_core_test` 2024 checks, 0 failures (three ordinary runs, and the ASan run with its negative control);
  C# 8541 (1 skipped).

-----

## 2026-11-23 (build 3778) — The proxies build into their own folder; the Cheat Engine plugin has a test `[CPP-TEST-TARGET-GAP-2026-11-23]`

- **What changes:** nothing in the UI or the DLL's behaviour. The four proxy DLLs build into `build\dll\proxy\`
  instead of `build\dll\`, and `build.ps1` takes each one from there by name, deleting one an older tree left in
  `build\dll\`. Until now a C++ test that imports `version.dll` (`dll_core_test`) loaded our proxy from its own
  folder, and the proxy's DllMain ran inside the test: a MinHook block, a pipe server, and logs plus a retention
  sweep in the user's Logs folder (working-lessons §3.xg).
- **New test target `methode_plugin_test`:** it #includes `Methode.cpp` and drives the Cheat Engine plugin through a
  fake Cheat Engine: GetVersion, InitializePlugin (a short table, a refused registration), the menu click with no
  process, with CE reporting failure, success with nothing mapped (the ambiguous force-load message), and success
  with the built UE5Dumper.dll mapped without running its DllMain; a second click with it loaded; the ProductName
  identity rule; DisablePlugin. 61 checks. Negative controls: identity by file name only fails 18, no ambiguous
  branch fails 5. Its three C# text pins of the dialogs are gone. 13 of the 33 `dll/src` .cpp files now reach a
  test target.
- **Also since build 3777 (docs and tests):** CLAUDE.md's rule "Everything testable is tested" was already in;
  `dll_core_test`'s SCANSIBLING runs `ScanForValue` over two Vector fields (the A3 verification row's steps 1-2
  close offline); rows that called Ubel / Genau / Flamme .cpp untestable carry a correction; census rows for the test
  gap, the unfixed DLL cache wipe `[CACHEWIPE-DLL-2026-08-19]` and a CEPTR failure seen once.
- **Tests:** C# 8544 (1 skipped), headless 74, the six C++ self-tests (`methode_plugin_test` also under ASan, its
  negative control reported); 40 gates; the nodiscard probe.
- AOT UI 62,980,608 bytes (sha256 bd7a4930c2a2555f...); UE5Dumper.dll 3,882,496 bytes (sha256 325c3a28dc8ec0b5...).

-----

## 2026-11-23 (build 3777) — Avalonia 12.1.6

- **What changes:** Avalonia 12.1.3 → 12.1.6 (Avalonia, Win32, Skia, HarfBuzz, Themes.Fluent, Fonts.Inter and,
  in the headless tests, Headless); Microsoft.NET.Test.Sdk 18.10.1 → 18.12.0 in both test projects.
- **Pins kept, checked against the nuspecs:** Avalonia.Skia / Avalonia.HarfBuzz 12.1.6 are built against the same
  SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 as 12.1.3, and Avalonia 12.1.6 against the same MicroCom.Runtime
  0.11.6 and Angle natives, so SkiaSharp 4.x, HarfBuzzSharp 14.x and MicroCom.Runtime 0.11.7 are not taken.
  Microsoft.Data.Sqlite stays on `10.0.*` (the .NET 10 LTS patches); 11.0.0 is the .NET 11 train and needs the
  same SQLitePCLRaw 3.0.5.
- **Also since build 3776 (docs and tools only):** the README and Features pages describe the MCP server and the
  experimental extras, and several stale lines are fixed; `check_derived_counts` pins the signature counts five
  docs restate from `Himmel.h`; `test-games.md` has a row for every game in the README's version matrix; CLAUDE.md
  says everything testable is tested, and that a `.cpp` no test target compiles yet is a gap to close.
- **Tests:** C# 8547 (1 skipped), headless 74, the five C++ self-tests; `build.ps1` reports the native renderer
  packages match what Avalonia was built against (2 pairs).
- AOT UI 62,980,608 bytes (sha256 2470f75e69ecbfe1...); UE5Dumper.dll 3,882,496 bytes (sha256 83b7af4bfe51bb06...).

-----

## 2026-11-20 (no build change) — The census keeps only what is not done `[OPEN-CENSUS-2026-10-28]`

- **The decision:** the maintainer ruled that a done item leaves the open-work census for `docs/archive/` and an
  item not done stays (the charter used to keep fixed rows in place, and the census had grown to 192 KB).
- **The first move:** 73 Table A rows and 3 Table B / C bullets (125,556 bytes) to
  `archive/todo-census-closed-2026-11-20-build-3776.md`; 48 Table A rows stay. `todo.md` is now 1.37 MB.
  `move_units.py` moves list bullets too, and `verify_split.py` matches a marker anywhere in a pointer line, so two
  passes on one day can be told apart.
- **Filed first:** six open items from the Stage C review notes (a machine-local scratch file), checked against
  HEAD, became census rows -- among them a refine that holds Radar's manager lock and blocks other clients.
- **Rigs kept:** `tools/verify/stark_drain_suspend.py` (forces `[STARK-DRAIN-OFF-GAME-THREAD]`'s worker-thread
  case) and the two UE 4.15 census Lua rigs in `tools/verify/ce/`, out of the scratch folder before it was cleaned.

-----

## 2026-11-20 (no build change) — The big docs slimmed, and the archive tools kept in the repo

- **What this is:** the maintainer asked whether `todo.md` and `verification-register.md` needed archiving, then
  for the dev-log to be split too and for the scripts to live in `tools/`.
- **`tools/docs_archive/`** (new): the unit index, the approval join, the bounds check, the byte-identical move,
  the independent re-check from git, and `split_devlog.py`. They had lived in a gitignored scratch folder (the
  dev-log split's script did not survive at all); `tools/README.md` says how a pass runs.
- **`todo.md` 1.63 MB → 1.48 MB:** 61 closed units (41 sections, 20 table rows) moved to
  `archive/todo-closed-2026-11-20-build-3776.md`. **The register 1.08 MB → 1.05 MB:** 7 closed sections moved to
  `archive/verification-register-closed-2026-11-20-build-3776.md`. Proposers and adversarial verifiers per region;
  three units were sent back for a note found nowhere else. The census (192 KB of `todo.md`) did not move: its
  charter keeps fixed rows in place, so archiving them is the maintainer's call.
- **The dev-log 554 KB → 206 KB:** builds 3263-3670 (153 entries, 2026-08-19 … 2026-10-29) moved to
  `archive/dev-log-2026-10-pre-build-3671.md`, cut on the 2026-11-01 date boundary.
- Every move was asserted before writing and re-checked against git afterwards: nothing was edited, only moved.
  `todo.md:NNNN`, `verification-register.md:NNNN` and `dev-log.md:NNNN` citations are stale from today; grep the
  tag or the build number.

-----

## 2026-11-20 (no build change) — Live checks on build 3776, in gameplay: L46 Phase J and Locate's drill to HP `[LIVEPASS-2026-11-20]`

- **What this is:** the rows the maintainer cleared for a loaded save (DRAGON QUEST XI S and The Adventures of
  Elliot, each from its autosave). One commit per row, then one per correction after an adversarial check of each
  record against its rig output and logs.
- **L46 `[P1-UPROP-DELEGATE]` Phase J, reached live for the first time:** DQ XI S (UE 4.18, UProperty mode) has a
  unicast delegate array that is non-empty only for a moment while a map widget loads. A scratch rig caught it and,
  inside that moment, read the element (`WBP_Hud_MiniMap_000_C_1::AsyncLoadFinished`), poked the inner
  ElementSize to 20 and got the refusal string and its WALK warning, then restored it and read the element again.
- **Locate in GWorld's drill to HP through the level-list recovery:** on Elliot, at a Locate depth the forward
  search cannot meet, an enemy's AttributeSet was located through the world's level list and the tail search, and
  HealthPoint drilled from it. At the default depth every live enemy AttributeSet is reachable directly, so the
  record says so.
- **Surveys:** neither title is a `[W3-XREF-CAP]` host in gameplay either (5,474 and 3,771 scripted functions).
- **Corrected:** L66 `[W1-PARTIAL-MARK]` leaves nothing to delete; its snapshots went on 2026-09-23, and the
  earlier entry's list carried a stale copy.
- Elliot's dxgi proxy was refreshed to 3776; `ui-options.json` was put back after the depth change; both games and
  the UI were closed.

-----

## 2026-11-20 (no build change) — Live checks on build 3776, continued: four more rows closed, three host surveys `[LIVEPASS-2026-11-20]`

- **What this is:** the rest of the day's pass, after the maintainer cleared gameplay on Avowed, Star Trek
  Voyager's Chinese mod and the test snapshot's deletion. Each row is its own commit; the lists are the
  `[LIVEPASS-2026-11-20]` section of todo.md.
- **Closed:** B28 on Star Trek Voyager (`版本` reads as CJK); the `Mimic: FIND_FUNCTION` leg of
  `[A3-FUNCFLAGS-2026-09-05]` on OCTOPATH, headless through a WriteProcessMemory mailbox; `[CT-FLOOR-208]`'s
  window-size steps on DumperTest58 Shipping (the Call Trace list holds 208 at 1000, 1400 and the 800 minimum); and
  L28 `[W5-CEXML-FSTRING]`'s FString-valued map on Avowed, its CE chain followed through the pipe.
- **Re-run:** `[LIVEFUNCS-STEP2]` item 2 on Avowed in gameplay; the 10,000-a-second total was not reached again.
- **Surveys, no host here:** `[W3-XREF-CAP]` (no field above 53 functions at any title screen), L46 Phase J
  (the < 4.25 titles' unicast delegate arrays are CDO-only and empty), `[VND9-01]` / `[VND9-04]` (Star Trek
  Voyager's title screen). Each names the gameplay host that could close it.
- **Finding:** `[FTEXT-NEIGHBOR-GARBAGE]` (LOW, census Table A): 10 of 1,616 FTexts read a heap neighbour's
  pointer bytes as text.
- The EVERSPACE 2 test snapshot the first entry left behind was deleted; `ui-options.json` and the window state
  were put back after the window-size run; every game and UI was closed.

-----

## 2026-11-20 (no build change) — Live checks on build 3776: 39 rows closed, three findings filed `[LIVEPASS-2026-11-20]`

- **What this is:** the maintainer's "run the live checks this PC can run" pass on the released build 3776,
  with MCP checks left for later and Cheat Engine left to the session using it. Each closed row carries its own
  record and was its own commit; the list, what is left and why, is the `[LIVEPASS-2026-11-20]` section of todo.md.
- **On the fixtures:** DumperTest Shipping (UI text, Fabricate, Live Walker, snapshots, Stealth, Fly export, the
  SDK pad rule, a parent-relative pose, no-pawn libraries, a dead game's stale status), the staged decoy (Extra
  Scan's result and its cancel), DumperTest58 (the 508 badge on a raised run, the Call Trace load line),
  UE423_Flying (the main-thread fallback) and DumperTest Development (a close while moving with See-through on).
- **On installed games:** ProcessEvent slots witnessed at 4.15 (Extinction), 4.18 (OCTOPATH) and 4.20 (EVERSPACE);
  DQ XI S's licensee build keeps it at 0x210, not the 4.18 row's 0x1F8 (the pattern path answers it). Avowed's scan
  log names the lld linker; NumericAll's volume measured there; a persisted contradicted override cleared by Auto
  on DQ XI S; EVERSPACE 2's UI legs and a capture killed mid-way; NEKOPALIVE's void-function return offsets; a DQ7R
  USMAP with named TSet enum inners.
- **Findings:** `[LW-REFRESH-STATUS-DUP]` and `[POINTERS-DISCONNECT-STALE-ERROR]` (LOW, census Table A), and a
  tools trap in `call_export.py`'s `--process` matching.
- **Tools:** `tools/verify/a4_raise_ladder.py --hold` keeps the game up after the log is read, for the UI leg.
- Proxies refreshed to 3776 in IS Defense, Extinction, EVERSPACE, Avowed, OCTOPATH and DQ7R; every game was closed
  afterwards. One test snapshot (about 206 MB) is left in EVERSPACE 2's snapshot database.

## 2026-11-20 (build 3776) — Extra Scan's follow-ups: a recovered array's code site, the MCP server during an apply `[INIT-RECOVERY-SCANADDR]` `[MCP-READY-DURING-APPLY]` `[RESCAN-STATIC-LAYOUT]`

- **What changes:**
  - **A GObjects init recovered** (its static-struct or heap fallback) no longer leaves the wrong match's code site
    behind: the Pointers panel's Asm / SYM buttons stop pointing at it, as after Extra Scan's replace. The
    static-struct recovery's method reads "static struct recovery", not its raw name.
  - **The MCP server** no longer treats a game as scanned while an Extra Scan apply is choosing among candidates:
    `scan_status` says `applying` then, and the AI is told the scan is still running.
- **Checked, nothing to change:** Extra Scan's replace reads every static array shape init's static recovery knows
  (Obsidian's packed 20-byte item among them) the same way init does; a test now pins it.
- **Not done, by decision:** running Extra Scan's whole-.data search inside init (`[RESCAN-INIT-THIRD-RECOVERY]`).
- **Tests:** red, then green, for the two fixes. dll_core_test 1981, dll_helpers_test 4456, C# 8547 (1 skipped),
  headless 74, 40 gates. Stock control on DumperTest Shipping with this build: 24,530 objects, offsets validated,
  `applying` false, Extra Scan has nothing to look for, a game-thread invoke answers.
- AOT UI 62,882,304 bytes (sha256 cab59503ae5050a1...); UE5Dumper.dll 3,882,496 bytes (sha256 a445e371311bc29d...).

## 2026-11-20 (no build change) — Three staging recipes stage again `[STAGING-STALE-RECIPES]`

- **What changes:** `l41-step2-prefix-stall`, `l45-giveup-mutant` and `l55-denken-before` (the red arms of the closed
  L41 / L45 / L55 live rows) match today's code again; each was staged and built. l41's commit reversal no longer
  applied, so it is now a direct edit with the same effect (a cancelled enum search latches FAILED).
- **Found doing it:** 20 of the 22 recipes that reverse a fix commit no longer reverse at today's code -- red arms of
  their day, for closed rows. They stay as they are; `stage_dll.py`'s refusal now says to re-express one as direct
  edits if its row reopens. The list is in todo.md.

## 2026-11-20 (build 3775) — Extra Scan replaces a GObjects that reads no objects `[RESCAN-DECOY-REINIT]`

- **What changes:**
  - **Extra Scan** now helps when GObjects was found but reads **no objects** and both of init's recoveries missed
    (before, it answered "All scannable pointers already found"). It sweeps the game's .data for candidates, keeps
    the largest array whose first objects are named, and re-runs what that session had measured over an empty
    pool: the property offsets, the GEngine pointer, the UE version check, and the detections that sample objects
    once (UFunction flags and code pointer, enum names, FName and soft-path layouts). It then checks GWorld, as init
    does, and the scan cache stops pointing the next launch at the wrong match.
  - The **Pointers panel** offers Extra Scan in that case (with DLL 3775 or later), keeps the result line in view
    after the scan hides the button, and says what happened: replaced (with the object count), no candidate
    qualified, or why nothing was applied. The panels refresh after every apply.
  - Extra Scan on a GObjects of 0 picks the same address as before; it now also resets the two detections an empty
    pool latched, and checks GWorld afterwards.
  - While an apply re-measures, God Mode / movement / time / fly / See-through skip their re-assert ticks, a UE
    version override and a new Extra Scan wait ("A scan is still running"), and ProcessEvent detection waits while
    a recovery moves between candidates. God Mode re-reads its targets after any offsets re-measure.
- **Found by the live check, not the unit tests:** the first staged run on DumperTest kept a heap structure that read
  as 524,312 objects with 8 named ones among its first 1,009 slots. A candidate's first slots must now be named
  densely (at most two slots per name); the re-run picked the real array.
- **Tests:** red, then green, throughout; a design critique (3 lenses) and a code review (4 lenses, 13 LOW findings,
  all fixed); 12 mutations caught. dll_core_test 1971, dll_helpers_test 4456, C# 8543 (1 skipped), headless 74,
  40 gates. **Live:** staged on DumperTest Shipping (`rescan_decoy_live.py`, `RESULT: PASS`: the decoy replaced by
  the real FUObjectArray, 24,530 objects, offsets validated); stock control on DumperTest with this build: 24,530
  objects, offsets validated, Extra Scan has nothing to look for, a game-thread invoke answers. No shipped title is
  known to reach the replace (a verification-register row).
- AOT UI 62,882,304 bytes (sha256 c9c7a36f6e38972e...); UE5Dumper.dll 3,882,496 bytes (sha256 eff53902e43e6304...).

## 2026-11-20 (build 3774) — A UE 5.8 GObjects anchor and the GObjects Cheat Engine pointer `[UE58-ADJ10-WRONG-ANCHOR]` `[CEPTR-OBJECTS-2026-11-19]`

- **What changes:**
  - **UE 5.8 GObjects:** an address 0x10 below a 5.8 FUObjectArray validated (Genau's relaxed D read its ObjObjects
    with the count pairs swapped), so a GObjects pattern with a -0x10 adjustment that was the first to hit a 5.8 title
    handed over that address and Aura read a wrong non-zero count. Relaxed D now refuses that shape; the 5.8 base is
    what such a row resolves. No 5.8 title is known where those rows win today. Stock shapes are pinned unchanged.
  - **Cheat Engine pointer for an object** (`get_ce_pointer_info`, the MCP tool `read_ce_pointer`): the chain
    dereferenced once too often and ignored where the array's Objects field sits, so on every chunked layout a CE
    pointer record built from it led elsewhere. It now starts at the Objects field and reads three times; the reply
    gains `objects_offset`.
- Both were found while mapping `[VND10-A1]` and fixed in their own sessions, then merged here.
- **Tests:** each red, then green. dll_core_test 1906, dll_helpers_test 4417, C# 8523 (1 skipped), headless 74,
  40 gates. The CE pointer is not checked in Cheat Engine on a running game yet (a verification-register row).
- AOT UI 62,868,992 bytes (sha256 b98e00e135c5aab5...); UE5Dumper.dll 3,862,528 bytes (sha256 acf952c5109e4d21...).

## 2026-11-19 (build 3773) — Phantom Blade Zero's engine layout is supported, untested on the game `[VND10-A1]` `[VENDOR-AUDIT10-2026-11-19]`

- **What changes:**
  - **Phantom Blade Zero** (UE 5.7.4, a licensee layout): the DLL reads its object array (a reordered
    `TUObjectArray`, at the FUObjectArray base, at ObjObjects, and through the Count == 0 recovery when an AOB hands
    over its Objects field), its property type names (`FFieldClass::Name` at +0x30; `FField::Next` and `Owner`
    swapped), `UStruct::PropertiesSize` and `UFunction::FunctionFlags` past its 0x50-byte insert, and finds
    `ProcessEvent` by its flag tests at +0x100. The layout is RE-UE4SS's config for the game; **nobody here owns it, so
    it is tested on synthetic shapes only.**
  - **Stock engines read as before:** every licensee reading runs only where the stock readings find nothing, or is
    appended after the stock rows and held to stricter rules; each test pairs the PBZ shape with the stock shapes.
- **Not done:** audit #10's upstream lea + three-call GUObjectArray AOB. It helps PBZ only ranked ahead of the stock mov
  rows, where it moves the winning pattern on stock titles and meets a decoy in FF7 Rebirth; PBZ goes through the
  recovery instead. Extra Scan on a published GObjects of 0 objects was tried and reverted (todo `[RESCAN-DECOY-REINIT]`).
- **Rules recorded** (working-lessons §7.3): a licensee layout never moves the stock logic (#5); a file outside every
  test target is no reason to leave a change untested (#6).
- **Tests:** each part red, then green; an independent review's findings fixed; three mutations (the whole-chunks rule
  removed, +0x30 added to the stock FFieldClass probes, the PropertiesSize search run before the stock offset) each turn
  their checks red. dll_core_test 1761, dll_helpers_test 4305, C# 8523 (1 skipped), headless 74, 40 gates.
- AOT UI 62,868,992 bytes (sha256 bda90a049b8b6c52...); UE5Dumper.dll 3,860,992 bytes (sha256 eac54186f0848652...).
- **Release notes:** "Added support for Phantom Blade Zero's engine layout (not tested on the game)."

## 2026-11-19 (no build change) — The zydis bump rig prints all of Denken's output again `[VND10-A4]`

- **What changes:** `tools/verify/zydis_bump_diff/` prints each field access's write sites, so a Zydis bump that moved
  only where Denken finds a field's writes would now show. A control case covers upstream's newest delta, so a run over
  just the commits since the last review can pass. A field added to Denken's result later stops the rig compiling
  instead of going unprinted.
- **The rig's exit code means what it says:** a relative `--out` broke the run; a toolchain it could not load, or a
  crash of its own, read as "something needs reading" instead of "the rig failed"; and a candidate build that crashed
  where the base's completed read as "the rig failed" instead of a finding. All nine exit paths checked with the
  toolchain and harnesses mocked; the rig before the fix passes two of them.
- An independent review of the rig changes confirmed eight distinct findings (one MED: the toolchain's exit code); all
  are fixed.
- **zydis stays held:** from the pin to `4b31c34` on four binaries, byte-identical; recorded in `vendor-pin-reviews.md`
  with the new-format hashes, which do not compare with older runs.

## 2026-11-19 (build 3772) — UE 5.9 is recognised `[VND9-05]` `[VENDOR-AUDIT10-2026-11-19]`

- **What changes:**
  - **Version detection:** a UE 5.9 game reads as 5.9 (509) at every version-string tier; before, one whose version
    resource did not answer read as 5.8. Cached detections run once more (detection logic rev 12).
  - **Pointer panel:** the UE version override list offers UE 5.9.
  - **Game-only filters:** UE 5.9 moves Cascade into a plugin; its classes (`ParticleSystemComponent`, the
    `ParticleModule*` set) now count as engine in the DLL's game-only filters, Dump All's GameOnly skip and the
    analysis scripts.
  - The ProcessEvent slot for 5.9 is not measured yet, so a 5.9 game uses 5.8's slot and the log says so.
- **Vendor audit #10** (`docs/audit-2026-11-19-vendor-delta10.md`): what `sync_tools.ps1` brought in; this build is its
  `VND10-A2`. zydis stays held (recorded in `vendor-pin-reviews.md`).
- **Tests:** each part red, then green; `tools/verify/needle_sweep/` swept 174 exes (the corpus and both Steam
  libraries; 84 of them read a version string at some tier) with the old and the new needle table: none reads
  differently, none is unreadable, and a planted 5.9 control does. An
  independent review found no fault in the shipped code; its findings on the sweep rig, a survey script, a test and
  three comments are fixed. C# 8523 (1 skipped), headless 74, the five C++ self-tests, 40 gates.
- AOT UI 62,868,992 bytes (sha256 fbaaa7493dc32d24...); UE5Dumper.dll 3,857,920 bytes (sha256 e9c16be431ab5e83...).

## 2026-11-19 (no build change) — Build 3771's parameter-copy memory measured on a game `[LIVEFUNCS-UI-SNAP-MEMORY]`

- **Live, DumperTest58 Shipping, the run that found it** (two CameraModifier functions, the 32 MB buffer, 162 s,
  31,184 copies): loading the trace in Call Trace now peaks at 340 MB of working set and leaves 241 MB, with 110 MB of
  heap; build 3733 peaked at 1,381 MB and left 1,035 MB with 900 MB of heap. Every copy was kept: the decoded values
  shared down to under 1 MB. The Live Funcs line read "this UI up to 477 MB ..., about 352 MB after", a bound the load
  stayed under. The PostProcess settings summary wraps inside its own Parameters column.

## 2026-11-19 (build 3771) — Call Trace keeps a full parameter buffer in a fraction of the memory `[LIVEFUNCS-UI-SNAP-MEMORY]`

- **What changes:**
  - **Call Trace, loading parameter copies:** a decoded value that recurs from call to call (a camera's post-process
    settings, say) is now held once and shared, and so is a raw copy that recurs. Past 256 MB of decoded values the
    earliest calls keep only their raw bytes, and their Parameters tab says so; the Bytes tab still shows them. A full
    32 MB buffer of a 1,984-byte struct had taken the UI from 251 MB to 1,381 MB and left 1,035 MB.
  - **Live Funcs, the memory line:** this UI's figures now include the parameter copies it will keep (the snapshot
    buffer, plus their decoded values up to the 256 MB budget), and the free-memory warning weighs them.
  - **Call Trace, Parameters tab:** "At the call" and "After the call" share the room left after Name and Kind, and a
    long struct summary wraps in its own column instead of running into the next.
- **Tests:** each part red, then green; the keeper's sharing, its budget by call order, and 4,000 copies of an
  unchanging 400-field struct held as one tree; the layout headless at two pane widths and with a scrolling list.
  C# 8521 (1 skipped), headless 74, 40 gates. Not yet re-measured on a game (verification register
  `[LIVEFUNCS-STEP2-UIMEM]`).
- AOT UI 62,868,992 bytes (sha256 a0d8a6167f3a187b...); UE5Dumper.dll 3,858,432 bytes (sha256 2a7e3da50baf3e4e...).

## 2026-11-19 (build 3770) — The build uses MSVC whatever else is installed; the scan log names the game's linker and Wine `[MSVC-GUARD-2026-11-19]` `[SCAN-LINKER-LOG-2026-11-19]` `[IDGUARD-CANNOT-RUN-2026-11-19]`

- **What changes:**
  - **Scan log:** the scan log says which linker built the game (MSVC's link.exe, or lld for a clang build, from the
    exe's Rich header, linker version and debug entries) and, under Wine or Proton, the Wine version. When every AOB of
    GObjects, GNames or GWorld misses on an lld build, the warning says so: the patterns come from MSVC builds.
  - **Building from source:** `build.ps1` builds with MSVC even when Git Bash, MSYS2 or a Visual Studio prompt shaped
    the environment: a coreutils `link.exe` or MSYS2's `c++.exe` ahead on PATH, a `CXX` naming another compiler, a
    build folder configured with one, a prompt whose toolset a Visual Studio update removed, or the `Platform` a vcvars
    prompt sets (which moved the C# outputs and failed the tests). CMake refuses a non-MSVC compiler with a message
    that says what it found. The identity guard no longer reports a leak when it simply could not run.
- **Wiki:** Quick Start gained "Running on Linux with Proton" (English, zh-TW, ja-JP), from issue #522's report.
- **Tests:** `tools/verify/msvc_guard_probe.py` runs eight scenarios (stale-cache in three cache types), each red then green (builds 3749-3769 are its
  runs); dll_core_test pins the linker verdict on built headers and on its own image (1677 checks, 0 failed); the
  identity checker's selftest covers git missing, no checkout, an empty listing and a crash. Live: DumperTest Shipping
  logs `main module linker = msvc (Rich header yes, linker 14.38, 4 debug entries, VC_FEATURE / POGO among them)`.
  The publish ran with Git's and MSYS2's folders in front of PATH; C# 8510 (1 skipped), headless 71, the five C++
  self-tests, 40 gates.
- AOT UI 62,764,544 bytes (sha256 9a15ad661658f8dd...); UE5Dumper.dll 3,858,432 bytes (sha256 76864ffde7406fc2...).

## 2026-11-19 (no build change) — The rest of the test, tools, scripts and XAML comments read and cleaned; line references gated there too `[COMMENT-NO-HISTORY-2026-11-18]`

- **Gates:** `check_comment_refs` now also reads Python docstrings, Lua block comments, the .axaml and the .ps1 (through
  `tools/comment_lang.py`), so an in-repo line number, a false "no test target compiles X.cpp" or a dead doc name there
  fails it too; `check_comment_history` counts a dated event with no preposition (`Found 2026-09-06`, `Added
  2026-09-09`). The 110 reference findings and 8 history lines this exposed are fixed: a line number became the function,
  constant or test it pointed at, and the stale `aot-pitfalls.md` citations state their fact instead.
- **Pass Y:** a loose scan's ~4,800 candidate lines in the 633 test / tools / scripts / XAML files no earlier pass had
  read were judged in context by ten editor/reviewer pairs; 321 files changed. The reviewers checked each new sentence
  against the code and corrected 111, mostly rig docstrings describing code that has since moved on (a cache that now has
  invalidation, a function lookup that now climbs to base classes).
- **Verified:** comments only (321/321 by `comment_only_diff`, no line splice, Python compiles warning-free); the five
  C++ self-tests 0 failures; C# 8510 (1 skipped); the headless tests build; 40 gates. Both comment gates are at 0.

## 2026-11-19 (no build change) — The XAML, scripts and tools comments cleaned and gated; build.ps1 heals a removed toolset; quiet log tests `[COMMENT-NO-HISTORY-2026-11-18]`

- **Comment scanners:** `tools/comment_lang.py` gives XAML, Lua (and the .CT), PowerShell and Python (docstrings
  included) a comment scanner and a comment-free view of the code, each with self-test controls.
  `check_comment_history` now also counts `ui/UE5DumpUI`'s .axaml, `scripts/` and `tools/`; `comment_only_diff`
  compares those languages' code with comments removed (Python through its AST with docstrings blanked).
- **Pass X:** the 537 lines it counted there, and the bare finding codes beside them, are rewritten in 241 files;
  each verify rig's docstring keeps what the rig runs, compares and needs. The history baseline is 0 for the whole
  repo. Comments only (241/241); C# 8510 passed (1 skipped), the headless tests build, the Lua suites pass; 40 gates.
- **build.ps1:** a build dir whose CMakeCache.txt names a compiler, linker, mt or rc that no longer exists (a Visual
  Studio update removed that toolset) is dropped before configure, so the configure runs fresh instead of every C++
  step failing (seen on the second PC; proven on a simulated removed toolset; build 3748 is that run's number).
- **Tests:** the log-rolling tests no longer echo megabytes of log lines to the test console (`LoggingService`
  takes `writeToConsole`, default on for the app).

## 2026-11-19 (build 3747) — CE Lua hardening: Invoke numbers read in any locale, the scan module named without `process`, String records read-only by default `[CE-LUA-FLOAT-LOCALE]` `[CE76-PROCESS-GLOBAL]` `[CEXML-STRING-CAPACITY]`

- **What changes:**
  - **Invoke record (CE):** a number box is read whatever the numeric locale (a comma-decimal locale no longer
    turns "3.14" into 0.0), and text that is not a number is refused by name -- nothing is sent and the form stays
    open -- instead of going out as 0.
  - **AOB scripts and the trainer (CE):** the scan module is the opened process's main module from
    `enumModules()`, not the `process` global, which CE 7.6 overwrites when a table registers a custom type.
  - **CE XML String records** are read-only by default: CE writes a typed string whole, past the buffer the game
    allocated, whatever the record's Length. The value still displays; a double-click opens a locked list that
    writes nothing. The new toolbar Options switch **Editable string records** (kept across restarts) turns that
    off. +CE records (through AOBMaker) are not affected.
- The CE facts each fix rests on were read in the CE source and are in docs/CE-Bugs-Minesweeper.md, whose rows now
  say which are handled.
- **Tests:** each fix red, then green; the float reader also runs on CE's own Lua 5.3 under the C and a comma
  locale. C# 8510 total (1 skipped, 0 failed); 40 gates. Read from source and generated text; not
  run in a game.
- AOT UI 62,766,592 bytes (sha256 211842305f400e3c...); UE5Dumper.dll 3,853,824 bytes (sha256 69f5bd7a4a869657...).

## 2026-11-19 (no build change) — The tests' comments cleaned and gated; the touched product files re-swept; CE 7.7 AutoAssembler defects documented `[COMMENT-NO-HISTORY-2026-11-18]`

- **Test code:** `check_comment_history` also counts the comments of `dll/tests`, `ui/UE5DumpUI.Tests` and
  `ui/UE5DumpUI.HeadlessTests`. Their 872 counted lines and the finding codes beside them are rewritten in 185
  files: a test's doc says what it pins and why its assertions have their shape; review ids, dated measurements,
  build numbers and bug stories are gone. A comment that claimed no test target compiles Genau.cpp
  (dll_core_test does) is corrected.
- **Re-sweep:** a loose scan flagged 4,886 candidate lines in the 244 product files earlier passes touched; each
  was judged in context and 141 files changed (history without keywords, plan wording, claims the code
  contradicts, e.g. Schlacht's header, LiveWalker's CSX depth range, AppLifecycle's writer count). The baseline
  is 0 across product and test code.
- **docs/CE-Bugs-Minesweeper.md:** a new appendix section carries what the AOBMaker project measured in CE 7.7:
  `reassemble()` refusing, silently changing and dropping prefixes of instructions, the x64 RIP-relative
  rewrite's borrowed register, RIP text in a far cave, what the syntax check cannot see, disassembler text quirks
  and CE Lua defects. 44 CE source cites checked and corrected. The AutoAssembler groups do not reach this repo
  (our scripts are Lua-only); three follow-ups went to todo.md: `[CE-LUA-FLOAT-LOCALE]`, `[CE76-PROCESS-GLOBAL]`,
  `[CEXML-STRING-CAPACITY]`.
- Comments and docs only: `comment_only_diff` passes for every file (one verbatim-string false positive checked
  by hand); the DLL and the C++ suites (1663 / 4262 / 22 / 37 / 287) pass, C# 8497 passed (1 skipped), the
  headless tests build; 40 gates.

## 2026-11-19 (no build change) — Bare finding codes cut from the comments (pass 4), the untouched files swept, Genau.h's version log stripped `[COMMENT-NO-HISTORY-2026-11-18]`

- **Pass 4:** bare finding ids and plan-phase labels with no `audit` / `review` word (`(A12)`, `(AE13)`, `(B10)`,
  `V1c:`, `the Z10 lie`, `tracked as A10`) are cut from the comments of 110 files -- about 1,020 codes, each judged
  where it stands: real names stay (UE versions, AOB pattern and series names, compiler and trimmer warning codes,
  game and tool names, a doc entry named with its doc). No gate counts this shape, since one prefix is a finding in
  one place and a pattern name in another.
- **Sweep:** every comment of the 173 product files no pass had touched was read for history without keywords,
  plan-phase wording, claims the code contradicts and unasserted every/only claims; 93 files changed. Corrections
  found on the way include RelatedObject's depth range (1..3), the invoke entry points InvokeFunctionResult names,
  McpGrantTools' claims about the grant tools, a dangling cref in StructFieldValue and HelperLuaResource's callers.
- **Genau.h's `kVersionDetectLogicRev` log**, as the maintainer decided: the bump rule stays and each rev keeps one
  present-tense line saying what its logic does (checked against Genau.cpp); measurements, build provenance and
  before/after stories are gone.
- **Log text:** a RefineGroup warning no longer quotes an audit id; it ships with the next build.
- Editor then reviewer for every batch; `comment_only_diff` passes for every file but the log string's; the DLL
  builds; C# 8497 passed (1 skipped); 40 gates.

## 2026-11-19 (no build change) — Audit and review ids cut from the code comments (pass 3); the Snapshot group-match flake `[COMMENT-NO-HISTORY-2026-11-18]` `[SNAPSHOT-GROUPMATCH-FLAKE]`

- **Comments:** `check_comment_history` counts audit and review finding ids (`audit #5 A12`, `Audit fix #10`,
  `audit U17`, `(review DWS-DLL-1)`, `(review F4)`), each shape with positive and negative self-test controls. The 489
  lines it found in 143 files are rewritten without them, keeping every hazard, invariant, cross-reference and `[TAG]`;
  the baseline is 0 again. Five and then three editor/reviewer pairs; `comment_only_diff` passes for every file. Six
  comments the reviewers found contradicting the code are corrected: Solide's `s_workerMutex` is the outer lock with
  `s_mutex` nested inside, SpcQuery's per-row GWorld button has no GWorld requirement of its own, LoggingService no
  longer names a sweep that does not exist, IDumpService names a parameter instead of a wrong line distance,
  DiagnosticsProbe's busy-ms note drops its measurement story, and CeXmlExportService gives LazyObjectProperty's real
  layout (an `FWeakObjectPtr`, then the `FGuid`). The idle-wait gate's text names the hazard instead of the id the
  emitters' comments no longer carry.
- **Flake:** two Snapshot group-match tests failed once each under a heavily loaded full run (10 and 11 s). Their only
  10 s element was the wait for the VM's own refresh; not reproduced in 9 more loaded full runs, so the cause is read
  from the durations. That wait and the file's other 10 s wait share a 60 s hang bound, and the drain's failure now
  says how long it waited and the refresh's state; a 0 ms bound fails both tests with that message.
- Left, in todo.md: bare finding codes without an `audit` / `review` word (`(AE13)`, about 420 lines; the shape also
  matches design-phase labels), `Genau.h`'s version-rationale log (the maintainer's call), one log string in
  `Aura.cpp`.
- Comments and tests only: the DLL builds; C# 8497 passed (1 skipped); 40 gates.

## 2026-11-19 (build 3746) — Five review rounds of the UI-text fixes: 53 more places the UI, its comments and the runbooks disagreed with the code `[UI-TEXT-AUDIT-2026-11-18]`

- **What changes:**
  - **The AOBMaker dialogs** (Find Class Funcs / Functions using field, and Properties used by): ⚙ Disassemble in CE
    and Write site in CE say why they are greyed out, in the bridge's own words (busy, refused or missing), and open
    that tooltip while greyed. Each dialog checks for the plugin once as it opens, so reopening it after starting
    Cheat Engine enables the button. A button stays off while its own push runs: a second click no longer adds a
    duplicate record to CE's table.
  - **Dump Explorer:** Find Instances and the panel note describe the class-name search (substring unless Exact
    match, templates included) instead of promising live instances; the live-match progress line names classes,
    structs and enums.
  - **Live Funcs, Call Trace:** the Trace tooltip and the status lines say that with Params? / Stack? choices the
    chosen calls outside the ticked functions are recorded too.
  - **The Instances tab** is named by its label wherever a text sends you there (Value Search's inst, the Force
    templates note, a Live Walker status, and the DLL's no-target note in Related).
  - **Live Walker:** Collapse Pointer Nodes says every group below the root collapses.
  - **Value Search, Instances, Properties, Classes:** a truncation or cap note names the Max, Timeout, scan type,
    Game classes only and Deep the search was sent with, even when a box changed while it ran; the group note no
    longer offers raising Max at its 1,000,000 ceiling; First and Next Scan send the scan type they validated.
  - **Docs and comments:** working-lessons §2.6, the handover runbook, the verify rigs' docstrings and the
    auto-verification plan describe a fresh injection beside a proxy as a take-over once the injected copy has
    scanned (a proxy-mode row reads `get_pointers.load_mode`); stale comments are corrected in the two dialogs, the
    experimental gate, `Aura::GetFunctionCodeAddr`, `Methode.cpp` and `scripts/UE5CEDumper.CT`.
- **Kept by decision:** Value Search's single-mode truncation note does not name Max (`[P5-GROUP-ADVICE]`: its cap
  remedy is narrowing the predicate, with Max beside it); UT-42 changed only the group note.
- **Reviewed:** five rounds after the build 3745 fixes (`wf_31921b9d-4a0`, `wf_017295c2-e45`, `wf_c7f1ac00-7e7`,
  `wf_6b61eb19-451`, `wf_b9bce568-10b`), each finding checked by a second agent that tried to refute it: 18, 14, 9,
  10 and 7 confirmed, mostly the same defect at a sibling site one step further out. All 53 are fixed, one commit
  each, text and behaviour red then green; the register ([audit-2026-11-18-ui-text.md](audit-2026-11-18-ui-text.md))
  has every row's commits. The fifth round looked only for regressions in the fourth round's changes.
- **Tests:** C# 8497 total (1 skipped, 0 failed); C++ dll_core_test 1663, dll_helpers_test 4262,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 37; 40 gates. Read from source and
  not run on a game.
- AOT UI 62,751,232 bytes (sha256 4c7012363f1fc92b...); UE5Dumper.dll 3,853,824 bytes (sha256 ffede2e62a8c53f6...).

## 2026-11-18 (build 3745) — The UI's text says what the program does: 35 places the Wiki update found `[UI-TEXT-AUDIT-2026-11-18]`

- **What changes**, by panel (each row and its commits: [audit-2026-11-18-ui-text.md](audit-2026-11-18-ui-text.md)):
  - **Live Funcs:** an unloaded row's tooltip no longer says it cannot be traced (a function is followed by name);
    the Trace tooltip says what a Start records with ticks, with Params? / Stack? choices, and with neither; the
    Parameters row's **Clear** has a tooltip naming both columns it empties; the parameter copies are said to show in
    the Bytes tab too; Δ prints `-N` in every locale.
  - **Teleport:** the hotkey hint lists Get POV and Gravity Dir. **Call Trace:** the object line names the object the
    function was called on, not a caller.
  - **System:** a refused UE-version override puts the dropdown back to the version in force; the GWorld and &GEngine
    **SYM** tooltips say they register from the game's export when there is one.
  - **Live Walker:** a refused field write shows the DLL's own sentence, without "DLL error:" or an internal command
    name; the String Len, Options, Functions filter and Collapse Pointer Nodes tooltips name everything they apply to
    (Options: each setting is saved).
  - **Dump Explorer:** "Live match" counts classes, structs and enums alike; Find Instances names the **Instances** tab.
  - **Properties:** the max-results tooltip quotes the marker the status line prints; Freeze's tooltip gives the
    AOBMaker bridge's real state (busy, refused or missing) and shows on the greyed button; the type refusal names
    enums.
  - **Console:** Enter in the command box runs the command; the tooltips name the Invoke dialog by its title.
  - **Value Search:** group mode shows **Game classes only** and **Max:**, which its scan already used; the scan-type
    tooltip and its refusal list Between.
  - **Classes, Instances:** the buttons that hand a class to Instances say what that search returns (class names that
    contain it, templates included); the batch Find Func counts classes.
  - **Class Struct:** a failed instance pick drops the old class instead of leaving it under the error.
  - **Snapshot, SPC:** **Addr** on an array-element row is refused, as HEX and +CE are.
  - **Class Pivot:** on the DataTable source, Open in Live Walker and both Locates are off (a row is struct data, not
    an object).
  - **Docs and comments:** docs/mcp.md's troubleshooting quotes the server's own texts; stale comments corrected (ASM
    on a Blueprint function, the experimental switch, the Disassemble button, why an already-loaded copy means connect,
    including a comment line in the CE Inject record's Lua); docs/tips.md.
- **Kept as they are, by the maintainer's decision:** the experimental switch's look (UT-06), and Inject connecting to a
  copy that is already loaded rather than injecting a second (UT-21).
- **Correction to the 3729 entry** (append-only, so here): "The UI and the MCP server reconnect" holds for the MCP
  server, whose next call reconnects; the UI window does not reconnect by itself -- click **Connect**.
- **Tests:** the 28 text and behaviour rows each went red, then green; C# 8467 passed (1 skipped); C++ dll_core_test
  1663, dll_helpers_test 4262, utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 37; 40 gates. Read
  from source and not run on a game.
- Build 3744 was published before the last comment fix, which changes a comment line in the Lua the CE Inject record
  writes; it is not released.
- AOT UI 62,734,336 bytes (sha256 8bee8c9d6adc564f...); UE5Dumper.dll 3,853,824 bytes (sha256 e94b72a2921d6e4c...).

## 2026-11-18 (no build change) — History cut out of the code comments, pass 2; stacked doc blocks `[COMMENT-NO-HISTORY-2026-11-18]`

- `check_comment_history` counts more narrative shapes -- "Nth review", "review N of <hash>", a sentence opening on
  "Previously" or "Until now", "the old X was ...", "PROMOTED 210 -> 101", "the first version required ...", "turned
  out", "commit <hash>", a maintainer decision with its date, "observed on ... <date>", "before/until/since <date>" --
  each measured over the comments for few false hits, with positive and negative self-test controls. The 120 lines they
  found in 50 files are rewritten, and those files were swept for history without the keywords: 507 more blocks (stage
  and phase markers, review suffixes, dated captures, game-tied repro stories, decision provenance). Baseline 0 again.
- `check_comment_refs`' CSDOC check also catches a `///` line ending in `</summary>` after its text, right before a
  `/// <summary>` line. It found 14 stacked doc blocks: 11 moved back onto the member they describe (one merged with
  that member's own), 3 deleted because their member no longer exists.
- Comments only: `comment_only_diff` passes for every file; the DLL and the four proxies build; C++ 1663 / 4262 / 287
  / 22 / 37 and C# 8429 (+1 skipped) pass; 40 gates.

## 2026-11-18 (no build change) — History cut out of the code comments, pass 1 `[COMMENT-NO-HISTORY-2026-11-18]`

- Every comment line in `dll/src` and `ui/UE5DumpUI` that `check_comment_history` counts as history -- 715 lines in 180
  files -- is rewritten to what the code does now: fix narratives, review ids, build numbers, measurements and "used to"
  cut, each `[TAG]` kept, still-true hazards and invariants restated in the present tense. The baseline is 0.
- Done in batches, each by an editor and then a reviewer that checked for dropped hazards, added claims and lost tags;
  `tools/verify/comment_only_diff.py` (new) proved every batch touched comments only. The DLL and its four proxies
  build, the C++ tests (1663 / 4262 / 287 / 22 / 37) and the C# suite (8429 passed, 1 skipped) pass, 40 gates.
- Two stale comments the review found are fixed: a static import does not guarantee a proxy loads
  (`ProxyImportAnalyzer`), and a cref named an overload that does not exist (`PointerPanelViewModel`).
- History the patterns do not count is left in blocks this pass did not rewrite; the todo row plans pass 2.

## 2026-11-18 (no build change) — A gate keeps history out of new comments `[COMMENT-NO-HISTORY-2026-11-18]`

- `tools/check_comment_history.py`, gate 40 (check_all and CI): a per-file ratchet over the comments in `dll/src` and
  `ui/UE5DumpUI`. It counts comment lines holding a history pattern -- a review id, "review of build", "X used to", a
  measurement on a machine or date, a build number, "pre-fix" / "was fixed" -- and fails when a file's count grows
  (cut the line to its `[TAG]` and date) or shrinks (lower the baseline with `--update`). Baseline: 715 lines in 180
  files. Self-test controls run first; a new history comment added to a clean file fails it.

## 2026-11-18 (no build change) — Code comments carry no history `[COMMENT-NO-HISTORY-2026-11-18]`

- The maintainer's rule, now working-lessons §8 rule 7 and CLAUDE.md's comment line: a comment says what the code does
  and why, as it stands. No fix narrative, review id, measurement or bug cause -- at most a `[TAG]` and the date it was
  fixed, which todo.md and this log resolve; a function's header comment says what the function does, not how it got
  there. The history-laden comments already in the code are tracked by the todo row of that tag (a clean-up pass and a
  ratchet gate).

## 2026-11-18 (build 3743) — Review fixes: an AI's raw write cannot reach another DLL copy's mailbox, walk_class refuses what is no class, See-through survives a DLL hand-off `[WRITEMEM-ANY-COPY]` `[WALKCLASS-NOT-STRUCT]` `[SEETHRU-HANDOFF-LEFTOVER]` `[WALKCLASS-STOP-EPOCH]` `[WALKCLASS-STOP-OFFSETS]` `[MCP-RELEASE-EXPERIMENTAL-OFF]` `[MCP-WRITE-SITES-DESC]` `[MCP-DOCS-OLDER-CC]` `[TRACE-DROPPED-ENTRY]` `[LAND-MEMO-RESIDENT]`

- **What changes:**
  - **An AI's raw memory write** (`write_memory`) is refused inside ANY copy of UE5Dumper loaded in the game, not only
    the copy answering it. After a later copy takes over it serves the earlier copy's Cheat Engine mailbox, and a write
    there would have run a game function as Cheat Engine's, past the AI's deny list. The check walks the range's memory
    regions, so it holds while the game loads or unloads a module.
  - **walk_class** (the Class/Struct panel, MCP `read_class`) refuses a live object that is no class, struct or
    function: "Not a class or struct at ... -- it holds X (Y); walk_class reads a class, a struct or a function". That
    covers an actor at a freed class's address after a level change, and an Enum row in the object tree, which used to
    be walked as a struct.
  - **See-through across a DLL hand-off:** when a later copy takes over while actors See-through hid could not be shown
    again (the game thread was stalled), the later copy takes them over and its deferred restore shows them once the
    game thread answers; they used to stay hidden for the session. New C ABI export `UE5_HandOverHiddenActors` (65
    exports), listed in the four proxy .def files.
  - **The class-walk memo's stop** on an address whose contents keep changing survives a re-init that re-derives the
    same offsets, and the address is counted once; a re-init that corrects the offsets the identity check reads lifts it.
  - **MCP:** with the UI's experimental features off, the AI can still turn off or release what it holds (Fly, Keep
    Foreground, See-through, a force-field job); turning on stays refused. `read_function_props` tells the model which
    write sites are proven to write the field. docs/mcp.md and MCP-Setup.html say what an older Claude Code gets (the
    write tools, asked about on every call, no grants), and the no-write-tools row gives each cause its own remedy.
  - **Call Trace:** the status line says a dropped return draws the later calls on its thread inside its call, and a
    call with no return, in a trace with records left out, names a dropped return as a possible reason.
- **Correction to the 3741 entry** (append-only, so here): its "Live Funcs records stay whole when the trace buffer
  wraps" overstates `[LINIE-SEQ-LAST]`, which is narrowed, not closed -- two writers overlapping on one slot can still
  leave a mixed record under the newer number. Its "every confirmed finding was fixed red then green" holds for the
  code fixes; the rig, test and comment fixes went in commits of their own, test fixes checked by a mutation run.
- **Reviewed:** a seam review of PR #561's product code and three rounds on each round's fixes (`wf_1b9e937b-a0d`,
  `wf_bbd1bbfe-20d`, `wf_846108f2-663`, `wf_eec7e89a-a4a`): 13, 9, 4 and 2 findings, all fixed -- code red then green,
  tests and docs in their own commits.
- **Proven live** on DumperTest Shipping, build 3742: walk_class's kind gate 6/6 (`walkclass_kind_live.py`), L49 PASS,
  A9 6/6, bool / V1c 13/13, MCP raw writes 28/0 (the DLL's own mailbox refused, -25), MCP writes 42/0 (the server's
  Fly OFF goes through with the experimental switch off). 3743 changes only when the deferred restore gives up:
  See-through's restore set on DumperTest Development, arms (a) during motion, (b) on a stalled game thread and (c) a
  yanked connection, each against a negative control: PASS. Not run live: the hand-off path and the any-copy write
  refusal need a second DLL copy injected through Cheat Engine; covered by tests and source pins.
- **Tests:** C# 8429 passed, 1 skipped (one run of the full suite failed `Listing_sees_a_live_pipe_without_connecting_to_it`,
  untouched here, which passed alone three times and on the next full run: `[AOBM-LISTING-FLAKE]`); C++ dll_core_test
  1663, dll_helpers_test 4262; 39 gates.
- AOT UI 62,715,904 bytes (sha256 391b22b707dab7e4...); UE5Dumper.dll 3,853,824 bytes (sha256 4f0e152976dc93f1...).

## 2026-11-18 (build 3742) — The review fixes before round 9 `[SEETHRU-HANDOFF-LEFTOVER]`

Superseded by 3743 before it left this machine: the deferred restore could still give up on actors adopted during its
last pass that it never tried (review `wf_eec7e89a-a4a` R9-1).
- AOT UI 62,715,904 bytes (sha256 e180529d64b0331f...); UE5Dumper.dll 3,853,824 bytes (sha256 19544761cbf72893...).

## 2026-11-18 (build 3741) — A class at a reused address is walked again; Live Funcs records stay whole when the trace buffer wraps `[WALKCLASS-MEMO-REUSE]` `[LINIE-SEQ-LAST]` `[TRACE-DROPPED-ENTRY]` `[GROUP-RAW-WIDTH-INTERN]`

- **What changes:**
  - **A class at a reused address.** After a level change frees a class and another object is allocated at its
    address (the reloaded class, or any object of that size), everything that reads a class -- the Class/Struct
    panel, Value and Property Search, snapshots, the CE export, Force Field, God Mode -- was answered with the freed
    class's fields. Each remembered walk now keeps a witness of what it walked (the object's name and Outer, its
    property chains and its super), and a hit whose witness no longer reads back is walked again. An address whose
    contents keep changing is walked again at most 8 times running and then stops for good; one that holds still for
    3 reads is forgiven. `get_diagnostics`' `class_cache` reports `parked_walks` and `stopped_addresses`.
  - **Live Funcs, Call Trace.** A trace record's number is now stored after its contents, so a record overwritten
    while the buffer wrapped is left out rather than read back with another call's function, object and thread. The
    call tree keeps a call's callers open when its start was left out (their later calls used to become roots), and
    the status line, the JSONL export (`entries_dropped`, `other_dropped`) and the load log say how many records
    inside the kept part were left out.
  - **Group scan, native C.** Two widths matched at one unmanaged offset no longer share the first width's
    descriptor, so an int32 there is refined and shown as an int32, not as an Int16.
- **Proven live on 3739** (the DLL's code is unchanged since; 3740 and 3741 change the UI's wording and a rig): the
  Live Funcs trace 18/18, snapshots 34/34 and stacks 32/32 on DumperTest 5.8; the L49 unmapped-class rig three times
  in one game process and twice more on 3740; A9's deep budget 6/6 and the bool / V1c search 13/13 on DumperTest
  Shipping; a level reload: the freed class's address refused, the reloaded class walked. Timing medians (5 runs):
  value scan 51 ms, deep group scan 57 ms, property batch 38 ms, against 51 / 62 / 42 ms on 3736.
- **Reviewed** by five review rounds; every confirmed finding was fixed red then green.
- **Tests:** C# 8413 passed, 1 skipped; C++ dll_core_test 1649, dll_helpers_test 4246; 39 gates.
- AOT UI 62,713,856 bytes (sha256 f1743b9dea12c93d...); UE5Dumper.dll 3,847,680 bytes (sha256 320f7b840f971f00...).

## 2026-11-18 (builds 3737 to 3740) — The class-walk witness and the trace numbers, four review rounds `[WALKCLASS-MEMO-REUSE]` `[LINIE-SEQ-LAST]` `[TRACE-DROPPED-ENTRY]`

Superseded by 3741 before any left this machine. 3737 added the witness, the number-last trace writer and the
group-scan key; 3738 gave the witness the object's name and Outer and kept the call tree's callers open; 3739 made
the per-address stop sticky and counted every dropped record; 3740 stopped calling the unattributed ones returns.
- 3737: AOT UI sha256 e4944354...; UE5Dumper.dll 3,842,048 bytes (sha256 60bc7fbd...).
- 3738: AOT UI sha256 9e700e66...; UE5Dumper.dll 3,844,608 bytes (sha256 5dcfdcc9...).
- 3739: AOT UI 62,713,344 bytes (sha256 6eb80967...); UE5Dumper.dll sha256 aeb83b0c....
- 3740: AOT UI sha256 3d0f558b...; UE5Dumper.dll sha256 9b5487cf....

## 2026-11-18 (build 3736) — A class freed by a level change shows an error, not an empty or old class `[WALKCLASS-STALE-OK]` `[CLASSSTRUCT-FAILED-KEEPS-OLD]`

- **What changes:** after a level change, picking a class the change freed (it is still in the object tree) used to
  show either an empty class with no error, or the class's fields from before the change. The Class/Struct panel now
  says `DLL error: Not a live class at … -- freed (a level travel?) or never a UObject`, shows nothing under it, and
  picking the class again (after another row) asks the game again. The class that the level loaded in its place has a
  new address and shows normally.
- **Pipe / MCP:** `walk_class` (and the MCP's `read_class`) refuses an address that is not a live object, before
  walking it, instead of answering an empty class or a remembered one.
- **Proven live** on DumperTest Development with a level reload (verification-register.md `[AE23-STEP4-2026-11-18]`).
- **Verification pass on 3733-3736** (the register): besides 3733's list -- PEHOOK step 8's Run Self-Test on screen,
  the Solide cap clause, the batch Cheat Table keeping native and packed bools, Z8's class-noise picker, AD18's
  dinput8 proxy (SEED), the ProcessEvent slot on UE 5.0 (Light Maze), CADENCEBAND measured on Lushfoil, no unresolved
  bool on DQ XI S, Live Funcs step 3's mutants caught live, step 2's UI memory measured (7-14x its estimate:
  `[LIVEFUNCS-UI-SNAP-MEMORY]`, open), U2's FName containers on a case-preserving host, and AE23 step 4.
- **Tests:** C# 8408 passed, 1 skipped; C++ dll_core_test 1631, dll_helpers_test 4244; 39 gates.
- AOT UI 62,711,296 bytes (sha256 5f7771dd81990944...); UE5Dumper.dll 3,837,952 bytes (sha256 b0bb1670181d7531...).

## 2026-11-18 (builds 3734 and 3735) — `walk_class` and a freed class, two steps `[WALKCLASS-STALE-OK]`

Superseded by 3736 before either left this machine. 3734 refused only an EMPTY walk of a non-live address; a class
walked before the level change was still answered from the walk cache with its old fields, so 3735 refuses before the
walk. 3735's panel still kept the old class under the error, which 3736 fixes.
- 3734: AOT UI sha256 27db6954032b1ac9...; UE5Dumper.dll 3,837,952 bytes (sha256 7e62bf9310058867...).
- 3735: AOT UI sha256 2ab0535b3d70c787...; UE5Dumper.dll 3,837,952 bytes (sha256 6029fd497483ee9a...).

## 2026-11-18 (build 3733) — A group scan names a container element once `[GROUP-DEEP-INDEX-TWICE]`

- **What changes:** a Value Search **group** scan with Deep on named a container element's field with its index
  twice -- `Deep_Buckets[29].Subs[29].Leaves[26][26]`, and a struct element's leaf `Items[3].Count[3]`, which the
  Live Walker's "open at this element" then read as an element row. It now reads `...Leaves[26]` and
  `Items[3].Count`, and the group filter matches the name as shown (`Map_IntToVec3f[0].X`). Found while closing
  verification rows on 3732; proven live on 3733.
- **Tests only:** a Live Funcs trace-ring check that failed about one run in eight now admits the slot the ring leaves
  out by design (`[LINIE-THREADS-FLAKE]`).
- **Verification pass (the register):** closed live on 3732-3733 -- `[BOOL-NATIVE-SEARCH]` step 1, V1c's refine
  prune, FREEZESCOPE step 4's hundreds-scale pool, audit L1 A9 (the deep budget bites), M1/M2/M3 arm (d) through
  `UE5_Shutdown`, U1's degraded map read, the ProcessEvent slot on UE 5.1 / 5.3 / 4.23, b648 on UE 4.23, and Live
  Funcs step 2's UE4 model.
- **Tests:** C# 8406 passed, 1 skipped; C++ dll_core_test 1631, dll_helpers_test 4244; 39 gates.
- AOT UI 62,711,296 bytes (sha256 086510e7807f77c6...); UE5Dumper.dll 3,837,440 bytes (sha256 816b05abb8910829...).

## 2026-11-17 (build 3732) — Where a native function writes a field `[DENKEN-WRITE-SITES]`

- **What changes:** the Props dialog (a function's Properties) now shows, for a native C++ function, the addresses of
  the instructions that write each field it touches -- a field's write site found with no debugger, to start an AOB or
  a code injection there. A new **Write sites** column shows the first one and how many more; its tooltip lists them,
  says which write through the object itself (a site marked "?" is not proven to), and names a byte several packed bools
  share. **Write site in CE** opens the first in Cheat Engine's disassembler through the AOBMaker CE plugin; **Copy write
  sites** copies them. The dialog is wider, and its buttons wrap.
- **Also fixed by the review:** a write to a byte of packed bools was filed under the first bool only; every bool of
  that byte now has its row.
- **Pipe / MCP:** `walk_function_props` rows carry `write_sites`, `write_sites_via_this`, `write_sites_truncated` and
  `shared_offset`; the MCP's `read_function_props` passes them on.
- **Proven live** on DumperTest Shipping: 2,962 native functions walked, 73 written fields all with write sites, and
  each of 74 sites read back is an instruction whose own displacement is the field's offset.
- **Review** `wf_c87eff0e-53f`: 8 real findings, all fixed.
- **Tests:** C# 8406 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4242, dll_core_test 1627, utf8_helpers_test
  287, grausam_window_test 22, sein_retention_test 37; 39 gates.
- AOT UI 62,711,296 bytes (sha256 58ea842521122781...); UE5Dumper.dll 3,837,952 bytes (sha256 c4736f2aa8d425e1...).

## 2026-11-17 (build 3731) — Where a native function writes a field, first version `[DENKEN-WRITE-SITES]`

Superseded by 3732 before it left this machine: its review found sites through other pointers listed as the field's,
and packed bools filed under the first of their byte.
- **What changes:** the write sites, the column and the two buttons, as 3732 has them before the review. Live on 3731:
  71 written rows with sites, 72 of 72 checked.
- AOT UI 62,709,760 bytes (sha256 d7afbbcebb0923ef...); UE5Dumper.dll 3,833,344 bytes (sha256 64a4e372aa46ed6c...).

## 2026-11-17 (build 3730) — The second copy's take-over, after a third review `[DUMPER-SECOND-COPY-AUTOSTART]`

No visible change from 3729 in ordinary use.
- **What changes:** after a take-over, a mailbox command can no longer be answered into the wrong mailbox or run twice
  when two mailbox pollers briefly overlap. When a Cheat Engine table finds an earlier copy whose mailbox could not be
  taken over, it now says one of the two copies is out of date (the proxy in the game folder or the DLL injected later)
  rather than blaming the proxy.
- **Review** `wf_2875eae9-da5`: 7 real findings, all fixed.
- **Proven live** on DumperTest Shipping: a 3730 proxy yielded to the injected 3730 copy (14/14, five rounds of mailbox
  commands through the taken-over mailbox), and one injected copy alone behaved as before.
- **Tests:** C# 8401 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4230, dll_core_test 1621, utf8_helpers_test
  287, grausam_window_test 22, sein_retention_test 37; 39 gates.
- AOT UI 62,694,400 bytes (sha256 28611632a6ce47a3...); UE5Dumper.dll 3,828,736 bytes (sha256 b324230f2094564e...).

## 2026-11-17 (build 3729) — A second copy of the DLL in a game takes over from the first `[DUMPER-SECOND-COPY-AUTOSTART]`

No visible change unless a game holds two copies of the DLL -- a proxy (version / dinput8 / dxgi / winmm.dll) from the game
folder, and a copy injected later.
- **What changes:** the later copy now takes over, whichever build is newer (the maintainer's call). Once its own scan
  is done it shuts the earlier copy down, takes the pipe, and answers that copy's Cheat Engine mailbox too, so a CE table
  that found the earlier copy keeps working. An earlier copy built from this change on hands over and passes its CE calls
  on; an older one is shut down.
- **When the earlier copy cannot be served** (a mailbox layout from before contract 3, or a newer contract than the
  later copy's), its mailbox says SKIPPED and a CE script stops at once, saying another copy took over and the old one is
  usually a stale proxy to update or remove.
- **Logs:** a copy that cannot open its process's log files, because another copy or another instance of the game
  holds them, now logs in `Logs\<Process>.<module>.<pid>\` instead of nowhere. After a take-over that is where the
  serving copy's logs are.
- **The hand-off ends what the earlier copy held**, like unticking it in CE: god mode, movement, time, force-field and
  fly holds, Teleport markers, value-scan sessions. The UI and the MCP server reconnect.
- **Proven live** on DumperTest Shipping: a 3727 proxy shut down in 140 ms and a 3729 proxy yielded in 125 ms, each
  time with its mailbox answered by the injected copy; one copy alone behaved as before.
- **Reviews:** two rounds (`wf_67396083-685`, 22 real findings; `wf_08585a12-ddf`, 11), every finding fixed in its own
  commit group with mutation runs (11 of 11, then 11 of 11 after one pin was tightened).
- **Tests:** C# 8400 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4230, dll_core_test 1621, utf8_helpers_test
  287, grausam_window_test 22, sein_retention_test 37; 39 gates.
- AOT UI 62,694,400 bytes (sha256 cdccd28ebb057c2a...); UE5Dumper.dll 3,827,200 bytes (sha256 8d9e18b0e69e72b8...).

## 2026-11-17 (build 3728) — A second copy of the DLL takes over from the first `[DUMPER-SECOND-COPY-AUTOSTART]`

Superseded by 3729 before it left this machine: its live run found that the copy that took over could not log.
- **What changes:** the take-over as 3729 has it, before review round 2. Live on 3728: a 3727 proxy shut down and a 3728
  proxy yielded, both mailboxes served by the injected copy; the later copy's own log lines were missing.
- AOT UI 62,691,840 bytes (sha256 90756cc083901958...); UE5Dumper.dll 3,821,568 bytes (sha256 8bd5af7b36fe8f77...).

## 2026-11-17 (no build change) — Claude Code's own prompts for the two MCP grants, checked live; the 繁中 checklist retired `[MCP-WRITE-GRANTS-2026-11-14]`

No program change.
- **What was checked:** on the 3727 MCP server and DLL (DumperTest Shipping, a Claude Code desktop session in auto
  mode), the maintainer read Claude Code's prompt for each grant as the tool's name, then Function or Field, then
  Class, then the scope_ack sentence, each whole, and pressed Allow.
  - **Invoke grant:** `KismetMathLibrary.Add_IntInt` ran three times unasked (5, 42, 993). `write_invoke` of
    another function still asked; it was denied and never reached the server.
  - **Field grant:** `DumperTestHolder:HolderValue` was written four times unasked and put back. `write_memory` on
    that same field still asked.
  - **Log:** the server logged one `Grant N:` line per grant. Both register sections (Phase 1b, Phase 2) are now
    closed.
- **Removed:** `docs/pending-verification_zh-TW.md`, the checklist of checks to run by hand, at the maintainer's
  request: nearly every check it held is automated now, and its last three items were already register rows. Its
  bucket tool and two spent helpers went with it.

## 2026-11-17 (build 3727) — A UE 5.8 object array its strict layout refuses is read whole; decoys still reach recovery `[UE58-PREALLOC-NO-ROW]`

No visible change unless a UE 5.8 game's object array fails its strict layout, which a large stock game does.
- **What changes:** UE 5.8 reordered FUObjectArray, and Aura's fallback (relaxed) tier had no layout for that shape. A
  5.8 array the strict layout refuses -- more than 20 live chunks (above 1,310,720 objects), a count at an exact
  multiple of 64K at scan time, a preallocated array, or fewer than 6 max chunks -- was read through an unreadable
  Objects pointer with a count above 0, so no object resolved and the decoy recovery never ran. Relaxed layout F now
  reads it whole. It takes the 0x1000 floor, its chunk counts must hold together as UE's PreAllocate leaves them, and
  its Objects must lead to a real chunk table.
- **3726 published F without the chunk check;** the review found it took a TArray<UObject*>-shaped decoy (the Avowed
  class), which kept the count above 0 and the recovery from running. 3727 refuses it again, so such a decoy still
  reads as no array and recovery runs.
- **Proven live** on DLL 3727 that no title's own layout moved: DumperTest58 `UE5.8`, DumperTest `UE5-Extended`, and
  Avowed (proxy refreshed) `UE5-Extended`, 92,003 objects at the main menu, each `relaxed_layout_live.py` 4/4. No corpus
  title carries a refused 5.8 array; dll_core_test holds each trigger and the decoy.
- **Reviews:** two rounds (`wf_47eca989-bf9`, `wf_a2cdbbd9-859`), every finding fixed in its own commit.
- **Tests:** C# 8391 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4166, dll_core_test 1621 checks (ASan:
  1619 at 3726), utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,691,840 bytes (sha256 514f46ada4430e44...); UE5Dumper.dll 3,799,040 bytes (sha256 49bc79fc6c88614d...).

## 2026-11-17 (build 3726) — Relaxed layout F for UE 5.8 arrays the strict layout refuses `[UE58-PREALLOC-NO-ROW]`

Superseded by 3727 before it left this machine: the review of this build found its new row took a TArray-shaped decoy.
- **What changes:** relaxed layout F (UE 5.8's slots) between B and D, with the floor, a first-entry check and a
  chunk-table check; dll_core_test 1619 checks, 0 failures (ASan 1619). Live on 3726: DumperTest58 `UE5.8`, DumperTest
  `UE5-Extended`, 4/4 each.
- AOT UI 62,691,840 bytes (sha256 2bd1b67fb8de98d0...); UE5Dumper.dll 3,799,040 bytes (sha256 baceddca31f4fedb...).

## 2026-11-17 (build 3725) — A preallocated object array's block pointer is not read as its count `[RELAXED-D-PREALLOC-HALF]`

The maintainer asked for this row the same day it was filed. No visible change unless a game preallocates its object
array (gc.PreAllocateUObjectArray) and its array fits no strict layout.
- **What changes:** UE 4.20-5.7 keep ObjObjects.PreAllocatedObjects right after the Objects pointer, which on a
  base-anchored array is where the UE4-Extended and flat layouts read Max and Num. A preallocated array that its own
  strict row refused could then be read with the block pointer's high dword as its object count -- by relaxed D, which
  had no floor at all, or, above 16 TB, by the strict Flat-Base layout first. UE stores that block as the chunk table's
  first entry, so every strict row and relaxed D now refuse a count that lies in a qword equal to their own table's
  first entry: exact at any heap height, and no genuine Max / Num pair can match it. Relaxed D also takes the 0x1000
  floor A/C and B have.
- **Proven live** on DLL 3725 that no title's own layout moved: DumperTest Shipping `UE5-Extended`, DumperTest58
  `UE5.8`, NEKOPALIVE (UE 4.11) `Flat-Base`, each `relaxed_layout_live.py` 4/4 with the object count and a GWorld walk.
  No corpus title preallocates; dll_core_test holds the misread above and below 16 TB.
- **Review:** `wf_7e04517c-ef7`; its three surviving findings were wording, fixed. It also found a different, older
  gap: a preallocated UE 5.8 array has no relaxed layout of its own (`[UE58-PREALLOC-NO-ROW]`, filed).
- **Tests:** C# 8391 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4166, dll_core_test 1606 checks (ASan:
  1606), utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,691,840 bytes (sha256 09d5761e865f67ea...); UE5Dumper.dll 3,798,528 bytes (sha256 74e336aee109efba...).

## 2026-11-17 (build 3724) — An object array no strict row fits is no longer counted from half of a pointer above 16 TB `[VND584-03-FLOOR-HIGH-HEAP]`

The maintainer asked for this row the same day it was parked. No visible change unless a game's object array fits no
strict layout.
- **What changes:** when no strict FUObjectArray layout fits, the relaxed fallback rows A/C and B read the object count
  from a slot that, on some layouts, is the high half of the real chunk table pointer. A 0x1000 floor refused that only
  while the table sat below 16 TB -- a chunk-counts-first UE 5.7 array (the Mistfall Hunter shape) with its table
  higher would have been read with a few thousand objects through a garbage table. The rows now follow the 8 bytes
  under the count as a pointer and refuse the count when they lead to a chunk table and the row's own Objects slot does
  not, so no array a row read before is refused; when A/C is refused and nothing else fits, the last-resort layout reads
  Objects where that table is. The test suite places a chunk table above 16 TB in every build, and the
  AddressSanitizer run no longer skips the check.
- **Proven live** on DLL 3724: the Outer Worlds 2, the one title that reaches relaxed A/C, is still read by it
  (`relaxed_layout_live.py` 4/4: 122,404 objects, nothing refused, GWorld walk at the title screen; its proxy is
  refreshed to 3724). No chunk-counts-first title is in the corpus; dll_core_test holds it.
- **Reviews:** three rounds (`wf_8fb9838c-132`, `wf_26ab4e6e-ff7`, `wf_16792939-c49`), every finding fixed in its own
  commit -- the first made the check unable to refuse a genuine array and fixed the fallback. Relaxed D's own gap (no
  floor; its count can be half of PreAllocatedObjects) is filed as `[RELAXED-D-PREALLOC-HALF]`.
- **Tests:** C# 8391 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4166, dll_core_test 1599 checks (ASan:
  1599, three runs), utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,691,840 bytes (sha256 3f27f5917252d9d0...); UE5Dumper.dll 3,798,528 bytes (sha256 5ee6847cfd06d433...).

## 2026-11-17 (build 3723) — A class walk that starts while an init is ending waits for it `[CPP-HARDENING-2026-11-14]`

A follow-up to build 3722's `[DYNOFF-LATCH-STALE]` fix, found by the fourth review round. No visible change.
- **What changes:** the end of an offset validation bumps a count and the property layout's epoch as two steps. A
  class walk that read the count between them saw the new count while its plain walk still used the old epoch, and
  could keep a walk made with half-measured offsets for the rest of the session -- a window of nanoseconds, once per
  init. The count is now seqlock-shaped (odd while the retire is under way, even only after the epoch moved) and the
  walk waits out an odd count. The `check_property_family` gate now pins the epoch's writers and the retire's order,
  in every form an atomic write takes.
- **Proven live** on DLL 3723 (DumperTest Shipping, UE 5.4): `latch_regression_live.py` 4/4 injected and 4/4 with a
  `dxgi.dll` proxy, `initlog_live.py` 6/6 (proxy removed afterwards); `mcp_client.py` probe 29/29, raw 28/28,
  fieldgrant 44/44. The interleaving itself is dll_core_test's: the retire left half done, finished from another
  thread.
- **Reviews:** round 4 (`wf_09b53b67-caf`) found it; a fifth (`wf_090d2608-d9f`) found two over-claims -- a comment and
  the gate's patterns -- both fixed. The rounds converged there.
- **Tests:** C# 8391 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4166, dll_core_test 1586 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,691,840 bytes (sha256 27c555bdcdc0f863...); UE5Dumper.dll 3,797,504 bytes (sha256 304357adf4dae173...).

## 2026-11-17 (build 3722) — An init's offsets are checked again when it ends, and a command read during it leaves a trace `[CPP-HARDENING-2026-11-14]`

The hardening analysis's two init-window logic gaps, fixed as the maintainer decided on 2026-11-17 and reviewed in
rounds with the maintainer present. No visible change in the UI.
- **What changes:**
  - `[DYNOFF-LATCH-STALE]`: the struct-slot calibration (CorrectSubclassOffsets) latched once per process, so a
    property layout published after it -- Genau's measurement on a later init, or the defaults a re-run init applies
    first -- was never checked again, and a derivation one pointer off stayed for the session. The latch now records
    the layout's epoch, which every layout move re-arms; and the end of every offset validation retires the epoch, so
    classes cached during an init, read with offsets still being measured, are walked again afterwards even when the
    layout did not move. A class walk that straddles the end of a validation walks again.
  - `[PIPE-WALK-DURING-INIT]`: the pipe still answers every command during an init (proxy mode's trigger_scan, an
    Extra Scan apply) -- nothing is refused -- and now writes each command name once per run to the pipe log,
    `<cmd> arrived during an init or rescan apply -- offsets or pointers it read may not be final yet`: the trace a
    wrong answer from that window needs.
- **Proven live** on DLL 3722 (DumperTest Shipping, UE 5.4): `initlog_live.py` 6/6 with a `dxgi.dll` proxy (four
  lines, one per name, over 48 commands, every one answered); `latch_regression_live.py` 4/4 injected and 4/4 in proxy
  mode (every StructProperty of DumperTestActor names its struct, no calibration move); `mcp_client.py` probe 29/29,
  raw 28/28, fieldgrant 44/44. The latch's failure itself needs a mis-derived layout no fixture has; dll_core_test
  holds it.
- **Reviews:** three rounds (`wf_8f451d97-434`, `wf_b0aa9098-dd5`, `wf_2d9f83d5-21b`), every finding fixed in its own
  commit; a fourth (`wf_09b53b67-caf`) found one more straddle, fixed in build 3723. Round 1's B-INIT-WINDOW -- what an
  init cached mid-run surviving it when the layout did not move -- predates this build: a first proxy-mode init already
  had it.
- **Left as decided** (maintainer, 2026-11-17): `[VND584-03-FLOOR-HIGH-HEAP]` waits for a real game; no ASan CI job.
- **Tests:** C# 8391 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4166, dll_core_test 1585 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,691,840 bytes (sha256 bca2b9ffcdfdb536...); UE5Dumper.dll 3,797,504 bytes (sha256 a04c5cffa6fef726...).

## 2026-11-17 (build 3721) — The remaining lazily measured offsets are race-free reads `[CPP-HARDENING-2026-11-14]`

The last code follow-up of the maintainer's C++ hardening item 2 (race-free reads of calibrated offsets). No visible
change.
- **What changes:** eight more DLL offsets -- the soft and lazy pointers' payload envelopes, UEnum::Names' layout
  (offset, value size, pair stride) and UFunction's Func, flags and tail -- are measured by whichever walk first meets
  the evidence while the scan workers and other pipe connections read them. As plain ints that was a formal data race,
  as the property family's was before build 3712; they are now `DynOff::RuntimeOffset` (relaxed atomics),
  pinned by eight `static_assert`s in dll_helpers_test. The compiler found every site that needed more than the
  implicit int (the envelope latch, four log arguments, one saved test value).
- **Proven live** on DLL 3721 (DumperTest Shipping, UE 5.4): the DLL log shows the measurements still land through the
  new type (TSoftObjectPtr and TLazyObjectPtr envelopes +0x08, FunctionFlags +0xB0 at 64/64 votes); `mcp_client.py
  fieldgrant` 44/44 (an enum written by its entry), `invokeparams` 30/30 (+3/3 apart, soft-pointer struct fields),
  `invokegrant` 29/29, `raw` 28/28, `holdsmore` 78/78.
- **Open in the item:** an AddressSanitizer CI job; and the hardening analysis's LOW rows `[DYNOFF-LATCH-STALE]`,
  `[PIPE-WALK-DURING-INIT]` (which needs a decision on which commands count as walking) and
  `[VND584-03-FLOOR-HIGH-HEAP]`, left for a session with someone to review a calibration change.
- **Tests:** C# 8390 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4150, dll_core_test 1574 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,691,840 bytes (sha256 410efce4173dc2a6...); UE5Dumper.dll 3,795,456 bytes (sha256 db0680bfa8fed215...).

## 2026-11-17 (build 3720) — `[[nodiscard]]` batch 2: a refused Fly preset is an error, not a silent no-op `[CPP-HARDENING-2026-11-14]`

The second batch of the maintainer's C++ hardening item 1 (2026-11-14: cheap hardening instead of a Rust rewrite).
- **What changes for a user:** `fly_set` with a key preset other than 0 (WASD), 1 (numpad) or 2 (arrows) used to be
  dropped while the reply read as applied. It now answers an error and changes nothing in that request (not the speed,
  noclip or ON/OFF it also carried), and records no holder. The UI, the MCP server and CE send only valid presets, so
  this reaches a raw pipe client or a script.
- **The batch:** 28 more DLL declarations are `[[nodiscard]]` -- the gameplay setters (god mode, movement, gravity
  direction, time dilation, Fly, See-through, Keep Foreground, forced fields), the teleport API, and the
  ProcessEvent hook's invoke and install entries. The compiler (`/we4834`, a build error) found the five discards
  batch 1 had recorded: the preset above; Fly's speed and noclip setters, which cannot fail and stay unannotated; and
  the shutdown's See-through and Keep Foreground OFFs, which return 0 by contract. The teleport's file-local helpers
  were tried and left unannotated: every teleport measures where the pawn ended up afterwards, so their 9 discards are
  by design.
- **Review:** a fresh-context review with a verifier (1 LOW, 3 INFO, all confirmed and fixed before the build): the
  shutdown's two warnings could never fire, a refused preset could still record a holder, a stale comment, a count.
- **Proven live** on the AOT exe and DLL 3720 (DumperTest Shipping, UE 5.4): `tools/verify/flyset_preset_live.py` 4/4
  -- preset 7 refused with the preset unchanged, preset 7 with a speed refused with neither changed, a valid preset
  taken, the originals put back. Regressions: `mcp_client.py writes --toggle-experimental` 39/39 (Fly among them),
  `holdsmore` 78/78, `raw` 28/28, `probe` 29/29.
- **Also since 3719 (docs and rigs, no code):** the verification register's holder-labels batch is closed (rows 3-6
  live: the experimental cards' chips, a restarted window's "exited" hold, a queued camera ON and an owed release --
  that row's premise corrected, since a departed AI's queued ON is dropped and never runs -- and a CE invoke behind a
  busy camera lock), and Phase 1b's row 3 (a Blueprint function under an invoke grant, the DLL's -28 naming the
  override).
- **Tests:** C# 8390 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4150, dll_core_test 1574 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; nodiscard_probe OK; 39 gates.
- AOT UI 62,691,840 bytes (sha256 a055a972c7d6dbf7...); UE5Dumper.dll 3,794,944 bytes (sha256 e45e174c31b01b24...).

## 2026-11-17 (build 3719) — An AI's UFunction call: an object argument must be of the class its parameter declares `[INVOKE-OBJECT-PARAM-CLASS]`

The LOW hardening item from Phase 1b's review, for `write_invoke` and `write_invoke_granted_function` alike.
- **What changes:** an object argument was checked for being a live object only, so a native function could be
  handed an object of another class and call through its vtable, or read its fields, as if it were the declared one.
  Now a top-level object argument (strong, weak or soft) must be of the parameter's declared class or a subclass,
  matched by the class's short name; a class parameter (`UClass*`, `TSubclassOf`) takes a class object only. Refused
  before anything is sent: "'Player' takes a PlayerController; 0x... is a ..., which does not derive from it." A
  chain that cannot be read is refused without a claim about the object's kind. Held to liveness only, as before: an
  interface parameter, one declared as any object (no class walk at all), a struct's object field and a parameter whose
  declared class the DLL could not read.
- **Review:** a fresh-context review with a verifier (2 LOW, 4 INFO, all confirmed), each fixed before the build: the
  class parameters, the unread-chain wording, Object's free pass, the dead Lazy arm, the short-name note and the docs.
  Mutation checks: 4 of 4 killed.
- **Proven live** on the AOT exe and DLL 3719 (DumperTest Shipping, UE 5.4): `mcp_client.py invokeparams` 30/30 (+3/3
  apart), its new step C: `GameplayStatics.GetPlayerControllerID` given the pawn refused by the server, no invoke line
  in the pipe log, and given the live PlayerController run, returning 0. Regressions: `invokegrant` 22/22, `fieldgrant`
  44/44, `probe` 29/29, `raw` 28/28, `writes --toggle-experimental` 39/39, `holdsmore` 78/78.
- **Also in this build's docs (no code):** the verification register's Phase 2 rows 3 and 4 closed on 3718 -- a
  Blueprint class's field (`ABP_Manny_C`'s `GroundSpeed`, written on the live `ABP_Quinn_C`) and a packed bool on a
  live object (`Actor:bCanBeDamaged`, only its own bit) -- through `mcp_client.py fieldgrant`'s new steps 4c and 4d.
- **Tests:** C# 8389 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4150, dll_core_test 1574 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,691,840 bytes (sha256 634c05dfe24ec4c3...); UE5Dumper.dll 3,794,432 bytes (sha256 84ba09fb89ade730...).

## 2026-11-17 (build 3718) — MCP field grants: one field of one class, approved once `[MCP-WRITE-GRANTS-2026-11-14]`

Phase 2 of docs/mcp-write-approval-eval.md (§4.5): the typed twin of the UI's field edit, approved once per game run.
- **What changes for a user of an AI client:** `grant_write_field_one_property_every_instance` asks once, for one game
  run, to let the AI write ONE field of ONE class, named by the field and the class's full path as `read_class` shows
  them (e.g. `HolderValue` and `//Script/DumperTest/DumperTestHolder`), on every live instance of that class and its
  subclasses (other characters included), with any value its type allows. `write_field` then writes it without asking
  again. `read_grants` names the field and counts its writes; `revoke_grants` ends one field's grant, named as
  `read_grants` shows it. `write_memory` still asks every call, for anything else.
- **What a field grant covers:** a number, a bool or an enum field, one element -- never a struct, a string, a
  reference, a container or a static array, nor a struct's or a function's member. Each write goes to the address the
  server computes from the granted class's layout, checked against the object's own walk; the gate passes the write
  nowhere else, and it carries the bytes read just before it (expect). An enum takes one of its own entries (by its
  qualified or short name, or its value; never its trailing `_MAX`, and nothing when the game's enum names could not
  be read); a packed bool keeps its sibling bits; `42.0` or `1e3` for an integer reads as 42 or 1000.
- **Refused:** a template -- a class default, one of its subobjects, a Blueprint component template -- by the DLL's
  own verdict; a field of the same name on a class outside the grant's derivation; and a walk of another object than
  the one resolved (freed, its slot reused).
- **The DLL:** `get_object` now names its template verdict (`template`: "", "class_default" or "template"), so field
  grants need DLL 3718; an older DLL is refused with that floor's own reason.
- **The server's instructions** name the field grant; what stays after the session names
  `write_invoke_granted_function` and `write_field` too; the per-call exceptions follow the server's refusal of an
  invoke or a field grant, never a declined prompt or the user's block; a host that asks every call no longer names
  tools it does not list. Under "Block AI writes", `write_field` answers with the block.
- **Reviews:** two fresh-context rounds, each with an adversarial verifier. Round 1, 26 rows (3 MED: archetypes written
  under a grant, and two in the live rig; 11 LOW; INFO) in 18 distinct issues, each fixed or covered by a test; round
  2, 22 rows (LOW / INFO) in 12, fixed. Mutation checks: 8, 16 and 4 mutations, every one killed.
- **Proven live** on the AOT exe and DLL 3718 (DumperTest Shipping, UE 5.4): `mcp_client.py fieldgrant` 33/33 --
  grants refused for a struct, a static array, a short class name and a missing field; `HolderValue` granted on
  `DumperTestHolder`, written unasked on a spawned `DumperTestDerivedHolder` (4321.5, read back) and put back byte for
  byte; refused on a `DumperTestHolderDecoy` (the same field name, no derivation), on the class default and on
  `Default__Character`'s movement component (an archetype named like any component); a float past its range refused;
  a native bool set and cleared, a 4-byte enum set by its short name (`Wide_High`, all four bytes) and refused a value
  that is no entry, each put back; one grant revoked while the others stand. Regressions: `probe` 29/29, `invokegrant`
  22/22, `raw` 28/28, `invokeparams` 26/26 (+3/3 apart), `writes --toggle-experimental` 39/39, `holdsmore` 78/78;
  `olddll` 41/41 on a fresh 3695 DLL (the field grant and `write_field` refused "older than 3718", nothing sent).
- **Not seen live:** Claude Code's own prompt for the field grant (the session's server is an older build), a
  Blueprint class's field and a packed bool on a live object (the fixture's flag is a native bool): the verification
  register's new batch, each with its acceptance test.
- **Tests:** C# 8379 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4150, dll_core_test 1574 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,684,160 bytes (sha256 b8278dc7993e07f3...); UE5Dumper.dll 3,794,432 bytes (sha256 176a023f13160af1...).

## 2026-11-17 (build 3717) — MCP invoke grants: one game function, approved once `[MCP-WRITE-GRANTS-2026-11-14]`

Phase 1b of docs/mcp-write-approval-eval.md (§4.4): CE's point for invoke, without unmarking arbitrary calls.
- **What changes for a user of an AI client:** `grant_invoke_one_function_any_object_and_args` asks once, for one game
  run, to let the AI call ONE UFunction, named by its function and its class's full path as `read_functions` shows it
  (e.g. `SetActorHiddenInGame` and `//Script/Engine/Actor`); the prompt shows the function first. `write_invoke_granted_function` then calls that function on any object, with any
  arguments, without asking again. `read_grants` names the function and counts its calls; `revoke_grants` ends one
  function's grant. `write_invoke` still asks every call, for anything else.
- **What no invoke grant can name** (refused when asked for, and by the DLL at every call): the DLL's deny list, a
  network RPC, a Blueprint event graph, and a function whose arguments reach other functions or properties by name --
  a delegate, a property path, a UFunction, a latent call, or a name, string or struct parameter named for a function,
  event, property, command, delegate or callback (`K2_ClearTimer`'s FunctionName, `SendGameplayEventToActor`'s
  EventTag). Under a grant the DLL also refuses a non-static function on a class default or a template, and checks
  that the function it resolves is the granted one by full name (`grant`, `expect_func_full`; code -28).
- **The AI's invoke deny list (every AI call, per call too)** gains what a scan of the UE 4.18-5.8 sources found: UE4's
  achievement write (`WriteAchievementProgress`), an async action's `Activate` (the online-service and save actions),
  latent calls, `RemoteEvent`, `SetBind`, the timeline's `AddInterp*`, `EditTextPropertySourceString`, the save slot's
  local-player twins, and every streaming-level and level-instance load and unload.
- **Reviews:** a fresh-context review with a refuter per finding (10 LOW confirmed, 1 refuted), each fixed or covered by
  a test: Blueprint function names with a space, '?' or '*' are grantable; a struct parameter named for an event is
  refused under a grant; the other level-loading functions are refused; the text that overstated when the grant's
  lookup runs and which replies carry `func_full` is corrected.
- **Proven live** on the AOT exe and DLL 3717 (DumperTest Shipping, UE 5.4): `mcp_client.py invokegrant` 22/22 -- a
  grant on `KismetMathLibrary.Add_IntInt` and its unasked call returning 42; six grants refused (a deny-list name, a
  timer by name, a latent call, a FunctionName parameter, a missing class, a short class name); a granted
  `SetActorHiddenInGame` refused by the DLL on a class default and run on a live StaticMeshActor; the DLL's own checks
  driven as a background client through the pipe (another function -28, a FunctionName parameter -28, a latent call
  -24 per call); one function's grant revoked while the other stands. Regressions: `probe` 29/29, `raw` 28/28,
  `invokeparams` 26/26 (+3/3 apart), `writes --toggle-experimental` 39/39, `holdsmore` 78/78.
- **Not seen live:** Claude Code's prompt for the invoke grant (this session's server is an older build); the argument
  order it shows is pinned by `McpToolsTests.TheInvokeGrant_StatesItsScope_WithTheFunctionFirst`, and Phase 0 (M1)
  measured that the prompt shows each argument whole.
- **Tests:** C# 8311 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4150, dll_core_test 1574 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,601,216 bytes (sha256 9ef40a357cd0dbb5...); UE5Dumper.dll 3,793,920 bytes (sha256 7dcde89886bcf970...).

## 2026-11-16 (build 3716) — each gameplay card says who else holds it `[MCP-WRITE-GRANTS-2026-11-14]`

The rest of Phase 1's UI half (docs/mcp-write-approval-eval.md §4.10): once grants took the per-call prompt away, this
is how the user sees what an AI session holds.
- **What changes on the Teleport tab:** every gameplay card with a hold (Debug Camera, God Mode, Stealth Meter, Keep
  Foreground, Move Speed, both time lanes, Gravity, Super Jump, Fly, See-through, Gravity Direction) shows an amber
  chip beside its state badge while someone other than this window holds the feature: "Held by an AI session
  (pid N)", "Held by Cheat Engine", "Held by another program (pid N)", "Held by a program that has exited (pid N)"
  (typically this window's own holds from before a restart), "Held by a program that recorded no owner", or "Turning
  off: the AI session that held it has ended". See-through counts the claims that are neither this window's nor
  CE's. The chip's tooltip says the card's OFF or Reset takes the feature over and ends it, and to tick Block AI
  writes first to keep an AI session from turning it back on.
- **The card follows the holder:** the window reads `get_holds` every 2 s (DLL 3704 or later), quietly. A card
  someone else holds is read again on each poll, so its badge and readout show their value (the time lanes' readout,
  never the sliders), and once more after they let go. A read the poll could not make (a command running, a failed
  read) is made on the next poll. A debug-camera ON still queued shows Queued rather than the OFF a read would give.
- **Stealth card:** its Reset now ends a job someone else holds on its meter (it used to send nothing once it had
  seen no hold), and its badge and readout follow that job from the forced-field list alone: Holding @0, Armed @0
  (nothing live to hold yet), Unknown (another value), Off.
- **Block AI writes:** the window re-reads the switch file off the UI thread on every poll, and a click that lands
  during that read is no longer undone.
- **Debug camera ownership (DLL):** the UI's Console panel Run of `ToggleDebugCamera` / `EnableDebugCamera` /
  `DisableDebugCamera` and a CE record's invoke of them (INVOKE_BY_NAME, CMD_INVOKE) now record who toggled the camera,
  so an AI session's end no longer turns off a camera the user turned on that way, and the chip names the right
  holder. A CE invoke never waits for the camera's lock (busy, it runs unrecorded, as before). An AI's `write_invoke`
  of those functions keeps its "use write_debug_camera" refusal, and `write_invoke` names the holder of a hold
  refusal instead of reporting "code -999". docs/pipe-protocol.md says which invokes name whom.
- **`[MCP-GRANT-USES-RELEASES]` fixed:** `read_grants`' uses count counts only what a grant let through, not an OFF
  or a reset that needed none.
- **Reviews:** three fresh-context review rounds over the change, each finding checked by a refuter (round 1: 2 MED,
  8 LOW; round 2: 8 LOW; round 3: 2 LOW, 1 refuted); every confirmed one fixed red then green or covered by a new test.
- **Proven live** on the AOT exe and DLL 3716:
  - `tools/verify/holder_chips_live.py` (DumperTest Shipping, the window connected): a background client's god mode,
    world time 0.5 and walk speed x1.5 read "Held by an AI session (pid 30864)", with the badges ON, the readout
    "0.5x (held; natural 1x)" and the world slider left at 100%; a foreground client's jump x1.2 read "Held by another
    program (pid 34140)"; the card's Reset took the jump over (chip gone, 700 cm/s); when the background client left,
    every chip went and the badges read OFF. PASS.
  - `tools/verify/dbgcam_ledger_live.py` 13/13 (DumperTest58 Development, UE 5.8): the AI's invoke refused by name;
    the window's invokes name the window on and off and take the camera over from the AI, whose end then left it on;
    the mailbox's INVOKE_BY_NAME names CE. No pre-3716 DLL was at hand for a live negative control; each fix had its
    red step in a unit test or a source pin.
- **Tests:** C# 8274 passed, 1 skipped; headless 71/71; C++ dll_helpers_test 4104, dll_core_test 1574 checks,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30; 39 gates.
- AOT UI 62,557,696 bytes (sha256 afbf6c4c086ff538...); UE5Dumper.dll 3,779,584 bytes (sha256 0c0a7cdf62d3ad10...).

## 2026-11-16 (build 3715) — MCP write grants, Phase 1 proven live; an AI's OFF of another's hold names the holder `[MCP-WRITE-GRANTS-2026-11-14]`

The live pass of build 3714's Phase 1, with the maintainer at the prompts (verification-register.md, "MCP write grants,
Phase 1": all five closed).
- **What changes for a user of an AI client:** when an AI tries to turn off or reset something the UI, a Cheat Engine
  record or another session holds, the refusal says who holds it again ("... is held by the UE5DumpUI window or
  another program (pid N)"). In 3714 the server refused it before the DLL could, but without naming the holder, and
  asked for a grant the DLL would have refused past anyway.
- **Proven live, in Claude Code 2.1.325 (the maintainer's default mode is auto):**
  - the grant prompt shows the tool's name and the whole `scope_ack` sentence, Deny / Allow once; a covered write
    before its grant is refused naming it and reaches nothing; after Allow the covered writes run without prompts,
    while `write_memory` still asks every call;
  - the server logs `initialize from claude-code 2.1.325 ... writes: Grants` (Phase 0's M7);
  - Block AI writes, ticked in the window, revokes the live grant at the AI's next call, refuses new changes and lets
    the AI's OFF of its own hold through; unticking does not bring the grant back;
  - the `--mcp-agent-guard` hook denies a subagent's and a workflow agent's write before it runs (a workflow agent's
    hook input carries `agent_id` too, which the hooks reference does not say);
  - See-through's own-claim OFF ends only the AI's claim (DLL `own claim released (1)` beside the UI's claim, `(2)`
    alone).
- **Rigs on the AOT exe and DLL 3715** (DumperTest Shipping): holdsmore 78/78, writes 39/39, raw 28/28, invokeparams
  26/26, olddll 37/37.
- **Found and left open:** `[MCP-GRANT-USES-RELEASES]` (LOW): `read_grants`' uses count counts an OFF or a reset that
  needed no grant.
- **Tests:** C# 8182 passed, 1 skipped; headless 71/71; C++ self-tests passed.
- AOT UI 62,439,424 bytes (sha256 06c2e4c4ec525591...); UE5Dumper.dll 3,777,536 bytes (sha256 c286ca5b75167b34...).

## 2026-11-16 (build 3714) — MCP write grants, Phase 1: one grant per game run instead of a prompt per write, and a switch to stop every AI session `[MCP-WRITE-GRANTS-2026-11-14]`

Claude Code's prompt for a write tool offers only "Deny" and "Allow once", so every repeat of a write asked again. The
maintainer took every recommendation of docs/mcp-write-approval-eval.md §8 for Phase 1; Phase 0 had measured the prompt
(it shows the tool's name and its arguments in full, nothing else) and that subagents share the session's server.
- **What changes for a user of an AI client** (the UI and Cheat Engine change only by the new switch):
  - Claude Code 2.1.246 or later asks once per game run, through two grant tools whose names say what they unlock:
    `grant_god_mode_camera_cursor_time_see_through_foreground` (god mode, the debug camera, the mouse cursor, time
    dilation, See-through, Keep Foreground) and `grant_movement_fly_teleport_may_kill_or_lose_pawn` (the movement
    knobs, gravity direction, Fly, teleport). The prompt shows a fixed sentence of what the grant covers and how long.
    A grant ends with the game run, the AI session or its server's reconnect, or `revoke_grants`; `read_grants` lists
    them. A dropped pipe connection to the same game run keeps them.
  - `write_memory`, `write_invoke` and `write_force_field` still ask before every call.
  - Turning off or resetting what the AI itself holds needs no grant. Turning off what it did not turn on needs the
    grant: god mode's OFF writes the normal value to every protection bool, whoever set them.
  - Every other client, Claude Desktop included, only reads unless its own registration adds `--writes=any-host`. An
    older or unparseable Claude Code asks before every write, as before. `--writes=off` turns writes off; any other
    argument stops the server with exit code 1.
  - A value outside the UI's own range is refused, never clamped by the DLL: time dilation up to 3 (world) and 10
    (pawn), walk speed and gravity 0.1 to 10 times, jump 10% to 30 times the height, Fly 50 to 20000, See-through 1 to
    10 (the tool said 16, the DLL clamps at 10). A zero gravity vector points to the reset.
  - **Block AI writes**, at the top of the Teleport tab: every AI session is refused every change but an OFF or a
    reset of its own holds, and every grant ends. Per Windows user (`ai-writes.json`); a file that cannot be read
    blocks too.
  - `UE5DumpUI.exe --mcp-agent-guard` is a Claude Code PreToolUse hook that refuses a subagent's or a workflow agent's
    write or grant; docs/mcp.md gives the settings entry.
  - An AI's See-through OFF ends only its own claim: `seethrough_set` takes `own_claim` (DLL change), and See-through
    stays on while the user or a Cheat Engine record has it on. `seethrough_get_state` says `you`.
- **How:** `McpGrants` decides the client once, at the first initialize, classes every DLL command, and keys grants on
  the game run (pid and process start time). `McpGameLink` checks every send under its lock: a write admitted for
  another run, another kind, a revoked grant, or an OFF of what this server does not hold (read from get_holds right
  before) is refused and nothing is sent.
- **Review:** a fresh-context review of the change found two MED and two LOW; all four were fixed, red then green,
  before this build (an OFF of what the server does not hold was passed as a release; See-through's OFF ended
  everyone's claims; a release did not need `if_free`; the time-dilation refusal named 0).
- **Tests:** C# 8180 passed, 1 skipped (McpGrantsTests and AiWriteBlockGateTests are new); headless 71/71; C++
  dll_helpers_test 4101 and dll_core_test 1574 checks. `mcp_client.py probe` 29/29 on a local build: the host decision
  through real server processes.
- **Live: pending.** The grant prompts in Claude Code, the guard hook in a real session, the switch against a running
  server, and the write rigs on DumperTest are open in docs/verification-register.md.
- AOT UI 62,438,912 bytes (sha256 040ba069aec56233...); UE5Dumper.dll 3,777,536 bytes (sha256 828bd085db3d5800...).

## 2026-11-16 (build 3713) — The AI's calls: three gaps closed before any grant ships `[INVOKE-DENY-INDIRECTION]` `[WRITEMEM-OWN-IMAGE]` `[MCP-INSTR-CLAIMS]`

The maintainer chose these three from the MCP write-approval plan (docs/mcp-write-approval-eval.md) because they need
no measurement and must be closed before a per-function grant could let an AI repeat a call unasked.
- **What changes for a user of an AI client** (the UI and Cheat Engine are not affected):
  - `write_invoke` also refuses the calls that reach a refused effect by name: timers by function name or event
    (`K2_SetTimer*`), property setters by name (`Set*PropertyByName`, `SetCollisionProfileNameProperty`) and a
    Blueprint's event-graph entry (`ExecuteUbergraph_*`); and `RemovePlayer`, whose `bDestroyPawn` destroys the pawn;
    and the achievement, leaderboard and in-app purchase calls, which leave the machine without being network RPCs.
    Every name was checked against Unreal Engine's source.
  - `write_memory` refuses a range inside UE5Dumper's own image (code -25): its globals include the Cheat Engine
    mailbox, and a write there would have handed an invoke to the CE-owned poller, past the deny list.
  - The server's instructions no longer tell the model that "the user is asked before each one" (only Claude Code
    asks; another host decides for itself) or that "everything this server turns on is released" (Fly's speed, preset
    and noclip stay, and so does what a `write_invoke` or `write_memory` did). docs/mcp.md says the same.
- **Tests:** red, then green, for each. dll_helpers_test +19 deny-list checks with controls (reading a timer, an
  ordinary setter, a name merely containing a family stay allowed) and the image-overlap edges; dll_core_test checks the
  own-image test on its exe's global, code and a heap block; `McpServerTests.Instructions_DoNotOverstateTheApprovalOrTheRelease`.
- **Publish:** C++ 287 + 4098 + 22 + 30 + 1574 checks; C# 7937 passed, 1 skipped; headless 71/71.
- **Live, 3713,** DumperTest Shipping: `mcp_client.py raw` 28/28, its two new checks included -- `write_invoke
  K2_SetTimer` refused as a call by name, `write_memory` into the DLL's own `g_invokeMailbox` answered -25.
- AOT UI 62,289,920 bytes (sha256 81c45bdae2913830...); UE5Dumper.dll 3,774,976 bytes (sha256 bd6c75610c353373...).

## 2026-11-15 (build 3712) — C++ hardening instead of a Rust rewrite: unhandled status results, a data race in the property offsets, AddressSanitizer on the self-tests `[CPP-HARDENING-2026-11-14]`

The maintainer decided against rewriting any part of the DLL in Rust (docs/rust-rewrite-eval.md: about 2-4% of the
recorded DLL defects would have been prevented) and asked for the cheap C++ hardening instead. Builds 3710 and 3711
were configure runs (a VS update moved the compiler to 14.52.37025, and the new ASan tree); 3712 is the release.

- **What changes for a user:** almost nothing visible, by design. Where something used to be reported that did not
  happen, it is now said:
  - Resetting a movement knob, the gravity direction or time dilation logs a warning when the restore write fails.
    The reset still ends the hold and answers OK, as before; the reply's current value already shows the field.
  - A debug-camera OFF whose view write fails now changes nothing and answers "still on". It used to clear the
    active flag alone, so the state read "off" while the view stayed the debug camera's.
  - The teleport's settle steps, the rotation fallback and the cursor's input-mode calls log what the game thread
    really answered (`-5` = still queued) instead of "invoked".
  - Diagnostic hex dumps say UNREADABLE when the read failed, instead of showing zeros as data.
- **`[[nodiscard]]`:** the DLL's write, read-into-buffer, invoke and file-write status returns are marked, and
  `/we4834 /we4858 /we5240` make a discarded one a build error on the DLL, all four proxies and the test targets that
  compile DLL sources. The compiler found 21 discarding sites; 19 now act on the result, 2 are `std::ignore` with the
  reason beside them. `tools/verify/nodiscard_probe.py` proves the flags and the behaviour, and reverting one fix
  failed the build with error C4834.
- **The property-family data race:** the five offsets of the `sizeof(FProperty)` family are corrected lazily while
  scan workers and other pipe connections read them. They are now relaxed atomics (`DynOff::RuntimeOffset`), and
  every writer holds one lock, taken through a `FamilyWriteLock` that the writers demand as a parameter, so a write
  without the lock does not compile. Genau's writers hold it too now.
- **AddressSanitizer:** `build.ps1 -Target AsanConfigure` configures `build_asan\` (the self-tests only; CMake refuses
  it in the shipping tree), and `py tools/verify/build_dll.py --asan --run` builds and judges all five. Macht's probes
  read the game's memory past objects and across sections on purpose; in the ASan build only, their foreign side goes
  through an unchecked copy while this DLL's side is still checked. Before that, dll_core_test gave 1,021 reports, all
  from the probes; after it, 0. Every run first proves ASan live with a deliberate overflow.
- **Found on the way** (todo.md rows):
  - `[VND584-03-FLOOR-HIGH-HEAP]`: the relaxed GObjects rows' 0x1000 floor tells a count from half a pointer only
    below 16 TB; the ASan build's heap sat higher and reproduced the misread.
  - `[DYNOFF-LATCH-STALE]` and `[PIPE-WALK-DURING-INIT]`: two logic gaps the race analysis found, which atomics do
    not fix.
  - `build_dll.py` printed only a 4000-character tail, which hid 2 of the 21 warnings; it now lists every diagnostic.
- **Review:** an adversarial review of the whole change (three lenses, each finding re-checked) confirmed six
  defects, all fixed; it found no deadlock and sound memory ordering.
- **Publish:** C++ 287 + 4070 + 22 + 30 + 1570 checks; C# 7936 passed, 1 skipped; headless 71/71. Under ASan:
  287 + 4070 + 22 + 30 + 1569 (one check SKIPs where its premise fails), 0 reports.
- AOT UI 62,289,408 bytes (sha256 a431eab8687bb325...); UE5Dumper.dll 3,773,440 bytes (sha256 92d51666e5067dd7...).

## 2026-11-12 (build 3709) — A teardown no longer waits out a frozen game; the UI names a game running as administrator `[SHUTDOWN-SEETHRU-WAIT]` `[UI-ELEVATED-ACCESS-DENIED]`

- **What was wrong (1), `[SHUTDOWN-SEETHRU-WAIT]`.** With See-through on and the game thread stalled, a CE untick could
  block for the whole invoke timeout before the pipe server even began to stop. Measured on 3707: 11.3 s of a 15 s
  timeout; `set_invoke_timeout` allows 600 s, and CE gives up after 5 s. A worker parked in a queued game-thread call
  can only be answered by `Stark::Shutdown`, and the teardown reaches that after joining the worker.
- **What changes (1):**
  - A queued call's wait now ends when a shutdown is under way and the game thread has been quiet for over 1 s. The
    call answers -5 and stays queued, the same as a timeout. A live game is untouched: its calls drain within a frame,
    and UE5_Shutdown's See-through un-hides still run.
  - A cut makes a call that did not run more likely, so See-through keeps its record of hidden actors through one.
    - A trace that did not run no longer reads as "nothing occludes".
    - An un-hide that did not run keeps its actor; one that faulted inside the engine is dropped, as before.
    - An actor whose un-hide may still run is hidden again if it still occludes.
    - A deferred restore that a shutdown ends or refuses says so.
  - Fly/Noclip keeps its collision record when a restore is cut during a shutdown.
- **What was wrong (2), `[UI-ELEVATED-ACCESS-DENIED]`.** A UI that is not elevated, connecting to a game that runs as
  administrator, showed only Windows' "Access to the path is denied.".
- **What changes (2):**
  - It now says "PID N runs as administrator (elevated) or under another Windows account, so Windows refused this UI
    its pipe. Close UE5DumpUI and start it as administrator, or start the game normally, then connect again.", with
    the cause inside the toolbar's first 30 characters.
  - An elevated UI that is refused is told another process probably made that pipe name. So is the MCP server, which
    now asks its own elevation instead of assuming it is not elevated.
  - Proxy Deploy's post-inject connect stops on a refusal of the game it injected, instead of spinning 45 s and
    blaming the DLL.
  - The Inject tooltip and the shipped guides no longer promise a UAC inject that auto-connects.
- **Tests:** red then green, twice each (the second round from an adversarial review: 11 findings confirmed, all
  fixed).
  - C++: `Test_Stark_CutInvokeWait`, `Test_Stark_WaitQueuedInvoke` (a fake clock), `Test_Schlacht_FailedDispatchKeepsTheRecord`
    and the Dunste commit-rule cases.
  - C#: source pins on `EnqueueInvoke`, Schlacht's Tick and every restore site, the shutdown flag's order and Dunste's
    commit sites.
  - C#: the connect tests (`PipePerProcessConnectTests`, one through a real pipe with a read-only DACL), the MCP
    mirror tests and the post-inject retry tests.
- **Publish:** C++ 287 + 4070 + 22 + 30 + 1566 checks; C# 7936 passed, 1 skipped; headless 71/71.
- **Live, 3709:**
  - **See-through, DumperTest Shipping, CE 7.7, timeout 15 s, the game thread frozen** (`stop_teardown_live.py`, now
    with a verdict):
    - The CE untick went from Cleaning up to Stop entry in **25 ms** (was 11.3 s).
    - A bare Stop's drain was **satisfied in 11 ms** (was a 5 s TIMEOUT).
    - The hidden actor's record survived the shutdown, and the next ON restored it.
  - **Elevated:** a stub pipe with an elevated game's DACL (no UAC), and the PathShape game started as administrator
    (the maintainer clicked UAC). The non-elevated UI named both. A non-elevated process cannot read an elevated
    game's main module (Win32 5), so the inject picker cannot list it.
- AOT UI 62,288,896 bytes (sha256 b87b3ca67a8d24c9...); UE5Dumper.dll 3,729,408 bytes (sha256 f07ce0333f5144e6...).

## 2026-11-12 (build 3708) — Cheat Engine's "start-up failed" message names both causes `[CT-INITFAILED-HEADLINE]`

- **What was wrong.** When the DLL's start-up fails (mailbox state INIT_FAILED), the .CT's inject record and the
  records the UI generates showed "The DLL loaded and initialised, but the Pipe Server failed to start." INIT_FAILED
  covers two cases, though. The pipe server did not start, or the scan was cut short (the record was unticked while
  it ran), in which case the pipe server usually DID start. Provoked live on 3707, the message said the server failed
  while the DLL's log said `pipe server started, init ABORTED`, and the message's own causes sentence pointed at the
  aborted scan.
- **What changes:**
  - The message now opens with "The DLL loaded, but its start-up did not finish: the scan was cut short, or the Pipe
    Server did not start.", then keeps the causes sentence.
  - The same change covers the .CT (its message and its log line) and the generated inject and autorun records.
  - `CeReadinessLua.PipeFailedMessage` is renamed `InitFailedMessage`, and the INIT_FAILED docs in `Mimic.h` and
    `CeMailboxLayout.cs` say the same. Comment-only, so the mailbox contract is unchanged (v9).
- **Tests:** `CeInitFailedMessageTests`, red then green. All four copies, the shared text, both generated records and
  the shipped .CT, must name both outcomes and never claim init finished. Only the INIT_FAILED branch's string literals
  are read. `Reports_failures_rather_than_returning_success` now finds the message by its new wording.
- **Publish:** C++ 287 + 4038 + 22 + 30 + 1566 checks; C# 7919 passed, 1 skipped; headless 71/71.
- **Live, DumperTest Shipping, 3708** (`tools/verify/initfailed_ct.py`, which now requires both the headline and the
  causes sentence): a CE worker thread fired `UE5_Shutdown` 406 ms after initState went RUNNING. The CT logged and
  showed the new headline, and the DLL's log read `pipe server started, init ABORTED -> initState=3`. The generated
  records were not provoked live: their INIT_FAILED branch is pinned by the test, and they compile on CE's Lua 5.3.
- **Reviewed:** two reviewers on the diff; 2 findings confirmed (the derived test-file count, the register wording),
  both fixed.
- AOT UI 62,280,704 bytes (sha256 b6093d1043716524...); UE5Dumper.dll 3,724,800 bytes (sha256 690039faa94ffe30...).

## 2026-11-12 (no build change) — The rest of the pipe-per-process pass: an elevated game, two games at once, an hour of MCP, CE-staged races `[PIPE-PER-PROCESS-2026-11-06]`

- **What.** The items the morning's pass left, all against build 3707. verification-register.md
  `[PIPE-PER-PROCESS-2026-11-06]` has each one's numbers and log lines. Open there now: only 12 and 13.
- **Closed:**
  - 4, the elevated half. An elevated UI connects to an elevated proxied game and scans it. The MCP server names the
    elevation, but a non-elevated UI shows only "Access to the path is denied.": todo.md `[UI-ELEVATED-ACCESS-DENIED]`.
  - 5, the DLL's own INIT_FAILED. During the CT's own inject, a CE worker thread fired `UE5_Shutdown` 406 ms after
    initState went RUNNING. The scan aborted (initState 1 → 0 → 3), and the CT showed its INIT_FAILED message. That
    message's headline is wrong for this case (it says the server failed; init aborted and the server started): todo.md
    `[CT-INITFAILED-HEADLINE]`.
  - 6, a name an older copy made. A CE int3 breakpoint held the 3707 copy at its own `CreateNamedPipeW` while a 3695
    copy created the name. 3707 joined it and logged "takes 3 clients at once, not this build's 6" and "all 3 pipe
    instances in use".
  - 7, the UI's "pipe is gone". Killing a game after the game list was read gave "PID 3292's pipe is gone ..." in 5.0 s.
  - 11, the hint-cache lock across processes. With two games persisting at once, 0 of 50 rounds lost an update on
    3707. The negative control on 3695 lost 50 of 50.
  - 14, an hour of one MCP session. 0 failed calls, one connection, p95 15 ms. Its memory rose in a straight line to
    43.8 MB without a collection; runs at 1 s showed it levels off at about 62 MB once the GC collects.
- **Not reachable here:** 13 needs another CPU shape. The thread pick reads the machine's topology, so an affinity mask
  cannot stage one.
- **Still open:** 12, behind `[SHUTDOWN-SEETHRU-WAIT]`.
- **Rigs:**
  - New: `hintcache_two_games.py`, `oldname_join.py`, `initfailed_ct.py`.
  - `mcp_soak.py` gained `--heavy-every` / `--report-every` / `--replay`. It now judges memory by whether it levels
    off: the tail's growth per interval against the first report interval's, never a size.
- **Reviewed:** an adversarial review of these records and rigs confirmed 14 findings and refuted 2. All 14 are fixed
  (rigs, register wording, lessons, this entry), or filed (`[CT-INITFAILED-HEADLINE]`).
- **Lessons:** working-lessons.md 1.aq (memory that has not been collected yet), 3.wd (a race staged with a CE int3
  breakpoint), 3.we (a CE worker thread beside a CT tick).
- **Not changed:** the shipped UI and DLL.

## 2026-11-12 (no build change) — The pipe-per-process verification pass: Cheat Engine, the real UI, a proxy, a large game `[PIPE-PER-PROCESS-2026-11-06]`

- **What.** The live checks that waited for a free Cheat Engine, and the rest that this PC can run, all against build
  3707. verification-register.md `[PIPE-PER-PROCESS-2026-11-06]` has each one's numbers and log lines.
- **Closed:**
  - 1: a foreign holder of a game's pipe name.
  - 2: a restart beside a straggling connection.
  - 3: a second copy's own logs.
  - 8: a suspended game.
  - 9: CE's See-through record.
  - 10: the UI meeting a scan another client started.
  - 16: EVERSPACE 2, with 1,000,790 objects.
  - 17: the UI's Value Search beside the AI's.
  - 19, 20, 23, 24: CE's mailbox and the UI window as holders, every hold kind released, every write tool, a 3695 DLL
    refused.
  - 25: the UI's and CE's queued invokes.
  - 27: write_invoke's struct, object and deny-list paths.
  - The 3707 owed release: on UE 5.8 and around a CE Disable/Enable.
- **Reached in part:**
  - 4: the proxy-loaded game; the elevated game not run.
  - 5: CE shows the per-process name and both INIT_FAILED texts, the latter display-only.
  - 12: the drain counts a See-through teardown, but only for a Stop with no UE5_Shutdown.
- **Found, open:** `[SHUTDOWN-SEETHRU-WAIT]` (todo.md, Stage D). A CE untick blocks on See-through's lock for the
  whole invoke timeout before Stop starts; measured 11.3 s at a 15 s timeout.
- **Not run:**
  - 6: needs a start race staged with python.exe remote threads, which the AV flags.
  - 7, 11: need two games at once.
  - 13: no other CPU shape here.
  - 14: a run of hours.
- **New rigs:** `straggler_restart.py`, `stop_teardown_live.py`, `scan_hold.py`, `pipe_hold.py`, `vs_beside_ui.py`,
  `mcp_large.py`, `mcp_soak.py`. Also `legacy_pipe_stub.py --pid/--instances/--answer`, and the `mcp_client.py`
  modes `dbgcamstop`, `holdsmore`, `olddll` and `invokeparams`.
- **Not changed:** the shipped UI and DLL.

## 2026-11-12 (build 3707) — An AI session's debug camera is turned off even when the game was frozen as it ended `[DBGCAM-RELEASE-STALLED]`

- **What was wrong.** Two cases left the camera on with no holder after the AI session ended:
  - The game thread was stalled (a long load, a debugger, a suspended thread) when the session ended. The release's
    OFF then waited the invoke timeout and stayed queued, and on stock UE 4.18-5.7 a queued OFF does not turn the
    camera off.
  - The AI left in the middle of a command. The release then ran with that connection's cancel still raised, and its
    camera read gave up ("unknown"), so it sent nothing.
- **What changes:**
  - A release that times out is now *owed*. The DLL's pipe monitor sends the OFF again once the queued one has run and
    the game thread is running. If the camera has become someone else's meanwhile (the UI's Force ON, a CE record, an
    ON on its way), the monitor leaves it alone.
  - The release's reads now answer to shutdown alone.
  - `get_holds` / `read_holds` mark an owed release with `release_owed: true`.
  - The DLL's pipe log records each step: owed, sent again, done or abandoned (with the reason).
  - A stop of the pipe server (a CE Disable) drops a release still owed instead of carrying it over, so a later start
    never turns off a camera the user turned on meanwhile.
- **Safety:**
  - The monitor only tries the camera's lock; it never waits for it.
  - It reads the camera only when that decides something.
  - It sends only when the hook's own fire stamp shows the game thread ran in the last 150 ms.
  - It brings a soft-removed hook back first, and never makes a direct call off the game thread.
- **Tests:**
  - `Test_Voll_OwedCameraRelease` (the decision, every branch) and the source pin
    `AStalledCameraRelease_IsOwed_AndSentAgainOnceTheQueuedOffHasRun`, each red then green in three rounds.
  - Three review passes: 15, 4 and 5 findings confirmed, none refuted, all fixed. Round 3 withdrew round 2's carrying
    of a release across a stop.
  - Publish: C++ 4038 + 1566 + 287 + 30 + 22 checks; C# 7915 passed, 1 skipped; headless 71/71.
- **Live, DumperTest Development (UE 5.4), 3707:**
  - `mcp_client.py dbgcamhold` 59/59: (H) a stalled session end, owed and then off; (I) the UI took the camera during
    the stall, so the release was abandoned; (J) the AI left mid-command, and its release was still owed and ended.
  - Negative control, the same rig on 3706: H, I and J failed, and J logged `released (code 0)` with the camera left on.
  - Also on Development: `dbgcam` 26/26, `raw` 24/24.
  - On Shipping: `raw` 25/25, `writes --toggle-experimental` 36/36, `live` 31/31, `holds_live.py` 22/22, `probe` 15/15.
- **Not live:** a CE Disable/Enable during an owed release (CE testing pending), a soft-removed hook, and UE 5.8. See
  verification-register.md `[PIPE-PER-PROCESS-2026-11-06]` item 24.
- AOT UI 62,280,192 bytes (sha256 f2c1704ad7591d03...); UE5Dumper.dll 3,724,800 bytes (sha256 6179c00bec22b7d2...).

## 2026-11-11 (no build change) — The AI holding the debug camera, proven on DumperTest Development; a stalled session end leaves it on `[PIPE-PER-PROCESS-2026-11-06]`

- **What.** `tools/verify/mcp_client.py dbgcamhold`, a rig mode for the debug-camera part of verification-register
  item 24. The AI's ON was held by the server and released when its session ended. Its own OFF worked. A camera the
  UI took over, or turned on itself, refused the AI's OFF and ON and outlived the AI session.
- **Measured** on DumperTest Development (UE 5.4), DLL and AOT exe 3706: 32/32. The DLL log shows the release (code 0)
  and the three refusals. `dbgcam` (item 26) re-ran on the revised rig: 26/26. `raw` ran 24/24 on Development, its
  Shipping-only check skipped and saying why, and 25/25 on Shipping, where that check still fires.
- **Found, open:** `[DBGCAM-RELEASE-STALLED]` (todo.md, Stage D). If the game thread is stalled when the AI session
  ends, the release's OFF stays queued (code -5), and on UE 4.18-5.7 a queued OFF does not turn the camera off. The
  camera stays on with no holder. The fix is the maintainer's call.
- **Rig fixes from two review passes** (13 findings confirmed, none refuted):
  - A suspend or resume that did not take now raises instead of passing silently, and a bad `--suspend-name` is refused
    before any case runs.
  - Case H reads the live invoke timeout and waits for `game_thread_stalled`. It prints the release's code from the
    DLL log, matched by pid and time.
  - Clean-up re-sends OFF until the camera reads off.
  - Every MCP session's framing is checked, not only the last.
  - `--ssh` is refused for the modes that freeze the game.
- **Not changed:** the shipped UI and DLL.

## 2026-11-11 (no build change) — Joining a pending debug-camera toggle, proven on DumperTest Development `[PIPE-PER-PROCESS-2026-11-06]`

- **What.** `tools/verify/mcp_client.py dbgcam`, a new rig mode for verification-register item 26, which Shipping
  could not reach (no live CheatManager). With the game thread frozen: the AI's ON queued and the UI's joined; the
  UI's ON queued and the AI's refused; the UI's ON sent while the AI's toggle was still dispatching.
- **Measured** on DumperTest Development (UE 5.4), DLL and AOT exe 3706: 26/26. The joined toggle survived the AI
  session's end and turned the camera on, held by the UI. The negative control (nobody joined) dropped the toggle, and
  the camera stayed off.
- **Found:** Frieren's "join while still dispatching" path is reached by no shipped client. The pipe and the CE
  mailbox both change the camera under the hold's lock, so a second request waits 3.5 s and then joins the queued
  toggle. Only a direct `UE5_SetDebugCamera` export call reaches that path. The register row says so.
- **Not changed:** the shipped UI and DLL.

## 2026-11-11 (build 3706) — The MCP server writes memory and calls game functions; `write_mem` writes data only; the game-thread queue knows who sent what `[PIPE-PER-PROCESS-2026-11-06]`

Stage D2-3, to the maintainer's decisions of 2026-11-11 (todo.md, "D2-3 decisions"). Two independent four- and
three-lens reviews (32 and 20 findings) and a mutation check (15 guards, each failing a test) ran before the build.

- **`write_memory`** (MCP): up to 4 KB of data at an address, only if it still holds the bytes the AI read (`expect`);
  returns the bytes before and after.
- **`write_invoke`** (MCP): a UFunction on a live object, on the game thread, its parameters by name. The server reads
  the signature (inherited and interface functions included) and builds the call by the Invoke dialog's rules, stricter
  where the dialog trusts a person reading the box: a function taking an FText (anywhere, a struct's field included) is
  refused, a value that does not parse as its type is refused rather than written as zero, structs go by their fields
  (an engine struct by its known layout), and every object pointer must be a live object (GObjects decides, not a read
  of the memory). It returns the return value and out parameters; a call answered queued (-5) is never sent again.
- **`write_mem` writes data only, for every client** (the UI's field edits included): code, read-only, guard and
  uncommitted pages are refused, and no page's protection is changed any more. It used to make any page writable for
  the write, so it could patch code, briefly remove a code page's execute right, and restore the wrong protection
  after a write across pages.
- **The game-thread queue records who sent each call.** An AI call is refused, naming the program, while one from the
  UI, a Cheat Engine record or another AI session is still queued or running; the UI's calls always queue. An AI call
  still queued when its session ends is dropped and never runs. With the ProcessEvent hook down, an AI call is refused
  rather than made off the game thread. The same rules cover the game-thread calls the gameplay tools make (the debug
  camera's toggle). `get_holds` lists each client's outstanding calls.
- **Calls the AI may not make**, refused by the DLL: network RPCs, console commands (including SendToConsole and
  ServerExec), quitting or travelling to another level, destroying actors, writing or deleting save slots, opening a
  URL, and the debug camera toggle (use `write_debug_camera`).
- **Fixed in the D2-2 tools:** `write_debug_camera` reported its refusals as success (it answers in `state`); a queued
  debug-camera ON came on with no owner, so the AI could not turn it off and its session end did not release it (now
  the client that sent it holds it, and a UI or CE request that joins it takes it over); the MCP's 30 s timeout could
  end before a user-set invoke timeout and drop every hold (each game-thread command now waits per call).
- **The UI is unaffected** except that a field edit into read-only or code memory is now refused with its reason. The
  write tools need the DLL from this build; a proxy needs **Update All**.
- Tests: C# 7914 passed, 0 failed, 1 skipped (117 MCP tools tests, 58 source pins); headless 71; dll_helpers_test 4018,
  dll_core_test 1566 (write ranges on real pages), utf8_helpers_test 287, grausam_window_test 22, sein_retention_test
  30, 0 failures; 39 gates passed; the AOT window starts (version 1.0.0.3706). Live, the published DLL and AOT exe on
  DumperTest Shipping (UE 5.4): `tools/verify/mcp_client.py raw` 25/25 -- a field written, a changed-since write
  refused with what is there, the field put back; a vtable and code refused; `Add_IntInt(40, 2)` returned 42; the
  debug-camera toggle refused by name; with the game thread frozen, the UI stand-in's queued call refused the AI's
  (naming its process) until it ran; the AI's own queued call refused a second one and was dropped when its server
  exited ("1 queued invoke(s) of it dropped, never run"). `writes` 36/36, `live` 31/31, `holds_live.py` 22/22, `probe`
  15/15. Not live: verification-register.md `[PIPE-PER-PROCESS-2026-11-06]` items 25-27. AOT UI 62,280,192 bytes
  (sha256 56deffe008723f52...); UE5Dumper.dll 3,721,216 bytes (sha256 949afc9535fe2017...).

## 2026-11-11 (no build change) — `inject.py` hands the injection to `UE5DumpUI.exe`; python.exe no longer injects `[AV-INJECT-2026-11-11]`

- **Why.** The AV's behavioural layer flagged python.exe running `tools/verify/inject.py` from a terminal, 75 s after
  the injection had already worked, and an AV exception covers the program that acts, not the script it runs
  (working-lessons.md §3.8, item 6). The maintainer had excepted python.exe for the day; that exception can now go.
- **What changes.** `inject.py` starts `dist\UE5DumpUI.exe --inject-elevated <pid> <dll> <result file>` -- the UI's
  own injector, started plainly, never elevated -- and reads its result. Its options, the guard against a build of ours
  already in the process, `--list` / `--modules` and its output lines stay; one line is new (`via    : <exe>`), and
  `--exe` names another exe (a worktree has no `dist\`). No caller changes.
- **Not changed:** the shipped UI and DLL. `call_export.py` and the rigs built on `mailbox_poke.py` still start a
  remote thread or write process memory from python.exe themselves.
- Tests: `InjectElevatedContractTests` (5): a bad pid exits 2 and a missing DLL exits 1 with its reason, neither
  opening a process; the switch, argument order, `OK <hex>` line and exit codes pinned in both `Program.cs` and
  `inject.py`; `inject.py` names no injection API and never elevates. 39 gates passed. Live on DumperTest Shipping
  with the build 3705 `dist\`: a missing DLL and a missing exe refused; a non-PE file reported as the exe's own
  "LoadLibraryW returned NULL" (exit 1); the injection loaded `dist\UE5Dumper.dll` and its pipe answered (build 3705);
  a second injection refused by the guard; `--allow-stale` returned the same module; no result file left in %TEMP%.

## 2026-11-11 (build 3705) — The MCP server can change the game: the gameplay toggles, the force field and teleport, asked every time `[PIPE-PER-PROCESS-2026-11-06]`

Stage D2-2, to the maintainer's D2 decisions of 2026-11-11.

- **Twelve `write_*` tools**: god mode, the movement knobs, gravity direction, time dilation, the debug camera, the
  mouse cursor, a teleport to explicit coordinates, and the experimental Fly, Keep Foreground, See-through, force a
  field and release a forced field. **Five more reads** around them: `read_holds` (who holds what), `read_pose`,
  `read_movement`, `read_time_dilation`, `read_forced_fields`.
- **Asked every time.** Every write tool carries Claude Code's `anthropic/requiresUserInteraction` marker (Claude Code
  2.1.246+ asks before each call in every permission mode, an allow rule notwithstanding) and `destructiveHint`.
- **Never over you.** A change to a feature the UI, a Cheat Engine record or another AI session holds is refused by
  the DLL under that feature's lock (D2-1's `if_free`), and the reply names the holder. The AI never releases every
  forced field at once, and never turns See-through off while you have it on too.
- **Ends with the AI session.** What the AI turned on is released when its server exits; what you hold stays.
- **Your teleport markers and Recall last are yours.** `write_teleport` goes only to the coordinates given and never
  touches a marker slot. New pipe key `keep_undo` on the explicit-pose `teleport_recall_marker` (the server always
  sends it) leaves the undo point alone, so the UI's **Recall last** still returns where you were.
- **The experimental features follow the UI's switch**, read on every call.
- **The write tools need the DLL from this build** (3705): an older one cannot tell the AI's change from yours, so
  each write tool refuses it before sending anything. A proxy needs **Update All**.
- `docs/mcp.md` and `MCP-Setup.html` describe the write tools; `MCP-Setup.html` in `dist\` is byte-identical to its
  source.
- Tests: the write tools' rules (if_free on every hold change, the old-DLL refusal, the experimental switch, the
  holder named in each refusal, teleport's keep_undo and `from`, object_null refused, the See-through OFF guard, no
  command from the never-sent set), the `_meta` marker in `tools/list`, a source pin for `keep_undo` in Wirbel and
  Fern. Nine mutations, one per guard, each failed a test. C# 7845 passed, 0 failed, 1 skipped; dll_helpers_test
  3952, dll_core_test 1548, utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30, 0 failures; 39
  gates passed. ⚠ The publish's headless step failed, 49 of 71, all with "No precompiled XAML found for
  avares://UE5DumpUI/Resources/Strings/en.axaml"; the same suite re-run on its own passed 71/71, and the published
  exe starts its window (up after 12 s, crash.log untouched, version 1.0.0.3705 in its init log). The cause was not
  found; it followed a long run of mutation rebuilds of the test project.
- Live, the published DLL and AOT exe on DumperTest Shipping (UE 5.4): `tools/verify/mcp_client.py writes
  --toggle-experimental` 36/36 beside a foreground pipe client standing in for the UI -- the UI's god mode refused and
  named; the server's own world time, walk speed, fly and force-field job marked background; a teleport and a return
  leaving the marker slots and the undo point (a control jump without `keep_undo` moved it), and the UI's Recall last
  landing where the UI left; Fly refused with the switch off; See-through OFF refused while the UI also had it on;
  each of the server's holds released when it exited (one DLL log line each), the UI's god mode and See-through
  kept. `probe` 15/15, `live --suspend-name DumperTest` 31/31, `holds_live.py` 22/22. Not live:
  verification-register.md `[PIPE-PER-PROCESS-2026-11-06]` items 22-24. AOT UI 62,190,080 bytes (sha256
  ab442c67207ec532...); UE5Dumper.dll 3,685,376 bytes (sha256 b562d419e5adc02e...).

## 2026-11-11 (build 3704) — The DLL records who holds each gameplay hold; an AI session's holds end with it `[PIPE-PER-PROCESS-2026-11-06]`

Stage D2-1, the groundwork for the MCP server's write tools (D2), to the maintainer's decisions of 2026-11-11: the DLL
records owners, what an AI session turned on ends with it, and an AI never replaces or ends what the UI holds.

- **Who holds what.** God mode, the three movement knobs, gravity direction, both time lanes, fly, Keep Foreground,
  the debug camera and each force-field job record their owner: a pipe client's process, CE's mailbox, or nobody.
  Every writer -- each pipe connection and the mailbox -- changes a hold under that hold's own lock.
- **`if_free`.** A request carrying it is refused (`code -20`, naming the holder) when the hold is in force and
  someone else holds it -- one with no recorded owner counts as someone else's. Nothing changes then, and no other
  writer can slip between the check and the change.
- **A background client's holds end with it.** A client whose requests carry `"background"` (the MCP server) has
  what it still holds released when its process leaves; a hold the UI took over meanwhile stays. The UI's own holds
  outlive it, as before.
- **New command `get_holds`** (107 in all) lists every hold, whether it is in force and who holds it; `pipe_clients`
  marks background connections.
- No change in what the UI or Cheat Engine do. A proxy DLL needs **Update All** for this, as for any DLL change.
- Tests: dll_helpers_test 3952 (the ledger, the if_free rule, the holds each request changes, the holder JSON), 52
  source pins for Fern and Mimic, which no test target compiles. C# 7822 passed, 0 failed, 1 skipped; headless 71;
  dll_core_test 1548, utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30, 0 failures; 39 gates
  passed. Live, the published DLL on DumperTest Shipping (UE 5.4), `tools/verify/holds_live.py` 22/22: a background
  client refused the foreground client's god mode and could not reset it; its own world time, walk speed, jump, fly
  and Keep Foreground were released when it left (one DLL log line each), while a lane the foreground client had
  taken over stayed. Not live: verification-register.md `[PIPE-PER-PROCESS-2026-11-06]` items 19-20. AOT UI 62,125,056
  bytes (sha256 b64a095f469b9b5c...); UE5Dumper.dll 3,685,376 bytes (sha256 983e092b3b87d1c1...).

## 2026-11-11 (build 3703) — `MCP-Setup.html` ships in every release: how to set up the MCP server `[PIPE-PER-PROCESS-2026-11-06]`

- **New in the release folder: `MCP-Setup.html`** (the maintainer: the setup document ships with the publish). How to
  register `UE5DumpUI.exe --mcp` in Claude Code and Claude Desktop, let the reading tools run without a prompt, use
  it beside the UI and against a game running as administrator, and start it from another machine over SSH with key
  authentication; then a troubleshooting table. `README.html` lists it.
- **The Windows 11 OpenSSH server commands in it are the maintainer's, for reference only:** they may not work on a
  given machine (another SSH server, a policy, a firewall), and setting up an SSH server is outside this project.
- No change to the app or the DLL. Tests pin the copy step, the guide against the server's switch, name, allow rule
  and tools, and the disclaimer ahead of the commands. C# 7812 passed, 0 failed, 1 skipped; headless 71;
  dll_helpers_test 3929, dll_core_test 1548, utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30,
  0 failures; 39 gates passed; `dist\MCP-Setup.html` byte-identical to its source. AOT UI 62,125,056 bytes (sha256
  ddbcff42d219504c...); UE5Dumper.dll 3,631,104 bytes.

## 2026-11-11 (build 3702) — The MCP server says when a game runs as administrator; it works in Claude Code, over SSH and elevated `[PIPE-PER-PROCESS-2026-11-06]`

- **A game running as administrator is named as such.** A server that is not elevated sees the game listed but is
  refused its pipe by Windows (UAC); it answered "Internal error in read_status: Access to the path is denied." It now
  says the game runs elevated and what to do: start the AI client elevated, or the game normally. Red, then green.
- **The UI's project runs the trim and AOT analyzers on every build** (no change to the exe): IL2026 / IL3050 now fail
  the first compile and the test build instead of the publish. A planted generic `JsonArray.Add("x")` failed a plain
  build; taken out, the build was clean.
- **Measured live** (verification-register.md `[PIPE-PER-PROCESS-2026-11-06]`), build 3701's AOT exe on DumperTest
  Shipping (UE 5.4):
  - **Claude Code itself** (item 15, closed): registered with `claude mcp add --scope local`, from a copy of the exe
    outside `dist\` (a running server would stop the next publish replacing it). A desktop session loaded the
    server (handshake 2025-11-25), offered its 35 tools, and called six of them on the game. With the allow rule
    `mcp__ue5cedumper__read_*` and `--permission-mode default`, the reads ran without a prompt and `scan_start` was
    denied; in auto mode, `scan_start` ran too -- why D2's write tools must carry `requiresUserInteraction`.
  - **SSH and an elevated game** (item 18, closed): an elevated server against an elevated game 31/31; a server
    started over SSH (an administrator's SSH session runs at High integrity) 26/26 against the elevated game and 31/31
    against a normal one. The temporary SSH key was removed afterwards.
- New in todo.md: `[UI-CONNECT-ELEVATED-GAME]` (MED, not measured in the UI) -- the UI's inject-elevated flow
  auto-connects from a non-elevated UI and would meet the same refusal, shown as raw text.
- Tests: C# 7809 passed, 0 failed, 1 skipped; headless 71; dll_helpers_test 3929, dll_core_test 1548,
  utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30, 0 failures; 39 gates passed;
  `mcp_client.py probe` 15/15 on this build. AOT UI 62,125,056 bytes (sha256 df435701e4e9414b...); UE5Dumper.dll
  3,631,104 bytes.

## 2026-11-11 (build 3701) — `UE5DumpUI.exe --mcp`: an AI can read a running game, beside the UI and without disturbing it `[PIPE-PER-PROCESS-2026-11-06]`

Stage D1. The maintainer chose the route on 2026-11-11 (todo.md, Stage D): the server goes straight to the game's
pipe, it is a mode of the UI's exe, the JSON-RPC is written by hand, and the first build only reads.

- **New: an MCP server for AI clients** (Claude Code, Claude Desktop). Registered as
  `UE5DumpUI.exe --mcp`, it opens no window: the client starts it on stdin / stdout. Its tools read a running game
  through the DLL: search and find objects, an instance's live fields, class layouts and functions, raw memory, who
  points at an object and the pointer path from GWorld, Cheat Engine pointers, DataTables, and Cheat Engine-style
  value scans. Every reading tool's name starts with `read_`, so one Claude Code allow rule admits them all; the one
  other tool, `scan_start`, keeps its prompt. Nothing in this build writes game memory or turns a feature on. How
  to register it, the tools and the messages: [mcp.md](mcp.md).
- **It runs beside the UI without getting in its way** (the maintainer: an AI working beside the UI must not
  interfere with its long operations or the user's writes):
  - its own pipe connection (the UI keeps its two of the six), and the server and the UI are separate processes, so
    the single-instance rule does not apply between them;
  - no command whose state another client shares: no snapshot, LiveFuncs recording, diagnostics reset or overrides;
    `scan_start` starts nothing on a game that is scanned, being scanned, or whose scan found nothing;
  - **the DLL: a request may carry `"background": true`**, and the server marks every one. A GObjects walk such a
    request starts takes at most half of the free worker threads, so a value scan or snapshot the user starts in the
    UI meanwhile keeps the other half. It used to get one thread. A DLL older than the key ignores it.
- **Behaviour an AI can rely on:** a game counts as scanned only once its scan has finished (the pool is published
  before the offsets are validated); with several games, every call names its pid; a game that came back as a new
  process is named; a call that overruns its time or is cancelled closes the connection (the DLL's own cancel), and
  the next reply says the old value-scan sessions are gone; a reply too long for the client is cut by whole entries,
  with a note.
- **An adversarial review** (four lenses, each finding checked against the code) found 1 HIGH, 4 MED and about 12
  LOW, all fixed but two LOW left for the DLL (todo.md, Stage D). The HIGH: nine `JsonArray.Add` calls bound to the
  generic `Add<T>`, which the trimmed publish refuses -- working-lessons §3.xc's trap; the analyzer build found 18
  errors, then none.
- Tests: the MCP protocol, tools, connection and host (C#, four new files); the half share and its scope
  (dll_core_test, 16 checks: on this PC a background walk on 7 threads, a foreground walk beside it on 8). C# 7808
  passed, 0 failed, 1 skipped; headless 71; dll_helpers_test 3929, dll_core_test 1548, utf8_helpers_test 287,
  grausam_window_test 22, sein_retention_test 30, 0 failures; 39 gates passed. Live, AOT exe on DumperTest Shipping
  (UE 5.4), `tools/verify/mcp_client.py`:
  - `probe` 15/15 with no game (handshake, `server/discover` answered with an error, framing, exit 0 at EOF);
  - `live` 31/31: every tool; a cancel of a call the suspended game could not answer got no response, and the next
    call reconnected and said so; a value-scan session `pipe_client.py` held for the run survived it;
  - the DLL log: the server's walks on 7 of 14 threads, "a background walk takes at most half of what is free".

  Not live: verification-register.md `[PIPE-PER-PROCESS-2026-11-06]` items 15-18 (Claude Code itself, a large game,
  the UI itself beside the server, SSH and an elevated game). AOT UI 62,124,032 bytes (sha256 a48805d6facd67ea...);
  UE5Dumper.dll 3,631,104 bytes.

## 2026-11-11 (build 3700) — build.ps1 leaves nothing in %TEMP%; a green test run no longer ends red on a busy disk `[TEST-TEMP-HYGIENE-2026-11-11]`

- **A build no longer leaves the SDK's folders and logs in %TEMP%.** Every dotnet call build.ps1 makes (restore,
  publish, both test suites, clean) now runs with TMP/TEMP in a folder of that build's,
  `%TEMP%\UE5CEDumper.Tests\b<pid>\`, removed when the build ends. A build stopped part-way leaves it, and the next
  test run removes it. The C++ steps and the shell that started the build keep their own TEMP. Measured on this build:
  no workload log and no random-named folder of this repository's appeared in %TEMP% while it ran, and its private
  folder was gone at the end.
- **A test run whose tests all pass no longer fails at the very end when the disk is busy.** The test run's own folder
  was removed as the process exited; while another session held the SSD at 100% I/O that took longer than xunit allows
  once a run is over (about 11 s), and xunit then ended the run with "Foreground threads were left running" and exit
  code 1 -- build 3699's publish ended FAILED that way with every test passing (2 of 17 runs, both in the same ten
  minutes). The folder now goes before the run ends.
- No change to the app or the DLL. C# 7,692 passed, 0 failed, 1 skipped; headless 71; 39 gates. AOT UI 61,767,680
  bytes (sha A83A90F63BFAB7FB…), UE5Dumper.dll 3,625,984 bytes. Build 3699 was taken by the publish described above;
  nothing from it was handed over.

## 2026-11-11 (no build change) — This repository's `dotnet build` leftovers leave %TEMP% after a day; nobody else's do `[TEST-TEMP-HYGIENE-2026-11-11]`

- **Every `dotnet build` leaves an empty random-named folder, a `Microsoft.NET.Workload_*.log` and a second folder in
  %TEMP%,** and the SDK never removes them (1,452 folders and 726 logs in a day here). The test run now removes the
  ones a finished build of THIS repository left a day ago, when it starts.
- **Nothing of anyone else's goes.** A log counts as ours only when its command builds a project git tracks in this
  repository; its folders only when the group is exactly the one a test run saw while it was young (the unit tests and
  the headless suite record what they see, up to 30 minutes old), two folders per log, all ours. Another repository's
  build (the AOBMaker session builds and cleans up beside this one), a build still running, a log that recorded real
  work, `dotnet --info`'s folders, a folder written since, and a group that lost or gained a folder all stay. Logs another session's build left are
  remembered, so a run does not read them again. The memory lives in `%TEMP%\UE5CEDumper.Tests\`, which now stays.
- Measured on the real %TEMP% before anything was deleted, as if a day had passed: 152 folders and 76 logs chosen by
  the first design, and 80 folders and 40 logs by the final rule (after the other session's own cleanup), every log
  independently this repository's and none of the other session's; a group stayed whenever the other session built
  in the same second.
- Three adversarial reviews (the top findings each checked by a second agent; the third found nothing above LOW)
  found the first designs took too much: `dotnet build ui` in a sibling repository with the same layout, an option's
  value, a program argument, a vendored clone, folders whose own log another cleanup had removed, a stranger
  completing a group another cleanup had cut, and an option the parser did not know eating the next option's name.
  Each is now a test, and the rule changed as above. Also fixed: a folder written a minute ago, the memories not
  written on a first run or written over after a failed read, a verdict reused by a checkout with other projects, a
  junction at the shared folder, a run's exit racing another run's start, a file time past the year 9999 stopping
  every test, a killed run's read-only folder never removed, and the headless suite's own build never recorded.
  Mutation runs (each guard removed in turn, the test processes on a scratch TEMP; 15, then 28, then 9): every guard
  is caught by its own test, but one that a later rule made redundant.
- Left behind on purpose: a build no test run follows within 30 minutes, or one named by an unusual path, stays until
  someone else's cleanup takes it. The other session's own cleanup already removes SDK leftovers about an hour old.
- Tests: red, then green, for every item. C# 7692 passed, 0 failed, 1 skipped; headless 71; 39 gates. A test run
  starts 0.2-0.6 s later, mostly `git ls-files` for the project list (measured while another session kept the SSD at
  100%).

## 2026-11-11 (no build change) — A test run leaves nothing in %TEMP% `[TEST-TEMP-HYGIENE-2026-11-11]`

- **The C# test suite no longer fills %TEMP%.** One run left about 220 folders there (`UE5Dump*_<guid>`,
  `ue5cd-vmtest-<guid>`, six `ue5-*` folders), and nothing removed them: 2,743 from one day, about 120 MB. A test
  deleted its folder and ignored the failure, and the delete fails while SQLite's connection pool still holds the
  database open. Now each run writes its scratch in a folder of its own, `%TEMP%\UE5CEDumper.Tests\<pid>-<time>\`,
  which goes when the run ends; a run that was killed is cleaned up by the next one, and the first run cleans up what
  older builds left. No test had to change.
- The app itself was checked too: it writes one file in %TEMP% (the result of an elevated inject) and deletes it, and
  its own folders already age out (logs and snapshots after 21 days).
- Not ours, and left alone: every `dotnet build` (in any repo) leaves two empty folders with random names and a
  `Microsoft.NET.Workload_*.log` in %TEMP%.
- The cleanup never goes through a junction (a test makes one to another drive's root): measured, a plain recursive
  walk went through one and changed the file it pointed at, so the cleanup walks by hand and skips them.
- A run still gets its own folder when another run's exit removes the shared one at the same moment.
- Tests: red, then green, three times. C# 7588 passed, 0 failed, 1 skipped; 39 gates. After a full run nothing of
  ours is left in %TEMP%: the first measured run left it at 2,185 entries before and after, and later runs grew it
  only by other processes' `dotnet build` leftovers. A run folder left without its owner was removed by the first run
  10 minutes later.

## 2026-11-10 (build 3698) — What a client turns on is its own; scans, watches and settings are safe with several clients `[PIPE-PER-PROCESS-2026-11-06]`

Stage C: the pipe is hardened for a client that stays connected (an MCP client, a rig) beside the UI and CE.

- **Closing the UI no longer turns off See-through that Cheat Engine turned on.** What a client turns on now belongs
  to that client: See-through goes off when the last program holding it leaves or anyone turns it off, and a Live
  Funcs recording (with its trace, up to 512 MB) is freed when the program that started it leaves -- also when other
  clients stay connected, which used to leave both running for nobody. The UI's two connections count as one program.
- **Value Search and Group Scan sessions are private to the connection that began them.** Another client can no
  longer end or refine yours, and its scans no longer expire your idle session while you are still connected. A
  connection's sessions are dropped when it disconnects (at most 8 of each kind per connection).
- **Watches are per connection, and stopping one is instant.** A watch on an address another client also watches is
  its own. The watching thread only reads memory; the connection sends the latest reading with its next reply (and how
  many readings it stands for), so a client that sends nothing no longer stalls every stop of its watch -- a
  10-minute watch now stops in about 1 ms.
- **Start Scan and Extra Scan run once, whoever asks.** A second request while one runs is answered "in progress"
  instead of "failed"; the scan keeps running if the client that started it disconnects, and another client crashing
  no longer cancels it (only stopping the DLL does). A scan stopped that way is not reported as scanned, and an Extra
  Scan cut short says so instead of "not found".
- **See who holds the pipe.** New command `pipe_clients` (106 in all) lists each connected program, its last command
  and whether it is busy; when every connection is taken, the DLL's log line names them and the UI's Connect message
  and `pipe_client.py` quote it. No client is disconnected for idling (the maintainer's choice).
- **Heavy walks use the CPU's performance cores less 2** (this 16-core / 32-thread PC: 14 threads per walk, was 16;
  an 8-core 9800X3D: 6; Intel E-cores are not counted).
- **Settings saved per game cannot overwrite each other.** The per-game hint cache is changed under one lock across
  every injected game, and "saved" in the reply (and the UI's log) means the write happened.
- Tests: red, then green, for every item and for every review finding. An adversarial review (5 lenses, each finding
  checked by a second agent) confirmed 23 LOW findings and two verification rounds found 8 more, all fixed -- among them
  a teardown count that a throw could leave raised (every later Stop then waited 5 s), status replies read in two steps,
  and a watch thread that a failed insert could leave running. C# 7560 passed, 0 failed, 1 skipped; headless 71;
  dll_helpers_test 3929, dll_core_test 1532, utf8_helpers_test 287, grausam_window_test 22, sein_retention_test 30, 0
  failures; 39 gates passed. ⚠ In `build.ps1 -Mode Publish` the headless step failed 49 of 71 with "No precompiled XAML
  found" right after the stopped 3697 publish (its MSBuild servers were still up); run alone it passed 71/71, and the
  AOT UI loads its strings and connects. Live, AOT build on DumperTest Shipping (UE 5.4):
  - `stagec_live.py` 29/29: CE's See-through survives a pipe client leaving and goes with the last claim; another
    process's claim goes with it while a client stays; sessions and watches are their connection's; an unwatch of a
    10-minute watch took 1 ms; two simultaneous `trigger_scan` gave one start and one "in progress", one RunScan in the
    log; `persisted` matched the cache file;
  - `pipebusy_capacity.py` PASS: 6 clients, the at-capacity line listed all 6 holders (phase `Reading`), `pipe_clients`
    listed 6 with one marked self;
  - `worker_budget_live.py` PASS: 14 threads per walk (13 workers), 32 walks by 4 clients, +14 threads, 0 errors;
  - the UI connected; `pipe_clients` showed its two connections under one PID.

  Build 3697 was taken by a publish stopped part-way to take the last review fixes; nothing from it was handed over.
  Not live: verification-register.md `[PIPE-PER-PROCESS-2026-11-06]`, items 9-14. AOT UI 61,767,680 bytes;
  UE5Dumper.dll 3,625,984 bytes.

## 2026-11-10 (build 3696) — The DLL takes 6 pipe clients, and heavy commands share one thread budget `[PIPE-PER-PROCESS-2026-11-06]`

- **6 clients at once instead of 3.** The UI uses 2, so a rig, an MCP client and a reconnect now fit beside it. The
  old rule "never run `pipe_client.py` while the UI is connected" is retired; it still holds for a game whose DLL
  predates this build (3 clients) until its proxy is refreshed and the game restarted. The DLL's log states the limit
  in force ("takes 6 clients at once").
- **Heavy commands running at once share one worker budget.** Each full object walk (value scan, find references,
  property xrefs, snapshots) used to start up to 15 threads on this PC, with no limit across walks: three at once put
  48 threads in the game. Now the walks running at the same moment share what one walk alone may use; a walk that finds
  it taken runs on its own thread, and the log says so. Measured on DumperTest, 480 walks from 3 clients at once:
  - this build: +16 threads at peak, 1.4 s;
  - build 3695: +48 threads, 8.7 s — the extra threads made the whole load six times slower.
- **A full pipe is named as such.** When every connection is taken, the UI says so (and that the game may instead not
  be responding) instead of "The operation has timed out". When the game exited after the list was read, it says the
  pipe is gone. `pipe_client.py` says the same; Python's `open()` reports a full pipe as "Invalid argument".
- **Rigs:** `pipebusy_capacity.py` reads the limit from the DLL's log and measures it (the 7th open finds every
  connection taken). The new `worker_budget_live.py` counts the game's threads while several walks run.
- Tests: red, then green, for the budget (dll_core_test: grants only what is free, a second walk runs on its own
  thread while the first holds the budget), the 6-client source pin, the UI's full-pipe and gone-pipe messages, and
  `pipe_client --selftest`. C# 7515 passed, 0 failed, 1 skipped; headless 71; dll_helpers_test 3856, dll_core_test
  1503, grausam_window_test 22, sein_retention_test 30, 0 failures; 39 gates passed. A review workflow found the UI
  reading any timeout as a full pipe, and wording that named an impossible "second copy of this UI"; both fixed, red
  then green. Live, AOT UI on DumperTest Shipping (UE 5.4):
  - the DLL logged "takes 6 clients at once"; `pipebusy_capacity.py` PASS: 6 connected, the 7th found every
    connection taken, one at-capacity line and one recovery line;
  - with the UI connected, `pipe_client.py` and the CE mailbox (10/10) worked beside it;
  - with every connection held, a 7th `pipe_client` and the UI's Connect both said every connection is in use; after
    release, Connect connected;
  - `worker_budget_live.py`: 3 and 4 clients both +16 threads (bound 18 / 19), 438-453 of 480 walks cut by the
    budget; the build 3695 control +48 threads.

  Not live: verification-register.md `[PIPE-PER-PROCESS-2026-11-06]`, items 6-8. AOT UI 61,739,008 bytes;
  UE5Dumper.dll 3,581,440 bytes.

## 2026-11-10 (build 3695) — Each game has its own pipe, and Connect names the game `[PIPE-PER-PROCESS-2026-11-06]`

- **The DLL serves `\\.\pipe\UE5DumpBfx_<pid>`, one pipe per game.** Every game used to serve the same
  `\\.\pipe\UE5DumpBfx`, and Windows hands a client the oldest listening one, so with two games running the UI, or
  even one of its two lanes, could reach the wrong game. The old name is no longer served: a protocol break by
  decision.
- **Connect in the UI:**
  - one game: connects at once, as before;
  - several games: a list showing each game's EXE name, its window title and its PID;
  - a game running an **old** UE5Dumper DLL (usually a proxy never updated): shown in the list with a warning, and it
    cannot be connected to. When it is the only one, there is no list: a message says the game runs an old DLL and
    how to fix it (close it, Update All on the Proxy Deploy tab, start it again);
  - **Inject into running game** connects to that game by its PID. Connecting to another game while connected
    switches to it.
- **The red "more than one process has the dumper loaded" banner is gone.** It warned that the shared name made the
  game you reached first-come-first-served, which is no longer true. After connecting, the UI checks that the game
  that answered is the one it asked for.
- **Python rigs:** `pipe_client.py` takes `--pid` (or `UE5DUMP_PID`); with several games it uses the lowest PID,
  lists them all and warns; with only an old DLL's pipe it refuses and says why. `tools/verify/legacy_pipe_stub.py`
  puts up the old name for testing.
- **A second copy of the DLL in one game** (a stale proxy beside a fresh injection) no longer starts a second server
  on the same name when every pipe slot is busy, and while it waits it no longer connects to the first copy twice a
  second.
- **Cheat Engine:** no change in behaviour. Its messages name the new pipe, and the "pipe server failed" message now
  gives the real causes (the record unticked during the scan, or ticked again too soon).
- Tests: red, then green, for the DLL (Voll's name, probe, first instance, watcher), the UI (the connect plan, the
  list, the refusal, switching games) and `pipe_client --selftest`. C# 7513 passed, 0 failed, 1 skipped
  (`DUMPDIFF_REAL_DIR` unset); headless 71; dll_helpers_test 3856, dll_core_test 1487, grausam_window_test 22,
  sein_retention_test 30, 0 failures; 39 gates passed. A review workflow (three reviewers, a skeptic per finding)
  found five defects in the first version, each fixed red then green. Live, AOT UI on DumperTest Shipping (UE 5.4)
  and DumperTest58 Shipping (UE 5.8):
  - Inject into running game connected by PID (`UE5DumpBfx_50208`, both lanes) after 2 s; Connect with one game
    connected without a list; with no game, "No game is running UE5Dumper";
  - with the old name up (`legacy_pipe_stub.py`): beside a game, the list showed the warning row, which disabled
    Connect; alone, the message and no list; neither the UI nor `pipe_client` ever opened it;
  - two games: the list showed both with EXE names and window titles, and each choice connected to that game
    (UE504 24,530 objects / UE508 34,570); `pipe_client` with no PID warned and used the lowest, `--pid` and
    `UE5DUMP_PID` chose;
  - CE mailbox (`mailbox_poke.py`) 10/10;
  - a second copy injected while every slot was busy: 0 connections to the first copy in 15 s, against 22 in 10 s
    from the pre-fix copy.

  Not live: verification-register.md `[PIPE-PER-PROCESS-2026-11-06]`. AOT UI 61,735,424 bytes; UE5Dumper.dll
  3,579,904 bytes.

## 2026-11-06 (no build change) — Two old explanations of the pipe's 3-client limit were wrong `[PIPE-PER-PROCESS-2026-11-06]`

- **`[PIPEBUSY-2026-08-18]` is not why the DLL's pipe takes at most 3 clients.** That tag recorded a logging bug: a
  full pipe logged an ERROR once a second, 1,826 lines in 31 minutes. It was fixed on 2026-08-19, and the pipe now
  logs one line when it fills and one when it frees. The 3 is simply "2 UI lanes + 1 spare". Several notes cited the
  tag as the reason, including a todo line written earlier today; each now carries a correction. Read this entry, not
  those notes.
- **multipipe-eval.md §4's warning no longer holds.** It said two commands running at once race on the class caches.
  Every access is now locked, and nothing is erased. What does grow with heavy commands running at once is the number
  of scan threads inside the game.
- **Planned, not built** (todo.md `[PIPE-PER-PROCESS-2026-11-06]`):
  - A. a pipe per game process, so the UI can tell games apart and list them by EXE name and window title;
  - B. 6 pipe slots;
  - C. hardening for a client that stays connected;
  - D. then the MCP automation idea.

## 2026-11-06 (build 3694) — The DLL's pipe refuses clients from other machines `[PIPE-REJECT-REMOTE]`

- **Another PC can no longer drive the DLL over the network.** The pipe `\\.\pipe\UE5DumpBfx` accepted remote clients.
  Anyone holding this user's (or an administrator's) credentials on another machine could open it through Windows file
  sharing, then read and write the game's memory and call its functions through the DLL. The pipe now refuses remote
  clients (`PIPE_REJECT_REMOTE_CLIENTS`). The UI, the Python pipe tools and Cheat Engine all connect locally and see
  no change. The four proxy DLLs carry the same change.
- **The pipe's permissions stay the Windows default, by decision.** An explicit "this user only" list was measured and
  not shipped. Against network clients it adds nothing to the flag. For a game running elevated it would let a
  non-elevated process in, which the default does not. The measurements and the remaining options are in
  todo.md `[PIPE-REJECT-REMOTE]`.
- **New rig, `tools/verify/pipe_remote_probe.py`.** It opens a pipe both locally and through this PC's own
  file-sharing server, which the pipe treats as a remote client, so the change can be checked without a second
  machine. Before scoring any refusal, it confirms a control pipe can be reached that way.
- Tests: the new source pin `EveryNamedPipeTheDllServes_RejectsRemoteClients` (red, then green); C# 7475 passed,
  0 failed, 1 skipped (`DUMPDIFF_REAL_DIR` unset); dll_core_test 1487 checks and dll_helpers_test 3832, 0 failures;
  37 gates passed, `check_processevent_slots` skipped (no `vendor/RE-UE4SS` in the worktree), 11/11 Lua suites.
  Live on DumperTest Shipping (UE 5.4, non-elevated):
  - `pipe_client.py get_pointers` returned all four globals and 24,530 objects;
  - the remote open was refused (err 5) while the local open succeeded;
  - the AOT UI of build 3693 connected (both lanes);
  - the CE mailbox (`mailbox_poke.py`) passed.

  DLL and proxies only, with no AOT UI publish; UE5Dumper.dll is 3,576,320 bytes.

## 2026-11-06 (build 3693) — Every hook the DLL enables is checked against Zydis before MinHook builds it `[MH-TRAMPOLINE-PREFLIGHT]`

- **A hook MinHook would build wrongly is refused instead of crashing the game.** MinHook copies a target's first
  instructions into a trampoline using its own small decoder. When that decoder misreads an instruction it reports
  success anyway, and the game crashes or misbehaves the first time the hooked function runs, with nothing logged.
  Before creating the ProcessEvent hook or the foreground lock's user32 hooks, the DLL now replays MinHook's build
  against Zydis (new module Falsch). On disagreement it logs the reason and leaves that hook out. Every target the DLL
  hooks today passes; the log line for the ProcessEvent hook now says how many instructions its trampoline copies.
- **The hook targets are read the way MinHook will read them first.** ProcessEvent's address used to get only a
  non-zero check before MinHook read it without SEH. A target whose bytes cannot be read is now refused, not faulted
  on. Execute-only code pages are accepted: x86-64 reads them.
- **The minhook and zydis submodules stay pinned, and why is now recorded**
  ([vendor-pin-reviews.md](vendor-pin-reviews.md)). `sync_tools.ps1` prints how many upstream commits are newer than
  the last review, so the next audit reads only what is new.
  - zydis, reviewed to `fcc63c6`: Denken's and Falsch's output is byte-identical against both revisions.
  - minhook, reviewed to `8af6b4a`: upstream's decoder rewrite fixes real mis-measurements but leaves two
    `trampoline.c` bugs. They are recorded there with the commit each was seen at, not reported upstream, and Falsch
    refuses both.
  - Bumping minhook is the next step, on the maintainer's go-ahead.
- Tests: dll_core_test 1487 checks, dll_helpers_test 3832, 0 failures; C# 7474 passed, 0 failed; headless 71; 39 gates.
  Live (`tools/verify/falsch_live.py`): DumperTest Shipping (UE 5.4) and DumperTest58 Shipping (UE 5.8), ProcessEvent
  hook vetted (4 instructions copied), FIRE returns "42", foreground lock on and off. AOT UI 61,676,032 bytes;
  UE5Dumper.dll 3,576,320 bytes.

## 2026-11-05 (build 3692) — A mistyped message template no longer breaks a command; gates freeze the hard-coded UI text `[I18N-READINESS-2026-11-04]`

- **A message template whose placeholders do not fit** (a translator's slip, a stray brace) now shows the template
  and its values instead of throwing out of the command that was setting the status line. Every template the app
  formats goes through one place, `Res.FormatTemplate`; en.axaml's own templates all fit (gate `check_res_format`).
- **The second-language policy is recorded** (working-lessons §6): English stays primary, the layout never changes per
  language, one English word with different meanings keeps different keys, and with no schedule yet the existing
  hard-coded text moves only when its file is touched.
- **Gates keep the hard-coded text from growing:** a new ratchet, `check_ui_literals`, freezes code-behind controls and
  dialog / error / progress messages (176 sites in 19 files); the status-line ratchet now counts `+=`, ignores the
  literals a line only compares, and its `--update` refuses to accept growth. 39 gates.
- Tests: dll_core_test 1487 checks and dll_helpers_test 3754, 0 failures; C# 7473 passed, 0 failed; headless 71.
  AOT UI 61,676,032 bytes.

## 2026-11-05 (build 3691) — Groundwork for UI languages: other programs' words, artifacts and parsed sentences each have one fixed source `[I18N-READINESS-2026-11-04]` P3-P5

No visible change: every sentence, export and generated table reads as before. What changed is where the text comes
from, so translating the UI later cannot break a branch or a saved table.

- **Other programs' words** (P5): the DLL's "Unknown command" / "Function not found" / "No instance found", the AOBMaker
  plugin's "Unknown type" and Windows' "being used" live in one do-not-localize block in `Constants.cs`, pinned to the
  DLL source that sends them.
- **A sentence the code reads back** (P3): Proxy Deploy's "Cannot read … here" is written and parsed from one template.
  The Live Walker's container crumbs always record their field name, so the CE XML export never parses a crumb label.
- **Artifact text** (P4): the CE XML export's enum note is an English constant (the status line keeps its own copy); a
  generated CT's categories have their own group names apart from the chips' labels; the standalone trainer escapes the
  record Description it quotes into Lua; trainer Descriptions and CE group names are marked do-not-localize.
- Tests: dll_core_test 1487 checks and dll_helpers_test 3754, 0 failures; C# 7467 passed, 0 failed; headless 71.
  AOT UI 61,675,008 bytes.

## 2026-11-05 (build 3690) — Status lines count classes as "classes"; unit tests read the English the app shows `[PARTIAL-UNIT-PLURAL]` `[I18N-READINESS-2026-11-04]`

- **The partial / capped / not-analysed notices read as English.** Find Func's status said "2 of 5 class(s) hit the
  scan deadline ... a 0 on those classs"; it now says "2 of 5 classes ... those classes", and every such notice agrees
  with its counts ("1 of 1 row ... is marked partial", "its count is a lower bound") with no "(s)" left.
- **Unit tests now see the app's real English** (first step of the move toward UI languages). With no Avalonia
  application a test's `Res.Get` answered "", so a string behind `Res` was blank and "must not say X" checks passed
  while testing nothing. The test assembly now installs en.axaml, read by a parser the headless suite pins against
  Avalonia's own loader, all 1914 keys (Avalonia keeps leading / trailing spaces). It exposed one test that pinned a
  fallback sentence the app never shows. Nothing changes in the app itself.
- Build 3689 was not shipped: that parser sat in the app and put System.Xml.Linq in the AOT image (+3.1 MB). It
  lives in the tests now; this build's AOT UI is 61,675,008 bytes (3688: 61,666,816).
- Tests: dll_core_test 1487 checks and dll_helpers_test 3754, 0 failures; C# 7452 passed, 0 failed; headless 71.

## 2026-11-05 (build 3688) — UE 5.5's narrow strings in optionals, struct arrays and Value Search; a struct array's FString member shows its text `[VND9-04]`

- **A struct array's string member shows its text** in the Live Walker (`Text = RowStrAlpha`), as a top-level string
  field does; an FString member said `(str)`, and a UE 5.5 `FUtf8String` / `FAnsiString` member showed its pointer's
  hex.
- **A `TOptional<FUtf8String>` / `TOptional<FAnsiString>` is read** like a `TOptional<FString>`, set or unset; it was
  refused as "not decoded".
- **Value Search's FString type finds UE 5.5's narrow strings too**, decoding each match at its own width (a direct
  field, a container element, a struct-array member, and the next scan's re-read), and V1c gates their optionals.
- **Property Search** suggests `Utf8StrProperty` and `AnsiStrProperty` in the type filter.
- **Checked live on build 3688** (`tools/verify/vnd9_live.py`, DumperTest Shipping 5.4): all six checks, including
  `Arr_StrRows[0].Text = RowStrAlpha` and an FString value search that finds and keeps a direct field (統一), an
  array element and a struct-array member. No fixture holds a narrow string, so that half rests on unit tests.
- Tests: dll_core_test 1487 checks and dll_helpers_test 3754, 0 failures; C# 7443 passed, 0 failed; headless 70.
  AOT UI 61,666,816 bytes.

## 2026-11-05 (build 3687) — A soft reference to an actor names the actor; FIRE shows a returned string's text; Dissect knows UE 5.5's narrow strings `[VND9-01]` `[VND9-02]` `[VND9-03]` `[VND9-DOC]`

- **A soft pointer to an object inside an asset keeps its sub-path.** A soft reference to an actor in a level reads
  `/Game/Map.Map:PersistentLevel.BP_Door_C_3` in the Live Walker, arrays, maps / sets / optionals and previews; it
  read as the map's own path. The sub-path's width is measured on the engine: an FString through UE 5.5, an
  FUtf8String from 5.6. Where it cannot be measured on 5.6+, nothing is added rather than text read at the wrong width.
- **Pipe FIRE shows a returned or out string's text** (`ReturnValue (return) = "42"`), where it printed the string's
  16 raw header bytes; the CE route already showed the text. New reply key `str_results` (pipe-protocol.md).
- **CE Structure Dissect** shows a UE 5.5+ `Utf8StrProperty` / `AnsiStrProperty` field as a pointer row, like FString,
  not as a decimal number.
- The USMAP writer's header records which writer it follows when an enum did not read, pinned by a test.
- **Checked live on build 3687** (`tools/verify/vnd9_live.py`): DumperTest Shipping (UE 5.4) 4/4 -- the probe logs
  `SubPathString StrProperty at +0x10`, Soft_Mesh is unchanged, a synthetic sub-path reads
  `/Engine/BasicShapes/Cube.Cube:統一` (restored after), FIRE `Conv_IntToString(42)` returns "42". DumperTest56 and
  DumperTest58 Shipping 2/2 each: `SubPathString Utf8StrProperty at +0x10`, and the same FIRE. Not checked live: a
  real actor soft reference on 5.6+ (no fixture field holds one), and the Dissect row in CE.
- Tests: dll_core_test 1476 checks and dll_helpers_test 3746, 0 failures; C# 7441 passed, 0 failed; headless 70;
  dissect_test.lua 437. AOT UI 61,666,816 bytes.

## 2026-11-05 (no build change) — Vendor audit #9; `sync_tools.ps1` stops a false FETCH FAILED `[VENDOR-AUDIT9-2026-11-05]`

- **`sync_tools.ps1`** no longer prints `FETCH FAILED` over a reference-clone fetch that succeeded. RE-UE4SS's new commits moved its UEPseudo pin, and git's default on-demand recursion fetched that submodule through its `git@github.com:` URL, which fails without an SSH key. The clone fetch passes `--no-recurse-submodules`; the RE-UE4SS block already syncs the submodules over https (`795ce835`).
- **Vendor audit #9** read RE-UE4SS (27 commits), UEPseudo (10), Dumper-7 (3), zydis (+13) and Epic's new `5.9` branch. Nothing forces code; three pre-existing gaps are filed as `[VND9-01]`..`[VND9-03]` ([audit-2026-11-05-vendor-delta9.md](audit-2026-11-05-vendor-delta9.md)).
- Tools and docs only: the DLL, the proxies and the UI are unchanged.

## 2026-11-04 (build 3686) — A pick never refuses a live object while a collection runs; mailbox contract 9 `[INVOKE-PICK-GARBAGE]`

- Review 1 of build 3685's fix. Telling a destroyed object apart now uses its destroyed flags alone. On UE4 and UE
  5.0-5.3 a garbage collection first marks every object it may free, and live ones are marked too until it clears them,
  so the old test could pass over every live object while one ran. This covers an invoke by class name, the Console's
  Run, the CE invoke helper, Navigate / Locate, the internal state reads and the local-PlayerController fallback.
- A destroyed object is no longer a partial class name's last-resort match either. The DLL log says how many destroyed
  objects a lookup passed over.
- **Mailbox contract 9** (MIN stays 1): CE's FIND_INSTANCE, and the INVOKE_BY_NAME and LIST_FUNCTIONS lookups through it,
  never hand over a destroyed object. A table generated now refuses an older DLL; an older table works unchanged with
  this DLL. The freeze helper bakes 9 and stays version 1.7.
- **Checked live on build 3686** (DumperTest Shipping): `invoke_pick_garbage_live.py` 8/8. The destroyed actor stayed in
  GObjects, flagged garbage, from the destroy until after the last lookup. The by-name invoke, find_live_instance and the
  mailbox all took the next live actor, and the DLL log said "passed over 1 destroyed".
- Tests: dll_core_test 1,464; dll_helpers_test 3,746; C# 7,433 total, 0 failed, 1 skipped; Lua 11/11; 37 gates; six
  mutations, each killed. AOT UI 61,664,256 bytes.

## 2026-11-04 (build 3685) — An invoke by class name never takes a destroyed object `[INVOKE-PICK-GARBAGE]`

- An actor destroyed in play stays in the object list until the next garbage collection (about a minute on a stock
  engine), and an invoke by class name could land on it if it sat first. An invoke by class name and the DLL's internal
  state reads (the Debug Camera's CheatManager among them) now pass over it, as Navigate already did. With only
  destroyed objects of a class left, an invoke falls back as it does with none live.
- **Checked live on build 3685** (DumperTest Shipping): `invoke_pick_garbage_live.py` 6/6. A destroyed StaticMeshActor
  stayed in GObjects, flagged garbage. The by-name invoke, find_live_instance and the mailbox's FIND_INSTANCE took the
  next one, while the old exact-name lookup still listed the destroyed one first.
- AOT UI 61,663,744 bytes.

## 2026-11-04 (build 3684) — Navigate and Locate open a live object of the class or a subclass, never a template `[MAINWINDOW-NAVIGATE-EXACT-NAME]`

- **"Live" and "Locate in GWorld / GameEngine"** (Interesting Funcs, Interesting Props, Live Funcs, Console, Detect
  Stats, the Xref dialog) and Detect Stats' live check now ask the DLL for **a live object of the class or of a
  subclass**: a class whose objects are all of a Blueprint or game subclass (`Character` in a third-person game) opens
  that object instead of falling back to the class view, and a class default object's subobject (a template) is never
  opened or searched from. An object of the class itself comes first; a destroyed one, not yet collected, is passed
  over; the Debug Camera's own objects come last, and the line says so.
- A function opened on a subclass's object is not in Live Walker's list (it shows a class's own functions): the line
  now says the function is inherited, instead of "not in this class". Every Locate names the object it searches from.
- A DLL older than this UI (a proxy left in a game folder) still answers, with the old exact-name lookup, and the line
  says how to replace it.
- New pipe command `find_live_instance` (105 commands).
- **Checked live on build 3684** (DumperTest Shipping, AOT UI): `live_instance_lookup_live.py` 7/7; the navigate,
  Locate and inherited-function lines as recorded in the row.
- Three review rounds (1 MED + 3 LOW + 8 INFO, 7 LOW + 8 INFO, 5 LOW + 5 INFO), all fixed but one INFO kept on purpose,
  each round mutation-checked.
- Tests: C# 7,433 total, 0 failed, 1 skipped; headless 70; dll_core_test 1,451; 37 gates. AOT UI 61,663,744 bytes.

## 2026-11-04 (build 3683) — Property Search's status line wraps on a row of its own; Freeze lights up when AOBMaker connects `[PROPSEARCH-STATUS-CLIPPED]` `[PROPSEARCH-FREEZE-STALE-AOBMAKER]`

- **Property Search's status line has a row of its own** under the search bar and wraps there, so a long status --
  a capped Force count, a hold armed on nothing, the templates-only note -- is read whole instead of ending in "..."
  with the rest in a tooltip.
- **Freeze follows what the app knows about the AOBMaker plugin.** It used to stay grey after the plugin connected
  until the tab was switched; now the toolbar ⟳, any panel's probe and a fresh Search all update it, and what
  Property Search learns updates the toolbar's DLL dot. A Freeze clicked while a probe is still waiting on the pipe
  waits for its answer instead of opening the value dialog over a pipe that is gone.
- **The toolbar's DLL dot hears every probe answer** of Live Walker, the System tab, Interesting Functions and
  Teleport, not only a change of their own flag: Interesting Functions' first probe after Cheat Engine came up never
  reached it, and a "down" confirmed while the dot said "up" left it saying connected.
- **A failed Freeze says which failure it was**: no reply (the record may still have been added -- check before
  sending it again), a refusal with the plugin's reason, a rejection, or an unreachable plugin with why.
- The availability cooldowns run on a monotonic clock; a clock set back no longer stops the re-checks.
- **Checked live on build 3683** (DumperTest Shipping, AOT UI): the status rows and the Freeze / DLL-dot transitions
  as recorded in the two rows.
- Three review rounds (2 LOW + 5 INFO, 2 LOW + 3 INFO, 6 INFO), all fixed and mutation-checked.
- Tests: C# 7,387 total, 0 failed, 1 skipped; headless 70; 37 gates. AOT UI 61,645,312 bytes.

## 2026-11-03 (build 3682) — Debug Camera Force ON with no live CheatManager is refused; Freeze and Force never write a template; the param dialog FIRE by class name; the local-PlayerController fallback by derivation `[DEBUGCAM-FORCEON-CDO]` `[LISTINSTANCES-KEEPS-TEMPLATES]` `[MAINWINDOW-INVOKE-LIMIT1-CDO]` `[FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS]`

- **Debug Camera Force ON with no live CheatManager** (every UE 5 Shipping build by default) is no longer sent: the
  Console row, the Teleport card, the hotkey and the CE record say there is no live CheatManager and the badge stays
  OFF. The DLL answers -17 at once (pipe, mailbox, C ABI), with no game-thread call. Force OFF there switches the
  controller back by hand. A failed Force or refresh (-1) no longer claims "no live CheatManager".
- **Freeze and Force hold live objects only.** A class default object's subobjects and a Blueprint's component
  templates are no longer frozen or forced -- whatever a template holds, every later spawn copies, and the freeze
  helper never restores. Counts and the "capped" flag count live objects. Mailbox contract 8 (MIN 1): a newly
  generated freeze record refuses an older DLL. A Force armed on nothing because only templates of the class exist
  says so. The Property Search preview no longer shows a template's value as live.
- **The param dialog opened from the Console** now FIREs: it sends the class name, and the DLL picks a live object
  at each FIRE (the line above the arguments says so; the result names the object).
- **When a game's world chain breaks** (menus, travel), teleport, god mode, movement, time, current target, the
  stealth meter and fly find the local PlayerController by class derivation, the local player's first, never a
  destroyed one; a debug camera subclass named without the word is hopped like the stock one.
- **Checked live on build 3682** (DumperTest Shipping 5.4, UE427_3rdPerson Shipping 4.27, DumperTest58 Development 5.8):
  `dbgcam_cdo_refusal.py --stall` 14/14 (plus controls on the two fixtures with a live CheatManager);
  `listinstances_templates_live.py` 10/10 on all three; `localpc_fallback_live.py all` 39/39 on all three;
  `localpc_dbgcam_live.py` 12/12 (the active debug camera picked and hopped); UI and CE checks as in the rows.
- Tests: C# 7,358 total, 0 failed, 1 skipped; dll_core_test 1,440; dll_helpers_test 3,736; 37 gates. AOT UI
  61,639,168 bytes.

## 2026-11-03 (build 3681) — The coordinate library measures no distance from a parent-relative pose; the empty undo slot's texts `[TELEPORT-RECALL-PARENTREL]`

- **The library's Dist column** no longer measures from a position that is not world coordinates: on a
  parent-relative read it shows "—".
- **An empty Last Position** reads "(none yet — auto-saved just before a jump)", and Recall last there says the slot
  is saved before a jump unless that position could not be read in world coordinates. Both used to say every jump
  saves it.
- **Checked live on DumperTest Shipping, build 3681:** a fresh launch showed the new placeholder; with the pawn
  attached and its world read failing, the Dist column showed "—" (0 and 1,432 when healthy); a jump in that state
  left the undo slot empty before, after and after the game resumed.
- Tests: C# 7,332 total, 0 failed, 1 skipped; 37 gates. AOT UI 61,632,512 bytes.

## 2026-11-03 (build 3680) — Recall, Force and TP facing refuse a position that is not world coordinates; the undo slot keeps the previous one `[TELEPORT-RECALL-PARENTREL]`

When the pawn rides a vehicle or mount and its world position cannot be read, the dumper falls back to its position
relative to that parent (the ⚠ parent-relative chip). Those numbers are not world coordinates, and teleporting to
them sends the pawn somewhere unrelated, often under the map.

- **Recall and Force refuse a marker saved from such a read.** Force overrides only the "saved on another map"
  check, which can be wrong; nothing makes relative numbers world coordinates. Nothing moves, and the marker save
  already says Recall and Force will refuse it.
- **TP facing direction refuses to step from such a read**: there is no world position to step from.
- **Recall last never returns to such a read.** The undo slot keeps its previous position instead (the
  maintainer's option B); the jump itself still runs.
- **Fill from current pos, Save current pos and Update from current** refuse such a read: the coordinate boxes and a
  library entry cannot carry the flag, and they are sent on as world coordinates.
- **CE:** the Recall marker and TP facing records name the refusal; a record generated earlier shows "failed (code
  -16)".
- **Checked live on DumperTest Shipping, build 3680:**
  - **DLL** (`recall_parentrel_check.py`, 9/9): Recall, Force and TP facing answered -16 and the undo slot did not
    change; a jump made while the position read was parent-relative left the undo slot at its previous world
    position.
  - **UI:** each refusal showed its sentence; the library stayed at 5 entries; the Last Position row did not change.
  - **CE:** the Recall marker and TP facing records showed their refusals.
- Tests: dll_core_test 1,413 checks and dll_helpers_test 3,732, 0 failures; C# 7,330 total, 0 failed, 1 skipped;
  37 gates. AOT UI 61,630,976 bytes.

## 2026-11-03 (no build change) — The publish build's two test-target warnings `[BUILD-WARN-C4456-C4819]`

- `dll_helpers_test`: C4456, an inner `s` in the Lager window test hid the outer one; renamed.
- The three `owncopy_*` fixtures for `dll_core_test` were the only C++ targets built without `/utf-8`. They include
  `Mimic.h`, whose comments are UTF-8, so a zh-TW (code page 950) machine warned C4819. They now take `/utf-8` like
  every other target.
- Test targets only: the DLL, the proxies and the UI are unchanged.

## 2026-11-03 (build 3679) — A refused Console run keeps the selection; the Debug Camera Force and a run never overlap `[CONSOLE-ROWRUN-SELECTED]`

- **A refused row Run leaves the selection** on the row that is still running.
- **The refusal reads "⏳ … was not sent — a Console command is still running. Wait for its result first."** It no
  longer calls the running command "another": it can be the same command, started a second way. And it no longer
  says to run it again.
- **The Debug Camera Force On / Off and a Console run no longer overlap.** Each refuses while the other is in flight:
  both queue a toggle on a busy game thread, and two toggles cancel out.
- **Correction to build 3678's entry:** "one command at a time" holds only while the first call is in flight, up to
  the 5 s dispatch timeout. After that the command can still be queued (the status line says so), and a second run
  is sent behind it. In 3678 the Force and a run could also still overlap; 3679 closes that.
- **Checked live on DumperTest58 Development, build 3679, with its game thread suspended:**
  - During a ToggleDebugCamera run, another row's Run said it was not sent, and the selection stayed on
    ToggleDebugCamera. Force On was refused the same way.
  - During a Force OFF, the ToggleDebugCamera row's Run was refused, and Recent invocations gained nothing.
  - Each time, one toggle drained once the thread resumed.
- Tests: C# 7,314 total, 0 failed, 1 skipped; 37 gates. AOT UI 61,621,248 bytes.

## 2026-11-03 (build 3678) — The Console sends one command at a time `[CONSOLE-ROWRUN-SELECTED]` `[RIG-PIPE-DOUBLE-CONNECT]`

- **No Console run is sent while another is still running.** A row's Run button or a history replay used to send a
  second command while the first was still waiting on a busy game thread. A command that times out stays queued and
  still runs, so the second one could run it twice. Now the Console says "⏳ … was not sent" and sends nothing.
- **Checked live on DumperTest58 Development, build 3678, with its game thread suspended:** Run Selected on
  ToggleDebugCamera, then the row's Run a second later, said it was not sent. The first answered -5 (still queued)
  after the 5 s timeout, Recent invocations held one entry, and once the thread resumed the camera was ON: one toggle,
  not two.
- **Tools:** a pipe rig that reuses its client after its handle was closed now reopens it instead of reading the
  dead handle, and ten more rigs stopped opening the pipe twice.
- Tests: C# 7,311 total, 0 failed, 1 skipped; 37 gates. AOT UI 61,620,224 bytes.

## 2026-11-03 (build 3677) — A Console row's Run button runs its own row `[CONSOLE-ROWRUN-SELECTED]` `[RIG-PIPE-DOUBLE-CONNECT]`

- **A Console row's Run button runs that row.** It used to run the row that was *selected*, and clicking the button
  does not select its row. So with nothing selected it only said "Pick an exec command from the list first.", and with
  another row selected it ran that other command. Present since build 731. Now it runs its own row and selects it;
  the toolbar's Run Selected is unchanged.
- **Checked live on DumperTest58 Development, build 3677:** with nothing selected, the ToggleDebugCamera row's Run
  ran it on the live CheatManager. With `PlayerController::Camera` selected, the same button ran ToggleDebugCamera
  again (pinned), and Camera's parameter dialog did not open.
- **Tools:** the pipe rigs no longer open a second pipe handle, which left no pipe instance free while the UI was
  connected. `pipe_client.py --selftest` is now a gate (37).
- Tests: C# 7,309 total, 0 failed, 1 skipped; 37 gates. AOT UI 61,619,712 bytes.

## 2026-11-03 (build 3676) — A function that is not static, run on a class default object or another template, is warned about; class lookups never take a template; mailbox contract 7 `[INVOKE-NONSTATIC-ON-CDO]` `[OPEN-CENSUS-2026-10-28]`

- **Warn, never refuse.** When an invoke runs a function that is not static on a class default object, one of its
  default subobjects, or another template (such as a Blueprint's component template), it still runs, and you are
  told. A game's own commands often work there, so nothing is blocked.
  - **Live Walker / param dialog:** a ⚠ line under the result names the object and what it is a template of.
  - **Console:** the status says so, and the Console no longer reuses ("pins") such an object.
  - **Debug Camera Force On / Off:** says when the toggle targets the CheatManager's class default object, which
    happens when there is no live CheatManager (usual in a Shipping build).
  - **CE records** (INV, AA(Baked), and scripts using the invoke helper): print the warning even with `UE5_DEBUG`
    off, and keep the Lua Engine window open. The Debug Camera record adds it to the message of an ON that did not
    take or is queued.
- **"An instance of class X" never lands on a template.** A component class used to resolve to a component of some
  class default object; it now takes the live component.
- **Mailbox contract 7** (minimum still 1): every saved .CT keeps working. `ue5_invoke_helper.lua` 1.6 reads the
  warning only from a DLL that publishes it.
- **Checked live, build 3676:**
  - **The pipe and the mailbox** (`invoke_template_live.py`): all 15 checks pass on DumperTest Shipping (5.4),
    DumperTest58 Development (5.8) and UE427_3rdPerson Shipping (4.27). On 3675 every component class had landed
    on a default subobject; on 3676 each lands on the live one.
  - **The param dialog:** `K2_GetActorLocation` on `Default__Actor` showed the warning. `GetMaxAcceleration` on the
    pawn's live movement component returned 1500 with no warning; on a template's it returned 2048 with the warning.
    A static function on `Default__KismetSystemLibrary` showed none.
  - **CE:** the INV and AA(Baked) records for a class with no live object printed the warning and stayed open; the
    INV record for a live object printed nothing and closed.
  - **Console and Debug Camera** (DumperTest58 Development, a live CheatManager): ToggleDebugCamera named its
    CheatManager, then ran pinned on it; Force On / Off carried no note.
  - **BugItGo's UI and CE halves, from build 3675** (DumperTest Shipping, a parent-relative pose staged with l78):
    Copy as BugItGo refused, emptied the Run field and left the clipboard alone. The CE BugIt record warned that
    BugItGo will refuse the pose; the BugItGo record refused it, and the pawn did not move.
- Tests: dll_core_test 1,413 checks and dll_helpers_test 3,723, 0 failures; C# 7,306 total, 0 failed, 1 skipped;
  36 gates. AOT UI 61,614,592 bytes.

## 2026-11-03 (build 3675) — BugItGo refuses a parent-relative BugIt; "an instance of class X" picks by derivation; mailbox contract 6 `[TELEPORT-BUGITGO-PARENTREL]` `[FINDINSTANCE-SUBSTRING-USERFACING]` `[OPEN-CENSUS-2026-10-28]`

- **BugItGo no longer drives a vehicle-relative pose in as world coordinates.**
  - A BugIt taken while the pawn is attached and its world position cannot be read stores numbers relative to the
    parent (vehicle, mount, platform). The CE BugItGo record now refuses it: nothing moves, and the "last" slot is not
    touched. The record says why. The BugIt record also warns that BugItGo will refuse that pose.
  - The UI's own pair: **Copy as BugItGo** copies nothing on such a read and empties the Run field, since a
    `BugItGo X Y Z` string cannot carry the flag.
  - Marker Recall and Recall last still accept such a pose; that is filed as `[TELEPORT-RECALL-PARENTREL]`.
- **"An instance of class X" picks the right object.** The Console's exec runs, CE scripts (FIND_INSTANCE,
  invokeUFunction) and the C ABI lookup used to take the first object whose class NAME merely contained the text. Now:
  - the first live object of that class or a subclass;
  - an object the Debug Camera controller is or owns, only when no other is live;
  - else the class's own default object;
  - and a name no class has is still matched as part of a class name, as before.
  So "CheatManager" no longer lands on a cheat extension or on the Debug Camera's own CheatManager, and "Actor" no
  longer lands on an ActorChannel.
- **The Console says which object it used** (name and class), warns when it is a class default object or the Debug
  Camera's, and keeps reusing ("pins") only a live object.
- **Mailbox contract 6** (minimum still 1): every saved .CT keeps working. A script generated now refuses an older DLL
  that would still guess.
- **Checked live on DumperTest Shipping, build 3674 against 3675:**
  - **BugItGo.** On 3674 it moved the pawn 1,461.7 uu to the parent-relative numbers read as world coordinates. On
    3675 it answered -16 and the pawn moved 0.0 uu.
  - **Class lookup.** On 3674, "Actor" took `Default__ActorChannel` ("Function not found") and the mailbox's
    "CheatManager" took `Default__CheatManagerExtension`. On 3675 those are a live `DirectionalLight` and
    `Default__CheatManager`, and all 12 checks pass.

## 2026-11-03 (build 3674) — The .usmap writes an enum's real underlying type, signedness included `[USMAP-ENUM-SIGNEDNESS]` `[OPEN-CENSUS-2026-10-28]`

The .usmap took an enum property's underlying integer type from its size alone, so the width was right but the sign
could be wrong: a `: int8` enum came out as a byte, and a `: uint32` one as a signed int. Both canonical .usmap writers
use the property's own underlying type.

- **The DLL reads it.** `walk_class` (and `walk_class_batch`, and Dump All, which share its encoder) sends an enum
  property's underlying type as `enum_underlying`. It is read only after the enum itself was read and checked, and only
  when it names an integer type.
- **The .usmap uses it** when its width agrees with the field's size. Otherwise (an older DLL, a read that did not
  check out, a width that disagrees) it falls back to the size, as before.
- **Checked live on 3674** (DumperTest Shipping, UE 5.4, `tools/verify/enum_underlying_pipe_check.py`): 7,696 classes
  and structs, 3,558 enum fields. Every one carries a type whose width equals its size: 3,326 Byte, 221 Int, 7 UInt32,
  4 Int8. The 11 UInt32 and Int8 fields are the ones the old guess got wrong. Their declarations in the engine source
  agree: `EPanningMethod : int8`, `ENetObjectCountLimiterMode : uint32`.
- Also checked live on 3673, recorded in the census rows: the Fly card's collision readout, Value Search refusing `inf`
  but not a value left in a hidden box, and Detect Stealth Meter's found path.

## 2026-11-03 (build 3673) — Review round 2 of the census pass: all 34 findings fixed `[FLY-REPORTS-WISH]` `[LEAD-LW-BACK-CACHED-ELEMENTS]` `[LEAD-BOOKMARK-SAMECLASS-RECYCLE]` `[VALUESCAN-NONFINITE-TARGET]` `[LOG-RESUME-LIVE-ROLLED]` `[UE4-USTRUCT-GIVEUP-STATS]` `[LEAD-PROPSEARCH-TOPTIONAL-LEAVES]` `[CEXPORT-ENUMNAMES-NOTE]` `[LEAD-DT-EXPORT-64ROWS]` `[LEAD-SECOND-UI-CRASHLOG]` `[SOLIDE-RELATED-STOPS]` `[UBEL-ASCII-FIRSTCHAR]` `[D4B-DELEGATEPAD-2026-09-09]` `[OPEN-CENSUS-2026-10-28]`

A second adversarial review, six reviewers over the census pass's later fixes and round 1's follow-ups, found 34 issues:
2 MED, 16 LOW, 16 INFO. Each was checked against the code, and every one is fixed, with a red test first where
behaviour changed.

- **The Fly card waits for the collision it asked for.** `fly_set` answers before the Fly worker applies noclip, so
  every Fly ON with noclip said "noclip asked, collision still on". Measured on 3672, DumperTest Development: the pawn
  stopped colliding 103 ms after the reply. The card now re-reads for up to about a second, and it reports
  "noclip off, collision still off" in the other direction.
- **Live Walker: re-entering a container view.**
  - Back / Forward / a crumb jump no longer wait for the owner re-read, and no longer wipe the navigation's own status
    line.
  - An owner that is gone, is another object, or lost the field drops the rows and says why, instead of leaving them
    editable. Refresh inside the view does the same.
  - A pointer slot rewritten in place shows its new target.
- **Bookmarks.** A container view whose owner's address holds another object is caught. A bookmarked struct is checked
  through the object holding it: an inline struct of a recycled owner reads stale, and a DataTable row whose bytes
  changed does not.
- **Value Search.** The non-finite check reads only the boxes the scan type sends, so a leftover hidden value no longer
  blocks a scan. It also refuses C99 spellings the DLL reads as infinite (`inf`, `nan(ind)`).
- **Property Search.** Copy Offset refuses a deep row, as Freeze and Force do, and the refusal now says the real reason.
- **UE4 offset give-up.** The Next probe no longer mistakes OuterPrivate (a case-preserving 0x28) for UField::Next.
- **Exports.**
  - A DataTable's row enums are covered by the enum-names note: `walk_datatable_rows` carries `enum_names_failed`.
  - The note says enum fields show as plain numbers, not that their dropdowns are empty.
  - The AOB fallback note no longer hides the limit warning.
- **Others.**
  - Log compression holds a game folder's newest rolled UI mirror log like its slot.
  - The UI's single-instance mutex is taken inside Main's crash.log catch. A mutex an elevated instance holds now reads
    as "another instance".
  - Wirbel's struct probe accepts a CJK name, as Ubel does.
  - Stealth Meter's stop line comes from en.axaml.
  - `pipe-protocol.md` documents `stops`, `fly_set` and `fly_get_state`.
- **Checked live on 3672** (DLL side, `tools/verify/census_pass2_pipe_checks.py`): a delegate row's hex holds the whole
  value, with no space on DumperTest Shipping and the 8-byte detector, then a space, on Development;
  `find_stealth_meter` carries `stops`; the Fly collision reads 1, then 0 with noclip, then 1.

## 2026-11-02 (build 3672) — The census pass's second half: delegate hex, a STATS UE4 give-up, bookmarks, the pipe reader `[D4B-DELEGATEPAD-2026-09-09]` `[UE4-USTRUCT-GIVEUP-STATS]` `[LEAD-PROPSEARCH-TOPTIONAL-LEAVES]` `[LEAD-BOOKMARK-SAMECLASS-RECYCLE]` `[TRACE-UI-READ-SPEED]` `[LEAD-LW-BACK-CACHED-ELEMENTS]` `[LEAD-USMAP-UDS-SHADOW]` `[UBEL-ASCII-FIRSTCHAR]` `[VALUESCAN-NONFINITE-TARGET]` `[CSX-LONG-ELEMENT-LABEL]` `[LEAD-KEYWORD-DROPDOWN-FIRST-CLICK]` `[SOLIDE-RELATED-STOPS]` `[FLY-REPORTS-WISH]` `[LOG-RESUME-LIVE-ROLLED]` `[LEAD-LW-INPLACE-SEARCHCOUNT]` `[OPEN-CENSUS-2026-10-28]`

The census fix pass went on through the remaining open items, one red and one green commit each.

- **A delegate row's hex holds the whole value.** On a checked build that means the 8-byte access detector, a space,
  then the payload; Shipping's hex is unchanged.
- **A STATS UE4 give-up** (Development / DebugGame 4.11–4.24) measures UField::Next on the property chains before it
  ships the UStruct head. Development UE423_Flying measured live on 3671: Next 0x30, head 48/50/58/60.
- **Live Walker.**
  - Back / Forward / a crumb jump into a container view re-read the owner and re-render when the container moved.
  - A bookmark on its saved address is not claimed by another instance of its class.
  - Property Search's Freeze and Force refuse a deep row themselves.
- **Opening a large Live Funcs trace.** The pipe's reply reader now reads 64 KB at a time, not 1 KB. Offline, the read
  loop went from about 60 to about 600 MB/s.
- **.usmap.** A super that a name-keyed reader cannot reach is written as none, with the whole chain kept. That covers
  UE 5's same-named AnimBlueprintGeneratedConstantData pair, and CUE4Parse keeps one struct per name.
- **Earlier in the pass, built here for the first time:**
  - CJK-first names pass the DLL's name guards.
  - Value Search refuses non-finite targets.
  - CSX keeps a long element label's start.
  - The keyword dropdown's first click reaches its button.
  - Detect Stealth Meter says when its search stopped short.
  - The Fly card reports the collision it observed.
  - Log compression treats a live rolled log as live.
  - An in-place refresh counts its search matches.
- **Chased, no code change.**
  - `walk_class` against `walk_class_batch`: 1000 of 1000 classes agree on UE 4.23 Shipping and Development; the old
    zero was a misreading of the reply's shape.
  - The G12/G3 log contradiction was a deliberately staged `-dirty` build (working-lessons 1.ap).
  - A second DLL copy's auto-start was re-derived; its fix is the maintainer's call.

## 2026-11-02 (build 3671) — Eleven census fixes: exports say what they left out, rows without an address stay put, a pinned Console run checks its object `[LEAD-LW-NULLROW-HEX]` `[LEAD-CONSOLE-PIN-EXPECT-CLASS]` `[LEAD-LW-SYNTH-CONTAINER-STRUCT-OWNER]` `[LEAD-BOOKMARK-DT-ROWVIEW-NOT-LAST]` `[DISSECT-SPARSE-DELEGATE-WIDTH]` `[LEAD-DT-EXPORT-64ROWS]` `[CEXPORT-NESTED-TRUNC-NOTE]` `[CEXPORT-STRIDE-NOTE]` `[CEXPORT-ENUMNAMES-NOTE]` `[CLASSPIVOT-DT-STARTUP-LOG]` `[LEAD-SECOND-UI-CRASHLOG]` `[OPEN-CENSUS-2026-10-28]`

The maintainer asked for the census's open bugs to be fixed one after another. Each item is a red test commit and a
green fix commit, and each census row names them.

- **Live Walker: HEX and +CE on a row with no address of its own do nothing, and say why.** A DataTable row whose row
  pointer reads null moved CE's hex view to 0 and added a record at 0x0. Addr, HEX, +CE and +CE Field (flat) now share
  one check.
- **The Console's pinned run checks the object is still the one it pinned.** The invoke reply names the class read at
  the address (`instance_class_addr`, a new optional reply key). A pinned run sends it back as `expect_class_addr`, so
  an object freed and its memory reused by another class is re-resolved instead of run.
- **Bookmarks.**
  - A bookmark file whose Array / Map / Set sits in an inline struct restores the container's elements, on load and
    on Back. Before, the struct's bytes were read as a UObject.
  - A bookmark saved below a DataTable row view restores that view's rows too, guarded by the row struct the next
    crumb was drilled into. If the table changed, the load says so, and Back says the view did not restore.
- **CE Structure Dissect (`ue5_dissect.lua`) draws a sparse delegate as its one bIsBound byte**, not a 16-byte pointer
  over the fields after it.
- **Exports say what they left out.**
  - Copy CE XML and CSX name a table cut to its first page: "Only the first page of a table's rows is read: RowMap
    (DataTable: 100 rows, 64 exported)".
  - They also name a container cut at the Array Limit inside a struct or pointer target the export walked. One cut met
    in many objects is said once, "in N objects", and past five the rest are counted.
  - Copy CE Field of rows you selected reports nothing as cut, unless you selected every loaded row.
  - When the object-array stride is a guess, each CE table and CSX opens with the badge's words as a comment. The
    AOB-wrapped export now also carries the packed-layout note it lacked.
  - When UEnum::Names was never found on a build, an export holding an enum field says why its dropdowns are empty.
    Walk replies carry `enum_names_failed`, and `enum_addr` goes out even with no entries.
- **Class Pivot left on the DataTable source no longer logs an error at start-up**, before any connection.
- **A second UI launch no longer overwrites crash.log.** It raises the first window and exits before Avalonia starts.
- **Review round 1.** One adversarial round over the first nine fixes found 25 issues: none HIGH or MED, all LOW or INFO,
  each confirmed by a refuter. Every one is fixed. The two that changed behaviour:
  - Select-all with Copy CE Field had lost the cut warning.
  - The enum note never fired for TEnumAsByte, and missed a latch that the export's own walk set.
- **Checked live on 3671.**
  - DumperTest Shipping, UE 5.4, through the pipe (`tools/verify/census_pass_pipe_checks.py`):
    - an invoke by name answers `instance_class_addr` equal to the object's class, and the pinned run with it ran;
    - 11 enum fields carry `enum_addr` with their entries, and there is no latch;
    - DumperTestTable_Big has 100 rows, and one page returns 64.
  - DumperTest Shipping, in the UI:
    - Copy CE XML of DumperTestTable_Big ends "...RowMap (DataTable: 100 rows, 64 exported)";
    - in its rows view, Copy CE Field of one row says nothing, and Ctrl+A gives the warning;
    - a Console run of AISystem::AIIgnorePlayers, then a pinned rerun, sent `expect_class_addr` = the reply's
      `instance_class_addr` and answered "ProcessEvent OK";
    - Class Pivot switched to DataTable before Connect logged nothing, and the connect listed both tables.
  - `tools/verify/second_instance_crashlog.py` (no game): the second launch exited 1 in 0.2 s, with crash.log's bytes
    and timestamp unchanged.
- **Not checked live.**
  - These have no fixture: a null DataTable row, an inline-struct-owned container in a bookmark file, a bookmark below a
    row view, a guessed stride, a build whose UEnum::Names is not found, a CJK-first name.
  - The dissect's sparse row was checked only on CE's own Lua 5.3, not drawn in CE.
  - Push to CE Structure Dissect carries no header note into CE: its parser drops every XML comment.
- Tests: dll_core_test 1,363 checks and dll_helpers_test 3,692, 0 failures; C# 7,154 total, 0 failed, 1 skipped;
  headless UI 69; dissect_test.lua 426; 36 gates. AOT UI 61,506,560 bytes.

## Older entries

Builds **3263–3670** (2026-08-19 … 2026-10-29, 153 entries) were moved to
[`archive/dev-log-2026-10-pre-build-3671.md`](archive/dev-log-2026-10-pre-build-3671.md)
on 2026-11-20 (nothing edited, only moved; `tools/docs_archive/split_devlog.py`).
Builds **2779–3261** (2026-08-10 … 2026-08-17, 75 entries) were moved to
[`archive/dev-log-2026-08-pre-build-3263.md`](archive/dev-log-2026-08-pre-build-3263.md)
on 2026-09-26 (nothing edited, only moved).
Builds **2220–2747** were moved to
[`archive/dev-log-2026-08-pre-build-2779.md`](archive/dev-log-2026-08-pre-build-2779.md)
on 2026-08-25 (nothing edited, only moved). Everything before that is in the four older
archives that file links.
