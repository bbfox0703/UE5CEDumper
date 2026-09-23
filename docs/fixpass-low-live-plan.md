# FIX PASS — LOW live-check plan and per-row recipes (surveyed 2026-09-22)

⭐ **Open this before running ANY LOW row of `docs/todo.md` `[FIXPASS-2026-09-10]` → *Live-check backlog*.** It holds the session order (which rows share one fixture launch), each row's status, and a recipe per row: fixture, preconditions, steps, the exact strings to expect with their source lines, an existing rig if any, and traps.

⛔ **Two rules that travel with this file** (the maintainer's, 2026-09-22):
1. **After every verified row, update BOTH records in the same commit:** the row's closure record in `docs/todo.md` (the backlog table) AND the row's status in the session table below. One row per commit, then push.
2. **Rows unreachable on this machine get a fixture change when one would make them reachable** — `tools/ue-sample/` DumperTest (5.4), DumperTest56, DumperTest58. See *Fixture changes* below.

⚠ **What the recipes are, and are not.** They are the read-only output of an 8-agent survey run at HEAD `b87915ea` (`git log` for the tree state); **no recipe was live-run by the survey**. Every `file:line` was read at that HEAD and drifts with the code. The survey's synthesis stage already corrected several of its own scouts (listed below). Treat a recipe as a starting point to re-check, not as evidence. Where a recipe and the row's text in `todo.md` disagree, the discrepancy is recorded in the recipe; re-read the source before trusting either.

-----

## Session order

One injected game at a time; kill the game, CE and the UI the moment a row is done (handover §4). The grouping is only about SHARED SETUP — each row is still its own verification and its own commit.

### S1

**Setup:** no game. AOT dist\UE5DumpUI.exe only. No Cheat Engine running (required by L64's manufactured pipe holder). Back up ui-options.json and window-state.txt once for the session.

**Why grouped:** All are UI-only rows with no game. L74's seed files must be planted BEFORE the first UI launch, because archiving runs at startup. L68's discriminating mid-session diff uses that same UI run. L76 (Cancel import) and L70 (typed-preview controls) touch no persisted state. L64 needs only the toolbar ⟳ plus a Python holder of \\.\pipe\AOBMakerCEBridge. L68's taskkill /F arm ends the session, followed by one relaunch to read back the caps. Restore ui-options.json at the end.

| # | row (arm) | status |
|---|---|---|
| 1 | L74 | ✅ PASSED `4d0cde0c` |
| 2 | L68 | ✅ RE-VERIFIED red→green 2026-09-22 (`git log --grep 're-verify(L68)'`); first PASS `650d00d4` retracted in `5517e09e` |
| 3 | L76 | ✅ PASSED `8b56eb32` |
| 4 | L70 (steps 1-2, optional re-run: already PASSED) | ✅ PASSED `38c7ac49` |
| 5 | L64 | ✅ PASSED red→green 2026-09-22 (`git log --grep 'verify(L64)'`) |
| 6 | L68 (hard-kill arm, last) | ✅ RE-VERIFIED red→green 2026-09-22 (`git log --grep 're-verify(L68)'`); first PASS `650d00d4` retracted in `5517e09e` |

### S2

**Setup:** no game. Offline build tree configured by build.ps1. Uses tools/verify/build_dll.py (never touches dist) and dotnet test. Commit before running any freshly built exe (Bitdefender).

**Why grouped:** Every staged DLL the later sessions need, built in one sitting. Stage each into out\staged\<name>.dll with its sha256, restore the source byte-exact (git diff empty, no NUL bytes), and finish with a clean build_dll.py rebuild so build\ matches HEAD. L89 (C# InvokeScriptTests plus dll_helpers_test through build_dll.py) is offline too.

| # | row (arm) | status |
|---|---|---|
| 1 | L89 | ✅ CLOSED 2026-09-22: C# half `b87915ea`, C++ half `git log --grep 'verify(L89)'` (Pass 2752 / Fail 0) |
| 2 | L55 (build the 'before' DLL: git apply -R of 20448583's Denken.cpp hunk) | ✅ PASSED 2026-09-22 — A/B IDENTICAL over 2,978 functions (`git log --grep 'verify(L55)'`) |
| 3 | L40/L52 (build the FORCE_GOBJ+FORCE_GNAM DLL) | 🔧 staged `out\staged\l40-l52-force-fallbacks` sha `bc740950` |
| 4 | L69/L85 (build the 'unmeasured:elemsize' DLL) | 🔧 staged `out\staged\l69-l85-unmeasured-elemsize` sha `f55d1745` |
| 5 | L41 (build step-1 enum-renamed DLL and step-2 one-shot Sleep DLL) | 🔧 staged step 1 `l41-step1-enum-names-unlocated` sha `8d11fbd9`; step 2 `l41-step2-first-search-stall` sha `c61978d9` |

### S3

**Setup:** DumperTest 5.4 SHIPPING, injected with dist DLL. Pipe rigs only: UI and CE closed. Two launches.

**Why grouped:** All are pipe or mailbox rigs on the house fixture and need the 3rd pipe slot or no UI. The order runs from least to most state-changing. L55's native-disasm census runs on a pristine process. L49 and L81 are additive and restore their bytes. L54's re-init restarts the pipe. L54 window B needs a fresh first init. L50's in-process version override poisons every version-derived path, so it goes last and that process is discarded.

| # | row (arm) | status |
|---|---|---|
| 1 | L55 (B-run: HEAD census on the fresh process) | ✅ PASSED 2026-09-22 — A/B IDENTICAL over 2,978 functions (`git log --grep 'verify(L55)'`) |
| 2 | L49 | ✅ PASSED red→green 2026-09-22 (`git log --grep 'verify(L49)'`) |
| 3 | L81 (pipe arms, write_mem U16/I16 then restore) | ✅ PASSED red→green 2026-09-22, DLL + C# (`git log --grep 'verify(L81)'`) |
| 4 | L56 (pipe half: search_properties + force_field/reset) | ✅ PASSED red→green 2026-09-22 incl. the CE Freeze count (`git log --grep 'verify(L56)'`) |
| 5 | L54 (window A: Shutdown→AutoStart re-init poke; relaunch afterwards) | ✅ PASSED red→green 2026-09-22, both windows (`git log --grep 'verify(L54)'`) |
| 6 | L54 (window B: fresh first-init poke) | ✅ PASSED red→green 2026-09-22, both windows (`git log --grep 'verify(L54)'`) |
| 7 | L50 (persist:false override to 502, then UI half; kill the process after) | ✅ PASSED red→green 2026-09-22 via the in-process 502 override (`git log --grep 'verify(L50)'`) |

### S4

**Setup:** DumperTest 5.4 SHIPPING with staged DLLs. One FRESH launch per DLL; CE for arms that need it.

**Why grouped:** The same fixture with non-dist DLLs. Each staged DLL needs a fresh process (per-process latches, one DLL per process). L69 step 2 and L85 arm 3 read the same staged verdict from one launch, one via dissect/C ABI and one via the mailbox helper. Confirm by SHA which DLL answered. Restore %COMPUTERNAME%.json after the old-DLL arm.

| # | row (arm) | status |
|---|---|---|
| 1 | L69 (step 2, 'unmeasured' DLL) | ✅ PASSED red→green 2026-09-23 (`git log --grep 'verify(L69)'`) |
| 2 | L85 (arm 3, same launch) | ✅ PASSED 2026-09-23, green only (red = arm 4) (`git log --grep 'verify(L85)'`) |
| 3 | L41 (step 1, enum-renamed DLL + AOT UI USMAP export) | ✅ PASSED red→green 2026-09-23 on the log WARN (`git log --grep 'verify(L41)'`) |
| 4 | L85 (arm 4, out\oldcontract build 3262; back up %COMPUTERNAME%.json) | ✅ PASSED 2026-09-23: false [dll-too-old], never true; also arm 3's stand-in red (`git log --grep 'verify(L85)'`) |
| 5 | L55 (A-run, 'before' DLL, pipe census; diff against S3's B-run) | ✅ PASSED 2026-09-22 — A/B IDENTICAL over 2,978 functions (`git log --grep 'verify(L55)'`) |

### S5

**Setup:** DumperTest 5.4 SHIPPING + AOT UI, no CE. Pipe preps with the UI closed first (L47 make, L48 locate, L61 V8_RebuildBigTable(2500), L60 list_all_functions total + V1a_GrowContainers(5000) + Spawn_Holders(200)). UI launch #1 is plain. L58's restart relaunches the UI WITH UE5DUMP_ALLFUNCS_LIMIT. L60's regression arm relaunches it without.

**Why grouped:** One UI-connected Shipping process serves every read-mostly UI row. The pipe preps run first, while all slots are free. The two snapshots for L65 also feed L70 step 3, and they give L59's A side ≥3 snapshots. State-changing rows come after the read-only ones: holds and pokes (restored), the 5-minute suspend, then the UI restarts that L58 and L60 need anyway. L63's inflation is late on purpose, and L79 reuses its 4,296-holder instance set to raise the odds of an inverting address pair. L57 kills the game, so it goes last.

| # | row (arm) | status |
|---|---|---|
| 1 | L73 | ✅ PASSED red→green 2026-09-23 (`git log --grep 'verify(L73)'`) |
| 2 | L47 | ✅ PASSED red→green 2026-09-23 (`git log --grep 'verify(L47)'`) |
| 3 | L56 (UI half) | ✅ PASSED red→green 2026-09-22 incl. the CE Freeze count (`git log --grep 'verify(L56)'`) |
| 4 | L71 | ✅ PASSED red→green 2026-09-23, real Cancel in both modes (`git log --grep 'verify(L71)'`) |
| 5 | L81 (UI half, optional) | ✅ PASSED red→green 2026-09-22, DLL + C# (`git log --grep 'verify(L81)'`) |
| 6 | L72 (optional re-run: already PASSED) | ✅ PASSED `0d182b15` |
| 7 | L65 (capture 2 snapshots first) | ⬜ |
| 8 | L70 (step 3: SPC on the new snapshot) | ✅ PASSED `38c7ac49` |
| 9 | L61 (step 1) | ⬜ |
| 10 | L48 (arm A Num poke, then arm B decryption; restore each) | ⬜ |
| 11 | L45 (variant A: suspend-tid, ≥305 s) | ⬜ |
| 12 | L58 (step 2: force F32@0 on a non-candidate, UI restart, gate off/on, Clear all) | ⬜ |
| 13 | L60 | ⬜ |
| 14 | L63 (inflate: V1a_GrowContainers(16384) + Spawn_Holders(4096)) | ⬜ |
| 15 | L79 (predict inversions on the inflated holder set, then header clicks) | ⬜ |
| 16 | L57 (step 1 kills and relaunches the game; step 2 Detect-kill in the new process) | ⬜ |

### S6

**Setup:** DumperTest58 (UE 5.8) SHIPPING (`launch_dumpertest.py shipping58`, inject by PID). Pipe rows first, then UI captures; a fresh launch with the staged Sleep DLL for L41 step 2.

**Why grouped:** The only fixture with 5.5+ intrusive and trailing-flag TOptionals (DumperTest58Actor) and with FNameData enum storage. L82, L84 and L86 are pipe-only and should run before the UI takes 2 slots. Side B for L59 has no snapshot DB yet (no snapshots.9651CFE40A244000.db), so it is staged here. L41 step 2 needs a never-enum-touched process, hence its own launch.

| # | row (arm) | status |
|---|---|---|
| 1 | L82 | ⬜ |
| 2 | L84 (flag byte 01→00→01, restored) | ⬜ |
| 3 | L86 (regression only) | ⬜ |
| 4 | L59 (pre-stage side B: UI connect, capture 3 snapshots) | ⬜ |
| 5 | L41 (step 2: fresh launch with the staged Sleep DLL, no UI, raw-pipe disconnect then reconnect walk) | ⬜ |

### S7

**Setup:** cross-game A↔B in ONE AOT UI session. A = DumperTest 5.4 Shipping (≥3 snapshots after S5); B = DumperTest58 Shipping (3 snapshots from S6).

**Why grouped:** The row needs a switch between two games in one UI session, with distinguishable class lists and overlapping snapshot ids. Step 3 uses Disconnect/Connect (Extra Scan is never offered on these fixtures). Step 4 is regression only, because pivot indexes are prebuilt. Run before S8 so A's pickers are not dominated by inflated snapshots.

| # | row (arm) | status |
|---|---|---|
| 1 | L59 | ⬜ |

### S8

**Setup:** DumperTest 5.4 SHIPPING, heavy snapshot inflation. Spawn_ManyComponents(20000)×k via the pipe with the UI disconnected. Snapshot 'Game objects only' OFF, 'Max size' Off, then 512 MB.

**Why grouped:** Both need the same inflated component population and big snapshots. Delete the inflated snapshots afterwards, and restore Max size / Game objects only / Type.

| # | row (arm) | status |
|---|---|---|
| 1 | L66 (steps 1-2: DB crosses 512 MB mid-capture; restart, reconnect, capture again) | ⬜ |
| 2 | L61 (step 2: ≥2,000,000-row fetch cap; count rows in sqlite BEFORE Run) | ⬜ |

### S9

**Setup:** DumperTest 5.4 SHIPPING + one Cheat Engine (AOBMaker plugin) attached to the Shipping child exe. UI closed except for the last two rows.

**Why grouped:** Every Shipping row that needs CE Lua. Fresh CE first (L85/L69 depend on a cold Lua state and the dissect dedup). L69 step 3's Disable/Enable re-init comes before L78, which changes pawn attachment and invoke timeout and must restore both. The two UI+AOBMaker rows go last. L80's sw3 arm freezes the game and must always be disarmed.

| # | row (arm) | status |
|---|---|---|
| 1 | L85 (arm 1: getOffsetsVerdict → true '') | ⬜ |
| 2 | L69 (step 1: C ABI verdict + dissect, no warn) | ⬜ |
| 3 | L69 (step 3: untick/re-tick init; mid-scan cmd=16 mailbox observation from Python) | ⬜ |
| 4 | L78 (manufactured attach, 200 ms invoke timeout persist:false + suspend-tid; restore) | ⬜ |
| 5 | L56 (Freeze arm, UE5_DEBUG=1, UI + AOBMaker) | ✅ PASSED red→green 2026-09-22 incl. the CE Freeze count (`git log --grep 'verify(L56)'`) |
| 6 | L80 (UI pushes Get GWorld; sw3 arm/disarm freezes the game; last) | ⬜ |

### S10

**Setup:** DumperTest 5.4 SHIPPING, NOT injected, with UE5Dumper.dll registered as a CE plugin (ce_plugin_register.py register), two fresh launches.

**Why grouped:** Needs the plugin registration and CE's 'Always force load modules' toggled on and back off. Both are persistent config and must be restored (unregister; force-load back to 0). It must be kept apart from every injected session.

| # | row (arm) | status |
|---|---|---|
| 1 | L53 | ⬜ |

### S11

**Setup:** DumperTest 5.4 DEVELOPMENT, staged FORCE DLL, cold hint cache (PE 6AAB48B710F29000 has no record today). No UI, no CE.

**Why grouped:** One uninterrupted staged cold scan answers both: L40's fallback tiers run to completion with no abort lines, and L52's Pass-2 refusals carry the HEAP text. It must be the FIRST dev launch. Back up %COMPUTERNAME%.json before and restore it after.

| # | row (arm) | status |
|---|---|---|
| 1 | L40 | ⬜ |
| 2 | L52 | ⬜ |

### S12

**Setup:** DumperTest 5.4 DEVELOPMENT (checked build) + one CE with AOBMaker + AOT UI. sw4 CE harness installed.

**Why grouped:** Dev is required: the 8-byte delegate access detector exists only with DO_CHECK, and UCheatManager (Debug Camera) only in non-Shipping. L42 and L43 share the actor, the sw4 harness and the CSX/CE XML exports. L77 reuses the same CE+AOBMaker setup and ends with the un-injected relaunch. The Shipping controls for L42/L43 can be run in S5 or S9.

| # | row (arm) | status |
|---|---|---|
| 1 | L42 | ⬜ |
| 2 | L43 | ⬜ |
| 3 | L77 (tick records, then relaunch un-injected and answer 'No' to CE's disable prompt) | ⬜ |

### S13

**Setup:** DumperTest 5.4 DEVELOPMENT through the b5 hardlinked proxy tree (D:\ZZProxyB5, fresh copy of dist\proxy\version.dll) + fresh CE.

**Why grouped:** Proxy mode with no scan is the only way to get 'probe-not-run' from a live DLL. Finish with b5_mailbox_race.py clean.

| # | row (arm) | status |
|---|---|---|
| 1 | L85 (arm 2: probe-not-run before any scan, then trigger_scan → true) | ⬜ |

### S14

**Setup:** DumperTest 5.4 DEVELOPMENT launched with --idle + one CE with AOBMaker + AOT UI. Keep Foreground OFF except for the control.

**Why grouped:** The idle-mode stall breaks every other row (all dispatches time out while unfocused), so it gets its own launch. The UI toggle stall and the CE toggle stall must be separate stalls.

| # | row (arm) | status |
|---|---|---|
| 1 | L83 | ⬜ |

### S15

**Setup:** UE423_Flying DEVELOPMENT (sw9 --engine 423 --keep); optional UE427_3rdPerson comparison run.

**Why grouped:** The only UProperty-mode host with a multicast delegate array (DelegatePadFixture, verified in the exe bytes). Summon needs a dev CheatManager.

| # | row (arm) | status |
|---|---|---|
| 1 | L46 | ⬜ |

### S16

**Setup:** EVERSPACE™ 2 (C: library, Steam running, save loaded), direct inject. Drop the 0D281EF60A6D5000 hint record after backing up %COMPUTERNAME%.json.

**Why grouped:** The only installed title whose install path has a character above 0xFF (™). L51 needs a hint-cache miss on a fresh launch. Once booted with a save, the same process can host the cross-object sparse-delegate probe for L48.

| # | row (arm) | status |
|---|---|---|
| 1 | L51 | ⬜ |
| 2 | L48 (step 1 listed half, only if DumperTest had no cross-object sparse triple) | ⬜ |

### S17

**Setup:** OCTOPATH TRAVELER via its deployed winmm.dll proxy (`steam.exe -applaunch 921570`, trigger_scan / Start Scan).

**Why grouped:** One UE4.18 proxy-mode launch covers the winmm load_mode and confirmed-proxy record (L62), UProperty-mode MulticastDelegateProperty scoring (L67), and the old-DB column migration (L66 step 3, because this exe's PE matches a DB without partial_reason). The PRAGMA must precede L62's first Connect, since SetEngineState opens and migrates the DB.

| # | row (arm) | status |
|---|---|---|
| 1 | L66 (step 3 pre-check: PRAGMA on snapshots.5EEB192C030CE000.db BEFORE the first Connect) | ⬜ |
| 2 | L62 | ⬜ |
| 3 | L67 | ⬜ |
| 4 | L66 (step 3 post-check after the UI closes) | ⬜ |

### S18

**Setup:** The Adventures of Elliot (dxgi proxy), fallback Avowed. Gameplay with a pawn.

**Why grouped:** Step 1 needs a title whose pawn-related objects carry a stealth-keyword float. DumperTest has none.

| # | row (arm) | status |
|---|---|---|
| 1 | L58 (step 1: probe find_stealth_meter over the pipe first; run only if it returns a candidate) | ⬜ |

### S19

**Setup:** Light Maze COPY on a SUBST drive (X:), AOT UI, no game running (optional; regression only).

**Why grouped:** The copy/SUBST/deploy/prune setup is self-contained and touches Proxy Deploy's persisted options. It is a regression re-run of an already-recorded PASS.

| # | row (arm) | status |
|---|---|---|
| 1 | L75 | ✅ PASSED `fa0a5439` (regression-only here: SUBST also refused pre-fix) |

## Not reachable on this machine as written

What the survey could not reach, and why. Each is a candidate for a fixture change (see the next section) before it is recorded as unreachable in `todo.md`.

| row | reason |
|---|---|
| L40 (natural trigger) | No installed title reaches the guarded GObjects/GNames recovery sweeps. Hogwarts Legacy (test-games.md:60) has no appmanifest in either Steam library. Avowed and EVERSPACE™ 2 are installed but resolve by AOB (hint records GOBJ_V13/GNAM_V5 for ES2; GOBJ_AV1 for Avowed). Reachable only by the staged FORCE DLL on DumperTest dev (S11). |
| L52 (literal atcuf64 shape) | GWLD_V3 always has main-exe hits, so it never reaches Pass 2, the only place a foreign module is refused. The atcuf64 refusal was seen only on python.exe, where GObjects never validates. The generalized HEAP-text check is reachable only staged (S11, EOSSDK/GNames). |
| L86 (page-edge discriminator) | This needs a 5.5+ class whose LAST reflected field is an intrusive TOptional<FName>, with instances ending within 16 bytes of an unmapped page. DumperTest58's Opt_Name_* are mid-object with a single instance, and no surveyed Steam title is known to carry the shape. Only the regression gate check (S6) is available. |
| L75 (discriminating half) | The fix's path needs a drive-root DRIVE_FIXED volume where GetVolumeNameForVolumeMountPointW fails but SHQueryRecycleBinW succeeds. On SUBST both fail, so the pre-fix code refused too. No such volume exists on this machine. |
| L46 (Phase J, unicast delegate array under UProperty mode) | No UE4 ≤ 4.24 host declares a TArray<FScriptDelegate>. A byte scan of UE423_Flying and UE427_3rdPerson finds 'Arr_MulticastDelegates' but no 'Arr_Delegates'. Only Phase K (multicast) is live. |
| L45 (step 3, -3 producer refusal) | ProbeProducers fails only when LineTraceSingle or SetActorHiddenInGame is cooked out, and no available title has that. It is covered by the listed TeleportViewModelTests/InvokeScriptTests pins. |
| L50 (literal UE 5.0–5.2 host) | Palworld (test-games.md:80) and the Satisfactory 5.2.1 depot build (:84) are OFFLINE ONLY, with no appmanifest or install. A correctly resolved 5.0–5.2 title would not discriminate anyway. The in-process 502 override on DumperTest Shipping is the reachable discriminator (S3). |
| L59 (step 4 live race) | Every populated snapshot DB already has pivot_index_built for all its snapshots, so class lists load from precomputed class_counts in milliseconds. It is regression-only unless a slow lazy build is manufactured by deleting pivot_index_built rows, which needs the maintainer's consent. |
| L55 (live discriminator) | By construction there is no live trigger: the removed guard was dead code. Only the A/B regression of the 'before' DLL against HEAD on the same Shipping package (S3/S4) plus the unit pin are available. |
| L89 | There is nothing observable on a running game. The offline pins (S2) are the whole check. |
| L41 (step 2 via a UI disconnect) | Successful detection stops at the first ENetRole within one monitor poll, so a UI disconnect cannot land in flight. Reachable only with the staged one-shot Sleep DLL and a raw pipe close (S6). |
| L78 (literal vehicle/mount host) | No installed title is known to have a vehicle or mount: Palworld and Hogwarts are absent. The parent-relative condition is fully manufactured on DumperTest Shipping (S9), as L27 and L37 did. |
| L69 (literal 'UE 5.8 fixture validated=NO') | DumperTest58 now logs validated=yes. The only validated=NO logs are two UnrealEditor injections from 2026-09-07. Step 2 needs the staged ELEMSIZE DLL (S4). |
| L58 (step 1, conditional) | It is reachable only if find_stealth_meter returns a candidate on Elliot or Avowed, which is unproven. DumperTest has no stealth-keyword float. If both probes return empty, step 1 is not reachable on anything installed. |
| L48 (step 1 listed half, conditional) | DumperTest's only sparse binding is self-bound, and Find Refs suppresses self-bindings (Aura.cpp:4053). It is reachable only if the `locate` storage walk (or ES2 with a save loaded) shows a cross-object binding whose target has fewer than 32 field refs. |
| L79 (conditional) | The check can fail only on an address pair of 10 vs 11 hex digits where the shorter address has the larger leading digit. This machine's recorded heap (11-digit) and module (12-digit 0x7FF…) addresses never invert. Any surface the pre-flight finds without an inversion must be recorded 'cannot fail on this data'. The FieldAddress columns cannot invert at all. |

## Fixture changes

Decided 2026-09-22. Sources in `tools/ue-sample/`; acceptance values in `tools/ue-sample/README.md`. ⚠ Each change needs a **repackage** (`py tools/ue-sample/repackage.py --engine <v> --project <p> --sync-mirror --configs ...`) before it exists in the binary. A repackage changes the exe's PE hash, so per-game state keyed on it (snapshot DBs, hint records, bookmarks) starts empty for the new package.

| row | change | fixture | status |
|---|---|---|---|
| L58 step 1 | `UDumperTestStealthComponent` (`StealthDetection`, a float the game rewrites every frame) attached to the player pawn from `ADumperTestActor::Tick`, registered as an instance component so the pawn's related-object walk reaches it | DumperTest 5.4 | source ✅ `4bed746a`; packaged ✅ `18083014` (all three configs; names checked in the exes) |
| L48 step 1 (listed half) | `UDumperTestSparseListener`: a rooted transient UObject bound to the actor's sparse `OnActorBeginOverlap`, referenced by nothing else. D4's `OnActorHit` self-binding is untouched | DumperTest 5.4 | source ✅ `4bed746a`; packaged ✅ `18083014` (all three configs; names checked in the exes) |
| L86 | `UDumperTest58OptTail` (64 bytes; intrusive `TOptional<FName>` as the LAST 8) plus `ADumperTest58Actor::OptTail_Spawn(MaxObjects, WantEdges)`, which spawns until enough instances end at an unreadable page, MEASURED per instance with `VirtualQuery`. Zero edges after the cap = still unreachable on that run | DumperTest58 | source ✅ `4bed746a`; packaged ✅ (Development + Shipping, 2026-09-22; names checked in the exes). ⚠ `repackage.py` knew only DumperTest's mirror until `06261090` |

**Considered and NOT changed:**

| row | why a DumperTest change does not help |
|---|---|
| L46 Phase J | Needs UE < 4.25 (UProperty mode); every DumperTest is 5.x. Its host would be the UE4 delegate fixture (`tools/ue-sample/ue4-delegate-fixture`), not in this scope |
| L79 | The inverting pair (10- vs 11-hex-digit addresses) is a property of the OS's address layout, not of any fixture |
| L59 step 4 | Needs a slow lazy pivot build; manufacturing one means deleting `pivot_index_built` rows from a snapshot DB — ask the maintainer, it is not a fixture change |
| L45 step 3 | Needs `LineTraceSingle` / `SetActorHiddenInGame` cooked out of the engine |
| L50 literal | Needs a UE 5.0-5.2 engine, not installed; the in-process 502 override on 5.4 is the reachable discriminator |
| L55 | The removed guard was dead code: no fixture can trigger it |
| L40 / L52 / L41 / L69 | Reached with STAGED DLLs (session S2), not fixture changes |
| L60 | Already reachable: `UE5DUMP_ALLFUNCS_LIMIT` for step 1; `V1a_GrowContainers(5000)` grows `Arr_Churn` past 4,096 for step 3 |
| L78 | A real vehicle adds nothing: the manufactured attachment (L27 / L37) already reaches the parent-relative condition |

## Corrections the survey made to its own scouts

| row | correction |
|---|---|
| L40 | The scout's 'DumperTest.exe (PE 6AAA12E610F27000 at present)' is wrong. The dev package was repackaged 2026-09-17 and its PE is 6AAB48B710F29000, which has NO hint record, so the first staged dev run is cold by itself. The backup/restore of UE5CEDumper.%COMPUTERNAME%.json still matters: the staged run creates a 6AAB48B7 record with data_scan hints, and later dev rows would inherit it. Build the staged DLL with `py tools/verify/build_dll.py --targets UE5Dumper`: it writes build\dll\UE5Dumper.dll and, per its docstring (build_dll.py:24-25), never touches dist\ or build_number.txt. Copy that to out\staged\ and restore Genau.cpp byte-exact. The rig's own build.ps1 -Target DLL route (genau_rip_recovery_ab.py:76) rewrites dist\UE5Dumper.dll plus all 4 proxies and must be undone. -Target DLL does NOT republish UE5DumpUI.exe (build.ps1:510/712 publish only for All/UI/Test). The FORCE_GNAM anchor still matches as a unique substring: the source comment now continues '— find FNamePool via code that uses FName' (Genau.cpp:2433), and sub() counts substrings (genau_rip_recovery_ab.py:120-121). |
| L52 | Same PE correction as L40: the current dev PE is 6AAB48B710F29000 with no record, so Pass 2 is reached only if L40/L52 run BEFORE any other dev row (L42/L43/L77/L83/L85-arm-2 would all write an AOB hint for 6AAB48B7). Run L40 and L52 together in the first dev launch, from the same FORCE_GOBJ+FORCE_GNAM staged DLL. The EOSSDK Pass-2 refusals the scout quotes come from the PREVIOUS package's 2026-09-16 cold scan, so treat them as a prediction for the new package. If no REFUSED line appears at all, record 'not reached', not a pass. |
| L54 | Add a trap, and a plausible residual defect. After UE5_Shutdown, g_cachedGObjects and g_cachedGNames stay non-zero. AutoStartWork sets initState=INIT_RUNNING (Frieren.cpp, just after Mimic::StartThread) before UE5_Init sets g_initInProgress (the InitInProgressScope just before 'Starting initialization...', Frieren.cpp:~158-165). A poke landing in that microsecond gap passes Mimic::InitFastPathOk(true,true,false) (Mimic.cpp:488) and answers from the stale globals with no waiting line. The 'poll initState==1 then poke' recipe can therefore log a false FAIL. Fire the poke only after 'UE5_Init: Starting initialization...' appears in init-0.log (tail it; the DLL flushes every line), or at least 50 ms after RUNNING. If a fast reply with no waiting line is ever seen, record it as this gap, with timestamps, rather than as the pre-fix behaviour. |
| L60 | Persisted options differ from the recipe. Array Limit is 2^6=64, not 2^7=128, so the SpawnedHolders control reads 'Showing the first 64 of 200 entries — raise the "Array Limit" slider…' (ContainerTruncation.cs:43-46). Interesting Funcs 'Game Only' is persisted OFF (interestingFuncs.gameOnly=false), so step 1a must TICK it first and restore false afterwards. The Arr_Churn arm is unaffected: scalar arrays are re-fetched up to the 4096 per-request cap whatever the slider says (LiveWalkerViewModel.cs:1369-1375, Ubel.cpp:42). |
| L61 | Restore pivot 'Source:' to the persisted 'Snapshot Array', not 'Snapshot' (ui-options pivot.selectedSource). The 2,000-row page claim is confirmed: DataTableRowLimit=2000 (ClassPivotViewModel.cs:82), and the DLL does not clamp walk_datatable_rows' limit (Fern.cpp:5459-5461; Ubel.cpp:7337 only bounds by `read < limit`). |
| L66 | (a) The per-game quota is currently UNLIMITED (experimental.json snapshotQuotaMb=0 → QuotaBytes 0, SnapshotViewModel.cs:514-515), so the FIFO-eviction trap does not fire at current settings. ES2's is_usable=0 snapshot #2 would still be deleted by the pre-capture auto-clean. (b) Step 3 is reachable on OCTOPATH in the L62 session: its current exe PE 5EEB192C030CE000 matches an existing DB without partial_reason. Run PRAGMA table_info before the first Connect. (c) The SPC finding is confirmed at source: SpcPanel.axaml:78-79 and :365 bind {Binding Label}, and SpcSnapshotPick.Label => Meta.Label (SpcQueryViewModel.cs:217). SPC shows neither the partial marker nor the '⚠' unusable prefix that LabelDisplay/PickerDisplay add (SnapshotModels.cs:50, :82). |
| L68 | Finding CONFIRMED: the recorded PASS does not discriminate. App.axaml.cs:172-177 ShutdownRequested calls FlushOptions (MainWindowViewModel.cs:2292-2296), which calls SaveOptionsNow → _uiOptions.Save(BuildOptions()) with no dirty gate (:2307-2311). A clean close therefore persisted the raised cap before the fix too. Re-run the discriminating form: mid-session file diff at least 1 s after the ▲ click (400 ms debounce), and/or taskkill /F then relaunch. |
| L51 | No VERSION.dll or dxgi.dll proxy is deployed in ES2\Binaries\Win64 today, so the 'proxy_refresh.py report first' precaution is moot. Step 2, which is regression-only, would need a proxy deployed into the non-ASCII tree first (or the b29 D:\測試 tree). Copy the pre-fix log scan-20260909-171715.log to out\ BEFORE this session; it expires around 2026-09-30. |
| L48 | ES2 has no stale proxy to rename (see L51), so inject directly with inject.py. Everything else stands. The per-delegate offsets line names the FMulticastScriptDelegate ADDRESS, not the delegate FName (Aura.cpp:4087-4090), and Find Refs on DumperTestActor_0 suppresses its own OnActorHit binding (Aura.cpp:4053). |
| L41 | Build both staged DLLs with build_dll.py, not build.ps1 -Target DLL (see L40), so dist stays byte-exact without a restore step. Step 2 needs a FRESH DumperTest58 process, and nothing may touch an enum before the raw walk (DetectUEnumNames is lazy, sole caller Ubel.cpp:132). Do not connect the UI in that launch. |
| L69 | Build the 'unmeasured' DLL with build_dll.py (dist untouched). One staged launch serves both L69 step 2 and L85 arm 3. The stale fixture claim is confirmed: no retained game log has validated=NO. |
| L85 | Arm 3 shares L69's staged launch. Arm 2's b5 hardlinked tree is DEVELOPMENT and has the same PE (6AAB48B710F29000) as dev, so its trigger_scan writes or uses the same hint record. Run it after the L40/L52 cold run. mailbox_addr.py already accepts the proxy module names (mailbox_addr.py:100-101), so the CE-free cross-check works in proxy mode as a library call. |
| L56 | propertySearch.gameClassesOnly is persisted OFF (not the code default ON), so step 1 needs no untick. Step 2 must TICK 'Game classes only' and then restore it to false. |
| L53 | Both findings are confirmed at source. The stale 'Details: Logs\UE5Dumper-*.log' appears in both dialogs (Methode.cpp:432, :442). The CE-hosted build-3315 UE5Dumper (2026-09-21) has no identifiable source: nothing under C:\Program Files\Cheat Engine or autorun references it. Record which DLL CE loads before step 1. |
| L58 | The finding is confirmed: _stealthState starts at "Off" (TeleportViewModel.cs:1785), while every other badge starts at "Unknown" (:587-826). Read the stealth badge only after the connect prime has finished. |
| L80 | Step 6 extraction works in principle: query()'s idle-wait locals (_idlePump/_idleTick) are emitted INSIDE query (PointerQueryScriptGenerator.cs:124-127 → CeLuaHygiene.cs:167-189), and it ends '  return a\nend' (:153-154). But `mb` is chunk-level (the loader must define it, as the recipe does), and CE may hold the script with CRLF. Normalise \r\n before matching, and dump B.Script to a file first to confirm the pattern. |
| L73 | There is no Bookmarks\bookmarks.09FF6E55081C9000.json, so there is nothing to back up. Slot 1 starts empty, and the first save is the non-discriminating empty→occupied case. The second save is the check. |
| L75 | The PASS is correctly qualified: SUBST refuses at the pre-fix gate too, so the fix's discriminating path (GUID lookup fails, bin works) has no volume on this machine. Treat any re-run as regression only. |
| L89 | The CLOSED note's 'only route is build.ps1 -Target Test' is wrong. `py tools/verify/build_dll.py --targets dll_helpers_test` builds it without touching dist (build_dll.py:24-25, :182, :212), and build\dll\dll_helpers_test.exe already exists (2026-09-16 12:31). The C++ half can therefore be re-run at HEAD. |

## Fixture availability, as checked by the survey

⚠ Installs change; re-derive with `py tools/verify/fixture_census.py` and the Steam `libraryfolders.vdf` before planning around a title. Never conclude a title is absent from a folder-name match.

| claim | verdict | evidence |
|---|---|---|
| Steam libraries on this machine | Two libraries, both enumerated | C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf has exactly two "path" entries: C:\Program Files (x86)\Steam and D:\SteamLibrary. The appmanifest_*.acf files in both were read. `py tools/verify/fixture_census.py` (read-only) found 11 ghost folders with no exe: FF7 Rebirth, Jedi Fallen Order, MH World, MH Rise, PRAGMATA, Romancing SaGa 2 RotS, Civ V, Tower of Mask, 3DMark, screenshots, Steam Controller Configs. |
| L40 fixture: Hogwarts Legacy (docs/test-games.md:60, 'GNames via pointer-scan fallback') | NOT installed according to the manifests | Neither library has an appmanifest for appid 990080, and no Hogwarts folder appears in the census. The other examples ARE installed but no longer reach the guarded tiers: Avowed (appmanifest_2457220, StateFlags 4) and EVERSPACE™ 2 (appmanifest_1128920, StateFlags 4). Their hint records (UE5CEDumper.%COMPUTERNAME%.json) show AOB winners: ES2 0D281EF60A6D5000 has GOBJ_V13/GNAM_V5. So L40 is reachable only by staging on DumperTest dev. |
| DumperTest dev current PE hash (L40/L52 say 6AAA12E610F27000) | WRONG: the current dev exe is 6AAB48B710F29000 and it has NO hint record | PE TimeDateStamp+SizeOfImage computed from D:\UE_Analyze_data\for testing\DumperTest\Development\...\DumperTest.exe (279,297,024 B, repackaged 2026-09-17) = 6AAB48B710F29000. %COMPUTERNAME%.json has no 6AAB48B7 record. 6AAA12E610F27000 (GNAM_V1, last scan 2026-09-16) belongs to the previous package. So the FIRST dev run on this machine is a cold scan by itself, and any earlier dev row will create a record that L52 then has to drop. |
| DumperTest variants available | 5.4 Dev/Shipping/DebugGame and 5.8 Dev/Shipping present; DumperTest56 also exists (undocumented) | Sizes match package-identity.json: Dev 279,297,024, Shipping 132,365,824, DebugGame 279,476,224. DumperTest58 Dev 332,198,400 and Shipping 166,090,752 (2026-09-16); Shipping58 PE 9651CFE40A244000 (record: UE 508, AOB). D:\UE_Analyze_data\For Testing\DumperTest56\{DebugGame,Development,Shipping} exist but are not in launch_dumpertest.py FLAVOURS. Shipping PE 09FF6E55081C9000 record: invokeTimeoutMsAt "", ueVersionUserOverrideAt "" (good for L78 and L50). |
| L46 UE4 delegate fixture (UE423_Flying / UE427_3rdPerson) | Present, and both carry the fixture | Both exes exist under D:\UE_Analyze_data\For Testing\{UE423_Flying,UE427_3rdPerson}\Development\WindowsNoEditor\...\Binaries\Win64. A byte scan found 'DelegatePadFixture' (utf-16le ×2) and 'Arr_MulticastDelegates' in each, and 'Arr_Delegates' in neither. That confirms Phase J (unicast array) has no host. UE423 PE 6AA0D2300691E000 matches its log folder. |
| L51/L48/L66 EVERSPACE™ 2 (docs/test-games.md:12) | Installed; CRC present; pre-fix red still on disk; NO proxy currently deployed | appmanifest_1128920 (C: library) has StateFlags 4. C:\...\common\EVERSPACE™ 2\Engine\Binaries\Win64\CrashReportClient.exe is present (27,922,432 B, 2026-09-02). ES2\Binaries\Win64 holds only D3D12, DML, ES2-Win64-Shipping.exe/.pdb and tbb*.dll, so there is no version.dll or dxgi.dll proxy (the L48 'rename the proxy aside' step is moot). Exe PE 0D281EF60A6D5000 matches a rev-7 hint record, so the record must be dropped. Logs\ES2-Win64-Shipping\scan-20260909-171715.log line 9 is the empty '[INFO] [SCAN:Ver] ' record (DLL 1.0.0.3483); with 21-day retention it expires around 2026-09-30. Snapshot DB 1,531,285,504 B has 2 snapshots, and #2 has is_usable=0. |
| L62/L67 OCTOPATH TRAVELER (docs/test-games.md:14, UE4.18, winmm) | Installed; post-fix winmm proxy deployed | appmanifest_921570 has StateFlags 4. Octopath_Traveler\Binaries\Win64\winmm.dll is 2,988,544 B, 2026-09-16 12:42, sha256 05a66930… (dist\proxy\winmm.dll is 46c94687…). Fix 723cb8f1 dates 2026-09-12 and the last dll/src commit (88632f29) is 2026-09-16 12:41. The folder also has ReShade (dxgi0.dll, ReShade.ini). Exe PE 5EEB192C030CE000 matches the hint record (UE 418, lowConfidence) AND snapshots.5EEB192C030CE000.db, which has user_version 4, 0 rows and NO partial_reason column. So L66 step 3 is reachable in the same Octopath session. ui-options: confirmedProxyByExe Octopath=2, lkgSuggestEnabled=false. |
| L67 other UE4 <= 4.22 titles | Installed: DQ XI S 4.18 (appid 1295510), NEKOPALIVE 4.11 (469990), EVERSPACE 4.20 (396750). Absent: FF7R 4.18 (no manifest), Jedi Fallen Order (ghost folder, no exe), Extinction (no manifest). Satisfactory 4.22 depot is OFFLINE ONLY. | From the appmanifest listing and the fixture_census ghost list. Satisfactory 4.22 depot build: test-games.md:82 'OFFLINE ONLY'. The installed Satisfactory (526870) is the current build (test-games.md:68/76, UE5.6). |
| L50/L78 Palworld (test-games.md:80), Satisfactory 5.2.1 depot (:84), Hogwarts (:60) | None is a live fixture here | No appmanifest for Palworld or Hogwarts. test-games.md rows 80 and 84 are marked OFFLINE ONLY (Ghidra projects). So L50 and L78 must use their DumperTest manufactured routes (in-process override; K2_AttachToActor). |
| L58 step-1 candidates: Elliot (test-games.md:93) and Avowed (:94) | Both installed; both carry our post-fix dxgi proxy | appmanifest_3483510 and appmanifest_2457220 both have StateFlags 4. Elliot\Binaries\Win64\dxgi.dll and Avowed\Alabama\Binaries\Win64\dxgi.dll are 2,973,184 B, 2026-09-16 12:42, and contain 'UE5Dumper'. TQ2 has the same. Whether find_stealth_meter returns a candidate is still unproven: probe first. |
| L75 Light Maze | Installed | appmanifest_2023910 has StateFlags 4, 0.21 GB, LightMaze.exe. There is no test-games.md entry (as the scout said), so availability rests on the manifest. |
| L69/L85 'UE 5.8 fixture says validated=NO' | STALE: no game fixture produces validated=NO; staging required | A grep of every Logs\*\ file finds 'validated=NO' only twice, both in UnrealEditor 2026-09-07 (reason=no-guid-or-vector-struct). DumperTest-Win64-Shipping init-0.log 2026-09-22 10:38 reads validated=yes. The staging bit UNMEASURED_PROP_ELEMSIZE exists at Genau.cpp:3915, and kUnmeasuredReason[8]='unmeasured:elemsize' is at Genau.cpp:4300. The dissect warn is at ue5_dissect.lua:526 and is reached via createFromPath → createFromClass (:604-610). |
| L41 DumperTest58 FNameData enum storage | Confirmed | Logs\DumperTest58-Win64-Shipping\offsets-*.log 2026-09-16 11:35 reads 'UEnum::Names detected at UEnum+0x40 (UE5.6+ FNameData, verified with 'ENetRole', count=5…)'. DetectUEnumNames is lazy: its only caller is Ubel.cpp:132. The candidates are still at Genau.cpp:5417-5419. |
| L53 CE plugin state | UE5Dumper is not registered; force-load is off; a stale 3315 build loaded into CE is unexplained | reg query HKCU\Software\Cheat Engine\Plugins64 shows only AOBMaker_CEPlugin.dll (enabled) and CE-Handwire.dll (disabled). 'Always Force Load' is REG_DWORD 0x0. Logs\cheatengine-x86_64-SSE4-AVX2\init-0.log 2026-09-21 08:01:58 shows 'UE5Dumper DLL loaded \| build: 1.0.0.3315'. No UE5Dumper*.dll exists under C:\Program Files\Cheat Engine, and autorun has no UE5Dumper reference. dll-path.txt names only D:\Github\UE5CEDumper\dist. |
| L85 arm 4 old DLL | Present | out\oldcontract\UE5Dumper.dll is 2,860,544 B, 2026-08-23. CMD 16 was unused before contract 5: 389d76bb^ Mimic.h stops at CMD_TIME=15. |
| dist binaries are current and AOT | Yes | dist\UE5DumpUI.exe is 57,718,784 B (AOT size class), 2026-09-17 09:51, after the last ui/ commit 35c99f2f (08:57). dist\UE5Dumper.dll is 2026-09-17 09:50, after the last dll/src commit 88632f29 (09-16 12:41). build_number.txt is 3546. Byte greps find every row's marker: UE5_TeleportGetPoseEx/MarkerEx/LastEx, UE5_GetOffsetsVerdict, array_elem_delegate_pad, 'toggle queued -- it will run' in the DLL; 'nothing to turn off', 'the toggle is QUEUED', 'Do not tick again', UE5_slotSymHolders, ' / InvocationList', 'partial: stopped at the size cap', 'class(es) hidden by the Diff denylist', 'EXISTS but no instance was free', 'TRUNCATED at the' in the exe. |
| L55 'before' DLL can be built from HEAD | Yes | `git show 20448583 -- dll/src/Denken.cpp \| git apply -R --check` reverses cleanly at HEAD. 20448583 is an ancestor of 943975f3, so L24's counts are not a pre-removal baseline. |
| L59/L65/L70/L57 snapshot preconditions | Shipping DB has 1 snapshot; DumperTest58 Shipping has NO DB yet; every populated DB has pivot indexes prebuilt | Read-only sqlite (immutable=1). snapshots.09FF6E55081C9000.db: 1 row, partial_reason present, pivot_index_built for 1/1. There is no snapshots.9651CFE40A244000.db, so side B needs 3 pre-staged captures. pivot_index_built covers every snapshot in the DBs that have rows (0D28 2/2, 5516 9/9, 6A7E 3/3, 6A8A 1/1, 6A9C 3/3), so L59 step 4's live window is milliseconds. |
| L66 step-3 old DBs (no partial_reason) | Many old DBs lack the column, but only an installed exe with the SAME PE hash can reach one | The column is missing in 03D2A471 (TQ2), 5EEB192C (Octopath), DDCE3EDE (Avowed), ED3D085C (Solarpunk), 6AA0D117 (UE427), 0146704D, 083BA527, 0CAB57A7, 1A40B98A, 4720D6A8, 62E78F5C, 67CA8EE0, 691B0D98, 69BB84C7, 69CE3439, 6A577F4E, 6A7328DF, 998ED285, E1AAB613, FBD525DD, plus the retired DumperTest 6A7EA603/6A8AA8DF/6A9C1C84 (those have rows). Octopath's current exe PE was verified equal to 5EEB192C030CE000. |
| Persisted UI options the recipes depend on (ui-options.json, read 2026-09-22) | Several differ from the scouts' assumed defaults | main.arrayLimitExponent=6 (64, not 128). interestingFuncs {gameOnly:false, showAll:true}. interestingProps {gameOnly:true, showAll:false}. propertySearch {gameClassesOnly:false, cap 2000}. gameClassFilter.classListCap=5000. valueSearch {gameOnly:false, maxResults:50000, deepScan:true}. pivot.selectedSource='Snapshot Array'. snapshot {gameOnly:true, autoSkipNoise:false, selectedMaxDataset:'Off'}. experimental.json {enabled:true, snapshotQuotaMb:0}: 0 = Unlimited (SnapshotViewModel.cs:514-515, ExperimentalSettings.cs:29). csxDrilldownDepth=4, fabricateArrayCountExponent=2, descShowOffset=true. system.autoCompressLogs=false. There is no Bookmarks\bookmarks.09FF6E55081C9000.json, so L73's slot 1 starts empty. |

-----

## Per-row recipes

### L40 — `[P1-GENAU-ABORT]` `[A2-GNAMES-PTRSCAN-ABORT]`

**Status:** ⬜ · **reachability:** `fixture-limited` · **needs CE:** no · **needs UI:** no · **estimate:** 60 min

**Fix commit(s):** `785b1730`, `89dc858c`

**Fixture:** No title on this machine reaches the guarded sweeps at HEAD. Every retained scan log (21-day window, all process folders under %LOCALAPPDATA%\UE5CEDumper\Logs) resolves GObjects and GNames by AOB or hint, and none contains 'Recovery', 'DataScanGObjectsCandidates', 'FindGObjectsStaticStruct', 'FindGNamesByStringRef' or 'FindGNamesByPointerScan'. The row's own examples are stale. docs/test-games.md:94 (Avowed) is now GOBJ_AV1 by AOB (Avowed-Win64-Shipping/scan-0.log 2026-09-06: 'winner: GOBJ_AV1 (hint)'), so the Count==0 decoy recovery never runs there. docs/test-games.md:12 (EverSpace 2, 'GNames via pointer-scan fallback') is a build-1.0.0.27 claim, and ES2's scan-0.log (2026-09-17) shows GNAM_V5 by AOB. docs/test-games.md:60 (Hogwarts Legacy, 'pointer-scan fallback', also from build 27) is the only entry still claiming a fallback tier; it is unmeasured at HEAD and a later stage must check whether it is installed. REACHABLE BY STAGING: DumperTest DEVELOPMENT with tools/verify/genau_rip_recovery_ab.py's two substitutions, FORCE_GOBJ (:78-91) and FORCE_GNAM (:93-102). They force ScanForTarget's result to 0 in FindGObjects and FindGNames, so the data-scan tier (DataScanGObjectsCandidates via FindGObjectsByDataScan) and the string-ref tier (plus the pointer-scan tier if string-ref fails) run uninterrupted. verification-register.md:4018-4058 measured this at build 3313: both fallback lines present, GNames identical to the AOB baseline, GObjects a HEAP false positive (583 or 2,556,928 objects).

**Preconditions:** (1) A staged DLL built from HEAD: the old out/genau/recovery_post.dll is build 3313 and predates 785b1730, so it cannot test the fix. (2) Back up %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.%COMPUTERNAME%.json. A staged run saves data_scan/string_ref hints for DumperTest.exe (PE 6AAA12E610F27000 at present). (3) One game at a time, and no UI or CE attached mid-scan: the control needs an UNINTERRUPTED scan. (4) Afterwards restore dist\UE5Dumper.dll byte-exact (rebuild from the clean tree and compare SHA), per working-lessons §2.11.

**Steps:**

1. Apply FORCE_GOBJ and FORCE_GNAM from genau_rip_recovery_ab.py:78-102 to dll/src/Genau.cpp. They still match HEAD: Genau.cpp:1665-1672 and :2431-2435. Build with `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Target DLL -NoBumpBuildNumber`, then copy dist\UE5Dumper.dll to out/ as the staged artifact. Optional extras: force FindGNamesByStringRef() to return 0 to reach the pointer-scan tier (Genau.cpp:2441-2443), and force `if (Aura::GetCount() == 0)` at Frieren.cpp:273 true to also run FindGObjectsStaticStruct and CollectGObjectsCandidates.
2. Restore Genau.cpp byte-exact (git diff must be empty) and rebuild, so dist holds the real DLL again. Alternatively run the whole existing rig, `py tools/verify/genau_rip_recovery_ab.py`, which stages, builds, runs DumperTest Development three times and restores in a finally block. Read the 'post' run's logs from the archived DumperTest\*-YYYYMMDD-HHMMSS.log files.
3. `py tools/verify/launch_dumpertest.py dev`, then `py tools/verify/inject.py --name DumperTest --dll out/<staged>.dll`. Injected mode auto-starts UE5_Init on the unbound DllMain auto-start thread (Frieren.cpp:867-927), the same Tot semantics as RunScan.
4. Wait for READY (`py tools/verify/pipe_client.py get_pointers`; object_count must be non-zero). Touch nothing during the scan.
5. Grep %LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest\scan-0.log and init-0.log for the expected lines below, and confirm NONE of the abort lines appear.
6. Kill the game. Restore the hint-cache JSON from the backup. Verify dist\UE5Dumper.dll matches the pre-run SHA, and that dist\UE5DumpUI.exe was not republished (size/SHA unchanged).

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| FindGObjects: All patterns failed, trying data-section scan fallback... | dll/src/Genau.cpp:1672 | scan-0.log [SCAN:GObj]. Anti-vacuity: proves the guarded tier ran. |
| FindGNames: All patterns failed, trying string-ref fallback... | dll/src/Genau.cpp:2435 | scan-0.log [SCAN:GNam]. Anti-vacuity for the GNames tier. |
| FindGNames: String-ref failed, trying pointer scan fallback... | dll/src/Genau.cpp:2441 | scan-0.log. Only if the optional string-ref staging is applied: it is what reaches A2-GNAMES-PTRSCAN-ABORT's sweep. |
| UE5_Init: Complete (UE504, GObjects=0x…, GNames=0x…, Objects=…) | dll/src/Frieren.cpp:607 | init-0.log [INIT]. The latch happened. |
| MUST BE ABSENT: 'DataScanGObjectsCandidates: aborted (client gone / shutdown)' / 'FindGObjectsStaticStruct: aborted (client gone / shutdown)' / 'FindGNamesByPointerScan: aborted (client gone / shutdown)' / 'FindGNamesByStringRef: aborted (client gone / shutdown)' | dll/src/Genau.cpp:568, :755, :2145, :2329 | scan-0.log |
| MUST BE ABSENT: 'UE5_Init: scan was cancelled (%s) — results are partial, NOT latching initialized so the next enable re-scans' | dll/src/Frieren.cpp:599-601 | init-0.log |
| MUST BE ABSENT: 'FindAll: scan was CANCELLED — NOT writing the hint cache' and '[GNames] multi-module fallback CANCELLED (client gone / shutdown)' | dll/src/Genau.cpp:5361; dll/src/Genau.cpp:1444-1445 | scan-0.log |

**Row text vs source:** (a) The row's fixture examples no longer hold. Avowed resolves GObjects by AOB (GOBJ_AV1) and never enters the recovery path. EverSpace 2's 'pointer-scan' claim is from build 27, and at HEAD it resolves GNAM_V5 by AOB. (b) 'A disconnect never cancels UE5_Init' is true for the UI's own lanes, but it is not a property of the code. When ANY connection dies with a command in flight, Fern::MonitorLoop also sets the process-wide g_perCommand (Fern.cpp:866-874, whose comment names RunScan/RunRescan as consumers), and the unbound RunScan thread reads it (Tot.h:179-180). A pipe rig holding a second connection in flight for several 200 ms polls during a proxy-mode trigger_scan could therefore reach the abort half. Unverified: no command is known that can be held in flight before init. The row scopes the abort half to dll_core_test anyway. (c) 'the monitor sets the per-command flag only for a connection with a command IN FLIGHT' is accurate (Fern.cpp:815-826).

**Rig:** EXISTING: tools/verify/genau_rip_recovery_ab.py holds the exact staging substitutions (it builds post/pre DLLs, runs DumperTest Development via inject.py, restores the source in a finally block and rebuilds). It asserts that the fallback lines are present (:227-229 of the rig) but NOT the absence of abort lines or the latch. Add a post-hoc grep of the post run's archived scan/init logs for the MUST-BE-ABSENT strings plus 'UE5_Init: Complete ('. Smallest new rig: reuse the rig's sub()/build() and FORCE_* constants, run one host with the staged DLL, and assert (fallback line present) AND (no 'aborted (client gone' line) AND ('UE5_Init: Complete (' present) AND (no 'scan was cancelled').

**Traps:** The staged DLL is built with -NoBumpBuildNumber, so pipe_client.assert_build() cannot tell it from the real one. Identify it by SHA, and prove dist was restored (working-lessons §2.11: restore bytes, not the substitution). The staged GObjects is a HEAP false positive (register :4047-4058), so compare methods and lines, never addresses or counts. Its count is non-zero, so UE5_Init's Count==0 recovery (FindGObjectsStaticStruct / CollectGObjectsCandidates, Frieren.cpp:273-308) does NOT run unless separately forced. A staged run writes hints into UE5CEDumper.%COMPUTERNAME%.json: back it up and restore it. -Target DLL also rebuilds the 4 proxies in dist\proxy. Row-scope trap: do not connect or disconnect the UI during the scan, because the control must be uninterrupted. Hint cache: FORCE_* overrides the result after ScanForTarget, so hints do not block the tiers here.

**Related rows:** L52, L41, L69, L85

### L41 — `[P1-ENUMNAMES]`

**Status:** 🟡 step 1 ✅ red→green 2026-09-23 (`git log --grep 'verify(L41)'`); step 2 ⬜ (S6) · **reachability:** `fixture-limited` · **needs CE:** no · **needs UI:** yes · **estimate:** 90 min

**Fix commit(s):** `7e5a71fc`, `defcb56e`

**Fixture:** STEP 1 (Names NOT located): no title here fails. No retained offsets log contains 'DetectUEnumNames: FAILED', and every detection seen succeeds (ENetRole at UEnum+0x40/0x48/0x50, including DQ XI S's fork at 0x50). Step 1 needs a staged DLL. Rename the three candidates at Genau.cpp:5417-5419 ('ENetRole', 'EObjectFlags', 'EPropertyFlags') to names that do not exist, on DumperTest Shipping (`launch_dumpertest.py shipping`). The full search then runs to completion and latches FAILED. STEP 2 (Names located, cancelled mid-search): a UI disconnect is a timing lottery. The success path stops at the first ENetRole (ForEach callback returns false, Genau.cpp:5430-5436). tools/verify/multipipe_cancel_isolation.py measured that the monitor needs a command in flight across SEVERAL 200 ms polls (0.5 s was not enough), and list_enums takes 0.09 s on DumperTest. Deterministic route: a staged DLL with a one-time Sleep(3000) right after 'DetectUEnumNames: Searching…' (Genau.cpp:5406). The discriminating fixture for the reconnect half is UE 5.6+ with FNameData enum storage: DumperTest58 Shipping (`launch_dumpertest.py shipping58`; its offsets-0.log 2026-09-16 reads 'UEnum::Names detected at UEnum+0x40 (UE5.6+ FNameData …)'). The pre-fix poisoning read with the DEFAULT Legacy format, which is only wrong there. On 5.4 the default is often right, so the reconnect half is vacuous on DumperTest 5.4. DumperTest58Actor inherits AActor's enum fields (Role/RemoteRole etc.).

**Preconditions:** Two separate staged builds from HEAD (step 1 = candidates renamed; step 2 = one-shot Sleep), each restored byte-exact afterwards. Both need a FRESH game process, because bUEnumNamesDetected/Failed latch per process (Genau.cpp:5402-5403, :5520-5521). The UI must be the AOT build (`build.ps1 -Mode Publish -NoBumpBuildNumber`), because the final status is UI code.

**Steps:**

1. STEP 1 build: rename the candidate names at Genau.cpp:5417-5419 and build `-Target DLL -NoBumpBuildNumber`. Keep the staged DLL, restore the source and rebuild.
2. Launch DumperTest Shipping and inject the staged DLL (`py tools/verify/inject.py --name DumperTest-Win64-Shipping --dll <staged>`). Anti-vacuity over the pipe: `py tools/verify/pipe_client.py list_enums`. The reply must carry `enum_names_failed: true` (Fern.cpp:2355), and offsets-0.log must carry the FAILED line.
3. Start the AOT dist\UE5DumpUI.exe, connect, then Export (toolbar DropDownButton str.Toolbar.Export) → 'USMAP (.usmap)' (str.Export.Usmap, en.axaml:812; MainWindow.axaml:284-285 → ExportUsmapCommand). Save to the scratchpad through the file dialog (computer-use).
4. Read the status bar. It must read the full warning below, not a bare 'USMAP exported'. Read Logs\UE5DumpUI\view-0.log (and Logs\DumperTest-Win64-Shipping\ui-view-0.log) for the WARN line.
5. STEP 2 build: insert `static std::atomic<bool> s_l41Once{false}; if (!s_l41Once.exchange(true)) Sleep(3000); // STAGING` after Genau.cpp:5406. Build, keep the DLL, restore the source.
6. `py tools/verify/launch_dumpertest.py shipping58` and inject the staged DLL. No UI. Resolve an actor with pipe_client (find_instances class_name DumperTest58Actor).
7. Open a RAW pipe connection, send walk_instance on that actor (the first enum-bearing lookup in the process), wait about 1.0 s (detection is in its staged sleep and the command is in flight), then close the handle. pipe-0.log must show 'PipeServer: client gone mid-command'.
8. Check offsets-0.log for the 'search cancelled … not latching FAILED' line. It may repeat once per later enum field of the dead walk, because each re-enters detection with the flag still set. There must be NO 'DetectUEnumNames: FAILED' line.
9. Reconnect with a new PipeClient and walk_instance the same actor. offsets-0.log must now show 'UEnum::Names detected at UEnum+0x40 (UE5.6+ FNameData …)'. The enum fields the dead walk touched (Role / RemoteRole …) must carry a non-empty `enum_name` and `enum_entries` (Fern.cpp:1702-1710), not an empty table.
10. Kill the game each time. Restore dist and verify the SHAs of dist\UE5Dumper.dll and dist\UE5DumpUI.exe.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| DetectUEnumNames: FAILED — no known enums found or validated (will not retry; enum names will show as raw values) | dll/src/Genau.cpp:5524-5525 | offsets-0.log [DYNO:Enum] (step 1) |
| USMAP exported — ⚠ enum member names are unavailable on this build (UEnum::Names was not located), so every enum in the .usmap is empty | ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:3693 + ui/UE5DumpUI/Services/UsmapExportService.cs:102-103 | UI status bar after the export (final status, not a progress line) |
| USMAP export: ⚠ enum member names are unavailable on this build (UEnum::Names was not located), so every enum in the .usmap is empty | ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:3695 | Logs\UE5DumpUI\view-0.log (WARN; view is the default category, LoggingService.cs:140) and the mirror Logs\<Process>\ui-view-0.log |
| "enum_names_failed": true | dll/src/Fern.cpp:2355 | list_enums pipe reply (step 1 anti-vacuity) |
| DetectUEnumNames: search cancelled (client gone / shutdown) -- not latching FAILED; the next enum lookup retries | dll/src/Genau.cpp:5513-5514 | offsets-0.log [DYNO:Enum] (step 2) |
| PipeServer: client gone mid-command (err=…) — aborting in-flight op | dll/src/Fern.cpp:861 | pipe-0.log. Anti-vacuity that the disconnect was seen in flight. |
| UEnum::Names detected at UEnum+0x40 (UE5.6+ FNameData, verified with 'ENetRole', …) | dll/src/Genau.cpp:5498 | offsets-0.log after the reconnect walk (step 2) |
| MUST BE ABSENT: 'GetEnumEntries: UEnum 0x… has no cached table — truncated read, retry pending' | dll/src/Ubel.cpp:252 | walk-0.log. Both steps: silenced under the latch (Ubel.cpp:238-241) and after a cancelled detection (:242-245). |

**Row text vs source:** (a) Step 2's 'a class with many enum fields' does not widen the window. Detection runs once, at the FIRST enum lookup, and stops at the first ENetRole. Later enum fields only matter AFTER a cancelled detection (each retries, Ubel.cpp:131-140). A UI disconnect is caught only while a command spans several 200 ms monitor polls (Fern.cpp:811-826; multipipe_cancel_isolation.py docstring), which a successful detection never does. So the step as written is not practically reachable without staging. (b) The step 1 quote matches source (the full text ends '…so every enum in the .usmap is empty'). The step 2 quote matches, but the source uses ASCII '--', not an ellipsis. (c) 'the DLL log' is specifically offsets-0.log (DYNO:Enum → LF_Offsets, Sein.cpp:70).

**Rig:** No existing rig for this tag. Step 1: UI export via computer-use, plus a pipe pre-check with `py tools/verify/pipe_client.py list_enums`. Step 2: new rig, about 60 lines. Use pipe_client.PipeClient for find_instances. Then a raw CreateFileW on \\.\pipe\UE5DumpBfx, write the walk_instance JSON line, sleep 1.0 s, CloseHandle. Then a fresh PipeClient walk_instance, and grep the three log files. tools/verify/multipipe_cancel_isolation.py is the precedent for 'kill a connection in flight and prove the monitor saw it' (it fails loudly when 'client gone mid-command' is absent; copy that anti-vacuity rule).

**Traps:** AOT: the final-status text is UI code, so hand over and test the -Mode Publish binary. Any build that reaches publish overwrites dist\UE5DumpUI.exe non-trimmed (CLAUDE.md). The per-process latch means every attempt needs a fresh game. On DumperTest 5.4 the reconnect half cannot discriminate (the default Legacy offset is right there), so use DumperTest58. The UI takes 2 of the 3 pipe slots (Fern.h:51 kMaxPipeInstances=3), so run step 2 with no UI attached. The file dialog and the game window steal focus: front windows with tools/verify/front_window.py. The UI status strings are hard-coded in MainWindowViewModel.cs, not en.axaml, so resolve them from the .cs, not the resource file. Restore the staged sources byte-exact (working-lessons §2.11).

**Related rows:** L39, L40, L52, L12, L82, L86

### L42 — `[A4-PUSHCE-UNPADDED]` `[W5-CSX-DELEGATEPAD]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** yes · **needs UI:** yes · **estimate:** 60 min

**Fix commit(s):** `6ffd827a`

**Fixture:** DumperTest 5.4 **Development** (`py tools/verify/launch_dumpertest.py dev`; exe D:\UE_Analyze_data\for testing\DumperTest\Development\Windows\DumperTest\Binaries\Win64\DumperTest.exe, PE hash 6AAB48B710F29000). Here dev is the REQUIRED subject, not a second opinion: the 8-byte access detector exists only with DO_CHECK on (tools/verify/sw4_ce_delegate_pad.py:14-18, d4b_delegate_pad.py docstring). ADumperTestActor offers both delegate shapes, both BOUND in BeginPlay: `Multicast_Inline` (MulticastInlineDelegateProperty, DumperTestActor.h:657, AddDynamic at DumperTestActor.cpp:312) and `Del_Unicast` (DelegateProperty, DumperTestActor.h:658, BindDynamic at .cpp:313). Control = DumperTest **Shipping** (`launch_dumpertest.py shipping`), where the pad is 0.

**Preconditions:** dist is current AOT: dist\UE5DumpUI.exe 57,718,784 B (2026-09-17 09:51) and dist\UE5Dumper.dll (2026-09-17 09:50), both built after the last dll/src + ui/UE5DumpUI change (35c99f2f, 2026-09-17 08:57); byte-grep of the exe finds " / InvocationList" (UTF-16) and the DLL has array_elem_delegate_pad, so the fix is in the binaries (build_number.txt still reads 3546 because the rebuild used -NoBumpBuildNumber, so the number alone cannot prove it). Exactly ONE Cheat Engine running with the AOBMaker plugin, attached to DumperTest.exe (dev), and the UI toolbar reading 'AOBMaker Connected'. The UI must be disconnected whenever a pipe_client-based rig runs.

**Steps:**

1. 1. `py tools/verify/launch_dumpertest.py dev` then `py tools/verify/inject.py --name DumperTest`. With the UI NOT connected, `py tools/verify/pipe_client.py get_object_count` must be about 24k (a dead engine gives coherent zeros, handover §3).
2. 2. Precondition (UI disconnected): `py tools/verify/d4b_delegate_pad.py` must PASS at pad 8. That proves the build is checked and the readers are right. Note the first non-Default__ DumperTestActor address and the `offset` / `delegate_pad` of Multicast_Inline and Del_Unicast from `py tools/verify/pipe_client.py walk_instance --args '{"addr":"<addr>","class_addr":"<class_addr>","array_limit":8}'`.
3. 3. Install the CE resolution harness: `py tools/verify/sw4_ce_delegate_pad.py install`, then (re)start Cheat Engine ONCE (autorun is read only at CE start). Attach it to DumperTest.exe (process icon, then Open, then answer Yes to 'Keep the current address list?'). Read the title bar.
4. 4. Start the AOT UI (`py -c "import subprocess,pathlib; subprocess.Popen([str(pathlib.Path('dist/UE5DumpUI.exe').resolve())], cwd='dist')"`) and Connect. Open the SAME DumperTestActor address in Live Walker: the Instances tab, or paste the address into the Live Walker address box and press Go.
5. 5. Per-row reference: press the '+CE' button (str.LiveWalker.AddToCe, en.axaml:293) in Multicast_Inline's address cell. The status reads 'Added to CE: Multicast_Inline'.
6. 6. Batch push under test: select the Multicast_Inline row (Del_Unicast too, optionally) and press '+CE Field (flat)' (en.axaml:296, LiveWalkerPanel.axaml:202-208). The status reads 'Added to CE: 1 record(s)' (or 2). view-0.log gets 'CE Field push: 1 added, 0 failed, 0 skipped (of 1 selected)'.
7. 7. Press Disconnect in the UI, then run `py tools/verify/sw4_ce_delegate_pad.py check --field Multicast_Inline`. BOTH Multicast_Inline records (per-row and batch) must resolve to the padded side (field + 8), '[padded] = 0x… <- InvocationList::Data' must be non-zero, and the verdict must be 'SW4: PASS'. Repeat with `--field Del_Unicast` for the unicast: padded = field + 8, which is its FWeakObjectPtr.
8. 8. CSX: open the ⚙ Options flyout (str.Toolbar.Options) and set 'Drill Depth:' to 1 (persisted value is currently 4; restore it). Reconnect. In Live Walker, choose 'Export CSX' then 'CSX — Pre-CE 7.7 (Byte)'. A native save dialog appears: save to out\l42\DumperTestActor_dev.CSX. view-0.log gets 'CSX (Pre-CE 7.7) exported to <path> for DumperTestActor'.
9. 9. Parse the .CSX offline (read-only; ElementTree). Element Description="Del_Unicast" must have Offset = walk offset + 8. Description="Multicast_Inline" (the raw block) must have Offset = field offset. Description="Multicast_Inline / InvocationList" must have Offset = field offset + 8, Vartype="Pointer" and a child <Structure>.
10. 10. In CE, open Memory View, then Tools → 'Dissect data/structures', then File → 'Import' (StructuresFrm2.lfm:121) and load the .CSX. Set the group address to the actor and expand 'Multicast_Inline / InvocationList'. It must open a child whose [0] element holds a non-zero FWeakObjectPtr (the bound actor).
11. 11. CONTROL on Shipping: kill the game, CE and UI and confirm they are gone. `launch_dumpertest.py shipping` + inject, then repeat steps 4-9. walk_instance carries no delegate_pad. Both +CE records sit at FieldAddress. The CSX has Del_Unicast at the field offset, and the Multicast raw block and '/ InvocationList' BOTH at the field offset. sw4 `check` REFUSES on Shipping by design (delegate_pad 0, sw4:120-124), so compare the dumped addresses by hand.
12. 12. Clean up: `py tools/verify/sw4_ce_delegate_pad.py uninstall` (CE keeps the timer until it restarts), restore Drill Depth to 4, and kill the game, CE and UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| +CE Field (flat) | ui/UE5DumpUI/Resources/Strings/en.axaml:296 | Live Walker toolbar row 2 button, visible with a selection, enabled only while AOBMaker is available (LiveWalkerPanel.axaml:202-208) |
| Added to CE: {ok} record(s){extra} | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:5139 | Live Walker status line after the batch push |
| CE Field push: {ok} added, {fail} failed, {skipped} skipped (of {selected.Count} selected) | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:5141 | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log (mirror: Logs\DumperTest\ui-view-0.log) [INFO] |
| StripHexPrefix(field.PayloadAddress)  (the batch push now sends PayloadAddress = FieldAddress + DelegatePad) | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:5117; LiveFieldValue.cs:636-647 | the address of the CE record (sw4 dump addr=) |
| Added to CE: {name} | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:5887 | status after the per-row +CE (the reference address, :5856) |
| {description} / InvocationList  (Vartype Pointer, at offset + field.DelegatePad) | ui/UE5DumpUI/Services/CsxExportService.cs:279-280 | the exported .CSX Element Description attribute |
| EmitElementRaw(sb, offset + field.DelegatePad, ...) for DelegateProperty | ui/UE5DumpUI/Services/CsxExportService.cs:266-271 | the .CSX Element Offset for Del_Unicast |
| CSX ({formatLabel}) exported to {filePath} for {CurrentClassName} | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:4777 | Logs\UE5DumpUI\view-0.log [INFO] |
| SW4: PASS -- Cheat Engine resolved the pushed %s record to 0x%X, which is InvocationList::Data (field + delegate_pad %d), NOT the access detector at 0x%X. | tools/verify/sw4_ce_delegate_pad.py:195-198 | rig stdout |

**Row text vs source:** None in the row's claims: batch = per-row = FieldAddress + 8 (LiveWalkerViewModel.cs:5117 and :5856 both use PayloadAddress), the unicast leaf moves to +8, and the multicast raw block stays at the field offset with its pointer at +8 (CsxExportService.cs:263-282). The row does not say the pointer element is emitted only for a multicast with a read binding at drill >= 1 (CsxExportService.cs:272-273). Doc-only drift, not blocking: the comment at CeXmlExportService.cs:4215-4218 says a multicast's element stride is '24 in a checked build', but the invocation-list elements are the never-padded NotChecked variant (DumperTestActor.h:641-644; Ubel.h arrayElemDelegatePad comment), and d4b expects 16.

**Rig:** EXISTING: `tools/verify/sw4_ce_delegate_pad.py install | check --field Multicast_Inline | uninstall`, plus its CE autorun harness tools/verify/ce/ue5-sw4-record-dump.lua. It dumps each CE record's CurrentAddress to out/sw4/sw4-records.txt every 2 s and compares that with the padded and unpadded candidates it derives over the pipe. Because it lists EVERY record whose description contains the field name, it shows the per-row and batch records side by side, and that side-by-side view is the row's 'equals the per-row +CE's' check. `tools/verify/d4b_delegate_pad.py` is the precondition. NEW (small, read-only): a ~30-line ElementTree parser that loads the saved .CSX and asserts the three Element Offsets against the walk_instance offsets and delegate_pad. The CE Structure-Dissect 'expands' check and all UI clicks need computer-use.

**Traps:** Never run pipe_client, sw4 `check` or d4b while the UI is connected: the UI holds 2 of the kMaxPipeInstances=3 lanes (handover §4.4). sw4 `install` writes into C:\Program Files\Cheat Engine\autorun\custom (writable without elevation; inert without its job file) and needs a CE RESTART. Use open_application only once, because CE is not single-instance; use `py tools/verify/front_window.py front cheatengine`. sw4 picks the FIRST non-Default__ DumperTestActor (working-lessons §1.y), so the Live Walker must be on the same address. '+CE Field (flat)' is disabled unless AOBMaker is Connected. Drill Depth is persisted (ui-options.json main.csxDrilldownDepth = 4): set it to 1 and RESTORE it. The '/ InvocationList' element exists only when drill >= 1 AND the multicast has >= 1 read binding (CsxExportService.cs:245-259 builds childStructure; :272-273 gates on it), so an unbound multicast shows no pointer element at all, and Multicast_Inline is bound on purpose. On Shipping the pad is 0 and right and wrong emitters produce identical offsets, so Shipping is a control only and sw4 refuses there. Export CSX uses a native save dialog. Game windows steal focus: run front_window.py before every click. AOT only; never hand over the 107 MB non-trimmed exe.

**Related rows:** L43, L77, L48, L83

### L43 — `[A4-DELEGATE-ARRAY-PAD]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** yes · **needs UI:** yes · **estimate:** 40 min

**Fix commit(s):** `403a6596`

**Fixture:** DumperTest 5.4 **Development** (`launch_dumpertest.py dev`). The fixture HAS the shape: `ADumperTestActor.Arr_Delegates` is a TArray<FDumperTestUnicastSignature>, i.e. a TArray<FScriptDelegate> (DumperTestActor.h:638-649). BeginPlay runs SetNum(2) and binds only element [1] to this actor's D4b_OnPingProbe (DumperTestActor.cpp:307-308), leaving [0] empty, which makes the stride observable. On a checked build the element is 24 bytes (8-byte detector + 8-byte FWeakObjectPtr + 8-byte FName). Control = DumperTest Shipping (16-byte elements, no pad).

**Preconditions:** The same session and setup as L42 (dev, injected, a single CE with AOBMaker attached to DumperTest.exe, AOT dist, which contains 'array_elem_delegate_pad' in both the DLL and the exe). The UI is disconnected for the pipe steps.

**Steps:**

1. 1. With the UI disconnected: `py tools/verify/pipe_client.py find_instances --args '{"class_name":"DumperTestActor"}'` → take the non-Default__ addr/class_addr. Then `py tools/verify/pipe_client.py walk_instance --args '{"addr":"<addr>","class_addr":"<class_addr>","array_limit":8}'`. On Arr_Delegates expect `array_elem_size: 24`, **`array_elem_delegate_pad: 8`**, **no `delegate_pad` key**, elements[0] '(unbound)' and elements[1] naming D4b_OnPingProbe. `py tools/verify/d4b_delegate_pad.py` also PASSes and prints 'Arr_Deleg  : elem_size=24'.
2. 2. Build the witnesses from Arr_Delegates' `array_data_addr` (=Data) (UI disconnected): `pipe_client.py read_mem --args '{"addr":"<Data+0x18>","size":8}'` must be 0 (element [1]'s detector, i.e. the pre-fix leaf position). `read_mem` at Data+0x20 must be non-zero: FWeakObjectPtr {ObjectIndex, SerialNumber}, where the low dword equals the DumperTestActor's object index because [1] is bound to `this`.
3. 3. Connect the UI and open the same actor in Live Walker. Press 'Copy CE XML' (str.LiveWalker.ExportCE, en.axaml:261). Read the clipboard as bytes (computer-use read_clipboard, or a ctypes CF_UNICODETEXT read). In Arr_Delegates' group (<Address>+<field offset hex>, Offsets [0], i.e. dereference Data), the children must be [0] <Address>+8</Address> and [1] <Address>+20</Address>. The Description text keeps the UNPADDED element offset (+0 / +18) because DescShowOffset is persisted ON. Read the <Address>, not the Description.
4. 4. Paste the XML into CE's address list (right-click, then Paste) with the L42 sw4 harness still installed. out/sw4/sw4-records.txt lists every child record: [1]'s addr must equal Data+0x20 and [0]'s Data+0x8. CE's Value for [1] (8 Bytes hex) must equal the step-2 read at Data+0x20 (non-zero). [0] reads 0 (unbound).
5. 5. Fabricate: in ⚙ Options, 'Fabricate:' must read 4 (the persisted exponent is already 2 = 4 rows; set it if not). Select ONLY the Arr_Delegates row and press 'Copy CE Field'. Rows [2] <Address>+38</Address> and [3] <Address>+50</Address> must appear (i*24+8), plus [0] +8 and [1] +20.
6. 6. CSX: take L42's export (Drill Depth ≥ 1, same actor). Arr_Delegates' child <Structure> must hold '[0]' Offset="8" and '[1] …' Offset="32" (decimal; CsxExportService.cs:744-745).
7. 7. CONTROL on Shipping (the L42 step-11 session): walk_instance has NO array_elem_delegate_pad and array_elem_size 16. CE XML leaves are +0 / +10, fabricated +20 / +30, and CSX 0 / 16.
8. 8. Clean up as in L42. Restore Fabricate if it was changed.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| array_elem_delegate_pad | dll/src/Fern.cpp:1535-1538 (emitted only when > 0) | walk_instance reply JSON, on the Arr_Delegates ArrayProperty field |
| delegate_pad (must be ABSENT on Arr_Delegates) | dll/src/Fern.cpp:1516-1524 (set only when fv.delegatePad > 0; Ubel.cpp:4887/:5091 write the pad into arrayElemDelegatePad, never delegatePad) | walk_instance reply JSON |
| +{elemByteOffset + elemPad:X} | ui/UE5DumpUI/Services/CeXmlExportService.cs:2947 and :2953 (live leaves), :2966/:2968 (fabricated tail); ElemDelegatePad at :1131-1132 | <Address> of each element CheatEntry in Copy CE XML / Copy CE Field clipboard output |
| int offset = elem.Index * elemSize + (arrField.TypeName == "ArrayProperty" ? arrField.ArrayElemDelegatePad : 0) | ui/UE5DumpUI/Services/CsxExportService.cs:744-745 | .CSX child Element Offset attribute |
| Arr_Deleg  : elem_size=24 | tools/verify/d4b_delegate_pad.py:158 | rig stdout (precondition) |

**Row text vs source:** The row says '(DumperTest dev, if its fixture has one; otherwise record the check as not reachable)'. The fixture has one (DumperTestActor.h:649, bound [1] at .cpp:307-308), so the row is reachable. 'index * 24 + 8' matches source (elemByteOffset = Index*ArrayElemSize, + ElemDelegatePad). No text mismatch otherwise.

**Rig:** EXISTING, partial. `tools/verify/d4b_delegate_pad.py` covers the stride and value precondition but does NOT assert the new key. `pipe_client.py walk_instance` / `read_mem` one-liners give step 1-2. The sw4 CE harness DUMP (sw4_ce_delegate_pad.py install) reports child-record CurrentAddress. ⚠ sw4 `check` cannot be used for this row: it REFUSES when the field has no `delegate_pad` (sw4_ce_delegate_pad.py:114-124), which is exactly the Arr_Delegates case. NEW (~40 lines, read-only): read the clipboard CE XML, pick the Arr_Delegates <CheatEntry>, assert the child <Address> values against i*array_elem_size+array_elem_delegate_pad from walk_instance, and cross-read Data+0x18 / Data+0x20 via read_mem with the UI disconnected. UI clicks and CE paste need computer-use.

**Traps:** Only element [1] discriminates. [0] is unbound and reads 0 at both the padded and unpadded positions, so a check on [0] passes on the pre-fix build too. The CE value of [1] is an FWeakObjectPtr (index | serial<<32), not a pointer: compare its low dword with the actor's object index, not with an address. Descriptions show the unpadded offset (DecorateDesc uses elemByteOffset, CeXmlExportService.cs:2941-2942), so reading the description instead of <Address> gives a false FAIL. Fabricate is persisted (ui-options.json main.fabricateArrayCountExponent = 2 → 4 rows) and applies to Copy CE Field only, never to Copy CE XML. The Copy CE Field button relabels to 'Copy CE Fields (N)' with a multi-select (LiveWalkerViewModel.cs:178-180). Select only Arr_Delegates, because fabricate pads only the SELECTED top-level TArray. The pipe/UI slot rule (handover §4.4). Fern.cpp is compiled by NO test target and the UProperty-mode site has no fixture (commit 403a6596 'Survivors by construction'), so this live check is the ONLY execution of Fern.cpp:1535-1538. A Shipping run proves nothing for the pad.

**Related rows:** L42, L77, L46, L48

### L45 — `[P1-SEETHRU-NOPRODUCER]` `[P1-SEETHRU-GIVEUP]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 45 min

**Fix commit(s):** `df09bbcb`

**Fixture:** DumperTest 5.4 Shipping (`py tools/verify/launch_dumpertest.py shipping`, exe present at D:\UE_Analyze_data\for testing\DumperTest\Shipping\...\DumperTest-Win64-Shipping.exe). The Third Person map gives a live pawn and camera; seethrough_restoreset.py:92-95 records that a fresh DumperTest hides an occluder within a second. ⚠ DumperTest does NOT pause when backgrounded unless launched with `--idle` (-DumperTestIdle; tools/ue-sample/README.md:240-246, launch_dumpertest.py:149-150). So for the 'pause' use EITHER (A, recommended) no --idle plus `tools/verify/suspend.py suspend-tid` on the game's main thread, OR (B, the row's literal wording) `launch_dumpertest.py shipping --idle`. Step 3 (-3 refusal) has no stock host: ProbeProducers (Schlacht.cpp:376-390) only fails when KismetSystemLibrary::LineTraceSingle(OutHit) or AActor::SetActorHiddenInGame is cooked out.

**Preconditions:** AOT-trimmed dist\UE5DumpUI.exe (~54-55 MB, not the 107 MB untrimmed one). The injected DLL's `init` build_git must have df09bbcb as an ancestor (`git merge-base --is-ancestor df09bbcb <build_git>`); the build number alone does not tell you this (see L9). The See-through card is hidden unless Experimental is on (TeleportPanel.axaml:1240-1244 IsVisible=ExperimentalEnabled). %LOCALAPPDATA%\UE5CEDumper\experimental.json currently holds {"enabled": true, "snapshotQuotaMb": 0}, so it is on today; the toggle is the System-tab checkbox 'Enable advanced experimental features' (en.axaml:500). One game only. Kill leftovers first.

**Steps:**

1. 0. Launch `py tools/verify/launch_dumpertest.py shipping` (variant B: add `--idle`), then `py tools/verify/inject.py --pid <pid>`. Before starting the UI, confirm the engine booted (object_count about 24.5k via init/get_pointers) and check that build_git contains df09bbcb. Start the AOT UI and connect. Open the Teleport tab (str.Tab.Teleport 'Teleport') and find the card 'See-through (occluders)' (en.axaml:1205).
2. STEP 1 (enable passes the producer probe): leave 'Pierce depth:' at 1 and click 'See-through ON' (en.axaml:1208). Expect the badge 'ON' and the status line 'See-through ON — hiding the nearest 1 occluder(s) in the view.' Then `py tools/verify/front_window.py front DumperTest` with the camera facing a wall or object, and click '↻' (RefreshSeeThroughCommand). The card should read 'Active — hiding the occluder in front of your character.' In the game log folder, init-0.log must have 'SeeThrough: enabled (pierce=1)' and 'SeeThrough: worker started (100 ms tick)', and NO 'SeeThrough: refusing to enable' line of either kind.
3. STEP 2a (disable while paused). Keep an occluder visibly hidden. Variant A: run `py tools/verify/suspend.py threads DumperTest-Win64-Shipping`, pick the main-thread tid, then `py tools/verify/suspend.py suspend-tid DumperTest-Win64-Shipping <tid>`. Variant B: click the UI title bar so the game loses focus. Either way, WAIT at least 1 s so the game counts as stalled (Stark kStallThresholdMs = 500, Stark.h:87), then click 'See-through OFF'. Expect the status 'See-through OFF — 1 actor(s) are still hidden because the game thread is paused (game in the background?)...' and the card 'Off — 1 actor(s) still hidden (the game thread was paused). Click back into the game and they reappear; Keep Foreground avoids this.' init-0.log should gain the 'disabled but 1 actor(s) remain hidden' and 'waiting for the game thread to resume' lines. If the status reads plain 'See-through OFF.', the thread was still responsive and this arm tested nothing: redo it.
4. STEP 2b (control on the pending state): about 30 s later click '↻'. The card must still show the pending text. In ui-pipe-0.log the seethrough_get_state reply must carry "restore_pending":true and "restore_abandoned":false (Fern.cpp:6177-6178).
5. STEP 2c (give-up): keep the game paused for at least 305 s (PENDING_RESTORE_MAX_MS = 300000, Grimoire.h:1015; the loop ticks every 250 ms). Do not click or focus the game window. A suspended window shows as Not Responding, so never click it. init-0.log must contain 'SeeThrough: gave up waiting for the game thread (300 s) — ...' and must NOT contain 'game thread resumed after'. Click '↻'. The card must read 'Off — 1 actor(s) still hidden: the game thread stayed paused past the restore window, so the automatic restore gave up. Turn See-through on and off again with the game running to restore them.' The wire reply must carry restore_pending false, restore_abandoned true and hidden_count 1.
6. STEP 2d (the card is truthful): variant A: `suspend.py resume-tid ...`; variant B: click into the game. The occluder must STAY invisible and no 'restored' line may appear. This is the promise the pre-fix card broke.
7. STEP 2e (remedy, game running): variant A: DumperTest keeps running while the UI has focus, so just click 'See-through ON' and then 'See-through OFF'. Variant B: first click 'Force ON' on the 'Keep Foreground' card (en.axaml:1109-1112; Grausam blocks idle mode), then ON and OFF, then 'Force OFF'. Expect the status 'See-through OFF.', the badge 'OFF', the card '—', and init-0.log 'SeeThrough: disabled (N restored)' with N ≥ 1. The actor reappears in the game (screenshot). '↻' then reports restore_abandoned false and hidden_count 0.
8. STEP 3: there is no live trigger. Record the pins instead: TeleportViewModelTests.ApplySeeThrough_refused_for_missing_producers_says_the_build_cannot, RefreshSeeThrough_after_the_restore_gave_up_names_the_real_remedy, RefreshSeeThrough_while_the_restore_still_waits_keeps_the_promise; InvokeScriptTests.SeeThrough_RefusesAtEnable_WhenTheBuildCannotHide and SeeThrough_PublishesARestoreThatGaveUp (source pins for Schlacht.cpp and Fern.cpp).
9. Teardown: resume-tid (safe to repeat), set Keep Foreground back to OFF, leave Experimental as you found it, then kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| See-through ON — hiding the nearest {SeeThroughPierce} occluder(s) in the view. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2967 | UI status line after 'See-through ON' |
| Active — hiding the occluder in front of your character. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2952 | See-through card after ↻, game focused and a wall in view |
| SeeThrough: enabled (pierce=%d) | dll/src/Schlacht.cpp:763 | Logs\DumperTest-Win64-Shipping\init-0.log (LOG_CAT "SEETHRU" matches no prefix in Sein.cpp:66-89, so it falls back to init.log) |
| SeeThrough: refusing to enable — %s not found (cooked out?), so nothing could be hidden | dll/src/Schlacht.cpp:734-735 | init-0.log. Must be ABSENT in step 1 |
| SeeThrough: refusing to enable — game-thread hook unavailable (the tracing invokes would have to run off the game thread) | dll/src/Schlacht.cpp:718-719 | init-0.log. Must be ABSENT |
| seethrough_set: enable=%s count=%d | dll/src/Fern.cpp:6188 | pipe-0.log (PIPE:cmd) for every ON/OFF |
| See-through OFF — {st.HiddenCount} actor(s) are still hidden because the game thread is paused (game in the background?). They are restored automatically as soon as you click back into the game. Keep Foreground prevents this. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2995-2997 | UI status after OFF while paused (step 2a) |
| Off — {st.HiddenCount} actor(s) still hidden (the game thread was paused). Click back into the game and they reappear; Keep Foreground avoids this. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2939-2940 | See-through card while the restore is PENDING (steps 2a/2b) |
| SeeThrough: disabled but %zu actor(s) remain hidden (game thread unresponsive) — waiting for it to resume to restore them | dll/src/Schlacht.cpp:799-800 | init-0.log, step 2a |
| SeeThrough: waiting for the game thread to resume so the leftover hidden actor(s) can be restored | dll/src/Schlacht.cpp:624-625 | init-0.log, step 2a |
| SeeThrough: gave up waiting for the game thread (%d s) — any leftover hidden actor(s) stay hidden until See-through is enabled and disabled again with the game running | dll/src/Schlacht.cpp:649-652 (%d = PENDING_RESTORE_MAX_MS/1000 = 300) | init-0.log, step 2c |
| Off — {st.HiddenCount} actor(s) still hidden: the game thread stayed paused past the restore window, so the automatic restore gave up. Turn See-through on and off again with the game running to restore them. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2935-2938 | See-through card after ↻ once the give-up happened (step 2c). The fix under test |
| "restore_pending" / "restore_abandoned" | dll/src/Fern.cpp:6177-6178 (parsed in DumpService.cs:3565-3567) | seethrough_get_state / seethrough_set reply in ui-pipe-0.log |
| SeeThrough: game thread resumed after %d ms — restored %zu leftover hidden actor(s) | dll/src/Schlacht.cpp:667-668 | init-0.log. Must be ABSENT in steps 2c-2d; its presence means the pause broke before 300 s |
| SeeThrough: disabled (%zu restored) | dll/src/Schlacht.cpp:788 | init-0.log, step 2e |
| See-through OFF. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2998 | UI status, step 2e. Plain OFF: badge 'OFF' (ApplySeeThroughState, :2875-2880) and card '—' (:2946) |
| Not supported on this game build — LineTraceSingle or SetActorHiddenInGame is missing (cooked out), so See-through cannot hide anything. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2923-2925 | Card for the -3 refusal. Step 3, not reachable live |

**Row text vs source:** None in substance. The row's step 2 says 'background it without Keep Foreground'. On DumperTest that pauses nothing unless the game was launched with -DumperTestIdle (launch_dumpertest.py `--idle`), so either launch that way or suspend the main thread; plain backgrounding does not reach the give-up. The row's 'the card reads plain OFF' is, in source, badge 'OFF' plus card text '—' (TeleportViewModel.cs:2944-2947).

**Rig:** Existing rigs: tools/verify/suspend.py (`threads` / `suspend-tid` / `resume-tid`; picks the main thread by creation time; resuming twice is safe) and tools/verify/seethrough_restoreset.py. Arm (b) of the latter (lines ~180-205) is exactly 'disable while the game thread is suspended', and it gets its bHidden witness from ReadProcessMemory on the hidden_actors addresses, with the offset resolved through search_properties bHidden. ⚠ Its relaunch() hardcodes the `dev` flavour and taskkills DumperTest.exe (lines 101-106). Adapt it to `shipping` / DumperTest-Win64-Shipping.exe. For a headless DLL-side twin, copy arm (b) with three changes: (1) keep the thread suspended ≥305 s instead of 0.8 s; (2) poll seethrough_get_state at +10 s (expect restore_pending true / restore_abandoned false) and at +305 s (expect restore_pending false / restore_abandoned true / hidden_count ≥1); (3) after resume-tid, re-read bHidden, which must STILL be set; then seethrough_set enable=true followed by enable=false, and bHidden must clear with restore_abandoned false. That twin uses the pipe, so run it with the UI closed. The UI walkthrough above needs computer-use: launch_dumpertest.py, inject.py and front_window.py.

**Traps:** • The UI holds 2 of the 3 pipe lanes (Fern kMaxPipeInstances=3; handover-2026-08-22.md:349-352), so never run seethrough_restoreset.py or pipe_client.py while the UI is connected. suspend.py talks to the OS, not the pipe, so it is safe alongside the UI.
• Clicking OFF within 500 ms of focus loss can find the game thread still 'responsive'. The disable then tries to un-hide straight away and the arm has no subject. Wait ≥1 s, and check the status says 'still hidden'.
• The give-up only records if the game stays unresponsive for the full 300 s. Focus stealing (MEMORY rule 6) or an accidental click into the game resets the arm: the pending loop restores and logs 'game thread resumed'. With variant A the window is frozen, so never click it; Windows may show a 'not responding' ghost.
• Variant B (--idle) makes every game-thread dispatch the UI issues (Teleport prime, POV) time out while the UI has focus (README:240-243). Step 2e needs Keep Foreground Force ON, or the remedy itself runs paused and just re-arms a pending restore.
• Do not judge 'the actor reappears' from hidden_count alone; that is the DLL's own bookkeeping (seethrough_arms.py:8-15). Use a screenshot, or bHidden read out of process.
• The card is experimental-gated. Restore experimental.json if you change it, and turn Keep Foreground off afterwards.
• AOT: hand over and test only the -Mode Publish trimmed exe.

**Related rows:** L35, L58, L14, L25, L26, L27, L37, L54

### L46 — `[P1-UPROP-DELEGATE]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 35 min

**Fix commit(s):** `cb20b61d`

**Fixture:** Self-built UE 4.23 package `UE423_Flying`, which carries tools/ue-sample/ue4-delegate-fixture ADelegatePadFixture: `TArray<FDPadPingSignature> Arr_MulticastDelegates`, a MULTICAST array whose [0] is empty and [1] is bound (DelegatePadFixture.h:58-62, .cpp:24-26). Exe: D:\UE_Analyze_data\For Testing\UE423_Flying\Development\WindowsNoEditor\UE423_Flying\Binaries\Win64\UE423_Flying.exe (present; Shipping is present too). use_fproperty=false on 4.23, so this is the UProperty path under test (todo.md:9632). ⚠ Use DEVELOPMENT: the instance exists only after UCheatManager::Summon, and Shipping has no CheatManager. The row is therefore closed on dev and must say so. ⚠ The fixture declares NO TArray<FScriptDelegate> (sw9 docstring), so Phase J (the unicast arm, Ubel.cpp:5102) cannot be reached live on any available host; only Phase K (multicast, Ubel.cpp:5114) can. Real <4.25 titles in docs/test-games.md: OctoPath 4.18 (line 14), FF7R 4.18 fork (16), DQ XI S 4.18 (58), IDOLM@STER 4.24 (61), SW Jedi FO 4.21 (63), Everspace 4.20 (74), Drop In 4.24 build (77), Satisfactory 4.22 depot (82), Extinction 4.15 (91), NEKOPALIVE 4.11 (101). None is known to carry a delegate array, and installs are re-checked in a later stage.

**Preconditions:** No other fixture running. sw9 carries its own one-game guard because launch_dumpertest.py's guard cannot see UE4 images. The injected DLL's build_git must contain cb20b61d. For the UI arm, use the AOT UI.

**Steps:**

1. STEP 1a (DLL/wire): `py tools/verify/sw9_ue4_delegate_read.py --engine 423 --keep`. It byte-scans the exe, launches it, injects by PID, summons DelegatePadFixture through a non-CDO CheatManager, walks the instance and exits. It leaves the game running. It must print use_fproperty=False, SW9: PASS, and the spawned address A. Arr elems must be ['(0 bindings)', '(1 binding) [DelegatePadFixture_<N>::DPad_OnPingProbe]'] with elem_size 16.
2. STEP 1b (UI): start the AOT UI and connect; the sw9 pipe client has already exited. In Live Walker, paste A into the 'Address (hex or module.exe+offset)' box and click 'Go'. The Arr_MulticastDelegates row's Value (read the TOOLTIP, not the 200 px cell) must be '[2 x MulticastInlineDelegateProperty (16B)] = [(0 bindings), (1 binding) [DelegatePadFixture_<N>::DPad_OnPingProbe]]', and no '(multicast array — … not read)' may appear. 'As on an FProperty title': run the same rig with `--engine 427` (FProperty path) in a separate session and compare the value strings; they must be the same shape. walk-0.log has 'UArrayProperty::Inner at UProperty+0x.. (delta=..) for 'Arr_MulticastDelegates' -> 'MulticastInlineDelegateProperty' elemSize=16'.
3. STEP 2 (the fix. Reachable live by poking memory, contrary to the row's 'dll_core_test covers it'). Disconnect the UI first. Use a sw6_stride_refusal.py-style rig: get_offsets gives `uproperty_elemsize` (Fern.cpp:5184-5188, published only when use_fproperty is false). A full (non-lean) walk_instance on A gives Arr_MulticastDelegates.array_inner_addr (Fern.cpp:1554-1557; in UProperty mode it is the inner UProperty*, Ubel.cpp:5015). Wrap the poke in mutate_guard.Mutation: write int32 20 (neither 16 nor 24) at inner+uproperty_elemsize, read it back, walk once, and restore in finally. Expect: value '(multicast array — unexpected multicast element size 20, not read)', array_elem_size 20, no `elements` key. walk-0.log, timestamped inside the poke window: 'ReadMulticastDelegateArrayElements: ElementSize=20 is neither 16 nor 24 (+detector) -- refusing to guess a stride'. After the restore, re-walk and the value must be byte-identical to step 1a.
4. STEP 2-UI (optional, so the refusal string renders): keep the UI connected and do the same 4-byte poke OUT OF PROCESS with WriteProcessMemory, using the address computed in STEP 2 before connecting, so it costs no pipe slot. Click Live Walker 'Refresh'. The Value must now be the refusal string, because DisplayValue prefers TypedValue (LiveFieldValue.cs:504-509). Restore, Refresh, and the value is back to the step 1b string.
5. Teardown: kill UE423_Flying.exe and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| [{ArrayCount} x {typeLabel} ({ArrayElemSize}B)] = [{joined}] | ui/UE5DumpUI/Models/LiveFieldValue.cs:714-737 (FormatArrayDisplay) | Live Walker Value/tooltip. Expected '[2 x MulticastInlineDelegateProperty (16B)] = [(0 bindings), (1 binding) [DelegatePadFixture_<N>::DPad_OnPingProbe]]' |
| (N binding[s]) [Target::Fn, …] / (0 bindings) | dll/src/Ubel.cpp:3856-3862 (display build in ReadMulticastDelegateArrayElements) | wire elements[].v and the UI preview |
| UArrayProperty::Inner at UProperty+0x%X (delta=%d) for '%s' -> '%s' elemSize=%d | dll/src/Ubel.cpp:5013 | Logs\UE423_Flying\walk-0.log (WALK:ArrayP). Shows 16 at baseline and 20 during the poke |
| (multicast array — unexpected multicast element size N, not read) | dll/src/Ubel.cpp:5114 (UProperty arm, the fix) + error text at :3758 | wire field `value` and the Live Walker Value cell, step 2 only |
| ReadMulticastDelegateArrayElements: ElementSize=%d is neither %d nor %d (+detector) -- refusing to guess a stride | dll/src/Ubel.cpp:3759-3761 | walk-0.log (WALK), step 2 only |
| (delegate array — unexpected FScriptDelegate element size N, not read) | dll/src/Ubel.cpp:5102 + :3625 | Phase J (unicast) arm. NOT reachable: no host declares TArray<FScriptDelegate> under UProperty mode |

**Row text vs source:** Step 2 says a stock build cannot produce an unrecognised ElementSize, 'so dll_core_test covers it'. The UProperty arm reads the inner UProperty's ElementSize LIVE on every walk (Ubel.cpp:4991-4993; the inner is discovered by type name, :4981-4988, independent of the size). So the sw6 house technique (a 4-byte poke of inner+uproperty_elemsize) reaches the fixed Phase K arm in a running 4.23 process. Only Phase J (unicast delegate arrays) has no live host. The row's quoted text '(delegate array — unexpected … element size N, not read)' matches Ubel.cpp:5102. The multicast twin reads '(multicast array — unexpected multicast element size N, not read)' (Ubel.cpp:5114), and that twin is the one this fixture reaches.

**Rig:** Existing: tools/verify/sw9_ue4_delegate_read.py --engine 423 --keep drives all of step 1 (launch, byte-scan guard, inject by PID, Summon through a non-CDO CheatManager, walk_instance, and it prints the address for the UI arm). tools/verify/sw6_stride_refusal.py is the template for step 2, but it reads `fproperty_elemsize` and the DumperTestActor SUBJECTS (lines 199, 263-300). The 4.23 variant must read get_offsets['uproperty_elemsize'], walk the DelegatePadFixture instance, and poke only Arr_MulticastDelegates. Use tools/verify/mutate_guard.py (Mutation: captures, writes, witnesses and restores with a read-back) for the poke.

**Traps:** • A Development package is required (CheatManager Summon). The row prefers Shipping, so state that this was 'dev only'.
• sw9's TRAP 1: invoke_function's class_name shortcut falls back to the CDO. Summon on a CDO then does nothing while still reporting OK. The rig resolves a non-CDO CheatManager.
• TRAP 2: pre-fixture UE4 builds share the exe stem AND the log folder (Logs\UE423_Flying). The rig byte-scans for DelegatePadFixture.
• The Value cell is 200 px with no ellipsis ([V8PREVIEWCLIP]), so read the tooltip.
• The pad cannot fail on UE4 (no access detector). Step 1 tests RECOGNITION and INDEXING (element [1] names the probe, [0] is empty), not the pad.
• The poke changes a per-CLASS UProperty (the engine's Inner->ElementSize). Keep the window to one walk, restore in finally, and spawn or re-bind nothing meanwhile.
• UI holds 2 pipe lanes: do the pipe poke with the UI disconnected, or do it out of process.
• 4.23 is > 4.22, so this host does NOT serve L67 (needs ≤4.22 MulticastDelegateProperty naming).

**Related rows:** L43, L42, L67, L11

### L47 — `[P1-WALK-UNREADABLE]` `[A4-REROOT-STALE-WARNING]`

**Status:** ✅ PASSED red→green 2026-09-23 (`git log --grep 'verify(L47)'`) · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 35 min

**Fix commit(s):** `4c094108`

**Fixture:** DumperTest 5.4 Shipping. ⚠ The row's natural trigger ('let the game destroy it') is UNRELIABLE for the 'no longer readable' text. The DLL sets `unreadable` only when Macht::IsAddrReadable fails on the header: VirtualQuery state != MEM_COMMIT, or no read permission (Ubel.cpp:4333-4339; Macht.cpp:36-57). A destroyed UObject's binned memory normally stays committed, so it walks as a zombie, a recycled object, or the stale path. The deterministic route is a MANUFACTURED freed object. Allocate a page in the game with VirtualAllocEx, copy a real small UObject into it (e.g. the DumperTestPayload that DumperTestActor_0.Payload points at; see L5 step 3), open it, then VirtualFreeEx it. This is dll_core_test's own 'reserved, uncommitted page' shape (todo.md:2237), played in a live process, and the game never references that page. DumperTest also offers a natural attempt: Spawn_Holders(300) and Spawn_DestroyHolders(), which forces a GC (DumperTestActor.h:891-901).

**Preconditions:** AOT UI. build_git must contain 4c094108. In Live Walker, Auto-refresh is off and 'Guess?' (FillGaps) is OFF (note the prior state). The clone page is made BEFORE the UI connects, because making it needs the pipe.

**Steps:**

1. 0. `py tools/verify/launch_dumpertest.py shipping`, then inject.py --pid. Rig phase `make` (pipe, UI closed): find_instances DumperTestActor gives the non-CDO DumperTestActor_0. walk_instance gives Payload's pointee X and its props_size S. Read S bytes at X out of process, VirtualAllocEx(MEM_RESERVE|MEM_COMMIT, RW) a page P, WriteProcessMemory the bytes, and print P and X.
2. 1. Start the AOT UI and connect. Live Walker: enter P in 'Address (hex or module.exe+offset)' and click 'Go'. The grid shows the clone's fields (same class as X), which is the positive control that P walks as an object.
3. STEP 1 (unreadable): rig phase `free`: VirtualFreeEx(P, 0, MEM_RELEASE). It is out of process and needs no pipe. Confirm with VirtualQuery that the state is not MEM_COMMIT. Click Live Walker 'Refresh'. Expect the status '⚠ This object is no longer readable (freed?) — re-open it from 🌍 GWorld or the finder.' over an empty grid. Game walk-0.log: 'WalkInstance: instance 0x<P> not readable (freed?), skipping'. UI view log (Logs\UE5DumpUI\view-0.log and the mirror Logs\DumperTest-Win64-Shipping\ui-view-0.log): 'UpdateDisplay: unreadable object at 0x<P> — nothing walked' and 'Skipping fill_gaps auto-retry for 0x<P>: object is stale or unreadable'. The walk_instance reply in ui-pipe-0.log carries "unreadable":true. The 'Guess?' box must NOT have ticked itself. Pre-fix this was a blank grid with no status.
4. STEP 2 (freed across a re-root, WITH a way back): 'Go' to DumperTestActor_0, then drill its 'Payload' pointer row so the leaf crumb's FieldName is 'Payload'. Now 'Go' to P, which is still freed. Expect the status '⚠ This object is no longer readable (freed?) — re-open it from 🌍 GWorld or the finder.  ·  ← Back returns to Payload' (two spaces either side of the '·'). Click 'Back': you must land on the Payload page again.
5. STEP 2' (first navigation, no way back): with Live Walker's spine empty (fresh UI session), 'Go' straight to P. The status must be the bare warning. It must not be empty, and the hint must not overwrite it (ComposeReRootStatus returns walkStatus when hint is "").
6. OPTIONAL natural attempt (the row's literal wording): on DumperTestActor_0's function list use the PIPE invoke to run Spawn_Holders(Count=300). On the Instances tab open a DumperTestHolder (a cross-tab re-root). PIPE-invoke Spawn_DestroyHolders, then click 'Refresh' and afterwards 'Go' to the same address. Record which status appears ('no longer readable' / '⚠ This object appears to have been freed/recycled — …' / none) and the page's VirtualQuery state, and do not score it.
7. Teardown: kill the game and the UI. If the auto-fill path ever fired, restore 'Guess?'.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| ⚠ This object is no longer readable (freed?) — re-open it from \U0001F30D GWorld or the finder. | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:6936 | Live Walker status line (renders with the 🌍 glyph) |
| UpdateDisplay: unreadable object at {result.Address} — nothing walked | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:6937 | Logs\UE5DumpUI\view-0.log (+ per-game mirror ui-view-0.log) |
| Skipping fill_gaps auto-retry for {addr}: object is stale or unreadable | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:6626 | view-0.log. Proves no fill_gaps retry was sent |
| WalkInstance: instance 0x%llx not readable (freed?), skipping | dll/src/Ubel.cpp:4335-4337 (WALK:safe) | Logs\DumperTest-Win64-Shipping\walk-0.log |
| "unreadable": true | dll/src/Fern.cpp:2171-2172 (both lean and full) | walk_instance reply in ui-pipe-0.log |
| {walkStatus}  ·  {hint} | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2309-2312 (ComposeReRootStatus), applied at :3035-3036 (Go box / cross-tab) and :2967 (Find Refs Open) | Live Walker status after a re-root onto a freed object |
| ← Back returns to {name} / ← Back returns to the previous object | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2314-2322 (ReRootedHint; name = leaf crumb FieldName ?? Label) | Second half of the composed status in step 2 |
| ⚠ This object appears to have been freed/recycled — re-open it from \U0001F30D GWorld or the finder. | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:6941 | The IsStale variant (implausible PropertiesSize). Possible outcome of the optional natural attempt |

**Row text vs source:** The strings match: the row quotes the prefix of LiveWalkerViewModel.cs:6936. The row's recipe ('let the game destroy it') will mostly NOT produce that string. The DLL reports `unreadable` only for a non-committed or unreadable header (Ubel.cpp:4333-4339). A freed-but-committed object gives the stale text (:6941) or a silent zombie walk. For a deterministic check, manufacture the freed page.

**Rig:** No existing rig covers this tag (grep of tools/verify for unreadable/VirtualFreeEx finds only unrelated d3/d5/a11). Smallest new rig, `l47_clone_page.py make|free`: `make` uses pipe_client (UI closed) for find_instances + walk_instance, then ReadProcessMemory, VirtualAllocEx and WriteProcessMemory (tools/verify/a11_step2_realloc.py:56-65 already sets up the VirtualAllocEx ctypes), and prints P. `free` does VirtualFreeEx(P,0,MEM_RELEASE) plus a VirtualQuery printout and never opens the pipe, so it is safe with the UI connected. Everything else is computer-use on Live Walker: the Go box, drilling Payload, Refresh, Back. Read the status and view-0.log.

**Traps:** • A naturally destroyed actor is usually NOT unreadable: binned allocator pages stay MEM_COMMIT, so you get a zombie walk or the stale text. That result is not a failure of the fix; the unreadable branch is keyed on VirtualQuery (Macht.cpp:44-51).
• The hint names the leaf crumb's FieldName, and every Go-box or cross-tab root sets FieldName to 'Custom' (NavigateToAsync(normalizedAddr, "Custom", 0, "Custom", …), LiveWalkerViewModel.cs:3031-3034). So when the page you came from was itself a Go-box root, the hint reads '← Back returns to Custom'. Drill a named field first to get a meaningful name.
• The Find Refs Open compose site (:2967) has no end-to-end harness; the ledger calls it a survivor by construction (todo.md:5073-5074). This recipe exercises the Go-box site only.
• Refresh has a 10 s deadline (Constants.cs:155). A timeout is a different message and must not be taken as a pass.
• UI holds 2 pipe lanes: the `make` phase must run before the UI connects.
• Game windows steal focus (front_window.py) before any click on the UI.

**Related rows:** L7, L57, L73, L3, L4, L23

### L48 — `[P1-SPARSEDELEGATE-REFS]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 60 min

**Fix commit(s):** `b123cf2a`

**Fixture:** DumperTest 5.4 Shipping for the FIX's observables: sparse_unlocated, the offsets line, and the 'never blame the game' status. DumperTestActor_0 binds its own OnActorHit, a MulticastSparseDelegateProperty, in BeginPlay (DumperTestActor.cpp:287-291), so the global FSparseDelegateStorage has a real entry. ⚠ That binding is SELF-bound (owner == target), and Find Refs suppresses exactly that case (Aura.cpp:4053 `if (ownerObj == target) continue;  // self-reference suppressed`). DumperTest therefore cannot show the step 1 'binding is listed' half unless an engine object binds a sparse delegate to a different object. Enumerate the storage to find out. If there is none, that half is fixture-limited to a UE5 title with a cross-object sparse binding, e.g. a component's OnComponentBeginOverlap bound to its actor. EVERSPACE 2 (docs/test-games.md:12, now UE 5.6.1/PE 506, used with a save loaded in L2/L31) is the likeliest; a later stage re-checks installs.

**Preconditions:** build_git contains b123cf2a. AOT UI. Sparse storage is resolved on demand: walk DumperTestActor_0 once. Its OnActorHit row must read '(1 sparse binding) [DumperTestActor_0::D4_OnActorHitProbe]' before get_pointers publishes a non-zero `sparse_delegates` (Fern.cpp:1314).

**Steps:**

1. 0. Launch shipping and inject. Rig phase `locate` (pipe, UI closed): walk_instance DumperTestActor_0 and confirm the OnActorHit value. get_pointers gives sparse_delegates S. Using ReadProcessMemory, walk the outer TMap exactly as Aura.cpp:4020-4070 does: outer stride 0x60, key UObject* at +0, inner TMap at +0x08, inner stride FNameSlotIn8Aligned()+0x18 (0x20 on non-CPN 5.4), TSharedPtr object at +FNameSlot. Resolve mcd for (DumperTestActor_0, OnActorHit), then locate {Data,Num,Max} at pad 0 or 8 the way LocateInvocationList does (Aura.cpp:3612-3646). While there, list EVERY (owner, delegate, bound-target) triple and flag the ones with target ≠ owner, which are hosts for step 1's listed half.
2. 1. Pick a Find Refs TARGET that is not DumperTestActor_0 and has fewer than 32 field references; a baseline Find Refs that returns 0 is ideal. Start the AOT UI, go to the target in Live Walker, and click 'Find Refs'. Baseline: sparse_unlocated 0 in the reply (ui-pipe-0.log), and with 0 refs the status 'No references found — likely held by a non-reflected pointer (TUniquePtr / raw pointer / non-UObject struct)  [scanned N/N in Xms]'.
3. STEP 1/2 arm A (unreadable sparse delegate): rig phase `poke` is out of process and needs no pipe. WriteProcessMemory Num=0 into OnActorHit's invocation-list header (IsBoundInvocationListHeader rejects num<1, Aura.h:1404-1408), then read it back. Click 'Find Refs' again. offsets-0.log must show 'FindReferences: no coherent sparse InvocationList at 0x<mcd> — this delegate's bindings are MISSING from the results, not absent from the game' and 'FindReferencesToUObject: 1 sparse delegate(s) had no readable InvocationList — their bindings are MISSING from these results'. The reply carries scan.sparse_unlocated = 1. The status must NOT contain 'likely held by a non-reflected pointer'. With 0 refs it reads 'No references found in what was read — 1 sparse delegate(s) could not be read, so their bindings are missing. That is not evidence that nothing points here.  [scanned …]'. With N>0 refs it reads 'Found N reference(s)  [scanned …]  [1 sparse delegate(s) unreadable — their bindings are missing]'. Run `restore` at once (Num back to its original value), click Find Refs again, and the baseline must return.
4. STEP 2 arm B (the scan did not finish): arm the object decryption hook at 0x1000 out of process with sw1_worker_fault.py's call_setter(pid, base, rva, 0x1000) (CreateRemoteThread → UE5_SetObjectDecryption; no pipe slot). Click 'Find Refs' immediately, then disarm with call_setter(...,0) or `py tools/verify/call_export.py UE5_SetObjectDecryption --process DumperTest-Win64-Shipping`. Expect in offsets-0.log 'ParallelGObjectsScan: worker … faulted' lines between 'Custom decryption function SET' and 'CLEARED (identity)', and 'FindReferencesToUObject: found 0 matches … DEADLINE HIT'. The status must be 'No references found in what was read — the scan did not finish. That is not evidence that nothing points here.  [scanned X/Y in Nms — DEADLINE HIT, retry to continue]'. Pre-fix it was the 'likely held by a non-reflected pointer' line over that same suffix.
5. STEP 1 listed half: if `locate` found a triple with target T ≠ owner O, and T has fewer than 32 field refs, run Find Refs on T with the storage untouched. A row with field_type 'MulticastSparseDelegateProperty', field_name = the delegate FName and element_index = binding index must be listed. offsets-0.log: 'FindReferencesToUObject: hit <O>.<Delegate>[i] (MulticastSparseDelegateProperty, owner=0x…, <Class>)'. If DumperTest has no such triple, repeat on ES2 with a save loaded. Rename the stale version.dll proxy aside first, as in L2. A cold 1.15M-object Find Refs there may hit the 30 s deadline (Aura.cpp:3702), which is also a natural arm-B observation.
6. Teardown: make sure Num is restored and decryption disarmed, then kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| No references found — likely held by a non-reflected pointer (TUniquePtr / raw pointer / non-UObject struct) | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2913 | Status ONLY on a complete scan (baseline). Must be ABSENT in arms A and B |
| No references found in what was read — {gaps joined by '; '}. That is not evidence that nothing points here. | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2905-2916 (gaps: 'the scan did not finish' / '{N} sparse delegate(s) could not be read, so their bindings are missing') | Live Walker status, arms A (0 refs) and B |
|   [{N} sparse delegate(s) unreadable — their bindings are missing] | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2876-2877 | Suffix on 'Found N reference(s)' and on the References header when refs > 0 |
|   [scanned {ObjectsScanned}/{ObjectsTotal} in {DurationMs}ms — DEADLINE HIT, retry to continue] | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2867-2871 | Status suffix, arm B |
| FindReferences: no coherent sparse InvocationList at 0x%llX — this delegate's bindings are MISSING from the results, not absent from the game | dll/src/Aura.cpp:4087-4090 | Logs\DumperTest-Win64-Shipping\offsets-0.log (LOG_CAT OARR routes to offsets). Arm A |
| FindReferencesToUObject: %d sparse delegate(s) had no readable InvocationList — their bindings are MISSING from these results | dll/src/Aura.cpp:4140-4142 | offsets-0.log, arm A |
| scan.sparse_unlocated | dll/src/Fern.cpp:4540; parsed DumpService.cs:978; IsComplete AddressLookupResult.cs:138 | find_refs_to_uobject reply in ui-pipe-0.log |
| FindReferencesToUObject: hit %s.%s[%d] (MulticastSparseDelegateProperty, owner=0x%llX, %s) | dll/src/Aura.cpp:4116-4120 | offsets-0.log, step 1's listed half (cross-object binding only) |
| FindReferencesToUObject: found %d matches in %lld ms (scanned %d/%d, %d classes with refs, %d thread(s)%s) | dll/src/Aura.cpp:4143-4145 (', DEADLINE HIT' suffix when incomplete) | offsets-0.log, every run |
| (sparse, bound — invocation list unreadable) | dll/src/Aura.h:1418-1419 | Live Walker OnActorHit row while the Num poke is live. A second witness that the poke took |

**Row text vs source:** The row says the offsets line 'had no readable InvocationList' NAMES any delegate that was not read. It does not. The aggregate line (Aura.cpp:4140-4142) gives only a COUNT. The per-delegate line (Aura.cpp:4087-4090) gives the FMulticastScriptDelegate's ADDRESS, not the delegate's FName and not its owner, even though both are in scope there (fieldFName at :4067, ownerObj at :4052). A check that expects the delegate's name in the log will fail on a correct build. Also: step 1 describes 'an object bound only through a sparse delegate' on DumperTest's own OnActorHit path. That path is self-bound and self-suppressed (Aura.cpp:4053), so the listed half needs a cross-object binding.

**Rig:** Existing: tools/verify/sw1_worker_fault.py. Its module/export_rva/call_setter helpers (lines 161-240) arm and disarm UE5_SetObjectDecryption out of process. Its own main() opens the pipe, so import the helpers and do not run it whole while the UI is connected. tools/verify/call_export.py is the panic disarm. tools/verify/mutate_guard.py covers poke/restore discipline, but it writes through the pipe, so for the UI arm use WriteProcessMemory. New rig `l48_sparse.py locate|poke|restore`: `locate` uses the pipe (UI closed) for walk_instance + get_pointers, then ReadProcessMemory for the storage walk, and prints mcd, the header and all cross-object triples. `poke` and `restore` are WriteProcessMemory only. Clicking Find Refs and reading the status are computer-use.

**Traps:** • Self-binding suppression (Aura.cpp:4053). Find Refs on DumperTestActor_0 will NEVER list its own OnActorHit binding, and that absence is correct. Do not read it as a failure.
• The sparse pass runs only if the field phase found fewer than max_results matches (Aura.cpp:4017), and the UI always sends max_results=32 (DumpService.cs:957). A target with ≥32 field refs skips the sparse pass silently. Use a low-ref target.
• The sparse pass is also skipped when the parallel phase was incomplete (`!deadlineHit`, Aura.cpp:4017). Arms A and B are separate runs and cannot be combined.
• FSparseDelegateStorage is resolved lazily, so walk a sparse-bound object before `locate`.
• Arm B's decryption arm is GLOBAL: every GetByIndex faults while it is armed, including UI polls, which Fern guards. Keep the window to one click, turn Live Walker Auto-refresh off, and do not run gameplay features.
• Arm A pokes a live engine delegate. Num=0 only makes OnActorHit broadcast nothing, but restore immediately. Do NOT repoint the binding's weak pointer at another object to fake a cross binding: a hit broadcast would then FindFunctionChecked on the wrong class.
• UI holds 2 pipe lanes, so `locate` runs before the UI connects.
• ES2: stale proxy DLL and a loaded save (L2 notes). A 1.15M-object scan is long, so read the timestamps.

**Related rows:** L84, L12, L2, L31, L7

### L49 — `[A2-WALKCLASSEX-UNMAPPED]`

**Status:** ✅ PASSED red→green 2026-09-22 (`git log --grep 'verify(L49)'`) · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 35 min

**Fix commit(s):** `84a8d8ca`

**Fixture:** DumperTest 5.4 Shipping. The defect is REACHABLE live, contrary to the row. The pipe command `walk_class` passes any raw address straight to Ubel::WalkClassEx (Fern.cpp:2255-2260). WalkClassImpl has no GObjects-membership check: it reads PropertiesSize and walks ChildProperties from whatever address it is given (Ubel.cpp:1004-1075). So a page reserved out of process with VirtualAllocEx(MEM_RESERVE) is exactly dll_core_test's decommitted class. Committing it afterwards and copying a real UClass's bytes into it reproduces the test's 're-commit, and the class walks'. For step 1's regression, DumperTest also offers DumperTestHolder (HolderIndex int32, HolderHealth struct), Spawn_Holders for Class Pivot, and bPlainBool at 0x661 on DumperTestActor.

**Preconditions:** build_git contains 84a8d8ca. The pipe rig runs with the UI CLOSED. Step 1 needs the AOT UI.

**Steps:**

1. 0. Launch shipping and inject; ensure_scanned (object_count ≈ 24.5k).
2. STEP A (live discriminator, pipe, UI closed): find_instances DumperTestActor gives class_addr C. walk_class C gives the baseline name plus the field list, N fields. VirtualAllocEx(MEM_RESERVE only, 0x10000) gives P, uncommitted. Call walk_class P, walk_class P again, and walk_class_batch [P,P]. Every reply must have empty fields. walk-0.log must gain EXACTLY ONE 'WalkClass: 0x<P> is not readable at +0x<USTRUCT_PROPSSIZE> — not a UStruct, or freed memory' and ZERO 'WalkClassEx: 0x<P> REFUSED' (that line is suppressed when readOk is false, Ubel.cpp:1302-1308).
3. STEP B (the memo is not poisoned): VirtualAllocEx(P, 0x1000, MEM_COMMIT, RW), then WriteProcessMemory 0x400 bytes copied from C (ReadProcessMemory C). walk_class P must now return C's name and the SAME field list as the baseline. Pre-fix, WalkClassEx had memoized {Address=P, PropertiesSize 0} for good, and this call returned the empty class. Then VirtualFreeEx(P).
4. STEP 1 (regression, UI): start the AOT UI. On the 'Properties' tab search `bPlainBool`: the DumperTestActor row at offset 0x661. On 'Value Search' search int32 90000: the LazyAnchors holder's DumperTestHolder.HolderIndex (DumperTestActor.cpp:445-446). PIPE-invoke Spawn_Holders(300) on DumperTestActor_0, then on 'Class Pivot' pivot DumperTestHolder, and its fields (HolderValue / bHolderFlag / HolderIndex / HolderHealth) must be present. In Live Walker on DumperTestActor_0, 'Copy CE XML' must list the fields.
5. STEP 2: grep walk-0.log for 'is not readable at +0x' and group by address: at most 1 line per address. There must be no 'WalkClassEx: … REFUSED' for any class the regression touched, and no 'WalkClass: 4096 unreadable class addresses reported'.
6. Teardown: kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| WalkClass: 0x%llx is not readable at +0x%X — not a UStruct, or freed memory | dll/src/Ubel.cpp:1052-1055 (once per address via FirstUnreadableClassWarn, :982-996) | Logs\DumperTest-Win64-Shipping\walk-0.log (WALK:safe). Exactly one per address |
| WalkClassEx: 0x%llx REFUSED (PropertiesSize=%d) — returning an EMPTY ClassInfo, so every caller will see this class as having no fields | dll/src/Ubel.cpp:1303-1306 (only when readOk) | walk-0.log. Must be ABSENT for P and for every normal class |
| WalkClass: %zu unreadable class addresses reported -- further ones are not logged | dll/src/Ubel.cpp:993-994 | walk-0.log. Should never appear in a normal session |
| {"class": {…fields…}} | dll/src/Fern.cpp:2255-2264 (walk_class → EncodeClassInfoToJson) | Pipe reply. Fields empty while P is reserved; the full list after the commit+copy |

**Row text vs source:** The row says 'No live trigger: the defect needs a transient read fault on a class pointer, which dll_core_test makes by decommitting a page.' The same thing can be done live: walk_class takes any raw address (Fern.cpp:2255-2260 → WalkClassEx), and an out-of-process VirtualAllocEx(MEM_RESERVE) page faults exactly as dll_core_test's does. Committing the page and copying a real UClass into it tests the 'not memoized' half against a running game. The quoted log text 'is not readable at +0x…' matches Ubel.cpp:1054.

**Rig:** None of the existing rigs drives walk_class on an unmapped address. New rig `l49_unmapped_class.py`, pipe only with the UI closed. It uses PipeClient (walk_class / walk_class_batch / find_instances) plus ctypes VirtualAllocEx, VirtualFreeEx, ReadProcessMemory and WriteProcessMemory (a11_step2_realloc.py:56-65 has the VirtualAllocEx declarations). It reads walk-0.log from the timestamp of its own first call, and must first show the channel carries the marker (mutate_guard.assert_channel_carries). Step 1 is computer-use on the Properties / Value Search / Class Pivot tabs and Copy CE XML.

**Traps:** • A green run of the regression alone (step 1) says nothing about this fix: a pre-fix build passes it too. STEP B is the only discriminator.
• Use MEM_RESERVE (uncommitted) for the fault, so that ReadSafe fails on the PropertiesSize read (Ubel.cpp:1040-1041). A committed zero page is 'readable, PropertiesSize 0', a different path; it is memoized as a legitimate empty class.
• The walk-0.log line is in walk-0.log (WALK:safe → LF_Walk, Sein.cpp:66-85), not offsets.
• A second walk of P via walk_class_batch goes through WalkClassesBatch → WalkClassEx. The once-per-address guard is process-wide, so any second line for P is a failure.
• The copied class at P is memoized after STEP B. That is harmless because nothing else ever looks P up, but do not reuse the address.
• UI holds 2 pipe lanes, so run the rig before the UI connects.

**Related rows:** L56, L18, L61, L28, L42, L43

### L50 — `[A2-LAZY-LATCH-GUESS]`

**Status:** ✅ PASSED red→green 2026-09-22 via the in-process 502 override (`git log --grep 'verify(L50)'`) · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 30 min

**Fix commit(s):** `8a22f413`

**Fixture:** DumperTest 5.4 Shipping. `UPROPERTY() TArray<TLazyObjectPtr<AActor>> Arr_LazyPtr` holds three DISTINCT spawned DumperTestHolders (HolderIndex 90000-90002; DumperTestActor.h:618-622, .cpp:436-455). The engine ElementSize is 0x18. The defect needs a version mis-resolved across the 5.2/5.3 line, and DumperTest can be pushed there in-process with `set_ue_version_override {version: 502, persist: false}` (Fern.cpp:1778-1850). That un-latches LAZYPTR_GUID (:1838-1840) and sets g_cachedUEVersion=502 without touching the persisted per-game cache. It is the mirror of the ledger's model (a 5.4 image read as 502): pre-fix, InferScalarSize's ≤5.2 guess 0x1C overrides the real 0x18. The literal fixture (a real UE 5.0-5.2 title with a lazy array) is fixture-limited. Candidates in docs/test-games.md are Palworld 5.1 (line 80) and the Satisfactory 5.2.1 depot build (line 84), both unverified for lazy arrays. A CORRECTLY resolved 5.0-5.2 title does not discriminate: guess and raw agree.

**Preconditions:** build_git contains 8a22f413. The UE5CEDumper.<MACHINE>.json cache entry for the current DumperTest-Win64-Shipping PE hash must show ueVersionUserOverrideAt "", i.e. no persisted override, both before and after the run. Never use the UI's System-tab 'Override:' combo for this: it always persists (PointerPanelViewModel.cs:865, persist: true).

**Steps:**

1. 0. Launch shipping, inject, ensure_scanned. `init` must show ue_version 504 and is_user_override false.
2. CONTROL (504): find_instances DumperTestActor, then walk_instance DumperTestActor_0 → Arr_LazyPtr. array_elem_size must be 24 and there must be 3 elements, each '{GUID} DumperTestHolder_<n> (DumperTestHolder)' with 3 different GUIDs. offsets-0.log: 'TLazyObjectPtr payload envelope measured: +0x08 (ElementSize 0x18 - payload 0x10, UEver=504)'.
3. ARM (502): pipe set_ue_version_override {"version":502,"persist":false}. The reply must be ue_version 502, is_user_override true, persisted false. pipe-0.log: 'set_ue_version_override: UE 502 — soft/lazy payload envelopes un-latched so they re-derive for this version'.
4. STEP 1: walk_instance DumperTestActor_0 again. Arr_LazyPtr must still have array_elem_size 24 (NOT 28) and the same 3 distinct GUIDs with resolved DumperTestHolder names, byte-identical to the control. Pre-fix fingerprint: elem_size 28, with [1]/[2] misaligned (garbage GUIDs, no resolved name).
5. STEP 2: offsets-0.log AFTER the override marker must hold exactly ONE lazy line: 'TLazyObjectPtr payload envelope measured: +0x08 (ElementSize 0x18 - payload 0x10, UEver=502)'. The ElementSize is the engine's own even though UEver is 502. Pre-fix wrote '+0x0C (ElementSize 0x1C - payload 0x10, UEver=502)'. No '<-- CHANGED' suffix is expected, because the override reset the latch to -1.
6. UI half: disconnect the pipe rig. The override lives in the process, so it survives. Start the AOT UI; the pointer panel shows the UE version as 502/override. In Live Walker, open DumperTestActor_0 (Instances tab 'DumperTestActor', or 'Go'). The Arr_LazyPtr row's Value (tooltip) must be '[3 x LazyObjectProperty (24B)] = [DumperTestHolder_a (DumperTestHolder), DumperTestHolder_b (DumperTestHolder), DumperTestHolder_c (DumperTestHolder)]'. The preview shows PtrName over Value (LiveFieldValue.cs:724-731). Drill the array to see each element's '{GUID} Name (Class)'.
7. Teardown: kill the game, which discards the in-process override. Re-check that the cache JSON has no override entry for this PE hash.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| %s payload envelope measured: +0x%02X (ElementSize 0x%X - payload 0x%X, UEver=%u)%s | dll/src/Ubel.cpp:412-416 (what = "TLazyObjectPtr", Ubel.cpp:442-445; category DYNO:PersistPtr) | Logs\DumperTest-Win64-Shipping\offsets-0.log (DYNO routes to LF_Offsets, NOT walk-0.log). Expected '+0x08 (ElementSize 0x18 - payload 0x10, UEver=502)' |
| set_ue_version_override: UE %u — soft/lazy payload envelopes un-latched so they re-derive for this version | dll/src/Fern.cpp:1842-1844 | pipe-0.log (PIPE:cmd). The marker to count lazy lines after |
| {%08X-%08X-%08X-%08X} <ptrName> (<ptrClassName>) | dll/src/Ubel.cpp:3435-3436 + 3460-3466 | walk_instance elements[].v for Arr_LazyPtr |
| [{ArrayCount} x {typeLabel} ({ArrayElemSize}B)] = [...] | ui/UE5DumpUI/Models/LiveFieldValue.cs:717-737 | Live Walker Value. Expected '[3 x LazyObjectProperty (24B)] = [...]' |

**Row text vs source:** None in the strings. The row names its fixture as 'a UE 5.0-5.2 game with a TArray<TLazyObjectPtr>'. Such a game only discriminates if its version is MIS-resolved. With a correctly resolved 501/502, the old guess (0x1C) and the raw ElementSize (0x1C) agree, so pre-fix and post-fix output are identical. The in-process override on DumperTest (5.4 read as 502) is the reachable discriminator.

**Rig:** Closest existing rig: tools/verify/a1_softlazy_envelope.py <ProcessFolder>. It finds lazy/soft properties by type, walks their owners, and reads the 'payload envelope measured' lines from offsets-0.log; its docstring warns the line is not in walk-0.log. tools/verify/d5_lazyguid_unread.py holds the Arr_LazyPtr baseline technique (3 GUIDs plus holder names). Neither sends the override. Smallest new rig, `l50_lazy_override.py` (pipe, UI closed): ensure_scanned → control walk → set_ue_version_override 502 persist:false → walk → assert elem_size 24, 3 distinct GUIDs and resolved names → read offsets-0.log from the override marker's timestamp and assert exactly one lazy line with 'ElementSize 0x18' and 'UEver=502'. Snapshot the cache JSON before and after to prove nothing persisted.

**Traps:** • The fixture header itself warns (DumperTestActor.h:618-621) that the lazy log line can be circular. Post-fix it prints the RAW size, but ELEMENT VALUES (three different valid GUIDs with names) stay the stronger witness.
• 'At most one line' holds PER LATCH EPOCH. The control at 504 writes one line and the override un-latches, so the 502 walk writes a second one. Count only after the override marker.
• persist:false only. A persisted override (the UI's combo, or persist:true) is saved per PE hash and re-applied on every launch (en.axaml:348; Flamme cache %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.<MACHINE>.json).
• The override does NOT re-run ValidateAndFixOffsets (Fern.cpp:1831-1837). Other version-derived paths now think 502 too. Judge only Arr_LazyPtr, and discard the process afterwards.
• Send the override AFTER the scan completes, so a later scan cannot re-derive over it; `init` is read-only (Fern.cpp:1743-1766).
• UI holds 2 pipe lanes, so do the rig before connecting the UI.

**Related rows:** L12, L44, L42, L43

### L51 — `[A2-CRC-PATH-LS]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** no · **estimate:** 40 min

**Fix commit(s):** `9bc177d8`

**Fixture:** EVERSPACE 2, per docs/test-games.md:12 ('EverSpace 2'). The install path recorded in its own logs is 'C:\Program Files (x86)\Steam\steamapps\common\EVERSPACE™ 2\…'. The ™ (U+2122) is a character above 0xFF, which is exactly the fix's condition (Genau.cpp:2875-2876). A read-only listing shows it ships '…\EVERSPACE™ 2\Engine\Binaries\Win64\CrashReportClient.exe'. The installation is to be re-checked by the later stage. ⭐ The pre-fix RED is still on disk: %LOCALAPPDATA%\UE5CEDumper\Logs\ES2-Win64-Shipping\scan-20260909-171715.log:9 is an EMPTY record, '[2026-09-09 17:17:11.113] [INFO] [SCAN:Ver] ' (DLL 1.0.0.3483), sitting between 'CrashReportClient ProductVersion -> UE 5.6 -> 506' and 'AGREE on 506'. Retention is 21 days, so copy it to out/ before about 2026-09-30. Alternative manufactured fixture, DumperTest Shipping: tools/verify/b29_nonascii_fixture.py create makes a real hardlinked directory D:\測試\DumperTest (junctions do not work, per its docstring). Plant a REAL copy of C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\CrashReportClient.exe at D:\測試\DumperTest\Engine\Binaries\Win64\ (crc_source_live.py's 'agree' source). That is within CRC_MAX_ANCESTORS=6 (Grimoire.h:873, candidate walk :877-891). Skip or remove b29's SBDR dxgi.dll wrapper.

**Preconditions:** Force a hint-cache MISS, or FindAll skips detection entirely. ES2's current record 0D281EF60A6D5000 has versionDetectRev 7 = kVersionDetectLogicRev (Genau.h:97), and every ES2 run since 2026-09-09 logged 'FindAll: UE Version = 506 (cached, rev=7 …) — skipped DetectVersion' (Genau.cpp:5094). To clear it, either use the UI Pointers tab 'Clear This Game' (str.Pointers.ClearGameCache, en.axaml:376; tip :1493) while connected, then restart and re-inject the game; or back up UE5CEDumper.%COMPUTERNAME%.json and drop the record by a json round-trip (crc_source_live.py drop_fixture_record precedent). For the hardlinked DumperTest, the PE hash equals the original Shipping exe's, so its records must be dropped the same way. Steam must be running for ES2 (handover §3).

**Steps:**

1. Copy Logs\ES2-Win64-Shipping\scan-20260909-171715.log to out/ as the before-half (it will be retention-swept).
2. Back up %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.%COMPUTERNAME%.json and drop the ES2-Win64-Shipping.exe record(s) for the installed PE.
3. Launch ES2 via Steam, wait until it has BOOTED (handover §3), then `py tools/verify/inject.py --name ES2-Win64-Shipping`. ES2 was last injected directly on 2026-09-17 (Module identity path dist\UE5Dumper.dll). If a VERSION.dll proxy is still deployed there, run `py tools/verify/proxy_refresh.py report` first.
4. Read Logs\ES2-Win64-Shipping\scan-0.log: the CrashReportClient line must carry the full path, including '™'. Check the bytes with Python: E2 84 A2 in UTF-8, and no empty '[SCAN:Ver] ' record.
5. Step 2 (regression only): with a version.dll proxy deployed in the non-ASCII tree, read init-0.log for the '[PROXY] Loaded real version.dll:' line. It names the System32 path, and 'Module identity: VERSION.dll | … | path:' names the non-ASCII proxy path.
6. Kill the game. Restore the JSON backup (or leave the fresh record, since a re-stamp is harmless, but say which).

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| DetectVersion: CrashReportClient at 'C:\Program Files (x86)\Steam\steamapps\common\EVERSPACE™ 2\Engine\Binaries\Win64\CrashReportClient.exe' -> 506 | dll/src/Genau.cpp:2879-2880 (the path is converted first at :2877) | scan-0.log [SCAN:Ver] (SCAN:Ver → LF_Scan, Sein.cpp:75) |
| DetectVersion: CrashReportClient and the game exe AGREE on 506 | dll/src/Genau.cpp:3080-3081 | scan-0.log, the line after (context/anti-vacuity: the CRC really was read) |
| MUST BE ABSENT: a bare '[INFO] [SCAN:Ver] ' record (the pre-fix red, ES2 scan-20260909-171715.log:9) | pre-fix Genau.cpp used %ls (9bc177d8 diff) | scan-0.log |
| Loaded real version.dll: C:\WINDOWS\system32\version.dll | dll/src/Lugner.cpp:81 (path from Lugner::SystemDllPath → GetSystemDirectoryW, Lugner.h:137-147) | init-0.log as '[PROXY]' (category PROXY is unmapped, so ResolveFile falls back to LF_Init, Sein.cpp:95-100). Observed verbatim in ES2 init-20260912-101207.log. |
| Module identity: VERSION.dll \| base=0x… \| path: C:\Program Files (x86)\Steam\steamapps\common\EVERSPACE™ 2\ES2\Binaries\Win64\VERSION.dll | dll/src/Heiter.cpp:404-409 | init-0.log (already UTF-8-safe before this fix; this is where the non-ASCII proxy path actually shows) |

**Row text vs source:** (a) Step 2 cannot discriminate this fix. 'Loaded real version.dll: %s' prints the SYSTEM32 library path (Lugner.cpp:66-81 via GetSystemDirectoryW), never the game folder. It was observed as 'C:\WINDOWS\system32\version.dll' under the EVERSPACE™ 2 install. A non-ASCII game folder therefore never reaches that line; it only would on a non-ASCII Windows directory. Step 2 is regression-only. (b) The row calls the CRC line's file 'scan.log': correct (SCAN:Ver → scan-0.log). The proxy line lands in init-0.log, not a proxy log. (c) The fix touched eight sites, not two (commit message). Only the two CrashReportClient lines carry a game-dependent path.

**Rig:** EXISTING, partly. tools/verify/crc_source_live.py plants a CRC and drops the hint record, but targets the ASCII Development path (FIXTURE_ROOT, :32). tools/verify/b29_nonascii_fixture.py builds the non-ASCII hardlinked Shipping tree. For ES2 no new rig is needed: a json-backup, drop-record, inject, then grep with a UTF-8 byte check. For the manufactured route: `py tools/verify/b29_nonascii_fixture.py create` + copy2 the UE_5.4 CrashReportClient into D:\測試\DumperTest\Engine\Binaries\Win64\ + drop the Shipping record + `b29_nonascii_fixture.py launch` + `inject.py --pid <pid>` + grep scan-0.log. Finish with `b29_nonascii_fixture.py clean`.

**Traps:** The hint cache is the big one: a cached, current-rev version skips DetectVersion, and the test then measures nothing (crc_source_live.py docstring; working-lessons §1.13). Do not plant anything into the SOURCE package: b29's tree is hardlinks, so only add NEW real files to the staged tree, never overwrite a linked file. A junction path is resolved by Windows and yields an ASCII path, a confident false PASS (b29 docstring). Read the log as UTF-8 bytes, because the cp950 console will mangle ™. ES2 is a Steam title: confirm it booted (object count) before judging. The retention sweep will delete the pre-fix evidence after 21 days.

**Related rows:** L85, L54, L69

### L52 — `[A2-HEAP-ANCHOR-TEXT]`

**Status:** ⬜ · **reachability:** `fixture-limited` · **needs CE:** no · **needs UI:** no · **estimate:** 30 min

**Fix commit(s):** `365326c6`

**Fixture:** The row's literal shape (GWLD_V3 matching in Bitdefender's atcuf64.dll on a game whose GObjects fell back to the data scan) is not reachable on any UE title, for two reasons. (1) No retained scan log shows a data-scan GObjects. (2) AdmitCandidate refuses only in Pass 2, which runs only for a pattern with ZERO main-module hits in a batch without a Pass-1 winner (Genau.cpp:1349-1355, :1436-1440). GWLD_V3 is '48 8B 1D ?? ?? ?? ?? 48 85 DB' (Himmel.h:752) at priority 900 (:2026), and it has hundreds of hits in any UE exe (Avowed scan log: 'GWLD_V3 hits=767'; Himmel.h:2021-2024 '95.7 per MB of .text'). The only recorded atcuf64 refusals were on python.exe (archive todo-closed-2026-08-23-build-3337.md:3919), where GObjects never validates: that is the None state, not Heap. REACHABLE BY STAGING, in generalized form, on DumperTest DEVELOPMENT (not Shipping, and this has to be said). Its cold scan refuses GNames Pass-2 candidates in 'EOSSDK-Win64-Shipping.dll' (DumperTest/scan-0.log 2026-09-16, lines 1491/1641/1788/1939/2357, currently with the monolithic text). Shipping's GNames wins GNAM_V8 in batch 1 and never reaches Pass 2. With FORCE_GOBJ from genau_rip_recovery_ab.py:78-91, GObjects comes from the data scan and lands on the HEAP (register :4047-4058, measured build 3313). Those same refusals must then carry the HEAP text.

**Preconditions:** (1) A staged DLL from HEAD with FORCE_GOBJ. The L40 staged DLL (FORCE_GOBJ + FORCE_GNAM) also works, because FORCE_GNAM zeroes the result only AFTER ScanForTarget has emitted its Pass-2 refusals. (2) A cold GNames scan: drop DumperTest.exe's hint record (PE 6AAA12E610F27000 caches GNAM_V1, and a hint HIT skips every batch pass, Genau.cpp:1196-1200) after backing up UE5CEDumper.%COMPUTERNAME%.json, or use the UI 'Clear This Game'. (3) Restore dist and the JSON afterwards.

**Steps:**

1. Build or reuse the staged DLL (see L40). Back up the machine JSON and drop the DumperTest.exe record(s).
2. `py tools/verify/launch_dumpertest.py dev`, then `py tools/verify/inject.py --name DumperTest --dll <staged>`.
3. Wait for READY. Grep Logs\DumperTest\scan-0.log for the anchor line and the REFUSED lines.
4. Classify: 'Module anchor set on the HEAP' present means the Heap arm. If the data scan found nothing at HEAD (validator hardening since build 3313), there is no anchor line and the refusals read 'never validated this run'. Record that as the Unanchored control, not a pass.
5. Step 3: the 'FindAll: Complete —' line's GWorld method is 'aob' and its address is inside DumperTest.exe (GWLD_TQ_1). No REFUSED line names atcuf64.dll (and if one appears, it must use the HEAP text).
6. Kill the game, restore the JSON, verify the dist SHA.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Module anchor set on the HEAP (GObjects lives in no module) — a later target resolving into a foreign module will be refused | dll/src/Genau.cpp:1688-1689 | scan-0.log [SCAN:GObj] |
| [GNames] GNAM_SF_1: REFUSED 8 match(es) resolving to 0x… in 'EOSSDK-Win64-Shipping.dll' — GObjects validated on the HEAP (a data-scan FUObjectArray lives in no module), so no module anchors this build; a match in a foreign module is not admissible | dll/src/Genau.cpp:1517-1521 (pattern ids and counts as in the 2026-09-16 cold scan: GNAM_SAT425_3 ×2, GNAM_SF_1 ×8, GNAM_CT1 ×7, GNAM_UD2 ×9, GNAM_PS1 ×11) | scan-0.log [SCAN] WARN |
| MUST BE ABSENT: '— GObjects never validated this run, so nothing has confirmed this process is the UE process; a match in an arbitrary loaded module is not admissible' | dll/src/Genau.cpp:1508-1512 | scan-0.log (present only if GObjects did NOT validate, which is the None control) |
| MUST BE ABSENT: 'Module anchor set to '(unknown)'' (the pre-fix anchor text) | 365326c6 diff of Genau.cpp:1684-1690 | scan-0.log |
| FindAll: Complete — GObjects=0x… (data_scan), GNames=0x… (…), GWorld=0x7FF6… (aob), … | dll/src/Genau.cpp:5327 | scan-0.log (step 3: GWorld from the main exe) |

**Row text vs source:** (a) The row's premise ('This PC, where GWLD_V3 matches inside atcuf64.dll, on a game whose GObjects falls back to the data scan') is not jointly satisfiable on a UE game. GWLD_V3 always has main-exe hits (Avowed: 767), so it never reaches Pass 2, the only place a foreign module is refused. The atcuf64 refusal was observed only on python.exe, where GObjects never validates (the None state, whose byte-identical text is kept on purpose). (b) Step 2's text check is reachable only generalized, with a different foreign module (EOSSDK) and target (GNames). (c) Step 3 is trivially true on any reachable fixture: GWorld wins in batch 1 by AOB, so it is regression-only. The admission rule itself is pinned by dll_helpers_test (16-row truth table, todo.md:3963-3966). (d) The row says 'scan.log': correct (SCAN and SCAN:GObj → scan-0.log).

**Rig:** No rig asserts this. Reuse genau_rip_recovery_ab.py's staging (FORCE_GOBJ, :78-91) and its build/restore helpers. New assertions, about 20 lines: anchor HEAP line present; at least 1 'REFUSED' line containing 'validated on the HEAP'; 0 lines containing 'never validated this run'; GWorld method aob. Can run in the SAME host launch as L40's control.

**Traps:** Staging makes the path RUN, not meaningful (register :4058+): the heap GObjects is a false positive, so never assert its address or count. At HEAD the data scan may no longer accept a heap candidate; then the run is the None control and must not be recorded as the Heap arm. The hint cache must be cold, or Pass 2 never runs. This row needs the DEVELOPMENT flavour, against the 'Shipping first' rule, because only Development's GNames reaches Pass 2 (EOSSDK); say so when recording. Back up and restore UE5CEDumper.%COMPUTERNAME%.json, since the staged run saves data_scan hints. Restore dist byte-exact (working-lessons §2.11). atcuf64's presence in the process is not re-checked here.

**Related rows:** L40

### L53 — `[A2-METHODE-MANUALMAP]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** yes · **needs UI:** no · **estimate:** 40 min

**Fix commit(s):** `953f2c3b`

**Fixture:** DumperTest **Shipping** as the throwaway target (`py tools/verify/launch_dumpertest.py shipping`, NO inject.py). The CE-plugin injection path is engine-independent, so Shipping-first costs nothing. Two separate launches (the row restarts the target between steps). Attach CE to the CHILD exe DumperTest\Binaries\Win64\DumperTest-Win64-Shipping.exe, never the 153 KB bootstrap stub (tools/ue-sample/README.md traps).

**Preconditions:** 'CE's plugin' in the row means UE5Dumper.dll registered AS A CE PLUGIN (Methode.cpp, menu 'UE5CEDumper: Inject && Connect'), not AOBMaker. It is currently NOT registered: HKCU\Software\Cheat Engine\Plugins64 holds only '.\plugins\AOBMaker_CEPlugin.dll' (enabled) and '.\plugins\CE-Handwire.dll' (disabled), read 2026-09-22. HKCU\Software\Cheat Engine\'Always Force Load' is currently REG_DWORD 0. The UI stays CLOSED for the whole row. Stale-DLL trap: Logs\cheatengine-x86_64-SSE4-AVX2\init-0.log (2026-09-21 08:01:58) and Logs\cheatengine-x86_64\ (2026-09-15/16) show a build **1.0.0.3315** (2026-08-22, pre-fix) UE5Dumper loaded INTO CE from an unidentified source. The plugin CE uses must be dist\UE5Dumper.dll = 1.0.0.3546.

**Steps:**

1. 1. `py tools/verify/ce_plugin_register.py status`, then `py tools/verify/ce_plugin_register.py register`. This adds dist\UE5Dumper.dll to Plugins64 as enabled, by absolute path, so no stale copy is involved. Close every CE, then start ONE.
2. 2. Open %LOCALAPPDATA%\UE5CEDumper\Logs\cheatengine-x86_64-SSE4-AVX2\init-0.log. The newest 'UE5Dumper DLL loaded | build: …' must read **1.0.0.3546**, followed by 'CEPlugin: InitializePlugin pluginid=… menuItemId=…'. If 3315 shows, STOP: the plugin under test is the pre-fix DLL.
3. 3. `py tools/verify/launch_dumpertest.py shipping` (do NOT inject). In CE, attach to DumperTest-Win64-Shipping.exe and read the title bar.
4. 4. In CE, open Edit → Settings → 'General Settings' tab and tick 'Always force load modules' (CE formsettingsunit.lfm:586, GeneralSettings tab :74), then OK. It takes effect immediately (formsettingsunit.pas:1027-1028 sets alwaysforceload), and HKCU 'Always Force Load' becomes 1.
5. 5. In CE's main menu, choose Plugins → 'UE5CEDumper: Inject & Connect' (g_MenuName 'UE5CEDumper: Inject && Connect', Methode.cpp:106). The expected ShowMessage is the two-possibility text below. Capture it as text, not as a screenshot paraphrase.
6. 6. Decide the branch from init-0.log in the CE-process folder, in this order: 'CEPlugin: OnInjectAndConnect triggered', 'CEPlugin: Injecting into PID=… | DLL=…\dist\UE5Dumper.dll | fn=UE5_AutoStart', 'CEPlugin: InjectDLL returned TRUE', 'CEPlugin: post-inject module check: NOT PRESENT (ok=1)', then the [WARN] ambiguity line. If it instead reads 'InjectDLL returned FALSE' plus the old 'Injection failed … (Cheat Engine also reported failure.)', CE's manual loader itself failed (ForceLoadModule re-raises, CEFuncProc.pas:768-811). Record that as 'step 1 not reached', NOT as a fail of the fix.
7. 7. Optional discriminator between the two possibilities the message names: check Logs\DumperTest-Win64-Shipping\init-0.log for a fresh 'UE5Dumper DLL loaded' line in the same second. If present, CE manual-mapped the image and its entry ran; if absent, nothing ran. DumperTest may crash (manual map processes no TLS or .pdata). That is expected; the message still comes from CE's side.
8. 8. Untick 'Always force load modules' (Edit → Settings → General Settings → OK). Kill DumperTest and confirm with `tasklist` that the Shipping image is gone. Relaunch shipping, attach CE, then Plugins → 'UE5CEDumper: Inject & Connect'. Expect 'UE5CEDumper: DLL injected — GObjects/GNames scan started in the background…' and log 'CEPlugin: post-inject module check: <…>\dist\UE5Dumper.dll (ok=1)'. Prove the host booted (UI still closed): `py tools/verify/pipe_client.py get_object_count` returns about 24k.
9. 9. Restore: confirm HKCU 'Always Force Load' = 0 (a winreg read). Run `py tools/verify/ce_plugin_register.py unregister`. Kill the game and CE.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| UE5CEDumper: Cheat Engine reported success, but the DLL is not in the target's\nmodule list. That means one of two things:\n\n  - the load failed after all (anti-cheat, a 32-bit target, or CE not running\n    as administrator), or\n  - CE force-loaded it by manual mapping, which it does for every injection\n    when Settings -> "Always force load modules" is ticked. A manually mapped\n    UE5Dumper.dll cannot handle exceptions, so the game is likely to crash:\n    untick that setting, restart the game, and inject again.\n\nDetails: Logs\UE5Dumper-*.log | dll/src/Methode.cpp:424-432 | CE ShowMessage dialog (step 1) |
| CEPlugin: InjectDLL TRUE but the module is absent — ambiguous: nothing was mapped, or CE's 'Always force load modules' manual-mapped it | dll/src/Methode.cpp:422-423 | %LOCALAPPDATA%\UE5CEDumper\Logs\cheatengine-x86_64-SSE4-AVX2\init-0.log [WARN] (LOG_CAT CEP routes to init.log, Sein.cpp:87) |
| CEPlugin: InjectDLL returned %s | dll/src/Methode.cpp:377 | same init-0.log ('TRUE' needed for the new branch) |
| CEPlugin: post-inject module check: %s (ok=%d) | dll/src/Methode.cpp:415-416 | same init-0.log ('NOT PRESENT (ok=1)' in step 1; the dist path with ok=1 in step 2) |
| UE5CEDumper: Injection failed — the DLL is not mapped in the target.\n\n(Cheat Engine also reported failure.) | dll/src/Methode.cpp:437-442 | CE dialog, the FALSE branch (step 1 NOT reached if seen) |
| UE5CEDumper: DLL injected — GObjects/GNames scan started in the background.\nIt takes a few seconds; connect UE5DumpUI.exe to \\\\.\\pipe\\UE5DumpBfx\n(check the log file if the scan fails on first run) | dll/src/Methode.cpp:448-451 | CE dialog in step 2 (the C literal renders as \\.\pipe\UE5DumpBfx) |
| Always force load modules | D:\Github\cheat-engine\Cheat Engine\formsettingsunit.lfm:586 (HKCU value 'Always Force Load', formsettingsunit.pas:1027) | CE Settings → General Settings checkbox |

**Row text vs source:** (a) 'with CE's plugin loaded' means the UE5Dumper.dll CE plugin (Methode), which is NOT registered on this machine today, so the row needs ce_plugin_register.py first. (b) Step 1 is only one of three outcomes: if CE's manual loader fails, ce_InjectDLL returns FALSE and the unchanged 'Injection failed' text is correct. (c) Both new and old dialogs end 'Details: Logs\UE5Dumper-*.log' (Methode.cpp:432, :442), a layout that no longer exists. Logs are per process: Logs\<process>\init-0.log (Sein.cpp:582-600), here Logs\cheatengine-x86_64-SSE4-AVX2\init-0.log. This is a user-facing stale string, a candidate finding.

**Rig:** EXISTING: `tools/verify/ce_plugin_register.py status|register|unregister` (HKCU Plugins64, absolute path to dist). NONE drives CE's Settings dialog or Plugins menu, so those need computer-use (CE is full tier). The 'Always Force Load' state can be read, never written, with a 5-line winreg read before and after. Evidence is file-based: the CE-process init-0.log plus the game's init-0.log.

**Traps:** (1) STALE PLUGIN: a build-3315 UE5Dumper has been loaded into CE at startup on 2026-09-15, -16 and -21 while Plugins64 has no UE5Dumper entry. Confirm the loaded build is 3546 before step 1 (step 2). (2) Two PERSISTENT config changes, the CE plugin registration and CE's 'Always Force Load', must both be restored. (3) While the plugin is enabled CE holds dist\UE5Dumper.dll open, which blocks any build.ps1 until unregister or CE exit. (4) Keep the UI closed: under force-load, ForceLoadModule resolves UE5_AutoStart and CreateRemoteThread's it (CEFuncProc.pas:793-798) inside an image with no TLS/.pdata, which can crash the game or bring up the pipe inside a manually mapped image. (5) IsAlreadyLoadedInTarget (Methode.cpp:303-316) short-circuits with 'already loaded' on a target that holds any of our modules, so each step needs a FRESH, never-injected process. (6) ce_InjectDLL also runs symhandler.reinitialize/waitforsymbolsloaded after injecting (pluginexports.pas:630-631). A target that crashes in between can turn TRUE into FALSE, which shows the old text. Read the log's 'returned' line before judging. (7) Attach to the child exe, not the stub. (8) One CE instance only (open_application spawns more).

**Related rows:** L54, L69, L85, L80

### L54 — `[A3-MIMIC-INIT-FASTPATH]`

**Status:** ✅ PASSED red→green 2026-09-22, both windows (`git log --grep 'verify(L54)'`) · **reachability:** `live` · **needs CE:** no · **needs UI:** no · **estimate:** 45 min

**Fix commit(s):** `420b1e53`

**Fixture:** DumperTest Shipping (`py tools/verify/launch_dumpertest.py shipping`), injected with dist\UE5Dumper.dll via inject.py. No CE is needed: `CommandRequiresInit` is true for every command except CMD_FOREGROUND and CMD_OFFSETS_VERDICT (Mimic.h:514), so a Python WriteProcessMemory poke of CMD_QUERY_PTR (13) or CMD_PROTECT (9) reaches EnsureInitialized (Mimic.cpp:484-496). b5_mailbox_race.py established this CE-free precedent. Two windows discriminate the fix. (A) WIDE, recommended: a RE-INIT. UE5_Shutdown never clears g_cachedGObjects/GNames (UE5_Shutdown, Frieren.cpp:658-690; ledger todo.md:4340-4342), and AutoStartWork restarts the mailbox poller BEFORE UE5_Init (Frieren.cpp:892). So pre-fix, EVERY poke during the whole re-scan took the fast path. (B) NARROW, the row's literal ask: on a first init, from 'FindAll: Complete —' (Genau.cpp:5327, scan-0.log) to 'UE5_Init: Complete (' (Frieren.cpp:607, init-0.log), measured at 190-445 ms (todo.md:4347). There are no enum, optional or container requirements: any UE process with a pawn works for CMD_PROTECT GET_STATE.

**Preconditions:** Resolve the mailbox address BEFORE the window (b5 lesson: interpreter start takes 0.5-1 s). Use mailbox_poke.py as a LIBRARY. Its CLI refuses unless initState==READY (mailbox_poke.py:176-178), which is exactly NOT the state during a (re)init (INIT_RUNNING=1, Mimic.h:288). Mark log offsets AFTER the process exists (b5 _fresh_log_marker note: *-0.log rotates on launch).

**Steps:**

1. Launch DumperTest Shipping and `py tools/verify/inject.py --name DumperTest-Win64-Shipping`. Wait until `py tools/verify/mailbox_poke.py DumperTest-Win64-Shipping` prints initState=2 READY and PASS. Anti-vacuity: `py tools/verify/pipe_client.py get_pointers` object_count ≈ 24.5K.
2. WINDOW A (re-init): the rig calls `py tools/verify/call_export.py UE5_Shutdown --process DumperTest-Win64-Shipping`, then `call_export.py UE5_AutoStart --process DumperTest-Win64-Shipping` (spawn-and-return, Frieren.cpp:852-866). Then it loops reading initState, and the moment it reads 1 (RUNNING) it calls MP.poke(mem, base, MP.QUERY_OP_GWORLD). The poke blocks on the poller until init ends. Record its send wall-clock and elapsed ms.
3. Repeat window A with poke(cmd=9, op=2) (CMD_PROTECT PROTECT_OP_GET_STATE, Mimic.h:236-242, read-only) for step 3. The FIRST reply must be result>=0 with params[2] resolvable=1, and a retry must give the same.
4. WINDOW B (first init, the row's literal step): relaunch the game fresh. Pre-resolve the mailbox as soon as the module is mapped (inject.py returns). Tail scan-0.log (the DLL fflushes every line, Sein.cpp:444-461) and poke the instant 'FindAll: Complete —' appears. Check that the poke's send time precedes 'UE5_Init: Complete (' in init-0.log.
5. Score each arm with the tally below (b5's three-outcome rule): PASS = exactly one 'Starting initialization' per init, a 'waiting' + 'resumed' pair and a successful reply. FAIL = two 'Starting' lines, or a reply that returns in about 1 ms during RUNNING with no waiting line. MISS = the poke landed after init ('Already initialized' only, or no auto-init line): re-stage, never record.
6. Kill the game.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Mailbox: auto-initializing (UE5_Init)... | dll/src/Mimic.cpp:493 | pipe-0.log (Mimic LOG_CAT 'PIPE' → LF_Pipe). Proves the fast path was refused. |
| UE5_Init: init already in progress on another thread — tid=<poller tid> is waiting (guard working, not an error) | dll/src/Frieren.cpp:144-145 | init-0.log [INIT]. Its timestamp must fall after the (re-)init's 'UE5_Init: Starting initialization...' (:165) and before its 'UE5_Init: Complete (' (:607); for window B, also after 'FindAll: Complete —'. |
| UE5_Init: tid=<same tid> resumed after waiting (first caller succeeded — returning its result, no second scan) | dll/src/Frieren.cpp:147-151 | init-0.log |
| UE5_Init: Already initialized | dll/src/Frieren.cpp:154 | init-0.log, right after 'resumed' |
| UE5_Init: Starting initialization... (exactly ONE per init) | dll/src/Frieren.cpp:165 | init-0.log |
| FindAll: Complete — GObjects=0x… (aob), GNames=0x… (aob), … | dll/src/Genau.cpp:5327 | scan-0.log (window B trigger) |

**Row text vs source:** (a) 'A command landing after the "Module anchor set" line … must produce a waiting line' names a window that is mostly NON-discriminating. 'Module anchor set' is logged INSIDE FindAll (Genau.cpp:1684-1690), while the globals are published only after FindAll returns (Frieren.cpp:172-173). A pre-fix DLL therefore also fell through to UE5_Init and logged the waiting line anywhere between the anchor line and 'FindAll: Complete —'. The discriminating first-init window starts at 'FindAll: Complete —' (Genau.cpp:5327). The widest discriminating window is a CE Disable → Enable re-init, which the row does not mention. (b) 'e.g. God Mode in the .CT': the shipped .CT has no God Mode record. (c) The 'waiting' line's full text is 'UE5_Init: init already in progress on another thread — tid=%lu is waiting (guard working, not an error)'; the row's elision is fine. (d) The row labels itself 'CE', but no CE is required (b5 precedent).

**Rig:** EXISTING precedent, not usable as-is: tools/verify/b5_mailbox_race.py (stage/race/clean). Its poke fires 120 ms after trigger_scan in proxy mode, i.e. INSIDE FindAll, before the globals are published (Frieren.cpp:172). A pre-fix DLL also waited there, so a b5 PASS does NOT discriminate this fix. Build a sibling rig of about 70 lines: import mailbox_poke as MP (mailbox_addr/Mem/poke) and call_export's logic (or subprocess it). Arm A: Shutdown → AutoStart → poll initState==1 → poke. Arm B: tail scan-0.log for 'FindAll: Complete' → poke. Tally the handshake lines as b5 does (:199-227), plus the timestamp ordering check.

**Traps:** The mailbox_poke CLI's READY gate (mailbox_poke.py:176-178) makes it useless inside the window, so use it as a library. After UE5_Shutdown, Mimic::StopThread memsets the mailbox (Mimic.cpp:460-472): re-read the address only if needed (it is the same export address). call_export.py finds its module by name 'UE5Dumper.dll' (--module), which is correct for injection and wrong for proxy mode (b5 docstring). A CE hotkey is the row's framing, but CE's own init record blocks CE's main thread while it polls for READY (CeReadinessLua.cs:55+), so a CE hotkey cannot fire during a CE-driven re-init; a Python poke is the instrument. Also, scripts/UE5CEDumper.CT has no God Mode record (only 'init' :12 and the hidden 'Inject DLL + Start Pipe Server' :915); God Mode records are UI-generated (ProtectionScriptGenerator). CMD_PROTECT op 0 would change game state: use op 2 GET_STATE only. Keep the hint cache warm (no need to clear it). A cold scan makes window A wider, but makes window B's tail the same length.

**Related rows:** L69, L85, L88, L53

### L55 — `[W5-DENKEN-DEADGUARD]`

**Status:** ✅ PASSED 2026-09-22 — A/B IDENTICAL over 2,978 functions (`git log --grep 'verify(L55)'`) · **reachability:** `no-live-trigger` · **needs CE:** no · **needs UI:** yes · **estimate:** 60 min

**Fix commit(s):** `20448583`

**Fixture:** Any game. DumperTest 5.4 Shipping is the calibrated host: L24 measured 2,945 of its game functions going through native disasm. ⚠ L24's numbers ('Props done: 191/2968 use class fields', disasm 2,945 / blueprint_no_script 17 / bytecode 6) are NOT a pre-removal baseline. That DLL (build_git 943975f3 per L9) already contains 20448583 (`git merge-base --is-ancestor 20448583 943975f3` is true), and the DumperTest package was repackaged 2026-09-17 (package-identity.json exe_mtime). So the regression has to be an A/B of two DLLs on the SAME package.

**Preconditions:** A 'before' DLL. Build it from HEAD with ONLY the removed guard put back: `git show 20448583 -- dll/src/Denken.cpp | git apply -R`, which is a one-hunk change of 7 lines in Denken.cpp:221-227, then `py tools/verify/build_dll.py --targets UE5Dumper`. build_dll.py does not touch dist\ or bump the build number (build_dll.py:25-27). Copy the built DLL aside (e.g. out\denken_before\UE5Dumper.dll), `git checkout -- dll/src/Denken.cpp`, rebuild, and check that `git status` is clean and the file has no NUL bytes (CLAUDE.md Git Operations). Record sha256 of both DLLs. Commit anything unsaved before building (Bitdefender).

**Steps:**

1. A-run: `py tools/verify/launch_dumpertest.py shipping`, then `py tools/verify/inject.py --pid <pid> --dll out\denken_before\UE5Dumper.dll`, then ensure_scanned. Pipe rig (UI closed): list_all_functions game_only=true, the same call InterestingFunctionsViewModel makes. Run walk_function_props on each function. Dump {func_full: {method, unmapped, budget_hit, props: sorted[(name, offset, confidence, occurrences, write_count)]}}. Kill the game.
2. B-run: FRESH DumperTest Shipping process, then `inject.py --pid <pid>` (default dist\UE5Dumper.dll, which contains 20448583) → the same dump.
3. Diff A vs B. Expect IDENTICAL method distribution, props lists, unmapped counts and budget_hit flags for every function. Also diff offsets-0.log's per-function 'AnalyzeNativeFunctionProps: … -> N mapped props (U unmapped, I instrs, C calls[, BUDGET])' tuples (N, U, I, C), matched in function order. Addresses differ under ASLR; the counts must not.
4. UI half (optional; the only UI surface that reaches Denken): Interesting Funcs tab → 'Load' → '⤋ Props' (confirm the dialog) on each build. The status 'Props done: X/Y use class fields …' must give the same X and Y for A and B. Spot-check one native row with the per-row 'Props' button: the dialog status carries '[native disasm — heuristic…]'.
5. Also run the unit pin: dll_helpers_test Test_Denken_FollowedImplOutlivesItsAlias (dll_helpers_test.cpp:4020, RUN at :8499) via `build.ps1 -Target Test`. ⚠ -Target Test republishes dist\UE5DumpUI.exe NON-trimmed, so re-run `-Mode Publish -NoBumpBuildNumber` afterwards and check size and SHA.
6. Teardown: kill the game each time. Delete or keep the out\ copy (gitignored), and confirm Denken.cpp matches HEAD.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| AnalyzeNativeFunctionProps: 0x%llX exec=0x%llX -> %zu mapped props (%d unmapped, %d instrs, %d calls%s) | dll/src/Aura.cpp:6489-6494 (', BUDGET' when budgetHit; LOG_CAT OARR) | Logs\DumperTest-Win64-Shipping\offsets-0.log, one per native function analysed |
| Props done: {withFields}/{targets.Count} use class fields | ui/UE5DumpUI/ViewModels/InterestingFunctionsViewModel.cs:369 | Interesting Funcs status after '⤋ Props' (en.axaml:908) |
|   [native disasm — heuristic[, N unmapped]] | ui/UE5DumpUI/Views/FunctionPropsDialog.cs:366-368 | Per-row Props dialog status for a native function |
| "method": "disasm" / "unmapped" / "budget_hit" / props[].confidence | dll/src/Fern.cpp:4939-4970 (walk_function_props) | Pipe reply compared A vs B |

**Row text vs source:** The row names 'a Live Funcs or Interesting Properties native-xref run', but neither surface reaches Denken. Denken's ONLY caller is Aura::AnalyzeNativeFunctionProps (Aura.cpp:6394-6445). It is reached only from WalkFunctionPropertyRefs' native fallback (Aura.cpp:6499-6509) via the pipe command walk_function_props (Fern.cpp:4939-4946). The UI sends that command only from the Interesting Funcs tab: the '⤋ Props' batch (InterestingFunctionsViewModel.cs:330) and the per-row 'Props' dialog (FunctionPropsDialog.cs:311, opened only from InterestingFunctionsViewModel.cs:272). Interesting Properties' xrefs go through find_property_xrefs → FindPropertyXrefs, which is a bytecode-only byte scan (Aura.cpp:5953-5961). Live Funcs never calls walk_function_props. Run the regression on Interesting Funcs, or directly over the pipe.

**Rig:** Closest existing rig: tools/verify/af7_budget_hit.py --attach. It already enumerates functions (list_all_functions, line 78) and runs walk_function_props over native ones, reporting the method distribution. Extend it to dump the full props JSON per function, and add a diff script. Related: af7_af8_pipe.py and af16_xref_fixture.py. The optional UI half is computer-use on Interesting Funcs 'Load' / '⤋ Props'.

**Traps:** • The guard was provably dead. TryFollow returns early for depth != 0 (Denken.cpp:104) and increments callsFollowed BEFORE recursing (:113-116), so depth>0 implies callsFollowed≥1. An A/B that shows identical output is the expected result, not a vacuous one. A difference would mean the invariant is false.
• Do NOT use L24's recorded counts as the 'before': different DLL lineage, different package.
• One DLL per process. You cannot swap the DLL inside a running game (inject.py's --allow-stale warns that a second inject is only a refcount bump). Relaunch between A and B.
• build_dll.py refuses to configure and needs a tree that build.ps1 already configured. Never build the 'before' DLL into dist\.
• -Target Test overwrites dist\UE5DumpUI.exe with the untrimmed exe (CLAUDE.md Build & Deploy). Re-publish.
• Patching through a heredoc can collapse escapes and write NUL bytes (CLAUDE.md). Use git apply -R, not a hand-written patch.
• UI holds 2 pipe lanes, so the dump rig runs with the UI closed.
• The tooltips str.Tip.IF.Props (en.axaml:900, 'Native functions have no bytecode and show nothing') and str.Tip.IF.BatchProps (:909) are stale about natives, which are analysed by disasm. L24 already recorded this as a false trail.

**Related rows:** L24, L31, L60

### L56 — `[A4-CDOSCOPE-ANCESTOR]` `[A4-CDOSCOPE-NESTED-PREVIEW]`

**Status:** ✅ PASSED red→green 2026-09-22 incl. the CE Freeze count (`git log --grep 'verify(L56)'`) · **reachability:** `live` · **needs CE:** yes · **needs UI:** yes · **estimate:** 30 min

**Fix commit(s):** `07b04894`

**Fixture:** DumperTest 5.4 Shipping (`py tools/verify/launch_dumpertest.py shipping`). It is a stock Third Person project (tools/ue-sample/README.md:5), so a live ACharacter subclass (the player character) exists. The Pawn/Character chain gives the ancestor case. For the nested case, ADumperTestActor has direct IntProperty fields `InvokeGate_LastArrayNum` and `InvokeGate_LastLabelNum` (DumperTestActor.h:711,722) AND `TArray<FDumperTestStrRow> Arr_StrRows` (DumperTestActor.h:764), whose element has `int32 Num` at +0x10 after an FString (DumperTestTypes.h FDumperTestStrRow). That is exactly a `Slots[].Count`-style leaf on the same class.

**Preconditions:** AOT `dist\UE5DumpUI.exe` (-Mode Publish, ~55 MB; check size/SHA). DLL injected with `py tools/verify/inject.py --pid <out/host.pid>`. get_object_count ~24.5k (not a dead engine). Back up `%LOCALAPPDATA%\UE5CEDumper\ui-options.json`: PropertySearch.GameClassesOnly and DeepSearch are persisted (MainWindowViewModel.cs:2494/2655). The Freeze arm only: CE running with the AOBMaker plugin (the Freeze button is disabled without it, PropertySearchViewModel.cs:40-43), plus `UE5_DEBUG = 1` in CE's Lua before ticking.

**Steps:**

1. Launch DumperTest Shipping and inject. With NO UI connected, run the pipe half first (the UI takes 2 of 3 pipe slots, handover-2026-08-22.md:261-262).
2. PIPE step 1 (ancestor): `search_properties query="EyeHeight" game_only=false limit=200` (Fern.cpp:2926-3031). Expect a row class_name=Pawn prop_name=BaseEyeHeight, and a row class_name=Character prop_name=CrouchedEyeHeight (UE 5.4 Character.h:520-522, Pawn.h:96-98). ⚠ Do NOT use `BaseEyeHeight` alone: with one preview class the pre-fix walk already credited Pawn, so it cannot discriminate.
3. Assert the Pawn row's `preview` ends with " (subclass instance)" and does NOT contain "(CDO default)". Control: the Character row also ends with " (subclass instance)" (the live character is a BP subclass). Pre-fix, Pawn read "<v> (CDO default)", because the nearest preview class, Character, swallowed the credit (old previewBaseOf, diff of 07b04894).
4. Same-instances cross-check without CE: `force_field class_name=Pawn field_name=BaseEyeHeight kind=numeric value=<the numeric part of the Pawn preview>`. Writing the value it already holds changes nothing in play. Record `held` (expect 1: the one character) and `truncated=false`. Then `reset_field class_name=Pawn field_name=BaseEyeHeight`, `reset_all_fields`, and `get_forced_fields` must be empty (freezescope_force_scope.py:83-120 pattern).
5. PIPE step 2 (nested): `search_properties query="Num" game_only=true deep=true limit=200`. Expect direct rows DumperTestActor·InvokeGate_LastArrayNum / InvokeGate_LastLabelNum WITH a `preview` key, and one row prop_name="Arr_StrRows[].Num" with `is_nested: true` and NO `preview` key (Fern.cpp:3009-3012). Pre-fix, the nested row previewed the int at inst+0x10, the low dword of UObject::ClassPrivate: a large garbage number.
6. UI: launch the AOT UI and Connect. Run `py tools/verify/front_window.py front UE5DumpUI`. On the **Properties** tab, UNTICK **Game classes only**, leave **Deep (structs/containers)** off, type `EyeHeight` and click **Search**. Read the Preview column of the Pawn row: `… (subclass instance)`.
7. UI Freeze arm (CE): select the Pawn row and click **Freeze**, entering the current value. In CE's Lua Engine, with UE5_DEBUG=1, the script prints `[Freeze] Started: Pawn::BaseEyeHeight = … on 1 instance(s)`. It must be the same count as Force's `held`. Untick the CE record afterwards.
8. UI step 2: tick **Game classes only** and **Deep (structs/containers)**, then search `Num`. The `Arr_StrRows[].Num` row has an empty Preview cell, while the InvokeGate_*Num rows show values.
9. Restore ui-options.json from the backup. Kill the UI, CE and the game.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
|  (subclass instance) | dll/src/Aura.h:844 (PreviewSourceSuffix) | The Pawn·BaseEyeHeight row's Preview column / the pipe `preview` value |
|  (CDO default) | dll/src/Aura.h:845 | Must NOT appear on the Pawn row (it was the pre-fix reading) |
| ✓ Holding {Class}::{Prop} = {what} on {N} instance(s). | ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:508 | Properties tab status after a UI Force value… (the pipe gives `held` instead) |
| [Freeze] Started: %s::BaseEyeHeight = %s (…@0x…) on %s instance(s) | ui/UE5DumpUI/Services/FreezeScriptGenerator.cs:159-163 (dbg-gated) | CE Lua Engine window, only when UE5_DEBUG=1 |
| SearchProperties '%s': %d matches from %d classes (scanned %d objects)%s  (suffix ', full sweep') | dll/src/Aura.cpp:5223-5228 (category PIPE:search) | %LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest-Win64-Shipping\pipe-0.log |
| Found {Total} properties in {classes} classes (scanned {objects} objects) [deep] | ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:614,640 | Properties tab status line (step 2 carries ' [deep]') |

**Row text vs source:** (a) Step 1's query `BaseEyeHeight` alone does NOT exercise the defect. With only Pawn in needPreviewClasses, the pre-fix previewBaseOf walked BP char → Character → Pawn and credited Pawn anyway (07b04894 diff, old Aura.cpp previewBaseOf). The query must also match a field declared on a NEARER class (use `EyeHeight` → Character.CrouchedEyeHeight). (b) 'Freeze on it reports the same instances' is only observable with UE5_DEBUG=1 (dbg-gated line). Otherwise use Force's held count. (c) Pawn/Character are engine classes, so the row silently requires 'Game classes only' OFF. (d) On DumperTest the `Slots[].Count` shape is `Arr_StrRows[].Num` with direct siblings `InvokeGate_LastArrayNum`/`InvokeGate_LastLabelNum`.

**Rig:** No existing rig drives this tag. The nearest template is tools/verify/freezescope_force_scope.py: search_properties + force_field + reset_field + reset_all_fields + get_forced_fields, over tools/verify/pipe_client.PipeClient. A new ~40-line rig: assert_build, ensure_scanned, the two search_properties calls above, suffix/`is_nested`/no-`preview` assertions, then the Force cross-check and cleanup. Run it BEFORE the UI connects. The UI and Freeze halves need computer-use.

**Traps:** (1) Game classes only defaults ON (PropertySearchViewModel.cs:51), which hides the Pawn row, and it is persisted, so restore ui-options.json. (2) Freeze's instance count is printed only through dbg() (FreezeScriptGenerator.cs:159-163). A clean success auto-closes the Lua window and shows nothing unless UE5_DEBUG=1. (3) The pipe needs the UI disconnected. (4) Force writes to the live character: use its current value and always reset_field + reset_all_fields. (5) If some other live APawn subclass whose nearest preview class is Pawn happens to exist, the pre-fix build would also have shown '(subclass instance)'. The post-fix check still holds, but record which pawns are live (Force's held). (6) Hand the AOT build over, not the plain one (CLAUDE.md Build & Deploy).

**Related rows:** L55, L58, L60, L71, L81, L61

### L57 — `[A4-LW-DISCONNECT-PARENT]` `[A1-DETECT-REPUBLISH]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 40 min

**Fix commit(s):** `e21bb297`

**Fixture:** DumperTest 5.4 SHIPPING, restarted (the row allows 'one game restarted'): `py tools/verify/launch_dumpertest.py shipping` + `py tools/verify/inject.py --name DumperTest`. Actor with an Outer = the live (non-Default__) ADumperTestActor (Outer = PersistentLevel); it carries dozens of UFunctions (Spawn_*, V8_*, MG2_*, AD4_*, InvokeGate_* — DumperTestActor.h:854-930) so the Functions list + filter have content. Step 2 needs >=2 USABLE snapshots in the current DumperTest Shipping per-game DB (read-only check today: Snapshots\snapshots.09FF6E55081C9000.db holds 1 snapshot, 16,928 fields); bigger snapshots (Game objects only OFF after Spawn_ManyComponents) widen the kill window.

**Preconditions:** AOT-trimmed dist (build.ps1 -Mode Publish; hash dist\UE5DumpUI.exe per working-lessons 2.5c). Experimental gate ON (System tab: 'Enable advanced experimental features', en.axaml:500) — Detect Stats and Snapshot tabs are IsVisible=ExperimentalEnabled (MainWindow.axaml:480-486). One injected game at a time.

**Steps:**

1. STEP 1 (Live Walker). Launch+inject DumperTest Shipping; start dist\UE5DumpUI.exe (py subprocess, never PowerShell); `py tools/verify/front_window.py front UE5DumpUI`; click Connect; header reads 'Connected — UE504 (N objects)'.
2. Instances tab -> find class DumperTestActor -> 'Open in Live Walker' (en.axaml:312) on the non-Default__ row. Confirm the 'Parent ↑' button and the 'Outer:' row (Level / PersistentLevel / address) are visible. Do NOT root at UWorld — HasParent is false there (todo.md:5083: X5's live PASS was rooted at UWorld and could not see this).
3. Click 'Find Refs' and wait for the references header ('References to <name> (N)' or '(none found)').
4. Expand the 'Functions' expander; type `Spawn` in its 'Filter by name...' box; the counter reads '<n> shown' with n>0.
5. Kill the game: `py -c "import subprocess;subprocess.run(['taskkill','/F','/IM','DumperTest-Win64-Shipping.exe'])"`. UI header -> 'Disconnected'. Immediately check: 'Parent ↑' button GONE, 'Outer:' row GONE, references panel GONE, 'Functions' expander GONE (all IsVisible bindings, see traps).
6. Relaunch + inject + Connect (same UI session). Before rooting anything, the four surfaces are still absent. Optional positive control: root on DumperTestActor again — the Outer address shown is the NEW process's, and Parent walks it.
7. STEP 2 (Detect). Snapshot tab: capture >=2 snapshots a few seconds apart (to widen the window: first invoke Spawn_ManyComponents(20000) via a pipe rig with the UI disconnected, and capture with 'Game objects only' unticked). Detect Stats tab: tick 'Use snapshot signal (...)'. Dry-run '🔎 Detect Player Stats' once and read the TX->RX gap of search_properties_batch in %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\pipe-0.log.
8. Arm the killer in the background: `py tools/verify/kill_on_marker.py "%LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\pipe-0.log" "search_properties_batch" DumperTest-Win64-Shipping.exe --after-ms <gap+50>` then click '🔎 Detect Player Stats'. The kill lands in the non-pipe snapshot-diff await (TryLoadDecreasedFieldsAsync), the recorded deterministic window.
9. After 'Disconnected': Detect Stats status == reset text and the grid is empty. Relaunch/inject/Connect: still the reset text and no rows; status must never read '<n> candidates · <m> confirmed …' or 'Detect failed — see logs.'.
10. Kill the game, close the UI (handover §4).

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Parent ↑ | ui/UE5DumpUI/Resources/Strings/en.axaml:250; button IsVisible="{Binding HasParent}" LiveWalkerPanel.axaml:48-53 | Live Walker toolbar — must disappear on disconnect |
| Outer: | ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:365-368 (IsVisible=HasParent) | Live Walker header row |
| References to {scanName} ({N})  /  References to {scanName} (none found) | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2878 / :2884; panel IsVisible=HasReferences LiveWalkerPanel.axaml:419-421; cleared by ClearReferences() :2919-2924 | Live Walker references panel — must disappear on disconnect |
| Functions  ·  '{0} shown' | en.axaml:1443; Expander IsVisible="{Binding HasFunctions}" LiveWalkerPanel.axaml:875; counter :895 | Live Walker Functions expander — hidden after disconnect (HasFunctions=false, LiveWalkerViewModel.cs:6192-6194) |
| Click Detect to shortlist likely HP / MP / Gold fields and confirm them live. | ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:124 (ClearOnDisconnect; identical to the initial value :67-68) | Detect Stats status line after the kill/reconnect |
| Detect failed — see logs. | DetectStatsViewModel.cs:298 (now guarded by gen == _detectGen) | must NOT be the final status |
| {n} candidates · {m} confirmed[ · snapshot: {note}]…  — reference only, verify before use. | DetectStatsViewModel.cs:288-293 | must NOT appear after the reconnect (the pre-fix republish) |
| Scanning candidate stat fields…  /  Confirming {class} ({i}/{n})… | DetectStatsViewModel.cs:135 / :198 | transient status while the run is live |
| need ≥2 usable snapshots | DetectStatsViewModel.cs:337 | snapshot note — if seen, the snapshot window was never open (precondition failed) |
| Pipe TX: {..."search_properties_batch"...} | ui/UE5DumpUI/Services/PipeClient.cs:232 (Debug is written: LoggingService.cs:93); cmd name DumpService.cs:1807 | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\pipe-0.log (mirror Logs\DumperTest-Win64-Shipping\ui-pipe-0.log) — kill_on_marker trigger |
| Pipe: ReadLine returned null (disconnected) | PipeClient.cs:344 | UI pipe-0.log at the kill |
| DetectStats live probe failed for {class} | DetectStatsViewModel.cs:219 | UI view-0.log — acceptable noise if the kill lands in the probe loop |

**Row text vs source:** 'Parent is disabled' -> source HIDES it (LiveWalkerPanel.axaml:48-53, :365-367 IsVisible=HasParent). 'Functions list is empty, including after typing in its filter' -> source hides the whole Functions expander (LiveWalkerPanel.axaml:875 IsVisible=HasFunctions, set false at LiveWalkerViewModel.cs:6194), so the filter is unreachable post-fix; check 'expander absent' instead. The reset happens at DISCONNECT (ResetPanelsOnDisconnect, MainWindowViewModel.cs:2099/2743-2769), not at reconnect — check both moments.

**Rig:** Existing: tools/verify/kill_on_marker.py (watches only bytes appended after start; taskkill /F /IM <proc> after --after-ms) — use it on the UI pipe log for step 2. tools/verify/inject.py / launch_dumpertest.py / front_window.py for setup. Nothing drives the Live Walker surfaces; step 1 is computer-use on the UI (read_page-style checks are not available for Avalonia, so screenshots/zoom). No existing rig greps for either tag.

**Traps:** (1) Row text vs source: 'Parent is disabled' — it is HIDDEN, not disabled (IsVisible=HasParent). 'Functions list is empty, including after typing in its filter' — post-fix the whole expander is hidden (IsVisible=HasFunctions), so the filter box cannot be typed into; the observable is 'expander absent'. The filter TEXT is deliberately not blanked (LiveWalkerViewModel.cs:6189-6191), so after rooting in the new game the old keyword still filters the new list — not a defect. (2) Root on an actor, not UWorld. Click Find Refs BEFORE the kill or there is no header to lose. (3) Step 2 ordering: the pipe failure continuation and the posted ConnectionStateChanged(false) (MainWindowViewModel.cs:2065-2100) race on the UI thread; post-fix every ordering ends at the reset text, but only a resume AFTER ClearOnDisconnect discriminates pre/post-fix — the non-pipe snapshot-diff await is the deterministic one, hence the marker+delay. With <2 usable snapshots the signal returns instantly and there is no window. (4) kill_on_marker kills EVERY DumperTest-Win64-Shipping.exe (taskkill /IM). (5) UI holds 2 of 3 pipe slots: any pipe rig (Spawn_ManyComponents invoke) must run before Connect or after Disconnect. (6) Game window steals focus — front_window.py before every click. (7) AOT-only build; experimental gate is persisted — leave it as found. (8) A process that exists is not a booted game — check the object count on connect (lesson 3.w).

**Related rows:** L58, L59, L66, L61, L65, L47, L73

### L58 — `[A4-STEALTH-PRIME]`

**Status:** ⬜ · **reachability:** `fixture-limited` · **needs CE:** no · **needs UI:** yes · **estimate:** 45 min

**Fix commit(s):** `a28ed440`

**Fixture:** STEP 1 needs a title where find_stealth_meter returns >=1 candidate: the local pawn (GWorld->GameInstance->LocalPlayers[0]->PC->Pawn) or one of its related objects (Aura::GetRelatedObjects, excluding Class/Outer) must carry a reflected Float/Double whose lowercased name scores >0 under Solide::MatchStealthField (Solide.h:148-176: stealth/visib/detect/noise/aware/conceal/camouflage/suspicio/exposure/expose/perceiv/sight/hidden/sneak/spotted/alert/threat/aggro, minus max/min/default/curve/config/radius/class/range/time/delay/rate), and you must be IN GAMEPLAY (no pawn -> FR_ERR_NO_TARGET -> empty list, Solide.cpp:525-534). DumperTest is NOT expected to reach it: no DumperTest header declares a stealth-keyword float (grep of tools/ue-sample/DumperTest/Source/DumperTest/*.h) and the pawn is the stock Third-Person template -> expect 'Not found'. Best documented candidate: The Adventures of Elliot (docs/test-games.md:93) — docs/archive/dev-log-2026-07-pre-build-2200.md:71-73 records a force_field on PointLightComponent::InverseExposureBlend there, a name the scorer accepts via 'exposure'; next Avowed (test-games.md:94). Probe before committing. STEP 2 is LIVE on DumperTest 5.4 Shipping.

**Preconditions:** AOT dist. Experimental gate ON (System tab 'Enable advanced experimental features') — the Stealth card and Property Search Force are gated (TeleportPanel.axaml:814, PropertySearchViewModel.cs:393). Game in gameplay with a pawn. Probe per candidate title with the UI disconnected: `py -c "import sys;sys.path.insert(0,'tools/verify');from pipe_client import PipeClient;c=PipeClient().connect();c.ensure_scanned();print(c.request('find_stealth_meter',max=8))"` -> code 0 and non-empty candidates.

**Steps:**

1. STEP 1 (candidate title). UI Connect. Teleport tab -> 'Stealth Meter' card -> 'Detect meter': badge 'Ready', readout '{Class}::{Field} = {v}', status 'Found N candidate(s). Top: … Verify it, then Hold @0.'
2. 'Hold @0': badge 'Holding @0' (green), status '✓ Holding {Field} = 0 on N instance(s) — …'. Game pipe log gains 'force_field: class=… field=… kind=numeric value=0.0000'.
3. Close the UI (game keeps running; the DLL hold survives a pipe drop). Relaunch the UI, Connect, and WAIT for the connect prime (stealth is the second-to-last of ~10 sequential reads, TeleportViewModel.cs:2404-2429). Card reads 'Holding @0' and the readout '{Class}::{Field} = 0 (held)'.
4. Click 'Reset': badge 'Off', status 'Released the stealth-meter hold.'; game pipe log gains 'reset_field: class=… field=…'; Properties tab 'Forced fields:' strip is empty. Optional M9 check: Hold again, then untick the experimental gate -> a reset_field line appears (gate-off releases a Holding card).
5. STEP 2 (DumperTest Shipping). Properties tab: search a numeric field that is NOT a meter candidate, e.g. `F32` on DumperTestActor -> right-click -> 'Force field (hold across instances)' -> 'Force value…' -> enter 0 -> 'Hold this value'. Status '✓ Holding DumperTestActor::F32 = 0 on N instance(s).'
6. Close+relaunch the UI (or Disconnect/Connect), Connect, wait for the prime: Teleport Stealth card reads 'Unknown' (a force exists, find_stealth_meter matched nothing).
7. Untick the experimental gate (System tab). The game pipe log must show NO 'reset_field:' for DumperTestActor/F32. Re-tick the gate: Properties 'Forced fields:' strip still lists it (refreshed on attach, PropertySearchPanel.axaml.cs:50), or with the UI disconnected `get_forced_fields` still returns it.
8. Control: Properties 'Clear all' (forced strip) -> reconnect -> card reads 'Off' (nothing forced). Restore the gate to its original state, kill the game, close the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Holding @0 | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:1794 (StealthHoldingState), applied by ApplyStealthState(1) :1802 from RefreshHeldStealthStateAsync :2482 | Teleport > Stealth Meter badge after the UI restart |
| {Class}::{Field} = 0 (held) | TeleportViewModel.cs:2481 | Stealth card readout after the prime |
| Unknown | TeleportViewModel.cs:1804 (ApplyStealthState(-1), reached at :2479 when no forced field matches a candidate) | Stealth badge in step 2 |
| Off | TeleportViewModel.cs:1803 (forced.Count==0 -> ApplyStealthState(0), :2464); Reset sets it at :1890 | Stealth badge after Reset / control |
| Released the stealth-meter hold. | TeleportViewModel.cs:1891 | Teleport status line after Reset |
| No stealth/visibility meter auto-found on the player pawn. Search Property Search (e.g. 'visib', 'detect', 'noise', 'aware') and Force → a numeric field to 0 instead. | TeleportViewModel.cs:1826-1828 (badge 'Not found' :1825) | what DumperTest is expected to show on Detect |
| force_field: class=%s field=%s kind=%s value=%.4f | dll/src/Fern.cpp:6022 (PIPE:cmd -> LF_Pipe, Sein.cpp:77) | %LOCALAPPDATA%\UE5CEDumper\Logs\<game exe>\pipe-0.log |
| reset_field: class=%s field=%s | dll/src/Fern.cpp:6050 | game pipe-0.log — must appear on Reset (step 1) and must NOT appear on gate-off (step 2) |
| ✓ Holding {Class}::{Prop} = {value} on {n} instance(s). | ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:~496-498 (ApplyForceAsync) | Properties status after the Force |
| Teleport prime 'stealth' failed — its badge stays Unknown | TeleportViewModel.cs:2452 | UI view-0.log if the prime throws (see finding in traps) |
| Stealth Meter / Detect meter / Hold @0 / Reset | en.axaml:1100,1103-1105 | card header and buttons |
| Force field (hold across instances) / Force value… / Hold this value / Forced fields: / Clear all | en.axaml:838,842,860,843,844 | Properties context menu, value dialog, forced strip |

**Row text vs source:** Strings match source. The row does not say the Property Search Force must be numeric AT 0 on a non-candidate field — without that, step 2 cannot catch the recorded unsafe variant. The fresh-start initializer 'Off' (TeleportViewModel.cs:1785) is not covered by the row and can show 'Off' before/instead of the prime's answer.

**Rig:** None drives this. Probe: a 3-line pipe_client call to find_stealth_meter per candidate title (UI disconnected — it takes 2 of 3 pipe slots). Verification of the DLL state: pipe `get_forced_fields` (pipe-protocol.md:600-602) after pressing Disconnect. tools/check_badge_prime_symmetry.py (gate 17d, run via `py tools/check_all.py`) is the static regression half. Card reads are computer-use.

**Traps:** (1) The Property Search Force in step 2 must be NUMERIC AT 0: the refuted unsafe fix keyed on 'any numeric job at 0' (todo.md:5170-5173); a nonzero force would not distinguish it, and neither would the pre-fix build (which read 'Off' and also released nothing). (2) The forced field must NOT be one of find_stealth_meter's candidates, or the prime correctly claims it as the hold. (3) FINDING (plausible residual): _stealthState is initialised to "Off" (TeleportViewModel.cs:1785) while every other badge starts "Unknown" (:587-826). On a fresh UI start — exactly step 1's restart path — the card reads 'Off' until the stealth prime (near the END of the sequential prime) lands, and if that prime throws PrimeOneAsync only logs (:2441-2453), leaving the false 'Off' over a live hold; gate 17d checks disconnect-vs-prime symmetry, not the initializer. Read the badge only after the prime completes. (4) Gate-off also forces Keep Foreground OFF and resets Fly / See-through (TeleportViewModel.cs:4684-4692); the gate is persisted — restore it. (5) Release every force afterwards (Clear all); holds survive a UI restart by design. (6) Hold resolves the class AND subclasses (A6), pool capped. (7) Game windows steal focus; AOT build only.

**Related rows:** L57, L56, L45, L60

### L59 — `[A4-PIVOT-CROSSGAME-ID]` `[W1-PIVOT-LOADCTS]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 50 min

**Fix commit(s):** `8037bc94`

**Fixture:** Game A = DumperTest 5.4 Shipping (`launch_dumpertest.py shipping`); game B = DumperTest58 Shipping (`launch_dumpertest.py shipping58`, a different project: ADumperTest58Actor, no DumperTestActor — launch_dumpertest.py:84-95). Different PeHash -> different per-game DB (snapshots.<pe_hash>.db, SnapshotStore.cs:72-73) whose ids both start at 1, and class lists that are distinguishable by name (two DumperTest configs of ONE project would give near-identical class lists and could not show 'the class list is B's'). Each needs >=3 snapshots. Steps 1-3 live; step 4 is regression-only unless manufactured (see traps).

**Preconditions:** AOT dist; experimental gate ON (Snapshot / SPC Query / Class Pivot tabs). Pre-stage B's snapshots FIRST (launch shipping58, inject, Connect, capture 3 on the Snapshot tab, kill). Then A: launch shipping, inject, Connect, capture until A has >=3. Choose the A pick so that its Id also exists in B's DB and is NOT B's newest — otherwise pre-fix and post-fix show the same thing.

**Steps:**

1. In A (same UI session throughout): Class Pivot -> 'Snapshot:' combo = an OLDER snapshot (not the newest); tick a non-default set in '🔍 Suggest targets' (e.g. the oldest two). Snapshot tab (Mode: Diff): Old = oldest, New = middle; switch Mode to 'Group (Multiple Values)': 'Snapshot:' = oldest, 'Compare with:' = middle. SPC Query tab: tick a non-default pair (e.g. untick the newest). Note the Ids/labels.
2. Kill A (py taskkill). Launch B (shipping58), inject, click Connect in the SAME UI. view-0.log gains 'SnapshotStore: active DB -> …snapshots.<B-pe>.db'.
3. Check B: Class Pivot 'Snapshot:' = B's newest; Suggest-targets ticks = B's two newest; the class list shows B's classes (DumperTest58Actor present, DumperTestActor absent) with status '{N} classes — filter + pick one to pivot.  (…)'. Snapshot tab: Old = B's 2nd-newest usable, New = B's newest usable; Group 'Snapshot:' = B's newest usable, 'Compare with:' empty. SPC: B's two newest ticked (first-visit default).
4. Step 3 (same-game SetEngineState on B): Extra Scan is not offered on DumperTest58 (all pointers found). Re-tick non-default picks in all three tabs on B, then either (a) click Disconnect then Connect on the still-running B (ApplyEngineState -> SetEngineState, same PeHash), or (b) System tab UE-version Override combo -> the detected version, then back to 'Auto' (RescanApplied fan-out, MainWindowViewModel.cs:721-737). All B picks survive.
5. Step 4 (W1-PIVOT-LOADCTS): in Class Pivot switch 'Snapshot:' to another B snapshot not yet class-loaded this session and immediately pick a class from the still-visible list; the class list must then refresh to the NEW snapshot's classes (status '{N} classes — …', not stuck on 'Loading fields…' / the old list). See traps: live window is ~ms.
6. Kill B, close the UI. If option (b) was used, confirm the Override reads 'Auto' before closing (persisted per game).

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| SnapshotStore: active DB -> {DatabasePath} | ui/UE5DumpUI/Services/SnapshotStore.cs:97 | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log on each connect (proves the DB switched) |
| {N} classes — filter + pick one to pivot.  ({k} field-set(s) cached · ~{m} MB heap) | ui/UE5DumpUI/ViewModels/ClassPivotViewModel.cs:691 / :715 + CacheNote :741-742 | Class Pivot status after the class list for B's snapshot applies |
| Loading classes… (first time for this snapshot) | ClassPivotViewModel.cs:700 | Class Pivot status during a cache-miss class load (step 4 window) |
| {Label}  ·  yyyy-MM-dd HH:mm:ss | ui/UE5DumpUI/Models/SnapshotModels.cs:77-85 (PickerDisplay) | every Diff / Group / Pivot snapshot picker line — identify picks by label+time |
| Snapshot: / Old: / New: / Compare with: / Mode: Diff \| Group (Multiple Values) | en.axaml:696, 556, 557, 598, 586-588 | Class Pivot and Snapshot tab pickers |

**Row text vs source:** Step 3 says 'An Extra Scan on B' — Extra Scan is not offered on any fully-resolved game (PointerPanelViewModel.cs:467-468); a same-game SetEngineState (Disconnect/Connect, or the UE Override combo's RescanApplied) is the reachable equivalent. Step 4's race window is ~ms on current data because class lists come from precomputed class_counts; the row implies a human can hit it.

**Rig:** None. Staging uses existing launch_dumpertest.py (shipping / shipping58), inject.py, front_window.py; the checks are computer-use on three tabs. Optional read-only oracle of each DB's ids: sqlite `file:<db>?immutable=1` SELECT id,label FROM snapshots (UI closed or immutable).

**Traps:** (1) Extra Scan is only offered when GObjects is missing or GWorld is not_found (PointerPanelViewModel.cs:467-468) — never on DumperTest/DumperTest58; use Disconnect/Connect on B (primary; _listedPe survives ClearOnDisconnect — it is only written in the three RefreshAsync methods) or the UE Override combo (persisted per game, 'Saved per-game and reapplied on every launch' en.axaml:348 — must end on Auto). The literal Extra Scan path needs a title with a missing pointer. (2) Step 4 is practically unreachable live: ListPivotClassesAsync reads precomputed class_counts (SnapshotStore.cs:2159-2165) built at capture finalize (:710-711); a read-only check today found pivot_index_built rows for EVERY snapshot in every DB on this machine, so the class load finishes in ms. To make it discriminating, manufacture a slow lazy build (delete one big snapshot's pivot_index_built rows — the lazy path rebuilds them — only with maintainer consent, on a DumperTest DB) or treat step 4 as a regression check. (3) The A pick Id must exist in B and not be B's newest, else the check is vacuous (lesson 1.1). (4) One injected game at a time: kill A and confirm it is gone before injecting B (lesson 3.9). (5) Group 'Compare with' auto-fills only on a Diff->Group switch with exactly 2 snapshots (SnapshotViewModel.Group.cs:144-154), not on refresh. (6) Persisted options touched: SPC join mode/rounding, Pivot source/key mode (MainWindowViewModel.cs:2700-2707). (7) AOT build; focus stealing.

**Related rows:** L65, L66, L61, L57

### L60 — `[A4-GAMEONLY-ADVICE]` `[P5-GROUP-ADVICE]` `[A3-CONTAINER-4096-ADVICE]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 50 min

**Fix commit(s):** `b660d34e`

**Fixture:** DumperTest 5.4 Shipping + the AOT UI. Step 1: no installed title reaches the 100,000 list_all_functions cap (verification-register.md:3465-3495), so the cap is lowered with the env var UE5DUMP_ALLFUNCS_LIMIT (DumpService.cs:2709-2733). Step 2: in group mode the result cap is the single-mode Max (min 100), and DumperTest's ~24.5k objects hold far more than 100 objects with two zero leaves. Step 3: DumperTest has NO TArray<float> over 4,096 (Deep_Buckets inner Leaves clamp to 400, DumperTestActor.cpp:963-965). `V1a_GrowContainers(int32 Count)` is UNCLAMPED (DumperTestActor.cpp:695-700, h:494) and appends Count ints to `TArray<int32> Arr_Churn` (seeded {7001..7004}, cpp:165). Growing it by 5000 gives a 5,004-element scalar array, which takes the same scalar re-fetch branch.

**Preconditions:** AOT UI. DumperTest Shipping injected. Back up ui-options.json: InterestingFuncs.GameOnly, Console.GameOnly, ValueSearch.GameOnly/MaxResults/SelectedDataType/ScanType, and Main.ArrayLimitExponent are all persisted (MainWindowViewModel.cs:2515-2523, 2588-2589, 2608, 2638-2677). The UI is single-instance, so close any running copy before relaunching it with the env var, or the second launch silently does nothing.

**Steps:**

1. PIPE PREP (UI not connected). `list_all_functions game_only=true limit=100000` → note total G. Pick LIMIT < G (e.g. G/2) so that Game Only ON also caps.
2. PIPE PREP. find_live_actor (tools/verify/ad4_contested.py:73-85), then `invoke_function func_name=V1a_GrowContainers instance_addr=<actor> parms_size=4 params_hex=(5000).to_bytes(4,'little').hex()` (the v1a_container_realloc.py:168-170 pattern). Confirm with `walk_instance addr=<actor>` that Arr_Churn's count is 5004.
3. Launch the UI with the env var, without PowerShell: `py -c "import os,subprocess,pathlib; e=dict(os.environ, UE5DUMP_ALLFUNCS_LIMIT='<LIMIT>'); subprocess.Popen([str(pathlib.Path('dist/UE5DumpUI.exe').resolve())], cwd='dist', env=e)"`. Then front_window.py front UE5DumpUI and click Connect.
4. STEP 1a: **Interesting Funcs** tab, **Game Only** ticked (the default, InterestingFunctionsViewModel.cs:106), click **Load**. The status must end `⚠ STOPPED at the <LIMIT>-row cap — more functions exist; scan a narrower game`, with no mention of "Game Only".
5. STEP 1b control: untick Game Only and Load again. The status now ends `…; tick "Game Only" to skip engine classes, or scan a narrower game`.
6. STEP 1c: **Console** tab, **Game Only** unticked (default false, ConsoleViewModel.cs:80), click **Load**. The status must end `…; tick "Game Only" to skip engine classes so the cap reaches further into the game's own classes`. Control: tick Game Only and Load. It ends `…; "Game Only" is already on, so the rest are the game's own classes past the cap`.
7. STEP 2: **Value Search** tab, **Single value** mode. Untick **Game classes only**, set **Max:** to 100 (the NumericUpDown minimum, ValueSearchPanel.axaml:209-213). Flip the toggle to **Multiple values (group)**. Two rows: NumericNoByte / Exact / `0` and NumericNoByte / Exact / `0`. Click **Group First Scan**. Status: `Group First Scan: 100 matching objects in <ms> ms (scanned … objects, … classes)  ⚠ truncated (25s deadline / result cap) — raise the Timeout slider (deadline) or use more / more distinctive values (result cap)`. Assert there is NO 'refine' in it, and that ms is far below the Timeout, which shows a cap stop rather than a deadline.
8. STEP 3: **Instances** tab. Search `DumperTestActor`, open the live instance (not Default__) in **Live Walker**, find `Arr_Churn` and drill into it. StatusText must be `Showing the first 4,096 of 5,004 elements — this view is capped at 4,096 per fetch.` and the crumb `  ⚠ showing 4,096 of 5,004`. It must NOT say 'raise the "Array Limit" slider'.
9. STEP 3 control (slider-governed): the pointer array `SpawnedHolders` after `Spawn_Holders(Count=200,bDerived=false)`. Invoke it in the pipe prep, reading ParmsSize from the function list; do not assume 8. With **Array Limit:** at the default 2^7=128, drilling it must show `Showing the first 128 of 200 entries — raise the "Array Limit" slider in the toolbar and re-open this container to read more.`
10. Regression arm: close the UI, relaunch WITHOUT UE5DUMP_ALLFUNCS_LIMIT, and Load both panels. There must be no cap suffix.
11. Restore ui-options.json. Kill the UI and the game (Arr_Churn/Map_Churn stay grown until restart).

**Expected strings:**

| text | source | where it appears |
|---|---|---|
|   ⚠ STOPPED at the {cap:N0}-row cap — more {thing} exist; {advice} | ui/UE5DumpUI/Core/PartialResultNotice.cs:49-50 | Suffix of the Interesting Funcs / Console status lines |
| scan a narrower game | ui/UE5DumpUI/ViewModels/InterestingFunctionsViewModel.cs:673 | IF advice when the scan ran with Game Only ON |
| tick "Game Only" to skip engine classes, or scan a narrower game | ui/UE5DumpUI/ViewModels/InterestingFunctionsViewModel.cs:674 | IF advice when Game Only was OFF (control) |
| tick "Game Only" to skip engine classes so the cap reaches further into the game's own classes | ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs:295-296 | Console status, Game Only OFF |
| "Game Only" is already on, so the rest are the game's own classes past the cap | ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs:294 | Console status, Game Only ON (control) |
| {Total:N0} functions from {M:N0} of {N:N0} classes  ({k:N0} above threshold {T}, scanned {S:N0} objects){capSuffix} | ui/UE5DumpUI/ViewModels/InterestingFunctionsViewModel.cs:679-682 | Interesting Funcs status line |
| ListAllFunctions: total=… (gameOnly=…, truncated=True, aborted=False) | ui/UE5DumpUI/ViewModels/InterestingFunctionsViewModel.cs:501-504 | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log |
| Console.Load: exec=… of total=… (gameOnly=…, …, truncated=True, aborted=False) | ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs:322-324 | UE5DumpUI view-0.log |
|   ⚠ truncated ({ScanTimeoutSeconds}s deadline / result cap) — raise the Timeout slider (deadline) or use more / more distinctive values (result cap) | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:1468 | Value Search status row after a capped Group First Scan |
| Showing the first {received:N0} of {total:N0} {unit} — this view is capped at {received:N0} per fetch. | ui/UE5DumpUI/Core/ContainerTruncation.cs:59-62 via LiveWalkerViewModel.cs:1396-1398 | Live Walker status after drilling Arr_Churn (unit=elements) |
|   ⚠ showing {received:N0} of {total:N0} | ui/UE5DumpUI/Core/ContainerTruncation.cs:34-37 | Live Walker breadcrumb label |
| Showing the first {received:N0} of {total:N0} entries — raise the "Array Limit" slider in the toolbar and re-open this container to read more. | ui/UE5DumpUI/Core/ContainerTruncation.cs:43-46 | Only on the slider-governed control (pointer array), never on Arr_Churn |

**Row text vs source:** (a) Step 3 names `TArray<float>`, but DumperTest has none over 4,096. The branch is chosen by 'not pointer/struct' (LiveWalkerViewModel.cs:2061-2069, 1368-1375), so TArray<int32> Arr_Churn grown via V1a_GrowContainers(5000) is the equivalent host. (b) Step 1's 'capped Interesting Functions load' is unreachable on any installed title without UE5DUMP_ALLFUNCS_LIMIT. (c) Step 2: the cap in group mode is the hidden single-mode Max, so the row's 'capped group First Scan' needs Max lowered in single mode first. The current text matches the row ('use more / more distinctive values', no 'refine').

**Rig:** No existing rig for these tags. Pipe prep reuses tools/verify/ad4_contested.py (`find_live_actor`, `invoke`) as tools/verify/v1a_container_realloc.py:168-170 does, plus `list_all_functions`. All three status checks are UI text: computer-use, reading StatusText by zoom, with front_window.py before every click.

**Traps:** (1) The env var is read ONCE at UI startup (static readonly, DumpService.cs:2730-2733). A UI already running ignores it, and the single-instance mutex swallows a second launch. (2) LIMIT must be below the game-only function count, or step 1a never caps. (3) Max and Game classes only exist only in the single-mode panel (ValueSearchPanel.axaml:153,209). Group mode silently inherits them, and both are persisted: restore them. (4) The UI holds 2 of 3 pipe slots, so do the invoke prep first. (5) The DLL per-request cap is kArrayElementsPerRequestCap=4096 (Ubel.cpp:42). Scalar arrays ≤4096 show no status at all, so only >4096 reaches the fixed-cap wording. (6) N0 formatting follows the UI culture (4,096). (7) AOT build only.

**Related rows:** L56, L61, L71, L81, L70

### L61 — `[W1-DT-TRUNC]` `[P5-PIVOT-FETCHCAP]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 60 min

**Fix commit(s):** `8157b31c`

**Fixture:** STEP 1: DumperTest 5.4 Shipping — Table_Big (a runtime UDataTable of FDumperTestTableRow) rebuilt past Class Pivot's 2,000-row page with V8_RebuildBigTable(2500) (clamped 1..5000, DumperTestActor.cpp:721-724; rows named Row_%03d, BuildTable :626-658). The default 100-row / 8-row tables are the negative controls. STEP 2 (fetch cap) is fixture-limited: one class in one snapshot must yield >=2,000,000 fetched rows (instances x fetched props). Possible on DumperTest only by inflation: Spawn_ManyComponents(20000) (clamped per call, registered so they persist, DumperTestActor.cpp:930-950) repeated k times, captured with 'Game objects only' OFF; compute instances x field count before running.

**Preconditions:** AOT dist; experimental gate ON (Class Pivot tab). The table rebuild is a pipe invoke, so run it with the UI NOT connected: `py -c "import sys;sys.path.insert(0,'tools/verify');from pipe_client import PipeClient;from ad4_contested import find_live_actor,invoke;c=PipeClient().connect();c.ensure_scanned();a=find_live_actor(c);invoke(c,a['addr'],'V8_RebuildBigTable',parms_size=4,params_hex=(2500).to_bytes(4,'little').hex());c.close()"` (the same call shape v8_datatable_cap.py:150-151 uses for 77).

**Steps:**

1. Launch+inject DumperTest Shipping; run the V8_RebuildBigTable(2500) invoke above; optionally confirm with walk_datatable_rows limit=3000 -> row_count 2500.
2. Start the UI, Connect. Class Pivot -> 'Source:' = DataTable -> '🔄 Find DataTables' (status '{n} DataTable(s) found') -> 'DataTable:' pick DumperTestTable_Big_<highest serial> (older serials may linger until GC).
3. Load status must read '<RowStruct>: 2,000 rows (showing 2,000 of 2,500) · key = RowName'.
4. Click 'Run Pivot': status must read '2,000 rows (showing 2,000 of 2,500) · key = RowName · <RowStruct>' (pre-fix: '2,000 rows · key = RowName · <RowStruct>').
5. Control: pick DumperTestTable_Small_<n> (8 rows) or rebuild Table_Big to 100 -> neither the load nor the Run status carries '(showing …)'.
6. STEP 2 attempt (optional, heavy): with the UI disconnected invoke Spawn_ManyComponents(20000) k times; Snapshot tab: untick 'Game objects only' and 'Auto detect Engine/System noise', 'Max size:' Off, quota >= the resulting DB; Capture Snapshot. Class Pivot -> Source 'Snapshot', pick that snapshot, class SceneComponent (read its instance count in the picker and '{F} fields' in the status); key mode 'Identity (object path)'; UNTICK every value field (then every prop is fetched). Only if instances x F >= 2,000,000: 'Run Pivot'.
7. Expected (fetch cap alone): '≥ {G} groups from ≥ {I} instances · identity  ⚠ the snapshot read stopped at its 2,000,000-row fetch cap, so these counts cover a prefix and are not totals — tick only the fields you need'; with the group cap too, ' (capped at 5,000)' legitimately appears after 'groups'. view-0.log gains the Warn line.
8. Restore 'Source:' to Snapshot (persisted); delete the inflated snapshot(s) or keep them for L66.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| {structName}: {Rows:N0} rows (showing {Rows:N0} of {RowCount:N0}) · key = RowName | ui/UE5DumpUI/ViewModels/ClassPivotViewModel.cs:951-953 (LoadDataTableFieldsAsync; page = DataTableRowLimit 2000, :82 / :926) | Class Pivot status after picking the DataTable -> expect '…: 2,000 rows (showing 2,000 of 2,500) · key = RowName' |
| {GroupCount:N0} rows (showing {Rows:N0} of {RowCount:N0}) · key = RowName · {RowStructName} | ClassPivotViewModel.cs:1100-1104 (the [W1-DT-TRUNC] fix); GroupCount = dt.Rows.Count (DataTablePivotEngine.cs:29) | Class Pivot status after Run Pivot |
| {≥ }{G:N0} groups{ (capped at 5,000)} from {≥ }{I:N0} instances · {keyDesc}  ⚠ the snapshot read stopped at its 2,000,000-row fetch cap, so these counts cover a prefix and are not totals — tick only the fields you need | ClassPivotViewModel.cs:1061-1070 (PivotRunStatus), called :1139 (snapshot) / :1122 (array: '{key} group(s) … elements · {field}'); FetchCap set at SnapshotStore.cs:2260 / :2420; MaxGroups 5000 Constants.cs:314 | Class Pivot status after a fetch-capped Run |
| Pivot: row fetch hit the 2,000,000 cap for class {class} — results truncated | ui/UE5DumpUI/Services/SnapshotStore.cs:2253-2255 (Warn, LogCatView) | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log |
| 🔄 Find DataTables / DataTable: / Source: / Run Pivot | en.axaml:699, 697, 695, 705 | Class Pivot controls |
| {n} DataTable(s) found | ClassPivotViewModel.cs:897 | Class Pivot status after Find DataTables |

**Row text vs source:** Step 1's '64' is the Live Walker/DLL page, not Class Pivot's: Class Pivot pages 2,000 rows (ClassPivotViewModel.cs:82, :926), so the notice is '(showing 2,000 of N)' and needs a table > 2,000 rows. Step 2's '≥ … groups … from ≥ …' and the fetch-cap sentence match PivotRunStatus; '(capped at 5,000)' is still legitimately printed when the group cap fires TOGETHER with the fetch cap.

**Rig:** Existing pieces: tools/verify/v8_datatable_cap.py (DLL half of V8, same fixture; its invoke shape is reused above) and ad4_contested.find_live_actor/invoke. No rig drives Class Pivot; the status reads are computer-use. Step 2 would need an ad-hoc invoke loop for Spawn_ManyComponents plus a read-only sqlite count (`SELECT COUNT(*) FROM fields WHERE snapshot_id=? AND class_fqn=? AND array_field IS NULL`, immutable=1) to prove >=2,000,000 BEFORE clicking Run (lesson 2.10: an absent notice proves nothing until the channel can carry it).

**Traps:** (1) Row says 'over 64 rows … (showing 64 of N)' — WRONG for Class Pivot: it requests 2,000 rows (DataTableRowLimit, ClassPivotViewModel.cs:82 -> :926); 64 is the DLL default and Live Walker's drill-down page (Fern.cpp:5459, Ubel.h:1154). DumperTest's stock 100-row Table_Big shows NO notice in Class Pivot, which would read as a fail/absence. (2) The invoke needs a pipe slot — the UI takes 2 of 3; invoke before Connect or after Disconnect. (3) Pick the highest-serial DumperTestTable_Big_N; the replaced table can survive until GC and is 100 rows. (4) 'Source:' and key mode are persisted (MainWindowViewModel.cs:2704-2707) — restore. (5) Step 2: any ticked value field adds a prop_name IN filter (SnapshotStore.cs:2226-2237), so untick all; Field key mode additionally needs a key field (CanRunPivot :218-222); 'Game objects only' would drop engine-class SceneComponents; a >=2M-row snapshot is roughly a 450+ MB DB — keep quota >= that or FIFO eviction deletes older snapshots (SnapshotViewModel.cs:986-994). (6) AOT build; focus.

**Related rows:** L66, L59, L65, L57

### L62 — `[W1-WINMM-LOADMODE]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 35 min

**Fixture:** OCTOPATH TRAVELER, UE4.18. Source: docs/test-games.md:14, which says 'PROXY: use winmm.dll — LIVE-VERIFIED 2026-08-18'. Its winmm proxy is ALREADY DEPLOYED at D:\SteamLibrary\steamapps\common\OCTOPATH TRAVELER\Octopath_Traveler\Binaries\Win64\winmm.dll (2,988,544 B, FileVersion 1.0.0.3546, sha256 05a66930…, mtime 2026-09-16 12:42). That is after fix 723cb8f1 (2026-09-12), and no dll/src commit has landed since 2026-09-16. A heuristic agrees the fix is in it: the file holds 3 'winmm.dll\0' literals, the same as dist\proxy\winmm.dll, while the pre-fix 1.0.0.3263 backup holds 2. Launch with `steam.exe -applaunch 921570`; handover §3 says OCTOPATH relaunches itself otherwise. The log folder Logs\Octopath_Traveler-Win64-Shipping already exists. The alternative is DumperTest Shipping, which DOES statically import WINMM. I measured this with tools/pe/pe_imports_exports.py: timeBeginPeriod, waveOutGetNumDevs and timeEndPeriod. But no proxy is deployed there. Proxy Deploy's drive scan excludes 'UE_Analyze_data' by default (Constants.cs:350; the persisted proxyDeploy.scanExcludedFolderNames is the same), so the panel cannot reach it. winmm has also never been live-run on DumperTest. Prefer Octopath.

**Steps:**

1. Backup first. Copy %LOCALAPPDATA%\UE5CEDumper\ui-options.json to out/. Its current proxyDeploy state: lkgSuggestEnabled=false and confirmedProxyByExe['Octopath_Traveler-Win64-Shipping.exe']=2. ProxyType serializes as a number with no string converter (UiOptionsSettings.cs:255-262): Version=0, Dinput8=1, Dxgi=2, Winmm=3 (ProxyType.cs:7-42). The pre-existing 2 (dxgi) is the built-in control: the check is a CHANGE 2 -> 3, not the mere presence of a record.
2. Do NOT redeploy and do NOT run `octopath_proxy_swap.py restore`. out/octopath-proxy-backup/ holds the 2026-08-19 winmm.dll (1.0.0.3263, sha 823d02b3, PRE-FIX), not the file deployed now, so a restore would install a build without the fix. `backup` would also refuse, because a backup already exists.
3. Launch: `steam.exe -applaunch 921570`. Load a save and confirm the game booted: it is not just a live process, and handover §3 expects roughly 274k-406k objects. Then `py tools/verify/front_window.py front Octopath_Traveler-Win64-Shipping` when needed.
4. Confirm the proxy loaded. Logs\Octopath_Traveler-Win64-Shipping\init-0.log must hold 'winmm proxy: lazily forwarded N/N exports to real System32 winmm.dll'. test-games.md records 180/180.
5. Step 1 of the row, over the pipe (the UI never displays load_mode): `py tools/verify/sweep_title.py Octopath_Traveler-Win64-Shipping`. It runs assert_build (dist/build_number.txt = 3546), then ensure_scanned (trigger_scan), then prints `load_mode = proxy:winmm.dll` (sweep_title.py:46). The quick form is `py tools/verify/pipe_client.py get_pointers`, then read "load_mode". A pre-fix DLL reports 'loaded:winmm.dll'.
6. Start the AOT UI: `py -c "import subprocess,pathlib; subprocess.Popen([str(pathlib.Path('dist/UE5DumpUI.exe').resolve())], cwd='dist')"`. dist\UE5DumpUI.exe is 57.7 MB, dated 2026-09-17 09:51, which is after the last UI commit (08:57), so it is AOT-trimmed. Front it and click Connect. The scan already ran, so ApplyEngineState fires and the status reads 'Connected — UE418 (N objects)'. If you skipped the rig, the status reads 'Connected — waiting for scan (load a save first, then click Start Scan)'; click 'Start Scan'.
7. Stay connected at least 25 s: the dwell is 20,000 ms (MainWindowViewModel.cs:2841) plus the 400 ms option-save debounce (:2271). Do not disconnect. A disconnect bumps _sessionEpoch and the record is dropped (:2860-2861).
8. Step 2 of the row. Re-read ui-options.json: proxyDeploy.confirmedProxyByExe['Octopath_Traveler-Win64-Shipping.exe'] must now be 3. Pre-fix it stays 2, because 'loaded:winmm.dll' fails the 'proxy:' prefix test at :2882-2884.
9. Optional visual. In the 'Proxy Deploy' tab, tick 'Suggest proxy' and click 'Scan Steam'. The OCTOPATH TRAVELER row's 'Suggested proxy' cell must read 'winmm.dll · confirmed working'.
10. Restore: untick 'Suggest proxy' (lkgSuggestEnabled back to false). Leave confirmedProxyByExe at 3 unless the maintainer wants 2 back. 3 is the truthful value; the old 2 claims dxgi works, and test-games.md:14 says dxgi CRASHES Octopath. Record the decision. Then kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| proxy:winmm.dll | dll/src/Fern.cpp:1416-1418 (classifier), :1425 (data["load_mode"]) | get_pointers / scan_status reply field load_mode; printed by tools/verify/sweep_title.py as 'load_mode = proxy:winmm.dll'. Not shown anywhere in the UI. |
| loaded:winmm.dll | dll/src/Fern.cpp:1421-1422 | the PRE-FIX value of load_mode, which is what a stale proxy reports. It is the failure signature. |
| winmm proxy: lazily forwarded %d/%d exports to real System32 winmm.dll | dll/src/Lugner_Winmm.cpp:310 (LOG_CAT "PROXY") | %LOCALAPPDATA%\UE5CEDumper\Logs\Octopath_Traveler-Win64-Shipping\init-0.log. This is precondition evidence only: the proxy loaded. |
| Connected: UE{ver}, {count} objects, module=Octopath_Traveler-Win64-Shipping.exe | ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2832 | Logs\UE5DumpUI\init-0.log and the mirror Logs\Octopath_Traveler-Win64-Shipping\ui-init-0.log. It proves ApplyEngineState ran, and ApplyEngineState is what schedules the confirmation (:2810). |
| winmm.dll · confirmed working | ui/UE5DumpUI/Services/ProxyImportAnalyzer.cs:231 (GetDisplayName → 'winmm.dll', ProxyType.cs) | The Proxy Deploy grid, column 'Suggested proxy' (en.axaml:768). It shows only while 'Suggest proxy' (en.axaml:771) is ticked AND the grid has been scanned. |
| "Octopath_Traveler-Win64-Shipping.exe": 3 | ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:510-522 (RecordConfirmedProxy) + UiOptionsSettings.cs:246 | %LOCALAPPDATA%\UE5CEDumper\ui-options.json → proxyDeploy.confirmedProxyByExe. No log line is written for the record. |

**Row text vs source:** Step 1 ('The load mode reads proxy:winmm.dll') has no UI surface. EngineState.LoadMode is consumed only at MainWindowViewModel.cs:2883 and is never bound in any .axaml, so it has to be read from the get_pointers reply. Step 2 ('the per-game confirmed-proxy record now appears') is always true on this machine, because Octopath already has a record (dxgi, 2). The verifiable claim is that the value becomes 3 (Winmm). The row names 'Proxy Deploy tab' for deploying, but the proxy is already deployed; redeploying would overwrite it, and the swap-rig backup is pre-fix.

**Rig:** An existing rig covers step 1: `py tools/verify/sweep_title.py Octopath_Traveler-Win64-Shipping`. It runs assert_build, then ensure_scanned, then prints load_mode. `tools/verify/pipe_client.py get_pointers` is the minimal form. Step 2 has no rig. It needs one read-only snippet: `py -c "import sys,json,os;sys.stdout.reconfigure(encoding='utf-8',errors='replace');d=json.load(open(os.path.expandvars(r'%LOCALAPPDATA%\UE5CEDumper\ui-options.json'),encoding='utf-8'));print(d['proxyDeploy']['confirmedProxyByExe'].get('Octopath_Traveler-Win64-Shipping.exe'))"`. Run it before Connect (expect 2) and 25 s after (expect 3). The UI part is only Connect (plus Start Scan if unscanned) and waiting. The Suggested-column view needs computer-use on the Proxy Deploy tab.

**Traps:** (1) The process picker's 'UE5Dumper loaded (proxy: winmm.dll) v…' (GameProcessInfo.cs:30-32, from DumperModuleDetector.cs:69-70) comes from module enumeration and was already right before the fix. It is NOT evidence for this row. (2) A record for Octopath already exists (2 = dxgi), so 'a record appears' is always true. Only 2 -> 3 discriminates. (3) The Suggested column is gated on lkgSuggestEnabled, which is persisted false. Restore it after. (4) The dwell guard needs the SAME connection alive for 20 s. Any reconnect or UI restart inside the window cancels it (MainWindowViewModel.cs:2877-2878). (5) Proxy mode never scans on load. Without trigger_scan or 'Start Scan', ApplyEngineState never runs and nothing is scheduled (:2174-2191). (6) assert_build cannot distinguish the deployed winmm.dll (sha 05a66930) from dist's (sha 46c94687): both report build 3546. (7) The stale octopath_proxy_swap backup is PRE-FIX (see steps). (8) One injected game at a time; kill the game and the UI when done. Octopath steals focus: use front_window.py. (9) Finding, minor doc drift: EngineState.cs:139-140 and the comment at MainWindowViewModel.cs:2886 still list only version/dinput8/dxgi as proxy load modes.

**Related rows:** L67, L46, L57, L59

### L63 — `[W5-INSTEXPORT-TRUNC]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 40 min

**Fixture:** DumperTest Shipping 5.4 (`py tools/verify/launch_dumpertest.py shipping`), inflated through two UNCLAMPED UFUNCTIONs that the 2026-09-16 attempt never used. (a) V1a_GrowContainers(Count) (DumperTestActor.cpp:695-711; no clamp) grows Map_Churn (TMap<int32,int32>, DumperTestActor.h:453) and Arr_Churn (TArray<int32>, :458) by Count each. (b) Spawn_Holders(Count, bDerived) (DumperTestActor.cpp:764-789; no clamp) spawns with P.Owner = this. That grows SpawnedHolders (TArray<TObjectPtr<AActor>>, DumperTestActor.h:532) AND AActor::Children, which UE_5.4 Actor.h:875-876 declares as UPROPERTY(Transient) TArray<TObjectPtr<AActor>>. Entry arithmetic at Array Limit 16384 is PREDICTED, not measured. Map_Churn is read up to arrayLimit (Ubel.cpp:5293, clamped to 16384 at :4317-4319) and emits group + Key + Value per pair: 1 + 3×16,384 = 49,153. Arr_Churn, SpawnedHolders and Children are each capped at 4,096 elements by kArrayElementsPerRequestCap (Ubel.cpp:42, enforced at :2559 and :2689) and emit 1 leaf each: 3 × 4,097 = 12,291. The measured baseline is 677, of which about 651 is not these four containers. Total ≈ 62,095, against the 60,000 cap (CeXmlExportService.cs:200). The margin is only about 2,000, and without Children it would be about 58,000 and FAIL. A commercial title would need one object whose walked containers add up to more than 60k at Array Limit 16384: for example a TMap with at least 16k scalar pairs plus another 11k or more elements, or a TMap<K, leaf struct> with at least 15k pairs.

**Steps:**

1. With the UI CLOSED, back up ui-options.json and set main.arrayLimitExponent from 6 to 14 (2^14 = 16384; the slider max is 14 at MainWindow.axaml:131-134). Leave main.collapsePointerNodes as it is (currently true). It does not change the entry count.
2. Launch the fixture: `py tools/verify/launch_dumpertest.py shipping`, then `py tools/verify/inject.py --pid <pid from out/host.pid>`. Then start the AOT dist UI, front it and Connect. Check that the object count is non-zero.
3. Step 2 of the row FIRST, as the negative control, on the pristine actor (baseline 677 entries). Open the 'Instances' tab, type DumperTestActor in the class box, click Search, select the LIVE instance (not Default__DumperTestActor), wait for the fields grid and click 'Copy CE XML'. The status line must be EMPTY, and view-0.log must hold 'CE XML copied to clipboard for instance DumperTestActor_… (N structs resolved)' with NO ' — TRUNCATED' suffix.
4. Near-miss control, which shows the check can fail. From a third pipe slot: find_live_actor, then invoke V1a_GrowContainers(16384) (parms_size 4, params_hex 00400000). Re-select the instance so it re-walks, then Copy CE XML. The prediction is about 53.9k entries with no warning, and the status must stay empty.
5. Inflate past the cap: `walk_functions addr=<class_addr>` to get Spawn_Holders' parms_size, then invoke Spawn_Holders with params_hex i32(4096)+'00', following the aa2_step4_churn.py:101-139 pattern. Verify with `walk_instance array_limit=16384` that Map_Churn map_count ≥ 16384 and that Children and SpawnedHolders array_count ≥ 4096.
6. Step 1 of the row: re-select the live DumperTestActor, then click Copy CE XML. The status must read the TRUNCATED text below. Read the clipboard (computer-use read_clipboard, or a ctypes CF_UNICODETEXT reader) and count '<CheatEntry>'. Expect MORE than 60,000: the per-element loops for scalar arrays and maps do not check the cap, so it overshoots until the next top-level field (the check is at CeXmlExportService.cs:2179).
7. Optional, to test the warning's own advice. Set collapsePointerNodes false (through json, UI restarted, as the row's method note describes) and export again. It stays TRUNCATED, with the same <CheatEntry> count. Lowering Array Limit back to 64 removes it.
8. Restore main.arrayLimitExponent=6 and any other option touched, with the UI closed. Kill the game (the inflation is not persisted) and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| ⚠ Copied, but TRUNCATED at the 60,000-entry export cap — the CE table is incomplete; tick Collapse Pointer Nodes or lower the DropDown Limit | ui/UE5DumpUI/ViewModels/InstanceFinderViewModel.cs:855-858 ({CeXmlExportService.MaxEmitEntries:N0}, CeXmlExportService.cs:200) | Instance Finder ('Instances' tab) status line after 'Copy CE XML' (button label str.LiveWalker.ExportCE = 'Copy CE XML', en.axaml:261; InstanceFinderPanel.axaml:332-333) |
| CE XML copied to clipboard for instance {Name} ({N} structs resolved) — TRUNCATED at the entry cap | ui/UE5DumpUI/ViewModels/InstanceFinderViewModel.cs:859-860 (_log.Info(msg) → LogCatView, LoggingService.cs:139) | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log and mirror Logs\DumperTest-Win64-Shipping\ui-view-0.log |
| CE XML copied to clipboard for instance {Name} ({N} structs resolved) | ui/UE5DumpUI/ViewModels/InstanceFinderViewModel.cs:859 | view-0.log for a complete export (row step 2 and the near-miss control). The status text is "" (:858). |
| ⚠ High memory usage — may cause crash | ui/UE5DumpUI/Resources/Strings/en.axaml:11 (shown when ArrayLimitExponent >= 8, MainWindowViewModel.cs:229) | The '⚙ Options' flyout next to 'Array Limit:'. It is expected at 16384 and is not a failure. |

**Row text vs source:** (a) The quoted status matches source (InstanceFinderViewModel.cs:855-857, and it does name 'Collapse Pointer Nodes' and 'the DropDown Limit'). But the advice it gives is INEFFECTIVE at this call site, because neither lever reduces _emitEntryCount (see traps). The lever that works in Instance Finder is Array Limit, which sets the walked element count. That is a finding about the fix itself. (b) The ⛔ NOT RUN note says the cap is 'UNREACHABLE on this fixture, measured' because 'the instance export does not descend into nested container elements'. That is true but not what limits it: the attempt never used V1a_GrowContainers (unclamped) or Spawn_Holders (Owner=this, so it feeds AActor::Children and SpawnedHolders). By source arithmetic those two reach about 62.1k. (c) 'a dense object with Collapse Pointer Nodes off' misdescribes the export. Collapse is cosmetic (the note's own 677 = 677 measurement shows it), and the export does not follow pointers. (d) The cap is not hard. The truncated XML can hold more than 60,000 entries, because per-element loops in EmitArrayProperty and EmitMapProperty are not cap-checked (only :2179, :2434, :2628, :2807, :2963, :3032 check).

**Rig:** No rig drives this row, and no tools/verify file mentions the tag. The smallest rig uses the third pipe slot, since the UI holds two: `sys.path.insert(0, r'D:\Github\UE5CEDumper\tools\verify'); from pipe_client import PipeClient; from ad4_contested import find_live_actor, invoke`. Then `with PipeClient() as c: c.assert_build(); c.ensure_scanned(); act = find_live_actor(c); fn = {f['name']: f for f in c.request('walk_functions', addr=act['class_addr'])['functions']}; invoke(c, act['addr'], 'V1a_GrowContainers', parms_size=4, params_hex=(16384).to_bytes(4,'little').hex()); invoke(c, act['addr'], 'Spawn_Holders', parms_size=fn['Spawn_Holders']['parms_size'], params_hex=(4096).to_bytes(4,'little').hex()+'00')`. Then verify with c.request('walk_instance', addr=act['addr'], array_limit=16384). Driving the invokes as two separate steps gives the near-miss control. The export itself needs computer-use: Instances tab → Search → select → Copy CE XML → read the status line and the clipboard.

**Traps:** (1) main.arrayLimitExponent is persisted, so restore 6. The row's own method note says the Options flyout's Collapse checkbox ignored synthetic input (MainWindow.axaml:107-113). Change options through ui-options.json with the UI closed, then restart. (2) Collapse Pointer Nodes and DropDown Limit do NOT change the count. Collapse only adds <Options moHideChildren> (CeXmlExportService.cs:3702, :3736). DropDown Limit only gates DropDownList construction. _emitEntryCount is incremented only in EmitGroupOpen, EmitGroupPlaceholder, EmitLeaf and EmitStringLeaf (:3686, :3726, :3750, :3795). Toggling them proves nothing, and the warning's advice is therefore wrong at this call site (see discrepancies). (3) Instance Finder passes no resolvedInstances (InstanceFinderViewModel.cs:835-840), so pointer 'density' is irrelevant. Only walked container elements count. (4) Pick the live instance, not the CDO. (5) The margin is about 2k entries. If AActor::Children is not read, you land at about 58k and see no warning. That would be a fixture-arithmetic miss, not a regression, so check array counts with walk_instance first. (6) Do this row LAST in the DumperTest session: it spawns 4096 actors and inflates the actor, which changes Instances counts for other rows. (7) The clipboard payload is around 15-20 MB. (8) Use the AOT dist exe. (9) Kill the game, CE (unused here) and the UI at the end.

**Related rows:** L73, L79, L47, L56, L61

### L64 — `[W1-PIPEBUSY-LOG]`

**Status:** ✅ PASSED red→green 2026-09-22 (`git log --grep 'verify(L64)'`) · **reachability:** `live` · **needs CE:** yes · **needs UI:** yes · **estimate:** 25 min

**Fix commit(s):** `62f1596b`

**Fixture:** No game. The UI (AOT) plus something occupying the AOBMaker bridge's single pipe instance. The row's staging, 'two Cheat Engines, the second holding the pipe', does NOT deterministically produce a busy pipe (see discrepancies). The reliable fixture is a manufactured holder at \\.\pipe\AOBMakerCEBridge. The ac3_denied_pipe.py rig already manufactures this pipe name without CE.

**Preconditions:** The UI log is %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\init-0.log. Serilog writes Debug lines (MinimumLevel.Debug, LoggingService.cs:93) with level tokens 'DBUG'/'WARN'/'INFO' ([{Level:u4}], LoggingService.cs:40-41). The toolbar AOBMaker chip is visible whenever the bridge is configured (MainWindow.axaml:75-93; IsAobMakerConfigured, MainWindowViewModel.cs:199), with no game connection needed. For the manufactured route no Cheat Engine may be running, because its plugin would own the name.

**Steps:**

1. 1. Confirm no CE is running (`tasklist /FI "IMAGENAME eq cheatengine-x86_64-SSE4-AVX2.exe"`). Start the AOT UI with no game. The toolbar reads 'AOBMaker' 'Offline'.
2. 2. STEP 2 of the row first (absent pipe): click the toolbar '⟳' (str.Toolbar.AobMakerRefresh, en.axaml:426; RefreshAobMakerCommand, MainWindowViewModel.cs:3098-3120). After about 2 s the status reads 'AOBMaker plugin not detected — open Cheat Engine with the AOBMaker plugin loaded'. init-0.log gains the [DBUG] 'no server on …' line.
3. 3. STEP 1 (busy pipe), manufactured: start the new holder rig (outline in `rig`). It creates \\.\pipe\AOBMakerCEBridge with nMaxInstances=1 and connects its OWN client to the only instance. Hold for 60 s. Click '⟳'. After about 2 s init-0.log must gain the [WARN] 'EXISTS but no instance was free within 2000 ms …' line and must NOT gain the DBUG 'Cheat Engine not running' line for that click.
4. 4. Release the holder (or wait for it to exit). Click '⟳' again: the DBUG 'no server' line returns. This control shows the WARN tracks pipe existence rather than a latch.
5. 5. Contrast, optional: `py tools/verify/ac3_denied_pipe.py --seconds 60` (the DENY-DACL pipe), then '⟳'. It must hit the OTHER warn, "AOBMaker bridge: connect to 'AOBMakerCEBridge' failed (UnauthorizedAccessException): …" (AobMakerBridgeService.cs:538-545), proving the three branches are distinct.
6. 6. The row's literal realism variant, optional: start CE #1 (AOBMaker). '⟳' gives [INFO] 'AOBMaker CE Plugin bridge: available'. Start CE #2: AOBMaker's own CEPlugin.log shows 'PipeServer: CreateNamedPipe failed, err=231 …' retry-spam. Click '⟳' and record what the UI logs; it is expected to stay Info 'available' because CE #1's instance is idle and listening. To force the WARN with real CE, run a client that connects to CE #1's pipe and re-connects faster than the plugin's ~5 s read timeout, and press '⟳' inside a held window.
7. 7. Kill any CE and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| AOBMaker bridge: \\.\pipe\AOBMakerCEBridge EXISTS but no instance was free within 2000 ms — another client holds it (a second Cheat Engine with the AOBMaker plugin, or another tool) | ui/UE5DumpUI/Services/AobMakerBridgeService.cs:527-530 (C# literal "\\\\.\\pipe\\{_pipeName}" renders as \\.\pipe\AOBMakerCEBridge; ConnectTimeoutMs 2000 at :19) | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\init-0.log, level [WARN] (plus Logs\<game>\ui-init-0.log mirror only when connected to a game) |
| AOBMaker bridge: no server on \\.\pipe\AOBMakerCEBridge within 2000 ms (Cheat Engine not running, or the AOBMaker plugin is not loaded) | ui/UE5DumpUI/Services/AobMakerBridgeService.cs:531-534 | Logs\UE5DumpUI\init-0.log, level [DBUG] |
| AOBMaker plugin not detected — open Cheat Engine with the AOBMaker plugin loaded | ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:3114 | main status line after ⟳ — appears in BOTH the busy and the absent case (only the log was fixed) |
| AOBMaker CE Plugin bridge: available | ui/UE5DumpUI/Services/AobMakerBridgeService.cs:99 | Logs\UE5DumpUI\init-0.log [INFO] (realism variant with one idle CE) |
| AOBMaker bridge: connect to '{_pipeName}' failed ({ex.GetType().Name}): {ex.Message} | ui/UE5DumpUI/Services/AobMakerBridgeService.cs:543-544 | init-0.log [WARN] — the DIFFERENT branch ac3_denied_pipe.py reaches (contrast, not the row's line) |

**Row text vs source:** (a) The staging 'with the second CE holding the pipe' does not by itself make the pipe BUSY. AOBMaker's server is single-instance (D:\Github\AOBMaker\plugins\CEPlugin\src\pipe_server.cpp:2995-3044, nMaxInstances 1). The losing CE's plugin fails CreateNamedPipe with 231 and never connects as a CLIENT, so the owner's instance stays idle-listening and the UI connects (Info 'available'). The WARN arm needs a client OCCUPYING the instance, because only a server at capacity makes ConnectAsync wait until TimeoutException (the ac3_denied_pipe.py docstring says the same). The 2026-09-10 'bridge unusable' with three CEs is more plausibly the UI talking to a different CE than the one attached to the game; that is unproven. (b) The row says `init.log`; the real file is Logs\UE5DumpUI\init-0.log. (c) The row's WARN excerpt matches source. Observation: the user-facing status after ⟳ still says 'not detected — open Cheat Engine…' in the busy case (MainWindowViewModel.cs:3114), so only the log distinguishes busy from absent.

**Rig:** NO existing rig drives this branch. `tools/verify/pipebusy_capacity.py` targets the DLL's own pipe (UE5DumpBfx), not this one. `tools/verify/ac3_denied_pipe.py` is the template: it already creates a server at \\.\pipe\AOBMakerCEBridge with CE not running, but with a DENY DACL, which reaches the generic Warn arm. NEW rig (~40 lines, e.g. tools/verify/aobmaker_pipe_busy.py): (1) CreateNamedPipeW(r'\\.\pipe\AOBMakerCEBridge', PIPE_ACCESS_DUPLEX, PIPE_TYPE_BYTE|PIPE_WAIT, 1, 65536, 65536, 0, NULL-DACL SA or None); fail loudly if it returns INVALID_HANDLE_VALUE, since that means CE owns the name. (2) CreateFileW on the same name, GENERIC_READ|GENERIC_WRITE, OPEN_EXISTING, to occupy the single instance. (3) Optionally ConnectNamedPipe, which returns ERROR_PIPE_CONNECTED. (4) Hold N seconds, then close both handles in a finally. (5) Print os.listdir(r'\\.\pipe\') membership as an independent witness that the name is enumerable. The UI click and the status read need computer-use; the log is file-readable (Serilog opens FileShare.Read).

**Traps:** The tab-switch probe (Interesting Functions) has a 5 s cooldown (InterestingFunctionsViewModel.cs:45, :455-460), so use the toolbar ⟳, which probes every click. `PipeExists` (AobMakerBridgeService.cs:75-86, Directory.EnumerateFiles(@"\\.\pipe\")) has NEVER executed in any test: both new tests go through the internal seam with `_ => true` / `_ => false` (AobMakerInjectTableFileTests). This live check is its first run, and it must be on the AOT build. Any exception it hits turns into 'false', which silently yields the DBUG line and reads as a FAIL of the fix. The manufactured holder cannot coexist with a running CE+AOBMaker: the name is taken and the rig must say so. A real AOBMaker server drops an idle client after its ReadMessage timeout (~5 s, AOBMaker pipe_server.cpp comment near :3000-3010), so a real-CE holder must renew. open_application starts extra CE instances (handover §6/register); use front_window.py. The row says 'init.log' but the file is init-0.log.

**Related rows:** L42, L43, L77, L13

### L65 — `[W1-GROUP-DENYLIST]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 20 min

**Fix commit(s):** `734d179d`

**Fixture:** DumperTest 5.4 Shipping. Two snapshots from ONE session a few seconds apart give a non-empty Diff (TickCount rises, F32_Ticking/Health.CurrentValue fall at 1 Hz — tools/ue-sample/README.md:589-601), so the noise picker lists DumperTestActor. Known constants for the Group match: I32 = 1234567 and FrozenInt = 424242 (README.md:468, :595) — both int32 on DumperTestActor.

**Preconditions:** AOT dist; experimental gate ON (Snapshot tab). Default group slots are 2, NumericNoByte, Exact (SnapshotViewModel.Group.cs:89-90; ValueScanModels.cs:225-226). Note the game's current Diff denylist before starting (Snapshot tab Diff mode 'Active denylist for this game:'), to restore it.

**Steps:**

1. Launch+inject DumperTest Shipping; UI Connect. Snapshot tab: 'Capture Snapshot' twice, ~5 s apart ('Game objects only' may stay on).
2. 'Mode:' toggle = Diff. 'Old:' older, 'New:' newer -> 'Run Diff'. Open 'Noise picker — top classes in this result'; tick 'Hide' on DumperTestActor -> 'Apply & re-run'. The chip DumperTestActor appears under 'Active denylist for this game:'.
3. Toggle 'Mode:' to 'Group (Multiple Values)' (the noise picker disappears — it is Diff-only). 'Snapshot:' = newest; slot 1 = 1234567, slot 2 = 424242; 'Find'.
4. No-match case: status 'No objects hold all 2 values.  ·  1 class(es) hidden by the Diff denylist (switch to Diff mode to see or clear it)'. This is the fix's target: the denylist, not the data, caused the miss.
5. Match case: back to Diff, 'Clear all', hide a DIFFERENT class from the noise picker (if the Diff has only DumperTestActor rows, capture with 'Game objects only' off to get others) -> Group -> Find: '{n} object(s) matched  ·  scanned {m}  ·  1 class(es) hidden by the Diff denylist (switch to Diff mode to see or clear it)'.
6. Control: Diff -> 'Clear all' -> Group -> Find: '{n} object(s) matched  ·  scanned {m}' with NO denylist sentence.
7. Restore the original denylist (it is persisted); kill the game; close the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| No objects hold all {GroupInputs.Count} values.  ·  {N:N0} class(es) hidden by the Diff denylist (switch to Diff mode to see or clear it) | ui/UE5DumpUI/ViewModels/SnapshotViewModel.Group.cs:245-251 | Snapshot tab, Group mode status line (no-match) -> 'No objects hold all 2 values.  ·  1 class(es) hidden by the Diff denylist (switch to Diff mode to see or clear it)' |
| {Total:N0} object(s) matched{ (showing first N)}  ·  scanned {ScannedObjects:N0}{capNote}{denyNote} | SnapshotViewModel.Group.cs:240-251 (trunc :229) | Group mode status line (match), with/without the denylist sentence |
| Noise picker — top classes in this result / Hide / Apply & re-run / Active denylist for this game: / Clear all | en.axaml:667, 669, 673, 674, 675; expander IsVisible=!IsGroupMode SnapshotPanel.axaml:488-490 | Snapshot tab, Diff mode only |
| Mode: / Diff / Group (Multiple Values) / Find | en.axaml:586-588, 590; ToggleSwitch SnapshotPanel.axaml:196-199 | Snapshot tab mode switch and group button |
| No noise classes picked — tick one or more rows first. | SnapshotViewModel.cs:1679 | Diff status if Apply is clicked with nothing ticked (setup error, not a result) |

**Row text vs source:** None: the row's quoted suffix matches SnapshotViewModel.Group.cs:246 exactly (with '1' formatted by :N0).

**Rig:** None exists (grep of tools/verify for the tag/strings: no hit). Computer-use only; the denylist file can be read to confirm persistence: %LOCALAPPDATA%\UE5CEDumper\Snapshots\snapshots.<pe_hash>.denylist.json (SnapshotStore.cs:2677-2678).

**Traps:** (1) The denylist is PERSISTED per game (snapshots.<pe>.denylist.json) and also scopes SPC/Pivot separately — restore the Diff scope with 'Clear all' / chip removal at the end. (2) The noise picker is empty until a Diff has produced rows (RebuildNoiseRows from diff.TopContributors, SnapshotViewModel.cs:1501/1653) — two snapshots of the same session with a ticking field are required. (3) The count is the size of the Diff denylist (_excludedClasses.Count), not the number of classes actually filtered out of THIS group result — a hidden class that matched nothing still counts. (4) Group mode applies the denylist by design (snapshot-group-match-spec.md:255) — the no-match with DumperTestActor hidden is correct behaviour; only the disclosure is under test. (5) Two spaces either side of the middle dot in the status. (6) AOT build; focus stealing.

**Related rows:** L59, L66, L61, L57

### L66 — `[W1-PARTIAL-MARK]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 60 min

**Fix commit(s):** `025dc031`

**Fixture:** STEPS 1-2: DumperTest 5.4 Shipping with its per-game DB driven past 512 MB. KEY SOURCE FACT: the cap compares the WHOLE per-game DB (db + -wal + uncommitted bytes, SnapshotStore.cs:552-553) against 'Max size', checked after every chunk (SnapshotViewModel.cs:943-950) — it is not the size of this capture. So inflate DumperTest (Spawn_ManyComponents(20000) x k, capture with 'Game objects only' and 'Auto detect Engine/System noise' OFF, 'Max size:' Off) until the DB is near 512 MB, then capture once with 'Max size:' 512 MB. Fast alternative, with destructive traps: EverSpace 2 (docs/test-games.md:12) — its DB snapshots.0D281EF60A6D5000.db is already 1,460 MB (read-only check today; hash mapped via Logs\ES2-Win64-Shipping\scan-*.log), so the cap trips on the first chunk. STEP 3: a title whose DB predates the column — read-only PRAGMA today: TQ2 (03D2A4710B289000), Octopath (5EEB192C030CE000), Avowed (DDCE3EDE0BE03000), Solarpunk (ED3D085C0811F000) lack partial_reason (0 snapshots each); valid only while each installed exe still has that PE hash.

**Preconditions:** AOT dist; experimental gate ON. Per-game quota >= the target DB size (default '1 GB', persisted as experimental.json snapshotQuotaMb — SnapshotViewModel.cs:95, :526); Auto snapshot Off. Record the current 'Max size', quota, 'Game objects only', 'Auto detect…' and 'Type:' values to restore (all persisted, MainWindowViewModel.cs:2381-2383). Read-only DB checks with the UI closed or via sqlite `file:<db>?immutable=1`.

**Steps:**

1. Launch+inject DumperTest Shipping. With the UI disconnected, invoke Spawn_ManyComponents(20000) (pipe_client + ad4_contested.invoke, parms_size=4) as many times as needed. Connect the UI; Snapshot tab: untick 'Game objects only' and 'Auto detect Engine/System noise', 'Max size:' Off; use 'Estimate size' to plan; 'Capture Snapshot' until the DB ('Used:' bar) is a little under 512 MB.
2. Set 'Max size:' = 512 MB; 'Capture Snapshot'. Transient status: 'Captured N objects, M fields — stopped at <X> cap (partial: first A of B objects)'.
3. Saved-snapshots grid 'Label' column ends '  (partial: stopped at the size cap)'. Check the same row's line in: Diff 'Old:'/'New:', Group 'Snapshot:'/'Compare with:', Class Pivot 'Snapshot:' combo and the Suggest-targets checkboxes — each shows '<Label>  (partial: stopped at the size cap)  ·  <local time>'. SPC Query tab: record what its Label column shows (source says: raw label, no marker).
4. DB oracle (UI closed or immutable): `SELECT id,label,is_usable,partial_reason FROM snapshots` -> the capped row has is_usable=1, partial_reason='cap'.
5. Step 2: close the UI; relaunch; Connect to the same game (the list is only read after a connect). The marker is still on the grid label and pickers. Then set 'Max size:' Off and capture once more: the pre-capture auto-clean runs (only unusable rows are purged) and the partial row is STILL listed. view-0.log shows 'Snapshot: auto-removed N unusable snapshot(s) before capture' only if N>0.
6. Step 3: with an old-DB title (e.g. Octopath): before connecting, `PRAGMA table_info(snapshots)` on its DB (immutable) -> no partial_reason. Launch+inject/proxy that title, Connect (SetEngineState opens the DB even if the Snapshot tab is never shown); view-0.log 'SnapshotStore: active DB -> …snapshots.<pe>.db' and no 'Snapshot: list failed'. Close the UI; PRAGMA again -> partial_reason present (TEXT NOT NULL DEFAULT ''), user_version unchanged (4).
7. Cleanup: 'Delete Selected' on the inflated/partial DumperTest snapshots (or keep for L61 step 2 / L57 step 2), restore Max size / quota / Game objects only / Auto detect / Type, kill games, close the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
|   (partial: stopped at the size cap) | ui/UE5DumpUI/Models/SnapshotModels.cs:57 (PartialSuffix; token Constants.SnapshotPartialCap = "cap", Constants.cs:281) | appended to LabelDisplay (grid) and PickerDisplay (pickers) |
| {Label}  (partial: stopped at the size cap) | SnapshotModels.cs:50 (LabelDisplay), bound at SnapshotPanel.axaml:174 | Saved snapshots grid, Label column — ends with the marker |
| {Label}  (partial: stopped at the size cap)  ·  yyyy-MM-dd HH:mm:ss | SnapshotModels.cs:82 (PickerDisplay); bound at SnapshotPanel.axaml:217, 227, 342, 351; ClassPivotPanel.axaml:103; DiscoverSnapshotPick.Display ClassPivotViewModel.cs:49 | Diff Old/New, Group Snapshot/Compare, Class Pivot snapshot combo + Suggest-targets — the marker is followed by the timestamp |
| Captured {objects:N0} objects, {fields:N0} fields — stopped at {size} cap (partial: first {n:N0} of {total:N0} objects) | ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:997-999, :1052 | Snapshot tab status right after the capped capture (transient) |
| Capture done: {objects:N0} objects, {fields:N0} fields in … | SnapshotViewModel.cs:1011-1014 | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log |
|   (partial: stopped on low disk) | SnapshotModels.cs:58 (token "disklow", Constants.cs:282) | only if the free-disk guard stops the capture instead |
| SnapshotStore: active DB -> {path} | ui/UE5DumpUI/Services/SnapshotStore.cs:97 | view-0.log on connect (step 3) |
| Snapshot: list failed | SnapshotViewModel.cs:664 | view-0.log — must NOT appear when the old DB opens |
| Snapshot: auto-removed {n} unusable snapshot(s) before capture | SnapshotViewModel.cs:803-805 | view-0.log before the step-2 capture (only if n>0); the partial must survive it |
| Max size: (Off / 512 MB / 1 GB / 2 GB / 4 GB)  ·  Per-game quota: (512 MB / 1 GB / 2 GB / 5 GB / Unlimited) | en.axaml:512, 529; SnapshotViewModel.cs:487-488, 501-502 | Snapshot tab capture settings |

**Row text vs source:** (a) SPC: the row says the marker appears in 'every Diff / Group / SPC / Pivot picker', but SPC Query binds the raw Label (SpcPanel.axaml:78-79, :365; SpcQueryViewModel.cs:217) — no marker there (source finding). (b) Picker lines do not END with the marker: PickerDisplay puts '  ·  <time>' after it (SnapshotModels.cs:82); only the grid label ends with it. (c) The control is labelled 'Max size:' (en.axaml:512), not 'Max dataset'. (d) The cap is on the whole per-game DB size, not this capture's size. (e) Step 2 needs a reconnect after the restart and a further capture to exercise the auto-clean.

**Rig:** None drives the capture. Existing read-only helpers: tools/verify/snapshot_cap_fixture.py (opens corpora mode=ro; different cap, but its sqlite pattern fits the partial_reason/table_info oracle). Inflation needs an ad-hoc pipe_client + ad4_contested.invoke loop for Spawn_ManyComponents, run with the UI disconnected. Grid/picker reads are computer-use.

**Traps:** (1) FINDING: the SPC Query pickers bind the RAW label — DataGridTextColumn Binding="{Binding Label}" (SpcPanel.axaml:78-79 and :365) -> SpcSnapshotPick.Label => Meta.Label (SpcQueryViewModel.cs:217) — so SPC shows NO partial marker, contradicting the row and the fix's 'every picker line' claim (commit msg; todo.md:670); the fix's tests pin only SnapshotMeta.LabelDisplay/PickerDisplay. (2) The cap measures the whole per-game DB, so 'capture more than 512 MB' really means 'the DB crosses 512 MB during this capture'; a DB already over 512 MB trips on the first chunk. (3) FIFO quota: after a capture EnforceQuotaAsync drops the game's OLDEST snapshots until the DB fits (SnapshotViewModel.cs:986-994; the new one is kept) — with the default 1 GB quota ES2's 1.46 GB DB would lose snapshot #1. ES2 also holds snapshot #2 with is_usable=0 (read-only check today), which the pre-capture auto-clean (:803) WILL delete. Do not use ES2 without the maintainer's consent and quota Unlimited. (4) The auto-clean only runs at the start of a capture, so 'the auto-clean kept it' needs a second capture after the restart; and the list is only populated after Connect (SnapshotViewModel.cs:527). (5) Old DBs WITH rows that lack the column exist only for retired DumperTest builds (6A7EA60310F17000, 6A9C1C8410F23000, 6A8AA8DF10F1F000) — unreachable by connect; the reachable old DBs are empty. (6) Persisted: Max size, quota, Game objects only, Auto detect, Type — restore. (7) UI holds 2 of 3 pipe slots (inflation invokes before Connect); a big capture takes minutes — do not run L2-style worker-fault experiments at the same time; AOT build; focus stealing.

**Related rows:** L61, L59, L65, L57

### L67 — `[P3-SCORING-MCDELEGATE]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 30 min

**Fixture:** A UE4 ≤ 4.22 title from docs/test-games.md. Candidates: OctoPath Traveler UE4.18 (:14), FF7R 4.18 fork (:16), DQ XI S 4.18 (:58), Jedi Fallen Order 4.21 (:63), Everspace 4.20 (:74), Satisfactory 4.22 depot build (:82), Extinction 4.15 (:91), NEKOPALIVE 4.11 (:101). Log folders already exist on this machine for Octopath_Traveler-Win64-Shipping, 'DRAGON QUEST XI S' and Nekopara, and the Octopath install is present under D:\SteamLibrary. Best choice is Octopath: same session as L62, winmm proxy already deployed, full scan live-verified. The self-built 4.15 and 4.18 projects do NOT build ('⛔ Windows SDK', tools/ue-sample/ue4-projects/README.md table), and DumperTest is 5.4, so no self-built fixture reaches ≤4.22. On UE4 ≤4.22 (UProperty mode) the DLL reports the raw class name as prop_type (Aura.cpp:4928 match.propType = field.TypeName; the literal is compared at Genau.cpp:3618 and Ubel.cpp:6558), so multicast delegates arrive as 'MulticastDelegateProperty'. The likely stat-named hosts come from UE engine knowledge, so confirm them with the precondition query: AActor::OnTakeAnyDamage and OnTakePointDamage ('Damage' is a CombatKeyword, PropertyScoringTable.cs:102), and UPrimitiveComponent::OnComponentHit ('Hit', :109). These are ENGINE classes, so 'Game Only' must be unticked (Aura.cpp:4844 skips IsEnginePackage).

**Steps:**

1. Precondition, over the pipe with the game scanned: `py tools/verify/pipe_client.py search_properties_batch --args "{\"queries\":[\"Damage\",\"Hit\",\"Health\"],\"types\":[\"MulticastDelegateProperty\"],\"game_only\":false,\"limit\":200}"`. Require at least one result whose prop_type == 'MulticastDelegateProperty' and whose prop_name carries a value-category keyword. Note its prop_flags: CPF_BlueprintVisible 0x4 adds +1 and CPF_SaveGame 0x1000000 adds +2 (PropertyScoringTable.cs:244-246 and :338-346), which predicts the exact structural term. If this returns nothing with the flag false, the row cannot be run on this title.
2. Back up ui-options.json. Currently interestingProps = {gameOnly: true, unusualOnly: false, showAll: false}.
3. In the UI (AOT dist, connected), open the 'Interesting Props' tab. Untick 'Game Only', tick 'Show All (incl. low-score)', click 'Load'.
4. Read the status: 'N unique properties  (threshold 4+: …, ⚠ unusual: …)'. If it adds '⚠ k of M keywords STOPPED at the 200-row cap (…) — more matches exist' and names Damage or Hit, then a missing delegate row is a cap sample, not an absence.
5. Type the delegate's name (e.g. OnTakeAnyDamage) in the filter box. The Type column must read MulticastDelegateProperty. Hover the Score cell: the tooltip must contain 'structural=-1' (or -1 plus the predicted flag terms). With one Combat hit (4) and classBonus 0, FinalScore = 3, which is below InterestingThreshold 4, so the row is hidden unless Show All is on. Pre-fix it would score 4 with no structural term.
6. Control: filter a numeric (Float or Int) property of the same keyword and class family, e.g. a *Damage* float. Its tooltip must NOT carry the -1 non-value term, and its FinalScore sits 1 above the delegate's when flags and classBonus are equal.
7. Restore interestingProps.gameOnly=true and showAll=false. Kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| FinalScore={FinalScore} = keywords({KeywordHits} hits) + classBonus={ClassBonus} + structural={StructuralBonus} | ui/UE5DumpUI/Models/ScoredPropertyRow.cs:45-48 (the structural term is printed only when non-zero) | Tooltip on the 'Score' cell in the Interesting Props grid (InterestingPropertiesPanel.axaml:135-144). The discriminating token is 'structural=-1' (NonValueTypePenalty = -1, PropertyScoringTable.cs:215, applied at :333-335). |
| MulticastDelegateProperty | ui/UE5DumpUI/Services/PropertyScoringTable.cs:406 (added to IsNonValueType, :397-408); dll/src/Aura.cpp:4928 (propType = raw TypeName) | The 'Type' column of the Interesting Props grid (InterestingPropertiesPanel.axaml:172), and prop_type in the search_properties_batch reply |
| {N} unique properties  (threshold 4+: {interesting}, ⚠ unusual: {unusual}) | ui/UE5DumpUI/ViewModels/InterestingPropertiesViewModel.cs:442-445 | Interesting Props status line |
|   ⚠ {k} of {M} keywords STOPPED at the 200-row cap ({first three}) — more matches exist | ui/UE5DumpUI/ViewModels/InterestingPropertiesViewModel.cs:437-440 | Suffix on that status line when a seed query hit PerQueryLimit (:45) |
| InterestingProperties load: queries={Q} unique={U} interesting={I} unusual={X} (gameOnly=False) | ui/UE5DumpUI/ViewModels/InterestingPropertiesViewModel.cs:446-448 | %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log (+ ui-view-0.log in the game's folder). It proves Game Only was actually off. |

**Row text vs source:** (a) 'needs a UE4 ≤ 4.22 game, and none is in the calibration set' is true of the scoring-calibration games (commit 0065a830). It does not mean no fixture exists: docs/test-games.md lists eight UE4 ≤ 4.22 titles, three with log folders on this machine. (b) 'ranks below the numeric field of the same name' cannot be taken literally, since one class cannot hold a delegate and a numeric field with the same name. The observable is the Score tooltip's 'structural=-1', a FinalScore one lower than an otherwise-equal numeric row, and, for a one-keyword delegate, dropping below the 4-point threshold (hidden without Show All). (c) The row does not mention that Game Only must be off to reach the engine delegates that exist on every title.

**Rig:** No live rig exists; the scoring itself is pinned by PropertyScoringTableTests (Score_ValueKeywordOnAUE4MulticastDelegate_GetsTheNonValuePenalty). The pipe precondition is one command through the existing tools/verify/pipe_client.py: search_properties_batch with types=['MulticastDelegateProperty'] and game_only=false (request shape Fern.cpp:3055-3064; reply per_query[].results[].prop_name/prop_type/prop_flags/defining_class_name). The scoring evidence is UI-only: computer-use to untick Game Only, tick Show All, Load, filter, then hover and zoom the Score tooltip.

**Traps:** (1) Game Only defaults ON and is persisted ON (InterestingPropertiesViewModel.cs:49; ui-options interestingProps.gameOnly=true). It hides every engine-class delegate, so restore it afterwards. (2) After the penalty, a single-keyword delegate scores below the threshold and is filtered out of the grid unless 'Show All (incl. low-score)' is ticked (:478-484). An invisible row is expected, not a miss. (3) With Game Only off, the 200-per-keyword cap (:45) can crowd a row out. Read the STOPPED suffix before concluding anything is absent. (4) The flag terms can cancel the -1 (e.g. BlueprintVisible +1 → structural 0 → no term in the tooltip). Predict from prop_flags, don't assume. (5) Tooltips need a real hover plus a delay, and grid coordinates reflow (working-lessons §2.5d). (6) Octopath: launch with `steam.exe -applaunch 921570`; it steals focus; it is proxy mode, so trigger_scan / Start Scan is required. (7) One injected game at a time; kill the game and the UI after.

**Related rows:** L62, L46, L57, L59

### L68 — `[W3-CAP-NOSAVE]`

**Status:** ✅ RE-VERIFIED red→green 2026-09-22 (`git log --grep 're-verify(L68)'`); first PASS `650d00d4` retracted in `5517e09e` · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 15 min

**Fix commit(s):** `2b386f26 fix(options): raising a Max cap alone is saved -- and a symmetry pin covers every option [W3-CAP-NOSAVE] (batch L30; adds PropertySearchCap to PropertySearchPersist at MainWindowViewModel.cs:2400 and ClassListCap to GameClassFilterPersist at :2435)`, `650d00d4 verify(L68): PASSED 2026-09-22 (docs only)`

**Fixture:** None. UI only, no game. AOT dist\UE5DumpUI.exe: currently 57,718,784 B, sha256 prefix 9001845c, build 3546. It is newer than the last ui/UE5DumpUI commit (35c99f2f, 2026-09-17 08:57), so it matches HEAD.

**Preconditions:** UI not running. It is single-instance (App.axaml.cs:42-52): a second launch only raises the first window. Back up %LOCALAPPDATA%\UE5CEDumper\ui-options.json first (UiOptionsStore.cs:30-32 = platform AppData + Constants.UiOptionsFile). Its current values are propertySearch.propertySearchCap=2000 and gameClassFilter.classListCap=5000, both read today. dist must be the AOT build: the NumericUpDown binds a decimal? wrapper (PropertySearchViewModel.cs:86-94, GameClassFilterViewModel.cs:42-50), which is the kind of binding CLAUDE.md says fails only after trimming.

**Steps:**

1. 1. Back up ui-options.json and record its sha256 and mtime.
2. 2. Launch dist\UE5DumpUI.exe. Open the 'Properties' tab (en.axaml:27). Click the ▲ spinner of the 'Max' box once (PropertySearchPanel.axaml:53-56, Increment=100): 2000 -> 2100. Touch nothing else, because any other persisted change also schedules a save that includes the cap.
3. 3. DISCRIMINATING READ, with the UI still running: wait at least 1 s (debounce OptionSaveDebounceMs=400, MainWindowViewModel.cs:2271; Track -> ScheduleOptionSave at :2313-2320 / :2298-2303), then diff ui-options.json key by key against the backup. Expect exactly one change, propertySearch.propertySearchCap 2000 -> 2100, and a new mtime (atomic temp + rename at UiOptionsStore.cs:68-70). Pre-fix, the file is untouched at this point.
4. 4. OPTIONAL KILL ARM (also discriminating): `taskkill /IM UE5DumpUI.exe /F`. This skips ShutdownRequested and so FlushOptions. Relaunch: Max reads 2100.
5. 5. The row's literal arm: close the window cleanly (WM_CLOSE), relaunch, and Max reads 2100. This arm alone does not discriminate (see discrepancies).
6. 6. Repeat steps 2-5 on the 'Classes' tab (en.axaml:33), 'Max' box (GameClassFilterPanel.axaml:27-30, Increment=1000): 5000 -> 6000, key gameClassFilter.classListCap.
7. 7. Close the UI, restore ui-options.json from the backup, and confirm the sha matches the backup.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Max | ui/UE5DumpUI/Resources/Strings/en.axaml:321 (str.PropertySearch.MaxResults) and :322 (str.GameClassFilter.MaxResults) | Label beside each NumericUpDown |
| Properties | en.axaml:27 (str.Tab.PropertySearch) | Tab header |
| Classes | en.axaml:33 (str.Tab.GameClassFilter) | Tab header |
| "propertySearch": { ... "propertySearchCap": 2100 } | Models/UiOptionsSettings.cs:37,157 plus the camelCase JsonSerializerContext at :261 | %LOCALAPPDATA%\UE5CEDumper\ui-options.json |
| "gameClassFilter": { ... "classListCap": 6000 } | Models/UiOptionsSettings.cs:44,213 | %LOCALAPPDATA%\UE5CEDumper\ui-options.json |
| UE5DumpUI shutting down... | ui/UE5DumpUI/App.axaml.cs:175 (it runs just before mainVm.FlushOptions() at :177) | Logs\UE5DumpUI\init-0.log on a clean close. It proves the close arm went through the unconditional flush. |
| UiOptionsStore: failed to save | Services/UiOptionsStore.cs:74 (init category) | Logs\UE5DumpUI\init-0.log, only if the save fails. Must be absent. |

**Row text vs source:** FINDING: the row's check, and the recorded PASS, do not discriminate. The row expects 'Close the UI. ui-options.json holds the new value'. On a clean close, App.axaml.cs:172-177 (ShutdownRequested) calls MainWindowViewModel.FlushOptions (:2292-2296). That calls SaveOptionsNow -> BuildOptions unconditionally, and BuildOptions copied BOTH caps before the fix too (pre-fix file 2b386f26^: FlushOptions :2291-2295, `o.PropertySearch.PropertySearchCap = ...` :2647, `o.GameClassFilter.ClassListCap = ...` :2669). So the unfixed build also persists a raised cap on a clean close. The defect only shows when the process ends without ShutdownRequested (crash, taskkill /F, power loss), or when the file is read mid-session. The recorded PASS diffed the file only AFTER a WM_CLOSE, so it would have passed on the unfixed build as well. Evidence that a normal close fires ShutdownRequested: 'UE5DumpUI shutting down...' is present in today's Logs\UE5DumpUI\init-0.log (10:43:02) and in 9 archived init logs. The discriminating form is step 3 (mid-session diff after the 400 ms debounce) or step 4 (hard kill). The finding text in todo.md:1267-1269 ('writes nothing to disk') is accurate only up to shutdown.

**Rig:** No committed rig: grep of tools/verify for ui-options / propertySearchCap / classListCap found nothing relevant, and verify commit 650d00d4 touched only docs/todo.md. Smallest rig: Python that (a) copies ui-options.json to a backup, (b) waits for the operator or computer-use click, (c) polls the file's mtime for up to 2 s while the UI runs and prints a JSON key diff against the backup, and (d) restores the backup after the UI exits. The ▲ click needs computer-use on the UI (front it with `py tools/verify/front_window.py front UE5DumpUI`).

**Traps:** The cap is also written on every clean close, so read the file BEFORE closing. Any other persisted change made during the test (a checkbox, the Deep toggle) also rewrites the file with the cap in it. The debounce save runs on a thread-pool Timer; allow at least 400 ms. Restore ui-options.json afterwards: the maintainer's live values are 2000 / 5000, not the defaults 200 / 5000 (Constants.cs:363,372). ClipValueToMinMax clamps to 100-50000 (Constants.cs:378-379), and ApplyOptions clamps on load too (MainWindowViewModel.cs:2499-2501, 2526-2527), so any value outside that range comes back clamped. window-state.txt is also rewritten on close; back it up if the window is moved. Use the AOT dist, never a -Target UI/Test build (CLAUDE.md Build & Deploy).

**Related rows:** L74, L76, L70, L13, L19

### L69 — `[W5-OFFSETS-UNMEASURED]`

**Status:** 🟡 step 2 ✅ red→green 2026-09-23 (`git log --grep 'verify(L69)'`); steps 1, 3 ⬜ (S9) · **reachability:** `fixture-limited` · **needs CE:** yes · **needs UI:** no · **estimate:** 60 min

**Fix commit(s):** `3373056c`

**Fixture:** Steps 1 and 3 are LIVE on DumperTest Shipping: its latest init-0.log (2026-09-22 10:38:33) reads '[SUMMARY] DynOff: … validated=yes'. Step 2's named fixture is STALE. The UE 5.8 fixture validates: DumperTest58 init logs 2026-09-09 (Development) and 2026-09-16 (Shipping) both read 'validated=yes'. Across every retained log there are 155 '=== Dynamic Offset Summary (validated=YES) ===' lines and zero validated=NO summaries, except two pathological UE 5.4 EDITOR injections (Logs\UnrealEditor\init-0.log 2026-09-07, build 3405, 'Name sanity: 0/10', 'validated=NO (DEFAULTS) reason=no-guid-or-vector-struct'). Those are unverified at HEAD and heavy to stage. Recommended step-2 staging: one line before Genau.cpp:4309 `const bool allMeasured`, `unmeasured |= UNMEASURED_PROP_ELEMSIZE; // STAGING`. That yields validated=NO reason 'unmeasured:elemsize' (table :4288-4306) while every offset stays genuinely measured, so walks and dissect still work. The same staged DLL serves L85's fallback arm.

**Preconditions:** CE launched FRESH (CE is cleared for standing use since 2026-09-16, todo.md:5511; the 'announce first' prefix is stale). The dissect's warned-reason dedup lives in the CE-GLOBAL _ue5_dissect_state (ue5_dissect.lua:28-33, :523-526) and survives a re-dofile. In a warm CE, reset `_ue5_dissect_state.offsetsWarned = nil` first, or step 2's 'first dissect' prints nothing. Load the module as `local dissect = dofile([[D:\Github\UE5CEDumper\scripts\ue5_dissect.lua]])` (scripts/README.md:169; it returns a table and defines no globals, working-lessons §2.5 note :1098). Capture print output into a table and write it with io.open (handover §6).

**Steps:**

1. STEP 1: `launch_dumpertest.py shipping`. In CE, attach to DumperTest-Win64-Shipping.exe, load scripts/UE5CEDumper.CT and tick 'init <== enable after process attached' (UE5CEDumper.CT:12; the real target 'Inject DLL + Start Pipe Server' :915 is a hidden child), or inject with inject.py first.
2. In the Lua Engine: `local b=allocateMemory(128); local r=executeCodeEx(1,5000,getAddress('UE5_GetOffsetsVerdict'),b,128); local why=readString(b,127) or ''; deAlloc(b)`. Expect r==1 and why==''. Then, with print captured: `dissect.createFromPath('/Script/DumperTest.DumperTestActor')`. Expect NO '[UE5Dissect WARN] UE property offsets were NOT measured' line. Afterwards run dissect.clearAll().
3. STEP 3: untick the 'init' record (its [DISABLE] runs UE5_Shutdown, UE5CEDumper.CT:863, which calls DynOff::ResetOffsetsVerdict, Frieren.cpp:686). Re-run the executeCodeEx snippet: expect r==0 and why=='probe-not-run' (Grimoire.h:774-777).
4. Re-tick 'init' (UE5_AutoStart, UE5CEDumper.CT:671). CE's [ENABLE] blocks until READY, so observe the MID-scan state from outside CE. A Python rig uses mailbox_poke as a library with cmd=16 (CMD_OFFSETS_VERDICT). It is init-exempt (Mimic.h:514), and the poller is restarted before UE5_Init (Frieren.cpp:892). Poke repeatedly while initState==1: params decode 'probe-not-run' with result 0 until ValidateAndFixOffsets publishes, then result 1. After READY, the CE executeCodeEx snippet gives r==1, why==''.
5. STEP 2 (staged DLL): build the 'unmeasured' staging, restore the source, and inject the staged DLL into a fresh DumperTest Shipping. Confirm init-0.log reads 'validated=NO (DEFAULTS) reason=unmeasured:elemsize' and `py tools/verify/pipe_client.py get_offsets` returns validated=false, probe_ran=true, fallback_reason='unmeasured:elemsize' (Fern.cpp:5154-5157).
6. In a CE state where offsetsWarned is nil: dissect.createFromPath('/Script/DumperTest.DumperTestActor') prints exactly ONE WARN naming 'unmeasured:elemsize'. A second createFromPath (or createFromClass) prints none. executeCodeEx UE5_GetOffsetsVerdict gives r==0, why=='unmeasured:elemsize', the same string as get_offsets' fallback_reason.
7. Kill the game and CE. Restore dist (SHA) after the staged build.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| [SUMMARY] DynOff: CPN=no FProp=yes TagFFV=yes Outer=+0x20 validated=yes | dll/src/Frieren.cpp:626-633 | init-0.log (SUMMARY → LF_Init, Sein.cpp:544-559). NOT scan.log. Precondition for steps 1/3. |
| === Dynamic Offset Summary (validated=YES) === | dll/src/Genau.cpp:4338-4341 | offsets-0.log [DYNO] |
| [UE5Dissect WARN] UE property offsets were NOT measured (unmeasured:elemsize): this structure uses version defaults and may be misaligned. Re-scan from the UI, then rebuild it. | scripts/ue5_dissect.lua:46 (warn prefix) + :526 | CE Lua Engine output (print). Exactly once per distinct reason (step 2). |
| validated=NO (DEFAULTS) reason=unmeasured:elemsize | dll/src/Frieren.cpp:631-633 (reason literal from Genau.cpp:4297) | init-0.log [SUMMARY] (staged step 2) |
| ValidateAndFixOffsets: PARTIAL — unmeasured:elemsize (validated=NO, offsets below mix probed values with version defaults) | dll/src/Genau.cpp:4330-4333 | offsets-0.log [DYNO] WARN (staged step 2) |
| probe-not-run | dll/src/Grimoire.h:775 (OffsetsVerdictReason); returned by UE5_GetOffsetsVerdict, Frieren.cpp:714-721 | CE executeCodeEx reason buffer after Disable (step 3); mailbox params during the re-scan |
| Mailbox: OFFSETS_VERDICT -> measured=0 reason='probe-not-run' | dll/src/Mimic.cpp:1082 | pipe-0.log (for the Python mid-scan observation) |

**Row text vs source:** (a) 'on a build whose scan log says validated=NO (the UE 5.8 fixture)' is STALE. DumperTest58 logs validated=yes on 2026-09-09 and 2026-09-16, and no retained game log has validated=NO. Grimoire.h:739-741's '(seen on UE 5.8)' describes the pre-fix FFieldClass era. (b) The 'validated=' summary is in init-0.log (SUMMARY), not the scan log. Its NO form is 'validated=NO (DEFAULTS) reason=<r>', printed even for a partial 'unmeasured:*' run where nothing was defaulted wholesale. (c) Step 2's 'names the same reason as get_offsets' fallback_reason' holds only when probe_ran && !validated && the reason is non-empty. Before any probe, the C ABI says 'probe-not-run' while fallback_reason is '' (Grimoire.h:774-777 vs Fern.cpp:5157). (d) Step 3's 'until the next Enable's scan finishes' is not observable from CE's own Lua while the Enable blocks; see the traps.

**Rig:** No rig exists. CE part: Lua snippets typed into CE's Lua Engine (handover §6: capture via io.open to the scratchpad, never from a screenshot). Mid-scan part: about 30 lines of Python importing tools/verify/mailbox_poke.py (Mem, mailbox_addr, poke with cmd=16), decoding params as a C string. Note that poke() returns only 32 param bytes, which is enough for 'probe-not-run' and 'unmeasured:elemsize' but would truncate the longest reasons (up to 57 chars, Genau.cpp:4303). Staging: a one-line Genau.cpp edit + `build.ps1 -Target DLL -NoBumpBuildNumber`, restored byte-exact afterwards.

**Traps:** The dissect dedup state is global and persists across dofile: a warm CE can hide step 2's single warning, and running step 1 first clears it (measured → offsetsWarned=nil). vt* constants must exist before dofile (working-lessons :1098). CE's [ENABLE] for the init record blocks the Lua and GUI thread until READY, so 'until the next Enable's scan finishes' can only be seen from outside CE, via the mailbox (the pipe is not up yet during an injected re-scan: AutoStartWork starts the pipe AFTER UE5_Init, Frieren.cpp:903-921). After a CE Disable the mailbox poller is JOINED (Mimic::StopThread): a mailbox verdict query between Disable and re-Enable times out and arms the helper's stale-mailbox latch (L88), so use the C ABI there. Re-front the Lua Engine after clicks (handover §6), and front windows with front_window.py. CE structures created by the dissect are global: run dissect.clearAll(). Staged-DLL hygiene as in L40.

**Related rows:** L85, L54, L88, L40, L53, L42, L43

### L70 — `[W2-BETWEEN-PREVIEW]`

**Status:** ✅ PASSED `38c7ac49` · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 15 min

**Fix commit(s):** `d2e7de9a`, `38c7ac49`

**Fixture:** Steps 1-2: the UI with no game. Step 3: DumperTest 5.4 Shipping plus a freshly captured snapshot, because SPC's value box is enabled only for a selected snapshot (SpcQueryViewModel.cs:74, AbsEnabled => IsSelected). ✅ ALREADY PASSED 2026-09-22 (docs/todo.md L70; commit 38c7ac49) on AOT sha 9001845c, build 3546.

**Preconditions:** AOT UI. For step 3, the Experimental gate on (the Snapshot/SPC tabs are bound to ExperimentalEnabled, MainWindow.axaml ~485-493) and ≥1 snapshot for the connected game. Back up ui-options.json (ValueSearch.SelectedDataType/ScanType/RoundingMode are persisted).

**Steps:**

1. Re-run only if the build changed. Value Search, **Single value**, **Type:** FVector, **Scan:** Between, **Rounding** Round. POSITIVE CONTROL first: `1` / `4` → preview `→ 1~4`. Then `1,2,3` / `4,5,6` in the same boxes → NO preview (pre-fix `→ 123~456`).
2. Type Int32, Between. Control `1000` / `2000` → `→ 1000~2000`. Then `1,000` / `2,000` → NO preview.
3. Connect to DumperTest Shipping, **Snapshot** tab → **Capture Snapshot**. On **SPC Query**, select that snapshot, set the absolute kind to Exact and type `1,000` → preview `→ 1000` (SpcBoundStyles = NumberStyles.Any, kept by design).
4. Restore ui-options.json. Kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| → {lo}~{hi} | ui/UE5DumpUI/Models/RoundModePreview.cs:73-77,105-118 (Arrow '→ ', RangeSep '~') | Between preview TextBlock next to the Value/… to boxes |
| DllBoundStyles = NumberStyles.Float | ui/UE5DumpUI/Models/RoundModePreview.cs:84 | The grammar that makes '1,2,3' and '1,000' preview nothing |
| SpcBoundStyles = NumberStyles.Any | ui/UE5DumpUI/Models/RoundModePreview.cs:90; used by SpcAbsolute at :122-130 | SPC absolute Exact '1,000' → '→ 1000' |

**Row text vs source:** Already recorded in the row: 'UI only' is wrong for step 3. SPC's value box needs a selected snapshot (SpcQueryViewModel.cs:74), and no snapshot is listed without a connected game. Otherwise current source matches the row. The row is ✅ PASSED 2026-09-22, so this entry is a re-run recipe only.

**Rig:** UI-only (computer-use). No pipe rig applies. The VM half is pinned by ui/UE5DumpUI.Tests/RoundModePreviewTests.cs.

**Traps:** An absent preview proves nothing without a positive control typed into the same boxes first, which is why the PASS used them. Step 3 needs a connected game despite the row's 'UI only'. Restore ui-options.json. AOT build only.

**Related rows:** L71, L81, L60, L19

### L71 — `[W2-DEADSCAN-LOADMORE]`

**Status:** ✅ PASSED red→green 2026-09-23, real Cancel in both modes (`git log --grep 'verify(L71)'`) · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 30 min

**Fix commit(s):** `79b0d3fb`

**Fixture:** DumperTest 5.4 Shipping + the AOT UI. With **Game classes only** OFF, Int32 Exact 0 (single) or NumericNoByte 0 + 0 (group) returns >1000 rows, and PageSize is 1000 (ValueSearchViewModel.cs:420), so Load More shows. DumperTest scans finish in ~50-120 ms (docs: '2 candidates in 52 ms', '836 candidates in 116 ms'), so a manual Cancel is hard to land. Two levers: (a) a deterministic FAILURE stand-in, a value the DLL rejects, which takes the same path because EndSessionIfAnyAsync has already zeroed the session before the await; (b) enlarge the pool with `Spawn_ManyComponents(20000)` (clamped 1..20000, DumperTestActor.cpp:930-932) and enable Deep + Native-C to slow the scan for a real Cancel.

**Preconditions:** AOT UI. Back up ui-options.json (ValueSearch GameOnly/MaxResults/DataType/ScanType/DeepScan/NativeCScan persisted, MainWindowViewModel.cs:2583-2597). Max ≥ 1001 (default 50000).

**Steps:**

1. Optional pipe prep, before the UI connects: invoke `Spawn_ManyComponents` with Count=20000 on the live DumperTestActor (read ParmsSize from its function list; expect 4). This enlarges the pool so a Deep + Native-C scan takes long enough to Cancel.
2. Value Search, **Single value**, untick **Game classes only**, Type Int32, Scan Exact, Value `0`, click **First Scan**. Window status: `Showing 1000 of N — Load More for the rest`, with **Load More** visible.
3. 2a (row, Cancel): set Type NumericAll, tick **Deep (nested containers)** and **Native-C (raw)**, Value `0`. Click **First Scan** then immediately **Cancel** (one computer_batch). StatusText `First Scan cancelled.`.
4. 2b (deterministic stand-in, same state): Type Int32, Value `abc`, click **First Scan**. Error row: `First Scan failed: DLL error: Invalid 'value' for data_type Int32`.
5. After either: the grid still holds the 1000 rows, **Load More** is hidden, and the window status reads `Showing 1000 of N from the previous scan — its session has ended; run First Scan again for the rest`. Next Scan is disabled (HasSession false).
6. Step 3, group: toggle **Multiple values (group)**, two rows NumericNoByte / Exact / `0`, click **Group First Scan**. Expect `Showing 1000 of N — Load More for the rest` with Load More visible (Max must be > 1000).
7. Group failure/cancel: set row 1's value to `abc` and click Group First Scan → `Group First Scan failed: DLL error: Invalid group value 'abc' (fits no numeric width)`. Or Cancel a slow group scan → `Group First Scan cancelled.`. Group window status: `Showing 1000 of N from the previous scan — its session has ended; run Group First Scan again for the rest`, and Load More is hidden.
8. Positive control: click **First Scan** / **Group First Scan** again with a valid value. The status returns to `Showing 1000 of M — Load More for the rest` and Load More returns.
9. Restore ui-options.json. Kill the UI and the game.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Showing {Count} of {FilteredTotal}{filt} — Load More for the rest | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:696 | Single-mode window status TextBlock left of Load More (ValueSearchPanel.axaml:312-318) |
| Showing {Count} of {FilteredTotal}{filt} from the previous scan — its session has ended; run First Scan again for the rest | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:700 | Single-mode window status after the failed/cancelled second scan |
| First Scan cancelled. | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:1042 | Shared status row |
| First Scan failed: DLL error: Invalid 'value' for data_type Int32 | ValueSearchViewModel.cs:1051 + DumpService.cs:3806 + dll/src/Fern.cpp:3257 | Red error row; also 'ValueSearch First Scan failed' in UE5DumpUI view-0.log (ValueSearchViewModel.cs:1052) |
| Showing {Count} of {GroupFilteredTotal}{filt} from the previous scan — its session has ended; run Group First Scan again for the rest | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:1675 | Group window status (ValueSearchPanel.axaml:622-628) |
| Group First Scan cancelled. | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:1478 | Shared status row |
| Group First Scan failed: DLL error: Invalid group value 'abc' (fits no numeric width) | ValueSearchViewModel.cs:1482 + dll/src/Fern.cpp:3619 | Red error row |

**Row text vs source:** None in the expected text. The row says 'Cancel it', but the fix covers failure and cancel identically (commit body; ValueSearchTests use a failure). On DumperTest a Cancel is timing-limited, so the invalid-value failure is the deterministic equivalent.

**Rig:** UI-only (computer-use). No existing rig. ui/UE5DumpUI.Tests/ValueSearchTests.cs has the VM pins (FirstScan_FailingAfterASession_…, GroupFirstScan_FailingAfterASession_…), which use a failure rather than a cancel. Optional pipe prep to grow the pool reuses tools/verify/ad4_contested.invoke.

**Traps:** (1) The window status flips to 'from the previous scan' at the START of the second scan: EndSessionIfAnyAsync's finally sets SessionId=0 (ValueSearchViewModel.cs:1258-1260), which fires OnSessionIdChanged → UpdateWindowStatus. Read it after the cancel/failure anyway, but do not credit the Cancel for it. (2) Cancel is enabled only while IsScanning, and DumperTest scans are ~100 ms, so batch the two clicks or use the invalid-value stand-in. (3) A cancelled scan leaves an orphan DLL session that idles out after 5 min. Harmless. (4) Group mode needs Max > 1000 for Load More, and Max is set only in single mode. (5) Game-window focus stealing: front_window.py before the clicks. (6) AOT build only.

**Related rows:** L60, L81, L70, L65, L17

### L72 — `[W3-DIP-PIXELS]`

**Status:** ✅ PASSED `0d182b15` · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 30 min

**Fix commit(s):** `96ac6c78 fix(dialogs): the restore-state guard judges a dialog by its physical size, not its DIP size [W3-DIP-PIXELS] (batch L31; WindowRestoreState.SetScale + PositionAcceptable w*_scale; ManagedDialogWindow pushes RenderScaling at :81 and :143)`, `0d182b15 verify(L72): PASSED 2026-09-22 (docs only)`

**Fixture:** DumperTest UE 5.4 SHIPPING (`py tools/verify/launch_dumpertest.py shipping`) supplies a class with a real row in the Classes tab: DumperTestActor (package-identity.json reflected_present). It runs on this machine's standing 3840x2400 display at 216 dpi (225%). Any connected game would do, because the dialog only needs a class address. Shipping is preferred per the launcher's header.

**Preconditions:** One injected game at a time. Back up ui-options.json and window-state.txt. Confirm the scale with `py tools/verify/af21_hidpi_placement.py --probe`: it returns before launching anything (:158-159). Its band, however, is for MainWindow's 1124 DIP, so recompute it for 860 DIP. After connecting, check that the object count is non-zero (about 24.5k on Shipping per the row), because a process that exists is not necessarily a game that booted.

**Steps:**

1. 1. `py tools/verify/launch_dumpertest.py shipping` (writes out/host.pid), then `py tools/verify/inject.py --name DumperTest` or `--pid <pid>`.
2. 2. Launch AOT dist\UE5DumpUI.exe and click 'Connect' (en.axaml:6). Check the object count.
3. 3. `py tools/verify/front_window.py front UE5DumpUI`. In the Classes tab click 'Load' (en.axaml:864), filter for DumperTestActor, then click the row's 'Find Func' button (en.axaml:869; GameClassFilterPanel.axaml:176-181). A modal PropertyXrefDialog (a ManagedDialogWindow) opens, titled 'Functions taking class: DumperTestActor' (PropertyXrefDialog.cs:128), 860x520 DIP (:147-149).
4. 4. Rig (Python ctypes, per-monitor DPI aware as in af21_hidpi_placement.py:57-60): find the hwnd by that title with EnumWindows, and record the open rect from GetWindowRect (the row saw (938,521)-(2901,1770)).
5. 5. Compute the band from WindowPlacement.IsVisibleEnough (MinVisibleWidth=120, WindowPlacement.cs:13, :29-44). Post-fix, the guard accepts x >= 120 - round(860*S). Pre-fix, it accepts x >= 120 - 860. At S=2.25 the band is [-1815, -740), with midpoint about -1277.
6. 6. ARM 1, the witness: SetWindowPos(hwnd, 0, -1300, y, 0, 0, SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE). Sleep 1 s so the Background-priority commit runs (ManagedDialogWindow.cs ScheduleSnapshotCommit / CommitSnapshot). ShowWindow(SW_MAXIMIZE=3) and assert IsZoomed. ShowWindow(SW_RESTORE=9), sleep 2 s, read GetWindowRect. EXPECT left == -1300. The pre-fix build returns the open position (938): NotePosition rejected -1300 because it measured 860 px.
7. 7. ARM 2, the control: SetWindowPos to x=-2809, which is fully off-screen. Maximize with ShowWindow, because the dialog's own button is off-screen. Restore. EXPECT left == -1300, NOT -2809. This proves the app's deferred re-apply ran (ManagedDialogWindow.cs HandleWindowStateTransition Post at Background) and that the guard still rejects a genuinely off-screen rect.
8. 8. Optional positive arm: x=200 -> maximize/restore -> 200.
9. 9. Close the dialog, the UI and the game. Diff ui-options.json and window-state.txt against the backups, and restore them.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Functions taking class: DumperTestActor | ui/UE5DumpUI/Views/PropertyXrefDialog.cs:128 | Dialog window title (use it for FindWindow/EnumWindows) |
| Find Func | en.axaml:869 (str.GameClassFilter.FindFunc) | Per-row button in the Classes grid |
| Load | en.axaml:864 (str.GameClassFilter.Load) | Classes tab toolbar |
| Connect | en.axaml:6 (str.Connect) | Main toolbar |

**Row text vs source:** (a) The row's 'x ≈ -1707' is MainWindow's AF21 witness: the midpoint of [120-2529, 120-1124) = [-2409, -1004) for a 1124-DIP window. The Find Func dialog is 860 DIP (PropertyXrefDialog.cs:147). Its band is [-1815, -740), so -1707 lies inside the band but only 108 px from its lower edge. (b) FINDING: 'about a third hangs off the LEFT edge' contradicts -1707 and does not discriminate on this dialog. A third of 1935 px off-screen means x ≈ -645, which is >= -740, so the unfixed build also accepts it and the check passes vacuously. The discriminating position has between about 38% and 94% of the dialog off-screen. The PASS used -1300, about two thirds off (a third visible). (c) 'Drag it' cannot be done here: Windows 11 Snap took the dialog to the left half three times (recorded in the PASS). SetWindowPos plus the off-screen control arm is the working substitute.

**Rig:** No committed rig for ManagedDialogWindow: grep of tools/verify for W3-DIP / ManagedDialog / PropertyXrefDialog found none relevant, and 0d182b15 is docs only. tools/verify/af21_hidpi_placement.py covers MainWindow's CROSS-RESTART path only: it seeds window-state.txt and reads the placement after relaunch, which is a different code path (MainWindow.OnOpenedValidatePlacement). Reuse its helpers: the DPI-awareness setup, ui_window() EnumWindows/GetWindowRect (:66-85), and scaling() (:92-96). New rig outline: find the dialog by title, run arms 1, 2 and optionally 3 with SetWindowPos/ShowWindow/IsZoomed/GetWindowRect as in the steps, and print PASS or FAIL per arm. This check produces no log line: ManagedDialogWindow.cs has no logging, so the window rect is the only observable.

**Traps:** Windows 11 Snap captures drags to the edge. Turning Snap off is a system setting and is prohibited, so place the window programmatically. The dialog is modal (ShowDialog(owner), PropertyXrefDialog.cs:111), so the main window is disabled while it is open. The rig must be per-monitor DPI aware, or GetWindowRect/SetWindowPos coordinates are virtualized and the band arithmetic is wrong. Compare GetWindowRect with GetWindowRect: it includes the invisible resize borders (the row's 1963 px against 1935 px), and mixing it with client or Avalonia Position values shifts the numbers by about 14 px. SetScreens/SetScale are refreshed only at Opened and on WindowState transitions (ManagedDialogWindow.cs:79-81, :142-143), so keep the dialog on the one monitor. Game windows steal focus: run front_window.py before any UI click. The UI occupies pipe slots, so do not also run pipe_client-driven rigs against the same game. Kill the game and the UI the moment the row is done.

**Related rows:** L73, L71, L79, L81

### L73 — `[P8-BOOKMARK-TIP]`

**Status:** ✅ PASSED red→green 2026-09-23 (`git log --grep 'verify(L73)'`) · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 15 min

**Fixture:** Any connected game. The first choice is DumperTest Shipping (`py tools/verify/launch_dumpertest.py shipping` + `py tools/verify/inject.py --pid <out/host.pid>`). Any two distinct objects work. For example: A = the UWorld reached with 'Start from GWorld'; B = PersistentLevel (double-click its row), or the object reached with 'Start from GameEngine', or the live DumperTestActor through the address box + 'Go'.

**Steps:**

1. Before connecting, list %LOCALAPPDATA%\UE5CEDumper\Bookmarks\. Today it holds bookmarks.0D281EF60A6D5000.json (a DataTable<ShipModule> slot) and bookmarks.6A7EA60310F17000.json (a 'ThirdPersonMap' UWorld slot). Get this session's pe_hash from `py tools/verify/pipe_client.py get_pointers` (Fern.cpp:1344). If bookmarks.<pe_hash>.json already exists, copy it aside.
2. In the AOT dist UI, Connect, open the 'Live Walker' tab and click 'Start from GWorld'. Note A's class, name and address from the header.
3. Click ★ (the button turns yellow; tooltip 'Save mode on: click a slot (1-8)…'), then click slot 1. The status must read 'Bookmark 1 saved' and the slot label must become A's name (first 14 chars + '..').
4. Navigate to B, e.g. double-click the PersistentLevel row, or 'Start from GameEngine'. Click ★, then slot 1 AGAIN. The status reads 'Bookmark 1 saved' and the label repaints to B's name.
5. Take a fresh screenshot to re-measure slot 1: the label width changed, so the bar reflowed. Move the pointer away, then hover slot 1 for about 1 s, then zoom. The tooltip must read 'Jump to bookmark 1: <B class> :: <B name>' on its first line and <B address> on its second. Pre-fix it kept naming A.
6. Navigate somewhere else, e.g. Start from GWorld again, then click slot 1. The status must read 'Bookmark 1 loaded' and the header must show B, with the same address the tooltip named.
7. Clean up: right-click slot 1 → 'Clear this bookmark'. If no file existed for this pe_hash, '✕' (Clear ALL, which deletes the file) is also fine. Otherwise put the backup back. Kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Jump to bookmark {DisplayNumber}: {SavedClassName} :: {SavedObjectName}\n{SavedAddress}\nClick to restore this view (object, selected rows, scroll). | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:7421-7422 (raised by RefreshTooltip() at :7411, called from SaveBookmarkToSlot at :3868) | Tooltip of Live Walker bookmark slot 1 (ToolTip.Tip="{Binding TooltipText}", LiveWalkerPanel.axaml:129-131). It must name B after the re-save. |
| Bookmark {DisplayNumber} saved | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:3870 | Live Walker status line; expected twice ('Bookmark 1 saved') |
| Bookmark saved slot={SlotIndex} addr={CurrentAddress} name={CurrentObjectName} sel={n} top={topName} | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:3872 | Logs\UE5DumpUI\view-0.log (+ ui-view-0.log in the game folder). There must be two lines, both slot=0: the first with A's addr, the second with B's. |
| Bookmark {DisplayNumber} loaded | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:4023 | Live Walker status after clicking slot 1 |
| Bookmark loaded slot={SlotIndex} addr={SavedAddress} sel={n} top={topName} full={restoredFully} | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:4032 | view-0.log. addr must equal B's address, and it must match the tooltip's second line. |
| Bookmark {DisplayNumber}: empty - no bookmark saved.\nClick ★ then this slot to save the current view here. | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:7423 | The slot tooltip before the first save. A quick sanity check that you are reading the right control. |

**Row text vs source:** None. The row's three steps match SaveBookmarkToSlot (:3832-3872), the RefreshTooltip fix (:3866-3868, :7410-7411) and LoadBookmark (:3892-4032). One clarification: the row's 'Bookmark object A into slot 1' must land in an EMPTY slot 1 for step 2 to be the TRUE → TRUE case. It always is, unless the game's bookmark file already has slot 1 filled, in which case step 1 is itself the discriminating re-save.

**Rig:** No rig exists, and no tools/verify file mentions bookmarks outside retention sweeps. This is a computer-use row: ★ is str.LiveWalker.BookmarkSave '★' (en.axaml:300), then the slot buttons in the ItemsControl at LiveWalkerPanel.axaml:120-153, then hover + zoom for the tooltip. The pipe is used only for pe_hash (get_pointers), and optionally to get B's address independently (walk_instance on the world → PersistentLevel ptr) so the tooltip address can be checked against ground truth.

**Traps:** (1) Only a TRUE → TRUE re-save exercises the fix. The first save into an EMPTY slot flips IsOccupied and always refreshed the tooltip (LiveWalkerViewModel.cs:7404-7406), so it proves nothing. The second save into the same slot is the check. (2) An Avalonia tooltip already open, or last shown, can hide a stale-vs-fresh difference. Move off the button and hover again after the re-save. (3) The label change reflows the bookmark bar, so re-measure coordinates from a fresh screenshot (working-lessons §2.5d). (4) Clicking a slot while save mode is on SAVES instead of loading (:3897-3901). Make sure ★ is idle (grey) before the load click. (5) Bookmarks persist per game under Bookmarks\bookmarks.<PEHASH>.json with retention OFF (CLAUDE.md App-data layout), so clean up or restore. (6) 'Bookmark is refused while the grid is behind the spine' (RefuseWhileGridBehindSpine, :3838). Let navigation finish before pressing ★. (7) Use the AOT dist exe. Kill the game and the UI after.

**Related rows:** L79, L63, L47, L56

### L74 — `[A1-LOG-RESUME]`

**Status:** ✅ PASSED `4d0cde0c` · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 10 min

**Fix commit(s):** `7ba7519c fix(logging): a rolled session's -0_NNN.log files are archived at startup, so the next session starts at -0.log [A1-LOG-RESUME] (batch L35; LoggingService.ArchivePreviousLog :320-324)`, `4d0cde0c verify(L74): PASSED 2026-09-22 (docs only)`

**Fixture:** None. UI only, no game. AOT dist\UE5DumpUI.exe (build 3546, sha 9001845c).

**Preconditions:** UI not running. Back up ui-options.json. ui-options.json currently has system.autoCompressLogs=false (read today). Keep it off, or the opt-in startup sweep may compress the planted archives. The UI log folder is %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\ (LoggingService.cs:71, Constants.LogSubfolderName="UE5DumpUI" at Constants.cs:28). It currently holds no *-0_*.log files and has live init-0.log / pipe-0.log / view-0.log.

**Steps:**

1. 1. With the UI closed, write two marker files into Logs\UE5DumpUI\: pipe-0_001.log (mtime T1) and pipe-0_002.log (mtime T2 > T1), each with one 'L74 SEED' line. Keep both mtimes inside the last 21 days (LogMaxAgeDays, Constants.cs:44) and the files under 4096 B (LogCompressMinSizeBytes, Constants.cs:55). Cheaper extra arm needing no Connect: also plant init-0_001.log.
2. 2. Launch dist\UE5DumpUI.exe. Before any Connect: no pipe-0_*.log remains. Each seed was renamed to pipe-<yyyyMMdd-HHmmss of its OWN mtime>.log (LoggingService.cs:343, :349), with a -1/-2 suffix for a same-second pair. Seeds are archived in name (sequence) order (:322-324) and their contents are intact.
3. 3. Init arm: init-0.log is a NEW file whose first session line is 'UE5DumpUI starting...' (App.axaml.cs:92), and init-0_001.log has been archived.
4. 4. Click 'Connect' (en.axaml:6) with no game running. The line 'Connecting to pipe: UE5DumpBfx...' (PipeClient.cs:126; PipeName at Constants.cs:9) lands in a NEW pipe-0.log. Both archives keep their original size and mtime. Pre-fix, Serilog.Sinks.File resumed the highest sequence and appended this line to pipe-0_002.log.
5. 5. Close the UI. Delete only the seed archives, and restore ui-options.json from the backup.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Connecting to pipe: UE5DumpBfx... | ui/UE5DumpUI/Services/PipeClient.cs:126 ($"Connecting to pipe: {Constants.PipeName}..."; Constants.cs:9 PipeName="UE5DumpBfx") | Logs\UE5DumpUI\pipe-0.log (pipe category, Constants.LogCatPipe) |
| UE5DumpUI starting... | ui/UE5DumpUI/App.axaml.cs:92 | Logs\UE5DumpUI\init-0.log (init category) |
| pipe-20260920-100000.log  (pattern: {prefix}-yyyyMMdd-HHmmss[-N].log from the file's own local mtime) | ui/UE5DumpUI/Services/LoggingService.cs:343 (stamp), :348-349 (suffix/dst) | Archived file names in Logs\UE5DumpUI\ |

**Row text vs source:** None of substance. The row's 'oldest first' is implemented as ORDINAL NAME order of {prefix}-0_*.log (LoggingService.cs:322-323), not mtime order. That equals sequence order while the numbers stay zero-padded to three digits (_001.._999). The row also says 'Generate one with a >8 MB session, or copy one in'. A copied real file older than 21 days is archived and then immediately deleted by PruneAgedLogs, so only freshly dated seeds prove anything.

**Rig:** No committed rig: grep of tools/verify for A1-LOG-RESUME / pipe-0_0 found nothing, and 4d0cde0c is docs only. Smallest rig: a Python seeder that writes the two or three marker files, sets their mtimes with os.utime, and lists the folder before and after launch. Launch the UI with subprocess and poll for the main window with the af21_hidpi_placement.py ui_window() pattern. Pass means the seeds are archived under their own mtimes and pipe-0.log/init-0.log are new. Connect needs one computer-use click, or skip it and use the init arm alone. Cleanup deletes the seed archives by their marker content.

**Traps:** The order is archive then prune (LoggingService.cs:80-83; PruneAgedLogs deletes non-'-0.log' files older than 21 days). A seed whose mtime is older than 21 days therefore vanishes, which reads like a failed archive. With system.autoCompressLogs=true (opt-in, App.axaml.cs:116), archives over 4096 B and older than 7 days (Constants.cs:55, :83) are compressed right after startup and change name. Serilog creates pipe-0.log lazily on the first pipe event, so nothing appears until Connect; the init arm avoids that dependency. ArchiveOne deletes the source outright after 100 same-second name collisions (LoggingService.cs:354), so never plant dozens of same-second seeds. GetLastWriteTime is LOCAL time. The same fix also runs for the per-game mirror folder Logs\<GameProcess>\ui-{cat}-0_NNN.log on StartProcessMirror (LoggingService.cs:199-204, MirrorLogPrefix="ui" at Constants.cs:29); that arm needs a connected game and is not in the row. The UI is single-instance.

**Related rows:** L68, L76, L70, L13

### L75 — `[A3-RECYCLE-GUID-FAILOPEN]`

**Status:** ✅ PASSED `fa0a5439` (regression-only here: SUBST also refused pre-fix) · **reachability:** `fixture-limited` · **needs CE:** no · **needs UI:** yes · **estimate:** 45 min

**Fix commit(s):** `314cb54b fix(proxy-cleanup): a failed volume-GUID lookup fails closed where the volume flag decides [A3-RECYCLE-GUID-FAILOPEN] (batch L38; RecycleBinPolicy.IsDisabled `if (volumeLookupFailed) return true;` :84; WindowsPlatformService.VolumeHasRecycleBin passes volumeLookupFailed: guid.Length == 0 at :899)`, `fa0a5439 verify(L75): PASSED 2026-09-22 (docs only) -- with the note that the fix is not the only guard on SUBST`

**Fixture:** The row as written is reachable: a SUBST drive (built-in subst.exe) over a COPY of a small real UE game. The B13 recipe (auto-memory project_b13_recycle_bin_refusal_done.md) and the L75 run both used Steam's 'Light Maze' from D:\SteamLibrary\steamapps\common\Light Maze (215 MB). docs/test-games.md has NO 'Light Maze' entry (grep -i 'light maze' returns 0 hits), so a later stage must re-check the install via libraryfolders.vdf and not rely on the name. The fix's DISCRIMINATING path is fixture-limited. It needs a DRIVE_FIXED volume mounted at a drive root (for example an ImDisk-style RAM disk not registered with the Mount Manager) where GetVolumeNameForVolumeMountPointW FAILS but SHQueryRecycleBinW returns S_OK. On SUBST, SHQueryRecycleBinW also fails (0x80070003, measured in the PASS), so pre-fix code refused too. The PASS records that no such volume exists on this machine.

**Preconditions:** UI not running, no game running. Back up ui-options.json. Run `subst` with no arguments and confirm X: is free. Create the SUBST from the SAME elevation level the UI will run at: DOS-device maps differ between elevated and non-elevated tokens. Work only on a copy of the game, never the real install.

**Steps:**

1. 1. Copy the game to D:\SteamLibrary\steamapps\common\L75 LightMaze SUBST copy, then run `subst X: "D:\SteamLibrary\steamapps\common\L75 LightMaze SUBST copy"`.
2. 2. Launch AOT dist\UE5DumpUI.exe and open the 'Proxy Deploy' tab (en.axaml:24). Click 'Scan Drives' (en.axaml:776) with only X: selected. It should find X:\LightMaze\Binaries\Win64. Deploy version.dll there. view-0.log receives 'Deployed <type> to <game>: X:\LightMaze\Binaries\Win64\version.dll' (ProxyDeployService.cs:1201; the 'ProxyDeploy' category resolves to the view logger at LoggingService.cs:115-120). That line is the DeployLog source the orphan scanner parses later (ProxyOrphanScanner.cs:673-688).
3. 3. Cut the copy down to the after-uninstall shape: delete everything except LightMaze\Binaries\Win64\version.dll. That includes Engine\Extras\Redist\en-us\UEPrereqSetup_x64.exe, which otherwise vetoes as LiveContentPresent. Re-scan X: until it reads 'Found 0 UE game(s)', so the Games grid no longer holds the row (a LiveGameFolder veto otherwise).
4. 4. Click 'Find leftovers' (en.axaml:785). EXPECT a status built at ProxyDeployViewModel.cs:825-829: 'Found 2 leftover proxy DLL(s) — nothing removed yet. 1 cannot be removed from here — read the row for why. Press “Report…” for a dry run of exactly what would go.'
5. 5. The X:\LightMaze\Binaries\Win64 row (source DeployLog) shows the blocker text from ProxyOrphanScanner.cs:341-343 (via OrphanProxy.ActionSummary :112). Its checkbox does not tick, 'Delete checked (0)' (en.axaml:789) stays at 0, and the confirm dialog does not list it. No log line is written for this refusal.
6. 6. CONTROL, the same physical file reached through D:: its row (source SteamShapeScan) reads 'Recycle version.dll, then remove up to 4 folder(s) it leaves empty, stopping below …' (OrphanProxy.cs:108-109). Tick it, click 'Delete checked (1)', then confirm with 'Move to Recycle Bin' (en.axaml:802). EXPECT 'Cleaned 1 of 1 leftover(s) — 1 file(s) recycled, 4 folder(s) removed' (ProxyDeployViewModel.cs:1128-1130), and view-0.log 'Recycled leftover proxy D:\…\version.dll' (ProxyDeployService.cs:1863). Check D:\$Recycle.Bin\<SID>\$I*.dll: it should name the original D: path, and its $R payload sha should match the deployed DLL.
7. 7. Optional attribution (read-only ctypes replay): on X:\LightMaze\Binaries\Win64 call GetVolumePathNameW (VolumeRoot.cs:55), GetDriveTypeW, GetVolumeNameForVolumeMountPointW, then SHQueryRecycleBinW, to record which gate refuses. Post-fix the refusal is RecycleBinPolicy.cs:84 (volumeLookupFailed). If SHQueryRecycleBinW also fails, the pre-fix code refused at WindowsPlatformService.cs:908 and the run does not discriminate.
8. 8. Teardown: `subst X: /D`. Restore ui-options.json. Leave the recycled test DLL in D:'s Recycle Bin: emptying it is a permanent delete. Confirm the real game install is untouched.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| This volume has no working Recycle Bin (removable/network, or the bin is disabled for it), so a delete here would be PERMANENT. Refused — remove the file by hand if that is what you want. | ui/UE5DumpUI/Services/ProxyOrphanScanner.cs:341-343 (shown via Models/OrphanProxy.cs:112) | Action text of the X: row in the Leftover proxy DLLs list |
| Found {N} leftover proxy DLL(s) — nothing removed yet. {blocked} cannot be removed from here — read the row for why. Press “Report…” for a dry run of exactly what would go. | ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:825-829 | Proxy Deploy operation-result status after Find leftovers |
| Recycle {DllNames}, then remove up to {N} folder(s) it leaves empty, stopping below {dir} | ui/UE5DumpUI/Models/OrphanProxy.cs:108-109 | Action text of the D: (control) row |
| Cleaned {ok} of {attempted} leftover(s) — {files} file(s) recycled, {dirs} folder(s) removed | ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:1128-1130 | Status after the control cleanup |
| Recycled leftover proxy {file} | ui/UE5DumpUI/Services/ProxyDeployService.cs:1863 | Logs\UE5DumpUI\view-0.log (category 'ProxyDeploy' -> view logger) |
| Could not recycle {file} (refused or no Recycle Bin) | ui/UE5DumpUI/Services/ProxyDeployService.cs:1868 | view-0.log. Only if a refused row somehow reached removal; must be absent. |
| Deployed {type} to {game}: {targetDll} | ui/UE5DumpUI/Services/ProxyDeployService.cs:1201 | view-0.log at step 2 (the DeployLog source) |
| Move to Recycle Bin | en.axaml:802 (str.ProxyDeploy.Orphans.ConfirmYes) | Confirm dialog button |

**Row text vs source:** (1) FINDING (recorded in the PASS; it bears on the verdict): SUBST does not discriminate on this Windows build. On X:, GetVolumePathNameW returns the non-root 'X:\LightMaze\', the GUID lookup fails (err 4390), and SHQueryRecycleBinW on that root ALSO fails (0x80070003). So the pre-fix code refused too, at its second gate (WindowsPlatformService.cs:908). The row's 'This path is unmeasured, and this check measures it' is only half met: the refusal now comes from the new branch (RecycleBinPolicy.cs:84), but the fail-open that branch closes was never reachable through SUBST here. (2) The row's expected wording, refusing instead of 'claiming "moved to the Recycle Bin"', is not literal UI text. The success wording is 'Cleaned … — N file(s) recycled' (ProxyDeployViewModel.cs:1128-1130) and the confirm button reads 'Move to Recycle Bin' (en.axaml:802). (3) The PASS quotes the status lines truncated. Source appends ' Press “Report…” for a dry run of exactly what would go.' (ProxyDeployViewModel.cs:829) and ', stopping below {dir}' (OrphanProxy.cs:109).

**Rig:** No committed rig: grep of tools/verify for RECYCLE-GUID / SUBST found only unrelated substring hits, and fa0a5439 is docs only. Reusable pieces: the B13 recipe of copying a real game rather than faking one (auto-memory), and subst.exe. Smallest rig: Python that copies the game, creates and removes the SUBST, prunes the copy to the after-uninstall shape, then runs a read-only ctypes replay of VolumeHasRecycleBin's Win32 sequence (VolumeRoot.Resolve -> GetDriveTypeW -> GetVolumeNameForVolumeMountPointW -> HKCU BitBucket registry reads -> SHQueryRecycleBinW), printing each result and which gate refuses. The UI parts (Scan Drives, deploy, Find leftovers, tick, confirm) need computer-use.

**Traps:** The refusal writes NO log line, and Report… does not name per-candidate veto verdicts (PASS text), so a veto looks exactly like 'no leftovers'. Two vetoes each cost the PASS run a full attempt: a stray UEPrereqSetup_x64.exe (LiveContentPresent), and the row still sitting in the Games grid (LiveGameFolder, LiveBinariesDirs() built from Games). SUBST mappings are per logon token, so a mismatch between an elevated and a non-elevated shell makes X: invisible to the UI. Never empty the Recycle Bin: that is a prohibited permanent delete. Deploy only into the copy, never the real install. MoveToRecycleBin re-checks VolumeHasRecycleBin (WindowsPlatformService.cs:998), so the scan-time and removal-time verdicts agree. Restore ui-options.json, because the proxyDeploy options are persisted (MainWindowViewModel.cs ProxyDeployPersist).

**Related rows:** L13, L51, L62

### L76 — `[A3-COORD-NONFINITE]`

**Status:** ✅ PASSED `8b56eb32` · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 15 min

**Fix commit(s):** `939a9512 fix(teleport): a non-finite coordinate in a CSV or Lua import is a rejected row, not the world origin [A3-COORD-NONFINITE] (batch L39; `&& double.IsFinite(value)` in CoordPrecision.TryParse, CoordinateLibraryFile.cs:154-158)`, `8b56eb32 verify(L76): PASSED 2026-09-22 (docs only)`

**Fixture:** None. UI only, no game connected. AOT dist\UE5DumpUI.exe (build 3546). Needs the experimental gate on: %LOCALAPPDATA%\UE5CEDumper\experimental.json currently reads {"enabled": true, "snapshotQuotaMb": 0} (read today).

**Preconditions:** UI not running. Back up ui-options.json and take a listing (with hashes) of %LOCALAPPDATA%\UE5CEDumper\TeleportCoords\. The Coordinate Library card is hidden unless experimental is enabled (TeleportPanel.axaml:527-529 IsVisible="{Binding ExperimentalEnabled}"). If it is off, turn it on with 'Enable advanced experimental features' (en.axaml:500, System tab) and restore it afterwards.

**Steps:**

1. 1. Write l76_nonfinite.csv (UTF-8) with header `uid,label,group,map,x,y,z,pitch,yaw,roll` (the codec's header, CoordCsvCodecTests.cs:117) and three rows: a CONTROL row l76a with x=100.5, then l76b with x=NaN, then l76c with x=1e400. All other columns finite.
2. 2. Launch the AOT UI with no game. Open the 'Teleport' tab (en.axaml:32) and expand the 'Coordinate Library (0)' card (header at TeleportViewModel.cs:3546-3549), which sits below 'Teleport to Coordinates'. Click 'Import CSV…' (en.axaml:1297; TeleportPanel.axaml:665). A native open dialog appears ('CSV file', TeleportViewModel.cs:4199); pick the file.
3. 3. EXPECT this preview, built at TeleportViewModel.cs:4228-4245: 'l76_nonfinite.csv: 1 row(s) read — 1 added.' then a newline and 'Skipped / adjusted:', then '  line 3 [x]: not a number — 'NaN'' and '  line 4 [x]: not a number — '1e400'', then 'Press Apply to commit (a .preimport.bak is written first).' Pre-fix, both rows parse (double.TryParse accepts NaN and overflows 1e400 to +Inf), Round maps them to 0, and the preview reads '3 row(s) read — 3 added.' with no issues.
4. 4. Click 'Cancel import' (en.axaml:1300). EXPECT 'Import cancelled — nothing was changed.' (TeleportViewModel.cs:4335). The header stays at (0). Do NOT press 'Apply import'.
5. 5. EXTRA ARM, not in the row but covered by the same fix: paste into the script box (TeleportPanel.axaml:689) the four lines `-- @UE5CD:COORDS v1` / `local COORDS = {` / `  { uid='a', label='A', map='M', x=1e400, y=2, z=3 },` / `}` / `-- @UE5CD:END` (fence regexes at CoordLuaParser.cs:38-43). Click 'Parse pasted script' (str.TP.LibImportLua, en.axaml:1310). EXPECT 'Nothing importable from pasted script:' followed by a line '  line <n> [x]: not a number — '1e400'' (TeleportViewModel.cs:4216, CoordLuaParser.cs:185). Only 1e400 discriminates on the Lua path: the RawNum regex (CoordLuaParser.cs:230-235) already kept the words NaN and Infinity out before the fix.
6. 6. Close the UI. Confirm no TeleportCoords\ file changed and no *.preimport.bak appeared. Diff ui-options.json against the backup and restore it.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| {sourceName}: {N} row(s) read — {SummarizeDiff}. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:4228, :4230; SummarizeDiff 'N added' at Services/CoordCsvCodec.cs:487 | Coordinate Library import preview text |
| Skipped / adjusted: | TeleportViewModel.cs:4242 | Import preview, followed by issue lines indented two spaces (FormatIssues :4249-4254) |
| line {Line} [{Column}]: {Reason} — '{RawText}'  -> 'line 3 [x]: not a number — 'NaN'' / 'line 4 [x]: not a number — '1e400'' | Services/CoordCsvCodec.cs:14-17 (ToString) and :307 (reason 'not a number') | Import preview |
| Press Apply to commit (a .preimport.bak is written first). | TeleportViewModel.cs:4244 | Import preview |
| Import cancelled — nothing was changed. | TeleportViewModel.cs:4335 | Coordinate library status after Cancel import |
| Nothing importable from pasted script: | TeleportViewModel.cs:4216 (sourceName 'pasted script' at :4443) | Import preview for the Lua arm |
| Import CSV… / Cancel import / Apply import / Parse pasted script | en.axaml:1297, :1300, :1299, :1310 | Coordinate Library card buttons |

**Row text vs source:** None in substance: the row's expectation (both rejected on column x, neither imported) matches CoordCsvCodec.cs:305-309. The PASS quote flattens the preview's newlines into ' · '; the source separates lines with \n plus two-space indents (TeleportViewModel.cs:4242, :4251). The row does not exercise the Lua half of the fix (CoordLuaParserTests: x=1e400 reported, not imported); step 5 covers it.

**Rig:** No committed rig: grep of tools/verify for COORD-NONFINITE / 1e400 found nothing, and 8b56eb32 is docs only (l10_step6_age_sweep.py only mentions CoordinateLibraryStore's retention). Smallest rig: Python writes the CSV and the Lua snippet and snapshots the TeleportCoords\ listing with hashes before and after, plus the ui-options.json backup and restore. The UI part needs computer-use: the native file picker needs the path typed. The Lua arm needs only a paste and one click, and is the cheapest to automate.

**Traps:** The card is invisible unless experimental.json has enabled=true. Toggling it persists and also changes experimental hotkey binding, so restore it. Apply writes a .preimport.bak and the per-game TeleportCoords\ file, which is irreversible for the maintainer's library, so only Cancel. With no game connected, the active library key is the no-game default: do not Apply into it. The native OpenFileDialog needs computer-use typing, and game or other windows can steal focus (front_window.py). Without the control row the preview would be 'Nothing importable from …', a different branch (TeleportViewModel.cs:4211-4219). Keep the finite control row so the rejected rows appear in the normal preview. Use the AOT dist.

**Related rows:** L68, L74, L70, L14

### L77 — `[W2-CEGEN-MODAL]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** yes · **needs UI:** yes · **estimate:** 45 min

**Fix commit(s):** `1b69a23c`, `8a2b37e8`

**Fixture:** DumperTest **Development** (`launch_dumpertest.py dev`), used twice: once injected, to create and tick the records, and once as a fresh UN-injected process for the untick. Dev is required for the Debug Camera half: UCheatManager is live only when !UE_BUILD_SHIPPING (launch_dumpertest.py:94-96), and on Shipping the toggle answers -1 ('Mailbox: SET_DEBUG_CAMERA req=1 -> state=-1', todo.md:5857 L88 record), so that record can never be left ticked there. The GodMode half works on either flavour (needs a pawn: BP_ThirdPersonCharacter).

**Preconditions:** A single CE with AOBMaker, so the UI pushes records straight into CE (else they come as clipboard XML to paste). AOT dist (exe contains 'nothing to turn off'). UE5_DEBUG must be set in CE's Lua Engine BEFORE the untick, because each chunk captures `local DEBUG = UE5_DEBUG or 0` at its start (CeLuaHygiene.cs:110-115). The only way to have a record ACTIVE while the DLL is absent is to tick it on an injected process, then re-attach CE to a fresh un-injected process and answer 'No' to CE's disable prompt (MainUnit.pas:3156-3195).

**Steps:**

1. 1. `launch_dumpertest.py dev` + `inject.py --name DumperTest`. Start one CE and attach to DumperTest.exe. Start the UI and Connect.
2. 2. Teleport tab → God Mode card → 'Add to CE' (str.TP.GmCopyScript, en.axaml:1093). The status reads "Added 'God Mode (toggle)' to Cheat Engine via AOBMaker — …". Console tab → 'Load' (en.axaml:949) → the Debug Camera strip appears → 'Copy CE Script' (en.axaml:981). The status reads 'Debug Camera AA Script created in CE (tick = ON, untick = OFF).' (MainWindowViewModel.cs:2051).
3. 3. In CE's address list, tick both records. They must succeed and stay ticked. pipe-0.log shows 'Mailbox: PROTECT op=… -> rc=…' and 'Mailbox: SET_DEBUG_CAMERA req=1 -> state=1'.
4. 4. Close the UI. Kill DumperTest and confirm it is gone. `launch_dumpertest.py dev` again, with NO inject. In CE, attach to the new DumperTest.exe. Answer 'Keep the current address list/code list?' with Yes, then 'There are one or more auto assembler entries or code changes enabled in this table. Do you want them disabled? (without executing the disable part)' with **No**. Both records must still read active.
5. 5. STEP 1a (the real user case): with UE5_DEBUG unset, untick GodMode from the address list. There must be NO dialog and no Lua Engine window. STEP 1b: in the Lua Engine run `UE5_DEBUG = 1`, then untick Debug Camera. There must be no dialog, and the Lua Engine prints '[DebugCamera] g_invokeMailbox not found -- nothing to turn off'. (To see both records in both modes, repeat steps 1-4 with the roles swapped.) Capture Lua output through io.open, not a screenshot (handover §6).
6. 6. STEP 2: with the DLL still not injected, tick GodMode. EXACTLY ONE dialog, '[GodMode] g_invokeMailbox not found -- is UE5Dumper.dll injected?', and the record unticks about 50 ms after OK (the deferred untick). CE then runs [DISABLE] (B30-REOPEN), which with UE5_DEBUG=1 only prints '[GodMode] g_invokeMailbox not found -- nothing to turn off'. Pre-fix, that DISABLE popped a SECOND dialog, so 'one dialog' is the discriminator. Repeat for Debug Camera: '[DebugCamera] g_invokeMailbox not found -- is UE5Dumper.dll injected?'.
7. 7. Set `UE5_DEBUG = nil`. Kill the game, CE and UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| [GodMode] g_invokeMailbox not found -- nothing to turn off | ui/UE5DumpUI/Services/ProtectionScriptGenerator.cs:66 | CE Lua Engine output ([DISABLE] via dbg, only when UE5_DEBUG=1) |
| [DebugCamera] g_invokeMailbox not found -- nothing to turn off | ui/UE5DumpUI/Services/DebugCameraScriptGenerator.cs:63 | CE Lua Engine output ([DISABLE] via dbg) |
| [GodMode] g_invokeMailbox not found -- is UE5Dumper.dll injected? | ui/UE5DumpUI/Services/ProtectionScriptGenerator.cs:59-60 (then DeferredUntickLua, :61) | CE showMessage dialog on a failed TICK (exactly once) |
| [DebugCamera] g_invokeMailbox not found -- is UE5Dumper.dll injected? | ui/UE5DumpUI/Services/DebugCameraScriptGenerator.cs:56-57 | CE showMessage dialog on a failed TICK |
| var bail = enable ? MailboxTimeout.UntickAndReturn : MailboxTimeout.SilentReturn | ui/UE5DumpUI/Services/ProtectionScriptGenerator.cs:72; DebugCameraScriptGenerator.cs:69 | governs contract/idle/wait bails in [DISABLE] (dbg only) — not reached when the mailbox is absent |
| There are one or more auto assembler entries or code changes enabled in this table. Do you want them disabled? (without executing the disable part) | D:\Github\cheat-engine\Cheat Engine\MainUnit.pas:1157-1159 (used at :3188) | CE prompt on re-attach — answer No |

**Row text vs source:** The row's 'With the DLL not injected, untick each record' presupposes the records are ticked while the DLL is absent. That state exists only through CE's re-attach 'keep active' path (MainUnit.pas:3156-3195). The row does not say so, nor that the Debug Camera record can only be ticked on a build with a live CheatManager (dev). Step 2's 'The ENABLE dialog still appears' is sharpened by source: after the deferred untick CE runs [DISABLE], which now only dbg()s, so exactly ONE dialog is the observable fix, where pre-fix gave two. Texts match source.

**Rig:** NONE existing for these two generators. Live: computer-use on CE (full tier) plus the Lua Engine. Offline half, per working-lessons §2.8: with AOBMaker offline both buttons copy CE XML. Extract <AssemblerScript>, split the {$lua} blocks, and load the [DISABLE] block under the machine's Lua (%LOCALAPPDATA%\Programs\Lua\bin\lua) with stubbed getAddressSafe → nil, showMessage recorded, UE5_DEBUG=1, print captured. Assert zero showMessage calls and the dbg line. This is the scripts/tests/*.lua convention, and untick_bailout_test.lua already models CE's setActive lifecycle. The stubs are not CE: the 'CE runs [DISABLE] after a failed ENABLE' half stays live-only.

**Traps:** Records cannot be made active without executing [ENABLE]. The ONLY route to 'active while the DLL is absent' is the re-attach with 'No' to the disable prompt. If 'Ask to clear list on opening new process' is off (cbAskToClearListOnOpen) the first prompt is skipped, but the second still appears. The Debug Camera record can only be ticked on dev. Tick AUTO-CLOSING records from the address list, not from inside the Lua Engine: a successful [ENABLE] closes the Lua Engine, giving a 'Can not focus' driver error (handover §6). showMessage is modal and blocks a Lua-driven `Active=` assignment. UE5_DEBUG is captured per chunk start, so set it first. One CE instance only. The Debug Camera left ON in session 1 is irrelevant after the kill. Close the UI before the relaunch so it does not reconnect to the un-injected process (nothing to connect to anyway).

**Related rows:** L83, L80, L42, L43, L34

### L78 — `[A2-CABI-TELEPORT-PARENTREL]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** yes · **needs UI:** no · **estimate:** 60 min

**Fix commit(s):** `46b0f7b7`

**Fixture:** DumperTest **Shipping** with a MANUFACTURED attachment, the recipe already proven twice (L27 todo.md:5616, L37 todo.md:5692). (a) `K2_AttachToActor` on the live BP_ThirdPersonCharacter_C onto a live StaticMeshActor, all three EAttachmentRule bytes = KeepWorld (1), weld 0; ReturnValue must be 01. DumperTestActor returns 00 because it has no root component. (b) The failing world read: `set_invoke_timeout {timeout_ms:200, persist:false}` plus suspending ONLY the UE game thread (`tools/verify/suspend.py suspend-tid`), which makes `K2_GetActorLocation` fail so GetPoseImpl falls back to RelativeLocation (Wirbel.cpp:425-461). DumperTest itself has no vehicle, mount or platform. Real hosts listed in docs/test-games.md (not needed; install presence to be re-checked): Palworld (line 80), Satisfactory (lines 67/68/76), Hogwarts Legacy (line 60).

**Preconditions:** DumperTest Shipping injected (`launch_dumpertest.py shipping`, `inject.py --name DumperTest`). The UI stays CLOSED: all pipe work goes through pipe_client, and CE uses no pipe slot. The dist DLL exports UE5_TeleportGetPoseEx / GetMarkerEx / GetLastEx (byte-grep confirmed). CE is attached to the Shipping CHILD exe. The hint cache (%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.%COMPUTERNAME%.json) has no invokeTimeoutMs for the current Shipping PE hash 09FF6E55081C9000.

**Steps:**

1. 1. Pipe (UI closed): find_instances BP_ThirdPersonCharacter_C and StaticMeshActor (take live, non-Default__ ones). Read K2_AttachToActor's param layout from the function listing, then `invoke_function` {instance_addr: pawn, func_name: 'K2_AttachToActor', params_hex: <ParentActor ptr, SocketName None, 01 01 01, 00>, parms_size: <listed>}. The result's ReturnValue byte must be 01.
2. 2. CONTROL A (attached, thread running): `teleport_get_pose` gives source 'invoke' and NO parent_relative key (Fern.cpp:6228 emits it only when true). In CE's Lua Engine: `local b = allocateMemory(4096); writeInteger(b+0x100, 0x7777); executeCodeEx(0, 10000, getAddressSafe('UE5Dumper.UE5_TeleportGetPoseEx'), b, b+0x80, 64, b+0x100)`. Read 6× readDouble(b+i*8), readString(b+0x80,64) and readInteger(b+0x100). The flag must be 0 (sentinel overwritten) and the pose must equal world coordinates.
3. 3. `set_invoke_timeout {"timeout_ms":200,"persist":false}`. Run `py tools/verify/suspend.py threads DumperTest-Win64-Shipping`, then `suspend-tid` on the process main thread. Confirm by EFFECT: `get_diagnostics` game_thread responsive=false, liveness 'stalled', hook_fire_count frozen, while the pipe still answers.
4. 4. STEP 1: repeat the GetPoseEx call. The flag must be **1**, rc 0, and the pose must be the RELATIVE numbers (L27/L37 measured (30.000, 31.714, 284.025) against world (900.000, 1110.000, 92.013)), map 'ThirdPersonMap'. walk-0.log gets the WARN 'Teleport: attached pawn but K2_GetActorLocation failed — falling back to RelativeLocation (parent-relative!)'. Cross-check `teleport_get_pose`: source 'raw' AND parent_relative true.
5. 5. STEP 4 (originals, same state): `executeCodeEx(0,10000,getAddressSafe('UE5Dumper.UE5_TeleportGetPose'), b2, b2+0x80, 64)` must fill the SAME six doubles as step 4 (3-arg signature unchanged).
6. 6. STEP 3: save a marker while degraded, via `executeCodeEx(0,10000,getAddressSafe('UE5Dumper.UE5_TeleportSaveMarker'), 1)` or pipe `teleport_save_marker {slot:1}`. walk-0.log WARN: 'Teleport: marker 1 saved from a PARENT-RELATIVE read -- its pose is not world coordinates'. Then GetMarkerEx(1, b3, b3+0x80, 64, b3+0x100) must give flag 1 and the same pose, and the original GetMarker(1, b4, b4+0x80, 64) the same pose.
7. 7. STEP 2 (flag 0): `suspend.py resume-tid`. GetPoseEx gives flag 0 while STILL attached (source invoke), so the flag tracks the degradation, not the attachment. Detach with `K2_DetachFromActor` (EDetachmentRule KeepWorld 01 01 01; returns VOID, so there is no ReturnValue to check). Then GetPoseEx on foot must give flag 0. Save marker 2 on foot: GetMarkerEx(2) flag 0 (control), while GetMarkerEx(1) still gives 1 (stored).
8. 8. Optional: GetLastEx and GetLast. The 'last' slot is written by SaveLastImpl before a jump (Wirbel.cpp:1345-1353) and carries ParentRelative.
9. 9. Restore: `set_invoke_timeout {"timeout_ms":0}` (back to 5000). Re-read the hint cache: no invokeTimeoutMs on peHash 09FF6E55081C9000. deAlloc the buffers, then kill the game and CE.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| int32_t UE5_TeleportGetPoseEx(double* outPose6, char* outMapName, int32_t mapNameCap, int32_t* outParentRelative)  → *outParentRelative = (rc == 0 && parentRel) ? 1 : 0 | dll/src/Frieren.cpp:1361-1369; declared Frieren.h:209-210 | the int32 flag buffer read back with readInteger in CE |
| UE5_TeleportGetMarkerEx(int32_t slot, double*, char*, int32_t, int32_t* outParentRelative) — flag copied from m.ParentRelative | dll/src/Frieren.cpp:1371-1382; Frieren.h:211-212 | CE flag buffer |
| Teleport: attached pawn but K2_GetActorLocation failed — falling back to RelativeLocation (parent-relative!) | dll/src/Wirbel.cpp:460-461 (LOG_CAT WALK → walk.log) | %LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest-Win64-Shipping\walk-0.log [WARN] |
| Teleport: marker %d saved from a PARENT-RELATIVE read -- its pose is not world coordinates | dll/src/Wirbel.cpp:1496-1498 | Logs\DumperTest-Win64-Shipping\walk-0.log [WARN] |
| parent_relative: true (with source: "raw") | dll/src/Fern.cpp:6221, :6228 | teleport_get_pose pipe reply (cross-check) |
| NOTE for CE: executeCodeEx cannot retrieve these return values | dll/src/Frieren.h:189-190 | header note — judge by the out-buffers, not rc |

**Row text vs source:** The row requires 'a game with a vehicle or mount'. DumperTest has none, but the parent-relative condition is fully manufacturable on it (Wirbel.cpp:425 attached test + :459 failed invoke), as L27 and L37 already did. 'Force the world read to fail if possible' = invoke timeout 200 ms + game-thread suspend. The row's step 4 names 'the three original getters', but its steps exercise only GetPoseEx/GetMarkerEx, so GetLastEx and GetLast need the optional jump step. Frieren.h:189-190 warns that executeCodeEx cannot retrieve these return values, so the rc half of 'fills the pose and sets the flag' must be judged from the buffers.

**Rig:** NONE committed. The L37 driver `l37.py` (find / attach / pose / slowtimeout / detach / restore) is named in todo.md:5692 but is not in tools/verify. Existing parts: `tools/verify/pipe_client.py` (find_instances, invoke_function, set_invoke_timeout, teleport_get_pose, teleport_save_marker, get_diagnostics) and `tools/verify/suspend.py threads|suspend-tid|resume-tid`. `tools/verify/call_export.py` cannot be used: it CreateRemoteThread's a no-argument export, and these take 4-5 args. So CE's executeCodeEx is the route, as the row intends. Suggested new rig: tools/verify/l78_parentrel_arm.py (attach / degrade / heal / detach / restore verbs, from the L27/L37 recipe), plus a CE Lua snippet that writes all readbacks to a scratch file with io.open (handover §6).

**Traps:** Keep the UI closed, or disconnected, whenever pipe_client runs (handover §4.4). set_invoke_timeout PERSISTS by default (`persist` defaults to true, Fern.cpp:1864): pass persist:false and clear with 0 anyway. Never use the 100 ms floor. The fixture is capped at 15 FPS (~66 ms/frame), so a 100 ms timeout makes HEALTHY invokes fail and destroys control A. Suspend ONLY the game thread (the main thread by creation time, suspend.py docstring). A whole-process suspend also stops the pipe and mailbox. Attach to a StaticMeshActor, not DumperTestActor (no root, ReturnValue 00). DumperTest does not respawn its pawn, so never teleport to absolute coordinates. Queued K2_GetActorLocation invokes drain on resume (harmless reads). GetMarkerEx has a 5th, stack argument, so use executeCodeEx with a plain integer list. Pre-fill the flag with a sentinel so 'unchanged' and '0' can be told apart. The module is 'UE5Dumper' for a direct inject (a proxy would be version/dxgi/...). CE must attach to the Shipping child exe.

**Related rows:** L27, L37, L38, L80, L36

### L79 — `[W4-HEXSORT]`

**Status:** ⬜ · **reachability:** `fixture-limited` · **needs CE:** no · **needs UI:** yes · **estimate:** 45 min

**Fixture:** DumperTest Shipping (+ Spawn_Holders(300), or reuse L63's 4096) is the vehicle. The DISCRIMINATING SHAPE, though, is a property of the run's address-space layout, not of the fixture. The check can fail only when one grid's result set holds an inverting pair: two addresses of different hex-digit counts where the SHORTER one has the larger leading digit, e.g. 0x2A12345678 (10 digits, 12 chars) next to 0x1D702BCFB30 (11 digits, 13 chars). That is exactly the row's '12- and 13-character' wording. The usual layout on this machine never inverts. Of the addresses recorded in docs/todo.md, 96 are 11-digit heap (0x1xx–0x2xx) and 7 are 12-digit module (0x7FF…). Heap vs module (13 vs 14 chars) always sorts the same as text and numerically. A pre-flight must therefore prove an inversion exists for each surface. Surfaces that can invert (scattered addresses): Instances 'Address' (find_instances), LW '[Ptr]' (pointer VALUES, e.g. the elements of PersistentLevel.Actors), Find Refs 'Owner Addr' (find_refs_to_uobject, max 32), LW Functions 'Address' (walk_functions), Class/Struct field 'Address' (walk_class FField addresses). Structurally vacuous: Instance Finder fields 'Address' and LW 'Address' (FieldAddress = instance base + offset, all the same length within one object), and 'Owner Addr' in container matches (find_by_address normally yields ≤ 1 row).

**Steps:**

1. Build or confirm the AOT exe. dist\UE5DumpUI.exe is 57.7 MB, dated 2026-09-17 09:51, which is after the last ui/ commit (08:57), so it is AOT. The DataGrid column sort is the reflection-shaped area CLAUDE.md warns fails only after trimming, so a non-trimmed exe does not count.
2. Launch DumperTest Shipping, inject, and Connect the UI. Spawn_Holders(300) from a pipe rig gives a large Instances set.
3. PREDICT (third pipe slot), per surface: fetch the same data the grid shows. That is find_instances class_name=DumperTestHolder limit=5000 exact_match=true (the 'addr' key); walk_instance of PersistentLevel with array_limit high, then the Actors elements' 'ptr'; find_refs_to_uobject addr=<World or DumperTestActor> max_results=32; walk_functions addr=<DumperTestActor class_addr>; walk_class addr=<class>. For each, print the digit-length histogram, then sorted(text) vs sorted(key=int(a,16)), ascending and descending, and the index of the FIRST differing row. A surface with no inversion is recorded as 'cannot fail on this data', not as a pass.
4. For every surface with a predicted inversion: open the grid ('Instances' tab → 'Address' header, en.axaml:311; Live Walker fields '[Ptr]', en.axaml:283; 'Find Refs' → 'Owner Addr'; 'Functions' grid → 'Address'; 'Class Struct' tab → fields 'Address'). Click the header once (ascending) and read rows 1-3 with zoom. They must match the NUMERIC prediction. Click again (descending) and check the same way. Follow b16_coord_sort.py's lesson: arrange, e.g. with the Instances name filter, for the inverted pair to sit at row 1 so one glance decides.
5. Row step 2: click the 'Hex' header in the Instance Finder fields grid and in the Live Walker fields grid. The order must equal the ORDINAL order of the displayed hex strings (memory-order byte dumps; walk_instance field key 'hex', Fern.cpp:1466). Discriminator: two int32 fields where the little-endian dump order differs from the numeric order, e.g. value 1 = '01000000' sorts AFTER value 256 = '00010000'.
6. Record per column: inversion present? (y/n), asc row 1-3, desc row 1-3. Kill the game and the UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| ["Address"] = DataGridSortComparers.Hex<InstanceResult>(r => r.AddressValue) | ui/UE5DumpUI/Views/InstanceFinderPanel.axaml.cs:20 (wired :38); column InstanceFinderPanel.axaml:224-227 | Instances grid 'Address' column. Source pin only: sorting emits no UI string or log line. |
| ["OwnerAddress"] / ["FieldAddress"] Hex comparers | ui/UE5DumpUI/Views/InstanceFinderPanel.axaml.cs:26, :32; columns InstanceFinderPanel.axaml:134-137 ('Owner Addr'), :389-392 ('Address') | Instance Finder container-matches and fields grids |
| ["FieldAddress"] / ["PtrAddress"] / Functions ["Address"] / References ["OwnerAddress"] Hex comparers | ui/UE5DumpUI/Views/LiveWalkerPanel.axaml.cs:55-56, :71, :78 (wired :91-93); columns LiveWalkerPanel.axaml:654-658, :930, :453-455 | Live Walker fields grid 'Address' and '[Ptr]'; Functions grid 'Address'; Find Refs grid 'Owner Addr' |
| ["Address"] = DataGridSortComparers.Hex<FieldInfoModel>(r => r.AddressValue) | ui/UE5DumpUI/Views/ClassStructPanel.axaml.cs:16 (grid x:Name ClassFieldsGrid, ClassStructPanel.axaml:90; column :122-126) | 'Class Struct' tab, fields grid 'Address' |
| A raw byte dump in MEMORY order ("Key \| Value" for a map element), not a number: text order is memcmp order | ui/UE5DumpUI.Tests/DataGridSortWiringTests.cs:347-350 (AddressColumnNumericExemptions) | The reason the 'Hex' columns (InstanceFinderPanel.axaml:387-388, LiveWalkerPanel.axaml:650-652) stay text-sorted, which is row step 2 |

**Row text vs source:** (a) The row lists eight address columns. The commit message says 'Nine address/hex columns… sorted as text'; that is seven address + two Hex, with Class/Struct as the tenth found by the pin. Eight comparers were wired (InstanceFinderPanel.axaml.cs:20/:26/:32, LiveWalkerPanel.axaml.cs:55/:56/:71/:78, ClassStructPanel.axaml.cs:16). Row and source agree; the commit prose is the loose one. (b) 'a result set that mixes 12- and 13-character addresses' is the right inverting shape (10 vs 11 hex digits), but it is not what this machine's UE processes normally produce. The recorded heaps are 11-digit and the modules 12-digit, which never invert, so the row may be unrunnable in a given session without a pre-flight. (c) Two of the listed columns (Instance Finder fields Address, Live Walker field Address) and usually the container-matches Owner Addr cannot exercise the fix on any data.

**Rig:** No live rig exists for the tag. tools/verify/b16_coord_sort.py is the method precedent (stage/predict, orders differing at row 1). The smallest new rig is a read-only PREDICT script on the third pipe slot. It imports PipeClient from tools/verify/pipe_client.py and find_live_actor from ad4_contested.py, pulls the five datasets above, and prints the first index where ordinal-sorted ≠ numeric-sorted, ascending and descending, with the predicted top 3 rows. Python's ordinal str sort approximates Avalonia's default culture-aware string sort; they agree on [0-9A-Fx] text. The header clicks and row reading are computer-use. The permanent regression check already exists as DataGridSortWiringTests rule 4 (every address-like sortable column carries a Hex comparer) plus DataGridSortComparersTests. Run it with -Target Test if the live data turns out vacuous, but remember that -Target Test republishes dist\ NON-TRIMMED (CLAUDE.md), so re-run -Mode Publish afterwards.

**Traps:** (1) The check is VACUOUS unless the data holds an inverting pair. Typical UE layouts (11-digit heap + 12-digit 0x7FF… modules) sort identically as text and as numbers, so a 'numeric order observed' on such data proves nothing. Predict first. (2) The FieldAddress columns cannot invert within one object. The row's inclusion of 'Instance Finder's fields' and 'Live Walker's field Address' can only be recorded as 'cannot fail'. (3) AOT-only: a non-trimmed exe can sort correctly where the trimmed one fails. Hand over and run -Mode Publish only, and note that -Target UI/Test overwrite dist\ with the non-trimmed exe. (4) Header clicks toggle asc/desc/none depending on state. Read the arrow from a screenshot rather than tracking it (working-lessons §2.5d). (5) The UI holds 2 of 3 pipe slots (Fern.h:51 kMaxPipeInstances = 3), so the predict rig gets exactly one. Do not run two rigs at once. (6) Empty or unparseable addresses map to 0 and sort first in both orders; they are not evidence either way. (7) Live Walker's toolbar reflows once an object loads ('Find Refs' / 'Related' appear). Re-measure coordinates. (8) Kill the game and the UI after.

**Related rows:** L73, L63, L47, L56

### L80 — `[A1-SLOTSYM-FAILED]` `[A1-LUA-WAIT]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** yes · **needs UI:** yes · **estimate:** 50 min

**Fix commit(s):** `d4ee88d7`

**Fixture:** DumperTest **Shipping** (the GWorld query and the Lua emitters are engine-independent), injected, with a MANUFACTURED busy mailbox: `tools/verify/sw3_mailbox_busy.py arm` suspends every game thread and writes cmd = 0x7FFFFFFF with WriteProcessMemory, so every emitted idle-wait sees a busy mailbox while memory stays readable.

**Preconditions:** One CE with AOBMaker attached to DumperTest-Win64-Shipping.exe. The UI is connected only to create the records. AOT dist (exe contains 'UE5_slotSymHolders', 'if _idleTick then _idleOver = (', 'elseif _tick then _over = ('). CE has getTickCount (CE LuaHandler.pas:16795). ⚠ On this PC CE's sleep(1) costs about 15.47 ms (CeMailboxLayout.cs:150), so 100 × 15.47 ≈ 1547 ms ≥ MailboxIdleWaitMs 1500 (CeMailboxLayout.cs:180/:188). The pre-fix min(ms, N×sleep) deadline therefore EQUALS the fixed one here unless sleep is made cheap (step 6).

**Steps:**

1. 1. `launch_dumpertest.py shipping` + inject; start CE (one) and attach; start the UI and Connect.
2. 2. Teleport tab → 'Global Pointers → Cheat Engine symbols' card → 'Get GWorld' (en.axaml:1039). Record A is pushed through AOBMaker: "Added 'Get GWorld → symbol UE_GWorld' to Cheat Engine via AOBMaker — …". Click 'Get GWorld' AGAIN: the status reads "'Get GWorld → symbol UE_GWorld' was already pushed to Cheat Engine this session — copied it as CE memory-record XML instead of adding a second record. …" (TeleportViewModel.cs:2036-2060). Paste it into CE's address list: that is record B. Add a manual CE record with address [UE_GWorld] (8 Bytes hex, or pointer UE_GWorld + 0).
3. 3. In the Lua Engine run `UE5_DEBUG = 1`. Tick A from the ADDRESS LIST. `print(getAddressSafe('UE_GWorld'))` must be non-nil and the manual record must resolve (a UWorld*). pipe-0.log gets 'Mailbox: QUERY_PTR GWorld -> &GWorld=0x… UWorld*=0x…'.
4. 4. `py tools/verify/sw3_mailbox_busy.py arm --image DumperTest-Win64-Shipping.exe`. It must print 'armed      : cmd = 0x7FFFFFFF OK'. THE GAME IS FROZEN until disarm.
5. 5. STEP 2: tick B. After about 1.5 s the dialog reads '[GWorld] the DLL mailbox is busy -- try again in a moment'; press OK and B unticks. CE then runs B's [DISABLE], which prints '[GWorld] UE_GWorld was never registered by this record (its enable failed) -- nothing to release'. A must still read Active, `getAddressSafe('UE_GWorld')` must be non-nil, and the manual [UE_GWorld] record must still resolve. There must be NO 'Mailbox: received cmd=13' for B in pipe-0.log.
6. 6. STEP 3, still armed: in the Lua Engine, extract the emitted query() from B's ENABLE text: `local s = <B>.Script:match('%[ENABLE%](.-)%[DISABLE%]'); local body = s:match('(local function query%(op%).-\n  return a\nend)')`. Then `local q = load("local mb = getAddressSafe('g_invokeMailbox')\n" .. body .. "\nreturn query")()`. Save the real sleep with `local S = sleep` and set `sleep = function() end` (sleep is a lua_register'd global, CE LuaHandler.pas:16225). Time it: `local t = getTickCount(); local a, e = q(0); local dt = getTickCount() - t`, then `sleep = S`. Expect dt ≥ 1500 and e == 'the DLL mailbox is busy -- try again in a moment'. NEGATIVE CONTROL: gsub the chunk's `if _idleTick then … else … end` deadline into the pre-fix `local _idleOver = _idleTick and (_idleTick() - _idleT0 >= 1500) or (_idleIters >= 100)`. With sleep a no-op it gives up far below 1500 ms. Write dt values to a scratch file with io.open.
7. 7. `py tools/verify/sw3_mailbox_busy.py disarm --image DumperTest-Win64-Shipping.exe` (ALWAYS). It prints 'cmd = 0x0 (IDLE, restored)' and resumes the threads.
8. 8. Untick A. The Lua Engine prints '[GWorld] UE_GWorld unregistered'. `getAddressSafe('UE_GWorld')` must be nil and the manual record must show ??.
9. 9. Set UE5_DEBUG = nil; kill the game, CE and UI.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| [GWorld] UE_GWorld was never registered by this record (its enable failed) -- nothing to release | ui/UE5DumpUI/Services/CeLuaHygiene.cs:873 (tag/sym from PointerQueryScriptGenerator.cs:62-63, :272) | CE Lua Engine output from B's [DISABLE] (UE5_DEBUG=1) |
| [GWorld] UE_GWorld unregistered | ui/UE5DumpUI/Services/CeLuaHygiene.cs:889 | CE Lua Engine output when A is unticked |
| [GWorld] UE_GWorld still held by <n> other record(s) -- left registered | ui/UE5DumpUI/Services/CeLuaHygiene.cs:879 | must NOT appear for B (it would mean B was counted as a holder) |
| the DLL mailbox is busy -- try again in a moment | ui/UE5DumpUI/Services/PointerQueryScriptGenerator.cs:126 (shown as '[GWorld] ' .. err at :215) | CE showMessage on B's failed tick; also the `e` returned by query() in step 6 |
| if _idleTick then _idleOver = (_idleTick() - _idleT0 >= 1500) / else _idleOver = (_idleIters >= 100) end | ui/UE5DumpUI/Services/CeLuaHygiene.cs:186-189; constants CeMailboxLayout.cs:180, :188 | emitted Lua inside every idle wait (the shape under test) |
| elseif _tick then _over = (_tick() - _t0 >= 10000) | ui/UE5DumpUI/Services/CeLuaHygiene.cs:643-648; CeMailboxLayout.cs:145, :154 | emitted status-wait deadline |
| Mailbox: QUERY_PTR GWorld -> &GWorld=0x%llX UWorld*=0x%llX | dll/src/Mimic.cpp:1378 | %LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest-Win64-Shipping\pipe-0.log (A only) |
| 'Get GWorld → symbol UE_GWorld' was already pushed to Cheat Engine this session — copied it as CE memory-record XML instead of adding a second record. | ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2057-2059 | Teleport status line on the 2nd click |

**Row text vs source:** (a) Step 2's example, 'Tick B while its ENABLE fails (for example, before the DLL is injected)', is incoherent with step 1: A must already be ticked successfully, which requires the DLL, and on the same process B cannot then fail for 'not injected'. The only non-injected route is re-attaching CE to another process, where A's [UE_GWorld]+offset 'still resolving' is meaningless. The coherent manufacture is a busy mailbox (sw3_mailbox_busy.py), which fails B's ENABLE at the idle wait before any register step (PointerQueryScriptGenerator.cs:125-127, :211-217). (b) Step 3 as written ('gives up at the real millisecond deadline, not after N sleeps') cannot discriminate on this PC: sleep(1) ≈ 15.47 ms makes N×sleep (1547 ms) ≥ the 1500 ms deadline, so the pre-fix min() equals the post-fix deadline. It needs cheap sleep (the no-op override) plus the pre-fix negative control. (c) 'Make two PointerQuery "Get GWorld" records': the UI deliberately refuses a second AOBMaker push and falls back to the clipboard (TeleportViewModel.cs:2029-2060).

**Rig:** EXISTING: `tools/verify/sw3_mailbox_busy.py arm|disarm|show --image <exe>` manufactures the busy mailbox that makes B fail while A stays valid, and the busy state step 3 needs. The rest is CE Lua Engine work plus computer-use for the Teleport clicks and the paste. ⚠ The offline rigs `scripts/tests/slotsym_gworld_test.lua` and `slotsym_release_test.lua` are STALE against this fix: (1) their stub `env.memrec = {Active = true}` has no ID, so the new `UE5_slotSymHolders['UE_GWorld'][memrec.ID] = true` (CeLuaHygiene.cs:836) raises 'table index is nil'; (2) case 2 reuses ONE memrec for both records, so its last release is now correctly 'not mine' and the test's 'last holder releases it' would fail; (3) the contract stub returns 3 while scripts now bake 5 (CeMailboxLayout.cs:92); (4) the captured scripts out/slotsym/get_gworld.lua.txt / get_gameengine.lua.txt date from 2026-08-20 (pre-fix). None of them has a failed-ENABLE case. Fix those stubs (a per-record memrec {ID=n}) before relying on them.

**Traps:** Do not follow the row's 'before the DLL is injected' example (see discrepancies). The second 'Get GWorld' click never pushes, so B comes from the clipboard. sw3 `--image` defaults to DumperTest.exe (dev), so pass the Shipping image name. Between arm and disarm the game is FROZEN: always disarm, even on failure. Tick A (an auto-closing record, since a clean success closes the Lua Engine when DEBUG=0) from the address list, not from inside the Lua Engine (handover §6). B's showMessage is modal and blocks Lua, so do not time B's tick; time the extracted query() instead. The `sleep` override is process-global in CE's Lua state: restore it, or every other script runs sleepless. Without the override the step-3 check CANNOT fail on this machine (1500 vs about 1547 ms). With sleep a no-op, the fixed loop spins a core for 1.5 s (acceptable; do not do this with the 10 s status wait). Wiring: sw3 WriteProcessMemory needs PROCESS_ALL_ACCESS to the game (non-elevated has been fine on this PC).

**Related rows:** L77, L78, L88, L54, L69, L85

### L81 — `[A4-AB4-BETWEEN]`

**Status:** ✅ PASSED red→green 2026-09-22, DLL + C# (`git log --grep 'verify(L81)'`) · **reachability:** `live` · **needs CE:** no · **needs UI:** yes · **estimate:** 60 min

**Fix commit(s):** `47226924`

**Fixture:** DumperTest 5.4 Shipping. ADumperTestActor holds `uint16 U16 = 54321` and `int16 I16 = -12345` (DumperTestActor.h:318-319, .cpp:194-195). L17 measured them captured in snapshots at off 1582/1580. That is neither the row's 'small value' nor 'near 32767', so there are two arms. NO-WRITE: ranges the defect also breaks, `Between -5 60000` (U16=54321; -5 has no unsigned encoding) and `Between -20000 70000` (I16=-12345; 70000 has no Int16 encoding). LITERAL: write_mem U16:=7 and I16:=32760, then use the row's `-5..10` and `10..70000`, then restore. The engine CDO `Default__IntSerialization` (every width, L17 precedent) is an alternative, but it needs game_only=false and risks the 50,000 cap.

**Preconditions:** DLL build ≥ the 47226924 fix. Back up ui-options.json (ValueSearch type/scan/GameOnly/Max persisted). The Experimental gate on for the Snapshot tab. For the literal arm, record U16/I16's original bytes and restore them.

**Steps:**

1. PIPE (UI disconnected): find_live_actor. `walk_instance addr=<actor> array_limit=32` → U16/I16 offsets (derive them; do not assume 1582/1580).
2. Single arm 1: `begin_value_scan data_type=NumericNoByte scan_type=Between value="-5" value2="60000" game_only=true max_results=50000`, then check_complete / deadline_hit=false. `query_candidates session_id=… filter="U16"` → a DumperTestActor.U16 row, field_type UInt16Property. Pre-fix: absent.
3. Single arm 2: `Between -20000 70000` → the I16 row (Int16Property). Control: `Between 40000 70000` must NOT return I16 (the predicate still applies).
4. Literal arm: write_mem U16=0x0007 and I16=0x7FF8 (32760) via tools/verify/mutate_guard.write_bytes, then re-read. Repeat with `Between -5 10` (expect U16) and `Between 10 70000` (expect I16). end_value_scan each session.
5. Group slot: `begin_group_scan game_only=true max_results=50000 per_slot_cap=4096 values=[{data_type:NumericNoByte,scan_type:Between,value:"-5",value2:"10"},{data_type:NumericNoByte,scan_type:Exact,value:"424242"}]` (FrozenInt=424242 anchors the actor). Then `query_group_slot_leaves session_id instance_addr=<actor> slot_index=0` must list U16. Repeat with slot0 `Between 10 70000` → I16 in the list. Assert per_slot_cap_hit=false (the default 256 can truncate the zero-heavy list).
6. Restore U16/I16 to the original bytes (54321 / -12345) and re-read. If the snapshot arm uses the literal values, capture the snapshot BEFORE restoring.
7. UI single + group (optional, same assertions): Value Search, NumericNoByte, Between `-5` … to `10`, **First Scan**; type U16 in the filter box. Group mode: set **Leaves/slot:** to 4096, then **Group First Scan**, and on the actor's slot open **All fields** → it contains U16.
8. SNAPSHOT GROUP (C# GroupMatch, UI only): **Snapshot** tab → **Capture Snapshot**. Mode: **Group (Multiple Values)**, pick that snapshot, row 1 NumericNoByte Between `-5` to `10` (or `-5` to `60000` in the no-write arm), row 2 NumericNoByte Exact `424242`, then **Find**. DumperTestActor matches. Discriminate on the representative's `(+N)`. PREDICT N from the snapshot SQLite (`%LOCALAPPDATA%\UE5CEDumper\Snapshots\snapshots.<hash>.db`, read-only): count the actor's NoByte-width leaves in range, excluding the slot-2 leaf. Post-fix N includes U16. Pre-fix N excludes every unsigned leaf in range (L17's method).
9. Restore ui-options.json. Kill the UI and the game.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| First Scan: {Total} candidates in {ms} ms (scanned {objects} objects, {classes} classes with matching fields) | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:1033-1035 | Value Search status row |
| Between range '<lo>'..'<hi>' covers no numeric width of NumericNoByte | dll/src/Fern.cpp:3248-3251 | Must NOT appear for these ranges (only for a range that misses every width) |
| Group Between range '<lo>'..'<hi>' covers no numeric width | dll/src/Fern.cpp:3626-3629 | Must NOT appear |
| Group First Scan: {Total} matching objects in {ms} ms (scanned … objects, … classes) | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:1463-1464 | Value Search status row, group mode |
| {Total:N0} object(s) matched{trunc}  ·  scanned {ScannedObjects:N0} | ui/UE5DumpUI/ViewModels/SnapshotViewModel.Group.cs:250 | Snapshot tab, Group mode status after Find |
| → -5~10 | ui/UE5DumpUI/Models/RoundModePreview.cs:105-118 (DllBoundStyles) | Between preview beside the bounds (sanity only) |

**Row text vs source:** The fixture's U16=54321 is not a 'small value' and I16=-12345 is not 'near 32767' (DumperTestActor.cpp:194-195). The row's literal ranges find nothing on the unmodified fixture. Use write_mem, or the equivalent defect ranges (-5..60000 / -20000..70000). The rest matches source: the four Fern sites call Radar::BuildNumericBetweenTargets (Fern.cpp:3247, 3401-3406, 3625, 3763-3768), and GroupMatch's Between arm uses BetweenOverlapsWidth.

**Rig:** No existing rig for A4-AB4-BETWEEN. Precedent: the L17 run (docs/todo.md L17), which drove begin_value_scan / begin_group_scan / query_group_slot_leaves on the same fixture. A new pipe rig: PipeClient; ad4_contested.find_live_actor; walk_instance for the offsets; mutate_guard.read_bytes/write_bytes for the literal arm; begin_value_scan + query_candidates(filter) + end_value_scan; begin_group_scan(per_slot_cap=4096) + query_group_slot_leaves + end_group_scan; restore and verify the bytes. Request shapes as in tools/verify/a11_container_anchor.py:121 and a12_group_anchor.py:128-133 (group slots accept only NumericNoByte/NumericAll). The snapshot half is computer-use plus a read-only sqlite3 count.

**Traps:** (1) Typed UInt16/Int16 scans do NOT go through BuildNumericBetweenTargets (only isMulti, Fern.cpp:3237-3252). A typed UInt16 `-5` is refused 'Invalid 'value' for data_type UInt16'. Use NumericNoByte as the row says. (2) game_only=false + Between around 0 easily exceeds max_results 50,000 and truncates silently into a false absence (L17's trap). Stay game_only=true on the actor, or raise Max and check_complete. (3) The per-slot leaf cap defaults to 256 (Constants.cs:298) and zero-heavy ranges can hit it; set 4096 and assert per_slot_cap_hit=false. (4) The snapshot group 'All fields'-style evidence is the (+N) count; predict it from the DB before reading the screen. (5) Restore the written bytes. (6) The UI holds 2 of 3 pipe slots. (7) AOT build only.

**Related rows:** L17, L16, L71, L60, L70

### L82 — `[A2-TOPTIONAL-VALUESCAN]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** no · **estimate:** 30 min

**Fix commit(s):** `19c5d065`, `96a6ad79`

**Fixture:** DumperTest58 (UE 5.8) Shipping: `py tools/verify/launch_dumpertest.py shipping58` (exe under D:\UE_Analyze_data\for testing\DumperTest58, launch_dumpertest.py:64,84-85). ADumperTest58Actor has `TOptional<FString> Opt_Str_Set` (= "Opt58StringPresent") and `Opt_Str_Unset` (never assigned) (DumperTest58Actor.h:87-89, .cpp:22). L12 measured on 5.8 that the string optional is INTRUSIVE: size 16 = inner 16, sentinel ArrayMax=0xFFFFFFFF. The CDO's Opt_Str_Set is also unset, since BeginPlay never runs on it, which gives a second discriminating instance. I relied on the DumperTest58 fixture, not docs/test-games.md. Steam 5.5+ titles there (TQ2 5.7, ES2 5.6, Lushfoil 5.6, Manor Lords 5.5, Solarpunk 5.7, Satisfactory 5.6, STVoyager 5.6) are unsurveyed for TOptional<FString>. The 5.4 DumperTest's Opt_Str_Unset is trailing-flag (size 24) and was already gated pre-fix, so it does not discriminate.

**Preconditions:** Nothing else injected (one game at a time). Inject by PID (`inject.py --pid $(cat out/host.pid)`); `--name DumperTest` is ambiguous. get_object_count ≈ 34.5k (L12: 34,554).

**Steps:**

1. Launch shipping58 and inject by PID. PipeClient: assert_build, ensure_scanned, get_object_count > 0.
2. `find_instances class_name=DumperTest58Actor exact_match=true limit=100` → live addr A (not Default__) and the CDO addr.
3. `walk_instance addr=A` → Opt_Str_Unset reads `(unset)`, size 16, hex ending FFFFFFFF (the intrusive sentinel). Opt_Str_Set reads Opt58StringPresent. This proves the field really is intrusive and unset, so the old reader would have produced "".
4. DISCRIMINATOR (pipe only): `begin_value_scan data_type=FString scan_type=Exact value="" game_only=false max_results=1000000`. check_complete, deadline_hit=false, total > 0: other empty FStrings DO match, so the absence is not vacuous.
5. `query_candidates session_id=… filter="Opt_Str"` (and a full page scan by field_name): NO candidate with field_name Opt_Str_Unset on A, and none named Opt_Str_Set on the CDO. end_value_scan.
6. SET control: `begin_value_scan FString Exact value="Opt58StringPresent" game_only=true` → exactly the live A's Opt_Str_Set (field_type StrProperty, the inner type), CDO absent. UI equivalent: Value Search, Type FString, Scan Exact, Value Opt58StringPresent, **First Scan** → one row DumperTest58Actor.Opt_Str_Set.
7. Kill the game (and the UI if it was used).

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Value is required. | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:970-974 | Value Search error row if "" is typed. The UI cannot send the row's discriminating needle |
| String scans take the user's needle verbatim. Empty needle is rejected at the C# layer; defensively accept here | dll/src/Fern.cpp:3219-3223 (comment) | Why the pipe accepts value="" |
| field_name "Opt_Str_Set", field_type "StrProperty" | dll/src/Aura.cpp:7268-7273 (V1c emits sf.typeName = f.innerType) | query_candidates rows for the SET control |
| First Scan: {Total} candidates in {ms} ms (scanned …) | ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs:1033-1035 | UI status for the SET control |

**Row text vs source:** 'Value Search, FString, Exact ""' cannot be entered in the UI: FirstScanAsync rejects IsNullOrWhiteSpace(Value) with 'Value is required.' (ValueSearchViewModel.cs:970-974). Only the pipe reaches it (Fern.cpp:3219-3223). The 'set optional holding text' half is UI-reachable. The row's 'a 5.5+ game ... if one can be found' is resolved by the DumperTest58 fixture built for it (DumperTest58Actor.h:14-17, README.md:583-584).

**Rig:** No existing rig (grep of tools/verify finds no Opt_Str/shipping58 driver besides launch_dumpertest.py). A new ~50-line pipe rig: launch/inject precondition check, find_instances, walk_instance (assert sentinel hex), begin_value_scan("") + check_complete + paged query_candidates absence + positive-count guard, begin_value_scan("Opt58StringPresent") presence, end_value_scan. Request shapes as in tools/verify/a11_container_anchor.py:121-131.

**Traps:** (1) The UI refuses an empty needle, so the discriminating half is pipe-only. (2) An absence claim needs check_complete: a capped or deadline scan can drop the row for the wrong reason (L17's trap). Use a large max_results with game_only=false, or game_only=true only if a positive "" hit exists in game classes. (3) 5.4 is not a discriminator here: pre-fix OptionalFlagOffset(24,16) already gated it. (4) Launch the 58 flavour: `shipping58`, not `shipping`, and taskkill the right image (FIXTURE_IMAGES, launch_dumpertest.py:113-116). The 58 Shipping build runs uncapped (launch_dumpertest.py:188-192). (5) Logs land in %LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest58-Win64-Shipping\ (value-scan OARR lines → offsets-0.log).

**Related rows:** L84, L86, L87, L12, L69

### L83 — `[W3-DEBUGCAM-QUEUED]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** yes · **needs UI:** yes · **estimate:** 50 min

**Fix commit(s):** `8a2b37e8`

**Fixture:** DumperTest **Development** launched with `--idle`: `py tools/verify/launch_dumpertest.py dev --idle` adds -DumperTestIdle, so alt-tabbing away guarantees the game thread stops ticking (tools/ue-sample/README.md:640-653, ShouldUseIdleMode needs !HasFocus). Dev is required because UCheatManager is live only in !UE_BUILD_SHIPPING (launch_dumpertest.py:94-96; the Shipping toggle returns -1, todo.md:5857). ToggleDebugCamera is UCheatManager's exec UFunction, so the Console strip appears after Load.

**Preconditions:** No persisted invoke-timeout override for the current dev exe (PE hash 6AAB48B710F29000). The hint cache holds 60000 ms and 5000 ms rows only for OLDER DumperTest.exe hashes (6A8AA8DF10F1F000, 6A7EA60310F17000), read 2026-09-22. The DLL's 5000 ms default (Stark.h:63) must stay below the CE record's 10000 ms mailbox wait (CeMailboxLayout.cs:145), or the Lua times out first with status 255 and unticks. Keep Foreground (Grausam) must be OFF during the stall (default Off, str.TP.FgHint en.axaml:1111). One CE with AOBMaker attached to DumperTest.exe. AOT dist (exe contains 'the toggle is QUEUED' and 'Do not tick again'; DLL contains 'toggle queued -- it will run').

**Steps:**

1. 1. `launch_dumpertest.py dev --idle` + `inject.py --name DumperTest`. Start one CE and attach. Start the UI and Connect.
2. 2. Console tab → 'Load' → the Debug Camera strip appears (HasDebugCameraToggle, ConsoleViewModel.cs:695-705). Press 'Copy CE Script': 'Debug Camera AA Script created in CE (tick = ON, untick = OFF).'
3. 3. CONTROL, no stall: Teleport → 'Keep Foreground' → 'Force ON' (en.axaml:1109-1112) keeps FApp::HasFocus true, so idle never engages. Console 'Force On' → '✓ Debug Camera forced ON.' with a green ON badge. 'Force Off' → '✓ Debug Camera forced OFF.' This also installs the PE hook. Then Keep Foreground → 'Force OFF' and confirm the badge reads Off.
4. 4. UI STALL: leave the UI in front, so the game is unfocused and idle. Press Console 'Force On' ONCE. After about 5 s the badge reads 'Queued' (amber #D7BA7D) and the status reads '⏳ Force ON: the toggle is QUEUED — …'. Do NOT press again. DLL logs: init-0.log WARN 'UE5_SetDebugCamera: ToggleDebugCamera ProcessEvent r=-5'; pipe-0.log 'set_debug_camera: enable=1' and ERROR 'GameThreadDispatch: invoke timeout (5000ms) inst=0x… func=0x…'.
5. 5. `py tools/verify/front_window.py front DumperTest` → the game ticks, the queued toggle drains, and the view becomes the free debug camera. Keep the game focused for at least 10 s. Then press ↻ in the UI (a memory read, so it works even though the click re-stalls the game): 'Debug Camera is ON.' and the badge reads ON. It stays ON; a second toggle would read OFF.
6. 6. Reset: Console 'Force Off' (it queues, because the UI click stalls the game), then front the game, then ↻ reads 'Debug Camera is OFF.'
7. 7. CE STALL (a SEPARATE stall, never together with step 4): with CE in front, tick the Debug Camera record. After about 5 s the dialog reads '[DebugCamera] ON queued -- the game thread is busy; the toggle will run when it is free. Do not tick again: a second toggle would undo it.' Press OK. After a second or more, `getAddressList()` shows the record still Active == true (no deferred untick). pipe-0.log: 'Mailbox: SET_DEBUG_CAMERA req=1 -> state=-5'.
8. 8. Front the game → camera ON; ↻ in the UI (or Teleport's Debug Camera ↻) reads ON.
9. 9. Cleanup: untick the record. Its [DISABLE] queues and only dbg()s '[DebugCamera] OFF queued -- the toggle will run when the game thread is free'. Front the game → OFF. Kill the game, CE and UI. Keep Foreground is not persisted, but confirm it is Off before the kill.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| Queued  (badge, color #D7BA7D) | ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs:726-729; TeleportViewModel.cs:1566-1568 | Console Debug Camera strip badge / Teleport Debug Camera card badge |
| ⏳ Force {want}: the toggle is QUEUED — the game thread is busy (stalled or unfocused). It will run when the game thread is free. Do not press Force {want} again: a second toggle would undo the first. | ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs:803-806 (Teleport twin :1626-1629); {want} = "ON" | Console status line |
| [DebugCamera] ON queued -- the game thread is busy; the toggle will run when it is free. Do not tick again: a second toggle would undo it. | ui/UE5DumpUI/Services/DebugCameraScriptGenerator.cs:101-104 (tested BEFORE `state ~= req`, no untick, no close) | CE showMessage dialog on the stalled tick |
| UE5_SetDebugCamera: ToggleDebugCamera ProcessEvent r=%d | dll/src/Frieren.cpp:1277 (LOG_CAT INIT) | %LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest\init-0.log [WARN] with r=-5 |
| GameThreadDispatch: invoke timeout (%dms) inst=0x%llX func=0x%llX | dll/src/Stark.cpp:429 (LOG_CAT PIPE) | Logs\DumperTest\pipe-0.log [ERROR] (5000ms) |
| Mailbox: SET_DEBUG_CAMERA req=%llu -> state=%d | dll/src/Mimic.cpp:1066-1067 | Logs\DumperTest\pipe-0.log (req=1 -> state=-5 on the CE path) |
| Debug Camera: toggle queued -- it will run when the game thread is free; do not re-send | dll/src/Mimic.cpp:1061-1063 | mailbox errorMsg (readable in CE memory; the record shows its own text) |
| set_debug_camera: enable=%d | dll/src/Fern.cpp:5748 | Logs\DumperTest\pipe-0.log (PIPE:cmd) |
| Debug Camera is ON. | ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs:748-751 | Console status after ↻ (memory read) |

**Row text vs source:** (a) The row quotes 'do not press Force ON again'. Source reads 'Do not press Force ON again: a second toggle would undo the first.' (capital D, ConsoleViewModel.cs:805-806). (b) The Console button is labelled 'Force On' (str.Con.DbgCam.ForceOn, en.axaml:978), while the status text and the Teleport card's buttons say 'Force ON' (en.axaml:1079). (c) The row's CE 'queued message' is concretely '[DebugCamera] ON queued -- … Do not tick again: a second toggle would undo it.' (DebugCameraScriptGenerator.cs:103-104). (d) The row names no control; with --idle the only non-stalled baseline is Keep Foreground ON. (e) The row's 'a game with ToggleDebugCamera' must be a NON-Shipping build with a live CheatManager (DumperTest dev); DumperTest Shipping answers -1.

**Rig:** NONE existing for the Debug Camera toggle (no tools/verify file references set_debug_camera/setDebugCamera other than generic capture rigs). Drive it with computer-use on the UI and CE plus `tools/verify/front_window.py front DumperTest` for the refocus. Every assertion is file-readable in the DLL logs (init-0.log / pipe-0.log under Logs\DumperTest\). A pipe_client witness of the stall (get_diagnostics) is impossible while the UI is connected, so use the pipe-0.log 'invoke timeout' line as the stall witness.

**Traps:** With --idle EVERY click on the UI or CE unfocuses the game and stalls it. The only non-stalled control is Keep Foreground ON (Grausam), which must be OFF again before the stall. NEVER queue the UI toggle and the CE toggle in the same stall: both drain on refocus, ON then OFF, which reproduces the bug manually. Keep the persisted invoke timeout < 10 s (see preconditions): L88 showed 60000 ms turns the CE path into status 255 and an untick. --idle breaks other rows (the D2 heartbeat) and times out every dispatch, so use a dedicated launch. Game windows steal focus: front explicitly and read back what is in front. Tick the CE record from the address list. Its queued branch neither closes the Lua Engine nor unticks, so its Active state is readable afterwards. One CE instance. AOT only.

**Related rows:** L77, L15, L35, L88, L42, L43

### L84 — `[A2-TOPTIONAL-STRUCT-DESCENT]`

**Status:** ⬜ · **reachability:** `live` · **needs CE:** no · **needs UI:** no · **estimate:** 40 min

**Fix commit(s):** `c6db7cf5`

**Fixture:** DumperTest58 (UE 5.8) Shipping (`launch_dumpertest.py shipping58`). `TOptional<FDumperTest58OptInner> Opt_Struct_Set`, whose inner is {TObjectPtr<UObject> Obj; TArray<TObjectPtr<UObject>> Objs; int32 Tag} (DumperTest58Actor.h:40-48,97-98), is seeded in BeginPlay with Tag=58001, Obj=this, Objs=[this] (.cpp:25-31). Opt_Struct_Unset is never assigned. The struct has no intrusive unset state (UE 5.8 PropertyStruct.cpp:423-430 returns CppStructOps->HasIntrusiveUnsetOptionalState()), so the optional is TrailingFlag and the fix descends it with a gate. The fixture has NO mutator that resets the struct optional (only Opt_SetObject/ResetObject/SetObjectNull/SetArray/ResetArray, h:109-129), so 'reset in game' = write_mem the trailing bIsSet byte to 00 (the L87 method: MarkUnset clears the flag and zeroes no value bytes).

**Preconditions:** One game at a time. Inject by PID. Record the original flag byte and restore it. No destructor runs on a flag-only write, so writing 01 back returns the exact original state.

**Steps:**

1. Launch shipping58, inject, ensure_scanned. `find_instances class_name=DumperTest58Actor exact_match=true` → live A.
2. `walk_instance addr=A` → Opt_Struct_Set offset O and size S (expect 40 = Align(32+1,8)); inner size 32 → flag at A+O+32. read_mem A+O+32 must be 01, and at Opt_Struct_Unset's +32 it must be 00. Read the Objs header at A+O+8: Data D, Num 1; read_mem D (8 bytes) must equal A.
3. SET, Find Refs: `find_refs_to_uobject addr=A max_results=64`. scan.objects_scanned == objects_total, deadline_hit=false. Expect owner=A hits with field_name `Opt_Struct_Set.Obj` (element_index -1) AND `Opt_Struct_Set.Objs` (element_index 0). Record every OTHER hit (e.g. UDumperTest58Subsystem.SpawnedActor, the Anchor's Owner) as the constant control set.
4. SET, Address Finder: `find_by_address addr=D scan_containers=true container_depth=5 container_elem_cap=256` (the exact UI request, DumpService.cs:845-859) → a container match owned by A on Opt_Struct_Set.Objs index 0.
5. RESET: write_mem A+O+32 = 00 (tools/verify/mutate_guard.write_bytes). Re-read: the 32 value bytes must be byte-identical to baseline, so only the flag moved.
6. Repeat Find Refs: both Opt_Struct_Set hits are GONE and the control hits are unchanged, with the scan complete. Repeat find_by_address D: no container match on Opt_Struct_Set; container_scan.deep_scan=true, and deadline_hit=false (the deep pass is gated too).
7. RESTORE: write_mem flag = 01 → Find Refs and find_by_address show the hits again (1→0→1-style bracketing, as in L12 step 4).
8. UI equivalent (optional): **Live Walker**, root on A, click **Find Refs** → header `References to <A name> (N)  [scanned X/Y in Zms]`. **Instances** tab, **Address** box = D, **Lookup** → the 'Container matches (address falls inside a TArray buffer):' list. Discovery aid: Property Search **Type filter** `OptionalProperty` with Game classes only lists the Opt_* rows.
9. Kill the game.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| FindReferencesToUObject: hit %s.%s (%s, owner=0x%llX, %s) | dll/src/Aura.cpp:3771-3775 (LOG_CAT OARR, Aura.cpp:8) | %LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest58-Win64-Shipping\offsets-0.log. Present for Opt_Struct_Set.Obj while SET, absent after the flag clear |
| FindReferencesToUObject: hit %s.%s[%d] (Array<%s>, owner=0x%llX, %s) | dll/src/Aura.cpp:3826-3830 | offsets-0.log, for Opt_Struct_Set.Objs[0] |
| References to {scanName} ({N})  [scanned {s}/{t} in {ms}ms] | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2867-2878 | Live Walker References header (UI arm) |
| FindReferences: {addr} -> {N} matches | ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2880 | UE5DumpUI view-0.log |
| Container matches (address falls inside a TArray buffer): | ui/UE5DumpUI/Resources/Strings/en.axaml:324 | Instances tab Address lookup result (UI arm) |
| [A2-TOPTIONAL-STRUCT-DESCENT] … Descend only into a TrailingFlag optional, carrying its flag to every entry below | dll/src/Aura.cpp:3453-3466 (CollectRefMetaRecursive) and the OptionalGateOpen checks at :2573/:2795/:2924/:8320 | Code that the reset case exercises |

**Row text vs source:** (a) 'Find Refs to that actor while the optional is SET: one hit'. The fixture's inner holds the actor in both Obj and Objs[0] (DumperTest58Actor.cpp:27-30), and Find Refs reports direct and array entries separately (Aura.cpp:3760-3830), so it is two optional-sourced hits. (b) 'Reset it in game' has no in-game mutator for the struct optional on this fixture; the trailing-flag write is the stand-in, justified as in L87. (c) 'if one can be found' is resolved by DumperTest58, built for this tag (DumperTest58Actor.h:20-21; README.md:585).

**Rig:** No existing rig for this tag. L12 step 4 (docs/todo.md:5569) drove find_refs_to_uobject on this fixture for Opt_Obj. A new ~70-line pipe rig: find_instances, walk_instance (derive O/S), read_mem via tools/verify/mutate_guard.read_bytes, find_refs_to_uobject ×3 (set/reset/restored) with completeness asserts and a constant-control-set diff, find_by_address ×3 with the UI's exact params, write_bytes for the flag, and a final restore-and-verify.

**Traps:** (1) Obj AND Objs both point at the actor, so expect TWO optional-sourced hits, not one. (2) Other referrers of the actor are always there: diff against them rather than expecting zero total. (3) Pre-fix behaviour is not re-run, since no old DLL is staged. The discriminator is the value bytes proven unchanged while the hits vanish. (4) The flag-only write is stricter than MarkUnset: the Objs buffer stays valid, so a pre-fix reader would certainly have attributed it. (5) Find Refs defaults to max_results 32 on the pipe (Fern.cpp:4526); pass 64 so controls are not crowded out. (6) Confirm the scan is complete (objects_scanned == objects_total) before an absence claim. (7) Restore the flag byte. (8) The UI holds 2 of 3 pipe slots.

**Related rows:** L82, L86, L12, L87, L22, L48

### L85 — `[W5-OFFSETS-MAILBOX]`

**Status:** 🟡 arms 3, 4 ✅ 2026-09-23 (`git log --grep 'verify(L85)'`); arms 1, 2 ⬜ (S9, S13) · **reachability:** `live` · **needs CE:** yes · **needs UI:** no · **estimate:** 60 min

**Fix commit(s):** `389d76bb`, `943975f3`, `e009ec78`

**Fixture:** (1) The 'validated=yes' arm: DumperTest Shipping, injected with dist\UE5Dumper.dll (build 3546). This was already OBSERVED as a side effect of L88 on 2026-09-16 (pid 28068): getOffsetsVerdict() returned true with an empty reason, and the DLL logged "Mailbox: OFFSETS_VERDICT -> measured=1 reason=''" (todo.md L88 row). Re-run it as its own record. (2) 'before any scan, in proxy mode' → probe-not-run: a proxy-launched hardlinked DumperTest. `py tools/verify/b5_mailbox_race.py stage` builds D:\ZZProxyB5\DumperTest (DEVELOPMENT) with a real copy of dist\proxy\version.dll. For Shipping, repeat that staging with SRC=Shipping, or reuse b29's D:\測試 tree plus version.dll (shared with L51). The proxy .def files export g_invokeMailbox as DATA (ProxyVersion.def:80), and g_mailboxContract/UE5_* are __declspec(dllexport) (Mimic.cpp:62, Frieren.h:34), so the helper's bare-symbol lookup (ue5_invoke_helper.lua:181-184, :159-160) resolves. (3) 'offsets fall back' with a real reason: no fixture here (see L69). Use L69's 'unmeasured' staged DLL. (4) The old-DLL arm: out/oldcontract/UE5Dumper.dll exists on this PC. It is build 1.0.0.3262 (embedded version string), a real shipped CONTRACT-2 binary copied from a DQ7R backup (docs/archive/todo-closed-2026-09-03-build-3369.md:945-958). Any contract below 5 lacks CMD 16, so it stands in for 'contract-4'.

**Preconditions:** Launch CE FRESH for each DLL generation. The helper defines its globals behind `if not getOffsetsVerdict then` (ue5_invoke_helper.lua:899), so a warm Lua state silently keeps older closures (L88 lesson). Load HEAD's helper with `dofile([[D:\Github\UE5CEDumper\scripts\ue5_invoke_helper.lua]])`, or through the UI 'Tools → Inject Helper into Current CE Table' (str.Tools.InjectCeHelperLua, en.axaml:821) plus a generated script's loader, as L88 did. The helper's contract check passes on both DLLs (UE5_SCRIPT_CONTRACT=1, ue5_invoke_helper.lua:153-177). Back up UE5CEDumper.%COMPUTERNAME%.json before running the build-3262 DLL, since it writes its own hint and logic-rev stamp.

**Steps:**

1. ARM 1: `launch_dumpertest.py shipping` + `inject.py --name DumperTest-Win64-Shipping`. Confirm init-0.log reads validated=yes. In CE, attach, load the helper and run `local ok,why=getOffsetsVerdict(); print(tostring(ok),'['..why..']')`. Expect 'true []'. pipe-0.log: "Mailbox: OFFSETS_VERDICT -> measured=1 reason=''".
2. ARM 2 (probe-not-run): `py tools/verify/b5_mailbox_race.py stage`, then launch D:\ZZProxyB5\DumperTest\DumperTest\Binaries\Win64\DumperTest.exe with the house args. init-0.log must show 'DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)'. In a fresh CE, attach, load the helper and call getOffsetsVerdict(). Expect 'false [probe-not-run]'. Afterwards there must be NO 'Mailbox: auto-initializing' and NO 'UE5_Init: Starting initialization...', because the command is init-exempt and skips the auto-init attempt (Mimic.cpp:327-341).
3. Then `py tools/verify/pipe_client.py trigger_scan` and poll get_pointers until the pool resolves. getOffsetsVerdict() now gives 'true []'. Finish with `b5_mailbox_race.py clean`.
4. ARM 3 (fallback reason, staged): inject L69's staged 'unmeasured' DLL into a fresh DumperTest Shipping. getOffsetsVerdict() gives 'false [unmeasured:elemsize]', which equals get_offsets' fallback_reason.
5. ARM 4 (old DLL): fresh DumperTest Shipping, `py tools/verify/inject.py --name DumperTest-Win64-Shipping --dll out/oldcontract/UE5Dumper.dll`. Wait for READY. init-0.log's 'Logger started | build: 1.0.0.3262' proves which DLL answered. In a fresh CE with HEAD's helper, getOffsetsVerdict() gives 'false [dll-too-old]', never true. If the old DLL's init had FAILED, the answer is 'false [dll-not-initialised]' (-10), also acceptable as 'never measured'.
6. Kill the game and CE after each arm. Restore the JSON after arm 4, and dist after arm 3.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| getOffsetsVerdict() → true, '' | scripts/ue5_invoke_helper.lua:901-910 (returns code == 1, readString(mb+OFF_PARAMS)) | CE Lua Engine (capture via io.open) |
| Mailbox: OFFSETS_VERDICT -> measured=1 reason='' | dll/src/Mimic.cpp:1082 | pipe-0.log (Mimic LOG_CAT 'PIPE') |
| getOffsetsVerdict() → false, 'probe-not-run' | dll/src/Grimoire.h:775 via Mimic.cpp:1077-1083 | CE Lua Engine (proxy mode, pre-scan) |
| DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan) | dll/src/Heiter.cpp (ProxyStart; observed verbatim in ES2 init-20260912-101207.log) | init-0.log. Anti-vacuity that arm 2 really had no scan. |
| getOffsetsVerdict() → false, 'dll-too-old' | scripts/ue5_invoke_helper.lua:908 (code < 0); pre-5 DLL answers SetError(-1, "Unknown command") (git show 389d76bb^:dll/src/Mimic.cpp:408-409) | CE Lua Engine (old DLL, initialised) |
| getOffsetsVerdict() → false, 'dll-not-initialised' | scripts/ue5_invoke_helper.lua:907 (code == -10); pre-5 DLL's init gate SetError(-10, "DLL not initialized") (389d76bb^ Mimic.cpp:333-340) | CE Lua Engine (old DLL whose init failed). Acceptable, but NOT the row's wording. |

**Row text vs source:** (a) 'Against a contract-4 DLL the same call says dll-too-old' is incomplete since e009ec78 [A1-VERDICT-STALEMB]. A pre-5 DLL that is not initialised answers -10 first, which the helper reports as 'dll-not-initialised' (ue5_invoke_helper.lua:895-907), not 'dll-too-old'. Still 'never measured', so the row's safety claim holds. (b) The only old binary on disk is contract 2, not 4 (build 3262). The behaviour is identical for CMD 16. (c) 'a game whose scan log says validated=yes': the validated= summary is in init-0.log (SUMMARY) and offsets-0.log (DYNO), not scan-0.log. (d) 'on a game whose offsets fall back' has no live fixture at HEAD (see L69); the row's parenthetical proxy route covers only the probe-not-run arm. (e) Arm 1 was already observed during L88; this row should cite that and re-record it as its own check.

**Rig:** No rig exists. The CE side is Lua typed in the Lua Engine (handover §6). Staging reuses tools/verify/b5_mailbox_race.py stage/clean (proxy) and inject.py --dll (old DLL: the archive's recipe, 'copy any *.20260819-*.bak to UE5Dumper.dll and inject.py --dll it'). A CE-free DLL-side cross-check for arms 1-3: import mailbox_poke and poke(cmd=16). The CLI's READY gate blocks it in proxy pre-scan (initState is not READY there), so use the library call.

**Traps:** A warm CE Lua state keeps old helper closures (ue5_invoke_helper.lua:854/:899 guards): restart CE. The helper version string cannot tell builds apart (L88). In proxy mode the module is VERSION.dll, so tools keyed on the module name 'UE5Dumper.dll' (call_export.py) fail. The b5 staging is DEVELOPMENT: for a Shipping proxy, stage from the Shipping package, and say which flavour ran. Deployed Steam proxies are usually STALE (handover §3, proxy_refresh.py report); the hardlinked DumperTest proxy is copied fresh from dist. The build-3262 DLL is old code: expect older-bug behaviour elsewhere, one game at a time, and kill it right after. It also re-stamps the hint cache, so back up and restore the JSON. The helper's 10 s mailbox timeout latches on any waitDone failure (ue5_invoke_helper.lua:827-834), so never query between a CE Disable and re-Enable (the poller is joined). The UI's two lanes plus a pipe rig use all 3 pipe instances (Fern.h:51).

**Related rows:** L69, L54, L88, L51

### L86 — `[A2-SENTINEL-OVERREAD]`

**Status:** ⬜ · **reachability:** `fixture-limited` · **needs CE:** no · **needs UI:** no · **estimate:** 20 min

**Fix commit(s):** `96a6ad79`

**Fixture:** The discriminating shape: a 5.5+ UObject class whose LAST reflected field is an intrusive TOptional<FName> (8 bytes, 12 under CPN). Enough instances that some allocation ends within 16 bytes of an unmapped page. The pre-fix 16-byte read (Aura.cpp:8058-8069 now reads min(SentinelBytesNeeded=4, sf.size)) then falls outside the prefetched body buffer to Macht::ReadBytesSafe (Aura.cpp:7652-7660) and faults. DumperTest58 has TOptional<FName> Opt_Name_Set ("Opt58NamePresent") and Opt_Name_Unset (h:93-94), but they are NOT last (Opt_Struct_*, Opt_Obj, Anchor and FrameCountReflected follow, h:97-109) and there is ONE live instance, so the page-edge case cannot occur there. No surveyed Steam title in docs/test-games.md is known to carry the shape. Building it needs a fixture change: a small UObject subclass with TOptional<FName> as its last UPROPERTY plus a spawner.

**Preconditions:** DumperTest58 Shipping for the regression check. One game at a time; inject by PID.

**Steps:**

1. REGRESSION (no live trigger for the page-edge half): launch shipping58, inject, `find_instances DumperTest58Actor exact_match=true` → live A and the CDO.
2. `begin_value_scan data_type=FName scan_type=Exact value="Opt58NamePresent" game_only=true`. check_complete → exactly one candidate: A's Opt_Name_Set (field_type NameProperty). The CDO's Opt_Name_Set (unset, ComparisonIndex sentinel ~0u) is absent, and Opt_Name_Unset is absent.
3. `refine_value_scan … scan_type=Unchanged` → the row is kept. The refine gate uses SentinelBytesNeeded too (Aura.cpp:8587-8593).
4. INSTRUMENT (read-only), to show honestly why this is not the discriminator: compute end = A + O(Opt_Name_Set) + 16, and with ctypes VirtualQueryEx on the game PID check whether [A+O, end) crosses into a non-committed page. Expect NOT, which records that the pre-fix over-read would also have succeeded here.
5. If a qualifying title/class is ever found: FName Exact its held name over ALL instances, and assert every live instance is a hit, including the subset the instrument flags as within 16 bytes of an unmapped page.
6. Kill the game.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| int32_t need = Ubel::SentinelBytesNeeded(sf.optionalSentinel); if (sf.size > 0 && sf.size < need) need = sf.size; | dll/src/Aura.cpp:8064-8065 | First-scan gate (code), the fix under test |
| const int32_t need = Ubel::SentinelBytesNeeded(sen);   // never a flat 16 [A2-SENTINEL-OVERREAD] | dll/src/Aura.cpp:8589 | Refine gate (code) |
| field_name "Opt_Name_Set" on DumperTest58Actor (live only) | tools/ue-sample/DumperTest58/Source/DumperTest58/DumperTest58Actor.cpp:24 | query_candidates result of the regression scan |

**Row text vs source:** None in the code: the fix is as described (SentinelBytesNeeded 16/4/8/0, Ubel.h; gate capped by sf.size). The row's own caveat stands: 'the offline pins may be the whole story'. The only 5.5+ fixture with an intrusive TOptional<FName> has it mid-object with one instance, so the page-edge condition is unreachable without a new fixture.

**Rig:** None exists. Regression: a ~40-line pipe rig (begin_value_scan / query_candidates / refine_value_scan / end_value_scan) plus a ctypes VirtualQueryEx page-edge probe (pattern: tools/verify/freezescope_step5_pawn.py's OpenProcess/ReadProcessMemory setup). The offline pins are the dll_helpers_test V1C block (SentinelBytesNeeded 16/4/8/0) and the InvokeScriptTests source pin.

**Traps:** (1) A hit on DumperTest58 proves only that the reduced 4-byte read still gates correctly (set found, unset/CDO excluded). It cannot show the page-edge miss, so do not record it as the row's discriminator. (2) The value scan includes CDOs; the CDO's unset Opt_Name_Set is a free negative control. (3) FName compare may be case-sensitive per request; the held name is exact. (4) Absence claims need check_complete.

**Related rows:** L82, L84, L87, L12

### L89 — `[A1-REVIEW6-PINS]`

**Status:** ✅ CLOSED 2026-09-22: C# half `b87915ea`, C++ half `git log --grep 'verify(L89)'` (Pass 2752 / Fail 0) · **reachability:** `no-live-trigger` · **needs CE:** no · **needs UI:** no · **estimate:** 10 min

**Fix commit(s):** `943975f3 fix(records): the init-gate comments name both exemptions, and the %ls gate counts what it scanned [A1-REVIEW6-PINS] (batch L49; Mimic.cpp:327 comment, dll_helpers_test.cpp:1303-1311, InvokeScriptTests.cs:1997-2009 new pin + :2090-2137 guard-the-guard)`, `b87915ea verify(L89): CLOSED 2026-09-22 as N/A; C# pins re-run 216/216 (docs only)`

**Fixture:** None. Offline source pins only: the UE5DumpUI.Tests project and the dll_helpers_test executable. No game, CE or UI.

**Preconditions:** Tree at HEAD. build/ must already be configured by build.ps1 (tools/verify/build_dll.py refuses to configure). Commit before executing any newly built exe (Bitdefender, auto-memory machine note).

**Steps:**

1. 1. C# half: `dotnet test ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj -c Release -- --filter-class "UE5DumpUI.Tests.InvokeScriptTests"` (handover-2026-08-22.md:387-393; do NOT pass --nologo or -v). EXPECT 0 failures, including InitGateComments_NameBothExemptions (InvokeScriptTests.cs:1997-2009) and DllLogCalls_NeverFormatAWideString (:2090), whose guards are scannedFiles >= 60 (:2133) and logCallLines >= 100 (:2135). Take the pass count from the runner output; do not reuse the row's 216.
2. 2. C++ half, without touching dist\: `py tools/verify/build_dll.py --targets dll_helpers_test` (it builds any ninja target, :182/:212, and per its docstring :24-25 'Deliberately does NOT touch dist/ or bump build_number.txt'). Then run build\dll\dll_helpers_test.exe (the binary exists today). EXPECT no '  FAIL: exactly two commands are init-exempt' line (EXPECT at dll_helpers_test.cpp:1311; FAIL format :66), a final 'Pass: N   Fail: 0' (:8655), and exit code 0 (:8656).
3. 3. Source sanity (grep): dll/src/Mimic.cpp:327 contains 'CMD_FOREGROUND and CMD_OFFSETS_VERDICT' and no 'today only CMD_FOREGROUND'; dll/tests/dll_helpers_test.cpp:1303 contains 'Exactly TWO exemptions' and no 'Exactly ONE exemption'.

**Expected strings:**

| text | source | where it appears |
|---|---|---|
| CMD_FOREGROUND and CMD_OFFSETS_VERDICT | dll/src/Mimic.cpp:327; asserted at ui/UE5DumpUI.Tests/InvokeScriptTests.cs:2004 | Source comment at the Mimic init gate |
| Exactly TWO exemptions | dll/tests/dll_helpers_test.cpp:1303; asserted by InvokeScriptTests.InitGateComments_NameBothExemptions | Test source comment |
| exactly two commands are init-exempt | dll/tests/dll_helpers_test.cpp:1311 (EXPECT label; it prints only on failure as '  FAIL: …' per :66) | dll_helpers_test.exe stdout. Must be ABSENT. |
| Pass: %d   Fail: %d | dll/tests/dll_helpers_test.cpp:8655 | dll_helpers_test.exe final stdout line; Fail must be 0 |
| only {scannedFiles} dll/src file(s) scanned -- the enumeration has stopped finding the sources | ui/UE5DumpUI.Tests/InvokeScriptTests.cs:2133-2134 | Test failure message. Must not appear. |

**Row text vs source:** FINDING in the CLOSED text: 'dll_helpers_test's EXPECT("exactly two commands are init-exempt", …) was deliberately NOT re-run by hand: the only route is build.ps1 -Target Test, which republishes dist\ non-trimmed.' That is not the only route. tools/verify/build_dll.py --targets dll_helpers_test builds that ninja target in the build.ps1-configured tree without touching dist\ or build_number.txt (build_dll.py:24-25, :182, :212), and build\dll\dll_helpers_test.exe already exists. So the C++ half of the pin can be re-run at HEAD without the non-trimmed republish. The pin line numbers quoted in the row (InvokeScriptTests.cs:2004 and :2090) match HEAD.

**Rig:** No new rig needed. Existing tools: `dotnet test … -- --filter-class` for the C# pins, and tools/verify/build_dll.py plus the built build\dll\dll_helpers_test.exe for the C++ EXPECT.

**Traps:** Never use build.ps1 -Target Test for this: it overwrites dist\UE5DumpUI.exe with the ~107 MB non-trimmed exe (CLAUDE.md Build & Deploy). If build_dll.py reports '#deps 0' objects it hard-fails by design; re-configure through build.ps1, never bare cmake. `dotnet test` with --nologo or -v prints help and exits 5 'Zero tests ran', which looks like a broken project. Running a freshly built exe on this machine: commit first (Bitdefender). There is nothing to observe on a running game, so do not spend a game session on this row.

**Related rows:** L85, L69, L88

