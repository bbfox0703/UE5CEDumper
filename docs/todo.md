# Todo

Open work only. **Read this when deciding what to do next.**

> 🤝 **Coming back after the 2026-08-19 fix programme? Read
> [handover-2026-08-19.md](handover-2026-08-19.md) first.** 32 commits took the audit register
> **166 → 4 open of 297** and the OPEN FIXES INDEX's original twelve **→ 1**, and **none of it has
> been verified on a running game**. That doc carries the three-phase verification schedule (two
> phases need nothing from you), the four findings left open on purpose, and the two-machine sync
> point (`dist` **1.0.0.3263**; 3262 was deliberately skipped).

> ## ▶ If the ask is "carry on fixing bugs", do NOT start here
>
> The bug queue is **not** in this file. It lives in
> [audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md) →
> **§3b "▶ THE NEXT FIX SESSION STARTS HERE"**, which carries an ordered, already-vetted list of
> the next six fix groups (① – ⑥) with file:line and the reason each group is one job. Start at ①;
> no re-derivation is needed to begin.
>
> **What IS in this file, and is not in that one:**
> - `## Pending live-game verification` — **40 open batches** needing a running game (this is a
>   DERIVED count and it had drifted to a stale 43, then to a stale 36; re-derive, never hand-adjust:
>   `awk '/^## Pending live-game verification/,0' docs/todo.md | awk '/^## /&&!/^## Pending live-game/{exit}1' | grep '^### ' | grep -c ⬜`).
>   **Offer these
>   whenever the maintainer has a game up.** The newest (2026-08-19) is the audit L9 (T1c
>   VMs/Core/DTOs) AE13/AE20/AE30 batch — fourteen of its seventeen findings need nothing live, two
>   of them because they were **already fixed** by an earlier batch and were closed by reading the
>   code. AE13's row is **DLL-gated**. Before it, the audit L8 (U5 VMs +
>   scoring) Z8/Z12/Z13 batch — ten of its thirteen findings need nothing live; **two of the three
>   that do are DLL-gated**, so a stale injected DLL makes them look like no-ops rather than
>   failures, and the third is the batch's one deliberate scoring change. Before it, the audit L7
>   (T1d UI Services) AC3/AC6/AC10/AC11/AC12 batch — five of its ten findings need nothing live, and the
>   rows that remain each need a thing no test has: a real CE, a real Steam `libraryfolders.vdf`, a
>   game killed mid-write, and a real game's Binaries folder. Before it, the audit L4 (D4b
>   Mimic/Sein/Flamme) MB1/MB2/SE1/FL1/FL2 batch — its three pure rules are unit-pinned with five
>   negative controls, so the live rows are the parts no test target can reach (nothing compiles
>   `Mimic.cpp` / `Sein.cpp` / `Flamme.cpp`): the CE re-FIRE routing WARN, Keep-Foreground on a
>   scan-failed game, a log category that cannot open, and the hint-cache staging sweep. **SE2 is
>   deliberately absent** — its trigger is not reproducible on demand. Before it the audit L3 (T1b)
>   AD10/AD12/AD13/AD15/AD16/AD18 batch, unusually **cheap and low-yield on purpose**:
>   almost all of L3 is machine-enforced offline (a compile-time `static_assert` plus
>   `extract_patterns.py --check` now pin every AOB entry's resolve geometry), so the four live rows
>   are a UE 4.27 log grep and a four-proxy launch regression check. Before it the audit L5 (S1 Lua)
>   AA26/AA31/AA32/AA37 confirmation batch — most of L5 is rig-covered offline
>   (`scripts/tests/{dissect,freeze_helper,invoke_helper}_test.lua`, all green), and only four have a
>   real-DLL/CE face the rig stubs: **AA37** `createFromPath('/Script/CoreUObject.Vector')` builds a
>   clean 3-field dissect with NO UObject header (needs the DLL's real meta-class name); **AA26** a
>   packed bitfield bool renders as a single bit in CE's Structure Dissect; **AA31** the Debug Camera
>   sample unticks a real CE record when the toggle errors; **AA32** a repeated string-param invoke
>   does not crash (the reclaim-on-next-invoke retention assumption). Before it the audit L1 (D1/D2/D3
>   DLL engine) U11/G6/G7/A7/A8/A9 batch; before it the audit L2 (T1a Radar)
>   AB12/AB13/AB14/AB16/AB17 end-to-end batch, then the CLASSTOTAL / PIPEBUSY honesty fixes and
>   the SLOTSYM / STALEDLL(b) generator + `.CT` fixes; the five before them are
>   2026-08-17's, and NONE has been
>   seen on a real target; two of those older ones need less than a full session:
>   **AA4–AA7 step 2 needs no DLL at all** (enable the dissect auto-callback with the DLL absent and
>   confirm CE still dissects an ordinary address), and **all six AE4–AE7 steps need no game** —
>   just the Proxy Deploy panel.
> - Everything below that is ordinary feature/infra work, unrelated to the audit.
>
> State as of 2026-08-19: **4 audit findings open of 297 · 0 HIGH · 0 MED · 3 LOW · 1 INFO**
> (audit **L12** closed the whole INFO tier bar one: 25 of 26 rows, leaving **AB23** open by choice —
> its `GroupSlotMatch::ownerClass` interning lives in `Aura.cpp`/`Fern.cpp`, which **no test target
> compiles**, so it is in-game-only work; the memory *accounting* it exposed is fixed. The three LOWs
> — AB9, A10, AA39 — remain open on purpose, see their rows.)
> (⚠ this line read **98 / 72 LOW** before audit L8 while the CI-gated headline in the audit doc's
> §3b read **88 / 62** — it had drifted by 10 and is now set from the gate's own output rather
> than by subtracting a delta. Only §3b is CI-enforced; **re-derive, never hand-tally**.)
> (audit L8 (U5 VMs + scoring) closed Z4-Z16 — **Z14 as "the comment was wrong"** with no score change,
> **Z16 as already-fixed** by `dcafa5fe` (confirmed by grep, not assumed), and **Z13 as the one
> deliberate score movement**, stated in full on its row; Z11's prescribed fix was **refuted** — the
> `resolved` field it names carries no information and the two zero cases are separated by `Code`;
> Z10's preferred half — adding a Max control — is **deferred, not closed**, see the index below)
> (audit L4 (D4b) closed MB1/MB2/SE1/SE2/FL1/FL2 — MB1 fixed with **no mailbox-contract move**, by
> removing a read of an OUTPUT field rather than promoting it to an input; MB2's second half and
> SE1's stated `written = 0` cause were both re-derived and **refuted**, see their rows;
> audit L5 (S1 Lua) closed AA11/AA21/AA22/AA23/AA24/AA26/AA27/AA28/AA29/AA30/AA31/AA32/AA33/AA34/AA37
> — all rig-covered offline; audit L1 closed U9/U10/U11/G4/G5/G6/G7/A7/A8/A9; A10 left open — needs
> the U5 by-value restructuring; audit L3 (T1b) closed AD7–AD22, i.e. the whole DLL-contract-header
> and Himmel block, leaving **AA39** open on purpose — see its row for why the prescribed fix is a
> measured no-op). Nothing is blocked on a maintainer decision. Re-derive with
> `py tools/check_audit_register.py --list` — never hand-tally.
>
> ### ▶ OPEN FIXES INDEX — 6 items, and they are NOT in the count above
> **Read the split before quoting a number.** Of the **twelve** field-found defects this index
> carried on 2026-08-18, **eleven are fixed** and exactly one survives: `[STALEDLL]`(a), which is a
> maintainer-only file deletion. The other five rows are **new** — three surfaced by the audit
> programme itself on 2026-08-19, two (`[PROXYDEPS]`, `[RELAUNCHPIPE]`) by the overnight
> verification run — and were deliberately deferred — none is a regression, and each states its own
> reason for waiting. So "12 → 1" is the honest headline for the original queue, and **6** is the
> honest row count of this table — the fifth and sixth, `[PROXYDEPS]` and `[RELAUNCHPIPE]`, were
> both added 2026-08-19 by the overnight verification run, in passing rather than by a scan.
> ⚠ `check_audit_register.py` reads **only** audit #5's table, so these are counted nowhere and are
> invisible to the gate. They carry **no severity tier** — the audits assigned those, these were
> found in the field. **Grep the tag** (stable; line numbers drift). Audits #3 and #4 are fully
> closed — #4's ten unmarked rows are its *refuted, do-not-re-raise* table, not open work.
>
> | tag | one-line defect |
> |---|---|
> | `[STALEDLL-2026-08-18]` | a 6-month-old `UE5Dumper.dll` in CE's install folder that the `.CT` will pick up — **(b) DONE: the `.CT` now reports the resolved DLL's size beside its path; (a) delete/refresh the stale file is maintainer-only** |
> | `[PROPSEARCHCAP-2026-08-19]` | **Property Search has no Max control and its cap is the compile-time default 200** — very low for a query like `Health` on a real game. Audit #5 **Z10** is ✅ because the half that was a *defect* is fixed (the status line no longer advises "raise Max" on a panel with no Max), but the finding's own preferred repair — add the lever, as Instance Finder already has (`InstanceSearchCap` NumericUpDown, clamped 100..50000) — was **deliberately deferred**: it is a feature on an AXAML toolbar that cannot be visually verified in an unattended session, not an honesty fix. `SearchPropertiesAsync` already takes `limit`, so the work is a VM property + a NumericUpDown + passing it through; the status line's cap sentence already names the applied cap, so it needs no change. |
> | `[VOLUMEROOT-2026-08-19]` | Three sites ask `Path.GetPathRoot` + `DriveInfo` about a path and therefore answer about the **host** volume, not the volume the path actually lives on when that path is a mount point: `WindowsPlatformService.GetFreeDiskSpaceBytes:407`, `GetTotalDiskSpaceBytes:419` (these feed the snapshot disk-space guard, so a game or snapshot folder on a mounted volume is measured against the wrong disk) and `WindowsLogCompressionService.IsSupported:71` (NTFS test; logs are under `%LOCALAPPDATA%`, so this one is near-theoretical). Audit #5 **AC17** fixed exactly this shape in `VolumeHasRecycleBin` using `GetVolumePathNameW` + the new `GetDriveTypeW` import, so the pattern to copy already exists in the same file; the disk-space pair additionally needs `GetDiskFreeSpaceExW` rather than `DriveInfo`. Found by the AC17 sibling-grep, left alone because it is a different feature and only a real mount point can verify it. |
> | `[PROXYDEPS-2026-08-19]` | **Six proxy objects carry NO recorded header dependencies, so a `.h` edit may not rebuild the four shipped proxy DLLs.** `ninja -C build -t deps` reports `#deps 0` for `Lugner.cpp.obj` / `Lugner_Dinput8.cpp.obj` under `UE5Dumper_Proxy`, `…_ProxyDinput8`, `…_ProxyDxgi`, `…_ProxyWinmm` (the two `.asm.obj` thunks also show 0, which CLAUDE.md says is legitimate — they are **not** the concern). CLAUDE.md's Build section calls a **C++** object at `#deps 0` exactly the broken-`msvc_deps_prefix` state in which "a `.h` edit silently stops triggering a rebuild" — i.e. a proxy could ship built against stale headers. ⚠ **But the simple explanation is already refuted**: all **17** objects in `UE5DumperCommon` record their deps correctly (`Genau.cpp.obj`, `Macht.cpp.obj` … all list `Macht.h`), so this is confined to the proxy targets rather than being a whole-tree console-code-page mismatch. **Do not "fix" it by re-configuring until that is explained.** The real predicate is empirical, not the deps listing: touch a header `Lugner.cpp` includes, rebuild, see whether those objects recompile. Found in passing 2026-08-19 while adding a header-dep guard to `tools/verify/build_dll.py` (which now WARNs on these rather than failing, since they predate the work and would block every build). |
> | `[RELAUNCHPIPE-2026-08-19]` | ⭐ **A game that RELAUNCHES ITSELF ends up with our DLL mapped and NO pipe server at all** — the proxy is loaded, the game runs fine, and nothing can ever connect. `UE5_StartPipeServer` (`Frieren.cpp:2220`) guards with a **one-shot** `CreateFileW(PIPE_NAME, OPEN_EXISTING)`; if that succeeds it logs `pipe already exists (another instance running) — skipping` and never starts a server. On a self-relaunching title that is a TOCTOU race against the **dying** first process. **Measured on OCTOPATH TRAVELER, reproduced 3 runs out of 3**, whole sequence in the logs: PID 28188 `pipe server started` → PID 65684 (3 s later) `pipe already exists … skipping` → 28188 `PipeServer: Stop entry (process exit — …)` → the survivor sat serverless through 140 s of polling. **Proven by repair, not just by reading**: calling `UE5_StartPipeServer` in the survivor via `tools/verify/call_export.py` logged `PipeServer: Started on the pipe (maxInstances=3)` and the sweep then completed normally (UE 4.18, 273,957 objects, 699 classes). ⚠ **Two things make this nearly invisible.** Each process start **rotates `init-0.log`**, so the two instances write to *different files* and the survivor reads as a single process contradicting itself. And `UE5_StartPipeServer` **returns `true` on the skip path too** — deliberately, "so CE Lua doesn't treat it as failure" — so no caller can distinguish "started" from "declined". Fix shape: have the guard confirm the holder is still alive and/or retry once the pipe frees, and give the skip path a distinguishable return. ⚠ Note `docs/test-games.md` records OCTOPATH as winmm-proxy LIVE-VERIFIED on 2026-08-18, so whatever launch route was used then must sequence differently — the title is not broken, the startup race is. |
> | `[SCANIDENTITY-2026-08-19]` | Value-scan candidates are re-read across refines by raw address with no re-validation of the owning object's identity (audit #5 AB7, now ✅ as docs-only). The refused `SerialNumber` witness is wrong for a passive observer and §4.3's "witness input bytes" does not apply (the value is expected to change). The only real check is re-reading the UObject class pointer to catch a slot recycled by a *different* class — a behaviour-changing feature with an open product question (AA2: class-wide targeting can be by design) and no unit-test seam. Deferred; needs a maintainer decision + live game with mid-scan object churn. |
>
> *`[AXAMLGATE-2026-08-19]` was **fixed 2026-08-19** by `a1bdd205` and its row is **deleted** — the
> gate is green again (`py tools/check_axaml_strings.py` → exit 0, 1316 keys defined / 1316
> referenced). Note the correction in that commit: the row above called this pre-existing and a false
> positive, and it was **neither**. The keys did not exist before 2026-08-19 (`git show
> 25af33fd:…/en.axaml | grep -c ValuePrompt` = 0), and the checker was correctly reporting that a
> `StaticResource` key had become invisible to static inspection — the exact property it defends. The
> fix was to make the four call sites select a static key, not to teach the checker the
> interpolation.*
>
> *`[CONTAINERCAP-2026-08-18]` was **fixed 2026-08-19** (client-only badge + status line) and moved to
> `## Pending live-game verification`.*
>
> *`[CLASSTOTAL-2026-08-18]` and `[PIPEBUSY-2026-08-18]` were **fixed 2026-08-19** and moved to
> `## Pending live-game verification`.*
>
> *`[SLOTSYM-2026-08-18]` was **fixed 2026-08-19** and moved to `## Pending live-game verification`.*
>
> *`[AUTOREFRESH-2026-08-19]` was reported from the field and **fixed the same day**; it went straight
> into `## Pending live-game verification`. It never sat in this index. Its sibling finding — that
> Property Search's Preview is a per-search snapshot and the A5 step wrongly implied it self-updates —
> was a **doc defect, not a code defect**, and is corrected in the A5 step itself.*
>
> *`[PROXYLOAD-2026-08-17]` was **part-fixed 2026-08-19** (offline screening + a real load signal —
> both offline halves) and moved to `## Pending live-game verification`.*
>
> *The seventh row — the untagged "SDK header does not compile" — was **fixed 2026-08-19** and has
> moved into the register below as `[SDKHDR-2026-08-18]`, where it is now grep-able like the rest.*

> **2026-06-06 cleanup.** This file was slimmed to open items only. The full
> pre-cleanup history (every shipped build's effort/risk retrospective, files
> touched, test counts, decision rationale) is frozen in
> [archive/todo-completed-build-937.md](archive/todo-completed-build-937.md).
> The running milestone log is [dev-log.md](dev-log.md).
>
> **Conventions:** each item is **flat and self-describing** — the title decodes
> its session shorthand (e.g. "V3-C", "#5 v2"), and a trailing *parent* line gives
> the one-line context (which already-shipped work it follows + the dev-log build).
> **Effort** S/M/L/XL (S=hours · M=1 session · L=multi-session · XL=weeks).
> **Risk** low/med/high (chance of breaking existing behaviour / perf regression).
> When an item ships: write it up in [dev-log.md](dev-log.md), update
> [roadmap.md](roadmap.md) if capability changed, then **delete it here** (don't
> strike-through — the archive holds the history).

-----

## Closed work is not here

Three sections used to sit at the top of this file — the Ghidra-free sweep (build 2545), the
2026-07-29 corpus state, and the PE build-identity investigation. All three were **finished**, and
this file's own rule at the top says finished work gets deleted here and written up in
[dev-log.md](dev-log.md). They were also near-verbatim copies of it, which is the failure mode that
matters: two copies agree until one drifts, and one of them had already drifted (it argued from
"five CI gates" after an eighth landed).

Where they live now:
- **Ghidra out of the sweep / `pe_sweep.py` acceptance** — [dev-log.md](dev-log.md), build 2545
  (the 138 s vs 773 s replay, 210/210 byte-identical, 162 ✅ / 59 ⚠ / 2 ❌, 70/70 EXACT).
- **Corpus state + the never-drop set** — [corpus-preservation.md](corpus-preservation.md), which is
  the authority; the copy here was a snapshot of it.
- **PE build-identity (`/Brepro`, `duplicate_copies`)** — [dev-log.md](dev-log.md), and the standing
  rule about `IMAGE_DEBUG_TYPE_REPRO` is stated with it.
-----

## UE5 non-Shipping: GNames reaches nothing — decide whether to mine a pattern

*Parent: the 2026-07-29 PDB+replay pass. Full evidence in
[GROUND-TRUTH.md](../tools/ghidra/GROUND-TRUTH.md) §Still open.* **Effort S–M · Risk med.**

**On a non-Shipping UE5 build, GNames survives on ONE pattern and costs ~2,300 wasted validations
to get there.** Sweep-verified 2026-07-29: it lands on **`GNAM_V1`** (priority 870, 4 literal
bytes) after **2,199 / 2,369 / 2,372 / 2,424** rejected candidates on 5.7.4-DbgG / 5.8.0-DbgG /
5.8.1-Dev / Titan — **the four most expensive fall-throughs in the corpus**, next worst 475. It is
**config, not a version regression**; every Shipping build resolves normally, so **no shipped game
is affected**:

| | 4.10.4 | 4.15.3 | 4.23.1 | 4.27.2 | **5.3** | **5.4.4** | 5.7.4 | 5.8.0 | 5.8.1 |
|---|---|---|---|---|---|---|---|---|---|
| Shipping | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ n=11 | ✅ n=11 |
| Development / DebugGame | ✅ | ✅ (1w) | ✅ n=16 | ✅ n=16 | ✅ **15/15, 0w** | ⚠️ **1/6, 2240w** | ⚠️ 1/8 | ⚠️ | ⚠️ |

⚠ **The boundary is NOT at 5.8** — the first pass called it a 5.8 thing and that was wrong; 5.7.4
DebugGame behaves identically.

### ✅ BISECTION CLOSED 2026-07-29 — the edge is **5.3 → 5.4**

Stock UE 5.4.4 ThirdPerson (all three configs) settled it in **one** install, not the two this item
budgeted for. 5.3 Dev/DebugGame land `GNAM_ES53_1` with **15/15 patterns correct and zero** wasted
validations; 5.4 Dev/DebugGame drop to **1/6 correct, landing `GNAM_V1` after 2,240** — already the
full collapse, indistinguishable from 5.7.4/5.8.x. Both 5.4 configs report the identical 2,240,
consistent with UE building DebugGame's engine modules optimized like Development.

So **5.5 and 5.6 are no longer needed for this question.** Whatever they add is coverage, not
bisection. And if a fix pattern is ever mined, **5.3-vs-5.4 is the pair to mine it against** — the
smallest interval that contains the change, with a clean control on one side.

4.10 and 4.15 extend the healthy band downward, so this is a sharp UE5-era edge, not a slow drift.

**Not project-specific.** A second, unrelated 5.8.0 DebugGame project (Titan) matches StackOBot's
coverage *down to the individual pattern IDs* — same GObjects quartet, same GWorld n=13, same
GEngine quartet, GNames 0 with the same `{CT3, CT4, G42_1}` decoy. Build configuration is the only
remaining variable.

Root cause is a hardcoded destination register — the `GOBJ_V1`-on-DropIn failure mode one target
over. The twin-LEA lazy init is there (46 xrefs to `NamePoolData`), but the first LEA targets
**rbx (`48 8d 1d`) / r15 (`4c 8d 3d`)** and every GNames pattern pins rax/r8/rdx/rsi/rbp.

Decision needed, because it is genuinely marginal:
- **Do nothing** (default). Nobody attaches to a Development build of a template project, and the
  candidate fix `4? 8d ?? <d32> eb ?? 48 8d 0d <d32> e8` has only ~2 literal bytes at its head —
  in the band where `GWLD_G42_4` proves wildcarding backfires.
- **Or mine it**, and put it through the full 65-program gauntlet before it goes anywhere near the
  table. If it survives decoy-free it is also insurance for a shipped game whose register pressure
  happens to land the same way.

All four affected rows are in `sweep.sh` and swept, so the cost is visible as a ⚠️ with its wasted
count in the regression matrix instead of being invisible. **Leave them showing that** until fixed
— note they are ⚠️ (lands correct, expensively), not ❌; the only ❌ in the corpus is 4.10 GObjects.

**Second result from the same pass, and arguably the more important one — rule 5 just paid out.**
The sparse-delegate patterns that were kept purely as redundancy are the *only* thing holding that
target up on non-Shipping builds: on 5.8 `SPARSE_ES2_1` misses and **`X1`/`X2` alone** reach it,
and on **5.7.4 DebugGame those miss too and `SPARSE_MEL55_1` is the sole survivor (n=1)** — the
thinnest coverage anywhere in the corpus. Every one of the three was added against a Shipping
binary that already resolved, i.e. they looked like dead weight at the time. Had any been pruned,
a whole build configuration would have silently lost sparse-delegate support.

All three of the non-Shipping oracles this item was written against (5.7.4 DebugGame, 5.8.0
DebugGame, 5.8.0 Titan DebugGame) are imported `-noanalysis` and swept — the sweep reads raw bytes
and never needs auto-analyze, which is what had made a 300 MB Development project look
un-importable.

-----

## Next self-built oracles: **5.4 and 5.3 are DONE — 5.6 / 5.5 are now OPTIONAL**

*Parent: the 5.3 + 5.4 builds, 2026-07-29. Shipped in [dev-log.md](dev-log.md).*
**Effort M each · Risk low.**

**Both bisection steps are spent, and they answered the question early.** Stock 5.3 and stock
5.4.4 ThirdPerson, each built in all three configs, imported `-noanalysis`, rows in `sweep.sh`:

| | 4.10 | 4.15 | 4.23 | 4.27 | **5.3** | **5.4.4** | 5.5 | 5.6 | 5.7.4 | 5.8.x |
|---|---|---|---|---|---|---|---|---|---|---|
| non-Shipping GNames | ✅ | ✅ | ✅ | ✅ | ✅ 15/15 | ⚠️ **1/6** | – | – | ⚠️ 1/8 | ⚠️ |

**The edge is 5.3 → 5.4.** That was budgeted at two installs (5.4 *and* 5.6) and cost one, because
5.4 collapsed outright rather than landing mid-interval. **5.5 and 5.6 no longer carry a bisection
argument** — judge them purely on coverage now:

- **5.6** — still the more interesting of the two: the `UEnum::Names` → `FNameData` change
  (struct-of-arrays + tagged pointers, the `Neu` module) has no non-Shipping row, and 5.6's only
  PDB oracle is `CrashReportClient`, which is not a game.
- **5.5** — the weakest remaining case, and it was already last: three symbolised Shipping oracles
  exist (Everspace 2 ×2, Meltopia). Worth doing only as part of a gameplay-matrix pass.

Neither is on the critical path for anything. **Do them when a reason appears, not on schedule.**

### 5.4 was packaged, not taken from the prebuilt target — deliberately

**UE_5.4 ships prebuilt `UnrealGame{,-Win64-Shipping,-Win64-DebugGame}.exe` with full PDBs** in
`Engine/Binaries/Win64` (so do 4.23 / 4.27 / 5.7 / 5.8; 4.10 / 4.15 ship two of three), and for AOB
truth alone that is free — copy → `pdb_globals.py` → import `-noanalysis`. It is what made 4.10
possible when VS2015 was unavailable.

⚠ **But it does NOT cover the gameplay-feature matrix.** `UnrealGame.exe` is the bare engine
default with no content and no `ACharacter` to possess, so it answers "where are the engine
globals" and nothing else — GodMode/Teleport/Laufen/Hemmung all need a real pawn. That is why 5.4
was packaged as ThirdPerson anyway: **those binaries serve both jobs.** Use the prebuilt shortcut
when you only want AOB rows; package when the version is also a gameplay target.

### THE ENGINE INSTALL IS TRANSIENT — that is what makes the packaged half affordable

5.3 established the pattern: **install → package 3 configs → `-noanalysis` import → DELETE the
engine.** What stays is ~3.2 GB of packages + PDBs (mirror it to `X:` like the rest); the ~114 GB
engine is temporary. Verified on 5.3 before deleting it: the `.rep`s are self-contained, the
packages are runnable standalone (pak + launcher exe), and both D: and X: hold byte-identical
copies. So this is not "which one can I afford" — it is a sequence, each step costing ~3 GB
permanently.

**One template is enough — do NOT package two.** An earlier version of this item said to build
Flying *and* ThirdPerson. Measurement supersedes that: at 4.27, Flying vs 3rdPerson gave
**identical voter sets down to the individual pattern IDs**, so the template does not affect
engine-global resolution at all. Use **ThirdPerson**, because it is also the Character-based target
the gameplay-feature matrix needs (Flying's pawn has no `CharacterMovement`).

### What 5.4 delivered besides the bisection — and it was ordered first for these, not for that

The ordering argument said the exact boundary version was worth *less* than durable corpus value,
so 5.4 went first on coverage grounds and the bisection was treated as a 1-in-4 bonus. Both paid:

1. **Every UE5 version now has a symbolised oracle.** 5.4 was the last one without. Elliot is 5.4
   but PDB-less and disassembly-derived — and the new stock Shipping row **corroborates it**
   (GObjects 8/15 vs 9/15, GNames 13/16 vs 13/17, GWorld 15/16 vs 13/14), the first independent
   check that row has ever had.
2. **MindsEye finally has a stock-5.4 control.** The engine is **5.4.4 — MindsEye's exact patch
   version.** `mindseye-fork-notes.md` is a whole re-derivation playbook whose "the fork changed
   X" claims all rested on inference about stock 5.4; each is now a measurable delta. Same
   evidentiary shape as the Avowed/DropIn gaps closed the same week.
3. The bonus landed too — it pinned the boundary outright.

Remaining, judged on coverage alone:

2. **5.6** — the `UEnum::Names` → `FNameData` change (struct-of-arrays + tagged pointers, the `Neu`
   module). 5.6 has a monolithic PDB oracle already (CrashReportClient) but no non-Shipping row.
3. **5.5** — last, precisely because it is already the **best-covered** version: three symbolised
   Shipping oracles (Everspace 2 ×2, Meltopia). Its non-Shipping row pairs against real data, which
   is nice but is the smallest marginal gain of the three.

### No C++ project needed — the 5.3 lesson, and it generalises

A C++ project on a launcher engine can fail in UBT (*"must be compiled with Visual Studio 2022 17.4
(MSVC 14.34.x) or later … detected 14.29.30159"*). The message blames the VS version and a forced
`VisualStudio2019` setting; **both are wrong**. It is toolset *ranking*: UE ranks families it does
not know as `FamilyRank=4`, so a recognised-but-too-old **14.29 (from VS2026's v142 component)**
ranks 3, outranks a perfectly usable 14.44, and then fails the `>= 14.34` gate. **Nothing needs
fixing** — the launcher ships `UnrealGame{,-Win64-DebugGame,-Win64-Shipping}.exe` **with PDBs**, so
a Blueprint-only project packages all three configs with nothing compiled.

Also extended BACKWARDS: **4.15.3 Development + DebugGame** rows added (the oldest config group in
the corpus). `pdb_globals.py` gained a pre-4.23 GNames route for them — `FName::GetNames`'s load at
+4, **no** `-0x10` — validated by reproducing the 4.15 Shipping row's recorded `GNames=142c92508`.

**5.3's engine can be deleted** (~114 GB) — checked before saying so: its three `.rep`s are
imported and self-contained, its packages run standalone, and D: and X: hold byte-identical copies
(3168 MB / 100 files / 3 PDBs each). The only thing lost is the ability to rebuild a *different*
5.3 sample, and per the note above a second template would add nothing anyway.

-----

## Gameplay-feature regression matrix on the self-built samples (Teleport tab et al.)

*Parent: "can the PDB corpus improve Teleport/GodMode accuracy?", 2026-07-29.* **Effort M · Risk low.**

**Not via offsets — that question resolves to "no", and by design.** `Solitar`, `Laufen`, `Hemmung`
and `Wirbel` contain **zero hardcoded struct offsets** (verified by grep); everything binds through
UE reflection by NAME (`CanBeDamaged`, `CustomTimeDilation`, `CharacterMovement`, `MaxWalkSpeed`,
`GetHitResultUnderCursorByChannel`, …), per the CLAUDE.md rule. Runtime reflection is *more*
authoritative than a PDB — it is the data the game itself uses, so it tracks licensee forks a PDB
cannot. Using a PDB to "correct" an offset would be a step down, not up.

> **▶ THE SAMPLE IS WRITTEN — [`tools/ue-sample/`](../tools/ue-sample/README.md) (2026-08-05).**
> A stock **UE 5.4 Third Person** project plus one `ADumperTestActor` carrying a deliberate property
> zoo, spawned by a `UWorldSubsystem` so **no binary level asset has to be edited**. It exists
> because a large share of the ⬜ register is blocked not on effort but on *finding a game that
> happens to contain the right UPROPERTY*: `TSet`/`TMap` (⬜ since build **927**), `TOptional`
> (**942**), the NumericAll byte family (**796**), **B28** CJK FText, and **B8**, whose blocker was
> *"needs a game that actually goes quiet when backgrounded"* — solved by setting
> `t.IdleWhenNotForeground` **from code** behind a `-DumperTestIdle` switch, which also turns
> Grausam's foreground lock into a **positive** test rather than "it seemed to keep working".
> (Not from an ini: that cvar is `ECVF_Cheat`, and an ini that sets one makes the project
> **impossible to cook** — 22 errors, `ExitCode=25`. Measured, not guessed.) Expected values are written down in that README, so a
> disagreement is a defect rather than a discussion. **Still to do: package it** (Shipping +
> Development, ~20 min in the editor) — the source is versioned, the binaries are not.

**The real win is a reproducible live test target.** Today every one of these features is verified
ad hoc on a commercial title — *"LIVE-VERIFIED P3R"*, *"VERIFIED Tower of Mask + DQ7R"*, *"NO-OP on
FF7R"* — one-shot, unrepeatable, and gated on owning and launching that game. The self-built samples
are runnable, free, symbol-carrying, and exist at six engine versions **with source**. That converts
"someone tested GodMode once" into a matrix, and it is the direct answer to the pile of
*"needs in-game verify"* / *"⏳ in-game verify"* / *"UNVERIFIED"* items in this file.

**Highest-value first cell: reflected-UFunction survival, Shipping vs Development, same project.**
The repo already knows the failure mode ([lessons-learned.md](lessons-learned.md) §UCheatManager):
`UCheatManager::Fly/Ghost/God/Slomo` **invoke successfully and do nothing** in cooked Shipping —
the bodies are `#if !UE_BUILD_SHIPPING`, but the `UFUNCTION(exec)` metadata is generated pre-cook and
survives. A same-project Shipping/Development pair *measures* which reflected functions get hollowed
out instead of discovering it per-game. **This is the DI427 `check()`-gating story one layer up** —
same source, same engine, config the only variable.

**What a PDB genuinely could add, with a caveat that narrows it.** `Schlacht`'s `FHitResult` is the
one non-reflected struct (UE4 `.Actor` weak-ptr vs UE5 `HitObjectHandle`); it currently locates the
field by sub-field NAME and dumps the layout when it fails, which is already fairly robust. PDB type
info could pin the layout per version — except GROUND-TRUTH records that these StackOBot PDBs carry
**only partial merged type info** (`FFieldClass`/`UObjectBase`/`FUObjectItem` have no TPI record at
all). For layout and API-name questions the **installed engine source** is the better oracle.

-----

## AOB specificity index (§6) — MEASURED and feasible, build it. Block library (§4) second.

*Parent: [aob-block-library-eval.md](aob-block-library-eval.md), extended 2026-07-29 with §6.*
**Effort M · Risk low.**

**The problem it solves:** answering *"is this candidate AOB too generic?"* currently needs the full
~156 GB Ghidra corpus + ~53 GB `UE_Analyze_Data`. That makes pattern authoring single-machine,
non-contributable, and dependent on 200 GB surviving.

**§6 is now measured and the licensing question dissolves** — it ships a byte n-gram *frequency
table*, never code. Validated on UE 5.4.4 Shipping against all 151 patterns: **0 upper-bound
violations**, and monotone (bound <10 → max 4 measured hits; bound ≥1000 → max 852). Size ~9.3 MB
per binary at threshold 16, less as a max-count union.

Build in this order — §6 before §4, because §6 needs no legal call, is already validated, and
answers the question that actually blocks authoring:

1. `tools/pe/build_ngram_index.py` — union index over the self-built inventory
   ([reference-builds.md](reference-builds.md)). Offline, corpus machine.
2. Commit the index.
3. `tools/pe/aob_specificity.py` — AOB in; bound + limiting window + the run-<4 verdict out. Stdlib
   only, so it **could** run on the bare second machine and in CI — ⚠️ **but it does not, and nothing
   in the repo calls it** (audited 2026-08-01; zero references from `dll/`, `ui/`, `scripts/`,
   `build.ps1`, both workflows). Advisory tool, human-invoked only.
4. ~~Does a one-version index generalise?~~ **ANSWERED — see §6 of the eval.** Cross-*version* is
   fine (4.27-only index vs the 5.4 binary: 0 violations / 113). Cross-*codebase* is not: on the 58
   binaries the index never saw, `CLEAR` violates at **0.20%** with a real tail (`GNAM_UD2` bounds
   ≤15, takes 932 on FF7R). Cause is code coverage — stock templates contain no game code, and
   licensing means third-party code can never be indexed, so the limit is **structural**. Wording
   in the tool now says "quiet in stock engine code", never "certified".
5. ~~§4's shape blocks + `blocktest.py`~~ **DONE.** 340 blocks / 195 KB from 22 self-built
   oracles; `blocktest.py` is stdlib-only, runs in seconds, and is **wired into CI** — the first
   automated check `Himmel.h`'s patterns have ever had. Asserts *resolution*, not just matching:
   perturbing one pattern's displacement `adj` by 8 still matches but fails 15 blocks.

**4 of 5 steps are complete — step 5 was substituted, not done.** The eval's own build order
([aob-block-library-eval.md](aob-block-library-eval.md) "Build order") ends with
*"5. Gate authoring on it: a candidate clears the pre-filter before it earns a sweep."* The list
above quietly replaced that with the §4 block work (which is genuinely done and genuinely in CI) and
then declared the set complete. **Gating was never built**, which is exactly why nothing reads the
n-gram index. Two honest options, and picking one is the open decision:

* **Wire it up** — add `aob_specificity.py --tsv` to the CI step beside `blocktest.py` and fail on a
  regression in the CLEAR set. Cheap; makes the artifact load-bearing and its accuracy worth tuning.
* **Or retire the claim** — keep the tool as a human-run triage aid and stop describing it as part of
  a completed pipeline. Also legitimate; it is a good tool that simply is not a gate.

Do NOT tune the index's threshold before choosing: a knob nobody reads cannot be evaluated.
Measured 2026-08-01: **every `CLEAR` verdict comes from the threshold FLOOR (limiting window absent),
never from a stored bucket — CLEAR-absent 47 / CLEAR-present 0.** So `CLEAR` means "we have never
seen this window", and a higher CLEAR count indicates a *worse-covered* index, not a quieter pattern
set. (Reductio: AOBMaker's x86 index, which contains zero x64 code, certifies 109 of our x64
patterns CLEAR.)

What remains is optional and should wait for a real need:
* Re-extract blocks whenever a pattern is added/renamed — `blocktest.py` reports `skipped N` when a
  recorded `found_by` no longer exists in `Himmel.h`, so drift is visible rather than silent.
* Rebuild the n-gram index when new self-built Shipping oracles land (it only ever needs to grow).
* Neither tool is an acceptance gate, and that must not erode: **rule 5 still means the sweep.**

⚠ **Neither half is an acceptance gate.** Neither can say a pattern hits the RIGHT address —
`GNAM_XX_1` scores a clean bound of 57 and is `DECOY-ONLY`. `Himmel.h` rule 5 keeps meaning the
sweep; this is a pre-filter so the expensive run only sees plausible candidates.

⚠ **The trap that already bit once, recorded so it does not repeat:** the first scorer indexed only
n=6 and bucketed *"no literal run reaches 6 bytes"* as **rare** — so `GWLD_V3` (852 measured hits)
scored "<8". That is the absence-of-evidence trap `replay_patterns.py` warns about, in a new place.
Index n=4/5/6 and score at the largest n the longest run supports; **never let "unscoreable" fall
into the "rare" bucket.**

-----

## Palworld: re-point the corpus manifest at the D: archive (the live install has patched)

*Parent: same pass.* **Effort S · Risk low.**

Palworld updated 2026-07-29 (md5 `fb10d568…` → `a2dadf69…`, +11,776 bytes), so
`py tools/ghidra/preflight.py Palworld --verify-hash` now correctly reports `id=MISMATCH`
against `H:\SteamLibrary\...`. Nothing is broken — the `.rep` is the artifact of record and
`D:\UE_Analyze_Data\Game Binary backup\Palworld` holds the exact corpus build (verified: its
`SparseDelegates` consensus is `148fb66b0`, the address hardcoded in `Himmel.h`'s `SPARSE_PAL51_1`
note). The backup was taken the day before the patch.

⚠ **DO NOT just re-run `build_corpus_manifest.py` — an earlier version of this item said to, and
that was wrong.** The generator NULLS `steam_buildid`/`size`/`sha256` on a drifted row (correctly:
it must never assert the wrong build), so regenerating would ERASE Palworld's `24181527`, which is
the only pointer to the SteamDB build the `.rep` was made from. **That value is now preserved in
[`tools/ghidra/corpus-provenance.tsv`](../tools/ghidra/corpus-provenance.tsv)** — a hand-made
snapshot that the generator must not overwrite. Regenerate the manifest only after confirming the
provenance snapshot is committed. `corpus-manifest.tsv/json` themselves stay generated — do not
hand-edit those.

The patch itself is now a **settled fact, recorded in GROUND-TRUTH.md**: every global moved
(+0x3300, Sparse +0x3180) and **not one pattern broke** — all six voter sets came back
character-identical. Do not re-measure this per patch.

`GOBJ_DI427_1/2/3` are the only patterns for which `UE4.27-DropIn` is the sole oracle, and what
they encode is the **32-byte `FUObjectItem`** (`shl r,5`). Those 8 bytes are `TStatId`, gated at
4.27 by `#if STATS || ENABLE_STATNAMEDEVENTS_UOBJECT` (`UObjectArray.h` @ `4.27.2-release`).
`STATS` is 0 in Shipping, so a Shipping sample adds nothing — Breeders and Maelstrom already
cover the stock 24-byte item.

Steps: import (one project, `-noanalysis` is fine for the gate) → derive truth from the PDB in
Python (`?GUObjectArray@@3VFUObjectArray@@A`, `?GWorld@@3VUWorldProxy@@A`,
`?GEngine@@3PEAVUEngine@@EA`, the sparse mangled name; GNames via
`FNameDebugVisualizer::GetBlocks` minus 0x10 — verify the 0x10 at 4.27) → add the `sweep.sh` row
and the `GROUND-TRUTH.md` block → full sweep → confirm `GOBJ_DI427_*` now land on it too.

Payoff: converts a sole-oracle dependency on an external store — where a patch can silently
replace the build, as happened to `ES2-0517` — into a locally rebuildable asset, and gives a
second three-config control group after 4.23.

-----

## In-game text: S2T conversion + local-LLM translation — EVALUATED (2026-07-24), mostly NOT BUILT

Full 41-agent evaluation in [text-translation-eval.md](text-translation-eval.md) (all 12 load-bearing
claims refuted or qualified). **In-memory text rewrite = rejected** (three UE-source-level walls: a
ProcessEvent hook can't see `SetText` §3-3; an in-place same-length overwrite doesn't repaint §3-4; and
`FString::Data` can't be repointed without corrupting GMalloc §3-1 — on top of the first-order **font
glyph-coverage** risk). The **offline `.locres` route wins outright** for the S2T half; LLM translation
belongs in an **offline pre-pass** (extract → translate on any GPU incl. a remote box → re-import), never
a live path. Open follow-ons, in priority order:

- **Phase 3 (SHIPPED, build 2368)** — `ReadFTextString` now decodes UTF-8 *and* UTF-16 display strings
  (`Utf8Helpers::DecodeFStringBuffer`) + UE5.4+ pointer-indirection probe; `ReadFString`/`ReadFUtf8String`
  torn-read fixed. ⚠️ **In-game-unverified**: whether STVoyager's UE5.6 ITextData header lands on a probed
  offset — if a re-test returns empty, CE-pointer-scan `2E1097B7000` for the offset chain (risk #3). **M · low**
- **Phase 1 — Locale switcher** (`Lektüre` module: `SetCurrentCulture` invoke + `locale_get`/`locale_set`
  + one UI card). Zero-write, solves "game has zh-Hant but the menu won't let me pick it". Smallest useful
  slice; validate on stock UE5.6 (Satisfactory / STVoyager). **S · low**
- **Phase 2 — Font coverage probe** (`UFont::FontCacheType` + composite-font cmap parse → Offline/covered/
  missing-N). Useful diagnostic even if translation is never built. **M · low**
- **Phase 0 (SHIPPED here)** — the one-page offline S2T/LLM workflow lives in
  [text-translation-eval.md](text-translation-eval.md) §附錄. **done**

-----

## 🔎 Audit #4 fixes (build 2554 — 2026-08-04; full detail in [audit-2026-08-04-findings.md](audit-2026-08-04-findings.md))

Fourth audit, and the first to cover **refactor** alongside bugs/leaks. Scope = the 96 shipped source
files / +15,372 lines changed since the audit #3 baseline (`af2ce50`). Run as two passes: **4a** swept 8
bug areas + 2 refactor areas; a **completeness critic** then mapped what 4a never read, and **4b** closed
those 6 gaps. 48 agents, test baseline 3110 green. **51 items kept** (2 HIGH · 14 MED · 32 LOW · 3 INFO),
**7 refuted** (listed in the findings doc — do not re-raise).

**Do not track individual status here** — that is the mistake audit #3's block made (one sentence said
"awaits in-game verification" for 13 fixes at once, so nothing could be ticked off). The findings doc is
the tracker; delete a row there when it ships and write it up in [dev-log.md](dev-log.md). This block only
records what the audit *is* and the two things that must not be got wrong:

- **✅ DONE (build 2560) — B6 + B27, one commit.** B27 = the Coordinate Library store was constructed and
  never passed (11 positional args into a 12-param ctor), so the whole feature never persisted; B6 =
  Clear-all had no pre-clear backup, harmless *only because* nothing persisted. Wiring now goes through
  `AppComposition.BuildMainWindowViewModel` with **required** parameters (verified: dropping the argument
  is now `CS7036`, not a silent no-op), and a `.preclear.bak` + confirmation guards the wipe. See
  [dev-log.md](dev-log.md) build 2560.
- **✅ DONE (build 2561) — B1 + B30 + B40, one commit.** The live test ran first and inverted the plan:
  `executeCodeEx` returned `nil` without raising, so (a) was real and (b) was *latent* — making "fix the
  arity alone" a certain session brick rather than a suspected one. Both halves shipped together, plus the
  serving-vs-parked split that lets a re-tick revive the DLL instead of tearing down someone else's proxy.
  A deliberate invariant ("no `executeCodeEx` in `[ENABLE]`") was **narrowed with its reasoning stated**,
  not dropped. See [dev-log.md](dev-log.md) build 2561.

**Two root causes, both worth fixing as patterns rather than site-by-site:** 4a's is *the report and the
reality are computed by different code paths* (a success message written by code that never observed
whether the operation ran); 4b's is *a cheap proxy signal substituted for a predicate this codebase
already computes* — filename instead of an export probe, a 1-second sleep instead of an actual signal,
directory mtime instead of the newest file inside — **and in 10 of 22 cases a sibling in this same repo
implements the real check correctly.** A secondary 4b thread, *silent defaults at composition points*
(B27, B31, B38, B45), is why B27's composition-root test is worth more than its one-line production fix.

**`ce-artifacts` is the area to give standing attention.** Three of 4b's five MEDIUMs live there, each can
leave a working setup broken with a confidently wrong message. **Partly closed since this was written**
(build 2747): CI now runs **eight** python gates, and `check_mailbox_contract.py` covers the CE surface
that had none — it hashes the mailbox contract and requires the version baked into every emitted script to
match the DLL. What is still uncovered is the **emitted Lua text itself** and the **CE-plugin entry path**;
`CeMailboxBailoutTests` asserts the generators' bail-out shape, but nothing executes the Lua.
`axaml-strings`, the AOT/dependency surface and the generated-proxy family came back effectively
clean, which is a real result worth recording.

-----

## 🔎 Audit #3 fixes (build 2168) — **CLOSED, archived**

All 23 scheduled items shipped; the rest were refuted or downgraded to optional cleanup.
The rollup moved to [archive/todo-closed-2026-08-build-2715.md](archive/todo-closed-2026-08-build-2715.md);
the per-finding detail was always in [audit-2026-07-14-findings.md](audit-2026-07-14-findings.md).

## ▶ Next up (genuinely actionable now)

- **Multi-pipe Phase 1 — residual verification: only the WATCH item is left** —
  Effort: **S** · Risk: low. The two-connection lane split shipped + in-game verified for §9.6 items
  1–5 (dev-log 2026-06-28).
  > **✅ The lane-drop edge is now verified (Elliot 2026-07-23).** Closing the game mid-snapshot
  > dropped the bulk lane and the router did exactly what §9.7 specifies:
  > `Pipe lane dropped — tearing down both lanes for a clean reconnect` → `Pipe disconnected`, with the
  > in-flight snapshot faulting into H1's delete path rather than half-finishing. No wedge, no orphan.
  Still open: (6) **watch-event delivery** to the interactive lane (System-tab / address watch still
  pushes correctly while the bulk lane is busy). Verify opportunistically.
  *Parent: multipipe-eval §9 (PR #396).*
  The build-1836 single-handle worker-pool was REVERTED (deadlocked on the synchronous pipe, §8.1).
  The sister repo `D:\Github\discrete` runs a proven alternative: the UI opens **two** client
  connections (interactive + bulk) to a `maxInstances≥2` server that serves **each connection on
  its own thread + own handle**. Each connection stays serial read→write on one handle (one thread)
  → **no same-handle deadlock, and NO overlapped-I/O rewrite needed** (each handle is touched by
  exactly one thread). Safe because the interactive lane never builds the Aura class caches and the
  bulk lane runs scans one-at-a-time (§9.1). DLL work = per-connection refactor of Fern (thread-per-
  connection accept, per-connection write-mutex / in-flight / watch-event routing, monitor + session
  cleanup keyed per-connection — §9.3); UI work = a 2nd `PipeClient` + a `BulkCommands` router like
  discrete's `BackendAdapter` (§9.4). Lane table = §6. **MUST pass the §9.6 in-game checklist before
  shipping.** Open decisions in §9.7 (maxInstances count; session-drop policy; cancellation scope).
  Snapshot SPEED is a SEPARATE issue (§9.5): UI-side single-threaded multi-MB chunk parse (~2.4s/chunk)
  → streaming `Utf8JsonReader`/smaller chunks. *Parent: reverted Phase 1 build 1836 (dev-log 2026-06-28).*

- **Magic-number centralization — Tier 2 remainder + Tier 3 (deferred; low priority)** —
  Effort: **M** · Risk: med. Tier 1 (dup/tunable literals) + the Tier 2 `IsUserspacePointer` paired-
  bounds helper SHIPPED (dev-log 2026-07-03). LEFT because each carries genuine per-site multi-meaning
  nuance: object-count/size ceilings — `0x800000` (8M UObject count), `0x100000` (1M, but needs
  SPLITTING into container-element-COUNT vs PropertiesSize-BYTES — one const would conflate them),
  `[0x1000..0x400000]` NumElements window; and `MAX_CLASS_HIERARCHY_DEPTH=64` (`64` is high-collision —
  buffers / bit-counts — needs per-site verification). Tier 3 = single-use knobs (Heiter startup
  delays, Fern watch/monitor poll, Mimic caps, Solitar caps, Wirbel eps/channel, movement/debug-cam
  protocol-id enums). Same careful methodology: pair/meaning-gated, never blind-sweep a literal.
  *Parent: magic-number centralization (dev-log 2026-07-03).*

- **CE responsiveness under heavy scans — pick a NON-priority approach if it ever bites** —
  Effort: **S-M** · Risk: med. Phase 0 (drop scan threads to `BELOW_NORMAL`) was **REVERTED** —
  with the game saturating cores it starved scans ~20× (Snapshot 1–2 min → ~1 h). The
  pre-existing `cores − 2` count cap (`ScanThreadCount`, Aura.cpp:105) is the right throttle and
  is restored. CE `.CT` invoke is already off-pipe (Mimic mailbox); if a real starvation case
  appears, prefer: yield points inside the scan loop, a smaller worker cap while a CE invoke is
  pending, or `Stark`-queue priority for mailbox invokes — **not** a blanket thread-priority drop.
  *Parent: reverted Phase 0 build 1834 (dev-log 2026-06-28).*

- **Class Pivot discovery (build 1742) — deeper capture-memory fix** —
  Effort: **M** (memory) · Risk: low. The bounded N-snapshot discovery + shape ranking +
  Locate-in-GWorld/GameEngine + resizable/filterable results shipped build 1742; the class
  picker freeze was fixed + in-game verified build 1764 (filter TextBox + ListBox — see
  dev-log). The **change-driven discovery ("Suggest Targets") path itself is still NOT in-game
  verified** end-to-end. Separately: the post-capture compacting `GC.Collect` (build 1742) is a
  **mitigation** for the multi-snapshot working-set bloat — the *deeper* fix is to stop the
  transient allocation at the source by replacing the capture's JSON-DOM parse with a streaming
  `Utf8JsonReader` (`SnapshotChunkAsync` / the chunk parse). Only pursue the streaming rewrite if
  the GC reclaim proves insufficient on huge (Avowed-scale, 266K-object) games. *Parent: dev-log build 1742.*

- **Class Pivot — rounding-mode + "can't-find-data"/GAS-capture follow-ups (deferred from build 1672)** —
  Effort: **S-M** · Risk: low. The per-panel **RoundingMode {Round/Trunc/Ceil}** (build 1672) was rolled out to Value Search, Snapshot, and SPC but **NOT Class Pivot**. Two distinct gaps:
  (1) **Rounding mode** — Pivot does **no numeric value MATCHING** today: it groups by the *rendered* key string (`PivotEngine` uses `SnapshotNumeric.Render`) and `PivotDiscoveryEngine.Direction()` compares raw `double`s with no reduce. So a rounding-mode switch is largely **N/A** — but if Pivot ever grows a value-target filter, it should reuse `SnapshotNumeric.ExactMatch/OrderedMatch/BetweenMatch(...,FloatRoundMode)` like the other panels. Lower priority: optionally apply the reduce to the grouping KEY so float GAS values bucket by displayed integer (e.g. 513.36/513.4 group as "513").
  (2) **"Can't find data" / GAS-capture** — the recent snapshot fixes (nested-`StructProperty` GAS capture `Aura::CaptureDirectStructFields`, build 1648; rounded-float matching) flowed into Snapshot/SPC/Group. Pivot reads the **same captured corpus**, so the GAS `Health.BaseValue`-style fields *should* now appear in Pivot automatically — **but this is UNVERIFIED**. Verify in-game that a GAS attribute captured post-1648 actually shows up as a pivotable field/key in Class Pivot; if Pivot has its own field-selection or numeric-only filter that drops nested-struct leaves, fix it. *Parent: rounding-mode switch build 1672; snapshot GAS-capture build 1648 (project-snapshot-nested-struct-gas).*

- **Flatten GAS attributes — optional extensions (deferred by user, build 1698)** —
  Effort: **S** · Risk: low. The "Flatten GAS attributes" Options toggle (build 1698) collapses a
  `GameplayAttributeData` StructProperty one level in **Copy CE XML / Copy CE Field** only. Two
  follow-ups the user explicitly scoped out of that change:
  (1) **Export CSX** — apply the same flatten to the CE Structure Dissect (`.csx`) export
  (`CsxExportService.EmitElement`). The `IsGasAttributeStruct` detection + combined-offset math port
  directly, but CSX is a separate emitter so it was intentionally left out.
  (2) **Other single-field / wrapper structs** — keep flatten GAS-only "for now"; a general
  "flatten any single-/two-scalar-field struct one level" option would need a careful detection rule
  to avoid surprising collapses ("various cases"). *Parent: Flatten GAS attributes build 1698
  (project-gas-attr-flatten-ce-export).*

- **dxgi proxy early-load fragility — harden (thin-shim + renamed real-dxgi copy), or leave dxgi as "late-load games only"** —
  Effort: **M-L** · Risk: med (loader-time code + deploy flow). **Deferred by owner (2026-06-19); the UI default is back to version.dll.** ⚠ **CORRECTION 2026-08-18: Octopath does NOT use version.dll — that proxy never loads there. It needs `winmm.dll`** (verified end-to-end, `[OCTOPATH-G2T3-2026-08-18]`), so this item's premise that Octopath is served by version.dll in the meantime was wrong. The dxgi proxy instant-exits on games that call dxgi **extremely early — under the loader lock, before our CRT is initialised** (Octopath Traveler: debugger-confirmed across 3 distinct crash dumps — execute-0 / `__tzset` uninit CRT lock / `RtlAllocateHeap` null heap; see dev-log 2026-06-19). Two genuine early-load fixes shipped + kept (`Sein::GetTimestamp`→Win32 `GetLocalTime`; dxgi lazy self-resolving thunks), but they do NOT make Octopath's dxgi work — the **root blocker** is that `LoadLibraryW(real same-named System32\dxgi.dll)` returns NULL under the early loader lock. **version.dll dodges it all by being called at normal runtime, not under early loader lock.** Robust fix = **thin-shim split (like RE-UE4SS):** `dxgi.dll` becomes a tiny CRT-free forwarder that (a) loads the real dxgi via a **renamed copy** (`dxgi_orig.dll`) to dodge the same-base-name-under-lock failure, and (b) `LoadLibrary("UE5Dumper.dll")` to run the heavy dumper as a **separate, normally-named, late-loaded** DLL. Deploy becomes **2 files** (`dxgi.dll` + `UE5Dumper.dll`) → the Proxy Deploy panel's deploy/undeploy/redundancy/Update-All must copy/remove both. NOTE: `/MD` (dynamic VCRuntime/UCRT) alone is only a **partial** fix — it removes the CRT-init crashes (Octopath already loads the shared UCRT early) but NOT the loader-lock same-name `LoadLibrary` blocker (that resurfaces as execute-0). version.dll/dinput8.dll don't need any of this (they load late). *Parent: dxgi proxy build 1172; early-load diagnosis + 2 fixes build 1351 (dev-log 2026-06-19).*

- **UE5.7+ packed FUObjectItem — live-verify + calibrate when a packed game appears** —
  Effort: **S** (mostly verify) · Risk: low (gated, last-resort only). Packed parsing shipped
  build 1108 but is **UNVERIFIED** (no `UE_ENABLE_FUOBJECT_ITEM_PACKING` game exists yet). When
  one does: attach, watch for the `*** UNVERIFIED ... PACKED ... ACTIVATED ***` WARN (or force via
  `set_packed_consts {force:true}`), then tune `align_bits`/`ptr_mask_bits`/`serial_off` against the
  echoed `GObjects[0..7]` samples until names resolve; confirm the object walk + a CE XML/CSX export
  are correct; then promote out of UNVERIFIED (drop the badge gate, pin the constants).
  Open sub-question: the packed **SerialNumber** offset (currently best-effort `0x0C`) is unpinned.
  *Parent: PackedItem.h + Aura packed mode + set_packed_consts shipped build 1108 (dev-log 2026-06-14).*

- **Guess? "missing" mid-object data — RESOLVED (working as designed; diagnostic kept).** The
  `WALK:guess` diagnostic (build 1364+, `Ubel.cpp` `WalkInstance` fillGaps block, one line per
  Guess? walk, opt-in-gated) confirmed it **live on Elliot `LSGameWork`**: `0x170=16(ArrayProperty)`
  covers `0x170–0x180` and `0x180=80(MapProperty)` covers `0x180–0x1D0` exactly — the region the user
  saw "missing" is the inline allocator bytes of a TArray + TMap, fully owned by reflected container
  properties. The `GAPS:` list has nothing in `0x170–0x1D0` (only small padding/bitfield holes like
  `[0x3,0x8)`, `[0xA04,0xA3C)`). So `Guess?` correctly emits no raw rows there — CE dissect just
  flattens the container internals; our walker shows them as expandable Array/Map rows. **No code
  change.** The diagnostic line is kept as the standing answer to future "why doesn't Guess? show
  region X" questions (gated on `Guess?` being on). Optional future nicety: a `docs/tips.md` note that
  container internals aren't decomposed into guessed rows. *Parent: Guess-What leading-gap fix (builds
  1330-1333) + diagnostic (build 1364, this session); confirmed live 2026-06-19.*

- **Native-C Value Scan — P0–P3 ALL SHIPPED on dev; only in-game verify of P3 remains** —
  Effort: **0** (verify only) · Risk: low. Full design + status in
  [native-c-value-scan-spec.md](native-c-value-scan-spec.md). Opt-in raw/unmanaged
  (non-`UPROPERTY`) scan for native HP/MP via "Guess What" (`Ubel::GuessGapTypes`), across
  Value Search + Group Scan + Snapshot→SPC→Pivot. **DONE + in-game VERIFIED:** P1 single
  (Octopath), P2 group (FF7 Rebirth). **P3 DONE (build+tests+AOT green):**
  `CaptureSnapshotChunk(captureNativeC)` → `AppendRawHoleFields` (GuessGapTypes →
  `NormalizeGuessedTypeToProperty` → drop Pointer/Padding → `<raw@0xNN>` fields,
  numericScope-filtered, ≤256/obj); pipe `native_c` on `snapshot_chunk`; C#
  `SnapshotViewModel.IncludeNativeFields` toggle + intro string. SPC Query + Class Pivot
  consume raw rows with ZERO code changes (key on prop_name=offset + canonical declared_type;
  existing `fields` schema, no migration). **REMAINING: in-game verify P3** — BLOCKED on the
  snapshot-perf item below (FF7 Rebirth capture with Native-C didn't finish — 16+ min, >50%
  uncaptured). Verify on a smaller / faster game, or after the perf work: capture a native
  snapshot pair around a stat change, confirm SPC diff tracks a `<raw@0x..>` value + Class
  Pivot decodes it (not hex).
  *Parent: P0–P3 shipped on dev (this session); builds on value_search_caveats, the `Orden`
  seam (group-value-scan-spec §3.1), and the "Guess What" build (commit 75ea723).*

- **[✅ IMPROVED — parked unless it bites again] Snapshot capture too slow on huge games** —
  User re-tested 2026-07-23 **on a smaller title rather than FF7 Rebirth and confirms the four
  changes below improved it**, so the item is parked. What that does NOT settle is the original
  433K-object case — keep the notes below for when it recurs; the untried levers are class-scoped
  capture (only a chosen class's instances) and a clearer "X% captured" progress.
  **Native-C P3 verification is no longer blocked by this.**
  Effort: **M-L** · Risk: med (touches the hot capture path). FF7 Rebirth snapshot with
  Native-C ON ran **16+ min and left >50% of objects uncaptured**, so P3 couldn't be verified
  there. Likely causes, in order: (1) **Native-C `AppendRawHoleFields` calls `Ubel::GuessGapTypes`,
  which reads memory BYTE-BY-BYTE** (the zero-run probe does one `Macht::ReadSafe<uint8_t>` per
  byte) — over every hole of every object that's very slow. **FIX SHIPPED (this session):** `Ubel::GuessGapTypes` now reads the whole gap
  ONCE into a reused `thread_local` buffer and guesses in-buffer — the per-position AND
  per-byte (zero-run probe) SEH reads are eliminated; output is byte-identical; an SEH
  fallback is kept for a faulting / over-large gap. Also speeds up LiveWalker "Guess What".
  (still 10+ min after this fix, so the round-trip overhead below dominates too.) (2)
  ~~one pipe round-trip per 200 objects~~ **CHUNK RAISED 200 → 1000 (this session, 待測 /
  in-game re-test pending):** `Constants.SnapshotChunkSize` — ~2166 chunks → ~433 for 433K
  objects, also cutting the per-chunk SQLite write-transaction count. Safe (byte-mode pipe +
  `StreamReader.ReadLineAsync` accumulate any size; DLL 15s per-chunk deadline re-chunks slow
  chunks). **NEEDS in-game re-test on FF7 Rebirth — if still too slow, the bottleneck is the
  single-threaded DLL walk → do (3).** Could raise further / batch SQLite inserts if needed.
  (3) ~~DLL `CaptureSnapshotChunk` is single-threaded~~ **PARALLELIZED (this session, 待測 /
  in-game re-test pending):** the per-object capture loop now runs across worker threads via
  `ParallelGObjectsScan` (each worker fills its own `SnapshotObject` vector, merged after; whole
  chunk processed → `scanned` stays contiguous for the pager; Tot-cancel only, no wall-clock
  early return). Chunk size raised to **8192** so each full chunk clears `ScanThreadCount`'s
  >=8192 worker-thread threshold + amortizes the per-chunk cancel watcher. Verified by an
  adversarial 3-lens race/correctness audit (capture-helper statics, GuessGapTypes/WalkClassEx
  copy-out, pager contiguity) — no crash/corruption/mis-paging; the worker stride-check was
  fixed to range-relative for prompt cancel. **NEEDS in-game re-test on FF7 Rebirth** (combined
  with the GuessGapTypes + chunk wins, this is the big one). (4) **Source-level noise skip
  SHIPPED (builds 1484-1486, dev, in-game verify pending):** the "Auto detect Engine/System
  noise" option (default ON) `continue`s past pure engine/system classes (UI widgets, textures,
  sounds, Niagara, anim instances, `/Script` engine packages) in the capture loop BEFORE the
  per-field walk — cutting the dominant per-object cost for the many noise objects a big game
  carries + shrinking the DB, with a gameplay guardrail that force-keeps Actor/Pawn/Character/
  component-derived classes. Complements (1)-(3); especially helps games heavy in engine UI/FX
  objects. Still open if needed: class-scoped capture (only a chosen class's instances) + a
  clearer "X% captured" progress.
  *Parent: Native-C P3 in-game test (FF7 Rebirth), this session.*

- **DynOff calibrated offsets are non-atomic — tighten the second writer (low-risk hardening)** —
  Effort: **S** · Risk: low. The race audit of the parallel snapshot flagged a PRE-EXISTING
  technical data race the parallel readers widen: `DynOff::FSTRUCTPROP_STRUCT` (and the sibling
  calibrated `DynOff::` ints) are non-atomic. `Ubel::CorrectSubclassOffsets` serializes its writes
  (`s_calibrationMutex` + acquire/release `s_checked`), but there's a SECOND unguarded writer at
  `Ubel.cpp:~4288` (`DynOff::FSTRUCTPROP_STRUCT = tryOffset;` inside `WalkInstance`), and the
  snapshot/WalkClassEx-enrichment readers aren't gated by `s_checked`. Benign in practice
  (idempotent convergent writes + aligned-int load/store atomicity on x64), so NOT a crash risk,
  but technically UB. Lowest-risk fix: drop the redundant `~4288` write (verify `CorrectSubclassOffsets`
  already covers that calibration first — don't regress StructProperty struct-name resolution), or
  make the calibrated offsets `std::atomic<int>` with relaxed loads. *Parent: parallel-snapshot race audit, this session.*

- **NEW (Elliot 2026-07-23) — a transient `MH_CreateHook` failure permanently poisons the session
  into the "unsafe direct call" path** — Effort: **S-M** · Risk: **med** (touches the hook path, the
  most crash-prone code in the DLL). **Observed once, on the run right after a session where the same
  hook installed fine at the same address:**
  > `[ERROR] GameThreadDispatch: MH_CreateHook failed: MH_ERROR_MEMORY_ALLOC`
  > `[WARN]  GameThreadDispatch: hook install failed, invoke will use direct call (unsafe)`
  > `ProcessEvent: first-time init complete — offset=608, hook_active=0`

  `MH_ERROR_MEMORY_ALLOC` means MinHook could not place a trampoline within reach of the target, which
  depends on the process's VM layout at that instant — i.e. it is **intermittent by nature**
  (22:24 install at `0x1415968E0` succeeded; 22:43 the same address failed). Two problems follow:

  1. **It is latched forever.** `TryInstallGameThreadHook` sets `static bool s_hookAttempted = true`
     **before** attempting and never clears it on failure, and `EnsureProcessEventReady` wraps the
     whole thing in `std::call_once`. So one unlucky allocation means *every* invoke for the rest of
     the process life takes the fallback — even when the user re-enables the feature minutes later,
     when the VM layout may well have room again.
  2. **The fallback is the historically crash-prone path**, and a WORKER-driven feature hammers it:
     See-Through logged **552** × `UE5_CallProcessEvent: hook not active, using direct call (unsafe)`
     in 19 seconds (~10 ProcessEvent calls/second from a non-game thread). It happened to survive
     here, but a one-shot user invoke and a 10 Hz re-assert worker are very different exposures.

  **✅ FIXED (build 2358) — (a)+(b)+(c) all built, with the UI reflecting failure AND recovery:**
  - **(a) Retry instead of latch.** One install path (`TryInstallGameThreadHook`), no permanent
    `s_hookAttempted`: it returns early when the hook is already up, otherwise retries up to 8 times
    with a 5 s cooldown. Cheap enough to sit on the lazy invoke path (a 10 Hz worker adds at most one
    attempt per cooldown) and bounded so a genuinely unhookable game stops trying. A user-initiated
    enable calls it with `force`, skipping cooldown and cap. Recovery logs
    `hook RECOVERED on attempt N`.
  - **(b) One line per transition.** `ReportHookState` logs the fallback once when the hook goes
    down and once when it comes back, carrying the count of invokes that took the fallback meanwhile.
    The per-invoke `direct call inst=…` / `direct call success` INFO pair is gone (the exception path
    still logs unconditionally — that IS per-call news).
  - **(c) Worker invokes refuse the unsafe path.** `Tot::IsBackgroundWorker()` (the thread-local the
    M4 fix already set on every re-assert worker) gates it: with the hook down, a worker invoke
    returns **-8** instead of calling ProcessEvent off the game thread. `Schlacht::SetEnabled(true)`
    forces one hook attempt and, if it still isn't up, declines with **`STR_ERR_NO_HOOK` (-5)**
    without starting a worker that could only tick uselessly.
  - **UI.** `seethrough_set`/`get_state` now carry `hook_active`; the See-through card shows
    "Unavailable" + *"Game-thread hook unavailable … press Apply again to retry"* on a refusal, and
    a later Refresh CLEARS it once the hook recovers. Gated on the refusal CODE, not on `hook_active`
    alone — the hook installs lazily, so "not installed yet" is the normal state of a fresh session
    and must not read as a failure. Three tests pin exactly that (refusal / recovery / lazy).

  **Re-check in-game:** the failure is intermittent, so it may not reproduce. What to look for if it
  does: the log should show at most 8 install attempts (not one), a single fallback WARN instead of
  hundreds, and See-through should refuse with a visible message rather than silently doing nothing.
  > **Not reproduced on the build-2361 run (2026-07-24)** — the hook installed first try at the same
  > address (`0x1415968E0`) that failed on 07-23, which is consistent with "VM-layout accident, not a
  > property of the game". Zero fallback invokes, zero retries needed. Worth banking from the same run:
  > the post-install validator reported **`hook fired 10238 times in 1500ms`**, i.e. the pattern-based
  > vtable detection (`vtable+0x260`) landed on the RIGHT slot on Elliot — the failure mode recorded in
  > `feedback-pe-vtable-wrong` for ES2 / Geri.
  *Parent: Stark::InstallHook / Frieren::TryInstallGameThreadHook; log review 2026-07-23.*

-----

## Bookmarks + Options persistence + CE-export filter — follow-ups (shipped PR #359, builds 1652-1663)

The three persistence features (CE-export system-component filter, global panel-options persistence, per-game bookmark persistence) shipped + in-game verified (dev-log 2026-06-24). Deferred refinements, none blocking:

- **#3 — snapshot capture options: GLOBAL → per-game (peHash)** — Effort: **M** · Risk: med.
  The snapshot capture block (`GameOnly` / `AutoSkipNoise` / `IncludeNativeFields` / `SelectedScope` / `SelectedFamily` / `SelectedMaxDataset`) is persisted as a single GLOBAL default in `ui-options.json` to avoid a connect-time load race. Making it per-game (a `snapshots.{peHash}.options.json` sibling of the denylist, or a section in the bookmark/per-game store) lets `SelectedMaxDataset` track each game's size (the Avowed-6.7 GB driver). **Load it inside `SnapshotViewModel.SetEngineState` AFTER `_store.SetActiveGame(peHash)` and BEFORE `RefreshAsync`, under its own suppression flag** — NOT in `ApplyEngineState` (the original adversarial-review C2 finding: wrong-game bleed + save-storm if loaded at the wrong point). *Parent: #3 Options persistence, PR #359 (dev-log 2026-06-24).*

- **#3 — opt-in "resume where I left off" (view-state persistence)** — Effort: **S** · Risk: low.
  `SelectedTabIndex` + panel-collapse toggles (`CaptureSectionOpen` / `CompareSectionOpen` / `NoisePanelOpen` / `IsFunctionsExpanded` / Object Tree `IsCollapsed`) were deliberately EXCLUDED as transient view state. Some users want them restored. Add as an OPT-IN (a "remember tab + panels" preference) so the default stays clean. Lives in the existing `UiOptionsStore` (a `View` sub-object). *Parent: #3 Options persistence, PR #359.*

- **#3 — "Reset options to defaults" button** — Effort: **S** · Risk: low.
  Delete `ui-options.json` + re-apply model defaults to every VM (reuse `ApplyOptions(new UiOptionsSettings())`). One en.axaml string + a menu item (System tab or a small ⚙ on the toolbar). *Parent: #3 Options persistence, PR #359.*

- **#2 — CE-export filter: also skip system-component CONTAINER elements** — Effort: **S** · Risk: low.
  The "Skip system components" filter currently only covers pointer/struct fields (`PtrClassName`); array/map/set ELEMENTS whose element class is an engine asset (`KeyPtrClassName` / `ValuePtrClassName`, `LiveFieldValue.cs:60/66`) slip through because the container emitters don't route through the `EmitFields` depth gate. Add the per-element check in `EmitMapProperty` / `EmitSetProperty` / `EmitArrayProperty` (depth>1 only). The tooltip already states elements are not filtered, so this is additive, not a bug. *Parent: #2 CE-export noise filter, PR #359.*

- **~~#1 — bookmarks across a game RESTART (deeper-than-GWorld-root paths)~~ — DONE (build 1690, MERGED main PR #365 `2e54d86`, in-game VERIFIED).**
  Bookmarks now SPINE-re-walk on every load: `LiveWalkerViewModel.TryReresolveBookmarkSpineAsync` re-resolves the saved breadcrumb chain (stable field name+offset+deref/container kind) from a live anchor — GWorld (`WalkWorldAsync`) or GameEngine (`ResolveGameEngineAsync`) — rebuilding each crumb with a fresh address (`BreadcrumbItem` is init-only → `CloneCrumbWithAddress`). Container element hops (`[N]`) re-resolve via the same element-address math as the `Populate*ContainerFields` helpers. Any unmatched hop / null ptr / DataTable view / non-anchor root → return null → fall back to saved addresses (same-process fast path) + the existing `SavedClassName` staleness guard. **v1 safety property kept: never silently shows the wrong object** (name+offset match + class guard). `BuildBreadcrumbSpineFromPath` now threads `rootKind` so Locate-in-GameEngine bookmarks persist the right `"GameEngine"` anchor marker (adversarial-review finding). Remaining game-PATCH case (offsets shifted across builds): the **"import bookmarks from a previous build"** affordance (pick an older `bookmarks.{oldHash}.json`, re-resolve against the new build) is still open if users ask. *Parent: #1 bookmark persistence, PR #359.*

- **~~#1 — orphaned per-game bookmark files accumulate~~ — PARTLY DONE (build 2726); the age sweep is REJECTED, not pending.**
  The clutter half is fixed: `bookmarks.*.json` now lives in `%LOCALAPPDATA%\UE5CEDumper\Bookmarks\` (snapshot DBs moved to `Snapshots\` in the same change), with a one-time migration from the old flat root in the `BookmarkStore` constructor — so a patch-per-hash pile no longer buries `dll-path.txt` / `ui-options.json` / `experimental.json` at the root.
  **The "delete older than N days" half was considered and turned down** (maintainer call, 2026-08-05): `BookmarkStore` passes `maxAgeDays: 0`, which disables `AppDataFolderMaintenance`'s sweep outright. A snapshot DB is a regenerable multi-GB capture, which is what makes a disk-reclaiming sweep worth its risk; a bookmark file is a few KB of hand-placed navigation nobody can replay their way back to, so the two live in the same folder scheme with deliberately different retention. **Do not "finish" this by enabling the sweep.** A "clear bookmarks for all games" ACTION (explicit, user-initiated) is still open if anyone asks. *Parent: #1 bookmark persistence, PR #359.*

-----

## Related Objects panel — Phase 2 + follow-ups (Phase 1 shipped builds 1323-1327)

Phase 1 (the "Related" tab: given an actor, list Self/Class/Outer + Controller↔Pawn + owned components/ASC/AttributeSet via a depth-3 owned walk; 🌍 GWorld / Live Walker / finder / copy per row; 🔗 Related handoff from Instance Finder / Value Search / Live Walker) + the Instance Finder **"Newest first"** opt-in shipped builds 1323-1326. **In-game VERIFIED on TQ2:** `bp_ai_default_character_C` → Related lists 58 objects incl. `TQ2AIController`, `GrimAbilitySystemComponent` (ASC), `bp_tq2_character_stats_component_C` (AttributesComponent) → `AttributeSetHealth.CurrentHealth` = live HP (73.57). **Phase 2 (`Edel` current-target auto-detect) SHIPPED build 1400** (dev-log 2026-06-20) — `🎯 Detect target` button resolves GWorld→PC→Pawn, scores the player's outgoing object-ptr fields (structural is-Actor gate + keyword boost), auto-loads the top candidate; the `Edel` roster name is now 🟢. Remaining follow-ups, in order:

- **Phase 2 Edel — HALF VERIFIED (graceful fallback, Elliot 2026-07-23); the positive case is what's left** —
  Effort: **0** (verify only) · Risk: low. **The fallback works exactly as designed.** Two 🎯 Detect
  target runs on The Adventures of Elliot both returned `resolved=False candidates=8`: nothing was
  auto-loaded, and the ranked list was surfaced instead. The ranking is also sane — top candidate
  `BP_SupportFairy_C` (score 45, reason `is-Pawn`, reached via `BP_PlayerCharacter_C.SupportCharacter`),
  then `DefaultPhysicsVolume` (30, `is-Actor`), then a gameplay-cue actor. That top hit is the player's
  COMPANION, not a target — i.e. precisely the plausible-but-wrong pick that auto-loading would have
  gotten wrong, and the confidence bar correctly refused it. **Still open: the positive case** — a
  lock-on / soft-target action title where the target really does live in a `UPROPERTY`, to confirm the
  top candidate IS the focused enemy and its AttributeSet/HP loads. Tune the score constants only if
  such a game motivates it. *Original note:* Built + unit-tested + AOT-green but unproven live. Verify on a **lock-on / soft-target action title** (the target lives in a `UPROPERTY` object field): click 🎯 Detect target → confirm the top candidate IS the focused enemy and its AttributeSet/HP shows in the grid; confirm the 🌍 Locate-in-GWorld now resolves for that target (it should — the player references it). On the named JP/CN test games (TQ2/SEED/DQ7R, mostly no target `UPROPERTY`) confirm the **graceful fallback** fires (note = "no clear target / weak guesses", nothing auto-loaded) rather than feeding a wrong actor. Tune the score constants / keyword tables only if a real game motivates it. *Parent: Edel shipped build 1400, dev-log 2026-06-20.*

- **Locate in GWorld — streaming / World-Partition actors — the `ok_via_level` RECOVERY is still the
  unverified half (Elliot 2026-07-23 exercised the normal path instead)** — Effort: **0** (verify only) ·
  Risk: low. A 🌍 on Elliot **succeeded through the ordinary forward BFS**: `status: "ok"`, `found: true`,
  depth 5, 28 ms, 3,065 nodes visited, root `MainField_A2` — path `GWorld > GameState > PlayerArray[0]
  > PawnPrivate > SupportCharacter > DamageHit`. Worth banking: that request carried `deep: true` +
  `container_depth: 4` and the path hops **through a container ELEMENT** (`PlayerArray` ArrayProperty,
  `element_index: 0`), so the deep / container-element descent is verified live. What it does NOT
  exercise is `RecoverViaWorldLevel`: the target was reachable normally, so `ok_via_level` never fired.
  **To exercise it:** 🌍 on an actor the forward BFS cannot reach — a just-spawned or streamed-in enemy
  (the original Elliot "weapons map, enemies don't" case). Look for the status note "via the world's
  level list" and confirm the breadcrumb spine still drills to its HP. *Original note:* `Aura::RecoverViaWorldLevel` now recovers a `not_reachable` actor through its owning `ULevel` (reached by the `ULevel::OwningWorld` back-reference, since an actor's Outer IS its level), emitting `world →(WorldLevel)→ ULevel → Actors[k] → actor [→ target]` with status `ok_via_level`. This makes ANY actor that belongs to the current world locatable + navigable in Live Walker (and a bounded tail BFS reaches an owned AttributeSet/HP), regardless of how its level was streamed in — closing the Elliot "weapons map, enemies don't" case. **Verify in-game:** on a streaming/WP title, 🌍 on a just-spawned enemy now lands (status note: "via the world's level list"); confirm the breadcrumb spine reaches the enemy and you can drill to its HP. Two honest residual limits (acceptable, not bugs): (1) the chain is NOT a clean CE static-pointer chain (the world→level hop is a back-reference) — it's for in-tool navigation; (2) a truly unreferenced actor not in ANY world level still returns `not_reachable` (correct). Edel (build 1400) remains the complementary path when the player references the target. *Parent: Related Objects Phase 1 in-game test (dev-log 2026-06-19); recovery dev-log 2026-06-20.*

- **Locate from GEngine — alternate root for UI-widget / GameInstance-owned objects** — ✅ **DONE (builds 1542-1544, MERGED main PR #345 `f488592`; in-game VERIFIED)** — shipped as **Locate in GameEngine** (⚙ icon on all 10 🌍 surfaces). The existing `find_path_from_gworld` handler gained a `root_kind` field (`engine` → `rootObj = Genau::FindGameEngine().engineAddr`, `no_engine` when absent); `FindObjectGraphPath` was already root-agnostic so it was untouched. Reaches engine-layer objects (GameInstance / LocalPlayer / GameViewport / UMG widgets — the Octopath `PartyCharacterPanel_C` case) that no GWorld chain reaches; a deliberate complement to 🌍 (weaker for world actors, since `RecoverViaWorldLevel`/`ok_via_level` is World-root-gated). See dev-log 2026-06-22. *Residual: `deadline_ms` still hardcoded 20000ms in `FindObjectGraphPath` — optional follow-up.*

-----

## Multiple Values Group Scan — remaining phases (P1 shipped build 1276)

P1 (object-aware group scan, direct numeric leaves + one-level struct descent, exact-per-slot, mode toggle + master-detail UI) shipped builds 1276-1278 — new `Orden` SDR matcher. Follow-ups, in order:

- ~~**P1 in-game verification**~~ **DONE** — verified on SEED (UE4.27): single Value Search + Group Search both pass; Deep mode surfaces the buried `Tunes` block. *(dev-log 2026-06-18.)*

- ~~**P2 — prev-value per slot + offset-table**~~ **DONE (builds 1295-1302)** — per-slot scan type now on `Orden::SlotTarget` (`st`+`tolerance`+`targets2`, routed through `ComparePredicate`) + `RefineGroupCandidates`: First Scan takes Exact/Bigger/Smaller/**Between** per slot (Between = bounded-unknown entry, e.g. HP in [1,100]), Next Scan also takes the prev-value four (Changed/Unchanged/Increased/Decreased — compare each leaf vs its own previous round). Locked-offset table (`🔒 Class — Str@0x20, Def@0x24`) shows once all slots lock. **"Copy CE Script" / export is a deliberate WON'T-DO (not pending work)** — the owner exports the resolved chain from Live Walker (which already does it); do not re-add a group-side CE/export button. **prev-value group refine in-game VERIFIED on SEED** (Unchanged/Unchanged/Increased ran clean); Between first-scan live-verify still nice-to-have. *(dev-log 2026-06-18.)*

- **P3 — numeric containers as blocks — DONE (opt-in Deep, builds 1283-1285; scalar maps builds 1561-1562)**. The "Deep" toggle treats each numeric `TArray/TSet` + each struct-array/map element as its own block via the recursive `WalkContainerLeaves`, matching the group WITHIN one array (finds the SEED `Tunes[N]` case); single-value Deep forces the existing deep pass on all classes. The scalar-map follow-up added **scalar-valued + scalar-keyed maps** (`TMap<Name,int>` → value block `<map>.Value`, key block `<map>.Key`) by extending `ContainerCacheEntry` with `keyLeafType`/`valueLeafType` — closes the walker TODO **and** the Value Search "Proper scalar-map value/key capture" item (one shared fix); struct sides byte-identical, adversarial 4-lens review 0-confirmed. *Remaining (verify only):* in-game verify of the Deep path + a scalar-map (`Map<Name,int>`) game on SEED. *Parent: dev-log 2026-06-18 deep entry + 2026-06-22 scalar-map entry.*

- **Snapshot Group Match follow-ups — feature S1-S5 SHIPPED + MERGED main PR #348, in-game VERIFIED on SEED** (spec: [snapshot-group-match-spec.md](snapshot-group-match-spec.md); dev-log 2026-06-23). Remaining optional work, same `Orden` matcher: ~~**(1)** SPC Query "N-field intersection"~~ **DONE — SPC Group** (Single/Group toggle in the SPC tab; the N-snapshot, per-slot predicate-CHAIN generalisation of Snapshot Group Mode B; builds 1575-1584, in-game VERIFIED on SEED, dev-log 2026-06-23). Still open: **(2)** Class Pivot "co-varying tuples" (spec §3.1 row 3); **(3)** array-AS-BLOCK deep (each nested array its own block, like the live deep — both snapshot reuses shipped object-flat: array elements as the owner's leaves); **(4)** the snapshot-wide >2^53 double-precision limitation (carry exact target bytes per width like the DLL's `NumericTargetSet` — affects SPC/Diff/Pivot too, not just group match). All low priority — pick up if a real case motivates it.

-----

## Locate-in-GWorld — `IsGWorldAvailable` gate decouple (Value Search done, others pending)

- **Decouple the other panels' 🌍 from `IsGWorldAvailable`** — Effort: **S** · Risk: low. Value Search's per-row/per-slot "Locate in GWorld" was gated `IsEnabled="{Binding IsGWorldAvailable}"` (and the command short-circuited on it); on TQ2 (proxy mode) the flag read false even though GWorld was resolved, so the button was silently disabled (no `find_path` sent, no feedback). Fixed for Value Search (build 1311) by decoupling — the button is always clickable and the DLL's `find_path_from_gworld` (which returns `invalid`/`no path` with no live UWorld) is the source of truth. **The same gate still exists on Instance Finder / Snapshot / SPC 🌍 buttons** — apply the same decouple if a user hits it there. Open question worth a quick check: *why* did `IsGWorldAvailable` (an `[ObservableProperty]` fed from `state.HasGWorld`, which was true) evaluate false in the button binding on TQ2 — a binding-resolution quirk in the group RowDetails template vs a real state-propagation timing bug. *(dev-log 2026-06-18.)*

-----

## CE export drilldown — remaining gaps (Phase A/B/C shipped)

Phase A (CE XML/Field container-value expansion, build 1085), Phase B (CSX parity,
build 1098), Phase C (depth-from-current-view tests + CSX truncation note, build
1098) all shipped. Spec: [ce-export-drilldown-spec.md](ce-export-drilldown-spec.md).
Open follow-ups (low priority):

- **CSX struct-array element full re-walk** — Effort: **S** · Risk: low. CSX struct
  arrays still flatten the shallow Phase-F `StructFields` preview, so nested
  structs/maps *inside* an array element stay shallow. CE XML's
  `EmitStructArrayProperty` already re-walks each element via `resolvedStructs`
  (build 1076); mirror that in `ConvertArrayStructElementsToFields` (stamp
  `StructDataAddr` per element + route to `EmitStructPropertyFlattened`). The unified
  resolver already populates `resolvedStructs` for array struct elements — only the
  CSX emit path ignores it.
- **Nested-container truncation note** — Effort: **S** · Risk: low. The
  `⚠ Container element limit` note (CE XML + CSX) only scans top-level fields; a
  container clipped by `ArrayLimit` *inside* a drilled struct/pointer is unreported.
  Cheap: scan `resolvedStructs`/`resolvedInstances` values too. (Marked optional in
  the spec.)
- **FName → live readable string in CE via a "UE FName to String" custom type** —
  Effort: **M-L** · Risk: med (CE-Lua + per-game GNames config). Highest-value of the
  remaining sample.CSX gaps. Today FName is shown statically: **CSX** emits a raw
  8-byte qword (`MapCsxType` `NameProperty`→`8 Bytes` — no name at all); **Copy CE XML /
  Field** emit the 4-byte `ComparisonIndex` + a static `DropDownList` snapshot (index→string
  captured at export time, arrays only; single scalar FNames mostly show the raw index).
  sample.CSX instead uses `Vartype="Custom" Customtype="UE FName to String"` — a CE custom
  type (Lua, registered via `registerCustomTypeLua`) that resolves the FName index against
  GNames **live, inside CE, at runtime**, so any FName value updates to its current string.
  The exporter change is the easy 10% (emit `Custom`/`Customtype` for `NameProperty`, opt-in,
  keep DropDownList as fallback); the real work is **shipping + auto-configuring a GNames-aware
  FName custom-type Lua**: parse the pool block layout (UE4 `TNameEntry` vs UE5 `FNamePool`,
  stride/casing — knowledge already in the DLL's `Serie` module) and feed it the live GNames
  address. GNames must be **ASLR/restart-stable** → reuse the AOB / GWorld-anchor recovery from
  the Copy CE AA Script work (dev-log 2026-06-21). Benefits all three exporters (CSX gains names
  at all; CE XML/Field gain live resolution vs the frozen snapshot + single-scalar coverage).
  Decide: keep DropDownList as a no-setup fallback when the custom type isn't installed.
  *Parent: CSX 7.7+ Binary format + sample.CSX audit (dev-log 2026-06-21, PR #335); FNamePool =
  `Serie` module; GNames anchor = `project-aa-script-gworld-walk`.*

-----

## Teleport Coordinate Library — P1-P5 SHIPPED (builds 2257-2267), needs in-game verification

Design contract: **[teleport-coord-library-spec.md](teleport-coord-library-spec.md)**.
Write-up: [dev-log.md](dev-log.md) 2026-07-23. All five phases are on `dev`, 2777 tests green,
**zero DLL/pipe change**. What remains is verification that unit tests structurally cannot do.

> **User verification pass 2026-07-23:** the **DLL-flavour** emitted Lua **WORKS in CE** (picker
> opens, list + filter + teleport), **CSV export/import was exercised**, and the group/label round
> trip was driven from the Lua picker UI. Two results came out of it — the DLL flavour is verified,
> and the **no-DLL (standalone) flavour does NOT work on the tested title**. The remaining VERIFY
> rows are the ones that pass was not aimed at. *(Which title the standalone failed on still needs
> filling in here.)*

- **✅ VERIFIED — CSV export/import (2026-07-23).** Round trip exercised. NOT separately confirmed:
  the two deliberate hostile cases (a group named `1-2` that Excel mangles into a date; a label
  starting `=` surviving the formula armouring). Retry those only if a real library corrupts.

- **BUG / LIMIT — the no-DLL (standalone) flavour does not teleport on the tested title** —
  Effort: **M** · Risk: med. Confirmed by the user 2026-07-23. The spec already carries the caveat
  ("needs *UE5 Trainer: Setup* enabled first; may not visibly move"), so this is that caveat firing
  rather than a surprise: the standalone flavour writes the pawn's location RAW, and a game that
  re-asserts its own transform every tick simply overwrites it. **Decide between** (a) documenting it
  as a hard limitation of the no-DLL flavour (cheap, honest), or (b) having the standalone picker
  DETECT the snap-back — read the location back N ms after the write and, if it drifted back, say so
  in the status line instead of silently doing nothing. (b) is what stops the next user concluding
  the feature is broken. *Parent: P5; teleport-coord-library-spec.md §10.*

- **VERIFY IN-GAME — the teleport itself, from the APP (not the CE picker)** — Effort: **S** · Risk: low.
  Still open: save current pos → move → Teleport selected → land back, then the **map guard** (save on
  map A, load map B; plain Teleport must refuse and Force must be the only way through). Watch for
  `Tier == 2` (raw-write fallback) in the status line. *Parent: P1.*

- **VERIFY — DataGrid behaviour at scale** — Effort: **S** · Risk: med.
  The grid carries `MaxHeight="260"` precisely because `ContentRoot` is a vertically unbounded
  ScrollViewer and an unconstrained DataGrid would not virtualize. Load ~4 000 entries (import a
  generated CSV) and confirm scrolling and filtering stay responsive. Also measure where CE's
  ListView actually stutters — the picker's 2 000-row display cap is inherited from the reference
  table as an unverified guess. *Parent: P1 + P3.*

- **VERIFY — experimental gating** (DECIDED + implemented, build 2269) — Effort: **S** · Risk: low.
  The card is now gated on `ExperimentalEnabled` like the other five. Confirm the whole card
  appears/disappears with the System-tab checkbox, that it is absent from the tab's right-click
  quick-jump menu while hidden (the code-behind skips a card that is not `IsEffectivelyVisible`),
  and that toggling the gate off mid-preview clears a pending CSV/Lua import. *Parent: user call
  2026-07-23; spec §10.4.*

- **Unrelated finding, worth doing anyway** — Effort: **S** · Risk: low.
  `AobMakerBridgeService.WriteMessageAsync` (`:495-506`) has **no send-side size check**, and the
  plugin's oversize path (`pipe_server.cpp:61`) returns *without writing a response*, so an oversized
  push surfaces as a confusing "no response"/timeout instead of a size error. Add a client-side
  pre-flight check against the 10 MiB cap. A 4 000-entry library is ~480 KB so this is not urgent for
  the coordinate library, but it is the failure mode a user would hit first. *Parent: spec §10.6.*

-----

## Teleport — follow-ups (deferred / future research)

Teleport shipped (Wirbel, build 1027-1043). Works where the possessed pawn is
the visible character (SEED) and, via the deep-force, even on hard-cooked HD-2D
titles (Octopath — character moves). Open items, all per-game / research-grade:

- **Camera/POV doesn't follow on hard-cooked games (Octopath / SE HD-2D)** —
  Effort: **M-L** · Risk: med. **Read-only camera-POV display DONE + LIVE-VERIFIED
  builds 1110-1112** (Teleport tab → "Camera POV" → Get POV; `Wirbel::GetPov` +
  `teleport_get_pov` + a "Get camera POV" mailbox AA record). POV now **reads on
  all four tested titles** — getters on SEED / DQ III, and a fully-reflected
  `CameraCachePrivate.POV` raw fallback on TQ2 / Octopath (getters present but
  `ProcessEvent` returns nothing) — so you can *measure* the camera↔pawn delta.
  **That's the READ; the actual camera-FOLLOW-after-teleport fix below is still
  open** (POV read confirms the divergence but doesn't move the camera). Phase 2 ideas: FOV set/reset (`SetFOV`/`LockedFOV` is the only
  persistently-settable POV component); a "re-anchor camera" nudge after teleport
  (`SetViewTargetWithBlend(pawn,0)` + `SetGameCameraCutThisFrame()`). There is no
  universal Set POV — `UpdateCamera` overwrites it every tick. See
  [teleport-spec.md](teleport-spec.md) §15.
  The deep-force moves the pawn's root
  `ComponentToWorld`, but the camera tracks a separate child component
  (SpringArm / CameraComponent) or follow-camera actor whose world transform we
  never refresh — so the view stays put and can get **stuck unrecoverably**
  (no in-game event re-syncs the view-target chain; a save reload / area
  transition fixes it). Options, in order of cheapness: (1) invoke
  `APlayerController::SetViewTargetWithBlend(pawn, 0)` to re-anchor (likely also
  cooked out on these titles); (2) `APlayerCameraManager::SetGameCameraCutThisFrame()`
  for an instant cut; (3) deep-force the follow-camera component's
  `ComponentToWorld` too (need to *find* it — game-specific); (4) recompute
  child world transforms (manual `UpdateChildTransforms` — native, no
  reflection, hard). **Deferred**: no universal solution; a failed camera nudge
  risks making the stuck-camera worse. In-app disclaimer covers it.
- **TQ2 teleport — FIXED (build 1113); two minor caveats remain.** The old
  "separate visible actor" theory was **disproven** (build 1113 ViewTarget
  diagnostic): TQ2's pawn IS the camera view-target and owns the mesh + CMC. The
  failure was `K2_SetActorLocation` reporting success but not moving + a stale
  cached transform; fixed by always running `K2_SetWorldLocation` + deep-force in
  the CMC-freeze path. Marker teleport now works. Remaining: **(a) cursor teleport
  blocked** — TQ2 strips `GetMousePosition` (returns 0,0 / virtual cursor),
  `GetViewportSize`, and `KismetSystemLibrary`, so there's no generic way to read
  the cursor target (per-game RE only — low value, deferred); **(b) minor visual
  lag** — the mesh snaps over on the next move after a marker teleport (CMC network
  smoothing; a CMC smoothing-offset reset would fix it, Effort S, deferred). See
  [lessons-learned.md](lessons-learned.md) "TQ2 verdict".
- **Gamepad / mouse-extra-button hotkeys** — Effort: **M** · Risk: low.
  Marker hotkey capture is keyboard-only (`RegisterHotKey`). Mouse extra buttons
  + gamepad need low-level hooks / XInput polling ("record on all-released" per
  the user's spec). Deferred until requested.

-----

## Value Search — coverage + memory (build 923 plan)

Dependency order was **V3-A → V3-B → V1a → V3-C → V2**; **all shipped** (V3-C build 949:
DLL owns the set, UI is a server-side-filtered/sorted window; V2 build 954: ceiling
raised to 1M, sort/filter verified sub-second). Remaining open: **V1b** (container
prev-value refine) and **V1c live-verify**.

- **Deep Value-Search candidate → multi-level 🌍 drill** — ✅ **DONE build 1208.**
  Generalised `TryParseStructArrayInner`/`DrillToStructArrayInnerAsync` into
  `TryParseContainerPath` + `DrillDisplayPathAsync` (parse the full multi-`[N]`
  display path into ordered `(name,index)` segments; drill each as a container
  hop or direct-struct field; land on the final leaf). Wired into BOTH the VS/SPC
  `LocateInGWorldAsync` reach branch AND `NavigateToInstanceFieldAsync`
  (Open-in-Live-Walker — also fixes the offset-0 mis-select for deep candidates).
  Verified by a 4-agent audit (drill-sites + scan/capture correctness); the value
  was always FOUND — this was the land-ON-it polish. ⚠ in-game live-verify pending
  (multi-`[N]` 🌍 should land exactly on the SEED `...Tunes[N]` value).

- **Top-level `TSet<FStruct>` / `TMap<K,FStruct>` depth-1 inner leaves (Value Search)** —
  Effort: **S/M** · Risk: low. The static depth-1 collector (`collectStructArrayInner`)
  only covers `TArray<FStruct>`; the recursive `deepEmit` skips `depth<2` to avoid
  double-counting it. So the DIRECT fields of a struct element in a *top-level*
  Set/Map are scanned by neither path. (Nested ones — the SEED `MsTunes` case — are
  depth≥2 and ARE caught.) Fix: add a Set/Map analogue to `collectStructArrayInner`,
  or relax `deepEmit` to `depth>=1` for the Set/Map element side only. Audit #2.

- **SPC Strict-join `prop_offset` migration edge** — Effort: **S** · Risk: low.
  1-level struct-array element rows now store `prop_offset=0` (build 1205, was
  `nf.Offset`); a Strict-mode SPC query that mixes a pre-1205 and a post-1205
  snapshot keys the same logical field differently. Either zero `prop_offset` for
  array-element rows in the Strict key, or bump the schema to force recapture.
  Audit #4. *Cosmetic unless mixing snapshots across the 1205 boundary.*

- **Interesting Props: optional "Locate in GWorld" 🌍** — ✅ **DONE (builds 1531+, MERGED main PR #344 `86fb765`; in-game VERIFIED)** — added a leftmost per-row 🌍 icon column that resolves a live non-CDO instance of the row's class then calls `LocateInGWorldAsync(addr, 0, null, stopAtParent:true)` (the Interesting Functions handoff, gated on `IsGWorldAvailable`). Same PR added the prominent Live Walker failure banner. The panel also gained the ⚙ Locate in GameEngine button via PR #345. See dev-log 2026-06-22.

- **V1b — container prev-value refine (stable key)** — Effort: **M** · Risk: **high**.
  `Candidate.addr` stores a raw element address; TArray realloc already makes it stale,
  and TSparseArray is worse — freed slots get reused, so `c.addr` on refine may point at
  a different logical entry → Changed/Unchanged semantics silently lie. Store
  `container addr + slot index` as a stable key and re-walk the sparse array on refine
  (same idea as snapshot's `SelectArrayInnerKey`). **Do only if refine-on-container is
  actually requested.**
  *Parent: V1a TSet/TMap key|value scan shipped First-Scan-only, build 927 (dev-log
  2026-06-06).*

-----

## Experimental: Snapshot / SPC / Class Pivot

Gated behind the System-tab opt-in (`IExperimentalGate`). Design of record:
[experimental-snapshot-spc-pivot.md](experimental-snapshot-spc-pivot.md). Phases
0/A/B/C (C1+C3-lite+C4+C5+C6) + N1 noise picker all shipped; the engine rework
(in-memory hash-joins), heavy-query cancellation, persisted pivot index, and
Windows-only AOT backend all shipped (dev-log builds 805–923).

- **C2 — find-by-value locator + pivot handoff** — Effort: **M** · Risk: **med**.
  Closes the loop: locate which class/field holds a known value, then hand off into
  Class Pivot.
  *Parent: Pivot Phase C (C1/C3-lite/C4/C5/C6) shipped builds 830-877 (dev-log).*

- **A3c — CE .CT freeze-export from a diff/SPC/pivot hit** — Effort: **M** · Risk: low.
  Copy Address already covers the manual path; this is the full automated freeze export.
  *Parent: A3 diff engine + Copy Address shipped build 817.*

- **Heavier C3 scorer** — Effort: **M** · Risk: med. Jaccard stability + greedy compound
  key + class shortlist / volatility ranking (the "29i-3" scorer).
  *Parent: C3-lite key scorer shipped build 830.*

- **N1 v2 — per-`(class, prop)` deny granularity** — Effort: **M** · Risk: low. v1 is
  by-class; some classes (`ACharacter`, `APawn`) carry both gameplay fields (`Health`)
  and noise (`Velocity`, `LastRenderTime`). v2 would chevron-expand each Top-N row to its
  Top-K noisiest props. **Defer until v1 proves the bulk case is solved.**
  *Parent: N1 per-tab class denylist shipped builds 908-910 (dev-log 2026-06-05).*

- *(optional)* **`discrete`-style gzip blob storage** — Effort: M · Risk: low. Only if
  snapshot DB size becomes a real concern.

-----

## Bytecode cross-reference: property ↔ function — deferred follow-ups

Path 1 (BP Kismet bytecode) core + v2a, and Path 2 (native via Zydis/Denken) forward
direction, both shipped (dev-log builds 838-872).

- **Path 1 v2b — CFG-precise attribution** — Effort: **L** · Risk: **med-high**. v2a's
  "nearest entry offset" mis-attributes a sub-graph reached from multiple events via
  jumps; the `EX_Let*` write detector misses wrapped LHS (`Other.Field` / `Struct.Member`
  / `Arr[i] = x`). A real variable-length decoder (follow `EX_Jump` / `EX_JumpIfNot` /
  `EX_ComputedJump`, parse the LHS expression tree) fixes both. Reference:
  `vendor/RE-UE4SS/.../KismetDebugger.cpp` (`render_expr`) + `EExprToken`. **Only when a
  real mis-attribution motivates the cost.**
  *Parent: Path 1 + v2a shipped builds 838-861.*

- **Path 2 follow-ups** — (a) **reverse direction** (property → native funcs; needs
  disassembling every native function per query — expensive); (b) **SIB-indexed**
  `[reg+idx*scale+disp]` accesses (currently skipped); (c) **CFG-aware branch following**
  (only fall-through + direct call/tail-jmp followed today); (d) live tuning of the
  `this`-tracking + Func-offset detector across more games.
  *Parent: Path 2 native UFunction analysis shipped builds 862-872.*

-----

## Call-UE-function / invoke

- **#2 Live ProcessEvent Call Profiler** — ✅ **SHIPPED build 2109** (new `Linie` module +
  "Live Funcs" tab). Ranks UFunctions by **observed behaviour** (Start → perform action →
  Stop → see what fired), the root-cause answer for game-specific functions (OpenShop/Dash)
  name heuristics can't find. Hot-path gate is one relaxed `atomic<bool>` load when off (the
  map + mutex are only touched while recording — the recording-window mutex was accepted over
  a lockless counter per the plan; escalate to a sharded table only if an in-game benchmark
  shows contention). `pe_profile_start` forces the PE hook via `UE5_EnsureGameThreadHook`.
  **Remaining:** in-game acceptance test (shop/dash on a live UE title) + confirm nil overhead
  with recording off. See [dev-log.md](dev-log.md) build 2109.

- **#7 View Snap Hotkey (Property → snap-to-step)** — Effort: **S-M** · Risk: **low**.
  Bind a CE hotkey that snaps a Float/Double property to the next N° step (rotation
  snap) — generalises to MoveSpeed multipliers, zoom levels, time-dilation cycling.
  New row action + `SnapHotkeyDialog` + `scripts/ue5_snap_helper.lua` +
  `SnapHotkeyScriptGenerator.cs`. **95% mirrors the build-719 freeze Route B.**

- **Add-on: AA(Baked) "Auto-tick every N ms"** — Effort: **S** · Risk: low. Wrap the
  generated `invokeUFunction` call in a `createTimer(N, callback)` block ([DISABLE] tears
  it down); same per-script keyed handle table as FreezeScript. Lands as a 1-day add-on
  after #7 (both touch the script generator + dialog).

- **#5 v2 — ObjectProperty return resolution + recursive struct expansion** —
  Effort: **S + S**. (a) Resolve ObjectProperty/ClassProperty returns to "Name (Class)"
  via a DLL pipe round-trip (`resolve_object_name(addr)` or extend the invoke response).
  (b) Recursively expand nested structs (`FHitResult.Location` → its own FVector rows).
  *Parent: #5 structured-return DataGrid shipped build 775, PR #211.*

- **#0c FTransform Translation offset** — Effort: **S** · Risk: low. `VectorStructNames
  (FTransform)` returns empty → zero hits. Needs per-version Translation offset detection
  (UE4 / UE5-non-LWC at +16, UE5 LWC at +32).
  *Parent: Value Search Phase 2 vectors shipped build 757, PR #208.*

- **FString / FText / TArray input in baked AA Script** — Effort: **M** · Risk: **med**.
  Functions like `KismetSystemLibrary::PrintString` are observable side-effect verify
  targets but unreachable — the helper's `writeBakedParams` only handles scalar inputs.
  Needs CE-side buffer alloc + FString header write (ptr/count/max) + keep-alive + free
  in the cleanup timer. Same pattern for FText + TArray-of-scalars. (Open since the build
  643-644 ES2 live test.)

- **LiveWalker batch generator (v2 of the CT batch)** — Effort: **S-M**. Heterogeneous
  rows (functions + fields + struct sub-fields + array elements) + drilldown state — needs
  its own UX pass.
  *Parent: #3 multi-row → one .CT batch (Interesting Funcs/Props) shipped build 760.*

- **Dual-connection pipe (eliminate head-of-line blocking)** — Effort: **M** · Risk:
  **high** (in-process DLL concurrency). **POSTPONED 2026-06-01.** Full design:
  [multi-connection-pipe-proposal.md](archive/multi-connection-pipe-proposal.md) *(archived — superseded by [multipipe-eval.md](multipipe-eval.md))*. Engine-side
  concurrency is already safe (builds 792/793 + SessionManager); residual risk is Fern's
  accept/shutdown rewrite. Benefit is moderate (parallel scans already shrank the blocking
  window) — revisit only if "UI freezes during a big scan" becomes a real pain.

- **KismetMathLibrary stub-pattern UX hint** — Effort: **S** · Risk: low. On UE 5.5+
  cooked Shipping, `KismetMathLibrary::Add_IntInt` etc. consistently return 0 (cooker
  strips the `execXxx` thunk). Add a "Recommended verification targets" footer hint when
  the selected class is `KismetMathLibrary` / `KismetSystemLibrary`, and update
  lessons-learned / test-games. (Not a feature to enable calling them — a UX redirect.)

- **Mimic: zero the ReturnValue slot before invoke** — Effort: **S** · Risk: low. ES2
  showed Before/After dumps identical (stale `0x49`) so we can't tell "wrote 73" from
  "didn't touch ReturnValue". Overwrite the slot with a sentinel / zero before calling PE
  so the After dump is unambiguous. ~2-line patch in `Mimic.cpp` (both fast path + game-
  thread dispatch).

- **CE Lua AA Script activation hang — UX hardening** — Effort: **M** (mitigation) ·
  Risk: low. AA Script sometimes never reaches the mailbox (CE Lua froze or hid an error).
  Mitigations: re-arm helper-injected check on UI Connect; mailbox heartbeat `print()`
  before the write; early-exit `if not g_invokeMailbox then showMessage(...) end` in the
  helper. (UX hardening, not a correctness bug — we can't distinguish AA-error from CE
  freeze from the DLL side.)

-----

## Time / Timer control (Hemmung) — L1 + E SHIPPED and live-verified (builds 2151/2158); L2/L3 deferred

Eval memo: memory `project-timer-feature-eval`. Multi-agent + adversarially verified.
User ask = auto/manual-assisted discovery of game **time/timer components**, list the
**methods** handling them, real-time **lock/reset/adjust + multi-select**, and a
**cross-session persistence** path (Copy-CE-Field-like). Confirmed the DLL has ZERO
`TimeDilation`/`WorldSettings`/`TimerManager`/`CustomTimeDilation` refs today — new
capability, but every building block already ships. **Verdict: build in layers; the L2/L3
native-RE parts are exactly the reflection-invisible ones — cut them from v1.** Order below.

- **L0 — timer discovery docs recipe (ships today, ~0 code)** — Effort: **S** · Risk: low.
  BP-authored `Cooldown`/`RemainingTime`/`RespawnTime`/`Duration` floats are reflected
  UPROPERTYs → already found by Property Search (by name) + Value Search (by value; a
  ticking countdown survives repeated **Decreased** refines, count-up via Increased, paused
  via Unchanged). Lock via class-wide **Freeze** (`FreezeScriptGenerator`+`ue5_freeze_helper.lua`,
  restart+respawn-safe by class+offset re-enum); multi-select via **CheatTableBuilder** (a
  `List<CtPropertyRow>`, no builder change). Deliverable = a `docs/tips.md` recipe + a Group
  Scan example ({Elapsed↑, Remaining↓} in one object). *Parent: this eval.*

- **L1 — global game-speed control + Timing discovery category — DISCOVERY + DLL SHIPPED (build 2148); UI Time card (Part C) REMAINING** —
  Effort remaining: **M** · Risk: low. **DONE (build 2148, dev):** the `Hemmung` DLL module +
  `PropertyCategory.Timing` discovery category shipped and green (all 2453 C# tests + C++ self-tests).
  `Hemmung.cpp/.h` (roster 🟢) = absolute-value `Laufen` sibling: DIL_GLOBAL `AWorldSettings::TimeDilation`
  (GWorld→PersistentLevel→WorldSettings reflected chain + `Aura::FindInstancesByClass("WorldSettings")`
  fallback) + DIL_PAWN pawn `AActor::CustomTimeDilation`, write-on-drift re-assert worker, clamp
  [0.0,100.0]; exports `UE5_Set/ResetTimeDilation`, Mimic `CMD_TIME=15` (`TimeOp` SET/RESET, ufuncAddr =
  target), pipe `set/reset_time_dilation` + `get_time_state`. Discovery: `PropertyScoringTable.Timing`
  (append LAST → BuffDuration stays Combat) + `TimeStructTypes` (Timespan/DateTime/QualifiedFrameTime/
  FrameTime/Timecode) + `SeedQueries` timer terms + `ClassLocationScorer` GameplayEffect/GameplayAbility/
  WorldSettings +2 + function-side `UtilityKeywords` widen (Cooldown/Dilation/Delay/Interval/Elapsed/
  Recharge). Dev-log 2026-07-13; memory `project-timer-feature-eval`.
  **Part C UI Time card — DONE (build 2149; UI SUPERSEDED at build 2207 by the dual-row World+Player
  card — see dev-log 2026-07-15).** A "Time Dilation" card in the Teleport panel beside
  Move-Speed/Gravity: *as first shipped,* a "Player only" toggle (global `TimeDilation` vs pawn
  `CustomTimeDilation`), 0–3×
  slider + % + presets (Freeze/¼×/½×/1×/2×) + Apply/Reset/↻ + badge/readout; new `IDumpService`
  `Get/Set/ResetTimeDilation` (+ `TimeDilationKnob`/`TimeDilationSetResult`/`TimeState` models) + VM
  commands + en.axaml strings + 6 VM tests (2459 C# green).
  **CE Lua/.CT generation — DONE (build 2150).** `TimeDilationScriptGenerator` (mirrors
  `MovementScriptGenerator`): stateful `[ENABLE]`/`[DISABLE]` records poking `CMD_TIME=15` (op SET on tick, op
  RESET on untick); `CeLuaHygiene`-compliant; wired into the Teleport panel's "Add to CE" (2 records: World +
  Player) + "Save .CT" batch; 6 generator tests. So the dilation lock now works from a standalone CE table
  without the UI.
  **Persistence — DONE (build 2151).** (1) live read-back: `SetConnected` reflects the DLL's held dilation on
  connect + on target-switch (syncs the slider to the engaged value; `RefreshHeldTimeStateAsync`), disconnect
  resets the badge — the "state lives in the DLL, survives a UI reconnect" markers model. (2) disk preference:
  `TeleportUiOptions.TimeDilation`/`TimeTargetIsPawn` in `ui-options.json` pre-fill the last value+target
  across UI restarts (NOT auto-applied; live read-back wins). +2 VM tests + options round-trip.
  *(Those two keys were renamed `WorldTimeDilation`/`PawnTimeDilation` at build 2207 with no migration.)*
  **L1 COMPLETE + LIVE-VERIFIED on Elliot (UE4.27, build 2151)** — log confirms `set_time_dilation target=pawn
  value=0.5` → `hold 0.5000 (rc=0)`, held 0.5/1.0/2.0/1.4688×, reset clean, `get_time_state` polled on connect.
  Per-pawn `CustomTimeDilation` exercised; global `WorldSettings::TimeDilation` wired+unit-covered but not yet
  live-exercised (verify opportunistically). Also NOT built (deferred):
  `SetGlobalTimeDilation`/`GetTimeSeconds` invoke wrappers, and a dedicated opt-in function-side
  `FunctionCategory.Timing` bucket (timer methods currently land in Utility at weight 3 — below threshold
  without a class bonus / Show-All).
  **DON'T over-promise (adversarial corrections):** (a) locating the ACTIVE WorldSettings is
  NOT one line — `Aura::FindInstancesByClass` matches immediate class FName only (no `IsA`,
  `Aura.cpp:1365`), misses BP subclasses + can't disambiguate streaming/PIE sub-worlds →
  prefer INVOKING `SetGlobalTimeDilation` (calls `GetWorldSettings()` internally, price = its
  `[Min,Max]` clamp; a direct write bypasses the clamp but needs the right instance);
  (b) `Ubel` only READS today — a generic "write reflected float by name on object" surface is
  new; (c) paused worlds can't be stepped via dilation + active Sequencer flickers within the
  250ms drift window; (d) cross-game parity is CONDITIONAL — L1 inherits the tool's GObjects
  baseline burden on hard-cooked/SE-fork/encrypted titles, and bespoke non-UE time multipliers
  won't respond at all. Persistence (Copy-CE-Field-like), best→worst: class-wide **Freeze** >
  **GWorld-anchored AA Script** (`CeXmlExportService.GenerateGWorldWalkedSymbolXml`, `useAob`;
  registers a SYMBOL only → add a CE freeze or route TimeDilation through FreezeScriptGenerator
  with className=WorldSettings) > **StandaloneTrainer**; NONE survive a game PATCH; multi-select
  → CheatTableBuilder verbatim. *Parent: this eval; reuses movement-tuning-laufen + godmode-spec
  + autodetect-stats + standalone-trainer + aa-script-gworld-walk.*

- **L2 — GAS effect cooldowns (DEFER; feasible-with-caveats)** — Effort: **L** · Risk: high.
  Deep `UAbilitySystemComponent::ActiveGameplayEffects` walk: partly non-UPROPERTY
  (FastArraySerializer), remaining time is COMPUTED (`Duration-(Now-StartWorldTime)`, needs
  world time), version-fragile. Current ASC folding (P4 cross-object) reaches
  SpawnedAttributes/AttributeSets VALUES, NOT ActiveGameplayEffects. Only if a GAS title
  motivates it. *Parent: this eval.*

- **L3 — live `FTimerManager` timer enumeration (DEFER; research-grade native RE)** —
  Effort: **XL** · Risk: high. `FTimerManager` is NOT a UObject (FNoncopyable via native
  `UWorld::GetTimerManager()`); its `FTimerData` timers live in a TSparseArray+heap with
  handle indirection, none reflected. Per-version native layout (ExpireTime widened float→double
  in UE5; internals refactored across 4.x) + often-unresolvable C++/lambda callbacks =
  Avowed-packed-FUObjectItem-tier. A reflected BP `FTimerHandle` var is opaque (just an index)
  → does NOT yield remaining time. Recommend NOT in v1. *Parent: this eval.*

- **E — Linie cadence flag — DONE + LIVE-VERIFIED on Elliot (UE4.27, build 2158).** `Linie::Stat` Welford
  inter-arrival mean/variance (fed the timestamp `Stark.cpp:143` already reads, zero hot-path cost);
  `pe_profile_get` emits `mean_period_ms`/`cv`/`gap_samples` + logs the periodic candidates; UI
  `PeProfileEntry.IsPeriodic` (≥3 gaps, CV≤0.25, period out of the ~40 ms frame band, ≤30 s) → "Timer" badge +
  Period column + "Periodic only" filter (idle-window workflow). +12 tests. **Verified:** an idle recording
  flagged 3 periodic funcs out of ~90 (`BP_SupportFairy_C::TryAttackEnable`+`ExecuteUbergraph` @ ~325 ms
  cv 0.02 = a real ~3 Hz BP timer; `ProvideSingleActor` ~108 ms), Tick correctly excluded, stable across two
  windows. Native lambda/member-ptr timers bypass ProcessEvent (documented). *Parent: this eval; extended
  Linie/LivePEProfiler build 2109.*

-----

## MindsEye licensee fork — follow-ups (GObjects + GNames SHIPPED builds 2220/2238)

Both halves are live-verified end-to-end, but **on game version 7.3.1 only** (PE hash
`0863E3B90C993000`). Everything below was identified while shipping and deliberately not blocked on;
full context + the re-derivation playbook is in [mindseye-fork-notes.md](mindseye-fork-notes.md).
**Before touching any of these, check the PE hash** — if it moved, the constants come first.

- **Wide `FNameEntry` payloads are not de-obfuscated** — Effort: **S-M** · Risk: low.
  `Serie::GetString` applies the XOR only in the **ANSI** branch; the wide branch reads and
  `EncodeUtf16`s the raw ciphertext, which `IsImplausibleWideName` then rejects → empty name.
  `Genau::ObfChainOk` likewise bails on the first wide entry ("stop, do not judge"). The fork ships a
  wide twin de-obfuscator (RVA `0x0178B540` on the solved build, `add r8,r8` — i.e. the same key,
  applied over 2-byte units), so wide names **are** obfuscated and the key lookup already works for
  them. Low practical impact (most FNames are ANSI) but it is a real hole: any wide name silently
  resolves empty rather than wrong. Fix = mirror the ANSI XOR into the wide branch (per-byte over the
  UTF-16 payload) + let `ObfChainOk` corroborate on wide entries instead of aborting.
  *Parent: MindsEye GNames, dev-log 2026-07-19 (build 2238).*

- **Process-lifetime caches that no rescan clears** — Effort: **S** · Risk: low.
  Two function-local statics survive a full re-scan: (1) `Genau::TryObfuscatedPool`'s
  `s_ctxTried`/`s_ctxCache` — `FindGNames`' reset block clears `g_nameChunksOffset` /
  `g_namePayloadGap` / `g_nameKeyTableCtx` but **not** these, so **one failed AOB scan is sticky for
  the whole process** and a rescan never retries the key-table resolve; (2)
  `Flamme::IsExperimentalEnabled()` caches its answer, so toggling the UI experimental switch after
  the DLL is loaded has **no effect until re-inject** (arguably correct — but it is undocumented in
  the UI and reads as a broken toggle). Fix (1) by clearing the statics from the same reset block;
  for (2) either re-read on each query or surface "re-inject required" next to the toggle.
  *Parent: MindsEye GNames, dev-log 2026-07-19 (build 2238).*

- **No test coverage for any of the fork-specific paths** — Effort: **M** · Risk: low.
  `dll/tests/` holds only `dll_helpers_test.cpp` + `utf8_helpers_test.cpp`; nothing exercises the
  preset-bound `LayoutPreset::itemHint` gate, `TryObfuscatedPool`'s acceptance rule, or
  `Serie::LookupTagKey`'s open-hash probe. All three are **pure enough to unit-test without a game**
  (fabricate a synthetic chunk/pool/key-table in a buffer), and two of them encode load-bearing
  invariants a refactor could silently break: the item hint must stay **evidence-gated**
  (`hGood>=8 && hBad*4<=hGood`) so it can never win on a 50%-aliased stride-16 read, and the pool
  must be **REFUSED when the key table is unresolvable** even though block 0 decodes. Also worth a
  regression test: the tag→key cache publishes value+flag in **one** `std::atomic<uint16_t>` — the
  two-plain-stores version produced wrong keys across threads.
  *Parent: MindsEye, dev-log 2026-07-19 (builds 2220 + 2238).*

- **`find_anchors.py` from the re-derivation playbook is not committed** — Effort: **S** · Risk: low.
  [mindseye-fork-notes.md](mindseye-fork-notes.md) step 0 tells the reader to run
  `python find_anchors.py <exe>`, but `tools/pe/` only has `disasm_function.py` (which takes explicit
  VAs — it neither parses `.pdata` nor searches for `__FILE__` anchors), `minidump_triage.py` and
  `pe_imports_exports.py`. So the playbook's first two steps have to be re-scripted ad hoc, which is
  exactly the friction the playbook exists to remove. Either commit the script under `tools/pe/` (with
  a line in [tools/README.md](../tools/README.md)) or rewrite steps 0–2 against what is actually
  committed. *Parent: MindsEye docs, commit `8ef4a9f`.*

- **`GAP = 2` is hardcoded, so a second fork needs a code change** — Effort: **S** · Risk: low.
  The obfuscated-payload gap is a `constexpr int GAP = 2` inside `TryObfuscatedPool` ("the only forked
  geometry seen so far"). That is the honest call today — inventing a search over gaps would weaken
  acceptance for zero known benefit — but note it as the first thing to generalise if a second
  licensee fork with a different `FNameEntry` shape appears. Do **not** pre-emptively loosen it.
  *Parent: MindsEye GNames, dev-log 2026-07-19 (build 2238).*

-----

## Property scoring / discovery

- **Class Family Browser (Proposal C)** — Effort: **L** · Risk: **med**. New "Class
  Family" tab bucketing game classes by inferred role (Character / Pawn / Inventory /
  Stats / Save / Components / DataAssets / DataTables / GameMode) — the "I have no idea
  where to start in a new game" entry point. **NOT a jump-in-and-code task** — the
  classification heuristic + UI design needs its own planning round (cluster the dump
  corpus's BPGCs by property-name similarity first).

- **Proposal B — per-row "similar BP-added properties"** — Effort: **M** · Risk: low.
  **DEFERRED indefinitely.** When the user lands on `bCanBeDamaged @ AActor`, surface a
  side-panel of fuzzy-matched game-specific bools (`bIsImmortal @ BP_PlayerCharacter_C`).
  B' (the broad-sweep Interesting Properties) already covers the workflow — revisit only
  if a real user reports the specific gap B fills.

- **Runtime `keywords.json` override** — Effort: **M** · Risk: **med**. Let users tune
  the scoring tables without recompiling (source-gen JSON for AOT; hardcoded fallback;
  additive vs replace mode; "Export current tables to JSON" seed button). **Only if a
  user actually asks.**

- **More-genre dump coverage (calibration)** — Effort: **S** (mostly user-side). The
  corpus is heavy on JRPG/sim/ARPG/FPS/racing/sandbox; missing MMO/fighting/horror/RTS/
  sports-sim. Dump 3-5 games per genre → re-run `scripts/analysis/analyze_dumps.py` → PR
  keyword adds with evidence attached.

-----

## Carryover capability gaps

Pick up when the active plan finishes or when blocked.

- ~~**MulticastSparseDelegateProperty UE 4.23-4.27**~~ — **DONE for 4.27 (build 2399)**, and
  the plan that used to sit here was based on a false premise. It said UE4 needed a separate
  AOB plus a walker branch for an `FObjectKey {FWeakObjectPtr; int32}` (16B) outer key with
  stride `0x60 → 0x68`. The DropIn 4.27.2 PDB shows the outer key is a **raw `UObjectBase*`**
  exactly as on UE5, and `FObjectKey` is **8** bytes, not 16. No new stride, no key
  reconstruction: deleting the `UEVersion < 500` gate was the entire fix, and `SPARSE_ES2_1`
  already resolved correctly on 4.27 (2 extra 4.27-verified patterns added anyway).
  **Remaining — narrowed 2026-07-29.** 4.23 is no longer unsampled: the self-built
  `UE4.23-Flying` oracle PDB-confirms the outer key is a raw `UObjectBase const*` at the very
  version sparse delegates were INTRODUCED, character-identical to 4.24, and `SPARSE_DI427_1`
  resolves it live. Combined with 4.24/4.25/4.27 that leaves **only 4.26** without a symbolised
  monolithic sample of its own (the 4.26 Satisfactory rows are modular DLLs and do carry the
  symbol). The key shape is now measured at every version the feature has ever had, so this is
  redundancy rather than a gap; the walker's runtime key-shape probe remains the real mitigation
  and is what covers licensee forks no sample can.

- **Find Refs v4 — TMap / TSet weak-like inner sides** — Effort: **M** · Risk: **low**.
  Currently Object/Class only; weak/soft pointer collections (`TMap<UObject*,
  FWeakObjectPtr>` etc.) silently miss target hits. Reuse the v3 weak-resolve helper
  inside the existing TMap/TSet walkers in `Aura::FindReferencesToUObject`.

- **FieldPathProperty drill-down + Find Refs** — Effort: **M** · Risk: **low**. Last
  remaining no-handler property type. Rare in shipping games (only Editor-derived
  classes) — genuinely low priority.

- **GWorld coverage** — Effort: **S each** · Risk: low. Two remaining titles:
  - **Star Wars Jedi: Survivor** (UE 4.27?) — untested; needs an AOB sweep + result triage.
  - **Satisfactory** (UE 5.3, modular DLL build) — (1) proxy DLL injection fails (loader
    bypasses normal proxy hooking; workaround = CE manual injection); (2) GWorld pattern
    likely lives in `CoreUObject-Win64-Shipping.dll` — adapt `Genau::FindAll` to scan
    multiple modules when the primary scan fails.

- **`kPublishers[]` table additions** — Effort: **S each** · Risk: **high** (if added
  casually — wrong publisher bias overrides correct detection). Only add a publisher with
  ≥3 misdetected titles AND a clear pattern. Wait for real misdetection reports.

- **UE 6.0 readiness — version-string map entry + remote-object watch (do only with a real UE6 binary)** —
  Effort: **S** · Risk: low. UE 6.0 is **layout-identical to UE 5.8** across every structure the dumper
  reads (verified `origin/5.8..origin/ue6-main`, 2026-06-30 — see [technical-notes.md](technical-notes.md));
  the core walk + AOBs are already UE6-ready, nothing to implement now. Two small, deferred items:
  (1) **Version-string map** — `Genau.cpp:2159` tops out at `{"5.8.",508}`; no `6.0.`→600 entry, so UE6
  games fall to the bias fallback (dynamic detection still works, so this is detection-clarity only).
  Adding `{"6.0.",600}` needs a `kVersionDetectLogicRev` bump (forces a one-time re-detect of all cached
  games) **and** care vs game-version strings like "6.0" (mirror the "15.6.0" guard at `Genau.cpp:2221`).
  (2) **UE6 AOBs** — add UE6-specific AOBs only against a real binary; our AOBs wildcard displacements and
  resolve the pointer, so the 5.8/6.0 reordered fields are handled post-resolve by the existing "UE5.8"
  preset. `StaticAllocateObject` gained a `UObject*` param (body changed) but the GObjects AOBs target the
  `mov reg,[rip+GUObjectArray]` sites, not that prologue. **Watch-item (far future, not shipping-default):**
  `UE_WITH_REMOTE_OBJECT_HANDLE` (experimental multi-server / UEFN remote objects, OFF in normal shipping)
  inserts `FRemoteObjectId` into `UObjectBase` (between `InternalIndex` and `ClassPrivate`) and `FUObjectItem`;
  if a UE6 game ships it ON, the hardcoded `OFF_UOBJECT_*` offsets shift by `sizeof(FRemoteObjectId)` and
  FUObjectItem packing is forced off — a real handler branch would then be needed.
  *Parent: UE6-vs-5.8 parity audit (2026-06-30); per-structure detail in technical-notes.md.*

-----

## CE Lua — two Teleport-row defects left open by the build-2743 sweep

Both were found by the audit behind [dev-log.md](dev-log.md) 2026-08-06 (build 2743) and
deliberately NOT fixed in it: each needs a product decision, not a mechanical change.

- **`Get camera POV` and `Get current coords` display nothing by default.** Effort **S** · Risk low.
  Both format their numbers only through `dbg(...)`
  ([`TeleportScriptGenerator.cs`](../ui/UE5DumpUI/Services/TeleportScriptGenerator.cs), the `op == 11`
  and `op == 0` blocks), which is silent at the shipped `DEBUG == 0` — so the two rows whose ENTIRE
  purpose is to show a number show nothing and then auto-close the window. **The decision:** a bare
  `print()` reopens the Lua Engine window this project works hard to keep shut; `showMessage` is
  modal and awkward to copy from; writing the value into a CE memory record is the most CE-native
  but needs a record to write to. Pick one before coding.

- **`Clear all markers` can raise three dialogs for one click.** Effort **S** · Risk low. The
  busy/timeout `break` lands inside the idle-wait / status `while`, not the `for slot = 0, 2` loop,
  so after a wedged mailbox on slot 0 the loop still runs slots 1 and 2. `hadError` does correctly
  suppress the false "all markers cleared" line and the auto-close, so this is UX cost rather than a
  correctness lie — which is why it was left. Fix = a flag checked at the top of the slot loop.

-----

## CE Lua — `executeCodeEx` follow-up (CE source audit 2026-08-11)

The two defects this section opened — `ue5_dissect.lua`'s infinite timeout and every call site
discarding CE's reason string — **shipped**; see [dev-log.md](dev-log.md). CE-side detail lives in
[ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §13. One judgement call is left:

- **Decide whether 5 s is the right freeze ceiling.** Effort **S** · Risk low — INFO, not a bug.
  `DllCallTimeoutMs = 5000` is not merely a timeout: because the wait cannot be pumped, it is a
  **hard ceiling on how long CE's GUI can be frozen**, and its XML doc previously implied only the
  infinite case hangs the UI. The doc comment is now corrected; the *value* is still unexamined.
  Nothing measured says 5 s is wrong — this is a "we now know what the number means" item.

-----

### 🔴 NEW 2026-08-14 — DumperTest cannot currently detect ANY of the audit #5 cluster ① fixes

**Checked, not assumed.** The sample's four containers are `TSet<int32>`, `TMap<FName,int32>`,
`TMap<int32,float>`, `TArray<FDumperTestStat>`. Working the arithmetic for each:

| Sample container | pairAlign | unpadded pair | old stride | new stride | discriminates? |
|---|--:|--:|--:|--:|---|
| `TMap<FName,int32>` (non-CPN) | 4 | 12 | 20 | 20 | ❌ identical |
| `TMap<int32,float>` | 4 | 8 | 16 | 16 | ❌ identical |
| `TSet<int32>` | — | — | 12 | 12 | ❌ TSet is unaffected by design |
| `TArray<FDumperTestStat>` | — | — | — | — | ❌ not a sparse container |

**The sample has exactly the blind spot the unit tests had** — every pair is either 4-aligned or
already a multiple of 8, so nothing discriminates. It also cannot reach MG2/A2: the containers hold
**3 entries each** and nothing is ever removed, while A2 needs **>128** entries (the `TBitArray` heap
spill) and MG2 needs a removal.

`FDumperTestStat` does not help either: it carries an `FText` (a `TSharedRef`, so 8-aligned), which
is exactly the case the size guess gets *right*.

**Add these five properties** (the arithmetic each one is chosen to expose):

- `TMap<int64,int32> Map_I64ToI32;` — pairAlign 8, unpadded 12 → **old 20 vs new 24**. The core MG1
  witness. `int64` key rather than `UObject*` so there is no lifetime/GC variable in the test.
- `TMap<FString,int32> Map_StrToInt;` — unpadded 20 → **old 28 vs new 32**. A second MG1 witness with
  different arithmetic, so one wrong assumption cannot pass both.
- A deliberately **4-aligned POD** struct (`USTRUCT FDumperTestVec3f { float X, Y, Z; }` — no FText,
  no pointer, no double) + `TMap<int32,FDumperTestVec3f> Map_IntToVec3f;` — **MG3**: the size guess
  says "≥8 ⇒ align 8" and puts the value at +8 where it really sits at **+4**, so *even element 0* is
  wrong. This is the only shape that exercises the `UScriptStruct::MinAlignment` read. It doubles as
  **A4**'s target (a scalar leaf inside a map's struct side).
- `TSet<int32> Set_Big;` populated with **200** entries, then `Remove()` of a **low** index (< 128)
  at BeginPlay — **A2** (post-spill stale inline bits: the freed low slot must not appear) and **MG2**
  (header count must equal the rows rendered).
- `TSet<FDumperTestVec3f> Set_Struct;` — **A4**'s set side.

**U2 cannot be covered this way.** `WITH_CASE_PRESERVING_NAME` is an engine build flag, not a project
property — it needs either a custom engine build or a real CPN title (Titan Quest II, UE 5.7).

**Where to edit.** `tools/ue-sample/DumperTest` is the source of record and holds **only** the
`DumperTest*` actor/types sources; the live project `D:\Unreal Projects\DumperTest` additionally has
the project scaffolding the repo does not track (`.uproject`, `Config/`, `Content/`, `Plugins/`,
`DumperTest.Build.cs`, the module `DumperTest.cpp/.h`, `DumperTestCharacter.*`). So syncing is a
**file-level copy of the tracked sources**, never a directory overwrite — the latter would drop the
scaffolding. Edit the repo copy first, then copy those files across and rebuild.

*(Checked 2026-08-14: the two copies differ by **line endings only** — `diff --strip-trailing-cr`
reports them identical. Nothing has desynced.)*

-----

## ✅ DONE 2026-08-17 — `pending-verification_zh-TW.md` rewritten as an OPERATIONAL checklist

*Effort M / risk low. Docs only, no code. **Rebuilt from scratch 2026-08-17** — 60 items, grouped by
what they COST to run (第 0 步 = needs nothing, through 第 5 步 = no sample exists), each a
`| # | 做什麼 | 預期 |` table and nothing else. Kept below as the contract for anyone editing it.*

⚠ **It is GENERATED-shaped, not hand-maintained prose.** When an item here changes, change the step
and the expected result over there — do not re-introduce narrative. When an item is CLOSED here,
delete the whole section there; that file holds only outstanding work.

**It is ~430 builds stale, measured not guessed.** Its own header says
*"目前狀態（2026-08-12，build 2804）"*; the tree is past 3237, and **86 finding IDs present in this
file are absent from it entirely**. It is stuck in the audit-#4 era (B4 / B29 / B18 / B19 / B10 /
B28 / B8) while this file is now almost all audit #5.

**What the maintainer said it is FOR** (2026-08-17, and it changes the shape): it exists so they can
see **how to operate** in order to confirm a bug is fixed, or to sanity-check. So:

- **Steps and expected results ONLY.** No background, no root cause, no "why this matters". This
  file keeps the reasoning; that one keeps the hands.
- ⚠ **Do NOT re-translate this file's ~2,800 lines.** That is precisely how it drifted the first
  time and it would drift again within days. A compact operational index is the deliverable.
- **Priority rule:** anything with **no environment to test on** ranks LOW even when the register
  says MED — e.g. the case-preserving-FName (CPN) work, population zero of 30+ tested games. The
  absence is itself the signal that such games are rare.
- Keep the "第 0 步" idea that CLAUDE.md credits it for: the checks that cost nothing from an
  ordinary session, first.
- This file stays canonical (CLAUDE.md's rule) — edit here first, then mirror.

-----

## Pending live-game verification (verify only — no code)

> **Session evidence tag `[ELLIOT-2026-08-16]`.** Three launches of **Elliot** (`Elliot-Win64-Shipping`,
> UE5.4 runtime-reconciled, PE `6A577F4E1D91B000`, 482,390,784-byte image) on 2026-08-16 — 20:12
> (`scan #1`, cold), 20:26 (`scan #4`) and 20:49 (`scan #7`, build **3127**). **This is the most
> productive single evening on this register**, because Elliot is the *stripped-PE-version* title
> that DSA structurally could not stand in for, and because three launches of one binary give
> cold-vs-warm pairs with **two unhinted targets acting as a built-in negative control**. It closes
> G10 steps 1 and 3, G8/G9 steps 1 and 2, G11 step 1, and G2 step 1 — and it produced one honest
> non-result: G2's speed is real but **2.4 s, not the predicted sub-second** (step 2, with a lead).
>
> **Session evidence tag `[DSA-2026-08-16]`.** A real session on **DragonSword Awakening**
> (`DSClient-Win64-Shipping`, UE5.4, PE `691B0D9809EB2000`) under **build 3122** settled a few steps
> below; each is ticked in place and tagged, so grep `DSA-2026-08-16` for everything it covered.
> **Read what it did NOT reach as carefully as what it did** — the session detected its version from
> the intact **PE VERSIONINFO**, so the whole memory-string tier ladder (G2's 29 s sweep, G8/G9/G11's
> tier rules) was never entered, and it resolved offsets via `Guid`, so G12's actual repaired branch
> was never entered either. A green session is not the same as an exercised code path.

> ### 🔄 MIRROR RECONCILED 2026-08-19 — `pending-verification_zh-TW.md` pruned 46 → 40
>
> The maintainer worked the 繁中 checklist off a NAS copy and ticked **18 individual step rows** across
> four items; those ticks were folded in above (`AC1`, `AE4–AE7`, `A6`, `AD4`). A second, independent
> sweep then compared **every** 繁中 item against this register. **Six sections were removed from the
> mirror; three more were kept after the "closed" claim was refuted.** Recorded here because the
> mirror carries no history of its own — deletions there are only auditable from this side.
>
> | 繁中 item removed | ground | evidence in this register |
> |---|---|---|
> | `AC1` | ticked **6/6** *and* already closed | `✅ ALL SEVEN STEPS CLOSED 2026-08-17 [AC1-UI-2026-08-17]` |
> | `W2 / W3` | drift | `✅ UI HALF NOW CLOSED 2026-08-17 [SDKHDR-UI-2026-08-17] — all three checks pass`; that WAS the mirror's only remaining step |
> | `D2` (Group Scan scalar) | drift | `✅ Step 4 SETTLED 2026-08-17 [D2-UI-2026-08-17] — but its PREMISE was wrong`; step 4 was the mirror's only remaining step |
> | `AB1 / AB2` | drift | `✅ 5-of-5 CLOSED 2026-08-18`. Its APC sub-step is **not** outstanding — it is `⛔ CANNOT BE RUN ON A PUBLIC CHEAT ENGINE`, now carried as a D_MANUAL row in the session plan |
> | `D2`（樣本心跳） | drift | `✅ D2 (樣本心跳) PASS 2026-08-17 [GRP4-UI-2026-08-17]` + `✅ VERIFIED 2026-08-12` (all five HUD lines in the Shipping package) |
> | `G8 / G9` | drift | the mirror's single remaining step is this batch's **step 3**, `✅ PASS [DQ7R-PIPE-2026-08-17]` with the log quoted. ⚠ **The mirror also prescribed the WRONG HOST** — it said Elliot, which step 3's own correction shows can never emit a Tier 1 line |
>
> ⚠ **Two stale blockers died with those rows and must not be re-copied anywhere.** Both `W2/W3` and
> `D2` carried *"卡在 UE5DumpUI 無法授權給 computer-use"*. That is false since the all-users Start-Menu
> shortcut landed — the SDK-header export and the `Leaves/slot` control were both driven on the AOT
> `dist` binary. Any row still citing that blocker is out of date.
>
> ⚠ **Three sections were KEPT because the sweep's "closed" verdict did not survive checking** —
> logged because the cost of getting this wrong is deleting verification nobody has done:
> * **`U16`** — the parent is `✅ DONE 2026-08-18`, but step 5 inside it is explicitly **🟡 PARTIAL**:
>   *"the largest table seen is 26 entries"* and *"the CE DropDownList half was not checked"*. Those
>   are precisely the mirror's remaining step 1. Only its step 2 (the `walk-0.log` grep) is discharged.
> * **`U3 / U17`** — the `✅ CLOSED 2026-08-17` covers steps 1–2, which the mirror had **already**
>   dropped. What it still lists is steps 3 (a UE5 **LWC** 24-byte `FVector` title) and 4 (the **GAS**
>   control); the closure ran on `Map_IntToVec3f`, a 12-byte float vector, so it cannot stand for either.
> * **`D2`（顯示配對） — the closest call.** `✅ VERIFIED in-game 2026-08-05` does cover the filter
>   pairing and `All fields` open/collapse, but the mirror's **step 1** (a non-zero default pair) was
>   *generated by* that session as a complaint and fixed **after** it — the write-up records the fix,
>   not a re-run — and its **step 4** (Live / Addr / Pivot / Locate off each leaf) appears only as a
>   design claim (*"act on it unchanged"*), never as an observation.
>
> **The mirror's own count table was re-derived, not edited by hand**: 第1步 1 · 第2步 16 · 第3步 8 ·
> 第4步 13 · 第5步 2 = **40**. CLAUDE.md's row said *63* and the session plan said *64 rows*; both were
> stale and are corrected in this commit.
>
> ⚠ **That `40` is a snapshot of that reconciliation, not a running total.** Audit **L10** added five
> items the same day and the four `[TAG]` items below were mirrored on 2026-08-19, so the mirror is
> larger now. **Never read a count out of this block — derive it**:
> `grep -c '^### ' docs/pending-verification_zh-TW.md` minus the two `###` under 「怎麼用這份清單」.

### ⛔ PRECONDITION FOR EVERY GAME ROW — as of 2026-08-19, ALL NINE deployed proxies are STALE

Measured with `tools/verify/proxy_refresh.py report` (build 3263, `dist/proxy` = dinput8 2,875,904 /
dxgi 2,876,928 / version 2,882,560 / winmm 2,889,216):

| game folder | proxy | deployed size | |
|---|---|---|---|
| EVERSPACE 2 · EVERSPACE · DQ7R · Lushfoil · Manor Lords · The Artisan of Glimmith | `version.dll` | 2,860,544 | stale |
| OCTOPATH TRAVELER | `winmm.dll` | 2,867,712 | stale |
| Avowed · Elliot | `dxgi.dll` | 2,855,936 | stale |

**9 deployed, 9 stale.** This is not cosmetic. A proxy auto-loads at game start and **owns the
pipe**, so injecting the current DLL afterwards is a no-op — the second instance logs
`pipe already exists (another UE5Dumper instance running) — skipping auto-start` and
`LoadLibraryW` merely bumps a refcount. Everything measured is then the OLD binary, silently.
`PipeClient.assert_build()` does catch it, but only after the launch has been spent.

⇒ **Refresh before measuring**: `py tools/verify/proxy_refresh.py refresh "<folder substring>"`,
which backs the old file up to `out/proxy-backups/` with size + SHA-256 verified before it
overwrites, refuses while the game is running, and refuses a needless write when already current.

⚠ **Correction to an earlier note: Avowed IS installed** (`…\common\Avowed\…\Avowed-Win64-Shipping.exe`).
It is simply absent from the Start menu, so `request_access` cannot resolve it and it is not
grantable for computer-use — but it is perfectly usable for any headless pipe/log row, which
matters for `A8` (flat-array CE pointer info) and `AA38` step 4's neighbourhood.

### ▶ HOW TO ENUMERATE THIS REGISTER — one invariant, and it is grep-able

**Every item's ID must appear in a `^###` (or `^####`) heading of this section.** A heading-level
scan is how anyone picks the next thing to run, so an item whose ID lives only in body prose is an
item that gets double-run or forgotten. Enumerate with:

`grep -n '^#\{3,4\} ' docs/todo.md` — then keep the lines that fall inside this section.

⚠ **`> ###` lines do NOT count, deliberately.** A blockquoted heading is an *evidence* sub-block — a
session result, a trap, a refutation — hanging under a real item, and `grep '^### '` cannot see it.
There are many and that is fine; what must never happen is an ITEM being introduced by one.

**Two blocks were violating this until 2026-08-19** and are the reason the rule is written down: the
build-2830 container group (**MG2 / TSet+UDataTable / U2**) sat under the `[SDKHDR-2026-08-18]`
heading, and the whole **"Shipped + unit-tests-pass but unproven on real games"** long tail sat under
`[STALEDLL-2026-08-18]`. No heading anywhere named a single one of their checks. Both now have their
own headings, and the headings that owned un-named items (the fourteen-MED batch, audit #4 ① and ②,
audit L10) carry their IDs. **Measured, not asserted:** cross-checking every ID that owns a 繁中
section against the register's `^###` headings went from **40 un-findable to 0**.

⚠ **Re-checked 2026-08-19 (closing sweep) and it had already sprung two small leaks**, both of the
same shape the rule forbids: two `### ⬜ Original checklist (kept for the steps)` blocks named no ID
at all, so a heading-level scan could not tell you *whose* checklist they were. They now read
`### ⬜ AE2 / AE3 — original checklist …` and `### ⬜ Y9 — original checklist …`, matching the
`U3 + U17` block that already had it right. **Re-derive with the two commands below and expect
`40` and `0`** — a machine check, since this is the second time the invariant drifted:

```
awk '/^## Pending live-game verification/,0' docs/todo.md | awk '/^## /&&!/^## Pending live-game/{exit}1' | grep '^### ' | grep -c ⬜
```

⚠ **Two of those 40 hang under a parent that is already `🟡` or `✅`** (the two just renamed). They
are kept `⬜` deliberately — losing a live check is worse than over-counting by two — but a
`🔲`-marked sibling (`U3 + U17`) shows the other convention exists, and `🔲` is **not** counted by
the command above. Reconciling the three is a maintainer call, not an agent one.

⚠ **Un-mirrored `[TAG]` items — a known, tracked gap.** `PIPEBUSY` / `CLASSTOTAL` / `PROXYLOAD` /
`SLOTSYM` were mirrored into 繁中 on 2026-08-19. Still un-mirrored: `STALEDLL` (b), `FREEZESTUCK`,
`PASTECRASH`, `FREEZESCOPE`, `PEHOOK`, `PEHOOKONCE`, `SDKHDR`, `CONTAINERCAP`. They are **not**
exempt — `AUTOREFRESH` is already a full 繁中 section — they are simply behind. Mirror each as it is
picked up.

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L12 (INFO tier): MB3 / AC13 / AC14 / AC15 / AC17 / AE27 / AF25

*L12 closed **25 of the 26 INFO rows**; only these seven changed runtime behaviour. The other
eighteen need NO live check and are deliberately not listed: **AD23 / AB22 / AE28 / AF27 / Z17** are
comments only; **AD24 / AD25 / AD26 / AD27** are pinned by the C++ suite (258 utf8 + 1603 dll
assertions, AD25 negative-controlled); **AA35** is covered by the offline Lua rigs (83 / 154 / 91);
**AA36** touches only a CI checker and is proven by seven negative controls; **AE29** deletes a
method with zero callers; **AC16 / AB21 / AF24 / AF26 / AE26** are re-verified negative results with
no code change; **AB23** was not fixed (see its row). Categories are §10's A/B/C/D.*

⚠ **MB3 is the row to run FIRST.** It restructures the CE mailbox poller — the loop every `.CT`
command rides on, inside the game's own process — and **no test target compiles `Mimic.cpp`**, so
none of it has executed. Two changes: the dispatch `switch` now runs inside
`Routine::RunTickGuarded` so one throwing handler loses that command instead of ending the mailbox
for the session, and `CompoundOpGuard`'s destructor now detects unwinding
(`std::uncaught_exceptions()` vs the count at entry) and publishes `-11` instead of the stale
`result` — which for `HandleInvokeByName` was normally **0**, i.e. it reported SUCCESS for a command
that threw. **The regression risk is not the throw path (hard to trigger) but the ORDINARY path**:
if the lambda refactor broke plain dispatch, every CE command breaks at once. So the check that
matters is simply "do normal mailbox commands still work".

| # | ID | cat | what to do | expected |
|---|---|---|---|---|
| 1 | `MB3` | **B** | Inject, then run any two `.CT` rows that use the mailbox (Teleport save/recall, and an Invoke). Cheaper first step: `tools/verify/mailbox_addr.py` resolves `g_invokeMailbox` with **no CE**, so a scripted poke of one command is category **A**. | Both succeed exactly as before. `pipe-0.log` / `init-0.log` show no `Mailbox: tick threw` and no `result=-11`. A `-11` with a message means a handler really did throw — capture the log, that is a genuine find. |
| 2 | `MB3` | **C** | The throw path itself. Needs a handler that actually throws — no way to force one on demand today. | If it ever fires: the mailbox keeps polling (subsequent commands still work) and the script reports `-11` + "the operation did NOT complete" rather than hanging at `status=PROCESSING`. |

> ### ✅ MB3 — THE ORDINARY PATH PASSES 2026-08-19 `[MB3-POKE-2026-08-19]`, no Cheat Engine involved
>
> The row says the risk is **not** the throw path but plain dispatch: "if the lambda refactor broke
> plain dispatch, every CE command breaks at once". That is exactly what was tested, and it turned
> out to be **category A, not B** — `tools/verify/mailbox_poke.py` drives the mailbox from Python.
>
> **50 consecutive dispatches, 0 failures** (`--repeat 25`, alternating `CMD_QUERY_PTR`
> `QUERY_OP_GWORLD` / `QUERY_OP_GAME_ENGINE`; both are read-only and thread-agnostic, so they
> exercise the refactored dispatch `switch` without touching game state or needing the PE hook).
> `initState=2 (READY)`; round trips 5.4 ms / 6.7 ms.
> **Logs are clean: no `Mailbox: tick threw`, no `result=-11`, and 0 `[ERROR]` lines across all 8
> current log files.**
>
> ⭐ **Independently corroborated, not self-confirming**: the mailbox returned
> `&GWorld = 0x7FF6483188A0`, byte-identical to what `get_pointers` reports over the *pipe* — two
> different transports out of the same process agreeing. Its second output word
> (`UWorld* = 0x20144924B60`) also matches the address the F5 watcher dereferenced independently.
>
> ⚠ **Two rig bugs worth keeping, because both produced a confident WRONG answer first.**
> (1) `paramsData` is at **`0x328`**, not `0x030`; the wrong offset reads the tail of `funcName` and
> reports a silent all-zero output that looks like "the command returned nothing".
> (2) The DLL leaves `status` at `DONE` after a command, so a poller that only waits for `DONE`
> **returns instantly with the PREVIOUS result** — the second dispatch reported a bogus failure
> until the rig started writing `status = IDLE` before each trigger. Write `cmd` LAST; it is the
> trigger.
>
> **Step 2 (the throw path) remains open** — unchanged, there is still no way to force a handler to
> throw on demand. Step 1's remaining half (two real `.CT` rows through CE) is still worth doing in
> the CE batch, but it can no longer fail silently: plain dispatch is now known good.
| 3 | `AC14` | ✅ **PASS 2026-08-20** — connected the UI to DumperTest, closed it **while still connected**: `pipe-0.log` has **0** `Pipe: ReadLoop error` lines and ends with an orderly `Pipe lane dropped — tearing down both lanes for a clean reconnect` (once per lane). That entry used to be the NullReferenceException logged as if a fault. | | |
| 3b | `AC14` (original) | **B** | Connect the UI to an injected game, then close the UI **while still connected** (this is the `Dispose()` path that nulls `_reader` without awaiting the read loop). | `pipe-0.log` ends cleanly. **No `Pipe: ReadLoop error`** line — that entry was the NullReferenceException this fixed, logged as if an ordinary shutdown were a fault. |
| 4 | `AC13` | **B** | System tab → note the IPC figure. Then kill the game while the UI is mid-request so a write fails, and look again. | The IPC total now includes the failed request's transport time. Previously a write-path failure contributed exactly 0 ms, i.e. the figure flattered itself precisely when the pipe was misbehaving. |
| 5 | `AC15` | **B** | Proxy Deploy → Scan Steam libraries, and the generic drive scan. | The same games are found with the same names/paths. The only intended difference is speed: one full VERSIONINFO resource load per detected game is gone. `UeVersion` was and remains null. |
| 6 | `AE27` | **B** | Game Class Filter → type in the Package box, and sort by the Package column. | Identical results to before. `Package` is now memoized per `ClassPath` with setter invalidation; a stale or blank Package cell would mean the invalidation is wrong. |
| 7 | `AF25` | ✅ **PASS 2026-08-20 `[AF25-CT-2026-08-20]`** — generated the real `.CT` from Teleport → CE Export → **Save .CT…** (34 rows, 281 KB) and read the emitted command numbers back. The file carries a section headed *"--- Teleport (17 rows) ---"*, and `writeInteger(mb + 0x00, 8)` appears **exactly 17 times** — the count matches the section header, so `CmdTeleport` is still **8** after the move to `CeMailboxLayout`. The other three agree three ways (DLL enum ↔ C# constant ↔ emitted script): **10** `CMD_MOVEMENT` ×8, **11** `CMD_FLY` ×11, **15** `CMD_TIME` ×4. `check_mailbox_contract.py` is green alongside. ⚠ "Run one" (an actual teleport) was **not** done — that needs CE plus a game with a controllable pawn. | |
 Byte-identical script and working teleport. `CmdTeleport` moved to `CeMailboxLayout` but the value is unchanged (8), and the generator tests already assert the emitted text — this is belt-and-braces. |
| 8 | `AC17` | **C** | **Needs a real mount point.** Mount a fixed volume into a folder (`mountvol`, or Disk Management → Change Drive Letter and Paths → Add → empty NTFS folder), put a leftover proxy under it, then run Proxy Deploy → leftover cleanup → Execute. | The file goes to the Recycle Bin. Before this fix the fixed-drive pre-filter answered about the HOST volume (`DriveInfo` normalizes through `Path.GetPathRoot`), so it always said "Fixed" for mount-point paths and judged nothing. A removable volume mounted the same way should now be REFUSED. |

### ⬜ DEFERRED, NOT A VERIFICATION ITEM — AB23: intern `GroupSlotMatch::ownerClass`

*Referenced from `dll/src/Radar.h` (the `kMaxGroupSessionLeaves` block) and from AB23's register row,
which stays **open** — this is unshipped work, not something awaiting a live check. Listed here so
those pointers resolve; it is counted by `check_audit_register.py`, not by the OPEN FIXES INDEX.*

`GroupSlotMatch` carries a by-value `std::string ownerClass` per LEAF, which is the per-record heap
string V3-A's interning was built to remove. `GroupSession` already has the machinery — `descriptors`
and `instances` pools, reached through `internDesc` / `internInstance` in `ScanForValueGroup` — so the
shape of the fix is settled: add an owner-class pool, replace the string with a `uint32_t` index, and
update the single reader (`Fern.cpp:377`, `lj["owner_class"]`). Six sites in total: four writes in
`Aura.cpp` (`:8427`, `:8774`, `:8899`, `:8965`), that one read, and the declaration.

**Why it was not done in L12:** no test target compiles `Aura.cpp` or `Fern.cpp`, so a refactor of the
group-scan hot record could only be verified in-game, and L12 ran unattended. What *was* done is the
half that could be made safe offline — the memory accounting the finding exposed. The budget's
justification read "~120 B per `GroupSlotMatch`, so 4,000,000 leaves is roughly half a GB", counting
the string OBJECT and not its heap block; UE class names routinely exceed the SSO buffer, so the real
ceiling is materially higher. The size is now derived (`kGroupSlotMatchBytes = sizeof(...)`) so it
cannot go stale, the under-count is stated, and a `static_assert` guards the premise by failing if
`kMaxGroupSessionLeaves * sizeof(GroupSlotMatch)` ever reaches 1 GB.

**Do the interning together with a raise of `kMaxGroupSessionLeaves`, not before it** — the cap is the
only thing that makes the per-leaf cost matter, and today's cap is far above any observed scan.

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L11 (U1/U4 + stragglers): V8 / V10 / V11 / W8 / Y10 / Y11 / Y12 / Y13 / F5

*L11 was the LAST LOW batch. **V9** (the Object Tree's Cancel button could not cancel a search) and
**Y14** (the baked export announced N params over values it had failed to parse) need NO live check —
both are driven end to end by the real ViewModel / real generator in `AuditL11HonestyTests`, and both
negative-controlled. **U18** is comments only. What follows is the rest.*

⚠ **F5 is the row to run FIRST and the one to worry about.** It is the only change in this batch that
touches the pipe every other feature rides on, in the game's own process: `MakeResponse` /
`MakeEvent` no longer splice their payload with nlohmann's `merge_patch` (per-key assignment
instead, which cannot delete an envelope key or be replaced wholesale by a non-object payload), and
`Fern::WriteLine` no longer materialises a `line + "\n"` copy — payload and terminator now go out as
two `WriteFile` calls under the same `writeMutex` on the byte-mode pipe. `Renge::ApplyPayload` is
pinned by 16 assertions in `dll_helpers_test` (the header IS compiled there), but **nothing compiles
`Fern.cpp`**, so the two-write split has never executed. If anything in this batch breaks a session,
it is this.

⚠ **Y10 / Y13 did NOT move the mailbox contract, and that is worth confirming rather than assuming.**
Both changes are script-side: a contract check placed before the first write, a pre-zero loop clamped
to the 1024-byte params region, and a wider Before/After dump window. Nothing about the LAYOUT
changed, `tools/check_mailbox_contract.py` passes unchanged, and the emitted script still bakes
contract **3** (min 1). A `.CT` saved before this batch stays valid.

> | # | cat | 做什麼 | 預期 |
> |---|-----|--------|------|
> | 1 | **A** | **F5.** With the UI **disconnected** (⛔ `kMaxPipeInstances=3` and the UI holds 2 — see `[PIPEBUSY]`), drive `tools/verify/pipe_client.py` against an injected game and send `snapshot_chunk`, `find_instances` (a class with thousands of instances) and `list_all_functions`. | Every reply parses as one JSON object per line and carries **all three** envelope keys `id` / `ok` / `game_thread_stalled` alongside its payload. The big ones matter most: they are the responses whose second copy the fix removed, and the two-`WriteFile` split is what could truncate or interleave them. |
> | 2 | **A** | **F5, the interleave control.** Same session: start a `watch` so the DLL pushes EVT_WATCH events on one connection while you issue ordinary commands on the other, for a minute. | No malformed line, ever. Both writes for a message happen under one `writeMutex`, so a watch event must never land in the middle of a response. A single garbled line here refutes the split and the change should be reverted to one `WriteFile`. |
> ### ✅ F5 STEPS 1 + 2 PASS 2026-08-19 `[F5-WIRE-2026-08-19]` — headless, DumperTest Development, dist 3263
>
> Rig: `tools/verify/f5_envelope.py`. ⚠ It does **not** use `PipeClient.request` to judge lines:
> that method silently `continue`s past any line it cannot parse, which is right for driving the
> DLL and **fatally wrong here**, where a malformed line is the entire subject — it would be
> dropped and the run would report a clean pass. The rig keeps every raw byte and judges the lines
> itself, distinguishing *truncated* from *two objects on one line* (they mean different bugs).
>
> * **Step 1 — PASS.** The big replies, the ones whose second copy the fix removed:
>   `list_all_functions` **961,873 B in 0.15 s**, `list_classes` 397,101 B, `find_instances` 48,102 B,
>   `begin_snapshot` + 3× `snapshot_chunk`. **Every reply carried all three envelope keys**
>   (`id` / `ok` / `game_thread_stalled`) and **9 of 9 wire lines were well-formed**.
>   (Incidentally re-confirms the `list_all_functions` timing note: 0.15 s, not minutes.)
> * **Step 2 — PASS, and the control is NOT vacuous.** 60 s, two connections: the main one issued
>   **17,205 commands**, the watch one received **187,553 lines including 1,179 real `watch`
>   events** — so a second writer genuinely competed for `writeMutex` throughout.
>   **204,758 lines total, ZERO malformed.** The two-`WriteFile` split never truncated a payload
>   and no event ever landed inside a response.
>   ⚠ Two traps this step nearly fell into, both now guarded in the rig: the parameter is **`addr`,
>   not `address`** (`Fern.cpp:4961`) — the wrong name is accepted as `addr=""`, i.e. a watch on
>   nothing; and the watched address must be one that **changes** (the `&GWorld` *slot* is static —
>   watch the `UWorld` it points at). With either wrong, "no malformed line" is trivially true of no
>   lines, so the rig now reports **INCONCLUSIVE** rather than PASS when 0 events were pushed.
> * **Step 3 (the UI regression control) — NOT RUN**, it needs the UI on screen. Deferred to the
>   UI batch; steps 1 and 2 are the ones that could only be done headless.

> | 3 | **A** | **F5, the ordinary path.** Connect the UI normally and use it for a few minutes — Object Tree load, Live Walker drill, a value scan. | Everything behaves as before. This is the regression control; the envelope change is invisible when it works. |
> | 4 | **B** | **W8.** On a Blueprint-heavy shipped title, Tools → export the `.usmap`, and compare the "N structs" line against the same game before this build. | The struct count rises by roughly the number of `BlueprintGeneratedClass` objects in the game (thousands, not a handful), and a known `BP_*_C` / `WBP_*_C` name is now present. Load the file in FModel / CUE4Parse if it is installed — the `W1/W7` item already wants that parser. |
> | 5 | **B** | **V10.** On a title where the first scan leaves GObjects **or** GWorld unresolved, press **Extra Scan** and wait for it to finish. | The green "Found: GObjects: 0x…" result **stays on screen**. Before the fix it appeared and was blanked a few ms later by the pointer refresh the scan itself triggered. Then, mid-scan, change the **UE version** ComboBox: the Extra Scan button must stay disabled until the scan really ends. ⚠ Sample-blocked if every installed title resolves both pointers on the first pass. |
> | 6 | **B** | **V11.** With CE + the AOBMaker plugin connected, click **Register symbol** on the GWorld card, then again with **CE closed**. | Success prints a teal line naming `gworld_addr`; the failure prints a RED line naming it. Before the fix both produced *nothing at all* on screen. Repeat on the **&GEngine** card — it was the second site, found by the sibling grep. |
> ### ✅ Y10's CONTRACT-BEFORE-WRITE HALF PASSES 15/15 2026-08-20 `[CONTRACT-ORDER-2026-08-20]`
>
> `scripts/tests/contract_check_test.lua` runs the **real `[ENABLE]` block the shipping UI emitted**
> (working-lessons §2.8) over stubbed CE globals, with **every mailbox write recorded** so "nothing
> was written" is measured rather than assumed.
>
> The ordering is the whole point: the contract check must happen **before the first write**, because
> the thing in question IS the layout — if the script's field offsets are wrong, a write placed first
> lands somewhere unintended.
>
> | refusal | unticks | explains | mailbox writes |
> |---|---|---|---|
> | contract symbol does not resolve | ✅ | names `g_mailboxContract` | **0** |
> | wrong magic (stale address) | ✅ | "wrong memory" | **0** |
> | DLL older than the script | ✅ | "older than this script" | **0** |
> | script older than the DLL | ✅ | "too old for the DLL" | **0** |
>
> ⭐ **The positive control is what stops this being vacuous:** with a VALID contract (magic ok,
> `min ≤ 3 ≤ cur`) the script stays ticked, prints no refusal, and **does** write the mailbox. A
> script that simply never wrote would have passed all four "0 writes" rows.
>
> This also exercises CLAUDE.md's CE-Lua rule that *a bail-out which applied NOTHING must untick the
> record* — all four do (`memrec.Active = false`), so CE cannot leave a row ticked while claiming a
> cheat is active.
>
> ⚠ **Not covered:** Y10/Y13's other half — the Before/After **dump window** reaching a return slot
> past byte 32 — needs a UFunction with a complex return and a real CE session.

> | 7 | **B** | **Y10 / Y13.** Open a UFunction with a **complex return** (FString / struct) whose return slot sits past byte 32, tick **Verify return**, and push the baked script to CE. Tick the record. | CE's Lua Engine shows the Before/After dump **containing the return slot** (the window is now sized to reach it) and the line no longer says "see After: dump above" when it cannot. Then untick, **detach CE from the game**, and re-tick: the contract check must fire FIRST with a message naming `g_mailboxContract`, and the record must **untick itself** — no `writeByte` may have run. |
> | 8 | **B** | **Y12.** Close CE (or disconnect AOBMaker), then **Copy AA Script (Baked)**, and right-click → Paste in CE's address list. | A memory record appears with type **Auto Assembler Script**. Before the fix the clipboard held a bare `[ENABLE]`/`[DISABLE]` body, which CE will not accept as a record at all. The result label should say "copied as CE XML", not "copied to clipboard". |
> | 9 | **B** | **Y11.** Find a UFunction taking an `FText`, `TArray` or `TMap` parameter and press **FIRE**. | An `FText` param is refused by name whatever the box holds. A `TArray`/`TMap`/`TSet`/struct param fires with the slot **left zeroed** when its box is untouched, and is refused with a message when you type a value into it. Before the fix the textbox was written as a raw int32 over the structure's Data pointer and handed to ProcessEvent. ⚠ Sample-blocked if no installed title exposes such a UFunction. |
> | 10 | **B** | **V8.** Walk a `UDataTable` with **more than 64 rows** in Live Walker and drill into its **RowMap**. | The breadcrumb, the header and the RowMap preview row all carry "⚠ showing 64 of N", and the status line says the view is capped per fetch — **without** naming the Array Limit slider, which does not govern this view. A DataTable with ≤64 rows must show none of that. |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L10 (T1e Views/app root): AF7 / AF8 / AF10 / AF11 / AF12 / AF13 / AF16–AF23

*Most of L10 needs NO live check. **AF9** (log-folder count cap removed) is pinned by three tests
driving the real `LoggingService` against real directories — 30 in-window folders survive, a
>21-day one still dies, the UI's own folder is exempt. **AF12/AF13/AF14/AF15** are pinned offline:
the per-slot truncation flag has a two-direction test plus a "reported even on a MISS" case, the
mailbox params slots have an arithmetic invariant test on top of the generators' existing
literal-text assertions, and the folded-groups note is now one shared function. What follows is
only what a running game — or specifically the **trimmed** binary — can settle.*

⚠ **AF12 / AF13 are in this heading even though they are pinned offline, and the distinction
matters.** Pinned offline means the *logic* is unit-tested; it does not mean the string has ever been
seen on screen — 繁中 鐵則 4, *"閘門答對 ≠ 使用者看得到"*. The mirror carries them (with **AF22**) as a
see-it-in-the-dialog check, so the ID has to be findable from a heading here or the two registers
disagree about whether anything is outstanding.

⚠ **The AOT sort rows below cannot be checked in a dev build, by construction.** The whole defect
class is "the reflection sort survives JIT and is trimmed away in the binary we ship", so a
`build.ps1` (non-trimmed) run passes with the bug present. Every row marked **AOT** must be done on
a `-Mode Publish` binary. The offline half is machine-enforced by
`DataGridSortWiringTests` (two guards, both negative-controlled), which is what makes this a
spot-check rather than a 30-column sweep.

> ### 🟡 THE AOT SORT (steps 1–3) — WORKING, on 2 grids of the named set `[AOTSORT-2026-08-20]`
>
> Run against the **`-Mode Publish` AOT binary** in `dist/`, which is the only build that can answer
> this: the whole defect class is "the reflection sort survives JIT and is **trimmed away in the
> binary we ship**", so a non-trimmed build passes with the bug present.
>
> **8 sort operations, all correct — 2 grids × 2 columns × both directions:**
>
> | grid | column | ascending | descending |
> |---|---|---|---|
> | Interesting Funcs | `Function` (text) | `AbortMatch` · `Abs` · `Abs_Int` | `Xor_IntInt` · `Xor_Int64Int64` · `WriteVector4` |
> | Interesting Funcs | `Param` (numeric) | all `0 (0B)` first | `9 (97B)` · `9 (97B)` · `9 (96B)` |
> | Classes | `Class` (text) | `ABP_Manny_C` · `ABP_Quinn_C` | — |
> | Classes | `Size` (hex) | all `0x0` first | `0x2956` · `0x2886` · `0x24E6` |
>
> The **↑/↓ indicator moves to the clicked header** and leaves the previous one, so the grid's own
> state agrees with the row order. Baselines were captured before each click (the Interesting Funcs
> grid started score-descending at `ClientCheatFly` / `ClientCheatGhost`), so these are reorderings,
> not coincidences.
>
> ⚠ **Why this is 🟡 and not ✅.** Steps 1–3 name a specific set of grids and only two of them were
> exercised: **not** Live Funcs `Period`, Detect Stats `✓`/`Offset`, Live Walker's `Params`, Class
> Pivot Discover, Snapshot / Snapshot Diff / SPC, or the Invoke param picker. The trimming risk is
> *global* (if the reflection path were trimmed, nothing would sort), so this is strong evidence for
> the defect class — but it is **not** the per-grid sweep the row asks for, and the remaining grids
> each need their own data before their headers can be clicked.
>
> ⛔ **The Props dialog could NOT be sort-tested and this is a SAMPLE limit, not a failure.** Opened
> from Interesting Functions on `CapsuleOverlapActors` and again on `Character.ServerMove`, it
> reports **`0 properties (0 written) [native disasm — heuristic, N unmapped]`** and the grid is
> empty — with "Class fields only" both ticked and unticked. That matches the headless `AF7` result
> exactly (`props: []`, `unmapped: 2–3`): `walk_function_props` is the Path-2 **disassembly** xref
> finder, and DumperTest's engine functions yield no `[this+off]` references to list. **An empty
> grid cannot demonstrate a sort**, so the Props/Xref half of step 2 needs a title where that
> dialog actually populates.

> ### ✅ STEPS 4–8 ALL PASS 2026-08-20 `[L10-HEADLESS-2026-08-20]` — the five category-A steps
>
> Driven against the **`-Mode Publish` AOT binary** in `dist/` (the one this batch requires),
> connected to DumperTest Development (`Connected — UE504 (25179 objects)`).
>
> * **Step 4 — `AF10` PASS.** A second `UE5DumpUI.exe` launched while one was running exited with
>   code **1**, not 0, and afterwards exactly **one** instance remained (the second did not linger).
> * **Step 5 — `AF11` PASS, and it was observed happening rather than staged.** `TeleportCoords\`
>   was created at **22:55 on 2026-08-19 — the first UI launch of this session** — and both
>   `teleport-coords.dumpertest.json` **and its `.bak`** are now inside it **with their original
>   `Aug 12 08:09` mtimes preserved**, i.e. moved as a GROUP, not rewritten. The root copies are
>   gone; `teleport-hotkeys.txt` correctly stays in root (app-wide, fixed in number).
> * **Step 6 — `AF11` negative control PASS, with the log line.** Planted a *distinct* 47-byte
>   `teleport-coords.dumpertest.json` in the root while the real one sat in `TeleportCoords\`, then
>   started the UI. The root copy was **left in place**, its content unchanged, and the
>   `TeleportCoords\` copy was **byte-identical** afterwards (SHA-256 compared, not eyeballed):
>   ```
>   [WARN] AppDataFolderMaintenance: left 'teleport-coords.dumpertest.*' at the old location
>          ('teleport-coords.dumpertest.json' already exists in the new folder)
>   [INFO] AppDataFolderMaintenance: moved 0 'teleport-coords' file(s) into '…\TeleportCoords',
>          left 1 behind
>   ```
>   ⭐ Note the wording is the **`.*` GROUP** form and the count is honest ("left 1 behind") — both
>   are the invariants CLAUDE.md's app-data rule demands. Planted file removed afterwards.
> * **Step 7 — `AF8` PASS.** `LandscapeMeshProxyComponent.ProxyLOD` is an `Int8Property`
>   (`prop_offset` 1628, `prop_size` 1) with 1 live non-CDO instance. Forced to **−5**:
>   `ok=true held=1 resolved=true`, and `get_forced_fields` reports `value=-5.0` — **negative and
>   exact**, not wrapped to 251. `reset_all_fields` → 0 held.
>   ⚠ Finding an `Int8Property` at all is the slow part; the shortcut is to grep the **exported SDK
>   header** for `Int8Property` (24 hits) and then confirm the true owner via `search_properties`,
>   because the header's nearest-enclosing-struct is unreliable for nested types.
> * **Step 8 — `AF7` PASS, 8 of 8.** `walk_function_props` carries the `budget_hit` key on eight
>   native functions across eight distinct classes, including a **19-parameter** one
>   (`FunctionalTestUtilityLibrary.TraceChannelTestUtil`). All reported `budget_hit=false`.
>   ⚠ **`props: []` here is CORRECT and must not be read as a defect** — this command is the Path-2
>   **disassembly** xref finder (`method: "disasm"`, `script_bytes: 0`, `unmapped: 3`), not a
>   parameter lister, and a static BlueprintCallable touches no `this` properties to report.
>
> **Steps 1–3 (the AOT DataGrid sorts) remain** — they need many grid-header clicks across Live
> Funcs, Detect Stats, Live Walker, two dialogs, Class Pivot, Snapshot and the Invoke picker.

> | # | cat | 做什麼 | 預期 |
> |---|-----|--------|------|
> | 1 | **B** | **AOT.** On a `-Mode Publish` build, click the **Period** header in Live Funcs, the **✓** and **Offset** headers in Detect Stats, and the **Params** header in Live Walker's function grid. | Rows reorder, and reverse on a second click. Before the fix these four headers animated and did nothing. Period must order numerically (a 16.7 ms row above a 1000 ms row), not by the rendered label. |
> | 2 | **B** | **AOT.** Same build: open the Props dialog from Interesting Functions and the Xref dialog from Class Struct, and click every column header in each. | All six headers in each dialog reorder. `Access` / `Refs` must sort by the NUMBER (a "12W / 3R" row above "2W / 1R"), not by the rendered string. |
> | 3 | **B** | **AOT.** Same build: Class Pivot's Discover grid (Changed / Cat / Shape / Score), Snapshot's list (Label / Size), Snapshot Diff's **Change**, Snapshot+SPC group grids' **Class**, and the Invoke param picker's four headers. | All reorder. **Size** must be numeric (a "980 MB" row below a "1.2 GB" row) — these are the ten columns no finding named, found by the repo-wide sweep. |
> | 4 | **A** | **AF10.** With the UI already running, launch `UE5DumpUI.exe` a second time from PowerShell and read `$LASTEXITCODE`. | **1**, not 0 — and the first instance's window comes forward. Previously the second-instance refusal reported success to any script that waited on it. |
> | 5 | **A** | **AF11.** Put a `teleport-coords.<module>.json` (plus a `.bak`) in `%LOCALAPPDATA%\UE5CEDumper\` root, then start the UI. | Both files are now in `%LOCALAPPDATA%\UE5CEDumper\TeleportCoords\`, the root copies are gone, and the Teleport tab still lists the coordinates. ⚠ Check the pair moved **together** — a `.json` migrated without its `.bak` is the group-move invariant broken. |
> | 6 | **A** | **AF11, negative control.** Repeat with a `teleport-coords.<module>.json` already present in `TeleportCoords\`. | The root copy is **left where it is** and a log line says so — never silently overwritten. Then confirm no sweep: backdate a library past 21 days and restart; it must still be there (`maxAgeDays: 0`, same as `Bookmarks\`). |
> | 7 | **A** | **AF8.** Find an `Int8Property` via Property Search (`walk_class` on any class, grep the reply for `Int8Property`) and Force it to a **negative** value, e.g. `-5`. Then `get_forced_fields`. | Held count > 0 and the value reads back as **-5**. Before the fix the write stored 0xFB correctly but the read returned **251**, so the re-assert worker rewrote the byte every tick forever and the UI showed permanent drift. Also try `200`: it must now be **refused** as out of range rather than landing as -56. ⚠ Sample-blocked if no title exposes an `Int8Property`. |
> | 8 | **A** | **AF7.** Run `walk_function_props` over the pipe against a **native** (non-Blueprint) UFunction on a large class and look for `budget_hit` in the reply. | The key is present. When `true`, the Props dialog's status line turns amber and carries "the disassembler hit its instruction budget", and the Interesting Functions batch **Uses** cell shows `⚠ partial`. ⚠ Needs a native function big enough to exhaust the budget — check the DLL's own `AnalyzeNativeFunctionProps ... BUDGET` log line to find one. |
> | 9 | **B** | **AF22.** Property Search → right-click a row → **Force value…**. | The dialog is titled **"Force property value"**, the field is labelled "Force value (…)", the confirm button says **"Hold this value"**, and the inherited-field caveat does **not** mention `className` or a CFG block. Then open the ordinary **Freeze** flow and confirm it still says "Create freeze script" and still gives the CFG-block advice. |
> | 10 | **C** | **AF21.** Set Windows display scaling to **150%**, move the main window so roughly a third of it hangs off the right edge of the monitor, close the app, reopen. | It reopens where it was left. Before the fix the guard measured the window at its DIP width (two thirds of its real size), so a legitimately-placed window could be judged off-screen and its position stopped being tracked. ⚠ Needs a real scaling change — the one row here a script cannot do. |
> | 11 | **B** | **AF12/AF13.** Snapshot tab → Group match with a value common enough that one slot matches >256 fields on some object. | The status line gains the "a slot matched more than 256 fields" notice — the same sentence the live Group Scan already shows. Also change Value Search's per-slot cap to 1024 and re-run the SNAPSHOT query: it still says 256, which is correct and now stated rather than implied. |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L4 (D4b Mimic/Sein/Flamme): MB1 / MB2 / SE1 / FL1 / FL2

*The pure decision rules of this batch are unit-pinned in `dll_helpers_test` and need NO live check:
**MB1**'s `ShouldRouteDirectInvoke` (10 assertions), **MB2**'s `CommandRequiresInit` (17), **FL1**'s
`ShouldPublishAtomicWrite` (11) — all five negative controls red exactly the predicted rows. **SE2 is
not listed below at all**: its trigger (`FindNextFileW` failing mid-enumeration) is not reproducible
on demand, the fix is structural, and the finding's own honest limit says so. What follows is only
what a running game can settle. ⚠ Note none of `Mimic.cpp` / `Sein.cpp` / `Flamme.cpp` is compiled by
any test target, so for those three files "green tests" means the HEADERS, never the handlers.*

⚠ **Rig trap found while verifying this batch, worth knowing before any future header-only change:**
`build/build.ninja` carries **no `msvc_deps_prefix`**, and this machine's MSVC emits `/showIncludes`
in Chinese, so ninja's English default prefix never matches and `ninja -t deps` reports **`#deps 0`**
for the test object. A header-only edit therefore yields `ninja: no work to do` and any check silently
measures the OLD binary — the first run of the negative-control rig reported `Fail: 0` for all four
breaks for exactly this reason. `build.ps1 -Clean` (what CI always passes) is unaffected; a bare
incremental `cmake --build` after editing only a `.h` is not.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | MB1 | on any game, generate an Invoke script for a **stateful, non-static** UFunction, enable it, FIRE once, then enable a **second** Invoke script for a `Native\|Static` Kismet helper and FIRE it — then go back and press FIRE on the FIRST form again | the first form's second FIRE still routes through GameThreadDispatch. `pipe-0.log` shows either no `INVOKE mailbox functionFlags=... is STALE` line, or that WARN naming the stale value and the re-read one | before the fix the second FIRE inherited the helper's `Native\|Static` from offset 0x024 and ran a stateful actor UFunction OFF the game thread; the WARN is the fix's own greppable evidence |
> | MB1 | same session: confirm a `Native\|Static` helper (e.g. a KismetMath call) still takes the fast path on an **idle** game (main menu) | `INVOKE -> static-native fast path` still logged; no -5 timeout | the re-read must not COST the fast path — a `ResolveFunctionInfo` that fails on some game would silently degrade every pure helper to a queued call that times out at the menu |
> | MB2 | with the DLL injected into a game whose GObjects scan **fails**, hit the CE `.CT` Keep-Foreground toggle | the toggle works (result 1/0), instead of the old `hook error -10` naming MinHook — a subsystem the command never reached | needs a genuinely failing scan; a healthy game only proves the exemption did not break the normal path (still worth doing as the regression check) |
> ### ✅ SE1 + MB2 PASS 2026-08-19 `[SEMB-HEADLESS-2026-08-19]` — both headless, no CE, no UI
>
> **SE1 — PASS, both halves.** `tools/verify/se1_log_reroute.py` takes a genuinely exclusive handle
> on `scan-0.log` via `CreateFileW` with `dwShareMode = 0` — **not** Python's `open()`, which shares
> the file and would let the DLL open it happily, passing the test vacuously. The rig proves the
> lock by attempting a second open that must fail with `ERROR_SHARING_VIOLATION`.
> * the announcement appears: `Logger: category 'scan' could not open 'scan-0.log' (errno=13) — its
>   lines are rerouted here for this run`
> * ⭐ and the half that actually matters — **597 `[SCAN*]` lines landed in `init-0.log`**, starting
>   at `FindAll: Starting global pointer scan...`. The announcement alone would not be the fix; the
>   LINES had to survive, and they did.
> * `init` was deliberately left writable: it is where the rerouted lines must land, so locking it
>   would have destroyed the evidence instead of producing it.
>
> **MB2 — PASS on the row's exact precondition.** Driven through the mailbox from Python
> (`tools/verify/mailbox_poke.py <pid> --cmd 12`), so no Cheat Engine was involved.
> * **Host with a genuinely failing scan** — `FindAll: Complete — GObjects=0x0 (not_found),
>   GNames=0x0 (not_found), GWorld=0x0 (not_found)`. The toggle sequence:
>   `GET → 0`, `SET 1 → 1`, `GET → 1`, `SET 0 → 0`. **No `hook error -10`**, i.e. the
>   `CommandRequiresInit` exemption holds and the command no longer blames MinHook, a subsystem it
>   never reached.
> * **Regression control on a second host** (Notepad++, `Partial init — GObjects=OK GNames=MISSING`):
>   identical `0 → 1 → 1 → 0`. So the exemption did not break the ordinary path either.
> * ⚠ Honest limit: `initState` was `READY` in both cases, because it reports whether the *pipe
>   server* started, not whether the scan succeeded. So this exercises "GObjects unresolved", which
>   is what the row asks for — not "init never finished", which no available host produces.
> * ⚠ Rig note: `pid_of` refuses to guess between same-named processes, and these rigs *run under
>   `python.exe`*, so the name "python" is permanently ambiguous with the rig's own interpreter.
>   `mailbox_poke.py` / `mailbox_addr.py` now accept a bare **PID**.

> | SE1 | before launching a game, open one of its `%LOCALAPPDATA%\UE5CEDumper\Logs\<Game>\*-0.log` files in a viewer that holds an exclusive-ish handle, then launch | `init-0.log` opens with `Logger: category '<name>' could not open ... its lines are rerouted here`, and that category's lines appear in `init-0.log` for the run | before, the category was dead for the process with **nothing logged anywhere** and its buffered early lines destroyed — a later grep read as "that code path never ran" |
> ### ✅ FL1 + FL2 PASS 2026-08-19 `[FLSWEEP-2026-08-19]` — headless, and the age guard is proven, not assumed
>
> Rig: `tools/verify/fl_staging_sweep.py`. It plants **two** files, because "the stale one is gone"
> would pass just as happily on a sweep that deletes EVERYTHING — which is the dangerous version of
> this code, given the UI writes its own `<file>.tmp.<pid>` concurrently.
>
> | planted | mtime | required | observed |
> |---|---|---|---|
> | `…json.tmp.99999` | 3 h ago | **deleted** | deleted ✅ |
> | `…json.tmp.88888` | now | **survives** | survived ✅ |
>
> Log line exactly as specified: `HintCache: removed 1 abandoned staging file(s) older than 1h`.
> FL1's production negative control also passes — `HintCache: Saved results for
> PE=67F515A70001A000 (python.exe, scan #2)` with **0** `staged write is incomplete` lines — and the
> real cache still parses with all **33** entries. Suffixes 99999/88888 are checked against the live
> PID list before planting so the plant cannot collide with a real staging write.
>
> ⚠ **THREE RIG TRAPS, each of which produced a FALSE FAIL of a working fix before being found.**
> 1. **The sweep is once-per-process.** `SweepOrphanTemps` holds
>    `static std::atomic<bool> s_swept` (`Flamme.cpp:136`), so in an already-injected game it has
>    *already run* — before you planted anything. It needs a **fresh process**.
> 2. **`trigger_scan` on an already-scanned process re-saves nothing.** Measured: after
>    `trigger_scan` the log gained only `FindGameEngine` lines and the last
>    `HintCache: Saved results` was still the injection-time one. So both the sweep line *and* the
>    save line are legitimately absent and the run reads as a total failure.
> 3. **`scan-0.log` is a SLOT NAME, not a file identity.** Each process start archives the previous
>    run, so a byte offset captured before the launch indexes into a different, shorter file and
>    discards the lines you are looking for. Read the whole fresh file.

> | FL1/FL2 | plant a stale `UE5CEDumper.<Machine>.json.tmp.99999` (mtime > 1 h old) in `%LOCALAPPDATA%\UE5CEDumper\`, then run any scan | after the scan the planted file is gone and `scan-0.log` has `removed 1 abandoned staging file(s) older than 1h`; the real cache is intact and a **fresh** temp from a live write is never touched | the age guard is what makes the sweep safe against the UI writing its own `<file>.tmp.<pid>` concurrently |
> | FL1 | ordinary regression: run two scans of the same game back to back | `HintCache: Saved results ... scan #2` and the cache file parses; **no** `staged write is incomplete` line | the refuse-on-failure gate must not refuse a legitimate write — this is the negative control for the production path, since the unit test only covers the predicate |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L3 (T1b): AD10 / AD12 / AD13 / AD15 / AD16 / AD18

*Most of L3 needs NO live check and is already machine-enforced offline: **AD12/AD13/AD15/AD16**'s
corrected geometry is now asserted by a compile-time `static_assert` AND by
`extract_patterns.py --check`, both negative-controlled 6-for-6 (**AD17**); **AD7/AD22** are pinned by
`check_derived_counts.py`; **AD20/AD21** were already fixed and are covered by `utf8_helpers_test` /
`dll_helpers_test`; **AD11/AD14** are proofs from the table's own text. What is listed below is only
what a running game can settle that a checker cannot.*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | Inject into any UE 4.27 title and grep `scan-0.log` for `GWLD_TQ_3`/`GWLD_TQ_4`/`GOBJ_PS1`/`GOBJ_PS6`. | If one of them WINS, its resolved address must be a plausible `&GWorld` / `&GUObjectArray` (matches the address the winning pattern in a previous run reported). Before build 3262 these four resolved to garbage on every hit, so any past log showing one of them *validated* is worth re-checking — that is the strongest available evidence the old geometry was wrong. ⚠ **A run where none of the four wins proves nothing** — they are low-priority entries and a better pattern normally lands first. |
| 2 | Same session: check whether the Teleport tab's Global Pointers card still offers an AOB-wrapped CE export for GWorld. | Unchanged from before. **AD10** only withholds the triple when replaying it does not reproduce the resolved address; every GWorld entry is `RipBoth`, and the direct arm is the normal winner. |
| 3 | Force the AD10 path if a title ever resolves GWorld via the DEREF arm (or a future entry gains a non-zero `adjustment`). | `scan-0.log` (or `init-0.log`) carries the new WARN `replaying its published AOB triple does not reproduce it` and the CE export offers **no** AOB — instead of exporting a triple that resolves to the wrong address. ⚠ Not reproducible on demand; watch for the line rather than trying to cause it. |
| 4 | **AD18** — launch a game with each of the four proxies (`version` / `dinput8` / `dxgi` / `winmm`) in turn. | Each still loads its real System32 DLL and the game starts normally. The refusal path is unreachable on a healthy system, so this is a **regression check on the rewrite**, not a test of the fix: the point is that routing all four through `Lugner::SystemDllPath` did not break the ordinary case. |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L1 (D1/D2/D3 DLL engine): U11 / G6 / G7 / A7 / A8 / A9

*The pure rules of this batch are unit-pinned in `dll_helpers_test` and need NO live check: **U9**
(`ReadEnumRawValue` — byte enums unsigned), **U10** (`IsPlausibleStringCount` — 8192 cap), **G4**
(`BlockBitsAreIndistinguishable` — the probe collision), **G5** (`UE4NameIndexInBounds` — negative
index). Each has a negative control (revert reds the exact rows). **A10 was LEFT OPEN** — its two
caches return `const T&` references, so a safe invalidation needs the by-reference→by-value
restructuring U5 deferred; it is not a live-check item, it needs its own session. The rows below are
the in-situ fixes that only a running game / obfuscated fork can prove.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | U11 | ⛔ **NO SAMPLE ON DumperTest — checked 2026-08-20 `[U11-NOSAMPLE-2026-08-20]`.** The repo's own TOptional fixture declares `Opt_Int_Set`, `Opt_Float_Set`, `Opt_Str_Set` (FString) and `Opt_Int_Unset` — **and no `TOptional<FText>` at all** (grepped the full 75,342-line SDK export; the only other OptionalProperty in it is World Partition's `CellBounds`, a `TOptional<FBox>`). Since the fix is specific to FText — it used to read an inline FString at `FText+0x10`, where UE stores the `uint32` Flags — an FString or FBox optional exercises a different path and proves nothing. ⚠ **Do not assume DumperTest covers this**; it is cited elsewhere (e.g. `[SDKHDR]`) as *the* TOptional sample, which is true only for the non-Text cases. Needs a title with a display/label `TOptional<FText>`; `search_properties` reports `inner_type`, so candidates can be screened over the pipe. | | |
> | U11 | on any game, Live Walker into an instance holding a **`TOptional<FText>`** that is SET (a display/label field) | the row shows the FText display string, not `(empty)` or 亂碼 | before the fix it read an inline FString at FText+0x10 (where UE stores the uint32 Flags) → garbage; now uses `ReadFTextString` like the plain TextProperty path |
> | G7 | ⛔ **NOT REACHABLE HERE, measured 2026-08-19 `[G7-NOSAMPLE-2026-08-19]`.** The step needs a title whose offsets validate **only after a re-scan**, so that the `validated=NO -> YES (re-run)` transition exists to observe. **All NINE titles swept tonight reported `probe_ran=true, validated=true` on the FIRST pass** — Lushfoil, Manor Lords, Solarpunk, EVERSPACE 2, Geri, Avowed, DQ7R, Elliot, OCTOPATH. ⚠ That includes **Solarpunk, which this row names as the example**; it validates immediately today, so the row's own suggested host no longer produces the case. Until a title that fails first-pass validation turns up, there is nothing to transition *from*. (Original step kept below.)<br><br>~~on a game that offsets-validates only after a re-scan (e.g. **Solarpunk**), connect, then trigger **apply_rescan** (the pipe/UI re-scan path)~~ | the DYNO/offsets log gains a `validation state CHANGED validated=NO -> validated=YES (re-run)` line and the summary header reads `=== Dynamic Offset Summary (validated=YES) ===`; `get_offsets` and the log now agree | before, the one-time UE5_Init scan-log summary said validated=NO forever while live state was true |
> | A9 | 🟡 **NO STALL OBSERVED 2026-08-19 `[A9-DEEP-2026-08-19]`, but the budget was never STRESSED — see below.** | | |
> | A9 | on a large game with deep/wide nested containers (a **SEED-class** object), run **Group Scan with Deep** enabled | no ~24 s single-object stall; the per-object element budget (`maxTotalElems`) bites before the global 15 s deadline, so the scan spreads across objects | before, the counter was never threaded so the budget was inert and one object could consume the whole scan window |
> | A8 | ✅ **PASS 2026-08-19 `[A8-FLAT-2026-08-19]` — see below.** ~~none available here~~ | | |
> | A7 | on a huge game, start a **find-object-by-address** (get_ce_pointer_info / find_by_address triggers `FindByAddress`) and **disconnect the client mid-scan** | shutdown/next command is prompt — no multi-second hang while the full GObjects walk finishes; the lookup returns "not found" | the loop now polls `Tot::Requested()` every 0x1000 objects like its siblings; only observable under a real disconnect on a large pool |
> | G6 | (obfuscated fork only — **MindsEye**, no sample here) let name resolution race the fork's live key-table growth; also view a block whose tag is genuinely **absent** from the table | a transiently-unresolvable tag recovers on a later name (no permanent blanking of every FName with that tag); an absent-tag block renders as plaintext | the tri-state `LookupTagKey` no longer caches a transient miss, and a clean-absent resolves to key 0 (plaintext) per Genau's rule |

> ### ✅ A8 PASS + ⛔ A7 NOT OBSERVABLE HERE — 2026-08-19 `[A8-FLAT-2026-08-19]`
>
> **A8 — PASS, all seven assertions**, on OCTOPATH TRAVELER via `tools/verify/a8_flat_layout.py`.
> ⚠ **The row's "(none available here)" is WRONG and is now corrected**: OCTOPATH is installed and
> is flat — `ValidateGObjects: Valid at 0x… (preset Flat-Base, Num=273957, Max=6146976,
> Objects=0x19D58710000 [flat])`.
>
> Asked about a live `Sequence` @ `0x21D3EA85800` with `field_offset=0x28`:
>
> | assertion | observed |
> |---|---|
> | `flat_layout` true | ✅ |
> | `packed_layout` false | ✅ |
> | `ce_offsets` is a **single** hop | ✅ `[40]` |
> | that hop **==** the requested `field_offset` | ✅ 40 == 40 |
> | `ce_base` is the **absolute object address** | ✅ `0x21D3EA85800` == the address asked about |
> | a warning is present | ✅ |
> | the warning says it will not survive a restart | ✅ names both restart and ASLR |
>
> `chunk_index=0` / `within_chunk=35006` are still *reported* — correct for a flat array, and the
> point is that they are **not in the chain**. A non-zero `field_offset` was used deliberately so
> "the single hop equals field_offset" cannot pass by accident on a zero.
> ⭐ The silent-degrade half matters as much as the address: without the warning a user pastes a
> session-only address into a saved cheat table, which is nearly as bad as the garbage pointer.
>
> **A7 — ⛔ NOT OBSERVABLE ON ANY POOL AVAILABLE HERE. Measured, not assumed.** The row wants a
> multi-second `FindByAddress` hang to interrupt. On the **largest pool on this machine**
> (OCTOPATH, **273,956 objects**) `find_by_address` returns in **0.11 s** for a bogus address and
> **0.05 s** for `0x1`; on DumperTest (25,179 objects) it is **0.07 s**. At ~0x1000 objects per
> `Tot::Requested()` poll that is ~67 poll points inside 0.11 s, so the cancellation mechanism has
> no window a client could disconnect into. ⇒ **Do not spend another session trying to catch it**:
> it needs a pool roughly two orders of magnitude larger, or a much slower per-object read. The fix
> itself is correct-by-construction and matches its siblings.

> ### 🟡 A9 — no stall, but the budget was never stressed 2026-08-19 `[A9-DEEP-2026-08-19]`
>
> Group Scan over the pipe on **Avowed** (92,036 objects, 7,404 classes), two `NumericAll` slots:
>
> | run | duration | scanned_objects | matches | `deadline_hit` |
> |---|---|---|---|---|
> | `deep=false` | 280 ms | **16,854** | 4,604 | false |
> | `deep=true` | 749 ms | **16,854** | 41,646 | false |
>
> ⭐ **The load-bearing number is that `scanned_objects` is IDENTICAL with and without Deep.** The
> defect shape is "one object consumes the whole scan window", which would show up as Deep covering
> *far fewer* objects before the deadline. It covered exactly the same 16,854, in 749 ms against a
> 15 s budget. No ~24 s single-object stall anywhere.
>
> ⚠ **Honest limit: this does not prove the per-object `maxTotalElems` budget BITES** — on this
> sample nothing ever needed it. The row wants a *SEED-class* object with deep/wide nested
> containers, and Avowed's main menu does not provide one. So the verdict is "no stall on the
> available sample", not "the budget is proven live".
>
> ⚠ **A probe that looked like a defect and was not**, recorded so it is not re-raised: asking for
> `deadline_ms=100` produced a **683 ms** scan reporting `deadline_hit=false`, which reads like an
> unenforced deadline. It is not — `Fern.cpp` clamps `if (deadlineMs < 1000) deadlineMs = 1000;`
> right where it is parsed, so 100 and 300 were both clamped to 1000 and the 683 ms run legitimately
> finished inside it. **The client cannot force deadline pressure below 1 s.**

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L6 (U3 MainWindow VM): X5 / X6 / X7 / X8 / X10 / X11 / X12

*The pure logic is unit-tested: **X4** (`DumpCompletionFormatter` — floating size + honest zero-class
line + the `DumpResult` count round-trip), **X7** (`GameThreadStalledLevel` — the stuck-ON resume
case, dedup, reset), **X9** (`CompetingHostBanner` — self-exclusion by PID and by module name, the
two-instances control), **X12** (`FileWriteFault.IsPlacementDenied` classifier), plus **X5** at the
VM level for ValueSearch (both sessions forgotten with NO End pipe call) and Console (rows dropped).
X4 and X9 are fully settled by tests; the rows below are what only a running game / real CE can prove.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | X5 | connect to game A, populate several panels (Instance Finder, Property Search, Live Walker, Value Search, Interesting Funcs), disconnect, then connect to a **DIFFERENT** game B | every panel is empty on reconnect — no rows, no addresses, no jump offers from game A; Live Walker's per-game **bookmarks survive** | before the fix only Teleport/DumpExplorer/LiveFuncs reset; the other ~13 kept stale rows offering jumps to dead addresses — bindings/timers only observable live |
> ### ✅ X5 (panel reset half) PASS 2026-08-20 `[L6-X5-2026-08-20]` — A→B, five panels, plus the bookmark half
>
> Game **A** = DumperTest **Development** (25,179 objects), game **B** = DumperTest **Shipping**
> (24,445 objects). Different builds, different module names, different PE hashes and a different
> object count, so a surviving row from A is unmistakable. A was **killed** before B was injected —
> two injected hosts at once is its own correctness bug (`working-lessons` §3.9), and the injector's
> ambiguity guard caught exactly that when a mangled `taskkill` left A alive.
>
> All five panels were populated on A first — otherwise "empty afterwards" proves nothing:
>
> | panel | on A | on B after reconnect |
> |---|---|---|
> | Instances | `Found 1 instances`, `Default__MockWorldMetricA` @ `0x23DD8498E90` | **grid empty**, no count line |
> | Properties | `Found 2 properties in 3,942 classes`, incl. `DumperTestActor.Health` @ `0x694` | **grid empty**, no count line |
> | Value Search | `First Scan: 836 candidates in 116 ms` | **grid empty**, and **Next Scan / New Scan are disabled** — the *session* is forgotten, not merely the rows hidden |
> | Interesting Funcs | full scored table (ClientCheatFly, CheatManager.God, …) | **empty**, back to "Click Load to scan all UFunctions" |
> | Live Walker | `GWorld → UWorld ThirdPersonMap`, ~20 rows with live addresses | **empty**, breadcrumbs gone, logo shown |
>
> The Object Tree also reloaded to B's own 24,438 named objects. ℹ️ The *query text* ("Health",
> "MockWorldMetricA") stays in the input boxes. That is input state, not a stale row — no address,
> no count and no jump offer from A survived anywhere.
>
> **Bookmark half — PASS, and per-game separation is positively demonstrated.** A bookmark was saved
> on A (★ → slot 1) before the switch, giving `Bookmarks\bookmarks.6A7EA60310F17000.json` with
> `slots [(0, "ThirdPersonMap")]`. After switching to B and walking GWorld there, **B's bookmark bar
> shows all eight slots empty** while A's file is still on disk unchanged. So the reset clears the
> *view* without touching the *per-game store*, which is the distinction the row is drawing —
> checking only that A's file survived would not have ruled out B displaying A's slots.
>
>  ℹ️ The **second X5 row** is now HALF settled: its *auto-refresh* clause — "both loops stop
> immediately on disconnect (no re-walk log spam against the dead pipe)" — was measured under
> `[AUTOREFRESH-LIVE2-2026-08-20]`: Auto ON and ticking at 10.0 s, game killed, **0 walk attempts
> and 5 log lines total** in the next 20 s, nothing for 121 s. The **auto-snapshot** clause and
> the corpus-preservation check are still open.
> | X5 | before disconnecting, start Live Walker **auto-refresh** and (experimental) an **auto-snapshot** loop, then disconnect. ⛔ **Needs a build carrying `[AUTOREFRESH-2026-08-19]`** — on `dist` 1.0.0.3262 and earlier the countdown freezes at `0s` and auto-refresh issues nothing, so "start auto-refresh" cannot be satisfied and a green result would only mean *a loop that never ran did not run*. The auto-snapshot half is unaffected and can be run alone | both loops stop immediately on disconnect (no "re-walk"/"capture" log spam against the dead pipe); the snapshot **corpus is preserved** | the timer/loop teardown and corpus-preservation are not unit-testable |
> | X6 | start a **Dump All** (or Full SDK / USMAP) on a large game, then kill the game / disconnect mid-export | the export aborts promptly with "… cancelled (disconnected)" instead of hanging on dead-pipe round-trips; no truncated file at the chosen name | the ct now threads from a connection-linked CTS; before, `ct` was `default` and every service ct-check was dead code |
> | X7 | pause the game thread **during a long bulk-lane scan** (so only the bulk lane observed the pause), let the scan finish, then resume and browse via Live Walker (interactive lane) | the "game thread paused" banner **clears** on resume; before the fix it stuck ON until a bulk command ran | the pure latch is unit-tested, but the PipeClient per-response feed + banner is end-to-end |
> ### ✅ X7 PASS 2026-08-20 `[L6-X7-2026-08-20]` — bulk lane saw the pause, the INTERACTIVE lane cleared it
>
> ⚠ **Two preconditions this row does not state, either of which makes it silently unrunnable.**
> * `Stark::IsGameThreadResponsive` opens with `if (!s_hookActive) return true;` — with **no PE hook
>   installed the game can never be reported stalled**, so the banner cannot appear and the row
>   passes vacuously. The hook was installed first via Teleport → **Get POV** (an `invoke_function`),
>   confirmed by it returning a real pose (`Location 500…`, `FOV 90`).
> * Freeze the **thread**, not the process. A whole-process suspend stops Fern too, so nothing
>   answers and no envelope carries the flag. `tools/verify/suspend.py suspend-tid` on the UE game
>   thread (`tid 38780`, the main thread) leaves the pipe answering while ProcessEvent dispatch is
>   frozen — which is the state the row describes.
>
> | # | action | result |
> |---|---|---|
> | 1 | suspend `tid 38780` (threshold `kStallThresholdMs` = **500 ms**) | — |
> | 2 | **bulk** lane command — Instances `find_instances` | the banner appears: *"⏸ Game thread paused — the game isn't ticking…"*. The scan itself **still returned** `Found 1 instances`, exactly as the banner promises ("memory scans still work") |
> | 3 | resume `tid 38780` | banner still ON — correct, nothing has observed a fresh envelope yet |
> | 4 | **interactive** lane only — drill a `→` in Live Walker | **banner clears** |
>
> ⭐ **Step 4 is the whole row, and it was verified on the wire rather than by eye.** From the resume
> to the clear the UI's pipe log carried exactly two commands — **`walk_instance`** and
> **`walk_functions`**, both interactive; **zero** of the 33 `BulkCommands` ran. That is precisely the
> stuck case: the bulk lane observed `true`, went idle, and never fired its own `true→false` edge.
> One latch shared by both lanes is what clears it. Had a bulk command slipped in, the old two-latch
> code would have cleared the banner too and the run would have proven nothing — so the lane audit
> is not a nicety here, it is the evidence.
>
> ℹ️ Incidental: the banner is a **layout row**, so while it is up every tab shifts down ~22 px. A
> click scripted against banner-less coordinates lands on the wrong tab and looks like a dead button.
> | X8 | on the **Console** tab, with CE + AOBMaker **closed**, click a baked-exec / Debug-Camera "to CE" action → then **open** CE with the AOBMaker plugin and click again (no tab switch) | the second click now sends to CE (was "AOBMaker not connected" from the stale cached flag) | the path now calls `CheckAvailabilityAsync` first; needs a real CE toggled between clicks |
> | X10 | on Teleport, change the **World / Player time-dilation** sliders, wait >1 s, close the app, relaunch | the slider values are restored (they now schedule a save) | before, only OTHER Teleport options triggered a save, so a time-dilation-only change was lost |
> ### ✅ X10 PASS 2026-08-20 `[L6-X10-2026-08-20]` — and the restore is proven to come from DISK
>
> ⚠ **The obvious way to run this row cannot prove anything.** `UiOptionsSettings` says it plainly:
> *"the live DLL state, read back on connect, wins when a dilation is held"*. Set the sliders, restart
> the UI, reconnect to the still-running game — and the values come back **from the DLL**, which is
> exactly the source the row is not asking about. Both sources agree, so a pass is indistinguishable
> from the persistence being broken. **The game must be dead for this row to mean anything.**
>
> 1. Teleport → Time Dilation, on a connected DumperTest: World **2×**, Player **½×** (via the
>    presets, which also apply). Card read `State: ON`, `Current: 2× (held; natural 1×)` /
>    `Current: 0.5× (held; natural 1×)`, and **`Combined player speed: 1× (world 2 * pawn 0.5)`** —
>    the dual-lane multiply is right.
> 2. Within seconds `ui-options.json` carried **`teleport.worldTimeDilation = 2`** and
>    **`teleport.pawnTimeDilation = 0.5`**. This is the fix itself: a dilation-only change now
>    schedules a save.
> 3. **Killed the game, then closed the UI**, and confirmed the two values were still on disk with
>    both processes gone.
> 4. Relaunched `dist/UE5DumpUI.exe` and left it **disconnected**. The card reads `State: Unknown`
>    (nothing held, no DLL to ask) and the sliders show **200 %** and **50 %**, with
>    `Combined player speed: 1× (world 2 * pawn 0.5)` recomputed from the restored pair.
>
> With no game and no DLL in the picture, disk is the only place those numbers could have come from.
>
> 🔎 **Note for the next session:** the sliders are deliberately left at **200 % / 50 %** — that is
> this row's evidence sitting in `ui-options.json`, not a stray setting. Nothing is *held* on any
> game (`State: Unknown`); one click on either **Reset** returns them to 1×.
>
> ℹ️ Two keys worth knowing before grepping: `ui-options.json` is **nested by section**, so these live
> under `teleport`, not at the root, and they are **camelCase on the wire** (`worldTimeDilation`)
> while the C# properties are `WorldTimeDilation`. A root-level flat lookup returns "absent" and
> reads exactly like the save never happening.
> | X11 | start a **Dump All** and abort it (disconnect / cancel) mid-stream | there is **no** truncated `.jsonl` at the chosen name — only a `<name>.partial` (or nothing); a completed dump appears atomically at the final name | temp-then-rename is only observable against a real abort |
> ### ✅ X6 + X11 PASS 2026-08-20 `[L6-X6-X11-2026-08-20]` — both halves, on DumperTest / dist 3263
>
> Same host, two runs: one **aborted** mid-stream and one allowed to **complete**. Both halves are
> needed — the abort alone cannot show that a finished dump publishes atomically, and the completion
> alone cannot show that an abort publishes nothing.
>
> **X6 — the abort is prompt, and it is reported.** Shared with `AC10` above: `taskkill /F` on the
> host with the `.partial` at **589,824 bytes and growing**.
> * `pipe-0.log` `ReadLine returned null (disconnected)` at **11:10:09.745**
> * `view-0.log` `DumpAll export cancelled` at **11:10:09.752**
> * → **7 ms** from dead pipe to abort. That number is the row: before the fix `ct` was `default`,
>   every service ct-check was dead code, and the export would have kept issuing per-class round
>   trips into a dead pipe. It did not hang, and the UI said **"Dump cancelled (disconnected)"**.
>
> **X11 — nothing at the final name on abort; the whole file at once on success.**
> * *abort run*: the `.jsonl` **never existed**, and the `.partial` was deleted.
> * *completion run*: polled at 50 ms throughout —
> ```
> t=13.28  partial 0 bytes            final -
> t=14.67  partial 3,145,728          final -
> t=17.14  partial 10,223,616         final -
> t=17.20  partial -                  final 10,484,429    <- one step, already full size
> ```
> The final name is **never observed at a partial size**: it goes from absent to complete inside a
> single 50 ms poll, which is the rename. The published file ends with its trailing
> `{"kind":"summary","classes_emitted":3942,…,"objects_scanned":25172}` line, and `view-0.log` agrees
> — `DumpAll exported to … (10484429 bytes, 3942 classes, 0 errors)`.
>
> ⚙ Operational note for whoever drives the rest of this table: **a game window steals focus back**,
> so a computer-use click on the UI behind it only re-activates the window and the button never
> fires — silently, with no error. Two Connect clicks were swallowed that way before
> `tools/verify/front_window.py front UE5DumpUI` was run first. The tell is the UI's `pipe-0.log`
> showing **no connect attempt at all**.
> | X12 | (maintainer) install CE under **%ProgramFiles%** (write needs elevation), run the app **non-elevated**, click **Install CE autorun** | it falls back to the manual save dialog ("… not writable — choose where to place it…") instead of failing | the denied-write branch needs a real non-writable CE folder |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L2 (T1a Radar) end-to-end: AB12 / AB13 / AB14 / AB16 / AB17

*The pure logic of each is unit-tested in `dll_helpers_test` (AB14 resolution, AB15 octal, AB16
`FormatCandidateOrigin`, AB18 witness distinctness, AB19 leaf budget), and AB8/AB10/AB11 are
compile-verified obvious fixes. What is NOT reachable from a test is the integration through Aura /
CE injection / the pipe, which is what this batch checks. AB15/AB18/AB19 need no live check (fully
unit-tested); AB9 stays OPEN (loader-lock, out of L2 scope).*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ AB14 + AB16 PASS 2026-08-19 `[ABRADAR-2026-08-19]` — over the pipe, no UI
>
> Rig: `tools/verify/ab_radar_batch.py`, DumperTest Development / dist 3263.
>
> * **AB14 — PASS.** A `NumericAll`/`Exact 1` scan (3,132 candidates) returns **834 byte/enum-typed
>   candidates: 370 `EnumProperty` + 464 `ByteProperty`** — e.g.
>   `ToolMenuEntryScript.Data.Advanced.UserInterfaceActionType` (EnumProperty) and
>   `SparseVolumeTextureViewerComponent.IndirectLightingCacheQuality` (ByteProperty). Before the fix
>   these read as 1 byte and were invisible to every value scan, so a non-zero count *is* the
>   result; no baseline is needed.
> * **AB16 — PASS, and it partitions exactly.** Scanned `Int32` with `native_c=true` (372
>   candidates), then drove the **server-side** `filter` — which is where the defect was; the UI
>   textbox is only its front end. `filter=native` → **278** (all genuine raw holes, e.g.
>   `SparseVolumeTextureViewer.<raw@0x230>`); `filter=reflected` → **94** (e.g.
>   `GenlockedFixedRateCustomTimeStep.FrameRate.Denominator`).
>   ⭐ **278 + 94 = 372 = the total.** Every candidate matched exactly one of the two Origin
>   spellings, which is stronger than "both filters returned something": it shows the filter is
>   reading `FormatCandidateOrigin` for *every* row rather than incidentally matching a substring
>   somewhere else in a few of them.
> * ⚠ The rig refuses to judge AB16 unless the scan actually produced Native-C rows. With
>   `native_c` off every candidate is `Reflected` by construction, so `filter=native` returns 0
>   legitimately and the row would read as still-broken; that case is reported INCONCLUSIVE, not FAIL.
> * **AB17 / AB12 / AB13 not run** — AB17 needs wall-clock idling, AB12 a >1024-module process, AB13
>   a non-ASCII install path (maintainer-only).

> | AB14 | on any UE game, run a **Value Search → NumericAll** scan for a value held by a known enum-backed field (e.g. a character state / difficulty enum) | the enum field now appears among candidates (it read as 1 byte); before the fix it was invisible to every value scan | the resolution is unit-tested, but whether Aura's meta scan actually emits enum candidates is only observable live |
> | AB16 | enable **Native-C** in Value Search, scan, then type `native` (and `reflected`) into the results filter box | rows visibly reading "Native-C (Int32)" match on `native`; "Reflected" rows match on `reflected` | before the fix the server-side filter ignored the Origin column and returned zero |
> | AB17 | ✅ **PASS 2026-08-20 `[AB17-REAP-2026-08-20]` — both halves, headless.** Rig: `tools/verify/ab17_session_reap.py`, against the real `kScanSessionIdleExpiry` of **300 s** (`Radar.h:837`). **Reap:** session A idled **320 s**, then a second `begin_value_scan` (session B) swept it — a query against A returns `ok=false, error="session_not_found"` while B answers normally. ⭐ The explicit error is what makes this meaningful: had a dead id returned an empty-but-ok reply, "0 candidates" would be indistinguishable from "reaped", so the rig asserts on `ok`/`error` and never on the row count. **Protect-mine-first:** session C idled **320 s** and was then REFINED — `refine_value_scan` returned `ok=true, remaining=94` and C stayed alive, i.e. a Refine protects its own session *before* sweeping others. A wrong ordering would have had C reap itself and the refine fail. Both are wall-clock behaviours, which is exactly why no unit test can reach them. | | |
> | AB17 | begin a value scan, do a Next Scan or End, leave the app connected & idle; separately, start a 2nd scan much later | a stale earlier session is reaped on the next Begin/Refine/End (memory does not accumulate); the session being refined is NOT dropped when you step away mid-refine | the sweep trigger + the "protect my own session" ordering are not unit-testable (wall-clock) |
> | AB12 | attach CE to a process with **>1024 loaded modules** and click Inject & Connect (or click it twice) | the "already loaded" / post-inject check correctly finds our DLL even past module 1024; a successful inject is never reported "not mapped" | needs a real large-module process |
> | AB13 | (maintainer) place the CE plugin DLL under a path with **non-ASCII characters** and Inject & Connect | injection succeeds (8.3 short-path fallback) and the log shows the exact UTF-8 path | needs a non-ASCII install path; ASCII paths are unchanged |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L7 (T1d UI Services): AC3 / AC6 / AC10 / AC11 / AC12

*Ten findings closed (AC3–AC12); **five need nothing live**. AC4/AC5 (corrupt-cache quarantine) and
AC6's sweep policy are unit-tested end to end against a real temp folder, and AC7/AC8/AC9 are
comment-vs-code corrections in `ClassLocationScorer` with **no scoring change for any game** — AC9's
deleted `UCheatManager` row was strictly subsumed by the `CheatManager` row beneath it, proven by a
negative control that turns exactly one assertion red. The rows below are the parts a test cannot
reach: a real CE, a real Steam install, a real game dying mid-write, and a real game folder.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | AC3 | with CE **closed**, use any "to CE" action (Teleport → AOBMaker push, Console → Debug Camera). Then open CE **without** the AOBMaker plugin and repeat. Then load the plugin and repeat. `rg "AOBMaker bridge" %LOCALAPPDATA%\UE5CEDumper\Logs\*\init-0.log` | three DIFFERENT lines: "no server on \\\\.\\pipe\\AOBMakerCEBridge within 2000 ms" (twice, Debug) then a success — and if CE is up but the pipe refuses, a **Warn** naming the exception type | before the fix the bare `catch` logged nothing at all, so all seven call sites produced the same blank "AOBMaker not connected" |
> | AC6 | plant `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.<MACHINE>.json.tmp.99999`, backdate its mtime >1 h, plant a second one with a **fresh** mtime, then launch the UI and connect to a game (any scan records) | the backdated one is gone, the fresh one **survives**, and `init-0.log` says "removed 1 abandoned staging file(s) older than 1 h" | the sweep is unit-tested, but the cross-process pairing with the DLL's identically-named `<file>.tmp.<pid>` is only observable with a game actually writing the same cache |
> | AC10 | start a long operation over the pipe (Dump All, or a big Value Search), then **kill the game process** mid-stream | the UI reports a **failure** ("disconnected"), not a silent cancel; a partial export is not finalised as complete | the write-vs-death race is a TOCTOU the classifier's unit test cannot drive; only a real kill hits it |
> ### ✅ AC10 PASS 2026-08-20 — killed 590 KB into a Dump All, reported as a failure, nothing published
>
> Dump All Metadata (.jsonl) over a connected DumperTest (25,179 objects), then `taskkill /F` on the
> host **while bytes were still moving**.
>
> ⚠ **A fixed sleep cannot stage this row and will pass without testing anything.** Fire early and
> the dump has not begun, so the disconnect is an ordinary idle one; fire late and it already
> published. Both look identical in the UI. `tools/verify/ac10_kill_midstream.py` triggers off the
> `.partial` instead — the dump streams to `<name>.jsonl.partial` and is renamed only after the
> trailing summary line, so a *growing* `.partial` is direct evidence of an in-flight stream.
>
> ```
> MID-STREAM: DumperTest-dump-20260820-110923.jsonl.partial is 589824 bytes -- killing DumperTest.exe NOW
> taskkill rc=0   (PID 6340 terminated)
> 3 s after the kill:  .partial still present : False      FINAL name published : False
> ```
>
> | assertion | result |
> |---|---|
> | the UI reports a **failure**, not a silent cancel | status → **"Dump cancelled (disconnected)"**; `view-0.log` `DumpAll export cancelled` |
> | the disconnect is detected, not hung | `pipe-0.log` `ReadLine returned null (disconnected)` → `Pipe disconnected`, 11:10:09.745 |
> | a partial export is **not** finalised as complete | the `.jsonl` **never existed**; the `.partial` was deleted (`TryDeletePartial`) |
> | the UI stays usable | reverted to `Connect`, Object Tree cleared, no error dialog, no hang |
>
> The temp-then-rename design (X11) is what makes the last row true by construction, and the
> `CreateLinkedTokenSource(_connectionCts.Token)` (X6) is what turns the dead pipe into the
> `OperationCanceledException` that produces the "(disconnected)" wording rather than a generic
> "Dump failed" — both were exercised on this path, 590 KB in.
> | AC11 | on an installed game: **Deploy** a proxy to a clean Binaries folder, then **Deploy again** over it, then **Undeploy**. Check the folder for any `*.ue5dump-stage` leftover | all three succeed exactly as before; no `.ue5dump-stage` file is ever left behind; the grid never shows "Other proxy" for a DLL we just wrote | staging changed the publish from a copy to a copy+rename — the first-time-deploy path and the locked-target path are the two that must not regress |
> ### ✅ AC11 step 1 PASS 2026-08-20 — deploy / re-deploy / undeploy leave nothing behind
>
> Driven through the real panel on **Light Maze** (`D:\SteamLibrary\…\LightMaze\Binaries\Win64`),
> picked because it was one of seven installed UE titles with **no** proxy of ours — so the
> first-time-deploy arm of `File.Move(overwrite: true)` is the one actually exercised. The folder was
> hashed to a 4-file baseline first.
>
> | # | action | grid | folder |
> |---|---|---|---|
> | 1 | Deploy | `NotDeployed` → **`DeployedCurrent`**, "Deployed: 1 success, 0 failed" | `dxgi.dll` 2,876,928 B appears; **no** `.ue5dump-stage` |
> | 2 | Deploy again, **Force Overwrite** | stays `DeployedCurrent`, "1 success, 0 failed" | same size, **no** `.ue5dump-stage` |
> | 3 | Undeploy | → **`NotDeployed`**, "Removed: 1 success, 0 failed" | **byte-identical to the baseline**, zero `*ue5dump*` residue |
>
> The status never once read **"Other proxy"** for a DLL we had just written — the truncated-PE
> failure mode the staging change exists to prevent.
>
> ⚠ **Force Overwrite is REQUIRED for step 2 to be a real test.** `PlanDeploy` returns
> `AlreadyCurrent` when the target is ours and the same version, so a plain second Deploy reports
> "1 success" **having written nothing**. The button says success either way.
>
> ⚠⚠ **`mtime` cannot witness a re-deploy, and it silently says "no write happened".**
> `File.Copy` copies the source's last-write time, and `File.Move` preserves it, so the deployed
> `dxgi.dll` carries `dist/proxy/dxgi.dll`'s timestamp **exactly** — identical before and after the
> second deploy. Use **`ctime`**, which does move on a rename-replace:
> `1787195042681345900 → 1787195091349982000`. The `view-0.log` pair confirms it independently
> (`Deployed dxgi.dll to Light Maze` at **11:04:02** and again at **11:04:51**).
>
> 📁 **ProxyDeploy logs to `view-0.log`, not `init-0.log`.** Worth knowing before grepping for this
> row's evidence — and note the category tag (`"ProxyDeploy"`) only *routes* the file, it is never
> printed, so grep the message text (`Deployed`, `Undeployed`) and not the category.
> | AC11 | with the game **running** (so the proxy is loaded and locked), click Deploy | still "File locked (game running?)", and the existing proxy is intact | the rename now raises the sharing violation the direct copy used to; the message must not change |
> ### ❌ AC11 step 2 FAILS — the message DID change `[STAGELOCK-2026-08-20]` **(new defect, build 3263)**
>
> The row's own premise is wrong, and that is the finding. "The rename now raises the sharing
> violation the direct copy used to" is **not true**: a locked target and a *mapped* target are
> different kernel states, and the two publish shapes hit them through different paths.
>
> **Measured at the OS level** (`tools/verify/ac11_locked_rename.py` — a reproducer, so it exits 1
> while this stands). Target mapped as a real image section by a live process, exactly as a running
> game's loader does it:
>
> | publish shape | Win32 error on a mapped target |
> |---|---|
> | **OLD** `File.Copy(src, target, overwrite)` — opens the target for write | `ERROR_SHARING_VIOLATION` (32) |
> | **NEW** `File.Move(stage, target, overwrite)` — renames over it | **`ERROR_ACCESS_DENIED` (5)** |
>
> A file carrying an image section refuses *deletion* with `STATUS_CANNOT_DELETE`, and the replacing
> rename has to delete the target. The negative control (nothing mapped) has **both** shapes
> succeeding, so this is the lock talking, not a broken path.
>
> **Confirmed one level up against the real `ProxyDeployService.CopyProxyStaged`**, called from a
> throwaway xunit test with the target mapped in-process:
> ```
> System.UnauthorizedAccessException   HResult = 0x80070005
> Message = "Access to the path is denied."      <- it does not even name the path
> DeployAsync's "File locked" filter catches it?  False
> stage file left behind?  False        target still intact?  yes
> ```
>
> **What the user sees.** `DeployAsync` filters on
> `catch (IOException ex) when (ex.HResult == 0x80070020 || ex.Message.Contains("being used"))`
> ([ProxyDeployService.cs:1152](ui/UE5DumpUI/Services/ProxyDeployService.cs:1152)).
> `UnauthorizedAccessException` is **not an `IOException`**, so it misses *both* arms and falls to
> the generic handler: the row goes to **`ErrorOther`** with **"Access to the path is denied."**
> instead of **`ErrorLocked`** / **"File locked (game running?)"**. That message names no path and
> reads as a permissions problem, so the natural user response is to re-run as administrator — which
> cannot help, because the file is not permission-denied, it is *in use*.
>
> **The other two halves of the row PASS**: no `.ue5dump-stage` survives (the `finally` fires on this
> path too) and the live proxy is left byte-intact.
>
> ⭐ **The fix is already written three lines away, twice.** `UndeployAsync`
> ([:1269](ui/UE5DumpUI/Services/ProxyDeployService.cs:1269)) and the orphan sweep
> ([:1813](ui/UE5DumpUI/Services/ProxyDeployService.cs:1813)) each carry an explicit
> `catch (UnauthorizedAccessException)` arm. `DeployAsync` is the only one of the three without it —
> and the only one whose write turned into a rename. So the fix shape is to add the same arm (or
> widen the filter to `0x80070005`), not to redesign staging. ⚠ Deliberately **not applied here**:
> this session verifies, it does not fix.
> | AC12 | on this machine (multi-library Steam install), open Proxy Deploy and let it scan | the same library folders as before are found; `proxy`/`init` log has **no** "libraryfolders.vdf is malformed" line | the parser is fully unit-tested but its input is a real Valve-written file — a rejected real VDF would silently halve game detection |
> ### ✅ AC6 + AC12 PASS 2026-08-20 `[L7-AC6-AC12-2026-08-20]`
>
> **AC6 — PASS, with the DLL's own sweep deliberately taken out of the way.** The DLL sweeps the
> *same* file family once per process, so the two bait files were planted **after** DumperTest was
> injected and had already swept — otherwise the DLL removes the bait, the UI legitimately logs
> nothing, and that reads as a failure of the UI sweeper. Then the UI was launched and connected.
>
> * backdated `UE5CEDumper.MSI-NB.json.tmp.99999` (3 h old) → **gone**
> * fresh `UE5CEDumper.MSI-NB.json.tmp.88888` → **survives** — this is the age guard, and it is the
>   half that matters: without it the sweeper would delete the DLL's in-flight write.
> * `init-0.log`: `AobUsageService: removed 1 abandoned staging file(s) older than 1 h`
> * the real cache still parses afterwards (28 entries)
>
> ⭐ **This is the C# twin of the DLL-side sweep, and the pair is now verified end to end**: the DLL
> logs `HintCache: removed 1 abandoned staging file(s) older than 1h` (see `[FLSWEEP-2026-08-19]`)
> and the UI logs `AobUsageService: removed …`. Two independent sweepers over one file family, both
> age-guarded, neither destroying the other's live write — which is exactly the cross-process
> pairing this row says is only observable with a game actually writing the same cache.
>
> **AC12 — PASS.** A live **Scan Steam** in the same session logged `Found 2 Steam library
> folder(s)` and then `Found 18 UE game(s)`, with **zero** library-related `[WARN]`/`[ERROR]` lines
> (no "malformed"). The 2 matches `libraryfolders.vdf` exactly — `C:\Program Files (x86)\Steam` and
> `D:\SteamLibrary` — so the real Valve-written VDF is still parsed and the multi-library install is
> not silently halved.

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L9 (T1c VMs/Core/DTOs): AE13 / AE20 / AE30

*Seventeen findings closed (AE11–AE25, AE30, AE31); **fourteen need nothing live**. AE12 and AE22
turned out to be **already fixed** by `6fc00e4d` (X5's `ClearOnDisconnect` fan-out) and were closed
by reading the current code, not by re-fixing it. AE25 and AE31 are **doc-only, direction
re-derived**: AE25's comment claimed `Between` was excluded from the group scan-type picker while
listing it fourth — the CODE is right (three independent witnesses: the spec, the DLL's `value2`
parser, and this VM's own validator), so removing the option would have deleted a shipped feature to
satisfy a stale comment. Everything else is unit-pinned with one combined negative control:
reverting the behaviours turns **21** tests red across all five affected classes and leaves every
"must NOT change" control green.*

⚠ **AE13's half is DLL-gated** — the UI defaults `per_slot_cap_hit` to `false`, so a stale injected
DLL makes it look like a no-op rather than a failure. Compare against `dist/build_number.txt`, not
the repo's (`[STALEDLL]`).

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | AE13 ⚠ DLL-gated | Value Search → **Group** mode, two slots, values chosen to be COMMON (e.g. `0` and `1`, or `100` and `100`) so one slot matches far more than 256 fields on some object; Group First Scan, then Group Next Scan | the status line gains `⚠ a slot matched more than 256 fields — only that many were kept, so "All fields" is a page …`, and the SAME clause survives the Next Scan | this fact was computed by `Orden::MatchGroup` and written only to `LOG_WARN` inside the DLL, so no user could ever see it. Distinct from `deadline_hit`: the result set is complete while a slot's field list is not. **Negative half worth doing:** repeat with DISTINCTIVE values (a real HP + a real MP) and confirm the clause does NOT appear |
> | AE20 | Proxy Deploy → **Find leftovers** on a machine with several leftover chains, tick 3+ rows, **Delete checked**, then click **Cancel operation** while it runs | the pass stops early and the result line reports what DID happen (`… cancelled`), with the un-processed rows still listed and unticked | the destructive Recycle-Bin delete accepted a `CancellationToken`, checked it between rows and carried a whole cancelled-reporting path that **nothing in the app could reach** — five of the panel's seven token-taking commands were in that state. ⚠ Needs several rows: with one row the loop finishes before a human can click |
> | AE30 | Object Tree → pick any UObject → set the address format to **module+offset** → Copy address, and paste into CE. Then relaunch the game and paste the same string again | the copied text is now bare hex (e.g. `1E55C298D40`), NOT `"Game-Win64-Shipping.exe"+FFFF81…`; it resolves this run and plainly fails to resolve after a relaunch instead of silently pointing somewhere unrelated | a heap UObject sits BELOW a `0x7FF7…` image base, so the old unsigned subtraction WRAPPED. That string round-trips within one run, which is what made it dangerous: it looked like the ASLR-stable form the user picked the option for. **Control:** copy an address that IS inside the module (a GObjects/GNames pointer from the Pointers panel) and confirm it still formats as `"exe"+RVA` and still resolves after a relaunch |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L8 (U5 VMs + scoring): Z8 / Z12 / Z13

*Thirteen findings closed (Z4–Z16); **ten need nothing live**. Z4/Z5/Z6/Z7/Z9/Z10/Z11/Z15 are all
unit-pinned at the VM level with a negative control each (reverting the eleven behaviours at once
turns **34** assertions red and leaves every "must NOT change" control green), Z14 is a comment-only
correction with **no score change for any game**, and Z16 was already fixed by `dcafa5fe` — verified
by grep, not assumed. The three rows below are what a test cannot reach: two are **DLL-gated** (the
UI defaults the new flags to "assume complete", so the disclosure only appears once the freshly
built `UE5Dumper.dll` is the one injected — a stale DLL makes both look like no-ops rather than
failures), and one is a deliberate scoring change worth one pair of human eyes on a real game.*

⚠ **Before running any of these, confirm the injected DLL is THIS build** — `[STALEDLL]` is exactly
the trap that makes a DLL-gated fix look unshipped. Compare against `dist/build_number.txt`, not the
repo's.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | Z8 ⚠ needs a BIG game | on a title with more than 100,000 UFunctions (a **SEED / FF7R**-class pool; `game_only` OFF makes the cap far easier to reach on any title), open **Console** and Load, then open **Interesting Functions** and Load | Console no longer claims anything about the GAME: it reads "No UFUNCTION(exec) commands in the N functions scanned so far … this scan did not finish, so it is not evidence the game has none", plus `⚠ STOPPED at the 100,000-row cap`. Interesting Functions shows the same cap suffix AND its class-noise picker now shows `⚠ Counts are partial` | the DLL emitted **no truncation marker at all** for `list_all_functions` before this, so a capped page was reported as a complete census of the game — and Interesting Functions had no flag it could even pass to its picker. A game UNDER the cap proves only that the flag stays off (still worth doing as the regression check: no spurious warning) |
> | Z12 | Instance Finder → **Address → Instance** on an address that lives in a deeply-nested container (the `SaveSlotList[].MsTuneData…` shape the deep descent was written for), and on a plainly-bogus address | on a deep HIT the suffix reads `[scanned (incl. deep descent) X/Y in Zms]` with the DEEP pass's counters and the SUMMED duration; on a deep MISS it adds `⚠ the deep descent probes at most 256 element(s) per container, so this miss is not proof of absence` | before, a deep success reported the SHALLOW pass's numbers (describing a pass unrelated to the answer) and dropped the deep pass's deadline flag; a deep miss never mentioned the element cap at all. Change the Options element cap and re-run — the suffix must name the value you set, not a constant |
> ### 🟡 Z12 deep-MISS half PASS · Z13 NOT RUNNABLE on DumperTest — 2026-08-20 `[L8-Z12-Z13-2026-08-20]`
>
> **Z12, the deep-MISS caveat — PASS, including the part that could have been faked.** Instances →
> `Lookup` on a plainly-bogus `0x1234567800`, DumperTest / dist 3263:
> ```
> No UObject found at this address  [scanned (incl. deep descent) 25,179/25,179 in 202ms
>                                    — ⚠ the deep descent probes at most 256 element(s) …]
> ```
> Then **Options → "Deep container scan cap" 256 → 1024** and re-ran the identical lookup:
> ```
> …in 102ms — ⚠ the deep descent probes at most 1,024 el…
> ```
> ⭐ That second run is the real assertion. A hard-coded "256" would have read correctly on the first
> run and been indistinguishable from the fix; the suffix tracking the option to **1,024** shows it is
> reporting the cap actually used. The duration also moved (202 → 102 ms), so it re-scanned rather
> than re-rendering a cached line. The cap has been **restored to 256**.
>
> ⚠ **Z12's deep-HIT half is NOT covered and cannot be, here.** It needs an address that lives in a
> nested container — the `SaveSlotList[].MsTuneData…` shape. DumperTest has no such fixture: a
> Property Search for `Tune` over all 3,942 classes returns exactly **one** row
> (`SynthKnob.MouseFineTuneSpeed`, a plain float), and there is no `SaveSlotList`. So "the DEEP pass's
> counters and the SUMMED duration on a hit" still needs a title with that shape.
>
> ⚠ **Z13 is NOT runnable on DumperTest** — it has no HP-named property to hover. Measured rather
> than assumed: Interesting Properties loaded **794 unique properties (threshold 4+: 530)** and
> filtering them for `hp` returns only incidental substring hits — `MaxDepenetrationWithPawn`,
> `NavMeshProjection…`, `DynamicMeshProperties`, i.e. the "…hP…" inside *WithPawn* / *MeshProjection*.
> Not one row has `HP` as a name token, so the `keywords(1 hits)` tooltip has nothing to appear on.
> **This row needs an RPG-ish title** (an `HP` / `CurrentHP` property). Worth pairing with `Z8`, which
> also needs a big real game.
> | Z13 | on any game, open **Interesting Properties** and **Interesting Functions** and sort by Score; find an HP-named row and read its score tooltip | the tooltip reads `keywords(1 hits)` for a plain `HP`/`CurrentHP` name, not `keywords(2 hits)`, and that row scores **5 lower** than it did before | this is the one DELIBERATE score movement in the batch and it is not silent: `"HP"` and `"Hp"` both tokenised to `["hp"]`, so one keyword was counted twice. Nothing visible on HP alone becomes hidden (10 → 5, both thresholds ≤ 5), but an HP function on an `Anim*`/`Niagara*`/`Sound*`/`Particle*` class (−2 class penalty) goes 8 → 3 and correctly drops below the threshold. ⚠ **What to actually watch for: an HP row you EXPECTED that is now missing from the default view** — if one appears, it is a threshold crossing, and the fix is "Show all", not re-adding the duplicate |

### ⬜ PART-FIXED 2026-08-19, NEEDS A LIVE CHECK `[PROXYLOAD-2026-08-17]` — `DeployedCurrent` no longer means "silently ignored"

*Was: the Proxy Deploy panel's `DeployedCurrent` is computed from the file on DISK only; it does NOT
mean the game loads the proxy. Measured on **OCTOPATH TRAVELER**: `version.dll` byte-identical to
`dist/proxy`, panel said `DeployedCurrent 1.0.0.3262`, yet the log folder never appeared and only
`C:\WINDOWS\SYSTEM32\VERSION.dll` was in the module list — the app-dir proxy is silently ignored. The
correlation was 3 for 3: a title that STATICALLY imports the proxy's base name gets the loader
satisfying the import from an already-mapped module (an overlay/launcher such as Steam maps it early)
and never searches the app dir; titles that don't import it get ours. DQ I&II, same flavour/build,
had BOTH `VERSION.dll` (ours) and System32's mapped and worked. ⚠ The load-order mechanism FITS but
is **untested** — `KnownDLLs` was refuted (none of version/dxgi/winmm/dinput8 is a KnownDLL), so it is
treated as a HEURISTIC, not a law.*

**What shipped (both offline halves, 2026-08-19):**
1. **Static-import BYPASS screening** — a small AOT-safe managed PE import reader already existed
   (`ProxyImportAnalyzer.Analyze`, unit-tested against synthetic PEs). Added `DescribeImportBypassRisk`
   / `DescribeDeployAdvisory`: when the chosen flavour IS named in the exe's import table, the deploy
   note and the Suggested column warn it may be pre-empted by an already-mapped copy and suggest a
   flavour it does not import, or direct injection. Screens all four base names. **Worded as a
   heuristic** (it can false-positive — OCTOPATH imports winmm yet its winmm proxy WORKS, so the load
   signal below is what actually settles it per-game).
2. **A "did it actually load?" signal** — a new **"Loaded?"** grid column, refreshed on every scan/
   refresh from the per-process log folder the DLL creates on load
   (`%LOCALAPPDATA%\UE5CEDumper\Logs\<exe-base-name>`; join key mirrors `dll/src/Sein.cpp
   InitProcessMirror` exactly). States: **"loaded &lt;date&gt;"** (folder present & fresh),
   **"loaded &lt;date&gt; (stale)"** (present but > `LogMaxAgeDays` old — a previous run/build, never
   claimed as "loaded now"), **"not observed"** (absent → honest UNKNOWN, NOT a failure claim for a
   game that simply hasn't been launched). Disk `Status` + "not observed" is the OCTOPATH silent
   failure, now visible. Pure logic (`ClassifyLoad` / `ProcessLogFolderName`) is unit-tested; the
   folder lookup is exercised end-to-end via a temp-appdata service test.

> ### 📊 THE IMPORT-BYPASS HEURISTIC, MEASURED 2026-08-20 `[PROXYLOAD-CORR-2026-08-20]` — it false-positives 4 times out of 4
>
> The row states the mechanism "FITS but is **untested**" and cites a 3-for-3 correlation. It is now
> measured across every title on this machine that has a proxy deployed, cross-referencing two facts
> already on disk: the exe's **static import table** (the same `tools/pe/pe_imports_exports.py` the
> row names) and whether the DLL **actually loaded** there — i.e. whether
> `Logs\<exe-base-name>` exists, which is exactly the join the new "Loaded?" column uses.
> Rig: `tools/verify/proxyload_correlation.py`.
>
> | title | deployed | imports that name? | loaded? | |
> |---|---|---|---|---|
> | Avowed | `dxgi` | **YES** | **yes** | counter-example |
> | EVERSPACE | `version` | **YES** | **yes** | counter-example |
> | OCTOPATH TRAVELER | `winmm` | **YES** | **yes** | counter-example (the row already knew this one) |
> | Elliot | `dxgi` | **YES** | **yes** | counter-example |
> | DQ7R · Lushfoil · Manor Lords · Geri | `version` | no | yes | as expected |
> | EVERSPACE 2 | `version` | *(exe unreadable by the parser)* | yes | not counted |
>
> **4 titles import their deployed flavour; all 4 loaded our proxy anyway. 0 titles imported it and
> failed to load.** So on this machine the warning would fire four times and be wrong four times.
>
> ⚠ **This does NOT refute the row's own 3-for-3**, and must not be read as doing so: that was
> measured on *specific flavour/title pairs* (notably OCTOPATH with **`version`**, which genuinely is
> bypassed), whereas this samples **whatever flavour happens to be deployed now** (OCTOPATH currently
> has `winmm`, which works). Both are true. The point is narrower and still useful: **a static import
> is not a prediction of bypass**, and the screening is right to be worded as a heuristic rather than
> a verdict.
> ⚠ **Observational, not controlled** — nobody deployed each flavour to each title on purpose. Note
> also what is *absent*: **zero** "imports it, no log folder" cases, which is the only shape that
> would have supported the heuristic — and even that one would be ambiguous, since "never launched"
> explains a missing folder equally well. Keeping "not observed" distinct from "bypassed" is the
> whole point of the `Loaded?` column.
>
> ⇒ **Steps 1–3 below still need the UI** (the Suggested / Loaded? columns are what they assert).
> What is settled here is the *data* those columns render, and that the heuristic's false-positive
> rate on real titles is high rather than incidental.

> Needs a running game. No sample was captured for the code path, so this is a real live check.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 ⚠ THE ONE THAT MATTERS | deploy `version.dll` to a title that STATICALLY imports version.dll (**OCTOPATH**; confirm with `py tools/pe/pe_imports_exports.py imports <exe> --dll version`), launch it, then **Scan/Refresh** the panel | Suggested column WARNS ("imported, may be bypassed"); after launch the **Loaded?** column stays **"not observed"** next to `DeployedCurrent` | before the fix the panel said only `DeployedCurrent` and nothing flagged the silent failure |
> ### ✅ STEPS 2 + 3 PASS 2026-08-20 `[PROXYLOAD-UI-2026-08-20]` — the `Loaded?` column is real, and it separates "not observed" from "loaded"
>
> Proxy Deploy → Scan Steam, `Found 18 UE game(s)`, `Source DLL v1.0.0.3263`. Read straight off the
> grid (Status · **Loaded?** · Suggested proxy):
>
> | title | Status | **Loaded?** | Suggested |
> |---|---|---|---|
> | **Echoes of Aincrad Demo** | NotDeployed | **`not observed`** | version · default · alt: dxgi |
> | **DQ7R** | DeployedOtherType | `loaded 2026-08-1…` | **`version.dll · confirmed work…`** |
> | **OCTOPATH TRAVELER** | DeployedOtherType (winmm) | `loaded 2026-08-1…` | `version · default · **imported**,` |
> | EVERSPACE | DeployedOtherType | `loaded 2026-08-1…` | `version · default · **imported**,` |
> | Avowed | **DeployedCurrent** | `loaded 2026-08-1…` | dxgi.dll · confirmed working |
> | Satisfactory | NotDeployed | `loaded 2026-08-2…` | version · default · imported, |
> | Solarpunk · DragonSword | NotDeployed | `loaded 2026-08-1…` | `injection · no proxy deployed` |
>
> * **Step 2 — PASS.** DQ7R does not import `version.dll`, has it deployed, and reads
>   **`loaded`** with **no bypass warning** (`version.dll · confirmed work…`). So the signal is not a
>   blanket "not observed" — it reads the real log folder.
> * **Step 3 — PASS.** OCTOPATH runs the **winmm** flavour, reads **`loaded`**, *and* its Suggested
>   column still says **`imported`**. That is precisely the row's point: the warning is a heuristic
>   and **the load signal, not the import table, is the per-game source of truth**.
> * ⭐ **"not observed" is demonstrated by a real never-launched title**, `Echoes of Aincrad Demo` —
>   not staged. That is the honest UNKNOWN the fix adds, and it sits next to 17 rows that DO say
>   `loaded`, so the column is discriminating rather than defaulting.
> * ⭐ **Direct injection is reported correctly too**: Satisfactory and Solarpunk are `NotDeployed`
>   yet `loaded` (they were injected, not proxied), with `injection · no proxy deployed` as the
>   suggestion. A disk-only view could not have said that.
>
> ⚠ **Step 1 NOT run.** It needs `version.dll` specifically deployed to OCTOPATH, which currently
> carries `winmm`; changing that means a deploy plus a launch. Note also
> `[PROXYLOAD-CORR-2026-08-20]` above measured the import warning false-positiving **4 of 4** on the
> flavours deployed today — so step 1 should be run expecting to *test* the claim, not to confirm it.

> | 2 | deploy `version.dll` to a title that does NOT import it (**DQ7R** / **DQ I&II**), launch, Refresh | **Loaded?** shows **"loaded &lt;today&gt;"**; no bypass warning | proves the signal is not a blanket "not observed" — it reads the real folder |
> | 3 | on OCTOPATH, switch to the `winmm` flavour, deploy, launch, Refresh | **Loaded?** → "loaded" (winmm proxy works per `[OCTOPATH-G2T3]`) even though winmm may also be imported | proves the warning is a heuristic and the load signal, not the import table, is the source of truth |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[SLOTSYM-2026-08-18]` — the slot `[DISABLE]` now actually unregisters, and says so honestly

*Was: on the `&GEngine` SLOT path the "Get GameEngine" record took the `mayFallBack` `[DISABLE]`
branch, where `unregisterSymbol` was nested inside the buffer-only `cur == mem` guard; with no
buffer, `mem` was nil, both arms were skipped, the symbol survived (a stale `UE_GameEngine` across a
game restart, resolving into the dead process's module base), and a trailing UNCONDITIONAL `dbg`
claimed it had been "unregistered". **Mechanism was read from the code, not either of the register's
two guesses:** it is NEITHER (a) a `getAddressSafe(sym)` guard returning falsy NOR (b) a
double-registration — ENABLE does a single `registerSymbol` on op 2, which matches the observed "one
manual `unregisterSymbol` sufficed". The register's `:256-258` cite was the GWorld branch (which
already unregistered correctly); the real code was the `mayFallBack` branch. Now: both slot ends
(GWorld + the GameEngine slot sub-path) go through shared
`CeLuaHygiene.AppendSlotSymbolRegister`/`AppendSlotSymbolRelease` emitters, so they cannot drift — a
per-symbol reference count in a CE Lua global keeps the symbol for a second still-ticked record, the
last holder unregisters it in a bounded loop, and the message re-reads `getAddressSafe` AFTER the
unregister so it claims success only when the symbol is actually gone. Also removed an accidental
duplicate `AppendContractCheck` (the block was emitted twice). Pinned by 6 new tests in
`PointerQueryScriptGeneratorTests` + a real-`lua` runtime simulation of the enable/disable sequence
(both cases below passed). Generator-only; contract surface untouched.*

> Needs a game whose `&GEngine` AOB validates so the record takes the SLOT path (DumperTest does).
> `UE5_DEBUG=1` in CE's Lua console to see the `dbg` lines.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ THE RELEASE LOGIC PASSES 12/12 UNDER REAL `lua` 2026-08-20 `[SLOTSYM-LUA-2026-08-20]`
>
> New rig `scripts/tests/slotsym_release_test.lua`. It runs **the script the shipping UI emitted
> today**, not a fixture: captured from Teleport → Global Pointers → **Get GameEngine** with AOBMaker
> offline (which copies the CE-XML), `<AssemblerScript>` extracted to
> `out/slotsym/get_gameengine.lua.txt`, then both `{$lua}` blocks executed over stubbed CE globals
> with the mailbox faked so ENABLE takes the **SLOT** branch — the branch that was broken.
>
> | case | result |
> |---|---|
> | **1. enable → disable** | ENABLE registers the slot address with **no buffer** and refcount 1; **one** DISABLE leaves `getAddressSafe('UE_GameEngine')` **nil** — no manual `unregisterSymbol` — and reports `UE_GameEngine unregistered` |
> | **2. two ticked records** | refcount reaches 2; the first DISABLE **keeps** the symbol and says `still held by 1 other record(s) -- left registered`; the second releases it |
> | **3. HONESTY (unregister neutered)** | the symbol still resolves, and the script says **`could NOT be unregistered after 8 attempt(s)`** — it does **not** print `UE_GameEngine unregistered`, and the retry loop is **bounded at 8** |
>
> ⭐ **Case 3 is the one that matters most.** The original defect was not only that the symbol
> survived — it was that a trailing *unconditional* `dbg` claimed it had been unregistered. The
> emitted script now re-reads `getAddressSafe` **after** the attempt and picks the message from
> that, so a failure cannot be reported as a success. Neutering `unregisterSymbol` proves the
> honesty branch is reachable and correct, which no amount of reading the source establishes.
>
> ⚠ **Scope:** CE's globals are stubbed, so this does not exercise Cheat Engine's own
> register/unregister semantics. It does exercise the thing that was wrong — the script's control
> flow, where both arms used to be skipped when `mem` was nil on the slot path. Step 1's live
> `print(getAddressSafe('UE_GameEngine'))` in CE would add only that CE agrees with the stub.

> | 1 ⚠ THE ONE THAT MATTERS | tick the single "Get GameEngine" record, untick it, then in CE's Lua console `print(getAddressSafe('UE_GameEngine'))` | **nil on the FIRST call** (no manual `unregisterSymbol` needed); the `dbg` reads `UE_GameEngine unregistered` | before the fix it stayed `0x…` after untick and the log lied |
> | 2 | paste the CE-XML to make a SECOND "Get GameEngine" record, tick both, untick the OLDER, `print(getAddressSafe('UE_GameEngine'))` | still resolves (survivor keeps it); the older record's `dbg` reads `still held by 1 other record(s) -- left registered`. Untick the second → now nil | the refcount half — two records resolve the IDENTICAL slot, so an address marker cannot tell them apart |
> | 3 ⚠ NON-REGRESSION | GWorld: tick "Get GWorld", untick, `print(getAddressSafe('UE_GWorld'))` | nil after untick | GWorld already unregistered before; must still |

-----

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[AUTOREFRESH-2026-08-19]` — Live Walker auto-refresh: the countdown can no longer freeze, and it comes back after a reconnect

*Reported by the maintainer from their own session on the **other PC**, running dist **1.0.0.3262**:
"Live Walker `Auto` refresh 無效，秒數數到0後就停在那" — the countdown runs down to 0 and sits there
while nothing refreshes. **Classification: B** (needs the UI + a game on screen, no human judgement —
Auto + computer-use can drive it).*

> **⚠ Read the evidence split before trusting any of this.** The logs are from a **different machine
> running 3262** and carry **none** of 2026-08-19's commits, so they are evidence about 3262 only.
> The auto-refresh block was re-checked and is **byte-identical** between `021053d6` (the last
> 2026-08-18 commit) and the dev tree, so code reading of it does transfer; nothing else does.
>
> **Log-proven** (`Y:\UE5DumpUI\pipe-0_005/006.log`, `view-0.log`, one UI session 12:27:16–12:55:44):
> - Auto-refresh issued **zero** refreshes. Every `walk_instance` in the 21-minute Elliot half maps
>   1:1 to a user action in `view-0.log`; the gaps between repeats of the same address are 7 s, 11 s,
>   187 s, 6 s, 187 s, 1.6 s, 26 s — **no periodic cadence exists anywhere in the session**. (1.6 s is
>   below `MinAutoRefreshIntervalSec`=6, so those cannot be ticks.)
> - **Not a dead dispatcher and not a dead pipe.** The Teleport poll's own `DispatcherTimer` ran at a
>   flawless ~500 ms (117–119 `teleport_get_pov`/min) for every minute from 12:36 to 12:55. A
>   negative control we got for free.
> - **Zero ERROR and zero WARN** in the UI logs for the whole window. The panel did nothing and said
>   nothing.
> - A real disconnect DID happen mid-session (12:33:17.109 `Pipe: ReadLine returned null`), 58.8 s
>   before the reconnect to a **different game** at 12:34:15.9. It is logged **only** in the pipe log
>   — `init-0.log` and `view-0.log` jump straight from one game to the next with no disconnect line
>   at all.
>
> **Code-proven** (reading 3262's source, which is identical here): `_countdownRemaining` was reset in
> exactly ONE place — inside `OnAutoRefreshTick`, *past* its early-return guard. `OnCountdownTick`
> decremented and clamped at 0. So **any** condition that keeps skipping the tick pins the label at
> `sec · 0s` forever while the Auto toggle still reads ON. `RefreshAsync` catches `Exception`
> internally, so a *failing* refresh could not have caused this — only a *skipped* one.
>
> **Narrowed by evidence, NOT proven — the one thing a live run still has to settle.** The guard had
> four conditions and three are excluded: `_isAutoRefreshing_InProgress` would have rendered
> `sec · refreshing...` instead of a number; `!HasData` / empty `CurrentAddress` are contradicted by
> three manual refreshes at 12:51:33 / 12:51:34 / 12:52:01 that DID walk `0x3A0F60240` (`RefreshAsync`
> returns immediately on an empty address). That leaves **`_isEditing`**, a latch set from
> `DataGridBeginningEditEventArgs` and cleared **only** from `CellEditEnded` — which Avalonia does not
> raise when it tears an edit down because the rows were replaced (`CancelEdit(…, raiseEvents:false)`),
> i.e. exactly what a Refresh or a navigation does to the field grid. **How it actually got stuck is
> not established offline** and no log records it.

*Fixed by making the whole class of failure impossible rather than betting on that last inference —
three changes, each independently unit-pinned:*

1. **The countdown cannot freeze.** New pure `Helpers/AutoRefreshCadence.cs` owns the rule; the
   counter **re-arms at zero** because it displays the timer's PERIOD, which keeps elapsing whether or
   not the last tick did any work. The reset also moved into `OnAutoRefreshTick`'s `finally`, so a
   future throwing `RefreshAsync` cannot strand it either.
2. **A skipped tick says WHY.** `AutoRefreshSkip` is surfaced in the status text — `paused (editing)`
   / `paused (no data)` — so "suppressed on purpose" can no longer be mistaken for "broken". And the
   `_isEditing` latch is now cleared wherever the grid is rebuilt (`UpdateDisplay`,
   `ClearDisplayedNode`, `ClearOnDisconnect`), which is where it could get stranded.
3. **It comes back.** A stop caused by something outside the user's control — the pipe dropping
   (audit X5's `ClearOnDisconnect`) or switching away from the tab — is now *resumable*, and the panel
   re-arms from `UpdateDisplay` once it is rooted on data again. A **user untick** and every
   **navigation re-root** deliberately do not resume. ⚠ The pending flag is only written by a stop
   that actually stopped something, because `NavigateToAddressAsync` calls the non-resumable overload
   on its way in — writing it unconditionally would have eaten the resume in exactly the path the
   maintainer walked (disconnect → reconnect → navigate).

> Tests: `AutoRefreshCadenceTests` (13). Shown able to fail: reverting the three behaviours to 3262's
> and re-running the class **fails 7 of them**, and the two "must not change" controls (user untick,
> navigation re-root) stay green through both.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEP 1 PASSES 2026-08-20 `[AUTOREFRESH-LIVE-2026-08-20]` — measured on the wire, not just on screen
>
> DumperTest Development + the AOT `dist` UI, Live Walker rooted on **GWorld** (`UWorld
> ThirdPersonMap`), **Auto** ticked, interval 10 s.
>
> * **The countdown cycles and re-arms.** Sampled every 4 s: `6s → 4s → 8s → 3s → 8s → 3s`. It
>   counts down and **wraps back up at least three times** in 20 s. The reported 3262 failure was
>   *"秒數數到0後就停在那"* — it stops at 0 forever. It demonstrably does not.
> * ⭐ **And the refreshes are REAL, measured to 0.1 s.** The UI's own `pipe-0.log` shows **13
>   `walk_world` requests**, and after the initial manual root (a 40.9 s gap) the gaps are:
>   `10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0` — **eleven consecutive ticks
>   at exactly the configured interval**, still running when the session ended.
>   This is the **exact inverse of the original diagnosis**, which was *"Auto-refresh issued **zero**
>   refreshes … no periodic cadence exists anywhere in the session"*. Same measurement, opposite
>   result.
>
> ⚠ **A trap worth carrying: the refresh command depends on the ROOT.** A GWorld-rooted view
> refreshes with **`walk_world`**, not `walk_instance`. Grepping for `walk_instance` (which is what
> the original investigation counted, correctly, for an *instance*-rooted view) returns **0** here
> and reads exactly like the bug. Check which command the current root actually issues before
> concluding an absence.
>
> Step **4** (proxy mode) not run; step **2** is settled below.

> ### ✅ STEPS 3, 5, 6, 7 PASS 2026-08-20 `[AUTOREFRESH-LIVE2-2026-08-20]` — every one measured on the wire
>
> Continues the run above on a **second** host and a **second** root type, so step 1 is now confirmed
> for an *instance*-rooted view too, not just GWorld. Throughout, the label was never trusted on its
> own: each verdict is the UI's `pipe-0.log` walk cadence.
>
> **Step 6 (non-regression) — first half PASS, and the second half's EXPECTATION IS WRONG.**
> * *user untick*: Auto ON → `sec · 6s`; untick → the button unlights and the label returns to plain
>   `sec`, still off 6 s later. **The untick sticks.** ✅
> * *drill into a child*: the row expects "stays OFF". It does **not** stay off — and it should not.
>   The `→` button is `NavigateToFieldCommand` → `NavigateToFieldAsync`, which is **not** one of the
>   six methods that call `StopAutoRefreshTimer` (`StartFromWorld`, `StartFromGameEngine`,
>   `NavigateToAddress`, `LocateInGWorld`, `LocateContainerInGWorld`, `LoadBookmark`). A field drill
>   never stops the timer, so "does not resume" never applies to it. What actually happens is the
>   useful thing, and it is correct on the wire — auto-refresh **re-targets to the new root**:
> ```
> 11:27:49.293  walk_instance 0x1C2444DBD80   <- the manual drill into PersistentLevel
> 11:27:54.857  walk_instance 0x1C2444DBD80   gap  5.6 s   (mid-cycle)
> 11:28:04.863 … 11:29:44.956                 gap 10.0 s × 12 consecutive
> ```
> It never walks the stale GWorld root again. ⚠ **Fix the row, not the code**: the fix note says a
> navigation re-root "does not *resume*", which is a statement about what happens after a stop — the
> step turned that into "must be off", which the code never did and the fix never claimed.
>
> **Step 5 (tab-leave / return) — PASS, and it genuinely stopped in between.** Auto ON, switch to
> Instances for 8 s, come back: the label reads `sec · 4s` and is running. The label alone would not
> settle it — a timer that never stopped looks identical. The wire does: ticks run at 10.0 s up to
> `11:30:04.971`, then **one 25.9 s gap** across the absence, then `11:30:30.838` and back to 10.0 s.
>
> **Step 7 (disconnect, empty panel) — PASS, and this is also X5's second row, auto-refresh half.**
> Game B killed at **11:31:04.943** with Auto ON and ticking. In the next 20 s the UI's pipe log
> gained **5 lines total** — the disconnect sequence — and **0 walk attempts**. No re-walk against a
> dead pipe, no spam. The Auto control itself is *hidden* while the panel has no data, so "the label
> reads `sec`" cannot be observed as written; the substantive half (nothing polls) is what was
> measured. Nothing polled for the **full 2 minutes** until a navigation.
>
> **Step 3 (THE MAINTAINER'S PATH) — PASS.** Killed game B (Shipping), started a **different** game A
> (Development), reconnected, and navigated. **Auto came back on by itself** — no click on the
> toggle:
> ```
> 11:31:04.9  game B killed, Auto was ON        -> 0 walks for 121 s
> 11:33:05.4  Start from GWorld (the navigate)  -> walk_world
> 11:33:15.5 … 11:34:05.5                       -> walk_world, gap 10.0 s × 6 consecutive
> ```
> ⭐ Both halves matter and they pull in opposite directions: it must **not** poll while
> disconnected *and* must **not** stay silently off afterwards. Pre-fix it stayed off for the rest of
> the session. Reconnecting also **reloaded game A's bookmark** into slot 1 (`ThirdPersonMap`) by
> itself, which is the same per-game store `X5` checked from the other side.
> ### ✅ STEP 2 PASSES 2026-08-20 — the suspected ORIGINAL trigger, and the pause is bounded
>
> This is the row that matters most in this block: `_isEditing` stranded by an editor torn down
> without a `CellEditEnded` is the mechanism the offline analysis narrowed to but **could not prove**.
> Driven exactly as written — open an editor, then navigate away **while it is still open**.
>
> 1. Rooted on `DirectionalLightComponent`, Auto ON and ticking at 10.0 s.
> 2. Double-clicked the `UCSSerializationIndex` (`IntProperty`, `-1`) value cell → the in-cell editor
>    opens and the label becomes **`sec · paused (editing)`**. ✅ The skip now *says why*, which is the
>    whole point of fix item 2 — before, a suppressed tick and a broken timer looked identical.
> 3. Clicked the **`GWorld` breadcrumb with the editor still open** — the teardown path Avalonia does
>    not raise `CellEditEnded` for.
> 4. The label is a **live countdown again (`sec · 9s`)**, not stuck on `paused (editing)`.
>
> ⭐ **And the pause is BOUNDED, measured on the wire — it did not merely look right on screen:**
> ```
> 11:35:45.565  walk_instance 0x1BCE29BA030   gap  2.3 s
> 11:35:55.568  walk_instance 0x1BCE29BA030   gap 10.0 s   <- last tick before the editor opened
> 11:36:25.574  walk_world    (root)          gap 30.0 s   <- exactly TWO skipped ticks, then resumed
> 11:36:35.575 … 11:37:05.592                 gap 10.0 s × 4
> ```
> Two things the label could not have given: the gap is **exactly 3 × the interval**, so precisely the
> ticks falling inside the editing window were skipped and no more; and `11:35:55.568 → 11:36:25.574`
> is **30.006 s**, i.e. the timer kept its phase straight through the pause. That is the re-arming
> counter of fix item 1 — it displays the *period*, which elapses whether or not the tick did work.
> The pre-fix failure was an **unbounded** pause; this one closed itself in two ticks.
>
> ⚠ No value was committed: the editor was abandoned by navigating, not by pressing Enter, so nothing
> was written to the game.

> | 1 ⚠ THE ONE THAT MATTERS | Live Walker → root on any live object → tick **Auto** → watch for 3 full intervals | the countdown cycles `10…1` and repeats, and the grid's values actually change | the reported failure is the counter reaching 0 and never moving again |
> | 2 | while Auto runs, double-click an editable scalar cell to open its editor, then click a breadcrumb to navigate away | the label reads `sec · paused (editing)` only while the editor is open, and auto-refresh resumes by itself afterwards — it must NOT stay paused | this is the suspected original trigger; a stranded latch used to kill Auto for the whole session |
> | 3 ⚠ THE RECONNECT, THE MAINTAINER'S PATH | with Auto ON, close the game (do not close the UI), start a **different** game, let it connect, then navigate to any object | Auto is off while disconnected (X5 — it must not walk a dead pipe or the old game's addresses) and **comes back on by itself** once the new object is showing | pre-fix it stayed off silently for the rest of the session |
> | 4 | repeat step 3 but go through **proxy mode** (`Connected (proxy mode — scan not yet triggered)` → `Connected:`) | same result | the resume hangs off data being re-rooted, not off the connect event, precisely so the two-step proxy path behaves identically — worth confirming rather than assuming |
> | 5 | switch to another tab with Auto on, then switch back | Auto is running again | tab-leave is now resumable too |
> | 6 ⚠ NON-REGRESSION | tick Auto, then **untick** it; separately, tick Auto then drill into a child object | stays OFF in both cases | only the pipe and the tab may re-arm it; a user's untick must stick |
> | 7 | disconnect with Auto ON and leave the panel empty (do not navigate) | label reads `sec` and nothing polls | resuming onto an empty panel would just re-arm a tick that skips |

-----

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[PIPEBUSY-2026-08-18]` — at-capacity logs ONCE, not an ERROR every second

*Was: at capacity (`kMaxPipeInstances=3`, UI holds 2 lanes) the accept loop's `CreateNamedPipe` fails
with `ERROR_PIPE_BUSY` (err=231) every second and logged `LOG_ERROR("PipeServer: CreateNamedPipe
failed …")` each time — **measured 1,826 ERROR lines in ~31.5 min on one Avowed session**, evicting
real diagnostics as the 8 MB pipe log rotated, and naming the wrong thing (busy ≠ broken). Now: a new
pure `Voll` policy (`dll/src/Voll.h`, header-only, Frieren roster) special-cases `ERROR_PIPE_BUSY` —
the accept loop logs **one INFO on the transition INTO** at-capacity ("all 3 pipe instances in use,
waiting for a free slot"), stays silent while it holds, and logs **one INFO on recovery** ("a pipe
slot freed, resuming accept"). Any OTHER errno still `LOG_ERROR`s every time — the capacity latch
never suppresses it. The retry/sleep is unchanged (it was correct), and `kMaxPipeInstances` is
unchanged (raising it would just move the spam to 4 clients). The state machine is unit-pinned in
`dll_helpers_test` (`Test_Voll_CapacityLoggingPolicy`, incl. the adversarial "a different-errno
failure during at-capacity still ERRORs and does not swallow recovery"). This is a DLL/pipe log fix,
NOT a mailbox-contract change.*

> ⚠ Reproducing this NEEDS a 3rd pipe client alongside the UI, which the register otherwise forbids
> ("never run `pipe_client.py` while the UI is connected", `[PIPEBUSY]`). That rule still stands as
> operational hygiene; the point of THIS check is only to observe that when it DOES happen the log is
> no longer a 1 Hz ERROR storm. Read the **pipe** log (`Logs/<game>/pipe-0.log`).
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ ALL THREE STEPS PASS 2026-08-19 `[PIPEBUSY-CAP-2026-08-19]` — and with NO UI involved
>
> `tools/verify/pipebusy_capacity.py` opens **three raw connections itself**, which fills the pool
> exactly as the UI's two lanes plus a client would — so the forbidden "pipe_client beside the UI"
> combination never had to be staged at all. DumperTest Development, dist 3263.
>
> * **Step 1 — PASS.** 75 s at capacity produced **exactly ONE** line:
>   `PipeServer: all 3 pipe instances in use, waiting for a free slot`, and **zero**
>   `CreateNamedPipe failed`. The pre-fix behaviour was ~1 ERROR/s, so the same window would have
>   yielded roughly **75** of them. *Exactly one* is the assertion — the defect was repetition, so
>   "at least one" would not have distinguished fixed from broken.
> * **Step 2 — PASS.** Releasing the clients produced **exactly ONE**
>   `PipeServer: a pipe slot freed, resuming accept`, i.e. the latch resets rather than sticking.
> * **Step 3 (NON-REGRESSION) — PASS, and broadly.** **23** other per-game log folders from this
>   machine were checked and **none** contains an at-capacity line. So "it only fires when actually
>   at capacity" is measured across 23 real sessions rather than asserted from one.

> | 1 ⚠ THE ONE THAT MATTERS | with the UI connected (2 lanes), start ONE extra `tools/verify/pipe_client.py` so the pool fills, leave it a minute, read `pipe-0.log` | **exactly ONE** `all 3 pipe instances in use, waiting for a free slot` INFO line — NOT `CreateNamedPipe failed` repeating once a second | before the fix this was ~1 ERROR/s forever (1,826 in 31 min) |
> | 2 | kill the extra client, watch the log | one `a pipe slot freed, resuming accept` INFO line, then normal `Waiting for client connection...` | proves the recovery transition fires exactly once |
> | 3 ⚠ NON-REGRESSION | during a normal single-UI session, grep `pipe-0.log` for `all 3 pipe instances` | absent | proves the at-capacity line only appears when actually at capacity |

-----

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[CLASSTOTAL-2026-08-18]` — the Classes tab reports the REAL class total, not the cap

*Was: `Aura::ListClasses` bounded its walk on the row cap AND counted `totalClasses` inside that loop,
so `totalClasses` could never exceed `maxResults` (5,000). The Classes tab rendered "5000 classes …
5000 total UClasses ⚠ STOPPED" — the two numbers identical exactly when the second was supposed to
add information — and the same capped value went out on the wire (`list_classes` → `total_classes`).
Now: the walk runs to the END of GObjects and increments `totalClasses` for every qualifying class;
only ROW materialization (the costly `WalkClassEx` + score + push) stops at the cap, so the extra work
past the cap is a handful of cheap reads per object (the same per-object cost `EnumerateAllFunctions`
already pays over the whole pool), not row building. `truncated` keeps its exact meaning (`rows >=
cap`). The status line now reads "5,000 classes shown of 6,609 total … ⚠ STOPPED at the 5,000-row
cap". `list_classes` is pipe-JSON, so no mailbox-contract implication. Pinned by
`ListClassesAsync_HonestTotalExceedsThePage` (UI) — the DLL walk itself has no test target
(`Aura.cpp` is compiled by none), so the class-count number is a live check.*

> Needs a game with **> 5,000 classes** (Elliot has ~6,609). A small game will not truncate.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ ALL THREE ASSERTIONS HOLD ON THE WIRE 2026-08-19 `[CLASSTOTAL-WIRE-2026-08-19]`
>
> Checked over the pipe (`list_classes` / `list_all_functions`), so this covers the **numbers**; the
> UI *status-line wording* is the only part still owed.
>
> ⚠ **Elliot is NOT the >5,000-class title the row assumes.** At the main menu it reports **3,236**
> classes, `truncated=false`. **Avowed is** the truncating one.
>
> | title | `total` (page) | `total_classes` | `truncated` |
> |---|---|---|---|
> | Avowed, `game_only=true` | 5,000 | **5,102** | true |
> | Avowed, `game_only=false` | 5,000 | **7,409** | true |
> | Elliot | 3,236 | 3,236 | false |
> | OCTOPATH | 699 | 699 | false |
>
> * **Step 1 — PASS.** On Avowed the page (5,000 = the `limit` default) and `total_classes` (5,102 /
>   7,409) **differ**, with `truncated=true`. Before the fix both were 5,000, i.e. "total" answered
>   nothing.
> * **Step 2 — PASS, like-for-like.** `list_all_functions` reports `scanned_classes=`**5,102**,
>   which equals `list_classes(game_only=true).total_classes` **exactly**. ⚠ It does *not* equal the
>   `game_only=false` figure of 7,409 — comparing those two is an apples-to-oranges scope mismatch
>   that reads as a failure. Compare the same scope.
> * **Step 3 (NON-REGRESSION) — PASS on two titles.** Elliot 3,236 = 3,236 and OCTOPATH 699 = 699,
>   both `truncated=false`: a full walk reports one honest number and does not falsely flag
>   truncation.
> * ⚠ **Read `total_classes`, never `total`.** `total` is `results.size()` and equals the cap
>   exactly when truncated — the very misreading this row exists to correct.

> | 1 ⚠ THE ONE THAT MATTERS | on a >5,000-class game (Elliot), open the Classes tab, Load with "Game classes only" off | status reads "**5,000 classes shown of ~6,609 total** … ⚠ STOPPED at the 5,000-row cap" — the two numbers DIFFER | before the fix both were 5,000, so "total" answered nothing |
> | 2 ⚠ CROSS-CHECK | note Interesting Funcs' "{N} functions across **{K} classes**" for the same game | the Classes tab's total matches K (both ~6,609) — the two panels now AGREE | before, Classes said 5,000 STOPPED while Funcs said 6,609; the honest number is now in both |
> | 3 ⚠ NON-REGRESSION | ✅ **PASS 2026-08-20** — DumperTest, Classes tab, **"Game classes only" OFF**: the status line reads exactly `3,942 classes shown of 3,942 total (scanned 25,179 objects)` — the two numbers EQUAL and **no STOPPED note**. Corroborates the pipe reading (`scanned_classes` 3,942). | | |
> | 3 ⚠ NON-REGRESSION | on a small game (< 5,000 classes), Load | "N classes shown of N total" (equal), no STOPPED note | proves a full walk still reports one honest number and does not falsely flag truncation |

-----

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[STALEDLL-2026-08-18]` (b) — the `.CT` reports the resolved DLL's size beside its path

*Was: the `.CT` logged only `DLL path: …`, so a stale/old `UE5Dumper.dll` resolved silently (the Feb
build in CE's install folder is ~0.5 MB vs the current ~2.7 MB). The build stamp is not a C ABI
export (only logged to `init-0.log` + carried on the pipe), the DLL is not injected yet at
path-report time, and CE Lua has no stat-by-path API, so the cheap honest signal is file SIZE. Now:
`ue5_dllSizeText(DLL_PATH)` is logged right after `DLL path:` and in the startup replay
(`ue5_dllFileSize`/`ue5_dllSizeText` verified under real `lua`). **(a) delete/refresh the stale file
in CE's install folder stays maintainer-only and is NOT done here.** Deferred idea for the pending
batch: read the ACTUAL build stamp from the `.CT` — would need a tiny data export (`g_buildNumber` /
`g_buildStamp`) or a `GetFileVersionInfo` read of the PE version resource; not worth a new export for
a LOW-priority readout.*

> | step | do this | expect | why |
> |---|---|---|---|
> ### ✅ BOTH STEPS PASS 2026-08-20 `[STALEDLL-B-LUA-2026-08-20]` — under real `lua`, no Cheat Engine needed
>
> New rig `scripts/tests/dll_size_text_test.lua` **lifts `ue5_dllFileSize` / `ue5_dllSizeText`
> verbatim out of `dist/UE5CEDumper.CT` and executes them** (working-lessons §2.5 — running the
> shipped script beats asserting about its text). It cannot pass against a `.CT` that no longer
> carries them: the extraction is a hard failure. **9 checks, 0 failures.**
>
> * **Step 1 — PASS.** `dist/UE5Dumper.dll` → `2879488 bytes (2.7 MB)`; the shape matches
>   `N bytes (X.X MB)` and the byte count equals the real file size.
> * **Step 2 — PASS, on the two files the row actually names.** Cheat Engine's install folder still
>   holds the February build and it reads **`536064 bytes (0.5 MB)`** against dist's
>   **`2879488 bytes (2.7 MB)`** — distinguishable, and the MB figures differ by more than 1 MB.
>   That is precisely the "0.5 MB vs 2.7 MB" discrimination this row exists to provide.
> * **Four negative controls — PASS.** A missing path, `nil` and `""` all return the sentinel
>   `unknown (could not read the file)` rather than throwing, and `ue5_dllFileSize(nil)` returns
>   **`nil`, not `0`** — `0` would render as a real, empty file and read as a legitimate answer.
>
> ⚠ **Scope, stated honestly:** this exercises the FUNCTION against the two real DLLs, not a live CE
> session emitting the line. The two call sites are present and wired to `DLL_PATH` in the shipped
> `.CT` (`ue5_log("DLL size: %s", ue5_dllSizeText(DLL_PATH))` and the startup replay), so what a CE
> run adds is only that `ue5_log` reached the console.
>
> 📌 **Incidentally re-confirms `[STALEDLL]` (a), which is still OPEN and maintainer-only:** the
> stale February `UE5Dumper.dll` **is still sitting in `%ProgramFiles%\Cheat Engine\`** at
> **536,064 bytes**, versus dist's 2,879,488. Deleting or refreshing it remains your call.

> | 1 | resolve a DLL via the `.CT` (any slot — breadcrumb, manual pick, …) and open the log / Lua console | a `DLL size: N bytes (X.X MB)` line appears next to `DLL path:` | the whole point — the size is now visible beside the path |
> | 2 | point the `.CT` at the ~0.5 MB Feb DLL vs the ~2.7 MB dist DLL | they read `0.5 MB` vs `2.7 MB` distinctly | the size is what catches the stale build a silent path never showed |

-----

### ⬜ FIXED 2026-08-18, NEEDS A LIVE CHECK `[PEHOOKONCE-2026-08-18]` — a failed ProcessEvent detection must now be RE-ARMABLE

*Was: a detection that failed because there was nothing to detect **yet** stored the same `-1` as a
hard failure, and every retry path in `Frieren.cpp` was gated against `-1` — so one
`pe_profile_start` before the first scan poisoned the PE hook for the whole process, and the message
told the user to retry the one thing that could not work. Now: three distinct sentinels
(`Stark::kPeOffsetNotDetected` = re-armable, `kPeOffsetFailed` = terminal, `>=0` = known), one
serialized detection entry point with its own bounded/rate-limited retry budget, separate from the
MinHook install-retry budget. **The rules are unit-pinned in `dll_helpers_test` (27 assertions across
`Test_Stark_PeOffsetSentinels` / `ShouldRetryPeDetection` / `PeValidationFailureVerdict`; the WIRING
is not — no target compiles `Frieren.cpp`).** Negative control run: forcing
`ShouldActOnValidationFailure` to `return true` — i.e. "act on every zero", the actual defect the
asymmetry prevents — failed exactly the 3 false-positive-guard assertions and nothing else. (Note for
anyone repeating it: *inverting* the predicate instead fails all 8 in that function, because
`PeOffsetAfterValidationFailure` gates on it too.) Step 2 is the whole point: it is the exact
order-swap that was permanently broken.*

> Needs a **proxy-mode** title (the DLL must start pipe-server-only, so GObjects is unset until a
> scan). Drive it headless with `tools/verify/` — no GUI needed for steps 1–4.
> Grep `init-0.log` by FORMAT STRING: `no UObject vtable available yet`, `offset resolved to
> vtable+`, `first-time init complete`.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEPS 1–4 PASS 2026-08-20 `[PEHOOKONCE-LIVE-2026-08-20]` — headless, on Lushfoil (proxy mode)
>
> Rig: `tools/verify/pehookonce.py`. Proxy mode is required and was confirmed each run — GObjects
> read `not_found` before the scan, so the "nothing to detect yet" window genuinely existed.
>
> * **Step 1 — PASS.** `pe_profile_start` before any scan → `hook_active: false`, and the detail is
>   the new wording: *"ProcessEvent is not resolved yet, and detection is still **ARMED** — this
>   attempt changed nothing. … Run a scan, then Start again; init-*.log tells you which"*. The old
>   advice told the user to retry the one thing that could not work; this names the actual remedy.
> * **Step 2 ⚠ THE ONE THAT MATTERS — PASS.** In the **same process**, after the poisoning attempt:
>   `trigger_scan` (GObjects → `0x7FF79F039B40 aob`) → `teleport_get_pov` (`ok`, code 0) →
>   `pe_profile_start` again → **`hook_active: true`**. This is the exact order-swap that used to
>   poison the hook for the whole process; it now recovers.
> * **Step 4 — PASS (non-regression).** A **fresh** Lushfoil, normal order, → `hook_active: true`
>   and `ProcessEvent: offset resolved to vtable+**0x260** via the pattern scan` — the offset this
>   row names.
> * ⭐ **The two runs differ by exactly one retry, which is the fix expressed as a number:**
>
>   | order | log line |
>   |---|---|
>   | normal (step 4) | `offset resolved to vtable+0x260 … (**detection run 0/8**)` — first attempt |
>   | profiler-first (steps 1→2) | resolved at **`detection run 1/8`** — it re-armed **once** |
>
>   Under the defect the second case had no run 1 at all: the `-1` was terminal.
> * **Step 3 — partially covered, and stated honestly.** Only **one** `detection run N/8` line
>   exists per session, so there is no retry storm. ⚠ But the row asks to grep *after step 1 and
>   before any scan* (expecting **zero**); this grep was taken **after** the scan, where exactly one
>   run is the correct and expected result. The no-storm property holds; the literal pre-scan-idle
>   form was not run in isolation.
> * **Step 5 (the UI path) not run** — steps 1–4 are the headless set.

> | 1 | fresh launch, proxy mode. `init` → `pe_profile_start` **before any scan** | `hook_active:false` and `hook_detail` starts **"ProcessEvent is not resolved yet, and detection is still ARMED"** and names BOTH causes (no scan / slot rejected). It must NOT say "do any invoke first" | the old text was unreachable advice by construction on this path. It must also not name only the no-scan cause — the same sentinel carries a re-armed rejection |
> | 2 ⚠ THE ONE THAT MATTERS — the negative control | in the SAME process, now `trigger_scan` → one invoke (`teleport_get_pov`) → `pe_profile_start` again | **`hook_active:true`** | this exact ordering returned `false` **permanently** before the fix; a live game is the only thing that can prove it converges |
> | 3 ⚠ NO STORM | after step 1, leave the process idle ~60 s with a 10 Hz feature running, then grep `init-0.log` for `detection run` | **zero** `detection run N/8` lines (nothing to detect ⇒ no run is spent), and **at most one** `no UObject vtable available yet` | ⚠ the single `no UObject vtable` line proves only the one-shot log guard (`s_loggedNoVtable`) and would still be 1 with the cooldown deleted — it is `detection run` that counts actual detection RUNS, so that is the line the anti-storm rule is measured on |
> | 4 ⚠ NON-REGRESSION | a known-good title (**Lushfoil**), normal order: `init` → scan → invoke → `pe_profile_start` | `hook_active:true`, and `ProcessEvent: offset resolved to vtable+0x260 via the pattern scan (detection run 1/8)` | one detection run, pattern path, unchanged behaviour |
> | 5 | UI path: Live Funcs → **Start** before running a scan, then run a scan and press Start again **without restarting the game** | first Start reports the "run a scan" detail; second Start records | this is the user-visible half; before the fix only a game restart recovered |

-----

### ⬜ FIXED 2026-08-18, NEEDS A LIVE CHECK `[PEHOOK-2026-08-17]` — a validation failure must ACT, and the advice must stop saying "re-deploy"

*Was: on **DumperTest** (UE 5.4 Development) the AOB pattern scan misses, the `UE=504` version-table
fallback picks `0x220`, the hook fires **0 times in 1500 ms** — and nothing acted on that verdict, so
every invoke silently timed out for the rest of the session while the UI advised a re-deploy that
cannot help. Now: a zero fire count on the **version-table** path soft-disables the hook and re-arms
detection (bounded at 3 failures, then terminal), and the Self-Test advice is chosen from the DLL's
own `get_diagnostics` hook state instead of asserting one cause.*

⚠ **The asymmetry is deliberate and step 5 is what protects it.** A zero fire count ALSO describes an
idle game thread (paused / loading / minimised). The pattern scan fingerprints ProcessEvent's own
body and has never been observed wrong, so a zero there is reported and the hook is **KEPT**; only
the version-table guess is acted on. Acting on every zero would disable a correct hook.

⚠ **Detector 2 alone proves nothing** — [working-lessons.md](working-lessons.md) §4.4: Kismet helpers
can no-op through ProcessEvent with a **correct** hook, producing the identical signature (args
written, return slot untouched). It is the fired-0-times validator that settles it, because it counts
the game's own traffic. **Do not read a `✗` as widening §4.4's population.**

⛔ **Steps 1–3 need a host whose pattern scan MISSES, and after the detection fix below there is no
longer one on this machine.** DumperTest was that host; the SIB alternates now match it, so it takes
the pattern path and the version-table branch these steps exercise cannot be entered there. Two
honest ways to run them, and **the second is preferred** (the X2 precedent — *step 4 proven by
LOWERING the cap, not by finding a host*):
> * against a **pre-2026-08-18 DLL** on DumperTest — records the old behaviour, not the new code; or
> * ⭐ **temporarily comment out the two `kPePat*Sib*` alternates in
>   [`Frieren.cpp`](../dll/src/Frieren.cpp) `DetectProcessEventVTableOffsetByPattern` and rebuild.**
>   That restores the miss on DumperTest and drives the real, current code down the version-table
>   path. Revert the edit afterwards.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEPS 1b/2/3 PASS 2026-08-20 `[PEHOOK-LIVE-2026-08-20]` — via the row's own ⭐ preferred route
>
> Took the route this row recommends: **temporarily removed the two `kPePat*Sib*` alternates** in
> `Frieren.cpp`'s `DetectProcessEventVTableOffsetByPattern`, rebuilt, and drove the **real current
> code** down the version-table path. Source reverted and rebuilt afterwards; `git status` clean.
>
> **The contrast is the whole verification, and it is one variable:**
>
> | DLL | detection path | offset | validation failures |
> |---|---|---|---|
> | SIB alternates removed | `pattern scan missed, falling back to UE=504 version-table primary=0x220` | `vtable+0x220` *(a guess)* | **2** (`failure 1/3`, then `2/3`) |
> | restored (shipping) | `DetectProcessEvent (pattern): match at vtable+0x268` | **`vtable+0x268`** | **0** |
>
> ⭐ **The version table guessed `0x220`; the true offset is `0x268`.** So the slot really was
> mis-detected — the validator was not firing on a healthy hook, it caught a genuinely wrong virtual.
>
> * **Step 2 — PASS, verbatim.** The log carries every element the step names:
>   `GameThreadDispatch: VALIDATION FAILED — hook at 0x7FF69AB99FC0 (vtable+0x220) fired 0 times in
>   1500ms, and that offset came from the version TABLE guess, not the pattern scan. Reading this as
>   a MIS-DETECTED vtable slot (failure 1/3): disabling the hook, refusing the off-thread direct call
>   for the rest of this process (it would call a known-wrong virtual), and re-arming detection.`
>   The next line shows the re-arm actually happening (`pattern scan missed, falling back…` again).
> * **Step 3 — PASS.** The counter is bounded and advancing: `failure 1/3` then `failure 2/3`, never
>   unbounded.
> * **Step 1b — PASS.** `re-deploy` appears **twice in the whole log and both are the negation**:
>   `Re-deploying the DLL will NOT help — the binary is fine, the slot guess is wrong.` No advice
>   string recommends re-deploying. The message even keeps the honest alternative in view: *"(If the
>   game was merely idle, the next invoke re-detects and re-installs by itself…)"*.
> * **Step 5's asymmetry is respected** — with the pattern path restored the hook fired normally and
>   there were **0** validation failures, so nothing acted on a correct hook.
> * **Step 1 proper (the UI Self-Test text) not run** — that needs System → Run Self-Test on screen;
>   what is verified here is the DLL-side verdict and advice the panel now sources from
>   `get_diagnostics`.

> | 1 | **DumperTest**, SIB alternates temporarily removed → System → **Run Self-Test**, as the **FIRST invoke of a freshly launched process** | `✗ Add_IntInt…`, and the advice names a **mis-detected vtable slot** | ⚠ order-dependent: `HookNeverFired` needs `hook_active == true`, and the validator soft-disables the hook 1500 ms after install. A later click sees the hook DOWN and correctly gets the `HookOff` wording instead — that is not a failure |
> | 1b | any Self-Test run | no advice string recommends re-deploying without ruling it out | `SelfTestAdviceTests.NoAdviceRecommendsRedeploying` pins the rule offline; this just confirms it reached the UI |
> | 2 | grep that run's `init-0.log` | `VALIDATION FAILED — … came from the version TABLE … (failure 1/3): … re-arming detection`, then `hook flag cleared` | the verdict is now acted on, and the log names the real cause |
> | 3 | force three CONSECUTIVE failing invoke attempts | the 3rd logs **"giving up on ProcessEvent for this process"**, and `pe_profile_start` then returns the **detection-FAILED** detail, not the "not resolved yet" one | proves the retry loop is bounded and lands in the honest terminal state. "Consecutive" is load-bearing — a validation that PASSES resets the counter |
> | 3b ⚠ SAFETY | after a condemn, issue an invoke within the next ~5 s (the install-retry cooldown, while the offset is usable but the hook is down) | the invoke returns **-3** and does **not** call through; the Self-Test says **the DLL REFUSED this call** | self-review found this: re-arming without the refusal made the mis-detected case WORSE than before, because the direct fallback would call a known-wrong virtual where the old code merely timed out |
> | 3c ⚠ THE RECOVERY, and it is the one that catches an over-correction | after a condemn, let the game tick and invoke until the hook re-installs and validates | `this offset is TRUSTED again` in the log, and direct calls (CE Lua `callFunction`, Run Self-Test) **work again** | review HIGH-1: a lifetime "have we ever failed" tally left the direct path dead for the rest of the process even after full recovery — `[PEHOOKONCE]` rebuilt one layer down. The refusal must be a STATE that lifts |
> | 4 ⚠ NON-REGRESSION | **Lushfoil** → Run Self-Test | `✓ Add_IntInt(3,4) = 7`, hook stays installed, **no** VALIDATION FAILED | the pattern path must be untouched |
> | 5 ⚠ THE FALSE-POSITIVE GUARD | on a pattern-detected title, background/pause the game so PE traffic stops, then force a first invoke | if 0 fires, the log is a **WARN** saying the offset came from the pattern scan and the hook is **KEPT**; invokes work once the game ticks again | a correct hook must survive an idle window — this is the regression the asymmetry exists to prevent |
> | 6 | on a title where a Kismet helper no-ops with a good hook (§4.4 — **EVERSPACE 2**), Run Self-Test | the advice is the **BlueprintFastCall** wording, not the wrong-slot one. ⚠ **`✓ = 7` is an equally valid outcome and is itself a result** | [working-lessons.md](working-lessons.md) §4.4 records that the EVERSPACE 2 no-op was diagnosed *while the hook was in the wrong slot* and was **never re-verified against a corrected hook**. A `✓` here narrows §4.4 again; it does not fail this step |

**The DETECTION half was also fixed, offline, from the binary's own bytes.** Root cause: the pattern
budgets two wildcards (ModRM + disp32 low byte), but when the compiler parks the `UFunction*` in an
**extended** register x64 makes a **SIB byte mandatory**, so the instruction is one byte longer and
the fixed `00`s land early. Measured at `ProcessEvent+0x36F` in the Development build:
`41 F7 84 24 B0 00 00 00 00 04 00 00` = `test dword ptr [r12+0xB0], 0x400`; the Shipping build of the
same project uses `rdx` and matches today. Ground truth for the slot came from the **paired PDB**:
`UObject::ProcessEvent` is vtable entry **77 = +0x268** in BOTH configs, and the fallback's `0x220` is
entry 68, `UObject::GetSubobjectsWithStableNamesForNetworking` — a replication callback that never
runs in a single-player sample, which is precisely "fired 0 times". SIB-tolerant alternates were
added, and the regression risk (a looser pattern matching an EARLIER slot) was **measured, not
argued**: over the **22 shipped UE games** in the local corpus plus both DumperTest configs, 60
candidate vtables each, **not one binary changed a first match it already had**; the only delta is
DumperTest Development going from no match at all to exactly one, at `0x268`.

> | step | do this | expect |
> |---|---|---|
> | 7 ⚠ THE DETECTION FIX | launch **DumperTest** (Development) with the new DLL, `init` → `trigger_scan` → one invoke, then grep `init-0.log` | `DetectProcessEvent (pattern): match at vtable+0x268`, **no** `falling back to UE=504 version-table`, and **no** `VALIDATION FAILED` |
> | 8 | Run Self-Test on DumperTest after step 7 | `✓ Add_IntInt(3,4) = 7` — the sample becomes usable for invoke-dependent rows |

⚠ **Until step 7 is observed, treat DumperTest as unproven for invoke-dependent rows and use
Lushfoil.** The slot is PDB-confirmed and the scan is file-verified, but nothing has yet watched the
DLL do it inside the running process.

⚠ **Do NOT "fix" the version table instead.** Measured true slots vs the table: DumperTest 5.4 →
table `0x220`, true **`0x268`**; Lushfoil 5.6 → table `0x228`, true **`0x260`**. DumperTest carries
the Iris/replication virtuals ahead of ProcessEvent, so 5.4 sits *later* than 5.6 does. Slot position
is a **build-flag** property, not a version property — the pattern is what has to work.

-----

### ⬜ NEW 2026-08-17 — A12: the same, in GROUP mode (build 3261)

*Needs a connected game and the same container as A11's check. **The rule and the anchor factories
are unit-pinned (17 assertions, two negative controls); the WIRING through three by-name hops is
not** — no target compiles `Aura.cpp`. Run this straight after A11's; it is the same in-game
actions with the panel in Group mode.*

> Grep by FORMAT STRING: `RefineGroup re-anchor:` (whole-pass summary) and `container-moved=`
> (the per-candidate drop tally).
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | Value Search → **Group** mode, **Deep ON**, two slots whose values both live inside the same `TArray<FStruct>` element. First Scan. | a candidate row whose slot fields carry an `[i]` index | establishes the leaves really are container elements and Deep is on — without Deep, nothing here is exercised |
> | 2 ⚠ THE ONE THAT MATTERS | grow that container in game until it must realloc, then Next Scan | the row **SURVIVES**, and `scan-0.log` has `RefineGroup re-anchor: N … repointed` | before 3261 a realloc left every leaf address stale |
> | 3 | remove an element BEFORE the matched one, then Next Scan | the row is dropped, and the `RefineGroup cand[...]` line shows `container-moved=` non-zero | the shift-in-place case; the tally is what tells it apart from a predicate rejection |
> | 4 ⚠ THE UNIT TRAP, and it needs a TSet/TMap | run the same two steps against a `TMap` whose value struct holds both values | rows behave as in 2 and 3, and are **NOT** all dropped on the very first Next Scan | a mass drop with no in-game change is the `MaxCapacity`-vs-`MaxIndex` mismatch. This is the failure the two named factories exist to prevent and the ONLY way to observe it |
> | 5 ⚠ NON-REGRESSION | Group scan a plain (non-container) field pair, Next Scan with nothing changed | rows survive, and **no** `RefineGroup re-anchor` line at all | `Direct` leaves must not enter the new path |
> | 6 | check the log for `carries no ValueAnchor` | **absent** | it fires only if one of the three by-name hops dropped the stamp — the one thing no offline test can see |

⚠ **Depth ≥ 2 is deliberately NOT anchored** (`UnverifiableNested`), so a leaf nested two containers
deep behaves exactly as it did before 3261. Not a failure.

-----

### ⬜ NEW 2026-08-17 — A11: a grown container must no longer lose its Value Search candidates (build 3253)

*Needs a connected game with a `TArray`/`TMap` UPROPERTY whose element count changes in play
(inventory, spawned-actor list, buff list). **The RULE is unit-pinned (15 assertions, two negative
controls); the WIRING is not** — no target compiles `Aura.cpp`.*

> **The cheapest decisive evidence is a log line the fix adds**, and it only appears when the
> re-anchor actually fired. Grep by FORMAT STRING: `Refine re-anchor:`.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | Value Search a known value that lives in a container element (a `TArray<FStruct>` field, or a `TMap` value). First Scan. | the row appears with a `[i]` element index | establishes the candidate IS a container element, not a direct field |
> | 2 ⚠ THE ONE THAT MATTERS | in game, ADD entries to that container until it must grow (pick up items, spawn enemies), then Next Scan with the same value | the candidate **SURVIVES**, and `scan-0.log` has `Refine re-anchor: N container element(s) repointed after a realloc` | before 3253 a growth realloc left every element address stale and they were lost outright. A surviving candidate with **no** re-anchor line means the buffer never moved — the container had slack, so this run did not test the repoint |
> | 3 | now REMOVE an element that sits BEFORE the candidate's index, then Next Scan | the candidate is **dropped**, and the log's `dropped` count goes up | this is the silent-wrong-value case: the tail shifts down one slot in place, so the old address reads cleanly and returns the neighbour's value |
> | 4 | for a `TSet`/`TMap`: remove the entry the candidate points AT, then Next Scan | dropped | the allocation bit is the only witness — a freed sparse slot is refilled at the identical address |
> | 5 ⚠ NON-REGRESSION, do not skip | scan a container value, then APPEND to that container without forcing a realloc (add one entry to a list that has slack), Next Scan | the candidate **survives** | the naive `{dataPtr,count}` rule drops these. If they vanish, the asymmetric rule was lost and this is a REGRESSION, not a fix |
> | 6 | repeat step 1 on a plain (non-container) field | unchanged behaviour, and **no** `Refine re-anchor` line at all | `Direct` candidates must not enter the new path |

**Known residuals, do not report as failures**: `TArray::Insert` at a low index shifts on a count
INCREASE and is not caught; balanced churn (remove one, add one back into the same slot) is
invisible; and the GROUP scan path is untouched (filed as **A12**), so a Group-mode refine still
behaves as it did before 3253.

-----

### ✅ VERIFIED 2026-08-17 `[F9-PIPE-2026-08-17]` — F9: walk_world must list actors AND their components (build 3247)

**All six steps PASS**, on **DumperTest Development** — one of the two titles that originally
reproduced `actor_count: 0`, so it is a discriminating sample and not a fresh one. Driven over the
pipe with `tools/verify/pipe_client.py` on build **1.0.0.3262**; **no UI was involved, so this says
nothing about the Live Walker's own bindings** — only that the DLL now returns the right payload.

* **1 — PASS.** `walk_world` returns `actor_count: 58` on the stock ThirdPersonMap. The defect was
  `0` here.
* **2 — PASS.** `actor_count: 58` == `actor_total: 58`, `truncated: false`.
* **3 — PASS (the gate).** Zero rows whose name contains `ModelComponent` or `ActorCluster`, over
  all 58. Both ARE outered to the level, so their absence is what shows the is-Actor gate ran rather
  than a bare outer comparison.
* **4 — PASS (the half the finding did not mention).** 53 components across 47 of 58 actors.
  `BP_ThirdPersonCharacter_C` lists six — `PawnInputComponent0`, `CollisionCylinder`,
  `CharacterMesh0`, `CharMoveComp`, `CameraBoom`, `FollowCamera` — i.e. the non-reflected
  `OwnedComponents` TSet is now read correctly.
* **5 — PASS, and checked INDEPENDENTLY of the payload under test.** `walk_world`'s component
  entries carry only `addr`/`class`/`name`, so the Outer cannot be read off the same reply that is
  being verified. Asked the DLL separately via `get_related_objects` for each of the six: all six
  report `Outer -> BP_ThirdPersonCharacter_C_2147482479`, the actor they were listed under. 6/6.
* **6 — PASS, with a stated substitution.** No large streamed map was used; `limit=10` against this
  58-actor level gives `actor_count: 10`, `actor_total: 58`, `truncated: true`. That is the same
  count-past-the-cap path, but it is **not** a streaming-map test and must not be read as one.

*No defect found. One false alarm of my own is worth recording so it is not re-raised: the actor
rows looked like they had a null class until I noticed I was reading `class_name`; the field is
`class`, and all 58 carry it.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | connect, Live Walker → **Load GWorld** | a **non-zero** actor list | `actor_count: 0` on 2 of 2 games was the defect; anything non-zero is new behaviour |
> | 2 | compare `actor_count` with `actor_total` on a small map | equal, and `truncated` false | `actor_total` is now the level, not the page |
> | 3 ⚠ THE GATE, observed in the wild | scan the list for `ModelComponent*` / `ActorCluster` rows | **none** | those ARE outered to the level. Seeing them means the is-Actor gate is not running and the list is outer-only |
> | 4 ⚠ THE HALF THE FINDING DID NOT MENTION | expand an actor that obviously has components (a Character) | **components listed** | this loop had never executed in production; `OwnedComponents` is a non-reflected TSet that was being read as an ArrayProperty |
> | 5 | check a listed component's Outer | it is the actor it is listed under | the one-hop ownership test is what keeps shared objects (the world, another actor) out |
> | 6 | on a big streamed map, set `limit` low | `actor_count` == limit, `actor_total` > it, `truncated` true | the count-past-the-cap path |

⚠ **A benign over-report is EXPECTED, not a failure**: the list is derived from the Outer
back-reference, so it can include an actor already destroyed but not yet garbage-collected, and it
is **not** in the engine's array order.

-----

### ✅ COMPLETE 2026-08-20 `[AA38-PYTHON-2026-08-17]` + `[AA38-MODULAR-2026-08-20]` — AA38: a GWorld must not be reported on a process with no object pool (build 3245)

**ALL FIVE STEPS PASS.** Steps 1/2/3/5 on 2026-08-19 (re-confirmed on 3263), and **step 4 on
2026-08-20 against Satisfactory** — see `[AA38-MODULAR-2026-08-20]` below. This row is complete.

Run on build **1.0.0.3262** (`05a9af58-dirty`), confirmed from the injected DLL's own
`Logger started` line rather than assumed — `dist/build_number.txt` agrees, so §2.6's stale-proxy
trap is excluded. Neither the sleeper nor DumperTest carries a proxy, so the injection genuinely
scanned.

* **5 (done first, or the rest proves nothing).** Deleted `67F515A70001A000` from
  `UE5CEDumper.MSI-NB.json` — it cached `gWorld: aob/GWLD_V3` with `gObjects`/`gNames`
  `not_found`, i.e. exactly the hint that would let the run resolve MAIN-module *and be accepted by
  design*. File backed up, edited by a `json` round-trip; the Solarpunk and DumperTest control
  entries were left intact and re-checked afterwards. Step 1 was therefore a cold scan.
* **1 — PASS.** `python.exe` sleeper, PID 26292:
  `FindAll: Complete — GObjects=0x0 (not_found), GNames=0x0 (not_found), GWorld=0x0 (not_found)`.
  **The before/after pair is from the same host**: the archived 2026-08-15 runs of this same
  `python.exe` recorded `GWorld=0x7FFB4595D5A8 (aob)` alongside `GObjects=0x0` — the defect itself.
* **2 — PASS, and it is the *unanchored* wording**, which is the half that matters:
  `[GWorld] GWLD_V3: REFUSED 7 match(es) resolving to 0x7FFF47461760 in 'atcuf64.dll' — GObjects
  never validated this run, so nothing has confirmed this process is the UE process; a match in an
  arbitrary loaded module is not admissible`. The module is named, and this is the branch that
  asserts only what the run established — not the monolithic sibling, which would have claimed more.
* **3 — PASS.** Non-regression on DumperTest-Shipping (PID 38764), whose hint entry
  `E1AAB613081BC000` was left in place: all three resolve by `aob` and the winners are
  **`GOBJ_V13` / `GNAM_V8` / `GWLD_TQ_1`** — identical to the cached ids. Addresses differ from the
  prior run (ASLR), which is why the comparison is on pattern id + method, as this row instructs.
* **4 — NOT TESTED.** Needs a modular-build title (GNames in `CoreUObject.dll`). Satisfactory is
  installed and is the shaped candidate, but it was not scanned; do not read the ✅ as covering
  `AnchorState::ForeignDll`.

⚠ The second reproducing sample (the Solarpunk launcher shim, `C9E9551B0003D000`) was **not** run —
one sample plus the reverse control was judged sufficient. Its hint entry is untouched if anyone
wants it.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | inject into `python.exe` (or any small non-UE exe) and let the scan finish | `FindAll: Complete` shows `GObjects=0x0` **and** `GWorld=0x0` | before 3245 GWorld was published from an arbitrary loaded module on exactly this run |
> | 2 | grep `scan-0.log` for `GObjects never validated this run` | the REFUSED line appears, naming the module | the refusal must state the UNANCHORED reason, not the older monolithic-build text, which asserts something this run has not established |
> | 3 ⚠ NON-REGRESSION | re-scan a normal game that already resolves all three (DSClient / TQ2 / Elliot) | same winning pattern id and same method as its current `scan-0.log` | compare pattern id + method, **not** literal addresses — those are not stable across launches |
> | 4 ⚠ NON-REGRESSION, the modular case | if a modular-build title is available (GNames in `CoreUObject.dll`, Satisfactory-shaped), re-scan it | GNames still resolves out of the DLL | `AnchorState::ForeignDll` must still accept; this is the case multi-module scanning exists for |
> | 5 | clear the per-PE-hash hint cache entry for `python.exe` first | step 1's run does a cold scan | with a cached `GWLD_V3` hint the run can resolve MAIN-module and be accepted by design, which would not disprove anything |

> ### ✅ RE-CONFIRMED on the SHIPPING build `[AA38-3263-2026-08-19]`
>
> The ✅ above was earned on **1.0.0.3262**. Steps 1, 2 and 3 were re-run on **1.0.0.3263** — the
> build that actually ships — and all three still PASS. Step 5's cold-cache precondition was redone
> first: the `67F515A70001A000` entry (which the 2026-08-17 run had re-created, all `not_found`) was
> deleted again, machine JSON backed up beforehand, and the `C9E9551B0003D000` / `E1AAB613081BC000`
> control entries were re-checked present afterwards.
>
> * **1 — PASS.** `python.exe` sleeper, PID 62288, DLL `1.0.0.3263 10b00cf8-dirty`:
>   `FindAll: Complete — GObjects=0x0 (not_found), GNames=0x0 (not_found), GWorld=0x0 (not_found)`.
> * **2 — PASS, still the *unanchored* wording.** `[GWorld] GWLD_V3: REFUSED 7 match(es) resolving to
>   0x7FFF47461760 in 'atcuf64.dll' — GObjects never validated this run, so nothing has confirmed
>   this process is the UE process; a match in an arbitrary loaded module is not admissible`.
>   ⚠ **Note which module it names**: `atcuf64.dll` is **Bitdefender's own Active Threat Control
>   filter**, injected into every process on this machine. That is a better adversary than a random
>   DLL — it is present in *every* future run here, so this refusal is load-bearing on this PC and a
>   regression would reappear immediately rather than intermittently. `GWLD_V3` had **148** raw hits.
> * **3 — PASS (non-regression).** DumperTest-Shipping, PID 59460, hint entry left in place:
>   `GOBJ_V13` / `GNAM_V8` / `GWLD_TQ_1`, all method `aob` — **identical pattern ids and methods** to
>   the cached entry, which is the comparison this row mandates. Addresses differ from 2026-08-17
>   (ASLR), exactly as the row predicts. `scanCount` 11 → 12.
> * **4 — ✅ PASS 2026-08-20 `[AA38-MODULAR-2026-08-20]`, on Satisfactory. AA38 IS NOW COMPLETE.**
>   A genuine modular build: **184 engine DLLs** beside the exe and **607 loaded modules** at
>   runtime. Every global resolved by the **symbol/export** path, and — the assertion — **not one
>   of them lives in the main module**:
>
>   | target | address | method / pattern | owning module |
>   |---|---|---|---|
>   | GObjects | `0x7FFCCA033620` | `symbol` / `GOBJ_EXP` | `…-CoreUObject-Win64-Shipping.dll` |
>   | GNames | `0x7FFCCA8BD8C0` | `symbol_call_follow` / `GNAM_EXP_TOSTR` | `…-Core-Win64-Shipping.dll` |
>   | GWorld | `0x7FFCC88CCB88` | `symbol` / `GWLD_EXP` | `…-Engine-Win64-Shipping.dll` |
>   | GEngine | `0x7FFCC88CF768` | `symbol` / `GENG_EXP` | `…-Engine-Win64-Shipping.dll` |
>   | *(main)* | | | `FactoryGameSteam-Win64-Shipping.exe` — **holds none of them** |
>
>   So `AnchorState::ForeignDll` accepted matches across **three different foreign DLLs**. The DLL
>   says so itself: `Module anchor set to 'FactoryGameSteam-CoreUObject-Win64-Shipping.dll' — later
>   targets must resolve there **unless this build is modular**`, and the scan log contains
>   **0** `REFUSED` / `not admissible` lines.
>   ⭐ **This is the non-regression AA38 most needed.** The fix's whole job is to refuse a match in
>   an arbitrary module *when nothing has confirmed the process* — a blunt version would have
>   refused this legitimate cross-DLL case too. It discriminates: GObjects validated first, so the
>   foreign-module matches were admitted.
>   Fully functional afterwards: `probe_ran/validated` true, `use_fproperty=true`, `item_size=24`,
>   **137,372 objects**, `walk_world` ok with 200 entries, `find_instances Actor` = 500.
>
>   ⚠ **Launch note — the shipping exe cannot be started directly.** It dies with *"Failed to open
>   descriptor file ../../../FactoryGameSteam/FactoryGameSteam.uproject"* (that folder does not
>   exist; the game ships `FactoryGame\`). Launch the **top-level `FactoryGameSteam.exe`**, which
>   then relaunches into the shipping exe under a *different* PID — so resolve the PID by name
>   **after** the handoff, twice if necessary.
>   ⚠ **Second >5,000-class title, corroborating `[CLASSTOTAL]` beyond Avowed**: `list_classes`
>   returns page **5,000** / `total_classes` **5,171** / `truncated` **true**.
>   ⚠ **`AB12` precondition still unmet**: 607 loaded modules is the most on this machine, still
>   short of the **>1024** that row needs.
>
> * ~~**4 — STILL NOT TESTED.**~~ ⚠ Correcting the note above: **Satisfactory IS installed**
>   (`/d/SteamLibrary/steamapps/appmanifest_526870.acf`), so the modular-build case is *available*,
>   not merely "shaped". It needs a real game launch, so it belongs to a title group, not to the
>   headless batch — but it is no longer blocked on finding a host.
>
> Injected with the new **`tools/verify/inject.py`**, not `dist/inject-ue.ps1` — see that file's
> docstring for why (no ad-hoc PowerShell on this machine).

**Known residual, filed as AA39, not a failure here**: Pass 1 (main-module) is ungated. Injecting
into a LARGE non-UE monolithic exe can still publish a main-module GWorld.

-----

### ⬜ NEW 2026-08-17 — ST1: our own direct calls must stop entering our own PE detour (build 3205)

*Needs a connected game. See dev-log build 3205. **The two predicates are unit-pinned (10 assertions,
two negative controls); the ROUTING is not** — nothing offline can observe which address a live
vtable actually holds, and no target compiles `Stark.cpp` or `Frieren.cpp`.*

> **The cheapest decisive check is the log line**, because the fix adds a distinguishable one.
> Grep by FORMAT STRING (never line number): `via trampoline — not re-entering our hook` vs the
> older `(caller-asserted safe)`.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | connect, run the Pointers-tab KismetMathLibrary self-test (`directCall: true`) | `pipe` log shows **`via trampoline`** | the ordinary path now bypasses the detour |
> | 2 | with the game running, `get_pointers` → note `hook_fire_count`, run step 1 again, re-read | the count does **not** jump by our own call | our call no longer enters `HookedProcessEvent` at all |
> | 3 ⚠ THE ONE THAT MATTERS | set a short invoke timeout, fire a game-thread invoke on a **paused/menu** game so it times out and stays queued; then fire a CE static-native invoke | the queued request is **still queued** afterwards, not executed | before 3205 the second call drained it on the pipe thread |
> | 4 | resume the game | the queued request now runs, on the game thread | the drain still works where it should — the regression guard for step 3 |
> | 5 ⚠ control | a class that OVERRIDES ProcessEvent (a BP with its own slot), invoked directly | log shows **`(caller-asserted safe)`**, and the call still works | fail-open is correct here; the trampoline would have run the BASE implementation |
> | 6 | ordinary gameplay for a few minutes with an invoke queued | no `SEH exception during queued PE call`, no 0xC0000409 | the `thread_local` guard did not suppress the legitimate drain |

### 🟡 7-of-8 CLOSED 2026-08-19 — AD4: the God Mode badge must now name WHY, not just on/off (build 3203)

*Needs a connected game with a pawn. See dev-log build 3203. **The badge MAP is unit-pinned (11
tests, two negative controls); what is not pinned is that the DLL actually reports the three fields
honestly on a real pawn** — `Solitar.cpp` is compiled by no test target.*

> **Read this first, because one cell is expected to be WRONG on some games and that is not a
> regression.** `Solitar::GetState`'s `live` falls back to the *desired* value when the T2 scan
> matched no canonical `bCanBeDamaged`, while `GetGodMode` returns `PR_ERR_REFLECT` for the same
> pawn. That mismatch is **deliberately out of scope** for build 3203 (live-only, needs
> `Solitar.cpp`). If step 4 shows "ON" where you expected "ON (pending)", that is this known gap —
> file it against Solitar, not against the badge map.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | connect with the game at a menu / no pawn, press ↻ | `Unknown` | baseline: nothing wanted, nothing readable |
> | 2 | still pawn-less, Force ON | **`ON (pending)`**, not `Unknown` | the toggle path — before 3203 this reported Unknown and looked like a failure |
> | 3 | enter gameplay so a pawn spawns, press ↻ | `ON` | the armed hold engaged on its own |
> | 4 | let the game damage-reset the flag, press ↻ repeatedly | `ON` mostly, occasionally **`ON (contested)`** | the drift race — the cell that used to read `OFF`. Rare by design; the re-assert worker wins quickly |
> | 5 | Force OFF, then ↻ | `OFF` | the unambiguous cell still works |
> | 6 ⚠ control | on a game whose pawn is immune for its OWN reasons, with nothing forced | **`ON (not held)`** | proves the badge distinguishes "we hold it" from "it happens to be true" |
> | 7 | Force ON, close the UI, reopen and reconnect | badge is `ON` **without pressing ↻** | the connect-time read; `want` lives in the DLL and survives a UI restart |
> | 8 ⚠ control | during step 7's reconnect, watch the status line | stays `Connected`, no button flicker | proves the connect read did not go through RefreshGodModeAsync (IsBusy / StatusText) |

> ### 🟡 SEVEN OF EIGHT CLOSED 2026-08-19 — by the maintainer, on their own machine
>
> The maintainer ticked rows **1, 2, 3, 5 and 6** of this item in the 繁中 checklist. That file
> compresses these eight steps into six, so five ticked rows discharge **seven** steps:
>
> | zh-TW row | steps here | verdict |
> |---|---|---|
> | 1 | 1 — menu / no pawn, ↻ → `Unknown` | ✅ |
> | 2 | 2 — pawn-less Force ON → **`ON (pending)`**, not `Unknown` | ✅ **the finding itself** |
> | 3 | 3 — pawn spawns, ↻ → `ON` | ✅ the armed hold engaged on its own |
> | 5 | 5 **and** 6 — Force OFF → `OFF`; and the ⚠ control, an own-reasons-immune pawn with nothing forced → **`ON (not held)`** | ✅ both |
> | 6 | 7 **and** 8 — reconnect shows `ON` with no ↻; status line stays `Connected`, no flicker | ✅ both |
>
> Step 2 is worth naming separately: it is the behaviour build 3203 exists to produce, and the
> pre-3203 output (`Unknown`, which read as a failure) is what it replaces. Step 6's control is the
> one that proves the badge distinguishes *"we hold it"* from *"it happens to be true"* — a badge that
> merely echoed the flag would read `ON` there.
>
> ⚠ **STEP 4 REMAINS, and it is the hardest one.** `ON (contested)` needs the game to damage-reset
> `bCanBeDamaged` while ↻ is pressed repeatedly — the drift race. It is **rare by design** (the
> re-assert worker wins quickly), so its absence proves nothing and it cannot be waved through: this
> is precisely the checklist's own rule 1 (a PASS defined by something *appearing* is not settled by
> not seeing it). It stays a `C_HYBRID` row — a human must take damage in combat mid-batch.
>
> ⚠ **Evidence class:** the maintainer's ticks, nothing more. No log line or screenshot from that run
> reached this repo. Recorded as reported, not re-observed here.

### ✅ CLOSED 2026-08-17 `[AC1-UI-2026-08-17]` — AC1: Force Overwrite must no longer be able to destroy a foreign DLL (build 3191)

*Needs **no game** — only the Proxy Deploy panel and one throwaway file. Same "free from an ordinary
session" shape as AE4–AE7, so it can ride along with those. See dev-log build 3191.*

> **The policy is unit-pinned (15 tests, negative-controlled); what is NOT pinned is that the two
> checkboxes are wired to the two halves.** `PlanDeploy` is pure and exhaustively tested, but nothing
> proves the AXAML binds `AllowForeignOverwrite` to `ForeignConsent` rather than to the persisted
> flag — that is exactly the kind of wiring a green build does not check.
>
> **Make the foreign DLL by copying any non-ours DLL** into a game's `Binaries\Win64` under a proxy
> name (e.g. `dxgi.dll`); it only has to lack our `ProductName`. Delete it afterwards.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | place a foreign `dxgi.dll`, Refresh | row reads `Other proxy: <name>` | baseline: detection still works |
> | 2 | tick **Force Overwrite** only → Deploy | **refused**, file untouched, row still names the owner | **the regression this fix exists for** — before 3191 this destroyed the file |
> | 3 | check the byte size / version of the foreign DLL | unchanged | proves "refused" means *not written*, not merely *reported as refused* |
> | 4 | tick **both** boxes → Deploy | succeeds; `proxy` log carries a `Replacing another program's dxgi.dll (…)` warn line **naming the old product** | the capability is kept, and the only surviving record of what was destroyed is written |
> | 5 ⚠ control | restart the app | **Force Overwrite still ticked, "Replace other tools' DLLs" back to OFF** | the whole point: the destructive half must not persist. If both come back ticked, the fix is defeated |
> | 6 | with our proxy already deployed at the same version, tick Force Overwrite → Deploy | redeploys (no "already current" skip) | the benign half still works — guards against over-correcting into a refusal |
> | 7 | Update All against a game with a foreign DLL | skips it, as before | `UpdateAllAsync` passes `ForeignConsent: false`; its own pre-gate should make that unreachable |

> ### ✅ Step 5 CLOSED 2026-08-17 `[AE4-UI-2026-08-17]` — and it needed no foreign DLL at all
>
> Step 5's claim is about **persistence of two checkboxes**, not about deployment, so it can be
> settled before the rest of the batch is staged. Both boxes were ticked, the app was closed, and the
> app was relaunched. **Two independent detectors, and they agree:**
>
> * **The persisted file.** `%LOCALAPPDATA%\UE5CEDumper\ui-options.json` → `proxyDeploy` carries
>   `forceOverwrite: true` and **no `allowForeignOverwrite` / `foreignConsent` key exists at all**.
>   This is stronger than reading the UI: an absent key *cannot* come back ticked.
> * **The relaunched UI.** ☑ `Force Overwrite`, ☐ `Replace other tools' DLLs`.
>
> That is exactly the required asymmetry — the destructive half does not persist. `Force Overwrite`
> was returned to OFF afterwards so the app is left as found.
>
### ✅ ALL SEVEN STEPS CLOSED 2026-08-17 `[AC1-UI-2026-08-17]` — on a synthetic folder, no real game touched

§4.1's preferred route worked, so **Light Maze was never involved**. Staged under
`D:\SteamLibrary\steamapps\common\ZZSynthProxyTest\ZZSynth\Binaries\Win64\` with two deliberate risk
reductions: the `-Win64-Shipping.exe` is a **57-byte text stub** (the scanner pattern-matches the
filename and nothing there is ever executed, so real executable bytes serve no purpose), and the
foreign DLL is a copy of **Intel's `tbbmalloc.dll`** rather than a System32 binary — it only has to
carry a `ProductName` that is not ours. SHA-256 recorded before and after every step, per §4.1
condition 1. `Found 17 UE game(s)` confirmed the synthetic folder is detected.

| step | verdict | evidence |
|---|---|---|
| 1 | ✅ | row reads `OtherProxy` / **`Other proxy: oneAPI Threading Building Blocks`** — the panel really does read the foreign DLL's own `ProductName` |
| 2 ⚠ the regression | ✅ | `Force Overwrite` alone → **`Deployed: 0 success, 1 failed`**, row still `OtherProxy`, Error cell **`Refused: another program…`** |
| 3 | ✅ | SHA-256 **byte-identical** to the planted file afterwards — "refused" means *not written*, not merely *reported as refused* |
| 4 | ✅ **both halves** | both boxes → `Deployed: 1 success`, file now SHA-matches `dist/proxy/dxgi.dll`, and `view-0.log` carries `[WARN] Replacing another program's dxgi.dll in ZZSynthProxyTest (oneAPI Threading Building Blocks (oneTBB) 2021.13.0) — foreign overwrite was explicitly allowed`. It names the product **and its version** |
| 5 | ✅ | see the block above — two detectors, `ui-options.json` has no foreign-consent key at all |
| 6 | ✅ | our proxy already at 1.0.0.3262, `Force Overwrite` only → `Deployed: 1 success`, i.e. it **redeployed** rather than skipping as "already current" |
| 7 | ✅ **stronger than asked** | foreign DLL re-planted, then `Update All` with **BOTH boxes ticked** → `All 10 deployed proxy DLL(s) already up-to-date`, row untouched, SHA still the foreign one. The count of **10** shows the pre-gate excluded it before consent was ever consulted, so the hard-coded `ForeignConsent: false` beats the UI state |

**Cleanup asserted, not assumed:** both synthetic trees removed and
`D:\SteamLibrary\steamapps\common` re-counted back to its original 63 children with no `ZZ*` left.

> **✅ CORROBORATED 2026-08-19 by the maintainer's own run.** The maintainer worked the 繁中 checklist
> off a NAS copy and ticked **all six** of its AC1 rows (which fold this section's seven steps into
> six — zh-TW row 2 carries both "refused" and "bytes unchanged"). This is a **second, independent
> pass** over the same claims, and the row that matters is the same one: Force Overwrite alone
> refuses and leaves the foreign DLL byte-identical.
> ⚠ **Recorded honestly: not observed here.** No log, screenshot or hash from that run reached this
> repo — the evidence is the maintainer's ticks, nothing more. It does not change the verdict, which
> was already closed above on evidence that *was* observed; it only removes the last reason to keep
> the item in the 繁中 mirror. **AC1's section was deleted from `pending-verification_zh-TW.md`
> accordingly**, on the mirror's own rule (an item recorded closed here does not stay listed there).
> The audit register's `⚠ Live check owed` caveat on the AC1 row is retired in the same commit; its
> ✅ did not move, because the fix had already shipped in build 3191.

### ✅ AE4 step 4 — removal half CLOSED, gate half NOT OBSERVABLE `[AC1-UI-2026-08-17]`

The same staging gave AE4 step 4 the leftover it lacked: a `…\ZZSynthOrphan\ZZOrphan\Binaries\Win64\`
holding **only** our `version.dll` and no exe.

**Removal works, and three independent detectors agree** — which matters because this is the
Recycle-Bin-only policy (B13/B41) actually holding in practice rather than in unit tests:
1. Panel: `Cleaned 1 of 1 leftover(s) — **1 file(s) recycled, 4 folder(s) removed**`.
2. Disk: the four-level tree is gone and the ceiling `…\steamapps\common` survives untouched.
3. **Recycle Bin: the file is recoverable** — `D:\$RECYCLE.BIN\S-1-5-…-1001\$RG84NG7.dll`, 2,860,544 B.

The confirmation dialog is itself worth recording: it lists the exact folders it will try **in order,
each only if left empty**, prints `Not touched: D:\SteamLibrary\steamapps\common`, and explains *why*
this is judged a leftover (no Steam appmanifest names `ZZSynthOrphan`; no executable survives under
the tree). `Cancel` reports `Cancelled — nothing was removed`.

⚠ **The gate arm is NOT verified.** Pressing `Delete checked` while a scan ran opened the
confirmation dialog rather than refusing — but that proves nothing either way, because the scan
finished inside the 2 s before the click and because the dialog opening is not the delete *running*.
**Same measurement limit as steps 1 and 2**: every operation here completes inside one input
round-trip. The gate itself is proven to exist and to name its operation (see AE4 step 1), so what is
missing is specifically the `IsRemovingOrphans` arm.

⚠ **A trap for whoever re-checks the Recycle Bin:** the first probe reported *no* recycled file and
nearly became a filed defect. `shutil.copy2` preserves mtime, so the `$R…` entry carries the
**source's** timestamp, not the deletion time — filtering the Recycle Bin by "modified in the last
30 minutes" hides it. Match on **size**, never on time.

### ✅ CLOSED 2026-08-17 `[GRP4-UI-2026-08-17]` — U3 + U17 — struct previews: dropped members, then wrong widths (builds 3169, 3171)

**Verified on the vehicle this file already named, and it carries its own negative control.**
DumperTest Development, dist 3262, Live Walker → `DumperTestActor_0` → `Map_IntToVec3f` (`0x518`).

The map expands to three distinct entries, each rendering **all three components**:
```
[0] 1 → {X=6201, Y=6202, Z=…}      [1] 2 → {X=6211, Y=6212, …}      [2] 3 → {X=6221, Y=6222, …}
```
and drilling into `[0]` gives the whole struct with offsets, widths and addresses:
```
0x0  X  FloatProperty  6201   00C8C145   0x1B062E6A964
0x4  Y  FloatProperty  6202   00D0C145   0x1B062E6A968
0x8  Z  FloatProperty  6203   00D8C145   0x1B062E6A96C
```

* **U3 (dropped members) — fixed.** Three members, not one.
* **U17 (wrong widths) — fixed.** Offsets `0x0/0x4/0x8` and addresses exactly 4 bytes apart, i.e.
  read as `float`, and the hex round-trips (`6201.0f` = `0x45C1C800` → little-endian `00C8C145`).
* ⭐ **The negative control is free and exact.** The old defect displayed `f:[6203.0000]` — a single
  float, the **last** one, from skipping 8 bytes of a 12-byte struct. `6203` is precisely `Z` here, so
  the broken rendering is the current output with `X` and `Y` deleted. Nothing else in the sample
  makes the before/after that legible.

### 🔲 U3 + U17 — original checklist (kept for the steps)

*Needs a connected game. See dev-log builds 3169 and 3171. **The decode RULES are unit-pinned
(35 assertions, four negative controls); the LOOKUP half is not** — resolving a `UScriptStruct*` and
`WalkClass`-ing it touches target memory, and no target compiles `Ubel.cpp`.*

> **There is a known-good vehicle already on record.** `docs/todo.md` names `Map_IntToVec3f` as the
> field that reproduced the original `f:[6203.0000]`, so the before/after is one row.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | Live Walker → expand a struct-valued `TMap`/`TSet` element | `{X=…, Y=…, Z=…}`, not `f:[…]` | U17: those callers now use the reflected layout |
> | 2 | cross-check against `hexValue` on the same row | all components present and correct | the hex always held them; that is how U3 was caught |
> | 3 | a UE5 **LWC** title (24-byte `FVector`) | three components at real magnitudes | the case the byte-blind path structurally cannot get right |
> | 4 ⚠ control | any **GAS** title — a `FGameplayAttributeData` preview | still `BaseValue` / `CurrentValue`, no pointer halves | **the regression guard**: GAS really does have a vtable, and "just delete the skip" would show four values here |
> | 5 | a struct with NO resolvable layout | still `f:[…]` | the byte-blind fallback is retained on purpose, not dead |

### ✅ A3 — VERIFIED 2026-08-19 `[A3-DOUBLE-2026-08-19]` (steps 1, 2, 4; build 3168)

**Steps 1, 2 and 4 done headless on DumperTest Development / dist 3263** with
`tools/verify/a3_struct_path.py`. Step 3 (Group Scan / Property Search Deep) not run — it is the
*asymmetry* corroboration, not the check.

⚠ **STEP 1'S INSTRUCTION IS WRONG ON A UE5 TITLE AND MUST NOT BE FOLLOWED LITERALLY.** It says
"Value Search, **Float** (or NumericAll)". Under **LWC an `FVector` is a double-precision
`FVector3d`**, so a Float scan *structurally cannot* see `RelativeLocation.X`. Measured side by
side on the same session: **Float/1.0 → 0 `*Scale3D*`, 0 `*Location*`; Double/0 → 177 and 114.**
Taken at face value the Float run reads as a clean FAIL and would have condemned a working fix.
Use **Double** (or `NumericAll`) on any UE5 game; Float remains right only for a UE4-era title.

* **Step 1 — PASS, and the statistic needs no baseline.** A vector leaf is a candidate path ending
  `.X`/`.Y`/`.Z`; strip the leaf and you have the struct field that produced it. Under the defect a
  class could contribute **at most one** distinct such parent, so the measurement is just *how many
  classes contribute two or more*. **151 classes do** (over all 3,450 candidates of a
  `Double`/`Exact 0` scan, `deadline_hit=false`, 25,172 objects / 1,415 classes):
  `TraceQueryTestResults` **72 distinct**, `RigVMMemory_Work` 34, `ArchVisCharMovementComponent` 26,
  `DumperTestCharacter` / `BP_ThirdPersonCharacter_C` / `ArchVisCharacter` 19 each — the last group
  spanning genuinely unrelated branches (`AttachmentReplication.LocationOffset`,
  `AttachmentReplication.RelativeScale3D`, `BasedMovement.Location`, `BaseTranslationOffset`),
  which is exactly the cross-branch suppression the whole-walk guard caused.
* **Step 2 (CONTROL) — recorded, unchanged in shape.** The `FVector` scan returns 45 rows whose
  names are bare struct fields (`RelativeScale3D`, `OffsetScale`), i.e. no `.X` expansion at all —
  consistent with the row's point that `acceptedStructNames` is non-empty for a vector scan so the
  recursion is skipped and the guard never fired there. ⚠ Honest limit: nobody captured this number
  *before* 3168, so this is a **baseline for the future**, not proof of no-change.
* **Step 4 — PASS.** `hit the 4000 scan-field cap` appears in **0** of the `scan-*.log` files
  anywhere under the log root, so the cap is unreachable in practice as intended.
* ⚠ **A pagination trap the rig now guards**: page size is server-side, so looping "until a short
  page" can stop on a full final page and silently under-count the population being measured. Drive
  the loop from `total` in the `begin_value_scan` reply and complain if the totals disagree.

> *(original row kept below for its steps)*

### 🔲 A3 — one FVector per class was ever indexed (build 3168)

*Needs a connected game. See dev-log build 3168. **The guard's CONTRACT is unit-pinned
(`Test_Aura_StructPathGuard`, negative control 7 red); the WALK THAT USES IT is not** — no test target
compiles `Aura.cpp`, so `expandFields` calling the guard has never run against a real class.*

> **Why this is cheap: the before/after is a single scan and the expected delta is huge.** The guard
> was whole-walk instead of path-scoped, so only the FIRST field of a given `UScriptStruct` type in a
> class contributed leaves — `Location` was indexed, `Velocity` / `Scale3D` / `Extent` never were,
> subtree and all, across unrelated branches.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | Value Search, **Float** (or NumericAll), any value, on a class with a pawn/actor | rows whose field name ends in `.Velocity` / `.Scale3D`, not only `.Location` | before 3168 exactly one FVector per class could appear; this is the whole defect |
> | 2 ⚠ control | the same scan with data type **FVector** | unchanged vs before 3168 | `acceptedStructNames` is non-empty for vector scans, so the recursion is skipped and the guard never fired there — **if this changed, the fix reached somewhere it should not** |
> | 3 | Group Scan or Property Search **Deep** for the same field | already found it before 3168 too | those walkers were always path-scoped; confirms the asymmetry that made this diagnosable |
> | 4 | grep `scan-*.log` for `hit the 4000 scan-field cap` | absent on ordinary classes | the new cap is meant to be unreachable in practice; if it fires routinely the value is wrong |
>
> ⚠ **Do not verify with an FVector scan.** It is the one data type the defect never touched, so a
> green FVector run proves nothing — that is what step 2 is for, as a control rather than as evidence.

### ✅ CLOSED 2026-08-16 `[ELLIOT-PIPE-2026-08-16]` — AB4: the Aura half of the ordered-predicate width fix

*Needs a connected game. See dev-log build 3133. **The Radar half is unit-pinned (16 new assertions,
negative control 6 red); this batch is exactly the half that could not be** — no test target compiles
`Aura.cpp`, so the wiring from `Find()` to `FindEntry()` across the first-scan, native-C and refine
paths has never executed against a real object pool.*

> **✅ ALL SIX CHECKABLE STEPS PASS. Steps 2 and 4 are a PAIRED control and step 4 is EXHAUSTIVE.**
>
> **Conditions.** Elliot (`Elliot-Win64-Shipping.exe`, PE `6A577F4E1D91B000`), DLL build **3156**
> loaded as `proxy:dxgi.dll`, scan resolved by AOB — `GObjects 0x149BFC140` / `GNames 0x149B18600`
> (`GNAM_V8`) / `GWorld 0x149D8BDA0` / `GEngine 0x149D8E290` (`GENG_X1`), `ue_version=504`,
> `item_size=24`, **84,990 objects / 84,387 scanned**.
> **Driven straight over the named pipe, NOT through the UI** — a deliberate choice: this batch is
> about `Aura`'s `Find()`→`FieldDescriptor` wiring, and going pipe-direct removes the Avalonia layer
> as a variable. The trade is that it does **not** exercise the Value Search panel's own binding;
> that half rides on the separate 14-MED UI batch below.
>
> | step | request | result | verdict |
> |---|---|---|---|
> | 1 regression | `NumericNoByte` Exact `100` | **34,117 rows, `deadline_hit=false`** (complete, untruncated) — Float 24,361 / Double 9,695 / Int 61, **0 one-byte rows** | ✅ Exact unchanged, and correctly excludes 1-byte widths |
> | 2 the fix | `NumericAll` Smaller `500` | **81,547 one-byte rows** (`ByteProperty` 81,283 + `Int8Property` 264) out of 1,000,000 | ✅ these were structurally impossible before |
> | 3 sign leak | `NumericNoByte` Bigger `-5` | `UInt32Property` 240 + `UInt16Property` 26 = **266 unsigned rows** | ✅ a negative target no longer suppresses the unsigned parse |
> | 4 ⚠ control | `NumericAll` Bigger `500` | **total 367,401, `deadline_hit=false`, all 367,401 paged, one-byte rows = 0** | ✅ **the pruning half still prunes — over the COMPLETE set, not a sample** |
> | 5 refine | Next Scan, same predicate, on step 2's session | byte rows survive, tally identical (`ByteProperty` 5,238 + `Int8Property` 105 at the 40k cap) | ✅ the `cmpEntry` branch does not drop the new entries |
> | 6 native-C | step 2 + `native_c`, `native_align=4`, `newest_first` | **8,321 one-byte + 8,628 unsigned rows**; distribution flattens (`UInt16`=`UInt32`=3,205) exactly as hole-scanning at multiple widths predicts | ✅ the separately-wired `&e`/`&me` path took |
> | 7 `Between` | not run | known-unfixed by design | — |
>
> **Why 2-vs-4 is the real evidence and not two loose numbers:** same `data_type` (`NumericAll`, so
> 1-byte widths are in scope for *both*), same value, same object population — only the predicate
> direction differs. 81,547 → 0. That is precisely the shape a correct implementation must produce
> (no byte exceeds 500), and it is the negative control the batch asked for. Step 4 completing with
> `deadline_hit=false` is what upgrades it from "sampled 40k and saw none" to a claim over the pool.
> *Honest limit:* steps 2/3/6 hit their result cap, so their counts are lower bounds, not censuses.

1. **⚠ REGRESSION FIRST — an ordinary Exact scan is unchanged.** Value Search → `NumericNoByte` →
   Exact → a value you know exists → First Scan. `ScanType` now reaches `BuildNumericTargets` but
   defaults to `Exact`, and Exact must be byte-identical. Compare the row count against a pre-3133
   build if you can; any change here is a real finding.
2. **THE FIX, first scan.** `NumericAll` → **Smaller** → `500` → First Scan. The results must now
   contain **`ByteProperty` / `Int8Property` rows**, which they never did before — every 1-byte field
   holds a value below 500 by definition. If 1-byte rows are still absent, the Aura wiring did not
   take. **Record the row count and whether byte-width rows appear**; a count alone proves nothing.
3. **The sign leak, which the finding never mentioned.** `NumericNoByte` → **Bigger** → `-5`. Every
   unsigned field satisfies it, so `UInt16`/`UInt32`/`UInt64` rows must appear. They were dropped
   wholesale before, because a negative string suppresses the entire unsigned parse.
4. **⚠ The opposite direction must still PRUNE.** `NumericAll` → **Bigger** → `500`: 1-byte rows must
   be **absent** (no byte exceeds 500). That half of the old gate was correct and the fix must not
   have widened it into a false-positive machine. This is the control for step 2.
5. **Refine still works on the new entries.** After step 2's scan, do a **Next Scan** with the same
   predicate and confirm the byte rows survive and the count narrows sanely. The refine path takes a
   different branch (`cmpEntry` vs the prev-value `cmpTarget`) and is separately wired.
6. **Native-C scanning.** Repeat step 2 with **native-C enabled**. Those paths enumerate
   `multiTargets->entries` directly rather than resolving per member, and were wired separately
   (`&e` / `&me` instead of `e.bytes`) — a distinct code path with the same intent.
7. **`Between` is KNOWN-UNFIXED, do not report it as a bug.** Its two bounds are built by two
   independent calls, so `Between -100 100` still drops unsigned widths. A correct fix needs a joint
   builder; it is filed, not forgotten.

### ✅ VERIFIED 2026-08-19 — SkiaSharp/HarfBuzzSharp ABI alignment: the UI must stop crashing

> ### ✅ CLOSED 2026-08-19 `[SKIA-ABI-2026-08-19]` — all four steps, by the maintainer
>
> **Evidence: the maintainer's own runs, reported directly. Not observed by an agent** — nobody
> re-derived this from a log or a dump, so it is recorded on their authority and stated as such
> rather than dressed up as a measurement.
>
> All four steps below came back ticked: the tab-walk + 繁中 rendering regression check (1), the
> Elliot → Live Walker → `GameState` → Copy CE XML repro left running (2), no crash to symbolize (3),
> and page heap removed before judging performance (4).
>
> ⚠ **Step 3's "do not close on one clean session" was satisfied by accumulation, not by one run** —
> that judgement is the maintainer's, and it is the only way this item could ever close: its PASS
> condition is the *absence* of an event. If the UI ever dies at `libSkiaSharp` again, this reopens;
> the page-heap + x64 `llvm-symbolizer` recipe below is kept for exactly that.

*Needs the UI running for a while. See dev-log build 3127. **This is the one item on this whole
register where a PASS is "nothing happened for a few sessions"** — so it cannot be closed by a single
run, and a crash is worth more than a green session.*

**What was wrong.** `Avalonia.Skia 12.1.1` is built against **SkiaSharp 3.119.4** and
`Avalonia.HarfBuzz 12.1.1` against **HarfBuzzSharp 8.3.1.3**. Routine `chore(deps)` bumps
(`5346f907` and two before it) had carried the project to **SkiaSharp 4.151.1** (one major ahead) and
**HarfBuzzSharp 14.2.1.2** (six ahead). NuGet cannot warn about this — Avalonia's constraint is an
open-ended minimum, so a major jump *satisfies* it: no NU1608, no NU1605, and
`TreatWarningsAsErrors=true` never had anything to catch.

**How it was caught, and why the first dump was not enough.** The UI died with
`0xC0000374` **STATUS_HEAP_CORRUPTION** ~2.3 s after a Copy CE XML on Elliot. That dump named
nothing: heap corruption surfaces at the *next* heap operation, so its stack is the **detector, not
the culprit** — it showed only ntdll's heap-error path on the UI thread. Full **page heap**
(IFEO `GlobalFlag=0x02000000` + `PageHeapFlags=0x3`) converted the next occurrence into an immediate
`0xC0000005` at **`libSkiaSharp.dll+0x102B8D`** (WER event `AutoVerifierV2`, `verifier.dll` on the
stack, target address a guard page). That is the whole method: **a heap-corruption dump is worth
almost nothing; re-run it under page heap.**

1. **⚠ THE REGRESSION CHECK COMES FIRST, AND IT IS BROAD.** Skia and HarfBuzz are what draw and shape
   *everything*, so a downgrade touches every pixel. Open each tab in turn; look for missing glyphs,
   wrong metrics, clipped text, DataGrid rows that fail to paint, and check a 繁中 string renders
   (HarfBuzz went back **six** majors — text shaping is where breakage would show first).
2. **The original repro, now expected to survive.** Elliot → Live Walker → `GameState` → **Copy CE
   XML** with AOB on and depth 4, then leave the UI up for several minutes. Two crashes happened
   within ~14 minutes of each other on the old versions.
3. **⚠ Do not close this on one clean session.** The old build ran for many sessions before anyone
   saw it. A pass is several sessions of ordinary use. **A crash is a definitive FAIL** — capture the
   WER dump and say whether the faulting module is still `libSkiaSharp`.
   **Session 1 of N `[ELLIOT-2026-08-16]`: no crash.** Build **3127** (the aligned one — confirmed
   from `Logs\UE5DumpUI\init-0.log` `Version: 1.0.0.3127`, not assumed), Elliot, 20:49–20:50, a full
   connect + scan + walk. **This is one data point and closes nothing** — the old build survived
   many sessions before the first crash, and this one was shorter than the session that crashed.
4. **Turn page heap OFF before judging performance.** `reg delete "HKLM\SOFTWARE\Microsoft\Windows
   NT\CurrentVersion\Image File Execution Options\UE5DumpUI.exe" /f`. With it on, everything is slow
   and memory-hungry; that is the tool, not the build.
5. **What this does NOT prove — updated, the fault IS symbolized now.** `SkiaSharp.NativeAssets.Win32`
   **ships `libSkiaSharp.pdb`**, so the earlier "faulting function unknown" was an assumption, not a
   fact. `libSkiaSharp+0x102B8D` (4.151.1 win-x64, binary identity confirmed by an exact 12,272,440-byte
   match with `dist/`) resolves to `skia_private::TArray<SkPathVerb,1>::size` inlined through
   `SkSpan` → `SkPathBuilder::verbs` → **`SkPathBuilder::computeFiniteBounds`**. So the fault is
   **path geometry, not text shaping — HarfBuzz is exonerated for this crash** — and `SkPathBuilder`
   is precisely what Skia restructured across this major.
   What is still unproven is the **caller**: naming the callee does not name who handed it a
   mis-shaped path. If crashes continue at the aligned versions the ABI hypothesis is refuted and the
   next step is a Skia-side bug. If one does recur, capture a page-heap dump and symbolize the FULL
   stack — now known to be possible (use the **x64** `llvm-symbolizer` under
   `VC\Tools\Llvm\x64\bin`; a recursive search finds the ARM64 copy first and it will not run).

### ⬜ NEW 2026-08-17 — AA12 / AA13: the freeze script must stop lying about success (key: FreezeOutcome)

*Needs a **real Cheat Engine** plus a connected game. See dev-log build 3125. The Lua rig stubs every
CE global, so what is unproven is precisely the CE-side behaviour: whether the window stays up and
whether the record ends ticked or unticked.*

1. **⚠ REGRESSION FIRST — a normal freeze still works and still closes.** Property Search → a numeric
   field on a class with live instances → Copy Freeze Script → paste into CE → tick. The value must
   hold, the Lua Engine window must **close**, and the record must stay ticked. Everything below
   changed this path.
2. **The hard failure — the whole point.** Tick the same script with **UE5Dumper.dll NOT injected**.
   Expect: a `showMessage` naming the reason, the record **unticked by itself**, and the Lua window
   **still open**. Before this it silently reported success, closed the window, and stayed ticked.
3. **⚠ The legitimate empty case must NOT untick.** Freeze a class with **zero live instances right
   now** (an enemy type not yet spawned). Expect: record **stays ticked**, window **stays open**, and
   one line — `[Freeze] armed: no live instances of X right now`. Then make one spawn and confirm the
   freeze takes hold within ~5 s. **If this unticks, the fix broke the feature and that is worse than
   the bug** — report it.
4. **A misspelled class is indistinguishable from (3), by design.** Edit `CFG.className` to nonsense
   and tick. It must behave exactly like step 3 — armed, 0. This is not a defect: the DLL answers
   `SetDone(0)` for both, so claiming a typo would be a guess. Confirm it does not claim one.
5. **An OLD helper is reported as unknown, not as a verdict.** Embed a **pre-1.2** `ue5_freeze_helper.lua`
   (any copy from before build 3125) and tick a newly generated script. Expect the "older
   ue5_freeze_helper.lua … re-inject it" line, the window left open, and the record **left ticked** —
   it must neither close over it nor untick a freeze that may well be running.
6. **Two freeze scripts still coexist.** Tick two different freezes at once, untick one: the other
   must keep working. The keyed-handle table is untouched by this change, and this is the check that
   proves it.

### ⬜ NEW 2026-08-17 — G12 / G3: the offset family, and the apply_rescan gate

*Needs the DLL injected. See dev-log builds 3119 / 3121. G12's invariant is unit-pinned; its WIRING
is not, because no test target compiles `Genau.cpp` or `Ubel.cpp`.*

1. **⚠ G12 REGRESSION — enums and TArray elements still read correctly.** Open Live Walker on a
   class with an **enum** field and a **TArray** field. The enum must show its member NAME (not a
   raw int) and the array must show its element type. All four writers of the family moved; this is
   the check that they still agree.
   **🟡 TArray half ✅, enum half still open `[DSA-2026-08-16]`.** The session walked arrays cleanly
   — `{"array_elem_size":8,"array_inner_addr":"0x1B59CB7FD80","array_inner_type":"ObjectProperty",
   …,"name":"ModelComponents","type":"ArrayProperty"}` — so `FARRAYPROP_INNER` is not 8 bytes off.
   **Zero `EnumProperty` appeared in the entire session**, so `FENUMPROP_ENUM` / `FBYTEPROP_ENUM`
   are untested. Pick a class with an enum field next time; that is the half that can still be wrong.
   **✅ BOTH HALVES NOW DONE `[G12-PIPE-2026-08-17]`** — DumperTest Development, build 3262, via
   `walk_instance` over the pipe (never `walk_class`, and without `lean`, so these are real per-object
   reads). **The enum half is covered by four fields across BOTH writers**, which is what makes it
   evidence rather than one lucky lookup:
   `Grade` → `EDumperTestGrade::Elite` and `UpdateOverlapsMethodDuringLevelStreaming` →
   `EActorUpdateOverlapsMethod::UseConfigDefault` exercise `FENUMPROP_ENUM`; `RemoteRole` →
   `ROLE_None` and `NetDormancy` → `DORM_Awake` exercise `FBYTEPROP_ENUM`. `Grade` is the
   discriminating one: the sample's `EDumperTestGrade` has a **hole at 3..6** (`Legend`=7), so a
   build that confused index with value could not land on `Elite` by accident — and `Elite` is the
   value `tools/ue-sample/README.md` documents in advance.
   TArray regression re-confirmed on the same reply: `Arr_Int` inner `IntProperty`/4,
   `Arr_Struct` inner `StructProperty`/**32** (FName 8 + int 4 + pad 4 + FText 16), `Tags` and
   `Layers` `NameProperty`/8.
2. **G12, the case it actually fixes.** Needs a title whose offset validation takes the **heuristic
   fallback** — `scan-0.log` / `offsets-0.log` shows `Cannot find Guid or Vector struct`. Solarpunk
   is the documented one (though a later build resolved via `Guid` instead, so it may not reproduce).
   On such a title, enum names and TArray inner types were previously read 8 bytes off. Confirm they
   are right now, and **record which branch the log shows** — a run that resolved via `Guid` did not
   exercise this.
   **⬜ Branch recorded, and it is the WRONG one `[DSA-2026-08-16]`:** `FindStructByName: Found
   'Guid' at 0x1B5FB6840C0` → `ValidateAndFixOffsets: Using struct 'Guid'`, i.e. the validated path,
   with `FStructProp::Struct = +0x70` published from a real measurement. The Step 2.5 default block
   G12 repaired was never entered. Still needs a heuristic-fallback title.
3. **⚠ G3 REGRESSION — Extra Scan → Apply still works.** Needs a game where something is missing to
   scan for (all 34 tested games resolve GWorld, so this may not be reachable). If it is: press
   Extra Scan, then Apply, and confirm `offsets-0.log` still contains exactly **one**
   `ValidateAndFixOffsets: Starting` line — the gate's whole purpose.
4. **⚠ G3 REGRESSION — GEngine still resolves after an Apply.** The GEngine second pass was hoisted
   out of the gated block precisely so it keeps running. If Apply is reachable, confirm
   `apply_rescan: Applied GEngine=0x…` still appears when GEngine was previously unresolved.
5. **✅ Free log check, no game needed beyond a normal session.** `walk-0.log` must show no burst of
   `Misaligned field … possible wrong FPROPERTY_OFFSET`. That line is the direct witness for a split
   or stale family.
   **PASS `[DSA-2026-08-16]`** — a 2.4 MB `walk-0.log` covering a full snapshot capture (35,891
   objects, **2,917,264 fields**) contains **zero** `Misaligned` lines and zero `[WARN]` lines of any
   kind. Conditions matter here, so record them: this is the *validated-`Guid`* branch (step 2), so
   it proves the family is coherent on the path that was already coherent — it is a regression check,
   not evidence for the repair.

### 🟡 GROUP 5 opened 2026-08-18 `[CE-2026-08-18]` — plugin bridge live, freeze record reaches CE

The AOBMaker CE plugin **is installed** (maintainer, 2026-08-18). With Cheat Engine 64-bit running:

* **AB1/AB2 — substantially verified.** `\\.\pipe\AOBMakerCEBridge` exists, UE5DumpUI's toolbar reads
  **`● AOBMaker Connected`** (green), and once CE attaches to a process an **`Unreal Engine` menu
  appears in CE's own menu bar** — three independent signs the plugin is loaded and talking.
* **Y9's remaining consumer — CLOSED.** With the bridge up the **Freeze button** is enabled (it is
  bound to `IsAobMakerAvailable`), opens the same dialog pre-filled `255`, rejects `9999` with
  `uint8 holds 0 to 255 — 9999 would be written as 15`, and on `200` reports
  **`Freeze script created in CE: Freeze: DumperTestActor::U8_Max = 200`**. CE's address list then
  holds that exact record as `<script>`. Y9 now has **both** consumers verified.
* **CE Lua hygiene — the bail-out shape is right.** Ticking the record before the helper was present
  produced a plain, actionable dialog — *"[Freeze] ue5_freeze_helper.lua not found in this table.
  Setup: UE5DumpUI → Tools → Inject Freeze Helper into Current CE Table"* — **and left the record
  UNTICKED**, which is CLAUDE.md's MUST for a bail-out that applied nothing. No Lua Engine window was
  left covering CE.
* **`Tools → Inject Freeze Helper into Current CE Table` works.** CE's `Table` menu then lists
  `ue5_freeze_helper.lua`, so the file really is attached to the table.

* **✅ The freeze ARMS, end to end.** The control was run: same flow on **Lushfoil**, CE re-attached
  to it, record ticked →
  ```
  [Freeze] armed: no live instances of PrimitiveComponent right now -- the freeze applies as they spawn.
  ```
  A success message that also states the *actual* state rather than implying a write happened. The
  dialog's **bool flavour** works too (`BoolProperty -> bool`, `0x272`, pre-filled `true`, hint
  *"Accepts: true / false / 1 / 0"*).

* **✅ And the earlier DumperTest failure was NOT a defect — the script diagnosed it correctly.**
  Opening CE's Lua Engine surfaced what the red ✗ meant:
  ```
  [ue5_freeze] DumperTestActor: 3 consecutive rescans failed -- freeze STOPPED writing
  (last error: the contract symbol resolved to the wrong memory (stale address) -- re-inject the DLL).
  Re-enable the record after fixing it.
  ```
  CE was still attached to a DumperTest process that had since been killed, so the registered symbol
  pointed at dead memory. **This is CLAUDE.md's "never report a mailbox failure by guessing" rule
  working**: it names the specific cause, stops writing after 3 consecutive failures instead of
  spinning, and tells the user what to do and to re-enable afterwards.
  ⚠ **Operational note that cost a diagnosis here:** the message is only visible with **CE's Lua
  Engine window open** — by design (DEBUG-gated hygiene), so a stopped freeze is silent until you
  open it. Open the Lua Engine *before* concluding anything about a record.
  ⚠⚠ **And read the checkbox correctly** (maintainer, 2026-08-18): in CE a **big red ✗ on a record's
  checkbox means ACTIVE, not failed** — an inactive record is an EMPTY box. So the red ✗ seen here
  was not a failure indicator at all; it was CE correctly reporting the record as still enabled while
  the freeze had stopped writing. That is what turned this observation into
  `[FREEZESTUCK-2026-08-18]` below.

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[FREEZESTUCK-2026-08-18]` — an abandoned freeze must untick its own record

*Needs **any** connected game plus CE. The whole batch is one freeze record and one DLL re-injection.*

> **What is already pinned offline and must NOT be re-checked here:** 13 executable cases in
> `scripts/tests/freeze_helper_test.lua` (`lua scripts/tests/freeze_helper_test.lua`, **154 checks**
> — re-measured 2026-08-20; the `117` this row carried was stale, and audit L12's row already had
> the right figure in its `83 / 154 / 91` triple)
> drive the abandonment against a stubbed CE — including a memory-record stand-in whose
> `Active = false` dispatches the `[DISABLE]` chunk, so the untick really does run `stop()` and
> destroy both timers; a control proving a **transient** failure does NOT untick; a no-`memrec`
> case; and a deleted-record case. Plus `FreezeScriptGeneratorTests` for the `CFG.memrec = memrec`
> wiring and for the removal of the old unfollowable "Re-enable the record after fixing it".
> **What no offline test can reach** is whether CE's real `TMemoryRecord.Active = false`, driven
> from a Lua timer, behaves like the stand-in. That is step 3, and it is the only step that matters.
>
> ⚠ **Read the checkbox correctly**: in CE a big red ✗ on a record's checkbox means **ACTIVE**, not
> failed; an inactive record is an **empty box**. Reading it backwards inverts every step below.
>
> ⚠ **Open CE's Lua Engine window before step 2** — the abandonment message is printed there, and
> hygiene closes that window on a clean enable.
>
> | step | do this | expect |
> |---|---|---|
> | 1 | Property Search any supported field → row **Freeze** → create the script → tick the record | the record ticks (red ✗) and the value holds |
> | 2 | With it still ticked, **re-inject `UE5Dumper.dll`** (or kill the DLL host) and wait ~15 s (3 rescans × 5 s) | the Lua Engine prints `… consecutive rescans failed -- freeze STOPPED writing … This record has been unticked; re-enable it after fixing the cause.` |
> ### 🟡 OFFLINE HALF RE-CONFIRMED GREEN 2026-08-20 — steps 1-5 remain CE-only, deliberately
>
> All three Lua rigs were re-run today and are green: `dissect_test` **83**, `freeze_helper_test`
> **154**, `invoke_helper_test` **91** — 328 checks, 0 failures. So the abandonment logic this row
> says "must NOT be re-checked here" is confirmed still passing on the current tree.
>
> Nothing else here was attempted, and that is correct rather than a gap: the row states outright
> that what no offline test can reach is whether **CE's real `TMemoryRecord.Active = false`, driven
> from a Lua timer, behaves like the stand-in** — and that is step 3, the only step that matters.
> A CE session is genuinely required.

> | 3 ⚠ THE ONE THAT MATTERS | look at the record's checkbox | it is now an **EMPTY box**. Before the fix it stayed a red ✗ forever, claiming a cheat nothing was applying |
> | 4 | check CE is still responsive; look for an error dialog | none. The untick is deferred onto a one-shot timer precisely so `[DISABLE]` does not destroy a timer from inside its own handler |
> | 5 | re-inject a working DLL, then re-tick the record | the freeze arms again and holds — i.e. step 2's advice is followable, which it was not before |
> | 6 ⚠ control | with a healthy DLL, leave a freeze running untouched for a minute | the record **stays ticked** and keeps writing. One transient `mailbox busy` must not untick anything |
> | 7 ⚠ control, opportunistic | delete the memory record while its freeze is mid-abandonment | no Lua error dialog; the failure is still reported |

### ⬜ FIXED 2026-08-18, NEEDS A LIVE CHECK `[PASTECRASH-2026-08-18]` — a clipboard paste must no longer terminate the UI

*Needs the UI only — **no game, no DLL, no pipe**. Three halves now (a follow-up hardening pass
landed on 2026-08-19): a `Dispatcher.UIThread.UnhandledException` guard
(`Services/DispatcherFaultGuard.cs`) that marks **only** classifier-confirmed input-layer faults
handled, a guard on the clipboard **WRITE** path (`WindowsPlatformService.CopyGuardedAsync`), and a
crash.log headline that states the real phase + uptime instead of the hard-coded phrase "startup
crash".*

> **What is already pinned offline and must NOT be re-checked here:** the swallow SCOPE (92 unit
> tests — `InputLayerFaultClassifierTests`, `DispatcherFaultGuardTests`, `ClipboardWriteGuardTests`,
> `CrashReportFormatterTests` — including negative controls for a ViewModel
> `NullReferenceException`, a mixed our-code/clipboard stack, eight never-swallow exception types, an
> over-deep exception chain, an `AggregateException` that tries to smuggle an unrelated fault
> through, and a marker that must not match a longer method name), plus reflection tests that fail if
> Avalonia renames an allow-listed type or drops one of the async state machines. **What no offline
> test can reach** is whether Avalonia's dispatcher actually raises `UnhandledException` for a fault
> arriving via `Task.ThrowAsync`, which is the entire premise — that is step 2, and it is the only
> step that matters.
>
> ⚠ Step 2 needs the clipboard to genuinely fail. Two ways that both work: hold the clipboard open
> from another process (`OpenClipboard` without `CloseClipboard`), or copy from an app that uses
> delayed rendering and close that app before pasting.
>
> ⚠ **Everything the guard logs — swallowed AND refused — goes to `view`.** The two outcomes used to
> split across `view-0.log` and `init-0.log`, so grepping one file showed half the story. Grep
> `Logs\UE5DumpUI\view-0.log` only.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEPS 1–6 PASS 2026-08-20 `[PASTECRASH-LIVE-2026-08-20]` — including the premise no offline test could reach
>
> UI only, no game. Clipboard made genuinely unusable with `tools/verify/clipboard_hold.py`
> (`OpenClipboard` without `CloseClipboard` from another process — the method this row names).
>
> * **Step 1 — PASS.** `PASTECRASH_BASELINE` pasted normally; the guard has not broken normal paste.
> * **Step 2 ⚠ THE ONE THAT MATTERS — PASS.** With the clipboard held, `Ctrl+V` left the app
>   **running** and the box **unchanged**. The discriminator was deliberate: the clipboard was
>   loaded with a *different* string (`SHOULD_NOT_APPEAR`) before it was locked, so a paste that had
>   silently succeeded would have been visible. It did not appear.
>   ⭐ **This demonstrates the fix's whole premise** — that Avalonia's dispatcher really does raise
>   `UnhandledException` for a fault arriving on the clipboard task. The log line names it exactly:
>   `Input-layer fault swallowed (#1) — the keystroke did nothing, the app is still running.
>   System.Runtime.InteropServices.COMException … [input-layer frame: Avalonia.Win32.ClipboardImpl]`
> * **Step 3 — PASS.** Three pastes gave **#1 → #2 → #3**, so the guard is **not one-shot**; after
>   releasing the clipboard a normal paste worked again (`RECOVERED_OK`), leaving nothing wedged.
> * **Step 4 — PASS.** `Ctrl+C` with the clipboard unusable → swallow **#4**, app alive.
> * **Step 5 — PASS.** A second UI instance rewrote `crash.log`, headed
>   `[2026-08-20 09:42:09] UE5DumpUI crash during STARTUP (uptime 0.16s)` — the real phase and a
>   real uptime, not the old hard-coded phrase. (Stack: `Cannot perform requested operation because
>   the Dispatcher shut down`.) ⚠ Note this **coexists with `AF10`**: the same launch exits with
>   code **1** as AF10 requires *and* writes this report — the two rows are not in conflict.
> * **Step 6 ⚠ CONTROL — PASS.** After recovery, pasting and then typing `_TYPED` both worked and
>   the swallow counter **stayed at 4**. So the guard is dormant when nothing fails; a fifth line
>   here would have meant it was swallowing healthy input.
>
> **Not done:** 4b (`Ctrl+X` then `Ctrl+Z` residue), 4c (the Copy-button WRITE path — needs a
> connected game for a Copy button to have anything to copy), and 7/8, which are opportunistic by
> construction.

> | 1 | start the UI, copy ordinary text, `Ctrl+V` into any filter box | the text pastes | baseline — the guard must not have broken normal paste |
> | 2 ⚠ THE ONE THAT MATTERS | make the clipboard unreadable (above), then `Ctrl+V` into a filter box | **the app is still running**, the box is unchanged, and `Logs\UE5DumpUI\view-0.log` gains `Input-layer fault swallowed (#1)` | this is the crash, reproduced; before the fix the process died here |
> | 3 | repeat step 2 a few times, then release the clipboard and paste again | counter climbs (`#2`, `#3`…), then a normal paste works | the guard is not one-shot and leaves nothing wedged |
> | 4 | `Ctrl+C` in a text box while the clipboard is unusable | same: alive, logged, no crash | `Copy` is the second allow-listed command, and it mutates nothing at all |
> | 4b ⚠ the residue is EXPECTED, not a defect | `Ctrl+X` in a text box while the clipboard is unusable, then press `Ctrl+Z` once | **the app is still running**; Ctrl+X degrades to nothing visible (text not removed, clipboard unchanged) and is logged; the **first `Ctrl+Z` is a no-op** — an extra undo entry is expected, press it again to reach the real previous edit | `TextBox.Cut`'s IL calls `SnapshotUndoRedo` BEFORE the await and `DeleteSelection` after, so a swallowed Cut leaves one undo snapshot with no edit behind it. Swallowed anyway as a judged trade: the snapshot is of text that never changed, so undoing it is a no-op, whereas the alternative is the process dying with a loaded session — and a busy clipboard fails all three keys for one reason, so Ctrl+X alone closing the app would be indefensible |
> | 4c ⚠ NEW, the WRITE half | with the clipboard unusable, press any **Copy** button (Pointer panel addresses, a Live Walker row, Property Search → Copy Offset) | **the app is still running**, nothing is copied, and `view-0.log` gains `Clipboard copy FAILED — nothing was copied` | ~60 Copy buttons went through an unguarded `SetTextAsync`; a faulted `AsyncRelayCommand` rethrows onto the dispatcher WITH our frames on it, so the read guard is structurally obliged to refuse it and the app died. Only the write guard can stop this one |
> | 5 | launch a SECOND copy of the UI (the known duplicate-launch crash) and read `%LOCALAPPDATA%\UE5CEDumper\crash.log` | headline reads `UE5DumpUI crash during STARTUP (uptime 0.??s)` — **not** the old fixed phrase | the honest-phase half, on the one crash that is reproducible on demand |
> | 6 ⚠ control | with an IME active, type into a filter box normally | text arrives as usual; **no** `Input-layer fault swallowed` line appears | proves the guard is dormant when nothing fails — a line here would mean it is swallowing healthy input |
> | 7 ⚠ control, opportunistic | the next time the UI crashes for any reason after startup, read `crash.log` | it says `while RUNNING` (or `during SHUTDOWN`) with a real uptime | proves the phase marker advances; cannot be forced, so tick it when it happens |
> | 8 ⚠ control, opportunistic | if a crash report ever opens `phase still says STARTUP after …` | read it as UNCERTAIN, not as a second defect | the stale-marker branch is a HEURISTIC over a 60 s threshold, and a genuinely slow cold start trips it honestly; the wording now names both possibilities instead of accusing the marker |

### ✅ FIXED 2026-08-19 `[PIPEBUSY-2026-08-18]` / `[CLASSTOTAL-2026-08-18]` — both moved to "Pending live-game verification"

Both honesty defects were fixed 2026-08-19 (PIPEBUSY: at-capacity logs once, not 1 Hz forever;
CLASSTOTAL: `total_classes` now the real pool count past the row cap). The live-check writeups —
including the ⚠ "never run a second `pipe_client.py` alongside the UI" caveat, which the PIPEBUSY fix
makes non-spammy but does not repeal — are under those tags in **"Pending live-game verification"**.

### ✅ PART-FIXED 2026-08-19 `[PROXYLOAD-2026-08-17]` — screening + a real load signal (writeup moved to "Pending live-game verification")

Both **offline** halves shipped 2026-08-19: (1) import-table BYPASS screening at deploy time and in
the Suggested column, and (2) a per-game "Loaded?" column read from the log folder — so a
`DeployedCurrent` proxy that never ran is no longer silent. **The live check** (OCTOPATH warns +
shows "not observed"; DQ7R/DQ I&II confirm "loaded") is under
`[PROXYLOAD-2026-08-17]` in **"Pending live-game verification"**.

### ✅ G8/G9 step 3 corroborated on a SECOND title `[DQ7R-PIPE-2026-08-17]`

DQ I&II HD-2D Remake, build 3262, proxy mode:
```
23:24:39.073 [WARN] DetectVersion: PE VERSIONINFO Product=1.0 File=1.0 — unrecognised
23:24:39.073 [WARN] DetectVersion: PE resource failed, falling back to memory string scan
23:24:39.187 [INFO] DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x42F5F30
23:24:39.187 [INFO] FindAll: UE Version = 427 (tier=1, detected=yes, lowConfidence=yes, publisher=SQUARE_ENIX)
```
`Product=1.0` is what the offline classifier predicted for this title — a second blind prediction
confirmed verbatim. `object_count` 104,867; patterns `GOBJ_ES53_1` / `GNAM_V8` / `GWLD_TQ_1`, i.e.
**identical to DQ7R's**, which is consistent with the two being the same engine-build family.

**Also from the same launch:** `case_preserving=false` with `probe_ran=true` and `validated=true` →
DQ I&II is a **fourth** confirmed non-CPN title (U2 sweep: TQ2 · Solarpunk · DQ7R · DQ I&II, still
zero CPN found), and another `validated`-clean host, so **G1/X3's amber-banner half still has no
host**.

### 🟡 GROUP 7 SWEEP DONE 2026-08-19 `[SWEEP9-2026-08-19]` — nine titles, headless, one at a time

Rig: `tools/verify/title_sweep.py` (+ `proxy_refresh.py`). Every title: refresh the stale proxy →
**clear the hint entry so `DetectVersion` actually runs** → launch the shipping exe directly → wait
for the pipe → `trigger_scan` → pipe round-trip → grep `scan-0.log` → **kill and confirm dead** →
next. dist **3263** confirmed by `assert_build()` on every one.

| title | UE (detected) | tier | detect path | GObjects | GWorld | objects | classes | CPN |
|---|---|---|---|---|---|---|---|---|
| Lushfoil | 506 | 1 | PE resource | `GOBJ_ES53_1` | `GWLD_TQ_1` | 58,619 | 1,770 | false |
| Manor Lords | 505 | 1 | PE resource | `GOBJ_ES53_1` | `GWLD_SP57_1` | 80,013 | 2,919 | false |
| Solarpunk | 507 | 1 | PE resource | `GOBJ_V13` | `GWLD_SP57_1` | 120,862 | 2,706 | false |
| EVERSPACE 2 | 505 | 1 | PE resource | `GOBJ_V13` | `GWLD_ES2_1` | 79,012 | 3,052 | false |
| The Artisan of Glimmith (Geri) | 427 | 1 | PE resource | `GOBJ_ES53_1` | `GWLD_TQ_1` | 24,132 | 799 | false |
| Avowed | 503 (→504 raised) | 1 | PE resource | **`GOBJ_AV1`** | **`instance_scan_recovery`** | 92,037 | **5,102** | false |
| DQ7R | 427 | 1 | **Tier 1 (utf16)** | `GOBJ_ES53_1` | `GWLD_TQ_1` | 149,408 | 2,543 | false |
| Elliot | 427 *(fallback)* | **0** | **publisher-bias fallback** | `GOBJ_ES53_1` | `GWLD_TQ_1` | 84,990 | 3,236 | false |
| OCTOPATH TRAVELER | 418 | – | (see `[RELAUNCHPIPE]`) | `GOBJ_ES53_1` | `GWLD_TQ_1` | 273,957 | 699 | false |

**`U2` CPN screening — swept 9, ALL FALSE.** `case_preserving=false` on every title, `probe_ran=true`
on every title. That is the honest form of the null result, and it covers UE4 (418, 427) as well as
UE5 (503–507), which the row asks for. ⇒ The row's escalation ("only if the sweep returns all-false,
build UE from source with `WITH_CASE_PRESERVING_NAME=1`") is now *reached*, and it is hours of work,
so it stays a maintainer decision.

**`G11` step 3 / `G8`–`G9` — the tier ladder was entered, and Tier 2 still did not fire.**
`DQ7R` is the one that reaches it: `PE VERSIONINFO Product=1.1 File=1.1 — unrecognised` → memory
scan → `DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x4BBC6D8`. **No
`Tier 2 Release prefix` line appeared on any of the nine.** That is the offline model's prediction
reproduced live — Tier 1 answering first and masking every Tier 2 hit — and it remains *not*
evidence that Tier 2 works.

**`G12` — the publisher-bias fallback branch is exercised, by Elliot.** Its resource is unusable
(`Product=1.2 — unrecognised`) *and* it carries no release tag, so it produces **no tier line at
all**, exactly as this register already predicted: `Could not detect UE version from PE or memory
(pre-UE4 markers 0/4, below the 2 needed)` → `UE detection failed — using publisher (SQUARE_ENIX)
bias fallback 427` → `UE Version = 427 (tier=0, detected=no, lowConfidence=yes)`.
⚠ **Elliot is really UE 5.04, so the bias picks the wrong number** — but it is honestly flagged
`detected=no, lowConfidence=yes`, and it is **harmless in practice**: the offset probe is empirical,
not version-driven, and reported `use_fproperty=true`, `item_size=24`, `validated=true`, with all
four pointers resolving. Worth knowing before anyone reads `427` on the UI's Elliot session as a bug.

**`X2` step 4 + `[CLASSTOTAL]` — the >5,000-class title exists and the wire reports it correctly.**
Avowed: `list_classes` → `total` (the page) **5000** = the default `limit`, `total_classes` **5102**,
`truncated` **true**. So the real pool total travels separately from the capped page, which is the
fix. ⚠ **Read `total_classes`, never `total`** — `total` is `results.size()` and equals the cap
exactly, which is precisely the misreading X2 is about. (This rig read `total` first and recorded
"5000 classes"; the number looked plausible and was wrong.)

**Avowed also re-confirms its documented shape**: `GOBJ_AV1`, **`item_size=20`** (the packed
`FUObjectItem`), and GWorld via **`instance_scan_recovery`** rather than a direct AOB.

⚠ **THREE DIFFERENT "UE VERSION" QUANTITIES, and confusing them manufactures a G11 false alarm.**
This cost two contradictory readings of Avowed before it was pinned down:
1. the **cached** `ueVersion` in `UE5CEDumper.<Machine>.json` — the *detected* value;
2. the **`FindAll: UE Version = N`** log line — also the detected value, and the right thing to
   compare a cache against;
3. **`get_pointers.ue_version`** — `g_cachedUEVersion`, which is the value **after any runtime
   raise**. Avowed detects 503 and then logs `property marker (CMC::GravityDirection) = UE5.4+ —
   raising version 503 -> 504`, exactly as DragonSword Awakening does, so 503 and 504 are *both
   right* for different questions.
⇒ **G11 step 1 must compare the cache against the LOG LINE.** On that basis: **6 of 8 IDENTICAL**
(Lushfoil 506, Manor Lords 505, Solarpunk 507, ES2 505, Geri 427, DQ7R 427 — and Solarpunk's is a
genuine cross-revision re-detect, its entry was still `rev=3`). The two that differ are Avowed
(cache 504 from an older run vs detected 503 + documented raise — **not** a regression) and Elliot
(504 → 427, the fallback change described above). No user override was destroyed by the clears —
every entry had `ueVersionUserOverrideAt` empty, checked before and after.

⚠ **Object counts drift by a few between runs** (Avowed 92,036 → 92,037) as the game loads; treat
small deltas as noise, not as findings.

⛔ **Two titles could NOT be swept**, recorded rather than silently skipped: **Star Trek Voyager**
exits immediately when its shipping exe is launched directly (Steam DRM wants the client), and
**EVERSPACE (RSG)** was not attempted. Both need a Steam-client launch.

### ⬜ NEW 2026-08-17 — G11: Tier 2 is alive; check it agrees with Tier 1

*Needs the DLL injected. See dev-log build 3112. **Measured 0/170 → 6/170 Tier 2 hits offline, with
Tier 1 agreeing on all six and masking all six** — so live behaviour should be UNCHANGED. This batch
exists to catch the case the offline model cannot see: the DLL scans the MAPPED image, the model
scanned on-disk bytes, and for packed/obfuscated titles those differ.*

1. **🟡 REGRESSION — no game's detected version moves.** `kVersionDetectLogicRev` went 4 → 5, so every
   cached game re-detects once. Note `ueVersion` / `versionDetected` / `lowConfidence` for two or
   three titles before running the new build and compare after. **Identical expected.** Any change
   is a real finding — report the game and the before/after.
   **✅ PASS on TWO titles — see the identical step under G8/G9 below for Elliot.** The DSA half,
   whose "before" is on disk rather than from memory:
   [test-games.md](test-games.md) records DragonSword Awakening as *"PE: 503 → runtime-raised to
   **504** by the `CMC::GravityDirection` property marker"*, and build 3122 produced exactly
   `DetectVersion: PE VERSIONINFO -> UE 5.3 -> 503` → `UE Version = 503 (tier=1, detected=yes,
   lowConfidence=no)` → `raising version 503 -> 504`. **Identical. The batch asks for two or three
   titles, so this stays 🟡 until a second one is checked.**

   **A third title was run, and it does NOT discharge the step `[DQ7R-PIPE-2026-08-17]`.** DQ7R
   detected **427**, matching the `ueVersion: 427` already cached for it — but its PE hash changed
   under a game patch (`69BA4044185AB000` → `69BB84C7069E9000`), so this was a first-ever scan of a
   *different binary* (`scanCount=1`), not the re-detect of a cached entry this step is about. What it
   does establish is weaker and still worth recording: the same title, across a publisher patch,
   detects the same version under `rev=5`. **Left 🟡.**
2. **A packed title is the interesting one.** Avowed is the documented packed case. Confirm its
   detected version is unchanged; this is the population where mapped-vs-on-disk could diverge.
3. **If a `Tier 2` line ever appears in `scan-0.log`, cross-check it.** Grep for
   `DetectVersion: Tier 2 Release prefix -> NNN`. On every corpus image Tier 1 answered first, so a
   Tier 2 line means a stripped-tag title reached the new path — record the game, the version, and
   whether it matches what the game actually is. That is the first real evidence Tier 2 works.
   **⬜ Not reachable from an ordinary session, and `[DSA-2026-08-16]` shows why:** a title whose PE
   VERSIONINFO is intact answers at `DetectVersion: PE VERSIONINFO -> UE 5.3 -> 503` and **never
   enters the tier ladder at all** — no `Tier 1 (ascii|utf16)` line either. Steps 3–4 here, step 3 of
   G8/G9, and every step of the G2 batch need a title with a **stripped version resource** (Elliot).
   **⚠ Correction: a stripped version resource is necessary but NOT sufficient, and Elliot is the
   wrong example** — it also lacks the release tag, so it produces no tier line at all. The ladder
   needs *unrecognised PE VERSIONINFO* **and** *a findable tag*; the three installed titles that have
   both are listed under G8/G9 step 3.
   **🟡 The ladder has now been entered, and Tier 2 still did not fire `[DQ7R-PIPE-2026-08-17]`.**
   DQ7R reached the memory scan and **Tier 1 (utf16) answered first** (`'++UE4+Release-4.27' -> 427`),
   so no `Tier 2 Release prefix` line was produced. That is the offline model's prediction reproduced
   live — Tier 1 agreeing on and masking every Tier 2 hit — and it is the first time the ladder is
   known to be *reachable* on this machine rather than merely modelled. **It is still not evidence
   that Tier 2 works**, and per step 4's warning this must not be read as closing G11.
4. **⚠ REGRESSION — Tier 3 still behaves.** A title that previously reported `Tier 3 (low
   confidence)` must still report the same version. The bare-needle change touches Tier 2 only, and
   two unit rails assert that, but Tier 3 is what stripped-tag games actually land on today.

### ⬜ NEW 2026-08-17 — G8 / G9: version detection after the tier-rule change

*Needs the DLL injected. See dev-log build 3105. **Expect NO visible difference** — both fixes are
measured no-ops on all 85 PE images in the local corpus, so this batch is a REGRESSION check, not a
demonstration. Anything that does change is a finding.*

1. **🟡 REGRESSION — every game still detects the same version.** `kVersionDetectLogicRev` went
   3 → 4, so the first launch after this build **re-detects every cached game once** (~0.35 s).
   For two or three titles, note `ueVersion` / `versionDetected` / `lowConfidence` in
   `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{Machine}.json` **before** running the new build, then
   compare after. **They must be identical.** A changed version is a real finding — report it with
   the game and the before/after values.
   **✅ PASS on TWO titles — the batch's own bar.** DSA `[DSA-2026-08-16]`: 503 → 504, matching
   test-games.md's build-2779 record. Elliot `[ELLIOT-2026-08-16]`: the cold `scan #1` produced
   `UE Version = 427 (tier=0, detected=no, lowConfidence=yes, publisher=SQUARE_ENIX)` and
   `UE5_Init: Complete (UE504…)` — **word for word** what test-games.md records for it ("PE version
   stripped → publisher fallback 427, upgraded via tagged FFieldVariant→503 + CMC::GravityDirection
   →504"). Two titles, two exact matches, under `rev=5`.
2. **✅ The re-detect happens once, not every launch.** Launch the same game twice more and confirm
   `scan-0.log` shows `skipped DetectVersion` on the later runs. If it re-detects every time, the
   rev stamp is not being written back.
   **PASS `[ELLIOT-2026-08-16]`** — three launches of Elliot in one evening: 20:12 ran the full
   `DetectVersion`, then **both** 20:26 and 20:49 logged
   `UE Version = 504 (cached, **rev=5**, detected=no, lowConf=yes) — skipped DetectVersion`. The rev
   stamp is written back and honoured.
3. **⚠ REGRESSION — a Tier 1 game is untouched.** G8/G9 only touch Tier 2/3, and Tier 1 returns
   first on nearly every real title. Confirm `DetectVersion: Tier 1 (ascii|utf16) …` still appears
   and still names the same version.
   **✅ PASS `[DQ7R-PIPE-2026-08-17]` — on DQ7R, over the pipe, build 3262.** `scan-0.log`:
   ```
   22:34:01.983 [WARN] DetectVersion: PE VERSIONINFO Product=1.1 File=1.1 — unrecognised
   22:34:01.983 [WARN] DetectVersion: PE resource failed, falling back to memory string scan
   22:34:02.299 [INFO] DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x4BBC6D8
   22:34:02.299 [INFO] FindAll: UE Version = 427 (tier=1, detected=yes, lowConfidence=yes, publisher=SQUARE_ENIX)
   ```
   The Tier 1 line appears, in the `utf16` flavour, and names **427** — matching what
   [test-games.md](test-games.md) and the pre-existing cache entry both carry for this title. That is
   the whole of what this step asks.

   **`lowConfidence=yes` alongside `tier=1` is NOT a finding — it is documented intent.** Checked in
   source before filing: [Genau.cpp](../dll/src/Genau.cpp) `if (publisher && out.UEVersion >=
   Grimoire::MIN_SUPPORTED_UE_VERSION) out.bLowConfidence = true;`, whose comment says a publisher
   thumbprint flags low confidence *"even when detection produced a clean Tier 1 / Tier 2 hit, since
   those strings can come from bundled SDKs"*. DQ7R matches `SQUARE_ENIX`.

   ⚠ **The earlier note here named the wrong host, and the correction is worth keeping.** It said
   Elliot was the title for this step. It is not: Elliot's PE resource fails *and* it carries no
   `++UE[45]+Release-` tag at all, so it falls past every tier to the publisher fallback (`tier=0`)
   and can never produce a Tier 1 line. The requirement is **two** properties, not one —
   *unrecognised PE VERSIONINFO* **and** *a findable release tag*. An offline sweep of all 16
   installed UE titles (`version.dll` APIs for the PE half, the `ue_version.py` regexes for the tag
   half) found exactly **three** that qualify, and none of them had ever been tried:

   | title | PE VERSIONINFO | tag | image |
   |---|---|---|---|
   | **DQ7R** | `Product=1.1` unrecognised | 4.27 | 99 MB |
   | **DQ I&II HD-2D Remake** | `Product=1.0` unrecognised | 4.27 | 87 MB |
   | **OCTOPATH TRAVELER** | `Product=1.0` unrecognised | 4.18 | 43 MB |

   The other twelve — TQ2, DSA, Solarpunk, STVoyager, Lushfoil, Manor Lords, ES2, EVERSPACE, SEED,
   Geri, Light Maze — all short-circuit at the PE fast path and cannot reach the ladder. The
   classifier reproduced the exact wording of two independently known log lines before being trusted
   (Elliot's `Product=1.2 File=1.2 — unrecognised` and DSA's `PE VERSIONINFO -> UE 5.3 -> 503`), then
   predicted DQ7R's `Product=1.1 File=1.1` and was confirmed verbatim by the run above.

   *Incidental, and it matters for anyone diffing this title:* **DQ7R's PE hash has changed** —
   `69BA4044185AB000` (2026-06-06) → `69BB84C7069E9000`. So this was a first-ever scan of a patched
   binary (`scanCount=1`), not a re-detect of a cached one. `ueVersion` is 427 on both, but the
   winning GWorld pattern moved `GWLD_GH_1` → `GWLD_TQ_1` (gNames `GNAM_V8` and gObjects
   `GOBJ_ES53_1` unchanged). The stale entry was left in place.

**Step 1 additionally re-confirmed `[G89-PIPE-2026-08-17]`, and more strongly than the step asks.**
Rather than comparing across a scan, the cached entry was **deleted outright** and a cold re-detect
reproduced every value: `ueVersion` 504, `versionDetected` true, `lowConfidence` false,
`versionDetectRev` 5 — all identical to the recorded before-state. `scanCount` reset 3 → 1, which is
expected when the entry is removed. The two other DumperTest entries were untouched and also compared
identical across the session.

*Incidental, not a finding:* the cold scan's winning GObjects pattern was `GOBJ_ES53_1`, where the
hinted run earlier the same session reported `GOBJ_GH_4` (hits=99). A hint short-circuits the sweep,
so cold and hinted runs legitimately crown different patterns; both resolved in-module and both
worked. Worth knowing before anyone diffs pattern ids across a hint boundary and calls it a
regression.
4. **G11 context — do not misread a pass here.** Tier 2 has never fired on any binary we own (the
   trailing-dot defect), so a green result on steps 1–3 says these fixes did no harm; it says
   **nothing** about Tier 2 working. Do not close G11 on the strength of this batch.

### ⬜ NEW 2026-08-17 — G10 / MA1: the hint cache must stop destroying itself

*Needs the DLL injected. See dev-log builds 3091 / 3095. **Step 1's control already exists on disk**
and is decisive — this is the rare case where the regression was captured before the fix.*

1. **⚠ G10 — THE DECISIVE ONE, and it is a two-launch test.** Pick a title where a pattern has many
   matches (DumperTest is the documented case, PE `6A7EA60310F17000`). Delete that PE hash's
   `gNames` entry from `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{Machine}.json`, launch and scan
   (run #1 writes the hint), then launch and scan **again**.
   **PASS** = run #2 shows `Hint HIT: 'GNAM_V1'`, or at worst a `Hint MISS` followed by a real
   winner. **FAIL (the shipped bug)** = `=== GNames: … NONE validated ===`, which is what
   `Logs/DumperTest/scan-0.log` recorded at 13:34 on 2026-08-14 while `scan-20260814-132936.log`
   found `winner: GNAM_V1` five minutes earlier on the same binary.
   **✅ PASS, decisively `[ELLIOT-2026-08-16]`.** Not DumperTest but a better subject: on Elliot
   (`PE=6A577F4E1D91B000`) the cold run (`scan #1`, 20:12) logged
   `[GObjects] GOBJ_ES53_1 hits=74 [WINNER]` — **74 matches**, i.e. 73 wrong candidates for a
   "stops at the first match" fast path to fall into. Both warm runs (`scan #4` 20:26 and `scan #7`
   20:49) answered `Hint HIT: 'GOBJ_ES53_1'`, plus `Hint HIT` on `GNAM_V8` and `GWLD_TQ_1`, and
   **`NONE validated` appears nowhere in any of the three logs.** That is the shipped bug's exact
   shape, exercised and clean.
> ### ⬜ STEP 2 ATTEMPTED ON DumperTest 2026-08-19 `[G10S2-2026-08-19]` — STILL NOT DECIDED, and here is why
>
> The step needs a `Hint MISS` whose true match count is **large**, so a correct implementation and
> the broken "always 1" one print different lines. On DumperTest Development the cold table gives
> `[GObjects] GOBJ_ES53_1 hits=619 [WINNER]` and `[GNames] GNAM_V7 hits=1 (not validated)` — so
> GNames is confounded here exactly as it is on Elliot.
>
> The attempt: stage `gWorld.patternId = "GWLD_V3"` (the loose pattern — **5,140 matches** on this
> binary) hoping for a big-count MISS. **It HIT instead**: `Hint HIT: 'GWLD_V3' -> 0x7FF648148D40
> (skipping remaining patterns)`. So a many-match pattern that also *fails validation* is still not
> in hand, and the confound survives. ⇒ **A pattern with hits ≫ 1 that does NOT validate is the
> missing ingredient**; neither Elliot nor DumperTest has one today.
>
> ⭐ **Two things the attempt DID establish, both worth keeping:**
> 1. **The GWorld decoy recovery works, and caught this.** The poisoned hint pinned GWorld to
>    `0x7FF648148D40`, and `FindAll: Complete` published exactly that — then
>    `ExtraScanGWorld: Starting instance scan… GWorld at 0x7FF6483188A0 -> UWorld 0x215516240C0
>    (index=24970, 1 candidate(s), active)` **corrected it to the cold-scan address**, and
>    `walk_world` then returned 58 entries. The two slots hold genuinely different `UWorld*`
>    values, so this was a real decoy, not two names for one thing.
> 2. ⚠ **But the cache then saved the AOB winner, not the corrected result** — the entry was written
>    back as `gWorld.patternId = "GWLD_V3"`. A wrong pattern therefore **re-hints itself forever**,
>    paying the instance scan on every launch. Low real-world risk (priority ordering means
>    `GWLD_TQ_1` is reached first on a cold scan — only 2 GWorld patterns were tried), but it is a
>    genuine "the report and the reality are computed by different paths" shape. The poisoned entry
>    was **deleted** afterwards so the next DumperTest scan is cold and re-derives `GWLD_TQ_1`.
>
2. 🟡 **G10 — the count no longer lies. NOT DECIDABLE ON ELLIOT — use DumperTest.** In `scan-0.log`,
   a `Hint MISS` line must report the real match count (`(%zu matches, none validated; …)`) and never
   say `1 match` for a pattern the cold run logged with hundreds.
   **Why Elliot cannot answer it (measured 2026-08-18).** A `Hint MISS` *is* stageable here — writing
   `gNames.patternId = "GNAM_V7"` into the cache would produce one, because every cold run logs
   `[GNames] GNAM_V7 hits=1 (not validated)`. But its **true count is 1**, so a correct
   implementation and the broken "always 1" one print the *same line*. That is a confounded probe
   (working-lessons §1.10a) and would return PASS either way.
   The full per-pattern table from Elliot's cold scan, which is what rules it out:

   | target | pattern | hits |
   |---|---|---|
   | GObjects | `GOBJ_ES53_1` | **74** — but it **validates**, so hinting it gives a `Hint HIT`, not a MISS |
   | GNames | `GNAM_V7` | 1, *not validated* — a MISS, but a useless one |
   | GNames | `GNAM_V8` | 1 → WINNER |
   | GWorld / SparseDelegates / GEngine | `GWLD_TQ_1` / `SPARSE_ES2_1` / `GENG_X1` | 1 each → WINNER |

   **What the subject must have: a pattern with MANY matches, none of which validate.** Elliot has no
   such pattern — its only many-match pattern is the winner. Use the case this row already names,
   **DumperTest (PE `6A7EA60310F17000`)**, and hint `gNames` to a high-count non-validating pattern
   there.
3. **✅ REGRESSION — a warm launch is still FAST.** The hint path now scans all matches instead of
   stopping at the first, so a genuine `Hint HIT` costs slightly more. Confirm run #2 is still far
   faster than a cold scan (`[X] AOB scan total: %lld us`), not merely correct.
   **PASS `[ELLIOT-2026-08-16]`, and the run carries its own negative control** — same binary, same
   machine, cold `scan #1` (20:12) vs warm `scan #7` (20:49):

   | target | cold | warm | ratio |
   |---|---|---|---|
   | GObjects *(hinted)* | 1,199,277 µs | 275,868 µs | **4.3×** |
   | GNames *(hinted)* | 1,125,987 µs | 287,709 µs | **3.9×** |
   | GWorld *(hinted)* | 1,239,921 µs | 264,492 µs | **4.7×** |
   | **SparseDelegates *(NOT hinted)*** | 1,449,253 µs | 1,462,536 µs | **1.00×** |
   | **GEngine *(NOT hinted)*** | 1,086,361 µs | 1,111,045 µs | **1.02×** |

   The two unhinted targets are the control that makes this a measurement rather than a number: they
   sit in the *same* process, on the *same* warm page cache, and did **not** speed up. So the 4× is
   the hint path and not disk caching or machine warm-up. Conditions: `Elliot-Win64-Shipping.exe`,
   **482,390,784 bytes**, build 3122.
4. ✅ **DONE 2026-08-18 `[ELLIOT-MA1-2026-08-18]` — the cancel fires, and every guard with it.**
   Elliot through its `dxgi` proxy, hint entry dropped (`tools/verify/cold_detect.py drop
   6A577F4E1D91B000`) so the scan is cold: **8.0 s**, against **3.3 s** warm.

   ```
   13:34:43.834 UE5_Init: Starting initialization...
   13:34:46.459 UE5_Shutdown: Cleaning up...                       <- fired 2.5 s into the scan
   13:34:47.310 [GNames]          AOB scan CANCELLED after 0/4 batches (client gone / shutdown)
   13:34:47.311 [GWorld]          AOB scan CANCELLED after 0/7 batches
   13:34:47.311 [SparseDelegates] AOB scan CANCELLED after 0/2 batches
   13:34:47.311 FindSparseDelegateStorage: CANCELLED — not latching, the next scan will retry
   13:34:47.311 FindAll: scan was CANCELLED — NOT writing the hint cache
   ```
   Both required lines are present and land **851 ms** after the shutdown — inside the ~1 s bar.

   ### ⚠ The staging is the hard part — four routes were measured and DO NOT WORK
   | route | what happened | cause |
   |---|---|---|
   | Untick the CE record ~2 s in (**how this step is written**) | click took effect **8.5 s later**, after the scan ended | the `.CT` `init` script blocks **CE's GUI thread** for the whole scan. **Not performable as written.** |
   | Kill the UI mid-scan (landed 2.8 s into an 8.0 s scan) | scan **completed normally** | `trigger_scan` is **async**; no in-flight command for a disconnect to cancel. Same cause as B4's third trap. |
   | CE Lua `createThread` + fixed `sleep` | fired **before** the scan started | a GUI round trip is 2–6 s, and **each operator action costs ~10 s of wall clock**, so two consecutive actions cannot hit an 8 s window. ⚠ A leftover thread later shut down a *fresh* Elliot — CE keeps one Lua state, so **restart CE between attempts**. |
   | CE Lua thread polling `init-0.log` | printed `NEVER SAW SCAN START` | **CE Lua's `io.open` cannot read our live log** (writer share mode — [working-lessons.md](working-lessons.md) §3). Python's reader can. |

   ### ▶ What DOES work — a two-process chain, both halves pre-armed
   1. `py tools/verify/kill_on_marker.py <init-0.log> "Starting initialization" --touch <flagfile>
      --after-ms 2500` — Python watches the log (it *can* read it) and drops an ordinary flag file.
   2. In CE, **pre-armed before the scan**:
      `createThread(function() ... poll for <flagfile> ... executeCodeEx(0,60000,getAddress('dxgi.UE5_Shutdown')) end)`
      — CE Lua *can* read a file nobody holds open.

   Neither half is on the operator's critical path, so the 8 s window is hit every time. Give the CE
   poll loop a **generous** timeout: a mis-registered Start Scan click cost 3 minutes and the first
   loop (120 s) had already expired when the flag finally appeared.

5. ✅ **DONE 2026-08-18 — MA1's three guards, each checked separately.**
   * **(a) the hint cache is untouched.** After the cancelled run, Elliot's entry
     `6A577F4E1D91B000` was **still absent** from `UE5CEDumper.MSI-NB.json` (28 games, none of them
     Elliot). The cancelled scan wrote nothing, exactly as `FindAll` promised.
   * **(b) a re-enable re-scans rather than short-circuiting.** `UE5_AutoStart` in the **same
     process** ran a full scan to `UE5_Init: Complete (UE504, …, Objects=85068)`.
     ⚠ **The obvious test is wrong and looks like a defect.** Calling **`UE5_Init` directly** instead
     re-scans and is **cancelled at `0/7` batches before doing any work**, every time — which reads
     exactly like a stale cancel flag. It is not: [`Tot.h`](../dll/src/Tot.h) states `g_shutdown` is
     *"cleared only by `Fern::Start()`"*, and [`Frieren.cpp:798-812`](../dll/src/Frieren.cpp:798)
     puts `Tot::ResetShutdown()` at the top of **`UE5_AutoStart`** precisely so a re-enable does not
     "rescan with `g_shutdown` still latched". **`UE5_AutoStart` is the re-enable entry point;
     `UE5_Init` alone is not.** Filed nothing — the header had already answered it
     (working-lessons §2.4).
   * **(c) the sparse latch does not stick.** `FindSparseDelegateStorage: Scanning` appears **3×** in
     `scan-0.log`: the cancelled run, the second cancelled attempt, and the healthy re-scan — so
     `CANCELLED — not latching, the next scan will retry` is literally true.

6. ✅ **DONE 2026-08-18 — REGRESSION: a healthy scan still completes and still saves.** The
   `UE5_AutoStart` re-scan above wrote the entry back with real AOB hints —
   `gObjects: GOBJ_ES53_1 (1 of 2 tried)`, `gNames: GNAM_V8 (2 of 5)`, `scanCount: 1` — and its run
   logged **no** `CANCELLED` line. So `bScanCancelled` is not over-broad.

7. **⚠ SUPERSEDED — original step 5 text, kept for the method** (a control that passes is how a bug in a fix gets
   found): after that cancelled run, (a) **diff `UE5CEDumper.{Machine}.json`** — it must be
   *unchanged* for that PE hash; (b) re-enable in the **same** process and confirm a full re-scan
   runs rather than short-circuiting (the `UE5_Init` latch guard); (c) drill into a
   `MulticastSparseDelegateProperty` and confirm `FindSparseDelegateStorage: Scanning` appears a
   **second** time rather than a latched 0 (the sparse latch guard).
8. **⚠ SUPERSEDED — original step 6 text, kept for the method.** Connect the UI, disconnect it
   mid-command, reconnect, and confirm a fresh scan resolves normally, writes the hint cache, and
   shows **no** `CANCELLED` line. This is what keeps `bScanCancelled` from being widened to
   `Tot::Requested()`, which would refuse the latch on a scan that finished fine.

**Not covered:** `Macht` still carries no poll (deliberate — see the comment above its AOB
declarations), and **MA2**, the `ScanRegionBatch` per-pattern underflow, is unreachable until
`AOBScanBatch` is given a `moduleBase`.

### 🟡 STEPS 4+5 CLOSED 2026-08-18 — G2: the version sweep is ~29 s faster, and must still be RIGHT

*Needs the DLL injected. See dev-log builds 3086 / 3088. The 29 new C++ assertions pin the rewrite
against a naive oracle; what they cannot pin is that it still reads a REAL image correctly, because
no test target compiles `Genau.cpp`.*

1. **✅ THE ONLY CONTROL THAT MATTERS — same answer, not just a faster one.** On a title whose PE
   version resource is stripped (Elliot is the documented one; a game that detects from Tier 1 exits
   early and measures nothing), **first delete that game's record from
   `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{Machine}.json`** — otherwise the run takes the
   `"skipped DetectVersion"` branch. Note `ueVersion` / `versionDetected` / `lowConfidence` before
   deleting, then re-scan and confirm the values written back are **identical**. A fast-and-wrong
   detection passes step 2 and fails only this one.
   **PASS `[ELLIOT-2026-08-16]`** — the 20:12 run was `scan #1`, i.e. genuinely cold, and the
   rewritten sweep ran end to end: `PE VERSIONINFO Product=1.2 File=1.2 — unrecognised` →
   `PE resource failed, falling back to memory string scan` → `Could not detect UE version from PE
   or memory (pre-UE4 markers 0/4, below the 2 needed)` → `UE Version = 427 (tier=0, detected=no,
   lowConfidence=yes, publisher=SQUARE_ENIX)`, reconciled by `UE5_Init` to **UE504**. Identical to
   test-games.md's record. **This is also the first real evidence G2's rewritten sweep executes
   correctly on a live image** — it is the branch DSA never entered.
2. **🟡 The speed, with its conditions — MEASURED, and it does NOT meet this batch's own prediction.**
   In `Logs\<proc>\scan-0.log`, measure the timestamp delta from
   `"DetectVersion: PE resource failed, falling back to memory string scan"` to the next `SCAN:Ver`
   line. Expect sub-second where it was tens of seconds. **Record the game and its image size** — a
   duration without those is not a measurement.
   **`[ELLIOT-2026-08-16]`: 20:12:37.431 → 20:12:39.831 = 2.400 s.** Conditions:
   `Elliot-Win64-Shipping.exe`, **482,390,784 bytes (460 MB)**, build 3122, warm page cache (third
   launch of the evening). So the fix unquestionably took — this was *tens of seconds* — but 2.4 s is
   **~7× the 0.35 s the dev-log claims for G2**, and the batch predicted "sub-second".
   **LEAD, not yet a finding:** that interval does not contain only the version needle. The terminal
   line reports `pre-UE4 markers 0/4`, so `CountPreUE4Markers` — a *separate* whole-image sweep added
   by the pre-UE4 refusal work — is inside the same window and may not have been gated the way the
   version needle was. Before filing anything, split the measurement: add or find a `SCAN:Ver` line
   between the two sweeps, or re-measure on a title where the pre-UE4 check exits early. Do **not**
   record "G2 is slower than claimed" until the two are separated — the 0.35 s figure may simply have
   been measured on a much smaller image, which would make this no defect at all.

   **✅ LEAD RESOLVED `[DQ7R-PIPE-2026-08-17]` — NO DEFECT, and it took no instrumentation.** The
   lead's own second option was taken: re-measure on a title where the pre-UE4 check exits early.
   `CountPreUE4Markers` is reached **only** in `DetectVersionDetailed`'s terminal all-failed branch,
   so any title that produces a tier hit keeps that second sweep out of the window by construction.
   DQ7R is such a title (see G8/G9 step 3 for how it was found):

   | | Elliot | DQ7R |
   |---|---|---|
   | window `PE resource failed` → next `SCAN:Ver` | **2.400 s** | **0.316 s** |
   | contains `CountPreUE4Markers`? | **yes** (`markers 0/4`, no tier hit) | **no** (Tier 1 hit) |
   | image | 482,390,784 B (460 MiB) | 103,878,656 B (99 MiB) |
   | bytes the needle actually covered | full image, ×2 flavours | ascii full + utf16 to the hit at `0x4BBC6D8` = 79,415,000 B |
   | build | 3122 | 3262 |

   ⛔ **A first pass here concluded "the lead is REFUTED". That was over-claimed and is WITHDRAWN.**
   It rested on extrapolating DQ7R's per-byte rate onto Elliot's image, which assumed the rate is
   stable. **A second Tier-1 title was then measured and it is not:**

   | title | bytes the needle covered | window | implied rate |
   |---|---|---|---|
   | DQ7R | 79,415,000 (hit at `0x4BBC6D8`) = 75.7 MiB | 0.316 s | **240 MiB/s** |
   | DQ I&II HD-2D | 70,213,424 (hit at `0x42F5F30`) = 67.0 MiB | **0.114 s** | **587 MiB/s** |

   **2.4× apart on two images of the same order.** Extrapolated onto Elliot's 460 MiB that spans
   0.78 s – 1.9 s of the 2.400 s window, so the needle is somewhere between **33% and 80%** of it.
   The fast end makes `CountPreUE4Markers` the *dominant* term — i.e. it **supports** the lead the
   first pass claimed to refute. **Two points, two opposite conclusions: the extrapolation cannot
   decide this and must not be used to.**

   **What IS established, and it is worth having:**
   * The needle-only window is directly measurable, and it is **small** — 0.114 s and 0.316 s over
     ~67–76 MiB — on any title where a tier hit keeps the marker sweep out of the window by
     construction. That part of G2's rewrite demonstrably works on live images.
   * The dev-log's 0.35 s sits inside that measured range for ~100 MB-class images, so the figure is
     **image-size-specific rather than wrong**.
   * ⚠ **Per-byte scan rate on this machine varies by 2.4× run to run**, which is itself the finding:
     it makes *any* cross-title extrapolation of scan cost unsound, here and in future batches.

   **So step 2 stays 🟡 and the only decisive route is the lead's FIRST option: instrument.** Add one
   `SCAN:Ver` line between the version needle and `CountPreUE4Markers` and re-measure **Elliot
   itself** — nothing measured on a smaller title can settle what fraction of Elliot's 2.4 s belongs
   to which sweep. Do **not** file "G2 is slower than claimed" either; both directions are currently
   unsupported.

   ⚠ **Conditions:** single run per title, warm page cache, mixed builds (Elliot 3122 vs DQ7R/DQ I&II
   3262), and the Elliot row is quoted from `[ELLIOT-2026-08-16]` rather than re-measured. OCTOPATH
   would have been the third point but **cannot be measured at all** — its `version.dll` proxy never
   loads; see the silent-proxy finding below.
3. **⚠ REGRESSION — a Tier 1 game still detects from Tier 1.** ⛔ **"Any ordinary UE5 title" is
   WRONG and is what made this step look runnable** — an ordinary UE5 title resolves at Tier **0**
   and never reaches Tier 1. Screen candidates with `tools/verify/pe_version_probe.py` first; see
   `[G2-TIER0-SWEEP-2026-08-18]`. Confirm
   `scan-0.log` still shows `DetectVersion: Tier 1 (ascii|utf16) '++UEx+Release-N.N' -> NNN`. The log
   lines were kept byte-identical on purpose, so any wording change here is itself a defect.
4. **The three new cancel points actually fire.** Proxy mode: start a scan from the UI, close the UI
   mid-scan, and confirm `scan-0.log` carries one of the new `aborted (client gone / shutdown)` lines
   (`DataScanGObjectsCandidates` / `FindGObjectsStaticStruct` / `FindGNamesByStringRef`) rather than
   the sweep running to completion. These are **compiled but unexercised** — do not read a pass on
   steps 1–3 as covering them.
5. **⚠ REGRESSION — recovery still runs on a healthy game.** The polls honour the client-disconnect
   latch, so a stale one would abort recovery at offset 0. Connect the UI, disconnect it mid-command,
   reconnect, and confirm a fresh scan still resolves GObjects/GNames normally and that **no**
   `aborted` line appears. This is exactly what `Tot::ResetPerCommand()` in `AutoStartWork` is for.

> ### ✅ STEPS 4 + 5 CLOSED, STEP 3 PARTIAL `[G2-ELLIOT-2026-08-18]` — and step 4's PRESCRIBED VEHICLE IS REFUTED
>
> Elliot, dxgi proxy, **DLL build 1.0.0.3262**, proxy mode confirmed by
> `DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)`.
>
> **Step 3 — 🟡 PARTIAL.** The wording is confirmed byte-exact against the source: the format string
> at `Genau.cpp:3047` is `"DetectVersion: Tier 1 (%s) '%s%s' -> %u at 0x%zX"`, and build-3262 logs
> carry `DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x4BBC6D8` (DQ7R) and
> `... at 0x42F5F30` (DQ I&II). ⚠ **But a machine-wide sweep of every log found exactly TWO Tier-1
> lines, both `utf16` and both UE4** — the `ascii` flavour and the **UE5** branch of `'%s%s'` are
> still unwitnessed at 3262. The obvious host, **Lushfoil, cannot supply one without a cache drop**:
> it logs `UE Version = 506 (cached, rev=5, detected=yes, lowConf=no) — skipped DetectVersion`.
> `tools/verify/cold_detect.py drop 998ED2850957D000 --apply` is the one-line unblock (it was
> refused by this session's command classifier, not by anything in the repo).
> **The cache drop was then DONE, and it settles what step 3 actually needs — it is not Lushfoil.**
> `cold_detect.py drop 998ED2850957D000 --apply` worked, the cold sweep ran
> (`DetectVersion: Attempting to detect UE version...`), and Lushfoil resolved at the FIRST stage:
> `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506` -> `UE Version = 506 (tier=1, detected=yes,
> lowConfidence=no)`. Its PE version resource is **intact**, so it exits before the memory-string
> needle and **structurally cannot** emit `DetectVersion: Tier 1 (…) '++UE5+Release-5.6'`.
> ⇒ **Step 3's UE5 branch needs a UE5 title whose PE version resource is stripped or unrecognised**
> (Elliot is the documented stripped title but is UE4-era). Candidates to try, all UE5 and already in
> the cache: TQ2 (507), Solarpunk (507), Manor Lords (505), ES2 (505), STVoyager (506).
> *Incidentally this re-ran G2 step 1's control on a second title and it passed:* the record was
> rewritten **identically** (`ueVersion=506 versionDetected=True versionDetectRev=5`).
> ⚠ The maintainer has confirmed the whole cache file is **disposable** — it only speeds up a second
> load — so a cold-detect row may drop a record, or the file, without ceremony.
>
> ### ⛔ STEP 3 IS AS CLOSED AS IT CAN GET — the UE5 branch has NO HOST, and all five candidates are REFUTED
> `[G2-TIER0-SWEEP-2026-08-18]`
>
> Asked to run step 3 on **Solarpunk**. It cannot: its `VS_FIXEDFILEINFO.dwProductVersionMS` is
> **5.7.1.0**, so `Genau::DetectVersionFromPEResource`'s very first test (`major==5 && minor<=9`)
> returns **507** and the ladder exits at `PE VERSIONINFO` — the same mechanism that ruled out
> Lushfoil. ⚠ Its *string* `ProductVersion` is the placeholder `"UE5-CL-0"`, which **looks**
> unrecognisable and is why this title read as a candidate; the strings are only consulted at stage 3,
> long after the fixed-info block has already decided.
>
> **So the question was settled offline for every binary on this machine, not one title at a time.**
> ⚠ **CORRECTED — the first pass of this sweep was WRONG and the error is the interesting part.**
> It globbed a fixed depth (`common/*/*/Binaries/Win64/*-Win64-Shipping.exe`) and so silently
> skipped **Avowed**, **Echoes of Aincrad Demo** (nested one level deeper / installed mid-session)
> and **SEED BATTLE DESTINY REMASTERED** (its exe is not named `*-Win64-Shipping.exe` at all).
> **An absence claim built on a glob that can silently skip files is worthless** — the corrected
> tool, `tools/verify/tier1_host_survey.py`, walks every `Binaries\Win64` directory instead.
>
> It also reports **two independent facts**, because either alone misleads: whether the title falls
> THROUGH Tier 0, **and** whether the `++UEn+Release-N.N` needle actually exists in the image, in
> which encoding. Validated on a known positive first — DQ7R yields `utf16 ++UE4+Release-4.27`,
> exactly the line its log carries — so a "no needle" result is a real negative, not a dead detector.
>
> **18 installed binaries; only THREE can produce a Tier-1 line, and all three are UE4:**
>
> | title | ProductVersion | needle | note |
> |---|---|---|---|
> | DQ7R | 1.1.1.0 | `utf16` 4.27 | the already-witnessed line |
> | DQ I&II HD-2D | 1.0.2.0 | `utf16` 4.27 | same flavour |
> | **OCTOPATH TRAVELER** | 1.0.0.1 | **`ascii`** + utf16 4.18 | ⭐ **the only `ascii` host on this machine** |
>
> **Falls through but has NO needle → detects nothing:** Elliot (1.2.0.0) and Echoes of Aincrad Demo
> (1.0.1.27081). ⚠ Both are **UE 5.4**, not UE4 — an earlier revision of this block said "all UE4-era"
> and that was wrong. They are exactly the shape step 3 wants (UE5 + falls through Tier 0) and still
> cannot serve it, because the needle is absent from the image.
>
> **Every UE5 title either exits at Tier 0 or has no needle**, and the split is instructive:
> Light Maze (5.0), Lushfoil (5.6) and Manor Lords (5.5) **do** carry `++UE5+Release-` but resolve at
> Tier 0 and never look; Solarpunk, TQ2, ES2, STVoyager, Satisfactory, DSA and Avowed carry **no
> needle at all**. So the required host — falls through Tier 0 **and** carries a UE5 needle — does
> not exist here, and newer UE5 shipping builds appear to strip the string entirely.
> Our own packaged reference builds do not help either: UE writes the ENGINE version by default, so
> 5.3/5.4/5.7/5.8 and DumperTest all resolve at Tier 0. It is games that *override* it with a product
> version (1.x) that fall through — the opposite of the intuition that a stock-built sample would be
> generic.
>
> ⇒ **The UE5 branch is unverifiable on this inventory** and no supported switch skips Tier 0
> (`cold_detect.py drop` only clears the cache; it re-runs the same ladder).
> ⇒ **The `ascii` branch IS reachable — via OCTOPATH**, which was previously written off as
> "proxy never loads". The maintainer supplied the missing piece (2026-08-18): it needs the
> **`winmm.dll`** proxy; `version.dll` and `dxgi.dll` do not load in it. That is a concrete,
> runnable next step rather than a blocked one.
> Corroborated from the other side: a fresh sweep of every log still finds exactly **two** Tier-1
> lines, both `utf16`, both `++UE4+Release-4.27`.
>
> **The rest of step 3 stands as already recorded:** the wording is byte-exact against
> `Genau.cpp:3047`, and the regression it guards is witnessed on the UE4/`utf16` path.

> ### ✅ THE `ascii` BRANCH IS NOW WITNESSED `[OCTOPATH-G2T3-2026-08-18]` — and OCTOPATH is a working host again
>
> **OCTOPATH TRAVELER**, UE **4.18**, DLL **1.0.0.3262**, **`winmm.dll` proxy**. The offline survey
> predicted `ascii` for this title; the DLL then reported `ascii`. Prediction first, confirmation by
> an independent method second:
>
> ```
> DetectVersion: PE VERSIONINFO Product=1.0 File=1.0 — unrecognised
> DetectVersion: PE resource failed, falling back to memory string scan
> DetectVersion: Tier 1 (ascii) '++UE4+Release-4.18' -> 418 at 0x1C06AB0
> FindAll: UE Version = 418 (tier=1, detected=yes, lowConfidence=yes, publisher=SQUARE_ENIX)
> ```
>
> That is the **`ascii` flavour of `'%s%s'`**, unwitnessed since the format string shipped, and it
> also re-confirms step 3's regression (a Tier-1 game still detects from Tier 1) on a second engine
> generation. ⇒ **Of step 3's four combinations, three are now closed** (`utf16`+UE4, `ascii`+UE4,
> and the Tier-0 exit); only the **UE5** branch remains, still hostless for the reasons above.
>
> ### ⭐ THE PROXY FOR OCTOPATH IS `winmm.dll` — `version.dll` and `dxgi.dll` do NOT work
> Supplied by the maintainer and verified end-to-end. This retires a blocker recorded in three
> places (§"could not be swept at all", the G2 rate-sweep drop-out, and the deferred dxgi item):
> * `winmm proxy: lazily forwarded 180/180 exports to real System32 winmm.dll` → `pipe server started`
> * full scan clean: GObjects/GNames/GWorld/**GEngine** all `aob`, **273,956 objects**, and
>   **0 `[ERROR]` across all five log files**.
> ⚠ **The measured fact and the mechanism are separate, and only the first is established.** The exe
> **statically imports BOTH `VERSION.dll` and `WINMM.dll`** (verified by parsing its import table),
> yet the game-folder `winmm.dll` loads while the loaded `VERSION.dll` is **System32's**. A KnownDLLs
> explanation was tested and **refuted** — `version.dll` is not in the KnownDLLs list and no KnownDLL
> imports it. **So "why version.dll loses" is NOT established; do not publish a cause for it.**
> What is safe to rely on: for this title, use `winmm`.
> ⚠ The stale `version.dll` proxy was removed from the game folder (to the Recycle Bin, recoverable).
>
> ### ➕ A THIRD DATA POINT FOR STEP 2, free from the same run
> `20:04:35.123` (fallback begins) → `20:04:35.154` (Tier-1 hit at `0x1C06AB0`) = **31 ms** to reach
> ~28.0 MiB, i.e. **~900 MiB/s**. Against the two existing points — DQ7R 240 MiB/s, DQ I&II 587 MiB/s
> — the spread widens from 2.4× to **3.8×**. That **strengthens** step 2's existing conclusion rather
> than settling it: per-byte scan rate on this machine varies far too much for any cross-title
> extrapolation to decide what fraction of Elliot's 2.4 s belongs to which sweep. Same conditions
> caveat as before (single run, warm cache, early-exit path).
>
> **Step 4 — ✅ THE LINES FIRE, but NOT by the route this row prescribes.** Witnessed on 3262, in
> `Logs/Elliot-Win64-Shipping/scan-20260818-13*.log`: **`DataScanGObjectsCandidates: aborted (client
> gone / shutdown)`** (×1) and **`FindGNamesByStringRef: aborted (client gone / shutdown)`** (×3).
> `FindGObjectsStaticStruct` remains unwitnessed, so this is 2 of 3.
>
> ⛔ **"Close the UI mid-scan" cannot produce them, and this is structural, not luck:**
> * `Tot::RequestPerCommand()` has exactly one caller — `Fern::MonitorLoop`, which peeks **only**
>   connections whose `inFlight` flag is set (`Fern.cpp:804-865`, and the `inFlight` mark at `:1087`).
> * `trigger_scan` **returns immediately** and does the work on a detached `std::thread`
>   (`Fern.cpp:4983-5008` -> `RunScan` -> `UE5_Init`). No command is in flight while the scan runs, so
>   a client vanishing during it is never even peeked.
> * `rescan` is async too (`Fern.cpp:4840`) **and cannot reach these functions at all** —
>   `RunRescanBody` calls `Genau::ExtraScanGObjects/GWorld`, which are different functions.
> * `Genau::FindAll` has ONE caller in the whole tree (`Frieren.cpp:155`, `UE5_Init`).
>
> ▶ **What actually fired them is the SHUTDOWN half of the same flag**, and the logs say so directly:
> `13:34:46.459 UE5_Shutdown: Cleaning up...` -> `PipeServer: Stop entry (conns=2)` ->
> `13:34:47.313 UE5_Init: scan was cancelled (shutdown) — results are partial, NOT latching
> initialized so the next enable re-scans`. The cancel was **already latched when the scan began**,
> which the log shows unambiguously as `AOB scan CANCELLED after 0/7 batches` (GObjects) and
> `0/4 batches` (GNames) — every poll bailed on its first check, in the same millisecond.
> Incidentally this is Tot.h's stated purpose #1 working: `Stop watches+scan joins done (852 ms)`.
> **Rewrite the step to say "shut the DLL down while a scan is in flight" (disable the CE script /
> close the game), not "close the UI".**
>
> **Step 5 — ✅ PASS.** Staged with a client that kills itself mid-command, because the window cannot
> be hit by hand: a *second* process cannot be aimed (a tool round trip is seconds) and the obvious
> long commands are not long DLL-side — `list_all_functions` is **634 ms**, `search_properties`
> query="e" over 355,949 objects is **307 ms** server-side (its 2-minute wall clock was the Python
> client formatting 14,902 results), and `begin_value_scan` finished inside 0.5 s. Arming the kill
> **inside** the client on a 200 ms timer after the write produced it first try:
> `15:08:14.037 Received: begin_value_scan` -> `15:08:14.434 PipeServer: client gone mid-command
> (err=109) — aborting in-flight op` (109 = `ERROR_BROKEN_PIPE`) -> `Failed to write response`.
> ⚠ **The latch then cleared ITSELF 30 ms later** — `per-command cancel cleared — no connection that
> raised it is still live` — i.e. `ReevaluatePerCommandCancel` (audit #5 F2) retires it when the
> raising connection is removed, **without needing the reconnect this step assumes**. The UI stayed
> connected throughout and was unaffected. It was then disconnected and reconnected anyway
> (`Connected — UE504 (355949 objects)`) and a fresh scan run: **`grep -c aborted scan-0.log` = 0**,
> GObjects/GNames/GWorld all resolved (`GOBJ_ES53_1 -> 0x149BFC140`, `GNAM_V8 -> 0x149B18600`,
> `GWLD_TQ_1 -> 0x149D8BDA0`, 355,717 objects).

**Not covered by this batch:** version detection is still uncancellable (by design — see the block
comment in `DetectVersionDetailed`), and **MA1** — `Macht.cpp`'s AOB family has zero cancellation, so
once a scan enters `AOBScanAllModules` every poll added here is unreachable.

### 🟡 4-of-6 2026-08-17 `[AE23-UI-2026-08-17]` — AE2 / AE3: the Class/Struct panel under fast selection

Run on **Lushfoil Photography Sim** (UE 5.6, 58,093/58,618 objects), dist 3262.

* **1 — PASS.** Clicking tree nodes populates the panel and the header tracks: `MaterialExpression`
  → `//Script/Engine/MaterialExpression`, `Super Class Object`, `Properties Size 176`, full field
  list; then `Light` → `Super Class Actor`, `696`.
* **2 — PASS, on the transition the old failure actually needed.** ⚠ **Filter recorded, because the
  step says a homogeneous list proves nothing:** keyword **`SkyLight`**, **26 results**, genuinely
  interleaved — 5 `Class`, `Enum`, `ScriptStruct`, `Function`, then **six instances**
  (`Default__SkyLight`, `Default__SkyLightComponent`, `SkyLightComponent0`,
  `Default__DatasmithSkyLightComponentTemplate`, `Default__ARSkyLight`, `SkyLightComponent0`), then
  three more `Function` rows, then two instances. Fourteen rapid `Down` presses crossed the whole
  instance block and landed on the class-like `Function BndEvt__3Dmenu_SkyLightAO_…`, and the header
  read that function's full signature with `Properties Size 4` and its single `Value FloatProperty`.
  **Header matched the highlighted row**; it did not stay on the preceding instance's class.
  *(A held `Down` advanced only one row — key-repeat does not reach this list, so use `repeat`.)*
* **3 — PASS.** No loading indicator stuck after the panel settled during that fast traversal.
* **6 — PASS.** Typing `Light` then `SkyLight` into the tree filter with a node selected left the
  Class/Struct panel **fully populated** on the previous class — it neither blanked nor flickered.
* **4 — not run** (needs a level travel to make a class address go stale; human-gated).
* **5 — not run** (the cross-tab handoff; nothing pushed a class into Class/Struct in this session).

### ⬜ AE2 / AE3 — original checklist (kept for the steps)

*Needs a game connected, but nothing else — the Object Tree is a permanent left pane beside the
Class/Struct panel, so every check is "do the two halves agree". See dev-log builds 3067 / 3068. The
11 new tests drive the ViewModel directly and therefore bypass Avalonia's ListBox entirely; what is
unproven is the real gesture under key repeat.*

1. **⚠ REGRESSION FIRST — ordinary selection still works.** Click a handful of tree nodes, both
   instances and class-like rows (`*_C`, `ScriptStruct`, `Function`). The header must track each
   click, and fields must populate. Everything below changed this path.
2. **AE2, the actual race.** Keyword-filter the tree so instances and class-like rows are
   **interleaved**, then hold ↓ to scroll through them fast and release on a class-like row. The
   Class/Struct header must match the highlighted row. The old failure needed exactly
   instance-then-class-like, so a list of only one kind cannot show it — **record what the filter
   was**, since a run over a homogeneous list proves nothing.
3. **AE2, the spinner.** During the same fast scroll the loading indicator must not stick on after
   the panel settles, and must not flicker off while a load is still running.
4. **AE3 — the retry that used to be refused.** Get a walk to fail on a node *after* a successful
   load (easiest: select a node, then travel/unload so its class address goes stale, then re-select
   it). The error line must appear, and **clicking the same row again must retry** — previously it
   was silently ignored and the panel stayed on the earlier class.
5. **AE3 — the cross-tab path, which needs no failure at all.** Select tree node P → use any
   handoff that pushes a class into Class/Struct (Interesting Funcs, Property Search, Dump Explorer)
   → click node P again. It must reload P. Before the fix the panel stayed on the handed-off class.
6. **The dedupe still holds.** Type in the tree's filter box while a node is selected (each
   keystroke nulls the selection) — this must NOT re-walk the class repeatedly, and must not blank
   the panel.

### ✅ DONE 2026-08-18 — U4 / U16 / U6 / F3: the three never-erased caches in `Ubel`

> **Ran on DumperTest Development, dist 3262, driven from CE's Lua Engine.** The exports resolve by
> name (`getAddress('UE5Dumper.UE5_WalkClassBegin')` → `0x7FFE762B6E90`), so the call sites — the
> thing no test target can reach — were exercised directly.
>
> ⚠ **Signature trap, recorded because it cost a round trip:** `executeCodeEx` is
> `(callmethod, timeout, address, …)`, **not** `(timeout, …)`. Passing the timeout first returns
> `nil, "Invalid callmethod:5000"`. That is also a live confirmation that CE's `nil, reason`
> channel carries a usable reason (cf. the `executeCodeEx` row and `ce-plugin-sdk-notes.md` §13).
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 regression | ✅ | Object Tree loaded 25,172/25,179 named (100.0%); Live Walker drilled `DumperTestActor_0` and every container; Property Search returned hits on four different queries; enum fields render member names (`ROLE_Authority`, `EActorUpdateOverlapsMe…`), not raw ints. |
> | 2 **U4** | ✅ | `A = 0x1F144477910` (a UObject instance, not a UStruct), `size = 556035168`. Two calls produced **two** `WalkClass: … at 0x1F144477910` DEBUG lines and **two** `WALK:safe … refusing to cache 0x1f144477910 — PropertiesSize=556035168 (read ok); not a UStruct, or recycled memory`. Before the fix the second call was served from the poisoned entry and logged nothing. ⚠ *Conditions:* the raw bytes at `A+0x58` read `60 6C 24 21` (=556270688) a minute earlier — those are live `AActor` bitfield bytes and they move; the point is that any reading of them is garbage as a `PropertiesSize`, and both were refused. A **second, independent** witness came free: `0x1F1408E1200` (a mis-transcribed tree address, not a UClass) was walked **4** times and refused **4** times, logging all four. |
> | 3 **U4 honest half** | ✅ | `FDateTime` `UScriptStruct` @`0x1F159AA8F80`, visited **4** times → exactly **ONE** cold-walk pair (`WalkClass: DateTime (super=, size=8)` + `— 1 fields`) and silence for visits 2–4. The gate rejects garbage, not small/empty structs. ⚠ **Strictly-zero-field case NOT demonstrated**: `FDateTime` reports **1** field (`InjectIntrinsicStructFields` supplies `Ticks`), so "0 fields is still cached" remains unwitnessed — every 0-field walk seen this session was a *refusal*. Do not read this row as closing that. |
> | 4 **U6/F3** | ✅ *(deterministic alternative)* | `DumperTestActor_0` `+0x18` = `7C C0 08 00 | 01 00 00 00` (ComparisonIndex `0x0008C07C`, Number 1). Wrote `ChaosDebugDrawActor`'s index `0x00150570` from CE, pressed Refresh: the live header changed to **`ChaosDebugDrawActor_0`** while the class stayed `DumperTestActor` (correct — only the object's FName moved). Restored `0x0008C07C`. The name memo is keyed on the input bytes, so no stale decode survived. *(The level-travel flavour was not run — this sample has no second level.)* ⚠ The **breadcrumb** still read `DumperTestActor0`, which is a historical crumb, not a stale cache — exactly the surface the step warns not to judge from. |
> | 5 **U16** | 🟡 PARTIAL | **138** `ResolveEnumValue` lines in `walk-0.log`, **0** with `N != M`, and **0** `GetEnumEntries: … truncated read` in *any* log in the folder. Healthy tables are still cached. ⚠ Two gaps: the largest table seen is **26** entries (no `EPhysicalSurface`-scale enum exists in this sample, so "large" is only exercised to 26), and the **CE DropDownList half was not checked**. |
>
> **Unchanged by this run** (as the note below already says): U5, and the class-cache-name panels.

<details><summary>Original U4 / U16 / U6 / F3 steps — kept for the method</summary>

### (superseded) NEW 2026-08-17 — U4 / U16 / U6 / F3: the three never-erased caches in `Ubel`

*Needs the DLL injected. See dev-log builds 3052 / 3058 / 3065. The C++ suite pins all three predicates
(21 new assertions, 1073 → 1094); what it structurally cannot pin is the WIRING, because no test
target compiles `Ubel.cpp`. **Every step below is about the call sites, not the predicates.***

1. **⚠ REGRESSION FIRST — ordinary browsing is unchanged.** Object Tree loads, Live Walker drills
   into an actor and shows its fields, Property Search returns hits, an enum-typed field still shows
   its member NAME (not a raw int). All three caches are on this path; if anything here is worse,
   stop and read `walk-0.log`.
2. **U4 — a non-UStruct address no longer poisons the cache.** From CE Lua pick an address `A` that
   is not a class, call `UE5_WalkClassBegin(A)` then `UE5_WalkClassEnd()`, **twice**. `walk-0.log`
   must show **two** `WalkClass:` DEBUG lines for `0x<A>` (before the fix the second was served from
   the poisoned entry and logged nothing), plus a `WALK:safe` line naming `A`. **Record `A` and the
   `size=` the first line reported** — a number without its conditions is not a measurement.
3. **U4 — the honest half.** Confirm a legitimately field-less class (or an `FDateTime` /
   `FTimespan` struct, which `InjectIntrinsicStructFields` covers) still walks and still caches:
   exactly ONE cold-walk log line across repeated visits. The gate must reject garbage, not emptiness.
4. **U6/F3 — the in-session recycle, the point of the whole commit.** Bookmark an actor, travel to
   another level **while staying connected**, then re-walk the bookmark. It must show the new
   occupant's name or `""` — never the destroyed actor's name. This is the failure that previously
   needed a game restart to clear, and the reconnect-only fix (2819) could not reach it.
   *Deterministic alternative, no level change:* note an inert object's name and the 4 bytes at
   `+0x18`, write a different valid `ComparisonIndex` there from CE, refresh the same address — the
   new name must appear. Read the name off `get_object` / `walk_instance`'s own `name` field, **not**
   off a panel that renders a class-cache name; those are frozen copies and will look stale either
   way (see the open finding below).
5. **U16 — enums are unaffected in the normal case.** Open a class with a large enum field
   (`EPhysicalSurface` or any Blueprint enum) and confirm the CE DropDownList still lists every
   member. The truncation path is not stageable on demand; what this checks is that the new
   publish gate did not stop caching healthy tables. Grep `walk-0.log` for
   `ResolveEnumValue: UEnum` — the line now reports `read N of M`, and **N must equal M**.
   Any `GetEnumEntries: ... truncated read` line is a real find, so record it.

</details>

**Still open after this batch, deliberately** — do not read a pass here as closing them:
**U5** (nothing is freed; eviction is illegal while `WalkClassEx` returns a reference),
class-to-class recycling (a recycled address whose new occupant has a *sane* `PropertiesSize`),
**A10** (`Aura`'s two reference-returning caches), and names baked into `ClassInfo::Name` /
`FullPath` / `SuperName`, which are never witnessed.

### ✅ 5-of-5 CLOSED 2026-08-18 — AA14–AA20: the CE Lua invoke path in a real game

*Needs CE + a game + the DLL injected. See dev-log build 3039. The Lua rig (63 checks) covers the
logic against stubs; what it cannot cover is a real ProcessEvent.*

1. **⚠ REGRESSION FIRST — an ordinary invoke still works.** UE5DumpUI → Interesting Funcs → pick a
   no-arg or int-arg function → **Copy AA Script (Baked)** → paste into CE → enable. It must still
   fire. Everything below changed this path, so this is the check that matters most.
2. **The one that was impossible before.** Export a baked script for a function with a
   `TArray<...>&` OUT param — `GetAllActorsOfClass` or `GetOverlappingActors` is the easy one.
   Before this it failed with *"Unknown param type 'tarray'"* and never called the game at all;
   it must now invoke, with the array param left empty.
3. **An FText param is refused, clearly.** A function taking an `FText` (a UI/dialogue setter) must
   fail with a message naming `ftext` and saying an FText cannot be built from CE Lua — **not** a
   crash. This one is deliberately still a refusal.
4. **A negative return reads as negative.** Verify Return Value mode on a function returning a
   negative int32 (or invoke one you know returns -1). It must print `-1`, not `4294967295`.
5. **A timeout says something true.** Hard to stage deliberately — pause the game hard (a loading
   screen, or break in a debugger) and invoke. The message must not quote an error from an earlier
   command, and the NEXT invoke must refuse with *"the DLL is STILL holding the mailbox"* rather
   than firing. Once the game recovers, a further invoke must work again (the guard clears itself).

> ### ✅ 5-of-5 CLOSED — steps 1-4 `[ELLIOT-CE-2026-08-18]`, step 5 `[LUSHFOIL-CE-2026-08-18]`
>
> Elliot (`Elliot-Win64-Shipping.exe`, PID 3528), dxgi proxy, **DLL build 1.0.0.3262**, CE
> **7.7.0.10568** attached, game in a loaded save. UI reported `Connected — UE504 (355717 objects)`.
> Helper delivered as a CE table file (`Table -> Add File...` -> `scripts/ue5_invoke_helper.lua`);
> CE confirmed it as `TLuafile, len=33432`, **byte-identical to the repo file** (`stat` = 33432).
>
> ⚠ **Two setup facts that will cost the next session an hour if not carried forward:**
> * **`scripts/UE5CEDumper.CT` has NO `<Files>` section** (`grep -c '<Files>'` = 0 in both
>   `scripts/` and `dist/`). Without adding the helper by hand, every row below instead hits the
>   loader refusal and nothing under test ever runs.
> * **The mailbox symbol is NOT `UE5Dumper.g_invokeMailbox` under a proxy.** Elliot loads the DLL as
>   `dxgi.dll`, so that qualified name resolves to **nil**. The generated script is already correct —
>   it tries the **bare** `g_invokeMailbox` first (`BakedScriptGenerator.cs:193-194`), which resolved
>   to `0x7FFEDD72A5D0`. A probe that only tries the qualified name reports a false failure.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | `KismetMathLibrary::Add_IntInt` A=3 B=4 via `AA(B)` -> `Copy AA Script` -> enable. `[Invoke] After : 03 00 00 00 04 00 00 00 07 00 00 00` / `[Invoke] OK: ... -> ReturnValue (int32@8) = 7`. DLL log: `INVOKE -> static-native fast path`, `INVOKE result=0` |
> | 2 | ✅ **PASS on its own assertion, with a stated limit** | `Actor::GetOverlappingComponents` — dialog shows `OverlappingComponents [Array, 16B, off=0, out]`, left empty. The invoke **reached the DLL** (`Mailbox: received cmd=4`, `INVOKE_BY_NAME starting...`), i.e. `writeParams` **accepted `tarray` and wrote nothing** instead of aborting the whole invoke with *"Unknown param type 'tarray'"* — which is exactly what AA16 fixed. ⚠ **The call itself did not execute**: `FIND_INSTANCE only CDO found for 'Actor'` -> `error=-3 'not found (0 functions walked)'`. Retried on `PrimitiveComponent`, which resolved a **real** instance (`0x1C1715F650`, no CDO warning) and still walked 0 functions. **So no TArray-OUT invoke has yet RUN to `result=0`** — the gap is target resolution, not AA16 |
> | 3 | ✅ **PASS** | `TextBlock::SetText` (`InText [FText, 16B, off=0]`). Export succeeded; the refusal fires at enable, verbatim: `[string "--[[..."]:307: [ue5_invoke] param 'InText' is an ftext -- an FText cannot be built from CE Lua (it holds a shared reference the engine allocates), and passing a zeroed one crashes the game. Invoke a wrapper that takes an FString instead.` Names `ftext` ✅, **not a crash** ✅ (game alive, PID 3528). Third witness: `grep -c SetText pipe-0.log` = **0** — it never reached the DLL, so the refusal really is client-side, before the `CMD` write |
> | 4 | ✅ **PASS** | `Subtract_IntInt(3,4)`: `[Invoke] After : 03 00 00 00 04 00 00 00 FF FF FF FF` / `-> ReturnValue (int32@8) = -1`. The **raw return bytes are `FFFFFFFF`** and the decode printed `-1`, not `4294967295` — AA20 witnessed on the bytes, not on the decoder's own word |
> | 5 | ✅ **PASS on all three assertions `[LUSHFOIL-CE-2026-08-18]`** | Retried on **Lushfoil** after Elliot's PE hook refused to install — the maintainer confirms Elliot's hook is **intermittent by title, "sometimes yes, sometimes no"**, so switching host is the correct response, not retrying. Lushfoil gave `hook_active: true`, `hook_fire_count` climbing. Vehicle: `CharacterMovementComponent::GetMaxJumpHeightWithJumpTime`, **non-static** (`flags=0x54020402`, and the DLL log shows **no** `static-native fast path` line), so it really queues on the game thread. **Baseline first**: `ReturnValue (float@0) = 89.99999237`. Then game thread frozen -> **(a)** `Mailbox timeout after 10000ms -- the DLL took the command but did not finish it (status=255, no message from the DLL)` — `status=255` is the **0xFF** branch (the one a whole-process suspend cannot reach) and *no message from the DLL* is AA18 holding **even though the immediately preceding command had succeeded**; **(b)** immediate retry -> `[ue5_invoke] the previous invoke timed out and the DLL is STILL holding the mailbox -- sending now would overwrite the class/function/params of a call that is mid-flight...` (AA19), returned at once rather than after another 10 s; **(c)** once the DLL reported done, the next fire was **allowed through** (timeout message again, not the refusal) — i.e. the guard cleared itself |
>
> **State left as found:** invoke timeout restored (`{"timeout_ms":0}` -> `invoke_timeout_ms: 5000`,
> `persisted: false`), no thread left suspended, no cache record modified, game/CE/UI all killed.
>
> ### ⚠ Two traps this retry paid for — both would silently invalidate a re-run
>
> 1. **The DLL's own invoke timeout must EXCEED the Lua's, or assertion (b) is untestable.** First
>    attempt used 30000 ms: the DLL released the mailbox at T+30 s and the retry click landed at
>    T+33 s, so the guard correctly stayed silent and the run *looked* like an AA19 failure. It was a
>    **mis-timed test, not a defect** — the DLL log settles it (`INVOKE_BY_NAME complete, result=-5`
>    at 15:39:52, next `received cmd=4` at 15:39:55). Raising it to **120000 ms** made the window
>    ~110 s and the guard fired first try. ⚠ A GUI round trip is ~5-10 s, so the window must be tens
>    of seconds, not the 20 s that 30000 leaves.
> 2. **`tools/verify/suspend.py` matches on a SUBSTRING and acts on the FIRST match.** Steam titles
>    have a launcher shim with the same stem (`LushfoilSim.exe` beside
>    `LushfoilSim-Win64-Shipping.exe`; likewise `Elliot.exe`), and the shim sorts first, so
>    `suspend-tid LushfoilSim <tid>` froze a **1-thread shim** while ProcessEvent kept firing.
>    **Always pass the full image stem.** The tell is the two-detector check doing its job:
>    `hook_fire_count` kept climbing under suspension.
>
> ⚠ **Also measured: creation order is NOT a reliable game-thread oracle here.** On Lushfoil **four**
> separate threads each took ProcessEvent to 0 when suspended (the frame pipeline stalls if any of
> them halts), and the earliest-created thread was not among the highest-CPU ones. Pick by EFFECT.
> ⛔ **And the game did not recover**: after `resume-tid` (suspend count 1 -> 0, verified)
> `hook_fire_count` stayed frozen at 2,070,966 for 5+ minutes while the process still reported
> `Responding: True`. That is the lock hazard the rig's own docstring warns about, now observed —
> **assume a suspended game thread is a one-shot and plan to restart the title afterwards.**

### 🟡 6-of-6 STEPS ATTEMPTED (step 4 closed 2026-08-20; step 2 still PARTIAL) — AE4–AE7: the Proxy Deploy panel, two buttons at once

*No game needed — just the UI and a folder with a couple of detected games. See dev-log build 3038.
Every step is a click sequence; the unit tests cover the logic, not what the panel looks like doing it.*

1. **Two operations no longer overlap.** Scan for games, tick a couple, press **Deploy** and then
   immediately **Remove**. The second must refuse with a line naming what is running
   (*"Busy: Deploy is running…"*) — not the old *"Wait for scan to finish"* when no scan is running,
   and not both operations writing over the same `Binaries` folder.
2. **The busy indicator finally appears for them.** The panel's progress bar is bound to
   `IsScanning`, which Deploy / Remove / Refresh / Update All never set — so they used to look like
   nothing was happening. Confirm the bar now runs during each of the four.
3. **⚠ REGRESSION — the three scans still work and still cancel.** Scan Steam, Scan drives (+ its
   Cancel button), Find leftovers (+ its Cancel). The gate took over `IsScanning` from all three, and
   `IsScanningDrives` / `IsScanningOrphans` still drive the two Cancel buttons independently — a
   ghost Cancel on the wrong card is the failure to watch for (it is what B45 fixed originally).
4. **⚠ REGRESSION — leftover removal is unaffected.** Find leftovers → tick one → Delete. It uses
   `IsRemovingOrphans`, which the gate now also tests; confirm a delete still blocks a scan and vice
   versa.
5. **The proxy-type radios.** Click through version → dinput8 → dxgi quickly. The grid's Status /
   Installed Version columns must end up showing the type the radio shows. Before this they could
   settle on a type nobody selected, with nothing to re-run it.
6. **The drive-selection reset.** Switch source to **Scan drives**, and while the drive list is
   loading switch back to Steam and to Drives again. Tick some drives. They must stay ticked — a
   second load used to `Clear()` the list and silently drop the selection.

> ### 🟡 4-of-6 CLOSED 2026-08-17 `[AE4-UI-2026-08-17]` — UI driven with computer-use, no game
>
> Build **1.0.0.3262** (the AOT `dist` binary), app `Disconnected` throughout. **Two steps are
> recorded NOT TESTED with a measured reason, not waved through.**
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS, via a stated substitution** | Deploy-then-Undeploy **cannot** be made to overlap here: a single 2.8 MB copy finishes faster than one input event, measured twice (`Deployed: 1 success, 0 failed` then `Removed: 1 success, 0 failed` — both ran). The **shared** gate was then exercised with a long first operation: with `Scan drives` running, pressing `Deploy` produced **`Busy: Scan drives is running — wait for it to finish`** — a line that *names what is running*, and not the old wrong *"Wait for scan to finish"*. Designed to risk nothing: **no rows were ticked**, so neither outcome could write a file |
> | 2 | 🟡 **PARTIAL** | Bar confirmed during **Scan Steam** (`Checking deploy status...`), **Scan drives** and **Find leftovers**. The last is the one that counts: it runs on `IsScanningOrphans`, *not* `IsScanning`, so the bar is demonstrably no longer bound to `IsScanning` alone. **NOT observable for Deploy / Undeploy / Refresh / Update All** — each completes in under one screenshot round-trip on this machine. Not a pass |
> | 3 | ✅ **PASS on 2 of 3 cancels** | Scan Steam runs; **Scan drives runs AND cancels** with an explicit `Scan cancelled` status. Find leftovers runs, its `Cancel` appears on the **correct card** and clears correctly — but the scan finished before the click **twice** (14 s then <3 s), so the orphan cancel itself is **NOT tested**. ⚠ The B45 failure was checked in **both** directions: no ghost `Cancel` ever appeared on the other card |
> | 4 | ✅ **PASS 2026-08-20 `[AE4S4-ORPHAN-2026-08-20]`** | Staged the synthetic leftover with `tools/verify/stage_synth.py create` — `ZZSynthOrphan\ZZOrphan\Binaries\Win64ersion.dll`, our proxy with **no exe beside it**. **Find leftovers** → `Found 1 leftover proxy DLL(s) — nothing removed yet`; ticking the row enabled **`Delete checked (1)`** and the row spelled out the plan (`Recycle version.dll, then remove up to 4 folder(s) if it leaves empty, stopping below ZZSynthOrphan`). The confirm dialog listed the four folders **leaf→root, each only if left empty**, and named the boundary `Not touched: …\steamapps\common`. Result: **`Cleaned 1 of 1 leftover(s) — 1 file(s) recycled, 4 folder(s) removed`**.<br>**Verified independently of the panel's own report** (working-lessons §1.4): `ZZSynthOrphan` is gone from disk, `…\steamapps\common` still exists, and the file is genuinely in the bin and recoverable — exactly **2,882,560 bytes** at `D:\$Recycle.Bin\S-1-5-…\$RGOIP98.dll`.<br>⭐ **The negative control is the strongest part**: the sibling tree `ZZSynthProxyTest`, which holds our `dxgi.dll` **beside a real `-Shipping.exe`**, was left completely untouched (both files still present). So the scanner distinguished a true leftover from a proxy that belongs to a game, rather than deleting every proxy it found.<br>⚠ **The mutual-exclusion half of this step was NOT demonstrated** — "a delete blocks a scan and vice versa". The delete completes faster than one input event, the same measured reason step 1 could not be made to overlap. |
> | 5 | ✅ **PASS, both directions** | version→dinput8→**dxgi** clicked quickly: header becomes `Source: dxgi.dll v1.0.0.3262`, the nine `version.dll` titles flip to `DeployedOtherType` with the Version column **cleared**, and **Elliot flips to `DeployedCurrent` 1.0.0.3262** — because its real proxy *is* dxgi. Clicking back to `version.dll` flips both sets symmetrically. So Status **and** Installed Version follow the radio, with a positive and a negative case in one view |
> | 6 | ✅ **PASS** | D: ticked → Source toggled Steam→Scan Drives to force a **second** load of the drive list → D: **still ticked** |
>
> **State left as found, verified independently of the panel's own report** (working-lessons §1.4):
> TQ2 was the deploy target and its `Binaries\Win64` was re-listed from Python afterwards — no
> `version.dll` / `dxgi.dll` / `dinput8.dll` / `winmm.dll` present. `Force Overwrite` and the Source
> radio were both returned to their original values.
>
> **§3a re-confirmed by a second, independent route.** The panel reports `Found 16 UE game(s)` —
> exactly the 16 titles an offline enumeration found — and every deployed proxy reads **1.0.0.3262**:
> nine `version.dll` (ES2, DQ7R, DQ I&II, EVERSPACE, Lushfoil, Manor Lords, OCTOPATH, SEED, Geri) plus
> **Elliot** on `dxgi.dll · confirmed working`. Ten, matching §3a exactly.
>
> ⚠ **NEW, and §3a's inventory does not cover it: an ELEVENTH deployed proxy, and it is STALE.**
> A drive scan surfaces `D:\UE_Analyze_data\Game archive\Satisfactory\UE5.6.1\…` as
> **`DeployedOutdated 1.0.0.2498`**. It is in the reference-build corpus rather than a game, which is
> why the game-only inventory missed it. Two consequences: §3a should say *eleven*, and this row is a
> ready-made **`DeployedOutdated` fixture** — the only one on the machine — for exercising `Update All`
> (AE4 step 2) against something that actually needs updating.
>
> *Incidental lead, not filed as a finding:* launching a **second** instance of the UI writes an
> unhandled exception into `crash.log` — `System.InvalidOperationException: Cannot perform requested
> operation because the Dispatcher shut down` at `ClassicDesktopStyleApplicationLifetime.StartCore` —
> instead of exiting quietly. The first instance is unaffected and keeps running. Worth a look because
> `crash.log` is documented as *the* AOT startup diagnostic, and a benign duplicate launch pollutes it.

> ### 🟡 STEPS 2 AND 3 CLOSED 2026-08-19 — by the maintainer, on their own machine
>
> The maintainer worked the 繁中 checklist from a NAS copy and ticked its rows **2, 3, 5 and 6**
> (that file's rows are 1:1 with the six numbered steps above). Two of those four were already closed
> here; the other two are exactly the two this block had recorded as **not** settled, and they are the
> reason this row moves from 4-of-6 to 5-of-6:
>
> | step | was | now | what changed |
> |---|---|---|---|
> | 2 | 🟡 PARTIAL — the bar was only observable on the three *scans*; Deploy / Remove / Refresh / Update All each finished inside one screenshot round-trip | ✅ | a human watching the panel live is not bound by the round-trip that blocked the automated pass — this is the measurement the machine could not take |
> | 3 | ✅ on **2 of 3** cancels — Find leftovers finished before the click, twice | ✅ **3 of 3** | the orphan-scan Cancel was finally caught mid-scan |
>
> Steps 5 and 6 were ticked too and were already ✅ above; the ticks corroborate, they do not add.
> **Step 1 was NOT ticked** and does not need to be — it is ✅ above via the stated substitution
> (a long first operation exercising the shared gate).
>
> ⚠ **What remains is step 4, and only half of it.** The *removal* half is closed (see
> "AE4 step 4 — removal half CLOSED" above: 1 file recycled, 4 folders removed, recoverable from the
> Recycle Bin). What is still unproven is the **`IsRemovingOrphans` gate arm** — that an in-flight
> delete refuses a scan and vice versa. The maintainer did not tick step 4, which is consistent:
> forcing that overlap needs a delete slow enough to click through, and nothing on this machine is.
>
> ⚠ **Evidence class, stated plainly:** these two ticks are the maintainer's own observation. No log
> line, screenshot or file hash from that run reached this repo, and nothing here was re-observed to
> confirm them. They are recorded as reported.

### ✅ 5-of-5 CLOSED (step 4 on 2026-08-18) — AA4–AA7: ue5_dissect.lua in a real Cheat Engine

*Needs CE + a game, and **step 2 needs no DLL at all** — it is the fastest check here. See dev-log
build 3037. The Lua rig (`lua scripts/tests/dissect_test.lua`, 40 checks) covers the logic against
stubs; what it cannot cover is CE's real dissect machinery.*

> **All five steps ✅ PASS.** Steps 1, 2, 3, 5 in two 2026-08-16 sessions, deliberately split;
> **step 4 closed 2026-08-18 on DumperTest** (`[DUMPERTEST-CE-2026-08-18]`, see it below):
> `[CE-NOTEPAD-2026-08-16]` = CE 7.7.0.10568 attached to `Notepad.exe` with **no DLL at all**
> (steps 2, 3); `[ELLIOT-CE-2026-08-16]` = the same CE against **Elliot** with the DLL injected
> (steps 1, 5). **The batch paid for itself**: step 1 failed on first run and turned out to be a
> real, shipped defect (**AU1**, fixed in build 3157) rather than a bad test.

1. **✅ PASS `[ELLIOT-CE-2026-08-16]` — but it FAILED FIRST, and that failure was a real defect.**
   CE → inject the DLL via `UE5CEDumper.CT` → Lua Engine →
   `local d = dofile("ue5_dissect.lua"); d.createFromPath("/Script/Engine.Actor")`. A structure
   appears in the Structure Dissect list with named fields at plausible offsets. **This is the
   regression half — `callDLL` now raises where it used to return nil.**
   **First run (build 3156) printed `[UE5Dissect WARN] Object not found: /Script/Engine.Actor`** and
   created nothing. The exports resolved fine — the DLL genuinely could not find the object. Root
   cause and fix: **AU1** below / dev-log build 3157; `UE5_FindObject` handed its `fullPath`
   argument to a bare-FName matcher, so no path ever resolved.
   **After the fix, same CE session, same command:** `STEP1 ok=true`, `structs before=1 after=2`,
   `name=Actor fields=129`. The before/after sits in one Lua Engine window.
5. **✅ PASS `[ELLIOT-CE-2026-08-16]` — No gap rows.** Walking the created `Actor` structure's 129
   elements: **`unnamed=0`, `unnamedPointer=0`**, and the header reads
   `0:VTable | 8:ObjectFlags | 12:ObjectIndex | 16:Class | 24:FNameIndex | 32:Outer` — the expected
   UE5 `UObject` layout, which also satisfies step 1's "named fields at plausible offsets". This is
   `addFieldsToStruct`'s own output (the DLL was present), so unlike the earlier no-DLL session it
   really does exercise the builder `fillGaps` was deleted from.
2. **✅ PASS `[CE-NOTEPAD-2026-08-16]` — ⚠ The one that needs NO DLL, and the one AA4 is about.**
   In a fresh CE with the DLL *not* injected: `local d = dofile("ue5_dissect.lua");
   d.enableAutoCallback()`, then open "Dissect data/structure" on **any ordinary address** (a plain
   allocation, not a UObject). CE must dissect it **normally**. Before this fix the callback raised,
   CE re-raised it as a Pascal exception, and its own `autoGuessStruct` never ran — so Structure
   Dissect was broken for every address until the user found `disableAutoCallback()`. Expect at most
   ONE `[UE5Dissect WARN] auto-dissect … failed` line, not one per node.
   **Conditions, because a number without them is not a measurement:** CE **7.7.0.10568** 64-bit
   attached to `Notepad.exe`, `UE5Dumper.dll` never injected, repo copy of `ue5_dissect.lua`
   (2026-08-16, 27,322 B). Target address was a **`allocateMemory(4096)` block in Notepad**
   (0x17A41C20000) pre-filled with `writeInteger(a+i*4, i*7+1)` so the readback is self-witnessing.
   **Result:** Structures → Define new structure → CE's own name/Guess-Field-Types dialog appeared,
   OK produced a fully populated `unnamed structure 1` — `0000/0004/0008/000C…` at a 4-byte stride
   reading back **1, 8, 15, 22, 29, 36, 43, 50, 57, 64, 71, 78, 85, 92, 99, 106, 113, 120, 127, 134,
   141, 148, 155, 162**, i.e. exactly the written pattern. **The whole operation emitted ONE
   `[UE5Dissect WARN] auto-dissect name lookup failed … (reported once; run
   dissect.disableAutoCallback() to unregister)` block** naming `UE5_GetObjectClass`, across 24+
   rows — so the per-node flood is gone and CE's own machinery ran to completion.
   *The warn line is also the proof the callback was REGISTERED and DID fire; without it, "CE
   dissected normally" would have been indistinguishable from not running the script at all.*
3. **✅ PASS `[CE-NOTEPAD-2026-08-16]`, and this batch named the WRONG export.** With the DLL not
   injected, `pcall(d.createFromPath, "/Script/Engine.Actor")` returned
   `false, …ue5_dissect.lua:80: [UE5Dissect] DLL function not found: UE5_FindObject` — the fix's
   whole point (a message naming the export, *not* `attempt to compare nil with number`).
   The expected name here used to read `UE5_WalkClassBegin`; that was never reachable first —
   [`ue5_dissect.lua:446`](../scripts/ue5_dissect.lua) resolves the path with **`UE5_FindObject`**
   before it ever walks a class, so `UE5_FindObject` is the correct expectation. Corrected in place.
   *Bonus, and it settles an open doubt:* the guard that fired is the `getAddress(name) == nil or 0`
   test at `:80`, which only exists because CE's `errorOnLookupFailure` really does default **FALSE**
   despite `celua.txt` claiming TRUE — see [CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md) §6. This
   is the first live confirmation of that; the guard is not dead code.
4. **✅ PASS `[DUMPERTEST-CE-2026-08-18]` — a mid-walk failure leaves nothing behind.** Staged
   exactly as written, on DumperTest Development (dist 3262) with the DLL injected.
   **Baseline:** CE's structure list at **0** (the table reload had cleared it), then
   `d.createFromPath('/Script/DumperTest.DumperTestActor')` →
   `[UE5Dissect] Struct created: DumperTestActor (193 elements, 1760 bytes)`, list at **1**.
   **Then `Stop-Process DumperTest -Force`, confirmed dead, and re-ran the same call.** It failed
   loudly — `Error:Failure to allocate memory` / `Script Error` (CE's own message; no invented
   wording) — and the structure list afterwards read:
   ```
   structure count now = 1
     [0] name=DumperTestActor elements=193
   ```
   **Still 1, still the intact 193-element structure from step 1: no half-built entry, no empty
   entry, and the good one was not damaged** — which is the whole of what this step asks.
   ⚠ **Precise about what was NOT shown.** The raise escaped a `pcall` wrapped around
   `createFromPath`, so it fired *before* that function — at the `dofile` re-load, where the script
   resolves exports against a process that no longer exists. So the *inside-`createFromClass`*
   unwind path is still unwitnessed; what is proven is the row's actual claim, that the attempt
   fails and leaves no debris.

### 🟡 4-of-5 CLOSED 2026-08-19 — A6: Force now holds the class AND its subclasses

*Any game. See dev-log build 3036. This one changes what an already-shipped, in-game-verified
feature WRITES TO (the Stealth Meter card), so the regression half matters as much as the fix.*

1. **The capability that did not exist before.** Property Search a field on a base class — anything
   whose row shows an "inherited by N" badge (`bCanBeDamaged @ Actor` is the easy one) → right-click
   → Force. Before this it said *"0 live instances of Actor … — nothing held"*; it must now hold on
   a real, non-zero count. **That message is the whole finding — if it still appears, stop.**
2. **The held instances are the SUBCLASSES.** Property Search's "Forced fields (N held)" strip
   should show a count in the hundreds for a broad base, not 1. If the pool is capped the status
   line must say *"cap reached, more exist unheld"* — a broad base hits the 256 cap easily, so
   confirm the badge appears rather than a bare "on 256 instance(s)".
3. **Derivation, not substring.** Force a field on a class with a same-prefix sibling (`Enemy` vs
   `EnemyProjectile`, or any `Foo` / `FooComponent` pair in the game). The unrelated class must NOT
   be held — check the ForcedFields strip / the DLL log line `FindInstancesDerivedFrom base=…`,
   which reports the distinct class count it walked.
4. **⚠ REGRESSION — Stealth Meter still works.** Teleport tab → Stealth card → Detect → Hold @0 →
   Reset. It resolves a CONCRETE class, so subclass semantics should be additive — but this is the
   shipped, previously in-game-verified path that A6 deliberately changed, and it is the one thing
   here that could get *worse*. Confirm Hold reports a non-zero count and Reset restores.
5. **⚠ REGRESSION — no CDO is written.** After forcing a bool on a base class, the game must not
   show every future spawn already carrying the forced value in a way that survives
   `reset_all_fields` — that would mean a class-default object was written. (The CDO skip moved
   inside Aura's walk; the local skip in `Solide` stayed as the invariant.)

> ### 🟡 STEPS 1, 2 AND 4 CLOSED 2026-08-19 — by the maintainer, on their own machine
>
> Ticked in the 繁中 checklist, whose five rows are 1:1 with the five steps above.
>
> * **Step 1 ✅ — the capability that did not exist before.** Force on a base-class field held a
>   non-zero count. The pre-3036 output *"0 live instances of Actor … — nothing held"* is the whole
>   finding, and the checklist says to stop if it reappears; it did not.
> * **Step 2 ✅ — the held instances really are the subclasses.** The "Forced fields (N held)" strip
>   showed a broad-base count rather than 1, with the cap disclosed as *"cap reached, more exist
>   unheld"* rather than a bare "on 256 instance(s)".
> * **Step 4 ✅ — ⚠ REGRESSION CLEAR.** Teleport → Stealth card → Detect → Hold @0 → Reset still
>   reports a non-zero hold and restores on Reset. This is the one already-shipped, already
>   in-game-verified path A6 changed, so it is the step that could have gone *backwards*. It did not.
>
> ### ✅ STEP 3 CLOSED 2026-08-19 `[A6-DERIV-2026-08-19]` — derivation, not substring, PROVEN
>
> Headless over the pipe on DumperTest Development / dist 3263 (`tools/verify/a6_derivation.py`).
> The pair chosen is stronger than the row's `Enemy`/`EnemyProjectile` suggestion because **UE
> guarantees it**, so the result does not depend on one game's class tree:
> `CharacterMovementComponent` starts with `Character` and derives from `UActorComponent` — it is
> not a `Character` by any super-chain, but it is an exact prefix match.
>
> | walk | live instances |
> |---|---|
> | `FindInstancesDerivedFrom base='Character'` | **1** |
> | `FindInstancesDerivedFrom base='CharacterMovementComponent'` | **7** |
>
> Forcing `bCanBeDamaged` on `Character` held **1**. A prefix matcher would have held **8**.
>
> ⭐ **The reachability control is what makes this decisive, and it is the half that is easy to
> skip.** "The impostor was not held" proves nothing if the impostor is not in the pool: forcing
> `bAutoActivate` on `CharacterMovementComponent` itself held **7**, so those objects are live,
> reachable and holdable — their absence from the `Character` hold is a real **exclusion**, not an
> empty pool. Both walks report the same corpus (`scanned=25179, nonNull=25172, 3941 distinct
> classes`), so the difference is not a scoping artefact either.
>
> Game state restored: `reset_all_fields` → `get_forced_fields` re-read, **0** fields held.
> ⚠ Also worth knowing: **`find_instances` matches by NAME** and cheerfully returns
> `Default__CharacterMovementComponent` for the query `Character`. The two code paths are different,
> and confusing them is exactly the mistake this step exists to rule out.
>
> **Step 5 (no CDO is written) still remains** — it needs spawns after a reset, which a static pool
> cannot provide.

> ⚠ **STEP 5 REMAINS (step 3 closed above); it is the one that could still be wrong:**
> * **Step 3 — derivation, not substring.** Nothing above distinguishes a real super-chain test from
>   a name-prefix match; both hold "hundreds". It needs a same-prefix sibling pair (`Enemy` /
>   `EnemyProjectile`, any `Foo` / `FooComponent`) with the unrelated class confirmed **not** held,
>   read off the ForcedFields strip and the DLL's `FindInstancesDerivedFrom base=…` line. A6's whole
>   point is that this is a derivation test, and it is still unproven on a live pool.
> * **Step 5 — no CDO is written.** Force a bool on a base class, `reset_all_fields`, then watch
>   *newly spawned* objects. If they still carry the forced value, a class-default object was
>   written. Needs spawns after the reset, so it cannot be settled from a static pool.
>
> ⚠ **Evidence class:** the maintainer's ticks. No log line or screenshot reached this repo, and
> step 2's actual count and step 4's actual numbers were not recorded. Reported, not re-observed.

### ✅ ALL 5 CLOSED 2026-08-18 — AB3/AB5: the vector scan on a UE5 (LWC) game

*Needs a **UE5** game — this is the one check a UE4 title structurally cannot make. See dev-log
build 3035.* Until then the DLL's LWC vector scan is **shipped but unproven on a real target**.

> **🡒 A suitable target was live and the scan type was wrong `[DSA-2026-08-16]`.** DragonSword
> Awakening is **UE5.4**, i.e. exactly the LWC population this batch needs, and the session ran both
> a `begin_value_scan` and a `begin_group_scan` — but **both used `"data_type":"NumericNoByte"`**, so
> the vector decode path never executed. Nothing here is settled. Next time that title is up,
> **one `FVector` Exact scan closes steps 1–3.** (Its `Rotator` / `Vector_NetQuantize100` struct
> fields already render correctly in the walker — `"value":"{X=0, Y=0, Z=0}"` — but that is the
> *walker's* struct decoder, a different code path from the scan predicate this batch is about.)

1. **A UE5 world-position scan returns real hits.** Value Search → data type **FVector** → Exact →
   type the player's current X,Y,Z (read them off the Teleport panel's POV/marker readout, which is
   already width-aware) → First Scan. Before this fix a UE5 game returned **zero** plausible hits
   because every 24-byte `Vector` was compared as three floats; it must now return the player pawn's
   location among the candidates. **This is the whole point of the fix — if it still returns nothing,
   stop and report, do not "narrow the search".**
2. **The value column reads back as the coordinates you typed**, not a huge/tiny number. That proves
   the *display* decoder agrees with the *compare* decoder about the width (they were one hardcoded
   12 before, and are now one canonical 3-double form).
3. **Next Scan (refine) survives.** Move the character, then Changed → the surviving candidates must
   include the pawn location. This is the half that needs `FieldDescriptor::vectorWidth`: refine has
   no access to the class index, so a session that lost the width would drop every candidate here.
4. **A UE4 game still works.** Same scan on any UE4 title (12-byte `Vector`) — this is the
   regression half; the width gate must not have narrowed what UE4 accepts.
5. **A `Vector3f` field on a UE5 game** (float-backed, 12B, in the same process as 24B `Vector`
   fields) also matches. That is the case a version-keyed fix would have got wrong, and the reason
   the width is read per field rather than per game.

> ### ✅ STEP 5 PASSES `[ELLIOT-AB3-2026-08-18]` — one scan matched BOTH widths in one process
>
> **Elliot**, `Elliot-Win64-Shipping.exe`, **UE 504**, DLL **1.0.0.3262**, dxgi proxy, 84,388 objects
> scanned per pass. A second UE5 title for this batch (steps 1–3 were DSA), which is itself worth
> having.
>
> **Target found over the pipe, not by clicking.** `search_properties` returns a `struct_type` per
> field, so sweeping ~18 vector-ish name queries and grouping by `struct_type` gives the width census
> directly: this process holds `Vector`=24 B, `Vector2D`=16 B, `Vector4`=32 B, **`Vector2f`=8 B** and
> **`Vector3f`=12 B**. `ChaosClothConfig`'s CDO is the ideal specimen — it carries **both widths in
> the SAME object**: `Gravity` / `LinearVelocityScale` as 24 B `Vector`, `MaxLinearVelocity` /
> `MaxLinearAcceleration` as 12 B `Vector3f`. Exact bytes from `walk_instance`:
> `MaxLinearVelocity.hex = 00007A44 00007A44 00007A44` = three **floats** of 1000.0 at
> `0x7FF4DE864B44`.
>
> **One `FVector` Exact scan for `1000,1000,1000` returned 3 hits spanning both widths:**
>
> | address | class::field | struct | width |
> |---|---|---|---|
> | `0x7FF4DE6877F0` | `BoxComponent::RelativeScale3D` | `Vector` | **24 B** |
> | `0x7FF4DE7345A0` | `NiagaraDataChannel_Islands::InitialExtents` | `Vector` | **24 B** |
> | `0x7FF4DE864B44` | `ChaosClothConfig::MaxLinearVelocity` | **`Vector3f`** | **12 B** |
>
> ⇒ **Exactly the case a version-keyed fix gets wrong**: a single UE5 process where one predicate has
> to accept 24 B and 12 B fields in the same pass. The width is demonstrably per field, not per game.
> Widths were confirmed from the class layout (`walk_instance` `size=`), not inferred from the hit.
>
> **Two controls, because a scan that matched everything would also "pass":**
> * A value present ONLY in a 12 B field — `60000,60000,60000` → **exactly 1** hit,
>   `ChaosClothConfig::MaxLinearAcceleration` (`Vector3f`, `0x7FF4DE864B54`). A 24 B-only compare
>   finds nothing here.
> * An implausible triple `1234.5,6789.25,-4321.75` → **0** hits over the same 84,388 objects.
>
> ⚠ **Scope, stated plainly:** driven over the pipe (`begin_value_scan`), which exercises the same
> `Radar` predicate the UI calls — that predicate *is* what step 5 is about. The **display** half was
> already covered by step 2 on DSA; this run does not re-cover the UI rendering.
> ⚠ `game_only` must be **false** — every specimen above is an engine class, and the default `true`
> hides all of them, which would read as "no Vector3f in this game".
> ⚠ The reply puts hits under **`candidates`**, not `results`; reading the wrong key reports a clean
> pass as a failure.

> ### ✅ STEPS 1-3 PASS `[DSA-2026-08-18]` — the LWC vector scan works on a real UE5.4 target
>
> **DragonSword Awakening**, `DSClient-Win64-Shipping.exe` PID 49612, **UE 504**, DLL build
> **1.0.0.3262**, CE-injected (no proxy deployed for this title), 275,612 objects, map
> `World_01_Main_WP`, pawn `0x18F21068040`.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | Value Search → **FVector** / **Exact** / Deep on / Native-C **off** → `41342,110645,1641` → `First Scan: 3 candidates in 766 ms (scanned 257500 objects, 722 classes with matching fields)`. **The player pawn is among them**: `DsPC_Lute_V2_C.ActiveLocation` at `0x18F21068F10` = pawn `0x18F21068040` **+ 0xED0**, the row's own reported offset. The other two are `CapsuleComponent.RelativeLocation` (`CollisionCylinder`) and `DsPCMovementComponent.LastUpdateLocation` (`CharMoveComp`) — both plausible, neither noise |
> | 2 | ✅ **PASS, witnessed on the RAW BYTES not on the UI cell** | The Value column truncates to `41342, 11064…` and would not widen, so the check was made independently: `py tools/verify/read_mem.py DSClient-Win64-Shipping 0x18F21068F10 24` → `00 00 00 00 C0 2F E4 40  00 00 00 00 50 03 FB 40  00 00 E0 06 5E A2 99 40` → as **3 doubles** = `(41342.0, 110645.0, 1640.5918231010437)`, matching the Teleport pose exactly. **The same 24 bytes read as 3 floats give `(0.0, 7.13, 0.0)`** — i.e. the pre-fix decode really is garbage on this target, so the fix is doing visible work rather than being a no-op here |
> | 3 | ✅ **PASS** | Moved the character (verified by re-reading the same address: `(42260.337…, 110719.078…, 1773.512…)`), then **Changed** → `Next Scan (Changed): 3 surviving candidates in 0 ms` — **all three survived, pawn included**, and the Value column re-rendered as `42260.3, 110…`. This is the half that needs `FieldDescriptor::vectorWidth`: a session that lost the width would have dropped every candidate |
> | 4 | ✅ **PASS `[DQ7R-2026-08-18]`** | **DQ7R**, UE **427**, 199,196 objects, `version.dll` proxy (build 3262) — the UE4 half. Pose `29.115 / -103.393 / 133.344` (pawn `0x20544680010`), scanned as **`29,-103,133`** → `First Scan: 4 candidates in 673 ms (scanned 154900 objects, 580 classes with matching fields)`: `SceneComponent.RelativeLocation` (`RootSceneComponent`), `DOLLPlayerMovementComponent.LastUpdated…`, `DOLLPlayableCharacterCapsuleComponent…`, `AtomComponent.RelativeLocation`. **Independently witnessed on the bytes, mirroring the UE5 half**: `read_mem … 0x20463ED831C 12` → `30 EC E8 41 3C C9 CE C2 F6 57 05 43` = **3 floats** `(29.115325927734375, -103.39303588867188, 133.34359741210938)`. So **24B doubles on DSA and 12B floats on DQ7R both match under the same predicate** — the width really is read per field, and the gate did not narrow what UE4 accepts |
> | 5 | ⬜ not run | `Vector3f` (12B) beside 24B `Vector` in the same process — no known field to aim at yet |
>
> ### ⛔ THE TRAP, AND THIS ROW'S OWN INSTRUCTIONS WALK INTO IT
>
> **The first scan returned `0 candidates` — and it was the INPUT, not the scanner.** This step says
> to "read them off the Teleport panel's POV/marker readout", which prints **three decimals**
> (`Z: 1640.592`). Pasting that verbatim is a **guaranteed zero-hit**, because
> `Radar::CompareFloatScalar` (`Radar.cpp`) branches on whether the TYPED target is a whole number:
>
> ```cpp
> case ScanType::Exact:  return IsWhole(a) ? (rc == a) : (cur == a);
> ```
>
> A whole target compares the **rounded** current value (tolerant); a fractional target compares
> **bit-exact doubles**. The true Z is `1640.5918231010437`, so `1640.592` can never match — and the
> raw bytes above are what proves that rather than a guess. Re-running with `41342,110645,1641`
> (all three axes whole) hit on the first try.
>
> ▶ **Round every axis to a whole number before an Exact vector scan**, or the row reports a defect
> that is not there. ⚠ This is exactly the failure the step warns about in the opposite direction —
> *"if it still returns nothing, stop and report, do not narrow the search"* — so the instruction as
> written would have produced a **false FAIL**. Worth fixing at source: either the Teleport panel
> should offer a whole-number copy, or the step should say to round.

### 🟡 A5 + AE9 CLOSED, V6 corrected to a HALF-pass 2026-08-19 — the fourteen-MED batch, all UI-visible: A5 / V6 / AE9 / U8 / V7 / U7 / G1 / X3 / AB6 / AF4 / AF2 / AF6 / AE8 / AF1 (builds 3016-3031)

Eight of the fourteen now carry a PASS block below (A5 · V6 · AE9 · V7 · AF4 · AB6 · AE8 · AF6) —
but **V6's is a HALF-pass and is corrected below**: its evidence covers the manual-Refresh half only,
and the auto-refresh half its own step prescribes was **not observed and could not have been**. The
rest are cheap to check because each has a *visible* pass/fail, and four of them only ever show up
when something ELSE goes wrong.

**Free from any ordinary session (just look):**

1. **A5 — Preview shows a LIVE value. ✅ VERIFIED 2026-08-19 — by the maintainer, on their own
   machine. I did not observe it.** Property Search a field you can change in-game (Health).
   The Preview column must read the value from a live instance, not the Blueprint default. A row
   whose class has no live instance must read `… (CDO default)` — the marker is the fix's honesty
   half, so confirm both.
   **Evidence: the maintainer's own run.** They re-ran the search after letting the value move
   in-game and reported the second Preview carrying the new number. That is *their report*, not an
   agent observation — there is no screenshot and no log line of mine behind it, and it is recorded
   to the same standard as the `[SKIA-ABI-2026-08-19]` close above. It corroborates the
   `[GRP4-UI-2026-08-17]` PASS block below, which was obtained by the same procedure.
   ⚠ **The Preview is a SNAPSHOT taken when the search runs; it does not update on its own, and it
   is not supposed to.** There is no timer and no live-cell binding in `PropertySearchViewModel` —
   `Preview` is a plain string on the result row, written once per search. So the check is
   **search → note the value → let the game move it → press Search AGAIN → the value must have
   moved**, which is exactly how the 2026-08-17 PASS below was actually obtained ("a re-search ~38 s
   later previewed 317"). Staring at the column waiting for it to tick is testing a feature that
   does not exist, and reads as a defect that is not there.
   📌 **Why this had to be run twice, and the lesson that is worth more than the close.** The
   2026-08-17 PASS was *also* obtained by re-searching — and the conditions that produced it were
   written into the **evidence block** ("a re-search ~38 s later previewed 317") but never into the
   **step**, which in this file *and* in the 繁中 mirror still told the reader to watch the Preview
   column. A wrong procedure therefore survived a PASS. On 2026-08-19 the maintainer followed it,
   saw nothing move, re-ran the same query three times in four seconds (12:47:22 / :24 / :26) and
   reported a defect that does not exist ("不知是否為 Issue: 再按一次 Search 才會刷新"). Cost: one
   round trip and one wasted run, for a feature that was correct the whole time. Generalised as the
   propagation half of [working-lessons.md](working-lessons.md) **§1.6** — writing a PASS's
   conditions beside the *number* is not enough when a second document owns the *procedure*.
2. **V6 — the search highlight survives a Refresh. 🟡 HALF-VERIFIED: manual PASS 2026-08-17, the
   auto-refresh half NOT OBSERVED and BLOCKED.** Live Walker → type a field-search keyword →
   press Refresh (and leave auto-refresh on for a few ticks). Highlights must stay, the ↑/↓ stepper
   must still land on highlighted rows, and **the grid must not jump to the top** — that last one is
   what the fix deliberately avoided by not re-using `ApplySearch`.
   ✅ **The manual-Refresh half PASSED 2026-08-17** — evidence in the `[GRP4-UI-2026-08-17]` block
   below, and it is a complete check of that half.
   ⛔ **The auto-refresh half is NOT OBSERVED, and the 2026-08-17 run could not have observed it.**
   That session ran on **dist 1.0.0.3262**, the build `[AUTOREFRESH-2026-08-19]` proves had a
   **frozen** Live Walker countdown: the countdown's only reset sat *past* `OnAutoRefreshTick`'s
   early-return guard, so one skipped tick pinned the label at `0s` forever while `Auto` still read
   ON — and the log analysis on that build measured **zero** auto-refreshes across a logged session
   ("no periodic cadence exists anywhere in the session"; every `walk_instance` in its 21-minute
   Elliot half maps 1:1 to a user action). So "leave auto-refresh on for a few ticks" was **physically
   unperformable** when the PASS was recorded, and the evidence block correspondingly says
   *"Pressed **Refresh**"* / *"Pressed the **▼ stepper**"* and never mentions Auto at all.
   ▶ **V6 stays OPEN for this half only**, blocked until `[AUTOREFRESH-2026-08-19]`'s fix reaches a
   **published** build (the other PC is still on 3262). Re-run: keyword → tick **Auto** → let it
   cycle 2-3 periods with no manual press → highlights, stepper target and scroll anchor must all
   survive a refresh the *timer* caused.
   📌 **This is why the record is corrected rather than deleted.** The PASS was not wrong about what
   it saw; it was wrong about what it *covered*. Same family as A5's 📌 above — there the conditions
   never reached the step, here a step's precondition was never checked against the build under test.
   **A procedure that names a behaviour the build cannot perform does not fail; it silently passes on
   the half that works.** Before recording a PASS, check that every clause of the step was runnable.
3. **AE9 — New Scan resets the Sort picker. ✅ VERIFIED 2026-08-17** (both halves; evidence in the
   `[GRP4-UI-2026-08-17]` block below, and its 繁中 step is deleted per close-then-delete).
   Value Search → First Scan → sort by Value → New Scan.
   The picker must read *"Scan order"*, and picking *"Value"* again must actually re-sort.
4. **U8 — `FName::Number` is back.** Live Walker a `NameProperty` whose value has a numeric suffix
   (`Slot_1`, `Slot_2`). Panel and Value Search must agree on the same 8 bytes. ⚠ Object/instance
   NAMES are a separate, unfixed lead — do not read a truncated instance name as a failure here.

> ### ✅ A5 · AE9 PASS · 🟡 V6 HALF-PASS 2026-08-17 `[GRP4-UI-2026-08-17]` — DumperTest Development, 3262
>
> ⚠ **Corrected 2026-08-19: this block originally read "A5 · V6 · AE9 all PASS".** A5 and AE9 are
> unaffected. **V6's evidence covers the manual-Refresh half only** — the auto-refresh half its step
> prescribes was unperformable on 3262 (`[AUTOREFRESH-2026-08-19]`), so the ✅ was broader than what
> was seen. Nothing observed here is withdrawn; only the scope of the claim is.
>
> **A5 — the live half AND the honesty half, on one screen.** Property Search `TickCount`:
> `DumperTestActor.TickCount` (IntProperty, `0x6A8`) previewed **279**, and a re-search ~38 s later
> previewed **317**. The sample's HUD drives TickCount at 1 Hz, so +38 in 38 s is the value *moving*,
> not merely looking plausible — that is what makes it a live reading rather than a Blueprint default.
> In the same result set, `NiagaraComponent.WarmupTickCount` and `NiagaraSystem.WarmupTickCount` read
> **`0 (CDO default)`**, i.e. classes with no live instance are marked instead of silently presented
> as live. Both halves of the fix, together.
>
> ⚠ **Lead, not filed as a defect: DEEP rows get no preview at all.** A `CurrentValue` deep search
> returned 5 rows (`DumperTestActor.Health.CurrentValue` @ `0x698` among them) and **every Preview
> cell was empty** — not a value, not `(CDO default)`. Same for `NiagaraSimCache.CacheFrames[]…`.
> `Aura.cpp`'s `(CDO default)` marker is only appended `if (!m.preview.empty())`, so an empty preview
> is upstream of it, in `Ubel::ResolvePropertyPreviews` not resolving struct/container-nested paths.
> A5's own wording does not cover deep rows, so this is a gap to confirm, not a failure of A5.
>
> **AE9 — both halves.** Value Search → First Scan (`424242`, 2 candidates in 52 ms) → Sort picker set
> to `Value` → **New Scan** → the picker reads **`Scan order`** again and the session ends. Then, on a
> result set with *varied* values (`Bigger` 400000 → **14,813 candidates**), picking `Value` re-sorted
> the whole set ascending — first rows went from `225000000, 1023969488, 549755813888…` (scan order)
> to `424242, 424242, 480256×4, 524288…`. Note the `Exact` predicate returns identical values and
> therefore **cannot** test a re-sort; that is why `Bigger` was used.
>
> **V6 — all three claims, ACROSS A MANUAL REFRESH ONLY.** Live Walker on `DumperTestActor_0`
> (reached via a Value Search row's `Live` button, which correctly scrolled to and selected
> `FrozenInt`): typed `Flag` → `3 matches`, `bFlagA` highlighted. Pressed **Refresh** → the keyword
> and `3 matches` survive, and the grid stays at the same region (`0x478…0x658`) instead of jumping
> to the top. Pressed the **▼ stepper** → it scrolled to and selected `bFlagA` at `0x670`.
> Highlights, anchor and stepper all survive a **user-pressed** refresh.
>
> ⛔ **What this does NOT cover, added 2026-08-19.** Every action above is a button press — the word
> *Auto* appears nowhere in this evidence. V6's step also says *"leave auto-refresh on for a few
> ticks"*, and on this build (**3262**) that was impossible: `[AUTOREFRESH-2026-08-19]` proves the
> countdown froze at `0s` after a single skipped tick and that **zero** auto-refreshes were issued
> in a logged session on it ("no periodic cadence exists anywhere in the session"). ⚠ *That session
> was a different machine on the same build, not a re-run of this one — the transferable fact is the
> code reading, which the `[AUTOREFRESH-2026-08-19]` entry confirms was byte-identical here.* The
> timer-driven path therefore remains **unverified** — see V6's
> corrected entry above. The manual half stands exactly as recorded.
>
> **Dump Explorer cross-game identity gate — PASS, on the two DumperTest flavours.** §8 promoted this
> row on the grounds that the gate compares main-module names and the two packages differ; that is
> now confirmed end to end. Dumped from **Development** (`Export → Dump All Metadata`, 3,942 classes /
> 10.5 MB, meta line records `module: "DumperTest.exe"`, `pe_hash 6A7EA60310F17000`), then the game
> was swapped for **Shipping** (`UE504`, **24,445** objects vs Development's 25,179 — a genuinely
> different binary) and the dump loaded:
>
> ```
> UE 5.4 · DumperTest.exe · 3,942 classes · 68,637 props · 9,806 funcs · 2026-08-17T22:22:03Z
> Live match refused: this dump is from DumperTest.exe, but the connected game is DumperTest-Win64-Shipping.exe.
> ✅ In current game — (run Re-check live)        <- EMPTY
> ⚠ Not checked yet — showing 2,000 of 82,385    <- everything
> ```
>
> Three things make it a pass rather than a shrug: the refusal **names both modules**, the
> *In current game* list is **empty** so nothing is falsely claimed present, and the 82,385 rows are
> labelled **"Not checked yet"** rather than *"Not in current game"* — refusing to match is not the
> same claim as absence, and the panel keeps them apart. `Re-check live` stays enabled as the
> deliberate override.
>
> **AF4 — PASS, and this one has no unit test by design.** Live Walker on `DumperTestActor_0` →
> switch to **Instances** → switch back. The object, the scroll region and the selection all survive.
> Then the real check, chosen so a dead callback cannot hide: search `Text_` (**8 matches**, and the
> `Text_*` fields live at `0x2A0…0x310`, far *above* the `0x4C8+` region on screen) and press the
> **▼ stepper** — the grid scrolls all the way up and selects `0x2A0 Text_Even2_OneNull`. Before the
> fix all six callbacks were dead after one round trip **and nothing errored**; a stepper that only
> had to move a few rows could not have told the difference, which is why the keyword was picked to
> force a long scroll.
>
> ⭐ **Free corroboration of the SDK-header export, from a different code path.** Every offset the
> Live Walker shows on this actor matches the exported header exactly — `0x639 U8_Small`,
> `0x63C I16`, `0x640 I32`, `0x650 F32`, `0x658 F64`, `0x670 bFlagA/bFlagB/bFlagC` (bits 0/1/2, masks
> 0x01/0x02/0x04, byte `05`), `0x671 bPlainBool`, `0x672 Grade`, `0x694 Health`, `0x6A8 TickCount`,
> `0x6AC FrozenInt`. Two independent emitters agreeing on the whole layout is stronger evidence for
> W2/W3 than either alone. *(Incidental for AA1: the bitfield byte currently reads `0x05`, the
> pre-toggle state that check expects to become `0x07`.)*

**Needs a specific condition (worth doing when it arises):**

5. **G1 + X3 — the offset banner.** On a game where offset detection partially fails, the Pointers
   tab must show the amber *"Dynamic offsets only partially measured (unmeasured:…)"* banner naming
   the probe. The pair is only observable together. ⚠ On a game where everything measures cleanly
   the correct result is **no banner at all** — absence proves nothing unless `get_offsets` on the
   same process reports `validated: true`, so check that too before concluding.
   **Host screening `[DQ7R-PIPE-2026-08-17]`: DQ7R is the NEGATIVE-control branch, not the positive
   one.** `get_offsets` reports `validated: true`, `probe_ran: true`, and emits **no** `unmeasured`
   key at all, so the only thing DQ7R can establish here is that a clean game shows no banner. **The
   amber half still has no host** — it needs a title whose offset detection *partially* fails, and
   screening for one is a single `get_offsets` call per title, so fold it into any future sweep rather
   than launching for it.
> ### ✅ V7 · AF6 PASS 2026-08-17 `[GRP4-UI-2026-08-17]`
>
> **V7 — the salmon line appears, forced the way the plan prescribes.** Live Walker on GWorld
> (`UWorld ThirdPersonMap`), then the **game process was suspended** from Python (`NtSuspendProcess`)
> rather than an actor destroyed — suspending leaves the object graph intact and stops only the DLL
> answering, which is precisely the "refresh could not complete" case. Pressing **Refresh** produced,
> under the status line and in salmon:
> ```
> Refresh timed out after 10s — the target object may have been destroyed in-game.
> ```
> Ten-second deadline, visible failure, and the grid kept its previous contents rather than blanking.
> The process was resumed immediately afterwards. Before this fix a dead refresh looked exactly like a
> live one.
>
> **AB6 — PASS, on a deliberately leaf-heavy set.** Group Scan `Exact 0` on both slots →
> **1,655 matching objects in 196 ms**, with slots keeping many leaves (`(+63)`, `(+89)`, `(+90)`,
> `(+91)`). Sorting by **First value** reordered the rows so the *rendered* first-slot values ascend
> monotonically: `-1`, `-0.00000000000111392`, `…110971`, `…110953`, `…110926`, `…110204`, … The sort
> follows the leaf that is actually **on screen**, not some other leaf the slot kept — which is only a
> meaningful check because each slot is holding ~63 of them.
>
> **AE8 — PASS, and both halves are visible.** A First Scan with an empty Value box is **rejected
> with an inline `Value is required.`** (red, box outlined) rather than silently ignored — and the
> **`Diagnostics — DLL dispatch cost`** table went 38 → 40 dispatches across the attempt, the two new
> entries being `get_diagnostics` and `end_group_scan`, both of which the operator caused. **No scan
> command appears for the rejected click**: it never reached the pipe, so it was never measured.
>
> *Two things fell out of the same panel.* The header reads `40 dispatches over 860.4s — dispatcher
> busy **0.2%** of wall-clock`, consistent with [multipipe-eval](multipipe-eval.md) §10's measured
> finding that the dispatcher is mostly idle and there is no head-of-line blocking to remove. And the
> **Pipe Activity** tail independently corroborates **V7**: `07:20:58.618 B → walk_world #6` is sent
> with **no matching `←` reply** — the very refresh that timed out while the process was suspended.
>
> **AF6 — PASS, on the evidence Y9 already produced.** Its ask is "a huge integer into Force → an
> explicit refusal *naming the substitute*, not a silent nothing", and `9999` into Force on a
> `ByteProperty` answers **`uint8 holds 0 to 255 — 9999 would be written as 15`**. The substitute is
> named and the value is not written. No separate run needed.

6. **V7 — failures are visible.** Live Walker an object, then destroy/unload it in-game and press
   Refresh. Expect the salmon error line under the status line (10 s timeout). Before this fix a
   dead refresh looked exactly like a live one.
7. **U7 — a CJK string preview.** Property Search a `StrProperty` holding non-ASCII text longer than
   50 bytes on a localized game. Before the fix the whole search returned zero rows with an error;
   success = rows come back and the preview ends in `…`.
8. **AB6 — group sort follows the visible column.** Group Scan with a filter that makes a slot keep
   many leaves, then sort by Value. The order must match the Value column on screen.

9. **AF4 — the Live Walker survives a tab round trip.** This one has NO unit test by design (it is
   an Avalonia visual-tree lifecycle fact). Open Live Walker on an object → switch to another tab →
   switch back → then use **🌍 Locate in GWorld**, a bookmark restore, or the ↑/↓ match stepper.
   The grid must still scroll. Before the fix all six callbacks were dead after one round trip and
   nothing errored — the buttons just stopped moving the view.
10. **AF2 — unchecked rows say so.** Experimental → Detect Player Stats on a game with more than 30
    candidate classes. Rows past the cap must read **"? not checked"** in amber, not "· guess", and
    the status line must say *"30 of N classes live-probed"*. On a small game with under 30 classes
    the correct result is that the suffix is ABSENT — check both, or you have only tested one branch.

**Trivially checkable, low value alone:** AF6 (type a huge integer into Force → expect an explicit
refusal naming the substitute, NOT a silent nothing), AE8 (a rejected scan click should no longer
appear in the diagnostics measurement list), AF1 (needs a malformed UEnum — not reproducible on
demand), U7's sibling paths.

### ✅ 5-of-5 CLOSED 2026-08-18 — install the plugin into a REAL Cheat Engine (audit #5 AB1, build 2913)

We were crashing CE by leaving a 1 ms-poll thread running in an image CE unloads. The fix stops
creating threads in a CE host and pins the module elsewhere. **The unload paths were read out of CE's
published 7.5 source; the shipping binary is 7.7.0.10568, and nobody has run this.**

This is the one verification in the register that needs **no game at all**.

1. Copy `dist/UE5Dumper.dll` into Cheat Engine's `plugins\` folder (or anywhere), open CE →
   **Settings → Plugins → Add** and select it. **Before the fix this is the crash.** Success = the
   dialog accepts it and CE is still running.
2. Tick the plugin to enable it, then **close CE normally**. Success = clean exit — and re-open CE and
   confirm your settings survived, since the unload runs *before* CE writes them.
3. Check `%LOCALAPPDATA%\UE5CEDumper\Logs` for the new line
   *"host is Cheat Engine — NOT starting the mailbox poller or the auto-start thread"*. Its absence
   means the guard did not fire and the rest of the check proves nothing.
4. **Then prove the fix did not break the feature**: with the plugin enabled, use its
   *"UE5CEDumper: Inject & Connect"* menu item against a running game and confirm the DLL still
   injects, the pipe opens, and the CE Lua mailbox works. The poller is *supposed* to run in the
   game — only CE's own process is refused.

   **⚠ This step also verifies AB2 (build 2932), so do it deliberately.** `UE5_AutoStart` now spawns
   and returns instead of running the scan on CE's remote thread, which CE frees after a hard 10 s
   (`CEFuncProc.pas:1346-1360`) or, with Settings' **`cbInjectDLLWithAPC`** ticked, after 1 s
   (`:1332-1343`) — the `ret` onto that freed page crashed the **game**. Measured async
   (`py tools/probe_autostart_async.py` → 2.3 ms, vs 3486 ms with the spawn reverted), but never run
   against a real CE + game. Check:
   - The menu item returns **immediately** and the dialog says the scan started *in the background*;
     CE's own window should not freeze for the scan any more.
   - The game does **not** crash a few seconds later — that was the AB2 symptom.
   - **Tick `cbInjectDLLWithAPC` in CE's Settings and repeat.** That is the near-certain-crash path
     before the fix and the strongest single check here.
   - The dialog now reports what it **observed** (is our module mapped?) rather than CE's `InjectDLL`
     BOOL, which is inverted for the common failures. A "success" dialog must mean the pipe really
     comes up; an "injection failed" dialog must mean the module really is absent.
5. Worth one negative case: a game whose folder is named e.g. `...\Cheat Engine 7.7\Game.exe` must
   still get its poller. Only the executable leaf is tested, and there is a unit test for it.

> ### ✅ STEPS 1-3 PASS `[CE-PLUGIN-2026-08-18]` — the crash is gone, on the SHIPPING 7.7 binary
>
> **Cheat Engine 7.7.0.10568**, DLL build **1.0.0.3262**, **no game involved** (as the row promises).
> The DLL was staged at `out\ce-plugin-test\UE5Dumper.dll` rather than pointed at `dist\` on purpose:
> once CE loads it the file is locked, and a later `-Mode Publish` would fail to overwrite.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | Settings → Plugins → **Add new** → selected the DLL. The dialog accepted it and the list gained `UE5Dumper.dll:UE5CEDumper`. **CE's PID was 34984 before the Add and 34984 after** — same process, so it neither crashed nor silently restarted. This is the exact operation the finding says used to take CE down |
> | 2 | ✅ **PASS, all three halves** | Ticked it → OK: CE's menu bar gained a **`Plugins`** menu, i.e. the CE entry points ran. **Closed CE normally → clean exit**: process gone, and `Get-WinEvent` over the Application log for the preceding 10 minutes returned **no** `cheatengine` entry. **Settings survived**: `HKCU\Software\Cheat Engine\Plugins64` gained `00000002 A = …\UE5Dumper.dll`, `00000002 B = 1` — written *at exit*, which is the point, since the unload runs before CE writes them. **Re-opened** (new PID 36608) → the plugin auto-loaded enabled and logged `CEPlugin: InitializePlugin pluginid=2 menuItemId=1 ef_size=1272` |
> | 3 | ✅ **PASS** | A `Logs\cheatengine-x86_64\` folder appeared, and `init-0.log` carries the guard verbatim: `[WARN] [INIT] DllMain: host is Cheat Engine — NOT starting the mailbox poller or the auto-start thread. CE FreeLibrary's plugin DLLs (on Settings→Add and on exit), and a thread left running in an unmapped image takes CE down with it. …` It fired on **both** loads (the Add, and again on the re-open) |
> | 4 | ✅ **PASS on the injection half; the APC half is UNREACHABLE — see below** | Elliot, **proxy temporarily moved aside** so the injection is genuine (see the trap below). `Plugins → UE5CEDumper: Inject & Connect` → dialog inside 3 s: *"DLL injected — GObjects/GNames scan started in the background."* **AB2's async is measured, not assumed**: `Injecting into PID=12780` 17:05:54.151 → `InjectDLL returned` .248 (**97 ms**), while the scan only finished at **17:05:58.596** — 4.4 s later, i.e. well past the 1 s APC / 10 s normal window CE frees the stub in. **Game did not crash**: alive and `Responding: True` **6 minutes** later, no Application event-log entry. **Pipe opened** (`get_pointers` returned live pointers). **Mailbox poller started IN THE GAME** (`Mailbox: polling thread started (poll=1ms)`) while CE's own log carries the refusal — the exact contrast AB1 is about. **CE Lua reaches it**: `g_invokeMailbox = 7FFE944CC610` |
> | 5 | ✅ **PASS, with a discriminating control** | Two hosts, **same DLL, same injector, same minute**, differing only in the executable leaf. **A** = `out\Cheat Engine 7.7\Game.exe` → `Mailbox: polling thread started (poll=1ms)` and **no** guard line, i.e. a folder literally named *Cheat Engine 7.7* does **not** cost the poller. **B (control)** = `out\plainhost\cheatengine-x86_64.exe` → the guard fired (`DllMain: host is Cheat Engine — NOT starting the mailbox poller…`) and **zero** poller starts. Both hosts were copies of `cmd.exe`, so B also shows the match is on the **name**, not on really being CE |
>
> ### ⭐ The BOOL-vs-observation fix, demonstrated live — this is the strongest single result here
>
> CE's `InjectDLL` returned **FALSE**, and the DLL was **mapped and working anyway**:
> ```
> CEPlugin: InjectDLL returned FALSE
> CEPlugin: post-inject module check: D:\…\out\ce-plugin-test\UE5Dumper.dll (ok=0)
> ```
> ⚠ **Read that second line carefully — it is easy to get backwards.** `ok=` is CE's BOOL (0 = FALSE);
> the `%s` slot prints the **found path** when present and the literal `NOT PRESENT` when not. So this
> says *module IS there, CE said it failed*. The dialog reported success because it trusts its own
> module walk, which is precisely the inversion step 4 asks about — and the pipe really did come up,
> so the "success" dialog was honest.
>
> ### ⛔ THE APC HALF CANNOT BE RUN ON A PUBLIC CHEAT ENGINE — it needs a private build
>
> Step 4 calls ticking `cbInjectDLLWithAPC` *"the strongest single check here"*. It is **not reachable
> on the shipping binary**, and this is not a UI-hunting failure — two independent signals:
> * **Source** (`D:\Github\cheat-engine`, tag 7.5): `formsettingsunit.pas` guards
>   `cbInjectDLLWithAPC.visible := true` with `{$ifdef privatebuild}`, and `MainUnit2.pas` reads
>   `useapctoinjectdll` from the registry **inside the same ifdef** — its `{$else}` branch hardcodes
>   `useapctoinjectdll := false`. So on a public build the checkbox is hidden **and** the flag is
>   forced off; **setting the registry value achieves nothing.**
> * **Observation**: the checkbox is absent from 7.7.0.10568's Settings (General Settings and Extra
>   both checked).
>
> ▶ **Rewrite the step**: the APC path needs a `privatebuild` Cheat Engine. Everything else in step 4
> is done. (⚠ Source is 7.5 while the binary is 7.7 — but the two signals agree, and the doc's own
> rule is that the public source lags the release, not that it invents ifdefs.)
>
> ### ⚠ Two traps for whoever re-runs this
>
> 1. **A deployed proxy makes step 4 vacuous.** `Methode.cpp` checks `IsAlreadyLoadedInTarget` *before*
>    injecting and bails with *"UE5CEDumper is already loaded in this process as '…'"* — its comment
>    even names the proxy case. Elliot ships `dxgi.dll`, so the menu would never reach `InjectDLL`.
>    It was moved to `dxgi.dll.ab1-bak` for the run and **restored afterwards**.
> 2. **CE attached BEFORE the injection has a stale symbol list.** `getAddressSafe('g_invokeMailbox')`
>    returned **nil** until `reinitializeSymbolhandler()`, after which it resolved. With a proxy the
>    DLL is present before CE attaches, which is why the earlier invoke rows resolved it immediately.
>
> ### ⭐ Why step 5 needed a control, and the staging that made it cheap
>
> `Grimoire.h:441` `HostAllowsBackgroundThreads` takes the **full** host path and `IsCheatEngineExeName`
> matches a **prefix of the LEAF** — so "path contains Cheat Engine" and "exe is named cheatengine*"
> are different questions, and only a **pair** of hosts separates them. Host A alone could pass simply
> because the guard never fires for anything; host B is what shows the check can fail.
>
> ⚠ **Staging note for a re-run: `notepad.exe` and `charmap.exe` from System32 DO NOT WORK as hosts.**
> Copied elsewhere they exit immediately (Notepad is a Store stub). `cmd.exe` copied to the target name
> and launched as `Start-Process … -ArgumentList '/k','timeout /t 900'` stays alive and is enough — the
> guard only reads the host path, so the host does not need to be a UE game at all. The UE scan then
> fails in that host, which is expected and irrelevant to this step.
>
> **State left exactly as found**, verified against a baseline captured before the run: the plugin was
> deleted, and `Plugins64` is byte-identical to `out\ce-plugin-test\plugins64-before.txt`
> (`AOBMaker_CEPlugin.dll` = 1, `CE-Handwire.dll` = 0, nothing else). CE exited cleanly a **second**
> time on the way out, with the plugin being removed — an incidental repeat of step 2's unload path.


### ⬜ NEW 2026-08-15 — 🌍 Locate-in-GWorld on a game where the AOB scan does NOT resolve &GWorld (audit #5 AE10, build 2961)

The 🌍 buttons were gated on the client `IsGWorldAvailable` flag, which is really *"the AOB scan
produced a &GWorld slot address"* — not *"a live UWorld exists"*. The DLL has world-recovery
fallbacks that work when that scan did not, so the gate **disabled the button on games where locate
worked**. All 19 gates are gone and the flag is deleted; the DLL now decides.

**The payoff case is a game where GWorld did NOT resolve by AOB** — the Pointers panel shows no
GWorld address, or the game runs in proxy mode (TQ2 is the recorded example). Nothing in the test
suite can reach this.

1. On such a game, the per-row **🌍** buttons must now be **enabled** in Instance Finder, Interesting
   Functions, Interesting Properties, Detect Stats, Class Pivot, Snapshot (Diff + Group) and SPC
   Query. Before this build they were greyed out with no explanation.
2. Click one. **Success = a path is found, or a clear "no path"/"invalid" message** from the DLL.
   Silence is a failure — the whole point is that a click now says something either way.
3. **Then the negative case**: on a game with genuinely no live UWorld (main menu before a level
   loads), the click must report the DLL's invalid/no-path status rather than appearing to work.
4. Regression check on a normal game where GWorld *does* resolve: the 🌍 handoffs still behave as
   before — this change should be invisible there.

### ⬜ NEW 2026-08-15 — keep a freeze running across deaths/respawns (audit #5 AA2/AA3, build 2926)

The freeze tick used to write to cached pointers guarded only by "is qword 0 non-zero", which a
recycled or pooled block passes — so between two rescans it could write into an object of a
different class. It now re-reads `ClassPrivate` before every write and refuses a foreign class, using
a `(UClass*, offset)` witness the DLL publishes on `CMD_LIST_INSTANCES` (**mailbox contract 1 → 2**).

**The behaviour is covered by an executable harness** (`lua scripts/tests/freeze_helper_test.lua`,
23 checks, negative-controlled one break at a time), so what is unproven here is the *live* half:
that a real game's `CMD_LIST_INSTANCES` fills the witness and that the guard does not reject valid
instances.

1. **Contract first.** With an **old** DLL injected and a freshly-injected helper, the freeze must
   refuse with *"the DLL is older than this script"*. If it runs anyway, the contract check is not
   firing and nothing below means anything.
2. With the new DLL: start a class-wide freeze on something with many live instances (enemies, pickups).
   **Success = the value actually holds.** A silently-refusing guard looks exactly like a freeze that
   does nothing — that is the main risk of this change, and it fails in that direction by design.
3. Check `init-0.log` for `LIST_INSTANCES ... classWitness=0x...`. **A zero witness means the guard
   fell back** and the fix is inert.
4. **Now cause churn**: kill/respawn the frozen actors, or cross a level-streaming boundary, with the
   freeze still enabled. Success = the freeze re-acquires within one rescan (~5 s) and nothing
   unrelated changes. Watch for any *other* object's fields changing — that is the old bug.
5. **AA3**: with the freeze running, unload/re-inject the DLL so rescans fail permanently. Expect the
   Lua console to print `... consecutive rescans failed -- freeze STOPPED writing` **once** within
   ~15 s, and no further writes.

### ✅ DONE 2026-08-18 — freeze a PACKED bitfield bool and check its 7 siblings survive (audit #5 AA1, build 2922)

> **Ran on DumperTest Development, dist 3262, CE 7.7 attached.** All five steps pass, and the
> DLL→UI half the box was waiting on is now witnessed: a real packed bool's mask reached the UI.
>
> | step | result |
> |---|---|
> | 1 | Property Search `bAlwaysRelevant` → **`Actor / BoolProperty / 0x58 / size 1`**. Row's **Freeze** button (visible only after collapsing the Object Tree — it lives in a per-row cell, not the toolbar) → dialog reads `Type: BoolProperty -> bool`, `Offset: 0x58`, value pre-filled **`true`**, hint *"Accepts: true / false / 1 / 0"*. → `Freeze script created in CE: Freeze: Actor::bAlwaysRelevant = true`. |
> | 2 | ✅ **The mask arrives.** Generated CFG: `boolMask = 0x08,  -- packed bitfield: only this bit is written`. `0x08` = bit 3, which is `bAlwaysRelevant`'s bit as Live Walker independently reports it. |
> | 3–5 | ✅ **Only the masked bit ever moves**, shown by *two* transitions rather than one baseline — the freeze had already been running, so a single before/after could not prove the neighbours predated it. Editing the CFG's `value` and re-arming gave, at `ChaosDebugDrawActor+0x58` (read with `tools/verify/read_mem.py`, not from a panel): **`0x6A` → (false) `0x62` → (true) `0x6A`**. `b1`, `b5`, `b6` are set throughout and never move; only `b3` follows the frozen value. The pre-fix whole-byte write produces `0x01`/`0x00`, and step 5's specific trap — a non-`0x01` mask leaving the target bool unset — is excluded because `b3` tracks the value in both directions. |
>
> ⚠ **Which instance it held is the incidental finding below** — not `DumperTestActor_0`.

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[FREEZESCOPE-2026-08-18]` — Freeze must hold the subclasses too

*Needs a game with a **player pawn** and any inherited `AActor` bool (`bCanBeDamaged`, `bHidden`,
`bReplicates`) — i.e. any UE game. Runs in the same sitting as `[FREEZESTUCK-2026-08-18]` above.*

> **What is already pinned offline and must NOT be re-checked here:** 11 executable scope cases in
> `scripts/tests/freeze_helper_test.lua` (derived is the default; `derived = false` is honoured; the
> 16-byte page stride, with a negative control that an 8-byte read would return a class pointer as
> an address; per-entry identity witnesses; a witness-less entry dropped rather than written blind;
> a missing `ClassPrivate` offset refused rather than degraded; a filter dropping address and
> witness in lockstep; cap reported, and a control that an uncapped pool does not claim to be
> capped; a contract-2 DLL refused). Plus `dll_helpers_test`'s page-geometry + contract-3 layout
> assertions and `FreezeValueDialogValidationTests`' scope-summary/warning pair.
> **What no offline test can reach** is whether a real `Aura::FindInstancesDerivedFrom` sweep
> reaches the user's pawn — that is step 4.
>
> ⚠ **DLL and UI must be from the same build.** This moved the mailbox contract to **3**; a
> contract-2 DLL is refused up front with *"update UE5Dumper.dll"*, which is a correct answer, not
> a defect. See `[STALEDLL-2026-08-18]` for the February DLL that can be picked up instead.
>
> ⚠ Read the checkbox correctly (red ✗ = ACTIVE) and open CE's Lua Engine first, as above.
>
> | step | do this | expect |
> |---|---|---|
> | 1 | Property Search `bCanBeDamaged` (or `bHidden`) — a field the Class column shows as **`Actor`** with an `+N inheritors` badge | the row exists |
> | 2 | Click **Freeze** and read the dialog before typing anything | a **Scope:** line reading `every live Actor and every subclass (N inherit this field)`, plus a ⚠ line saying the field is declared on `Actor` and how to narrow it. Neither existed before |
> | 3 | Create the script and read the generated CFG | it contains `derived            = true,` |
> | 4 ⚠ THE ONE THAT MATTERS | tick the record, then check `Logs\<Game>\pipe-0.log` | `LIST_INSTANCES class='Actor' page=0 scope=derived` and a **returned count in the hundreds/thousands**, not `1/1`. Before the fix this was `1/1` in a 25,179-object level |
> | 5 | with `bCanBeDamaged` frozen to `false`, take damage on the **player pawn** | the pawn is unharmed. Pre-fix the freeze held one incidental `ChaosDebugDrawActor` and the pawn died normally — that is the whole finding |
> | 6 ⚠ the honesty half | if the log line ends in `CAPPED`, read the Lua Engine | it printed `CAP REACHED, so that is a floor, not a total: more instances exist and are NOT held`, and the Lua Engine window **stayed open** instead of auto-closing over the notice |
> | 7 ⚠ control | edit the CFG to `derived = false`, re-tick | `scope=exact` in the log and the old narrow pool returns — the flag is a real switch, not decoration |
> | 8 ⚠ control, backward compatibility | tick an **older saved .CT** whose freeze script predates contract 3 | it still runs and still holds its exact-class pool. The flag defaults off and the handler clears it, so an old script must be unaffected |
> | 9 ⚠ control, cross-feature | on the same row, use **Force ON/OFF** (Solide) | it reports a comparable instance count to step 4 — Force and Freeze sit on one row and must not scope oppositely, which is what started this |

### ⬜ SUPERSEDED — original AA1 steps, kept for the method

Sibling of the Y15 check below, same panel, same failure shape — a whole-byte write over a field that
does not own the whole byte. Freezing a `BoolProperty` now emits `boolMask` into the generated CFG and
the helper writes only that bit. 24 unit tests plus a negative control cover the C# and the helper
*source*, **but the DLL→UI half has never run against a real game**: nobody has seen a real packed
bool's `bool_mask` arrive on the `search_properties` wire.

**Needs a game with a `uint8 bFoo:1` bitfield bool** — extremely common on `AActor`
(`bHidden`, `bReplicates`, `bCanBeDamaged` are bitfields on many UE versions), so any UE game should do.

1. Property Search a bool on a live class. In the row, generate the freeze script.
2. **Read the generated CFG.** A packed bool must show `boolMask = 0xNN,` (one of 0x01…0x80). Its
   *absence* is the whole finding, so this line is the check — if it is missing, the mask is not
   reaching the UI and everything below is moot. A **native** bool correctly shows no `boolMask`;
   confirm you are looking at a packed one (Live Walker shows the mask in the field's tooltip/CSX
   description).
3. Note the **whole byte** at `prop_offset` in Live Walker / CE before enabling.
4. Enable the script, let it tick, and re-read that byte. Success = only the masked bit changed;
   **failure = the byte became `0x00` or `0x01`**, which is the pre-fix behaviour.
5. The nastiest half of the old bug: when the mask is **not** `0x01`, the intended bool was never set
   at all. So also confirm the target bool actually reads as the value you froze.

### ✅ DONE 2026-08-18 — freeze a 1-byte enum and check its neighbours survive (audit #5 Y15, build 2904)

> **Ran on DumperTest Development, dist 3262.** Steps 1–5 pass; **step 6 not run** (needs a 4-byte
> `enum`; every EnumProperty reachable here reports size 1).
>
> **Target choice is the whole result, so it is recorded first.** The obvious candidate —
> `Actor::PhysicsReplicationMode` @`0x17C` — reads `00 00 00 00` with its three neighbours, and on
> **all-zero neighbours a 4-byte write is indistinguishable from a 1-byte one**: freezing to 3 gives
> `03 00 00 00` either way. That probe can only return "pass" (working-lessons §1.10a). Rejected it
> and swept for an enum with a **non-zero** neighbour, which is what makes the read decisive:
>
> | offset | field | baseline |
> |---|---|---|
> | `0x5E` | `Actor::UpdateOverlapsMethodDuringLevelStreaming` (EnumProperty, size 1) | `00` |
> | `0x5F` | `Actor::DefaultUpdateOverlapsMethod` (EnumProperty, size 1) | **`02`** |
> | `0x60`, `0x61` | — | `00`, `00` |
>
> | step | result |
> |---|---|
> | 1 | Baseline at `ChaosDebugDrawActor+0x5E` = **`00 02 00 00`** (`tools/verify/read_mem.py`). |
> | 2 | ✅ Dialog reads **`Type: EnumProperty -> uint8`** — not `-> int32` — with the field labelled `Freeze value (uint8):` and pre-filled **`255`**, not `9999`. |
> | 3 | ✅ Entering `9999` produces exactly **`uint8 holds 0 to 255 — 9999 would be written as 15`** and **no script is created** (the dialog stays open). Re-entered `3` → `Freeze script created in CE: Freeze: Actor::UpdateOverlapsMethodDuringLevelStreaming = 3`. |
> | 4 | ✅ **`00 02 00 00` → `03 02 00 00`.** The enum took the value and `DefaultUpdateOverlapsMethod` **kept its `02`**. The pre-fix 4-byte `writeInteger(3)` writes `03 00 00 00` and silently resets a *named, adjacent enum property* — which is the damage the finding is about, now stated as a field name rather than "the following bytes". |
> | 5 | ✅ CFG: `propOffset = 0x5E`, **`valueType = 'uint8'`**, `value = 3`, and **no `boolMask`** (correct — the mask line is bool-only, cf. AA1 above). |
> | 6 | ⬜ **NOT RUN** — no 4-byte enum in this sample; skipped per the run plan. |
>
> ⚠ Same scope caveat as AA1: the record held `ChaosDebugDrawActor`, the only non-CDO exact-`Actor`
> instance — see `[FREEZESCOPE-2026-08-18]`.

### ⬜ SUPERSEDED — original Y15 steps, kept for the method

Freezing an `EnumProperty` now picks its writer from the width the engine reported instead of always
using a 4-byte `writeInteger`. The mapping and both call sites are unit-tested with four negative
controls, **but nobody has watched a real 1-byte enum freeze leave the following bytes alone** —
which is the actual damage the finding is about.

Needs any connected game with an `enum class : uint8` field (Property Search → type filter
`EnumProperty`; almost every UE game has several — states, stances, difficulty, team).

1. **Property Search** → type filter `EnumProperty` → pick a row. **Live Walker** the owning class
   and write down the values of the **three fields immediately after** it (or read the raw bytes at
   `offset+1..offset+3`). This is the baseline; without it the rest proves nothing.
2. Back in Property Search, **Freeze** that row. The dialog's **Type** line must read
   `EnumProperty -> uint8`, *not* `-> int32`, and the value box must pre-fill **`255`**, not `9999`.
   Those two are the only places the fix is visible before the script runs.
3. Type `9999` → expect *"uint8 holds 0 to 255 — 9999 would be written as 15"* (Y9's check now
   reaching enums). Correct it to a valid enum value and generate.
4. Enable the script in CE, let it tick a few seconds, then **re-read the three neighbouring fields
   from step 1. They must be unchanged.** Before this build they were overwritten 20x/sec.
5. Confirm the CE script's CFG line reads `valueType = 'uint8'`.
6. If the game has a **4-byte** enum (rarer — a plain `enum`, not `enum class : uint8`), repeat: it
   must still map to `int32`. That is the no-regression half; 4 is the one width the old code was
   right about.

### ✅ CLOSED 2026-08-17 `[Y9-UI-2026-08-17]` — freeze a byte-wide property and try to overflow it (audit #5 Y9, build 2895)

**All five steps PASS** on DumperTest Development, dist 3262, using `U8_Max` (`ByteProperty`,
`0x63A`), `F32` (`FloatProperty`, `0x650`) and `F64` (`DoubleProperty`, `0x658`).

| step | evidence |
|---|---|
| 1 | Dialog opens headed `Type: ByteProperty -> uint8` with **`Freeze value (uint8):` pre-filled `255`** — the width is named in the label, not just enforced |
| 2 | `9999` → inline error **`uint8 holds 0 to 255 — 9999 would be written as 15`**, verbatim, and the dialog **stays open**. (9999 mod 256 = 15, so the number in the message is the truth, not a placeholder) |
| 3 | `200` → `✓ Holding DumperTestActor::U8_Max = 200 on 1 instance(s).` The ordinary path is intact |
| 4 | **This is how 1–3 were run** — see the ⚠ below |
| 5 | `1e300` on `F32` → **`Too large for a 4-byte float (max ±3.4028235E+38) — it would be written as infinity`**; the *same* value on `F64` → **accepted**, `✓ Holding DumperTestActor::F64 = 1E+300`. The narrowing check did not leak into the 8-byte path |

⚠ **A precondition this checklist does not state, and it inverts steps 1–4.** The **Freeze button** is
bound `IsEnabled="{Binding IsAobMakerAvailable}"`
([PropertySearchPanel.axaml:294](../ui/UE5DumpUI/Views/PropertySearchPanel.axaml)), and the toolbar
read `AOBMaker Offline`, so that button is greyed and **steps 1–3 cannot be run through it without
the CE plugin installed** (GROUP 5). Everything above therefore went through **step 4's** route —
row context → *Force field (hold across instances)* → *Force value…* — which opens the *same*
`Freeze property value` dialog, exactly as step 4 says. So the dialog and its arithmetic are fully
verified; what remains unexercised is only the **Lua-helper consumer** reached from the button.
Rewrite the step order accordingly: Force first, button only once AOBMaker is up.

*Incidental, both confirming Solide end to end:* the **`Forced fields:` strip** appears with
`DumperTestActor U8_Max (1 held)` and a `Clear all` that empties it; and the float pre-fill is the
generic `9999.0`, i.e. the 255 pre-fill is specific to byte-width targets rather than a blanket
change.

### ⬜ Y9 — original checklist (kept for the steps)

The freeze / force value dialog now rejects values wider than the target property instead of letting
them wrap. The arithmetic is measured against the writers' own masking in unit tests, **but nobody
has run the dialog against a real property** — and the pre-fill change is only observable in the UI.

Needs any connected game with a `ByteProperty` (Property Search → `byte`, or any `bEnabled`-style
flag stored as one).

1. **Property Search** → find a `ByteProperty` row → **Freeze**. The value box must open pre-filled
   with **`255`**, not `9999`. That is the pre-fill half of the fix and nothing else surfaces it.
2. Type `9999` and press OK. Expect the inline error
   *"uint8 holds 0 to 255 — 9999 would be written as 15"*, and the dialog must **stay open**.
3. Correct it to `200`, confirm the script generates as before — the check must not have broken the
   ordinary path.
4. Repeat step 2 via the **Force** submenu on the same row (Property Search → row context → Force →
   value…), which reuses this dialog. Same error expected; that consumer is Solide, not the Lua
   helper.
5. Worth one **float** case: on a `FloatProperty`, `1e300` should now be refused with
   *"would be written as infinity"*, while the same value on a `DoubleProperty` must still be
   accepted. If the double case is refused, the narrowing check leaked into the 8-byte path.

### ✅ ALL 5 CLOSED 2026-08-18 — AA(B) / FIRE on a class past the 5,000-row cap (audit #5 X2, build 2888)

The three handoffs that need a class address stopped re-deriving it from the capped `list_classes`
page and now use the address the row already carries. The pure logic is unit-tested with three
negative controls, **but the end-to-end path is not**: no test issues a real `walk_functions` against
an address sourced from `list_all_functions`.

Needs a game with **more than 5,000 classes** — any large UE title (DQ7R, Hogwarts Legacy, FF7R).

1. **Game Class Filter → Load.** Confirm the status line ends with
   *"⚠ STOPPED at the 5,000-row cap — more classes exist"*. If it does not, this game is too small
   and the rest of the check proves nothing — pick another title.
2. **Interesting Funcs → Load**, then pick a row whose class is **absent** from the Game Class Filter
   list (that is what "past the cap" means; filter by the class name there to confirm the absence).
3. Click **AA(B)** on that row. Success = the script generates / reaches CE. Before this build it
   aborted on *"Class X not found"*.
4. Repeat on the **Console** tab with an exec command taking parameters (**Run** → the FIRE dialog)
   and with its own **AA(B)** — those two twins were not named by the finding and share the fix.
5. Worth one negative case: a class that genuinely does not exist should still report plainly
   *"not found"*, not the "may still exist" caveat. The Live-handoff path (no live instance +
   an unknown class name) is where that text appears.

> ### ⛔ STEP 1 SAYS STOP — **DQ7R IS TOO SMALL**, and this row's own candidate list is wrong
> `[DQ7R-2026-08-18]`
>
> Classes tab → Load, on **DQ7R** (UE 427, 199,196 objects, DLL 3262), both ways:
> * `Game classes only` **on**  → `2888 classes (scanned 199,196 objects, 2888 total UClasses)`
> * `Game classes only` **off** → `4738 classes (scanned 199,196 objects, 4738 total UClasses)`
>
> **4,738 < 5,000, so the cap is never reached and the status line carries no
> `⚠ STOPPED at the 5,000-row cap` at all.** Step 1 is explicit that this makes the rest of the row
> meaningless here, so nothing further was attempted on this title.
>
> ▶ **Correct the row's candidate list.** It names "DQ7R, Hogwarts Legacy, FF7R"; DQ7R does **not**
> qualify. A host that plausibly does, and is already known-good for driving: **Elliot** — its own
> `list_all_functions` status line this session read *"20239 functions across **6612 classes**"*, and
> classes-with-functions is a **lower bound** on total UClasses, so Elliot is ≥6,612 > 5,000.
> (Object counts point the same way: Elliot 355,717 vs DQ7R 199,196.) ⚠ That is a lower bound from a
> *different* command — confirm with the Classes tab's own total before relying on it.

> ### ✅ STEPS 1-3 PASS ON ELLIOT `[ELLIOT-X2-2026-08-18]` — the lower-bound inference held
>
> Elliot, UE **504**, 355,679 objects, `dxgi.dll` proxy build **3262**, AOBMaker **Connected**.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | Classes tab → Load → `5000 classes (scanned 355,679 objects, 5000 total UClasses)` **`⚠ STOPPED at the 5,000-row cap — more classes exist`** — verbatim. Confirms Elliot qualifies where DQ7R (4,738) does not |
> | 2 | ✅ **PASS** | Interesting Funcs → Load → `20235 functions across 6609 classes`. Took the top row's class **`BP_EnemyCharacter_C`** and filtered the *capped* Classes list by that exact name → **0 rows**. So it is in the function list and absent from the class page: past the cap by construction, not by assumption |
> | 3 | ✅ **PASS, end to end** | `AA(B)` on `BP_EnemyCharacter_C::SetBlockDispHPGauge` → the dialog **opened** (`ParmsSize=1`, `bBlock [bool, 1B, off=0]`) instead of aborting on *"Class … not found"*, and `Copy AA Script` **reached CE**: the record `Invoke (baked): BP_EnemyCharacter_C::SetBlockDispHPGauge` is in the address list. That is the whole finding — the handoff used the address the row carries rather than re-deriving it from the capped page |
> | 4 | ⬜ **NOT DECIDABLE ON ELLIOT — and the reason is measured** | The Console twin needs an `exec` command **on a past-cap class**. Elliot has **none**: with `Game Only` on, Console reports `No UFUNCTION(exec) commands found in this game (scanned 12,822 functions across 3,935 classes)`. All **94** exec commands it does find sit on engine classes (`CheatManager`, `AISystem`, `AbilitySystemCheatManagerExtension`), and `CheatManager` is **present** in the capped list (4 matching rows), i.e. inside the first 5,000. Running the twin here would pass **vacuously**. ▶ Needs a title with **>5,000 classes AND game-class exec commands** — check the Console tab's `Game Only` count before committing to a host |
> | 5 | ⬜ not run | The negative case (unknown class must say plainly *"not found"*, not the "may still exist" caveat) goes through the **Live handoff**, which was not staged this session |

> ### 🔁 SECOND SITTING `[ELLIOT-X2b-2026-08-18]` — step 4 CONFIRMED vacuous, step 5 is MIS-SPECIFIED
>
> Elliot, UE **504**, DLL **3262**, title screen (84,990 objects — smaller than the first sitting's
> 355,679, and it does not matter: `game_only=false` still returns **5,000 with `truncated=true`**,
> so the cap precondition holds).
>
> **Step 4 — ⛔ NOT DECIDABLE HERE, now confirmed by a SECOND, independent method.** The first
> sitting read the Console tab's counts; this one tested class membership over the pipe. Every
> exec-bearing class sits **inside** the capped page, by index:
> `CheatManager` **idx 2836**, `AISystem` **idx 1120**, `AbilitySystemCheatManagerExtension` **idx
> 3900** — all < 5,000. The controls agree in the other direction: `BP_EnemyCharacter_C` and
> `BP_PlayerCharacter_C` are **absent** from the page (that is why step 3 could use them).
> ⇒ The Console twin's fix only bites when the exec command's class is past the cap, so running it
> here would go **green while proving nothing**. Still needs a title with **>5,000 classes AND
> game-class exec commands**.
> ⚠ `list_all_functions` with `game_only=false` on this title **does not return** (killed at 10 min);
> the membership test above needs only `list_classes`, so prefer it.
>
> **Step 5 — ✅ the REACHABLE half passes, and the half the row asks for is UNREACHABLE BY DESIGN.**
> Staged exactly as the row describes: `WBP_DebugLevelJumpItem_C` is past the cap **and** has zero
> live instances (verified over the pipe first), so its `Live` button takes the no-live-instance
> path. The UI fell back to Class Struct and logged, verbatim:
> ```
> [WARN] InterestingFunctions navigate: WBP_DebugLevelJumpItem_C not in the class list
>        — it was CAPPED at 5,000 rows, so the class may still exist
> ```
> That is the **correct** answer — the class does exist, it is merely past the cap — and it is the
> branch build 2888 added. **But the row asks to see the OTHER branch, and two independent facts stop
> it:**
> 1. `ClassAddrLookup.MissReason` ([`Models/ClassListResult.cs:123`](../ui/UE5DumpUI/Models/ClassListResult.cs))
>    selects on **`Truncated`**, never on whether the class exists. On any game that hits the cap the
>    answer must be the caveat; asking for a plain *"not found"* there is asking the UI to claim
>    knowledge it does not have.
> 2. **A class that "genuinely does not exist" cannot reach this path at all.** All four call sites
>    (`MainWindowViewModel.cs` 1178 / 1232 / 1392 / 1775 — Interesting Funcs, Interesting Props,
>    Console/exec) take `className` from a **discovered row**, so the class always exists; and they
>    all call `ListClassesAsync(gameOnly: false)`, whose result on a **non**-truncating game is the
>    complete class list — in which that class is therefore present, so the miss branch does not fire
>    either. Dump Explorer, the one panel that knows about classes *not* in the game, hands off via
>    `NavigateToLiveWalker(addr)` / `NavigateToInstanceFinder(className)` and never touches
>    `FindClassAddr`.
> ⇒ `MissReason == "not found"` is reachable only in a narrow race (a class collected between the
> function-list and class-list calls). **It is not stageable deliberately, and the row's own note
> that the pure logic already has three unit-tested negative controls is the right place for it.**
> Rewrite step 5 as *"confirm the CAVEAT is what a past-cap miss reports"* — which is what passed here.

> ### ⛔ EVERSPACE 2 ALSO FAILS STEP 4 — and the reason looks STRUCTURAL `[ES2-X2-2026-08-18]`
>
> ES2 (UE **505**, **1,150,301** objects, in-game) is the best candidate yet on the first criterion:
> Interesting Funcs reads **`29805 functions across 6324 classes`** — comfortably over 5,000, while
> the Classes tab reads `5000 … ⚠ STOPPED`, which is `[CLASSTOTAL]` in one screenshot.
>
> **But its exec commands are on a class INSIDE the cap, so it decides nothing.** Console → Game Only
> finds **6** exec commands, all on **`ESGameInstance`** — and that class sits at **index 22** of the
> walk. Every exec-bearing class that actually exists is inside:
>
> | class | idx | inside cap |
> |---|---|---|
> | `ESGameInstance` | **22** | ✓ |
> | `ESPlayerController` | 25 | ✓ |
> | `PlayerController` / `Character` / `WorldSettings` | 64 / 68 / 71 | ✓ |
> | `AISystem` / `GameInstance` / `CheatManager` | 1034 / 2266 / 2745 | ✓ |
>
> ▶ **The structural claim this suggests, and it should be tested before another host is hunted:**
> `UFUNCTION(exec)` lives on **long-lived singletons constructed at startup**, which is exactly what
> puts them at the FRONT of GObjects — so they are inside the first 5,000 *by construction*. A
> past-cap class is by definition late-registered (a `BP_*_C` loaded with content), and Blueprint
> classes essentially never declare native exec commands. **Step 4 may therefore be near-unstageable
> on any title**, not merely on Elliot and ES2. If so, the honest resolution is to close it against
> the shared fix (below) rather than keep hunting.
>
> **What ES2 does establish, even vacuously:** the Console **AA(B)** twin resolves a class and opens
> its dialog — `Invoke: ESGameInstance::SetRichPresence`, `(ParmsSize=4)`,
> `PresenceId [int32, 4B, off=0]` — and **Copy AA Script** produced a complete, well-formed script
> (`AA Script ready: …`; AOBMaker offline so it went to the clipboard, read back and checked: correct
> `invokeUFunction('ESGameInstance','SetRichPresence', 4, PARAMS)`, the helper-file guard, and the
> untick-on-bail-out shape). The two twins share the class-address resolution the fix changed, so
> this is evidence the Console path works — it just cannot be *past-cap* evidence here.
> ⚠ Nothing was fired: these commands have real side effects (`SetAchievement`,
> `UnlockAllAchievements` touch the user's Steam account). The defect is in resolving the class
> **before** FIRE, so opening the dialog is the whole check.
>
> ### ⛔ AVOWED TOO — third title, richest exec surface anywhere, still ZERO past the cap `[AVOWED-X2-2026-08-18]`
>
> Avowed (UE **504**, **281,501** objects, save loaded) is the strongest candidate the machine has:
> **8,780 classes** and **281 exec functions across 22 classes** (193 of them on game classes — the
> figure the UI's Console reports, which is how the detector below was validated).
>
> **All 22 exec classes are INSIDE the cap. The highest is index 4929, seventy-one short of 5,000.**
>
> | idx | class | cmds | | idx | class | cmds |
> |---:|---|---:|---|---:|---|---:|
> | 5 | `AlabamaPlayerController` | 2 | | 2535 | `GameInstance` | 2 |
> | 36 | `DebugCameraController` | 2 | | 2657 | **`AlabamaCheatManager`** | **152** |
> | 55 | `AlabamaGameModeBase` | 6 | | 2831 | `PlayerInput` | 5 |
> | 59 | `PlayerController` | 14 | | 3094 | `CheatManager` | 50 |
> | 170–251 | `GameMode`/`GameHud`/`HudBase`/`HUD` | 13 | | 3301 | `ActivitiesSubsystem` | 5 |
> | 1092 | `AlabamaGameInstance` | 4 | | 4080–4102 | `AlabamaAutoPlayer`/`DevUtility`/`UiCheatManagerExtension` | 6 |
> | 1265–2050 | `AISystem`/`FogOfWarSubsystem` | 5 | | 4412–4929 | `HealthSnapshotBlueprintLibrary`/`UiCheatManagerExtension` | 12 |
>
> ⇒ **THE STRUCTURAL CLAIM IS NOW CONFIRMED ON THREE TITLES** (Elliot 0 game execs; ES2 6, all at
> idx 22; Avowed 281, all ≤ 4929) **and it should be refined**: it is not merely "startup singletons
> sit at the front". Every exec-bearing class is a **natively-declared C++ class**, registered while
> modules load — i.e. before content. The classes *past* the cap are the tail of the walk, which is
> content-loaded Blueprint assets (`BP_*_C`, `WBP_*_C`, `GA_*`), and a Blueprint cannot carry a
> native `UFUNCTION(exec)`. **A past-cap exec command is therefore close to a contradiction in terms.**
>
> ▶ **Recommendation: close step 4 against the shared fix rather than hunt a fourth host.** Step 3
> already proved the past-cap path end-to-end (`BP_EnemyCharacter_C::SetBlockDispHPGauge`, AA(B)
> dialog + script into CE), and the Console twin shares that same class-address resolution — ES2
> exercised it successfully, just not past the cap. Hunting further has a poor prior.
>
> ### ⚠ THE DETECTOR WAS WRONG TWICE, AND EACH TIME IT RETURNED A CLEAN ZERO
> Recorded because a zero from a broken detector is indistinguishable from a real absence — the exact
> failure this row keeps producing:
> 1. Read `flags` / `name`; the reply's fields are **`function_flags`** / **`func_name`** → every
>    lookup was `None` → "EXEC: 0 across 0 classes".
> 2. Fixed the field, then used **`FUNC_Exec = 0x100`** from memory. `0x100` is **`FUNC_NetRequest`**;
>    the real value is **`0x200`**, and this repo states it in
>    [`ConsoleViewModel.cs:15`](../ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs) — still "EXEC: 0".
>
> **What made it safe in the end was a cross-check against a number computed by other code**:
> `game_only=true` must yield the UI's own `193`. It does, exactly, so the 281/22 figures are
> trustworthy. **Never accept a zero from a filter that has not been shown to fire.**
>
> ### ✅ STEP 4 CLOSED, NON-VACUOUSLY — by LOWERING the cap instead of hunting a host `[AVOWED-X2b-2026-08-18]`
>
> **The maintainer's idea, and it is the right one.** Three titles proved no game ships an exec
> command past 5,000 (above), so the row looked unstageable. But the code's only input is *"is this
> class absent from the page it was handed"* — **it never sees the cap's value**. Lowering the cap
> puts a real class outside it, which is the identical condition, reached without a fourth host.
>
> ⚠ Note the asymmetry with the warning further up: **raising** the cap would hide the defect
> forever; **lowering** it exposes the defect on demand. They are not the same act.
>
> **Setup.** `int limit = 5000` → `3000` in the two UI defaults
> ([`IDumpService.cs:242`](../ui/UE5DumpUI/Core/IDumpService.cs),
> [`DumpService.cs:2549`](../ui/UE5DumpUI/Services/DumpService.cs)) — every call site omits the
> argument, so two lines move all of them. **No DLL rebuild, no game restart.** Avowed, UE **504**,
> **289,018** objects, save loaded, game paused (which also stops the walk order drifting).
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ | Classes → Load → `3000 classes (scanned 289,018 objects, 3000 total UClasses) ⚠ STOPPED at the 3,000-row cap — more classes exist` — verbatim, and the message tracks the new limit |
> | 2 | ✅ | Filtering that page for `Activi` returns **0 rows** ⇒ `ActivitiesSubsystem` is absent from the capped page — the UI's *own* witness of absence, which is what the handoff will see |
> | 4a | ✅ **AA(B)** | `Invoke: ActivitiesSubsystem::EnableActivity`, `(ParmsSize=17)`, `activityID [FString, 16B, off=0]` + `Enabled [bool, 1B, off=16]`. **Copy AA Script** → `AA Script ready: ActivitiesSubsystem::EnableActivity` |
> | 4b | ✅ **Run** | The **FIRE dialog** opened with both parameter fields and `FIRE / Copy AA Script / Close / Cancel`. **Cancelled — nothing fired** (`exec EnableActivity cancelled`); these commands mutate a live save, and the defect is in resolving the class *before* FIRE, so opening the dialog IS the check |
>
> ⇒ **Both Console twins resolve a class the capped page does not contain.** Before build 2888 this
> aborted with *"Class … not found"*. Step 4 is closed on real behaviour, not by argument.
>
> ⚠ **What this does and does not prove.** It proves the handoff works when the class is *absent from
> the returned page* — the only input the code has. It does **not** separately exercise index >5000,
> and no claim is made that it does. Given the code never reads the limit, that is not a gap.
> ⚠ **The cap change was reverted and verified** (`grep` shows `5000` restored, `3000` gone,
> `build_number.txt` back to 3261, `dist` republished AOT).
>
> ### ⚠ TWO TRAPS THIS RUN, both of which produced convincing wrong answers
> * **The rig's `max_results` was silently ignored — the DLL reads `limit`.** So a "cap = 3000" query
>   quietly returned **5000 rows**, and the class indices taken from it disagreed with what the UI saw.
>   That is the **third** wrong-field-name of this sitting (`flags`→`function_flags`,
>   `name`→`func_name`, `max_results`→`limit`): the pipe silently ignores unknown keys, so a wrong
>   name is never an error, just a wrong answer. **Echo one known value back before trusting a query.**
> * **Walk position is NOT stable while the game streams.** `HealthSnapshotBlueprintLibrary` sat at
>   index 4412 in one query and 2582 in another minutes later. So "past the cap" must be re-checked
>   at the moment of the test — and the UI's own filter is the right witness, not a stored index.
>   Pausing the game stabilises it.
>
> ### ⛔ FOUND WHILE DOING THIS — `[PASTECRASH-2026-08-18]`: a clipboard paste can KILL the UI
> Typing a 19-character filter made computer-use paste rather than type, and the UI **died**:
> ```
> System.Runtime.InteropServices.COMException (0x8007000E): EnumFormatEtc failed
>    at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()
>    at Avalonia.Controls.TextBox.Paste()
>    at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_0(Object state)
> ```
> A failed clipboard READ inside `TextBox.Paste()` surfaces on the dispatcher as an unobserved async
> exception and terminates the process — **Ctrl+V into any textbox is a potential crash**, and the
> user loses a loaded session. Worth a dispatcher-level guard (`Dispatcher.UnhandledException`) that
> logs and swallows input-layer faults. Effort **S** · Risk **low**.
> ⚠ Second, smaller defect in the same evidence: `crash.log` labels it **"UE5DumpUI startup crash"**
> though it happened long after startup — the handler hard-codes that phrase.
> ➜ **BOTH HALVES FIXED 2026-08-18** (dispatcher input-fault guard + honest crash.log phase/uptime).
> The live check that is still owed is the batch tagged `[PASTECRASH-2026-08-18]` in
> `## Pending live-game verification` above — grep the tag.
>
> ### ⚠ MY OWN ERROR, recorded because it is the same shape as the trap this row keeps hitting
> I first read the class as **`ES2GameInstance`** off a 0.6-scale screenshot (the package is `ES2`,
> the class is `ESGameInstance`) and membership-tested *that*. It came back "not in the capped page"
> — **because it does not exist at all** — and I reported ES2 as qualifying. A nonexistent name is
> absent from every list, which is indistinguishable from "past the cap". **Always confirm the class
> EXISTS before concluding it is past the cap**; `find_instances` answers it in one call, and the
> corrected table above pairs `exists` with `inside cap` for exactly this reason.

### ✅ DONE 2026-08-18 `[ELLIOT-Y1c-2026-08-18]` — run a generated CE invoke against a live game (audit #5 Y1, build 2862)

The invoke form passed **0** for every `UObject*` / `FName` argument since the feature shipped;
`tonumber(s, 16)` was handed a string still carrying its `0x`. Fixed, and the Lua semantics are
measured in three independent interpreters (CE's own `lua53-64.dll`, a 5.4 CLI, and CE's bundled
`lbaselib.c`).

**What that does NOT prove is that the corrected value reaches the function.** The measurement stops
at the Lua expression; everything after it — the mailbox write, the DLL's `CMD_INVOKE`, `ProcessEvent`
— is untested end-to-end.

1. In Live Walker, pick a UFunction taking an object parameter (`K2_AttachToActor`, or any
   `BlueprintCallable` with an `AActor*`), and use **Copy AA Script** / push to CE.
2. Paste an instance address from any panel — i.e. the app's own `0x`+uppercase-hex format — into the
   `[UObject*: …]` field and FIRE.
3. Success is **not** `INVOKED OK`: that was printed by the broken version too. Confirm the *effect*
   in-game, or set `UE5_DEBUG=1` and read the decoded return.
4. Worth one negative case: FIRE with the untouched `0x0` default and confirm it behaves as a null
   argument — that path was the only one that ever worked, so it should be unchanged.

> ### 🟡 PATH PROVEN, ARGUMENT NOT — and BOTH of this row's staging instructions are wrong
> `[ELLIOT-Y1-2026-08-18]`
>
> Elliot, UE **504**, DLL **3262**, CE 7.7, AOBMaker connected, PE hook installed this launch
> (`hook installed at 0x141596890`). Target: `BP_PlayerCharacter_C::FireBirdLaserOngoing` via the
> Live Walker **`INV`** button — the button that actually reaches `InvokeScriptGenerator`, which is
> where the `tonumber` fix lives.
>
> **What IS established (the transport):** `INV` pushed the script (`Invoke script created in CE: …`),
> the record popped the CE form titled `BP_PlayerCharacter_C::FireBirdLaserOngoing | 0x134FE8040`
> — i.e. it resolved a live instance by itself — the `0x`+uppercase address was accepted into the
> `[UObject*: Actor, 8 B]` edit, and FIRE reached the DLL: `Mailbox: received cmd=1` →
> `INVOKE inst=0x134FE8040 func=0x3779D5900` → **`INVOKE result=0`**, with `UE5_DEBUG=1` printing
> `Invoking via mailbox… INVOKED OK`.
>
> ⛔ **That is exactly as far as it goes, and step 3 predicted it: `INVOKED OK` is not the result.**
> The pointer's arrival is still **unwitnessed**, for a reason worth writing down:
>
> ### ⚠ TRAP 1 — the form's `[UObject*: …]` rows can be BP LOCALS, outside `parmsSize`
> The DLL logged **`parmsSize=8 numParms=1`**: the function's only real parameter is
> `ElapsedSeconds [double, 8B, off=0]`. Every object row the form offered —
> `CallFunc_Conv_SoftObjectReferenceToObject_ReturnValue [UObject*: Object, off=8]` and
> `K2Node_DynamicCast_AsActor [UObject*: Actor, off=16]` — is a **Blueprint frame local past the
> parameter block**, not an argument. Reading the mailbox params buffer after FIRE
> (`g_invokeMailbox 0x7FFEDC3AA5D0` + `0x328`, 24 bytes) returned **all zeros**.
> ⇒ **Picking any `[UObject*: …]` row the form happens to show cannot decide Y1 — and it will look
> like it did, because the invoke returns `result=0` and prints `INVOKED OK` either way.** The target
> must be a function whose ObjectProperty is a *parameter* (offset < `parmsSize`).
>
> ### ⚠ TRAP 2 — Live Walker lists only the class's OWN functions, so this row's own example is unreachable
> The row says to pick `K2_AttachToActor`, which is declared on `Actor`. On the walked pawn
> (`BP_PlayerCharacter_C`, **105 functions**), filtering the function list for `K2_` returns **0**, and
> `Owner` returns **0** — the list is own-class only. Nor can `Actor` itself be walked. Confirmed from
> the other side too: `KismetSystemLibrary` (which *does* declare `GetObjectName(UObject*)`) is refused
> by the UI with *"No live instance … LiveWalker has nothing to walk because there is no instance."*
>
> ▶ **What the next attempt needs:** a class that (a) has live instances and (b) **declares** a
> function taking an `ObjectProperty` **within `parmsSize`**. Screen candidates in Interesting Funcs
> by the `Param` column, then confirm the type and offset in the `AA(Baked)` dialog — it prints
> `[UObject*: …, 8B, off=N]` — and only then drive `INV`. **Steps 3 and 4 remain unrun**: with no
> argument-carrying target, neither the effect-confirmation nor the `0x0` null control is meaningful.
>
> ### 🔁 SECOND ATTEMPT — a qualifying target WAS found, and the witness turned out to be invalid
> `[ELLIOT-Y1b-2026-08-18]`
>
> **Finding the target is solved and cheap** — do it over the pipe, not by clicking:
> `walk_functions {addr: <class_addr>}` returns each function's `num_parms` **and** a `params[]` list
> with `type`/`offset`/`ret`; a real parameter is one of the **first `num_parms` entries**. Screening
> the classes that had live non-CDO instances produced **38** functions with a genuine object
> parameter. Script kept at `out/ce-plugin-test/find_y1_target.py`.
> ⚠ `walk_function_props` is **not** the parameter list — it is Denken's property-xref walk
> (`scope`, `occurrences`, `offset:-1`). Using it here returns nothing and looks like "no candidates".
>
> **Target used:** `AttackCollisionData::SetOwnerClass` — `num_parms=1`, the single param
> `OwnerClass [ObjectProperty, off=0]`, **native** (`flags=0x4020401`), on a live instance
> `0x1C1FED3200` whose own `OwnerClass` field (`+0x1F8` → `0x1C1FED33F8`) read **all zeros** first,
> in both `read_mem` and the Live Walker grid. A perfect-looking witness: baseline zero, so any
> non-zero could only come from the typed value.
>
> **What ran:** `INV` → CE form with exactly one field `OwnerClass [UObject*]` → typed
> `0x3C8940A30` (a real `UClass`) → FIRE → `Mailbox: INVOKE inst=0x1C1FED3200
> func=0x7FF4DE4B6A48`, `result=0`.
>
> ⛔ **And the witness is INVALID — which is the actual result of this attempt.** Both `OwnerClass`
> and `paramsData` read back zero, which *looks* exactly like the old bug (`tonumber('0x…',16)` →
> nil → 0). It is not. Four checks, in order:
> 1. **The emitted script is the FIXED form.** Dumped from the CE record itself
>    (`out/ce-plugin-test/inv_script.txt`, 11,900 chars), line 251:
>    `writeQword(PD + 0, (function() local s = edits[1].Text or ''; … local h = s:match('^0[xX](%x+)$'); if h then return tonumber(h,16) or 0 end; …)())`
>    with `local PD = mb + 0x328` — the prefix IS stripped before `tonumber(h,16)`.
> 2. **That expression parses correctly in CE's own Lua**: `PARSE of 0x3C8940A30 -> 16250047024 (0x3C8940A30)`.
> 3. **My reader and the address are sound** — negative control: `writeQword(mb+0x328, 0xDEADBEEF)`
>    then `read_mem` shows `0x00000000DEADBEEF`; a plain write of `0x3C8940A30` reads back
>    `0x00000003C8940A30`. The address `mb+0x328` is confirmed against `Mimic.h` (`paramsData` @ 0x328),
>    and the header survived the call (`instanceAddr`/`ufuncAddr`/`parmsSize=8`/`numParms=1` all correct).
> 4. ⇒ **`paramsData` is cleared by the invoke path**, so reading it *after* the call cannot witness
>    what was passed — and `SetOwnerClass` does not store into the `OwnerClass` field either.
>
> ▶ **Next attempt needs a witness that SURVIVES the call**, and the two that would:
> * **Freeze the game thread and read `paramsData` while the DLL is still blocked** — exactly the
>   AA14-AA20 step-5 staging (`set_invoke_timeout` well above the Lua's 10 s, `suspend.py suspend-tid`
>   on a thread picked **by fire-rate**). ⚠ This needs `hook_active: true`; on this launch the hook
>   again failed with `MH_CreateHook failed: MH_ERROR_MEMORY_ALLOC`, so **restart until it installs**.
> * Or a function that **persists** its object argument somewhere readable (verify by reading the
>   field back, not by assuming a setter stores it — `SetOwnerClass` did not).

> ### ✅ THIRD ATTEMPT — CLOSED, and the previous attempt's diagnosis was WRONG
> `[ELLIOT-Y1c-2026-08-18]`
>
> Elliot, UE **504**, DLL **3262**, CE 7.7 attached, AOBMaker connected, **`hook_active: true`**.
> Target `DropItemSpawner::Setup` — chosen because its two parameters are *exactly* the two types
> this bug affected: `InOwner [ObjectProperty, off=0]` and `NameLotteryID [NameProperty, off=8]`,
> `parmsSize=16 numParms=2`, flags `0x04020401` (Final|Native|Public|BlueprintCallable — **not**
> `FUNC_Static`, so it routes through GameThreadDispatch). One FIRE settles both halves.
>
> **Both values were typed WITH the `0x` prefix**, which is the whole point: a bare-hex string goes
> down `tonumber(s,16)`, the path that always worked. Distinct values so they cannot be confused.
>
> | witness | pre-FIRE | post-FIRE | typed |
> |---|---|---|---|
> | `paramsData+0x00` (InOwner) | `0x0` | **`0x1078919D0`** | `0x1078919D0` |
> | `paramsData+0x08` (NameLotteryID) | `0x0` | **`0x1234ABCD`** | `0x1234ABCD` |
> | instance `Owner` field `+0xE0` | `0x0` | **`0x1078919D0`** | — reached the function |
>
> ⇒ **Step 3 satisfied by the EFFECT, not by `INVOKED OK`**: `Owner` is a stored field that survives
> the call, and it holds the typed pointer. ⇒ **Step 4 (null control) run first, deliberately**, so
> the positive case started from a known zero: FIRE with the untouched `0x0` gave `result=0`,
> `status=1`, `Owner=0`, no crash. The check is demonstrably able to fail in both directions.
>
> ### ⚠ The Y1b conclusion "`paramsData` is cleared by the invoke path" is REFUTED
> It is not cleared on **any** path, and the code says so: the game-thread path copies `ownedParams`
> back over the caller's buffer ([`Stark.cpp:430`](../dll/src/Stark.cpp)), the timeout path
> deliberately performs no copy-back, and both the static-native and the no-hook fallback pass
> `&g_invokeMailbox.paramsData` straight to ProcessEvent. Two things that DO produce the observed
> zeros, and both applied to Y1b: the hook was **inactive** on that launch, and `Mimic.cpp` contains
> **eight** `memset(g_invokeMailbox.paramsData, 0, …)` calls in *other* command handlers — so any
> later mailbox traffic wipes the buffer. **Read it immediately after FIRE, with no mailbox command
> in between, and it is a valid witness.** Generalisation worth keeping: *a shared buffer is only a
> witness for as long as nothing else is entitled to write it.*
>
> ### ⚠ The script picks its OWN instance — witness THAT one
> The form resolved `inst=0x7FF4DE7EE190` (first live instance) while Live Walker was walking
> `0x7FF4DE81F970`. Reading `Owner` on the walked instance shows **no change** and looks like a
> clean FAIL. The mailbox header names the instance actually invoked — read it from there.
> Rig: [`tools/verify/mailbox_addr.py`](../tools/verify/mailbox_addr.py) resolves `g_invokeMailbox`
> by parsing the injected DLL's export table (no CE involved — CE's own `getAddress` is part of the
> path under test), and `tools/verify/y1_witness.py` prints both witnesses.
>
> ### ⚠ Two rig traps this cost, both worth not repeating
> * **A reader that returns 0 for "read failed" is useless here**, because 0 is also what the *bug*
>   looks like. A screener that dropped the `ReadProcessMemory` return check reported
>   `PERSISTS = False` for a store that had actually happened — the UI was showing the stored value
>   at the same moment. Every reader in `tools/verify/` now fails loudly instead.
> * **The IME eats typed hex.** This machine's default input is Chinese; typing `0x1078919D0` into a
>   CE form produced Han characters and a candidate window that also swallowed `Ctrl+A`/`Ctrl+V`.
>   `shift` toggles the IME to English; `End` + repeated `BackSpace` is the reliable clear, since
>   triple-click-to-replace silently left the old text in place.




### ⬜ NEW 2026-08-14 — open the exported .usmap in a real consumer (audit #5 W1/W7, build 2853)

The `.usmap` export declared v3 and wrote the v0 body; it has been unopenable since the feature
shipped on 2026-03-01. Now fixed to v4 with a round-trip reader in the test suite that asserts the
stream is fully consumed at the widths the vendored canonical writers define.

**What the round-trip cannot prove is that a real parser agrees with our reading of the format.**
Both are derived from the same two sources (`vendor/RE-UE4SS/.../Generator.cpp`,
`vendor/Dumper-7/.../MappingGenerator.cpp`), so a shared misreading would satisfy both.

1. Export a `.usmap` from any connected game (**Export → USMAP**).
2. Open it in **FModel** (Directory selector → *Mappings file*), or run it through CUE4Parse's
   `UsmapParser` directly.
3. Success criterion is not "no error" — it is that **property names and types appear for a class you
   can independently verify**, e.g. `AActor`'s `bHidden` / `InitialLifeSpan`. A parser that accepted
   the header and produced an empty or garbage table has still failed.
4. Worth including a **Blueprint-generated** class in the check: `W8` (the bare `"Class"` filter that
   drops every `*_C`) is **still open**, so a BP class legitimately will not be there yet — confirm
   that is the reason rather than a parse failure.

### ✅ CLOSED 2026-08-17 `[W23-PIPE-2026-08-17]` + `[SDKHDR-UI-2026-08-17]` — SDK header layout: inherited-property boundary + packed bitfields (audit #5 W2/W3, build 2842)

**Both halves now pass — the headless boundary value AND the emitted header.** DumperTest
Development, build **1.0.0.3262**, headless via `tools/verify/pipe_client.py` and then through the
real UI. ⛔ **The export also surfaced a separate, unrelated defect that makes the header
uncompilable — see the block after the UI half. That one is still open.**

1. ✅ `walk_class` on `DumperTestActor` (`//Script/DumperTest/DumperTestActor`) reports
   `super_props_size: 672` against `props_size: 1760` — non-zero and smaller, as required.
2. ✅ **The real check.** Walking `super_addr` (`0x1DB6FDEAE00` = `//Script/Engine/Actor`) directly
   gives `props_size: 672` — *exactly* the child's `super_props_size`. This is the equality that
   would catch the offset being read off the wrong struct, and it holds. Corroborated independently
   by the layout itself: `DumperTestActor`'s own first field, `Text_Even2_OneNull`, sits at offset
   **672** — the derived data starts precisely at the boundary.
3. ✅ **Not an absence-shaped result** (§1.2). The lowest-offset field in `fields` is
   `PrimaryActorTick` at **40**, far below the 672 boundary, so the reply genuinely does carry
   inherited properties and the filter has something to do.

*Also visible in the same reply, though it is the emitter that W3 is about:* `AActor`'s replication
block is present in the packed form the header generator has to handle — `bNetTemporary`/
`bOnlyRelevantToOwner`/`bAlwaysRelevant`/… all at **offset 88** with `bool_mask` 1/4/8/16/…, and the
sample's own `bFlagA`/`bFlagB`/`bFlagC` at **1648** with masks 1/2/4 plus `bPlainBool` at 1649.

### ✅ UI HALF NOW CLOSED 2026-08-17 `[SDKHDR-UI-2026-08-17]` — all three checks pass

UE5DumpUI **is** grantable once the shortcut lives in the all-users Start Menu (see
`docs/auto-verification-session-plan.md` §1), so the export was run for real: DumperTest Development
injected via the panel's own **Inject into running game…**, `Connected — UE504 (25179 objects)`,
`v1.0.0.3262 DLL 3262` on screen, then **Export → SDK Header (.h)** → 3.48 MB / 75,342 lines.

```cpp
struct DumperTestActor : public Actor
{
    FText Text_Even2_OneNull; // 0x02A0 (0x0010) TextProperty      <- FIRST member
    ...
    uint8_t bFlagA : 1; // 0x0670 (0x0001) BoolProperty [Mask: 0x01]
    uint8_t bFlagB : 1; // 0x0670 (0x0001) BoolProperty [Mask: 0x02]
    uint8_t bFlagC : 1; // 0x0670 (0x0001) BoolProperty [Mask: 0x04]
    bool bPlainBool;    // 0x0671 (0x0001) BoolProperty
}; // Size: 0x06E0
```

1. ✅ **Opens at the super's size.** The first member sits at **0x02A0 = 672** — the exact
   `super_props_size` the headless half measured — with no filler ahead of it.
2. ✅ **Declares none of the base's properties.** Zero `AActor` members in the block: no
   `PrimaryActorTick`, no `bNetTemporary`/`bOnlyRelevantToOwner`/`bAlwaysRelevant`, no
   `RootComponent`. All of those **are** in the `walk_class` reply this header was built from
   (`PrimaryActorTick` at 40, the replication bools at 88), so the filter demonstrably ran on data
   that contained them — an absence with a witness, not a bare absence.
3. ✅ **Bitfield runs match the gap.** `bFlagA/B/C` all at **0x0670** with masks 1/2/4, and the next
   member starts at **0x0671** — the run consumed exactly one byte. `bPlainBool` is emitted as a full
   `bool`, correctly *not* as a bitfield.

`Size: 0x06E0` = **1760** = the headless `props_size`, which is a fourth cross-check for free.

Both fixes are unit-verified end-to-end against the real emitters, with separate negative controls.
What no unit test can cover is the **boundary value itself**: `super_props_size` is a new
`walk_class` field read off a live `UStruct`, and the tests supply it by hand.

**Cheapest check — headless, no UI**, using the pipe recipe in
[audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md#the-reusable-win-from-today--headless-in-game-verification):

1. Inject into any game, then `walk_class` a **derived** class (anything `*_C`, or `AActor` itself).
2. Assert `super_props_size` is **non-zero, less than `props_size`**, and equal to the `props_size`
   the same command reports for `super_addr` when walked directly. That last equality is the real
   check — it is the only one that would catch the offset being read off the wrong struct.
3. Confirm the lowest-offset field in `fields` is **below** `super_props_size` (i.e. the reply really
   does carry inherited properties, so the filter has something to do). A run where every field is
   already ≥ the boundary proves nothing — it is the absence-shaped result
   [working-lessons.md](working-lessons.md) §1.2 warns about.

**Then the UI half**: export an SDK header for that class and check the struct opens at the super's
size, declares none of the base's properties, and that a class with packed bools (`AActor` has a
replication-flag block) emits `uint8_t bX : 1` runs whose byte count matches the gap to the next
field.

-----

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK `[SDKHDR-2026-08-18]` — the exported SDK header COMPILES again

*This is the OPEN FIXES INDEX's one **untagged** row ("SDK header does not compile"), surfaced by the
`[SDKHDR-UI-2026-08-17]` export above and worth more than the step that found it. It now has a tag, a
fix and a batch, and the index row is gone.*

*Was: `OptionalProperty` and any unresolved `StructProperty` baked the array extent into the **type**
string, so the field writer's `{type} {name};` emitted `uint8_t[0x40] CellBounds;` — not valid C++.
Measured over the whole 75,342-line export: **5 malformed declarations, every one an
`OptionalProperty`**, against **7,543 well-formed** `uint8_t Pad_0000[0x0028];` padding declarations —
the padding emitter was always right and only the two fallbacks were wrong. `CellBounds` is an engine
(World Partition) property, so **any** real UE5 title with a `TOptional` UPROPERTY exported a header
that could not be compiled. Now: `SdkExportService.MapCppDecl` returns a `CppDecl(Type, ArraySuffix)`
pair and the extent is written **after** the identifier, exactly as the padding path always did.
`null` out of the type switch is the ONLY route into the raw-byte fallback, so an extent cannot be
smuggled back into a type string by a future branch.*

> **Why nothing caught it**: the emitters were unit-covered, but never over a `TOptional` field — and
> a generated header is only ever *read* in this repo's checks, never compiled. Both gaps are closed
> offline now. `ui/UE5DumpUI.Tests/SdkHeaderDeclaratorTests.cs` walks **every** emitted member and
> rejects an extent that precedes the identifier *whatever produced it*, and
> `tools/verify/compile_sdk_header.py` puts the real emitter's output in front of `cl.exe`
> (`/Zs`, no dev shell needed — the artifact includes nothing). Negative control: re-inserting the
> pre-fix spelling gives `error C2059: syntax error: '['` on that exact line, and the shape oracle
> flags exactly one bad declarator.
>
> **What the offline half cannot cover** is the *corpus*. The fixture has the branches; a real title
> has the distribution — and an unresolved `StructProperty` did not occur even once in the
> 75,342-line export, so that fallback has still never been seen on live data.
>
> | step | do this | expect |
> |---|---|---|
> ### ✅ ALL THREE STEPS PASS 2026-08-20 `[SDKHDR-REALEXPORT-2026-08-20]` — on a real export, matching the row's own numbers
>
> UI connected to DumperTest Development (`Connected — UE504 (25179 objects)`), **Export → SDK
> Header (.h)** → `out/DumperTest_SDK.h`.
>
> ⭐ **The export is 75,342 lines — the exact figure this row cites for the PRE-FIX export of this
> same sample.** So it is the same corpus, and the comparison is like-for-like:
>
> | | pre-fix (per this row) | now |
> |---|---|---|
> | extent PRECEDING an identifier (step 2) | **5** | **0** |
> | `OptionalProperty` declarations (step 3) | 5 | **5** |
>
> Step 3 is satisfied, so step 2 is **not vacuous**: the same five fields are present and every one
> now emits the extent *after* the identifier —
> `uint8_t CellBounds[0x40]; // 0x0088 (0x0040) OptionalProperty` (the World Partition property this
> row names), plus DumperTest's `Opt_Int_Set` / `Opt_Float_Set` / `Opt_Str_Set` / `Opt_Int_Unset`.
>
> ⭐ **Confirmed by a real compiler, not only by grep.** `cl /Zs /TP /permissive-` over the whole
> 3.48 MB header produces **zero `C2059`** — the `syntax error: '['` the negative control produces
> when the pre-fix spelling is re-inserted. The defect is gone from the artifact, not just from the
> emitter.
>
> ⚠ **The unresolved-`StructProperty` fallback still has no live sample** — as the row predicted, it
> did not occur even once in this export either. That branch remains fixture-only.
>
> ### 📌 And this settles the separately-tracked `G4`-followup, with evidence
>
> The handover records that the header "still will not compile as one translation unit" because
> `GenerateFullSdkAsync` emits classes in **GObjects order with no topological sort**. **Confirmed.**
> Compiling the real export as one TU fails with **152+ errors** (`C1003` stops the count, so that is
> a floor), and the mix is diagnostic:
>
> | code | n | meaning |
> |---|---|---|
> | `C4430` | 50 | missing type specifier |
> | `C3646` | 32 | unknown override specifier |
> | `C2079` | 31 | uses undefined struct |
> | `C2143` / `C2238` | 19 / 19 | syntax / unexpected token before `;` |
> | **`C2059`** | **0** | **the SDKHDR defect — absent** |
>
> Every populated code is a **use-before-declaration** symptom; none is a malformed declarator. So
> the two problems are cleanly separated: `[SDKHDR]` is fixed, and what remains is ordering.

> | 1 | connect to a UE5 title with a `TOptional` UPROPERTY (**DumperTest** has `Opt_Int_Set`; any World Partition title has `CellBounds`), then **Export → Dump All** and **Export → SDK Header (.h)** | both complete without error |
> | 2 ⚠ THE ONE THAT MATTERS | grep the header for an extent that PRECEDES an identifier: `rg "^\s+\S*\[0x[0-9A-Fa-f]+\]\s+\w+;" out.h` | **0 matches**. It was **5** on the pre-fix export of this same sample |
> | 3 ⚠ NOT AN ABSENCE-SHAPED RESULT | `rg "OptionalProperty" out.h` | **≥1**, each of the shape `uint8_t Name[0xN]; // … OptionalProperty`. A header containing no `TOptional` at all makes step 2 vacuous ([working-lessons.md](working-lessons.md) §1.2) |
> | 4 | `rg "\[0x0\];" out.h` | **0** — MSVC rejects a zero-length array (C2466), so that would not compile either |
> | 5 | cut the struct(s) that own the `OptionalProperty` members into a small `.cpp`, prepend the stub prelude from `out/sdk-smoke/sdk_smoke.cpp`, and run `cl /Zs /TP /permissive- /utf-8` on it | **exit 0** |
>
> ⚠ **Do not over-read step 5.** It is deliberately scoped to an EXCERPT. `GenerateFullSdkAsync`
> emits classes in GObjects order with no topological sort, so the full 75,342-line header very
> likely does not compile as one translation unit regardless of this fix (a `struct X : public Y`
> whose `Y` is declared later is an incomplete base). That ordering question is **untested and
> separate** — if step 5 on the whole file fails with "undefined base class" rather than a syntax
> error at a `[`, that is the ordering gap, not this defect, and it is worth opening as its own item.

Shipped as the first fix batch of [audit #5](audit-2026-08-13-early-code-findings.md) cluster ①.

-----

### 🟡 MG2 / TSet+UDataTable no-regression / U2 — container geometry on a real game (build 2830)

*Closed here already: **MG1 · MG3 · A2 · U1 · V1** (two sittings below — the DLL half 2026-08-14, the
UI half 2026-08-18). **Still open: three.** **MG2**'s rows-equal-count half (the count half passed;
the rows half is undecidable while the drill-down caps — `[CONTAINERCAP-2026-08-18]`), the
**`TSet<FName>` / `TSet<UObject*>` / `UDataTable` no-regression** check (DumperTest ships none of the
three, so it needs a real game), and **U2** (needs a `CasePreservingName: YES` title — twelve
confirmed non-CPN, zero CPN, so the absence is itself the signal and this stays LOW).*

> ### ⬜ 2026-08-14 — the UI half (build 2830) is NOT yet verified in-game
>
> The ✅ table below is **correct and narrower than it looks**: it verifies that the *DLL* reads map
> elements at the right stride. Audit #5 segment **U1/V2** then found the same formula in **three C#
> copies that `5ef4c2b` did not update**, so the key→value *text* in the grid was right (the DLL read
> it) while every map element **address the UI computed itself** was short by 4+ bytes past index 0.
> Alongside it, **U1/V1** (the audit's only surviving HIGH) had map rows publishing the element base —
> the **key** — as the address the inline editor writes to.
>
> **Fixed in build 2830**: the DLL now publishes `map_stride` / `set_stride`, `Core/ContainerGeometry.cs`
> is the single client-side consumer, all three mirrors are deleted, and a map row's type/address/size
> all describe the value. Unit-verified with a negative control (reverting the fix turns 5 tests red,
> including the seam test).
>
> **What still needs a live process** — the DLL half already has witnesses below; this is the **UI**
> half, which no headless pipe check can see because it is client-side arithmetic:
>
> | Check | On a `TMap<AActor*,float>`-shaped field (8-aligned key, 4-byte value) | Expect |
> |---|---|---|
> | Address column | drill into the map, read element **[1]**'s Address | `MapDataAddr + 24 + 8`, **not** `+20`, and **not** the element base |
> | Inline edit | edit element [1]'s value, then Refresh | the **value** changes; the key text is unchanged |
> | CE record | "+CE" on element [1] | CE shows the value, and freezing it does not corrupt the key |
>
> `DumperTest`'s `Map_I64ToI32` / `Map_StrToInt` already exercise the DLL side; the UI check wants a
> map whose pair alignment is 8 and whose pair size is NOT already a multiple of 8, since that is the
> only shape where the old and new strides differ.

> ### ✅ FIVE OF SIX VERIFIED IN-GAME 2026-08-14 — DumperTest, UE 5.4 Development package
>
> Driven **entirely headlessly**: launch the packaged sample → `scripts/inject-ue.ps1 -ProcessId` →
> a ~10-line PowerShell `NamedPipeClientStream` issuing `find_instances` + `walk_instance`. No UI.
> This is repeatable in one command; the witnesses were added to the sample the same day
> (commit `58ddf76`) precisely because none of the pre-existing containers could discriminate.
>
> | Fix | Verdict | Evidence from the live walk |
> |---|---|---|
> | **MG1** | ✅ | `Map_I64ToI32` all three elements correct (`600000000001..3` → `6001..3`). A stride of 20 makes elements 1–2 read from the previous element's tail; they are exact, so the stride is 24. |
> | **MG1** (2nd witness) | ✅ | `Map_StrToInt` `map_value_offset=16`, values `6101/6102/6103`. Different arithmetic from MG1's first witness, so one wrong assumption cannot satisfy both. |
> | **MG3** | ✅ | `Map_IntToVec3f` reports **`map_value_offset: 4`**. The old size guess yields **8**. This is `Ubel::GetStructAlignment` reading `MinAlignment=4` off a live `UScriptStruct`. Raw hex `00C8C145 00D0C145 00D8C145` decodes to 6201.0/6202.0/6203.0 — all three floats at the right offsets. |
> | **MG2** | ✅ | `Set_Big` `set_count=199` (200 added, 1 removed). Before the fix `NumFreeIndices` always read 0, so this reported **200**. |
> | **A2** | ✅ | `Set_Big` returns 199 elements with **9005 absent** and 9000 / 9004 / 9006 / 9199 all present. 9005 is index 5, i.e. its bit lives in the inline words the `TBitArray` froze when it spilled at 128 — the defect would still list it. |
> | **U2** | ⬜ | **No known vehicle.** See the box below — TQ2 is NOT CasePreservingName on the current build. |
>
> ### ⚠ 2026-08-14 — TQ2 is NOT CasePreservingName, contradicting `test-games.md`
>
> Injected into `TQ2-Win64-Shipping.exe` (PID 53412, Steam, save loaded) to verify U2.
> `get_offsets` returned **`case_preserving=false`**, and the DLL's own detection log is unambiguous:
>
> ```
> [DYNO] DetectCasePreservingName: votes standard=20, CPN=0 (tested 20 objects)
> [DYNO]   CasePreservingName: no
> [SUMMARY] DynOff: CPN=no FProp=yes TagFFV=yes Outer=+0x20 validated=yes
> [SCAN] FindAll: UE Version = 507 (tier=1, detected=yes, lowConfidence=no)
> [OARR] FUObjectItem size=24, object-ptr offset=+0x08 (UE5.7+ reordered item) — 200 named, 200 total, 0 bad
> ```
>
> A **20–0 sweep** is not a marginal or failed detection, and everything around it resolved cleanly
> (correct UE 5.7, correct reordered `FUObjectItem`, 200/200 named). So this is not the detector
> failing — this build genuinely has `WITH_CASE_PRESERVING_NAME` off.
>
> **`docs/test-games.md:13` says the opposite** ("CasePreservingName + DynOff. Stride 16."). One of
> two things is true and they need different responses: the game was **patched** since that row was
> written (then the row needs a date and a re-test note), or the row was **wrong from the start**
> (then every conclusion drawn from "TQ2 is our CPN title" needs re-checking — including the claim,
> made earlier today in commits `58ddf76` and `b281ca1`, that TQ2 is U2's verification vehicle).
> **Do not treat that row as evidence until this is settled.**
>
> **Solarpunk (UE5.7) — 2026-08-14: measurably NOT CasePreservingName.** A first sample 60 s after
> injection returned `case_preserving=false` with `probe_ran=false` — i.e. nothing, the probe had not
> run yet. **Re-queried later on the same still-running process: `case_preserving=false,
> validated=true, probe_ran=true`** — a real measurement. Solarpunk joins TQ2 as a confirmed non-CPN
> title. (The log is misleading here and that is filed as audit G7: its only `DynOff:` summary still
> says `validated=NO (DEFAULTS)` because it is never re-emitted after the later validation.)
>
> **Method note worth keeping:** the first sample was *real* but was not a *verdict*. `probe_ran` is
> the field that separates the two, and reading `case_preserving` without it produces a confident
> wrong answer in either direction.
>
> **U2 therefore has no known verification vehicle right now.** Three candidates are exhausted: TQ2
> is measurably not CPN, Solarpunk is indeterminate, and DumperTest cannot be (engine flag). Options:
> sweep other titles (`case_preserving` is one `get_offsets` call each, so this is cheap), or build UE
> from source with the flag on and repackage DumperTest. Until then U2 stands on the unit tests and
> code review only.
>
> **Sweep, title 3 of N — DQ7R is measurably NOT CPN `[DQ7R-PIPE-2026-08-17]`.** `get_offsets` on the
> live process returned **`case_preserving=false, probe_ran=true, validated=true`** — a verdict, not a
> sample, because `probe_ran` is set (the method note above). Population of confirmed non-CPN titles
> is now **TQ2 · Solarpunk · DQ7R**; still zero CPN titles found. Per the register's own priority
> rule, that growing absence is itself the signal and keeps U2 LOW.
>
> ### ⬜ Sweep completed to 9 titles 2026-08-17 `[SWEEP-2026-08-17]` — still ZERO CPN
>
> Six more titles, each a launch + one `get_offsets`, every one with `probe_ran=true` so every one is
> a verdict rather than a sample:
>
> | title | UE | detected via | objects | `case_preserving` | `validated` | GObj / GNames / GWorld |
> |---|---|---|---|---|---|---|
> | DQ7R | 427 | **memory Tier 1** | 149,497 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | DQ I&II HD-2D | 427 | **memory Tier 1** | 104,867 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | EVERSPACE™ | 420 | PE | 191,363 | false | true | **`G42_4` / `CT3`** / `TQ_1` |
> | The Artisan of Glimmith | 427 | PE | 24,132 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | Lushfoil Photography Sim | 506 | PE | 58,617 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | Manor Lords | 505 | PE | 80,013 | false | true | `ES53_1` / `V8` / **`SP57_1`** |
> | SEED BATTLE DESTINY REMASTERED | 427 | PE | 26,113 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | DragonSword Awakening *(injected)* | 504 | **cached, `rev=5`** | 72,604 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | Star Trek Voyager *(injected)* | 506 | PE | 46,994 | false | true | **`V13`** / `V8` / `TQ_1` |
> | Light Maze *(injected)* | 500 | PE | 14,958 | false | true | **`V13`** / `V8` / `TQ_1` |
>
> The last three carry **no proxy**, so they were injected through the panel's own *Inject into
> running game…* — which also exercises that button on three unrelated titles.
>
> **U2: TWELVE confirmed non-CPN titles, zero CPN** (TQ2 · Solarpunk · DQ7R · DQ I&II · EVERSPACE ·
> Geri · Lushfoil · Manor Lords · SEED · DSA · STVoyager · Light Maze), spanning **UE 4.20 · 4.27 ·
> 5.0 · 5.4 · 5.5 · 5.6 · 5.7**. The population is no longer thin enough to call unrepresentative,
> and the register's own rule — *no environment to test on ⇒ LOW* — now rests on a real sample rather
> than an assumption.
>
> **G8/G9 step 2 corroborated again, on DSA:** `UE Version = 504 (cached, rev=5, detected=yes,
> lowConf=no) — skipped DetectVersion`. The rev stamp is written back and honoured on a second title.
>
> **G1/X3 gains ten more clean hosts and still no amber one.** Every title returned
> `validated=true` with **no `unmeasured` key at all**. Twelve titles, twelve negative controls: the
> partial-offset-failure branch is looking genuinely rare rather than merely unvisited, and screening
> for it stays a single `get_offsets` per title.
>
> ⭐ **A control for the `lowConfidence` rule fell out for free.** Five of these are `tier=1,
> lowConfidence=**no**` with `DetectPublisher: no thumbprint match`, against DQ7R / DQ I&II at
> `tier=1, lowConfidence=**yes**` under `SQUARE_ENIX`. Same tier, opposite flag, publisher the only
> difference — which is exactly what the source says drives it, now observed rather than only read.
>
> **G12 discovery value — four distinct pattern families across twelve titles:** EVERSPACE (UE 4.20) is the only title using `GOBJ_G42_4` + `GNAM_CT3`, and
> Manor Lords the only one on `GWLD_SP57_1`, and STVoyager + Light Maze the only two on `GOBJ_V13`.
> **All twelve resolved all three pointers; no failures.**
>
> ⛔ **OCTOPATH TRAVELER could not be swept at all** — its `version.dll` proxy never loads. ✅ **RESOLVED
> 2026-08-18: use the `winmm.dll` proxy** (`[OCTOPATH-G2T3-2026-08-18]`) — 180/180 exports forwarded,
> full clean scan, 273,956 objects. See the
> `[PROXYLOAD-2026-08-17]` finding; it needs a different flavour or direct injection.
>
> **Incidental — D1/U3 CONFIRMED LIVE as still broken (not yet fixed).** `Map_IntToVec3f` renders as
> `f:[6203.0000]`: one float, the **last** one. The raw hex holds all three correct values, so the
> loss is in `InterpretValue`'s 8-byte "vtable preamble" skip — 12-byte struct − 8 = one float. U3
> moves from inferred to observed.

The remaining unchecked boxes below are superseded by the table above except where noted; U2 and the
`TSet`/`UDataTable` no-regression check still stand.

> ### ✅ THE UI HALF RAN 2026-08-18 — DumperTest Development, dist 3262, CE attached
>
> Every address below was checked against an **independent read of the process's own bytes**
> (`tools/verify/read_mem.py`), never against another number the UI computed — the map's data pointer
> comes from the `TMap` property's first 8 bytes, so the expected value cannot be derived from the
> observed one (working-lessons §1.10a).
>
> | row | verdict | evidence |
> |---|---|---|
> | **MG1** | ✅ | `Map_I64ToI32` property @`0x1F144477D88` → data ptr **`0x1F16D348980`** (ArrayNum 3, ArrayMax 4). UI element[1] Address = **`0x1F16D3489A0`** = ptr + 24 + 8. The old stride-20 arithmetic gives `0x1F16D34899C`, which is *not* what is shown. Element offsets 0x0/0x18/0x30 = stride 24. |
> | **MG3** | ✅ | `Map_IntToVec3f` property @`0x1F144477E28` → data ptr **`0x1F172A9E3E0`**. UI element[0] Address = **`0x1F172A9E3E4` = ptr + 4**, not +8. The 12 bytes there decode as **(6201.0, 6202.0, 6203.0)** — matching the row's `{X=6201, Y=6202, Z=…}`. This is the `MinAlignment` read. |
> | **U1 / V1** | ✅ | The two consumers of the element address both aim at the **value**. In-place edit of element[1] → status `Written: [1] 600000000002 = 7777`; memory at the element base then reads `02 70 C9 B2 8B 00 00 00 | 61 1E 00 00` — key **600000000002 intact**, value **7777** written at +8. `+CE` pushed `1F16D3489A0 / 4 Bytes / 7777` — the value address and the value's width, not the int64 key. |
> | **V1** (freeze control) | ✅ | Ticking that CE record and changing it to **1234** wrote `D2 04 00 00` at +8 while the key stayed `02 70 C9 B2 8B 00 00 00` across repeated freeze writes. The pre-fix bug wrote the user's 4 bytes over the key. |
> | **MG2** | 🟡 PARTIAL | `Set_Big` sparse array: **ArrayNum = 200**, UI header **`{Set: 199}`** — so `NumFreeIndices` is being subtracted and the count is no longer inflated. ⚠ The *rows-equal-count* half is **not decidable here** — see the cap finding below. |
> | **A2** | ✅ | This is the real A2 predicate and `Set_Big` satisfies it exactly. 200 slots > the 128 inline `TBitArray` bits, so the allocation **has spilled to the heap**; the freed slot is at **index 5**, i.e. inside the window the stale inline words used to cover. The walker shows `[4] 9004 → [6] 9006` — **[5] is absent**, and memory confirms slot 5 holds `FF FF FF FF FF FF FF FF 2D 00 00 00`, the sparse-array free-list links, not an element. Pre-fix this read as allocated and rendered a dead element. |
> | **TSet / UDataTable no-regression** | ⬜ **NOT TESTED — do not record as passing** | `TSet<int32>` (`Set_Int`, `Set_Big`) and `TSet<FStruct>` (`Set_Struct`) resolve, but the row asks for **`TSet<FName>` / `TSet<UObject*>` and a `UDataTable`**, and DumperTest ships **none of the three** (`Set_` filter returns exactly 3 matches, all covered above). Needs a real game. |
> | **U2** | ⬜ still open | needs a `CasePreservingName: YES` title; unchanged by this sitting. |

### ✅ FIXED 2026-08-19 `[CONTAINERCAP-2026-08-18]` — container drill-down now discloses "showing N of M" when capped

**Was.** `Set_Big` drilled to a grid whose last row is `[128]` under breadcrumb
`SetBig {Set: 199, IntProperty}` — nothing distinguished a complete 128-entry set from the first 128
of 199, so a user who expanded a 500-entry `TMap`, missed an item, and concluded it wasn't there was
misled. `Constants.DefaultArrayLimit = 128` (surfaced as the toolbar **Array Limit** slider /
`ArrayLimitExponent`) caps the walk; the cap is correct — the *silence* was the defect.

**Fix (client-only, no protocol change).** The DLL already sends BOTH the true total
(`set_count` / `map_count` / array `count`, `Fern.cpp:1448-1602`) and the capped element list, so the
UI now compares them. New pure helper `ContainerTruncation` (`ui/UE5DumpUI/Core/ContainerTruncation.cs`)
drives three disclosures, ALL empty on the non-truncated common case (no noise): the drill breadcrumb
label and the panel header (`CurrentObjectName`) gain a `⚠ showing 128 of 199` suffix, and `StatusText`
points at the **Array Limit** slider. Wired into `NavigateTo{Array,Map,Set}Container` +
`Populate{Array,Map,Set}ContainerFields` in `LiveWalkerViewModel`. Scalar arrays are re-fetched in full
on drill, so they correctly show no badge; only `TSet` / `TMap` / pointer-`TArray` (inline preview)
truncate. Covered by `ui/UE5DumpUI.Tests/ContainerTruncationTests.cs` (pure helper + real-VM drill per
container kind, truncated & full).

**Verify in-game (verify only — no code):**

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Live Walker → drill into a `TSet` / `TMap` / `TArray<obj>` with **> 128** entries | Breadcrumb AND header read `⚠ showing 128 of N`; a status line names the **Array Limit** slider |
| 2 | Drill into a container with **≤ 128** entries | No `showing…` badge anywhere and no status line (common case stays clean) |
| 3 | Raise the toolbar **Array Limit** slider, re-open the same big container | The shown count rises (e.g. `showing 256 of N`); once the cap ≥ N the badge disappears entirely |
- ⬜ **`TArray<FName>` / `TMap<FName,V>` on a CasePreservingName game (U2).** Needs a UE 5.5+/5.7
  title where `Genau` logs `CasePreservingName: YES` (e.g. Titan Quest II). Expand any actor's `Tags`.
  Before the fix `InferScalarSize` forced the stride to 8 against the engine's real 16, so every
  element but the first was read from the middle of its predecessor.
- 🟡 **PARTIAL `[DUMPERTEST-LOG-2026-08-17]` — A `TMap`/`TSet` whose ELEMSIZE reads garbage no longer
  wedges the walk (U1).** The **passive half PASSES**: every `KeySz=`/`ValSz=` in the DumperTest
  `walk-0.log` is plausible (8/4, 4/4, 8/4, 16/4, 4/12) — nothing like `1073742336`.
  ⚠ **The degraded branch itself is NOT TESTED and must not be recorded as passing.** All five maps
  read cleanly (`Read 3/3 map entries … skipped 0 unallocated` on each), so
  `Cannot read map elements for '%s'` (`Ubel.cpp`, `Sein::Warn`) structurally cannot fire here — and
  this file already says the case is hard to force deliberately. The "no multi-second freeze" half is
  a UI-perceived claim and is likewise unmeasured from a log.
- ✅ **DONE `[DUMPERTEST-LOG-2026-08-17]` — `walk-0.log` `Stride=` values are correct.** Grepped
  `WALK:MapP` in `Logs\DumperTest\walk-0.log`; all five maps present with `ValOff=` and `Stride=`:

  | field | KeySz/ValSz | ValOff | Stride | the defect would have shown |
  |---|---|---|---|---|
  | `Map_I64ToI32` | 8 / 4 | 8 | **24** | 20 — the core MG1 witness |
  | `Map_StrToInt` | 16 / 4 | 16 | **32** | 28 — second witness, different arithmetic |
  | `Map_IntToVec3f` | 4 / 12 | **4** | 24 | value at +8; the only one wrong at element 0 (MG3) |
  | `Map_NameToInt` | 8 / 4 | 8 | 20 | unchanged by design |
  | `Map_IntToFloat` | 4 / 4 | 4 | 16 | unchanged by design |

  The two witnesses disagree in their arithmetic, so one wrong assumption cannot satisfy both, and
  `Map_IntToVec3f` exercises the `UScriptStruct::MinAlignment` read specifically. Log is from
  build `5ef4c2b` (1.0.0.2812) — the DLL-side commit for this fix, so it is in scope.
  **This is the log half only**; the UI-side arithmetic remains open in the rows above.

### 🔴 NEW 2026-08-11 — `executeCodeEx` finite timeout + reason capture (build 2792)

Shipped in [dev-log.md](dev-log.md) build 2792, **never run against a game**. Three call sites
changed: `scripts/ue5_dissect.lua`'s `callDLL` (was an INFINITE timeout, now 5000 ms),
`CeLuaHygiene.AppendCallDllHelper`, and `UE5CEDumper.CT`'s `ue5_callDLL`. CE-side model:
[ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §13.

**Free from any ordinary session** (no special setup — just use the tool once):

- ✅ **DONE 2026-08-18 — Dissect still builds a structure**, DumperTest Development, dist 3262.
  `dissect.createFromPath('/Script/DumperTest.DumperTestActor')` returned a CE structure named
  **`DumperTestActor` with 193 elements**.
  ⚠ The row warns that "a structure window appeared" is not a pass, so the check was **per field**:
  seven elements were looked up **by offset** and compared with what Live Walker independently
  reports, across five different type shapes — **matched 7, missed 0**.

  | offset | element | CE `Vartype` / `Bytesize` |
  |---|---|---|
  | `0x5E` | `UpdateOverlapsMethodDuringLevelStreaming` | 0 / 1 |
  | `0x17C` | `PhysicsReplicationMode` | 0 / 1 |
  | `0x478` | `Map_I64ToI32` | 12 / 8 (pointer stub) |
  | `0x518` | `Map_IntToVec3f` | 12 / 8 |
  | `0x568` | `Set_Big` | 12 / 8 |
  | `0x608` | `Opt_Int_Set` | 2 / 4 |
  | `0x671` | `bPlainBool` | 0 / 1 |

  Since a class walk runs the changed `callDLL` **once per field**, 193 successful elements is 193
  successful `executeCodeEx` round trips through the 5000 ms path.
  **This also closes AA7 step 1** (`createFromClass` succeeds and the structure appears in CE's list).
- ✅ **DONE 2026-08-18 — No stray warnings on a healthy run.** **Zero** `[UE5Dissect WARN]` lines in
  CE's Lua console across the whole dissect (and across a second run of the same class for the
  by-offset comparison). `warn()` is ungated, so this is a real absence, not a suppressed one.
- ✅ **DONE 2026-08-18 — `.CT` disable still tears down.** Evidence and timings are in the
  `Fern::Stop` block above: `init-0.log` shows `UE5_Shutdown: Cleaning up...` →
  `[Grausam] Foreground lock DISABLED` → `[SENSE] Diagnostics counters reset`, and CE's console
  printed `[UE5Dump] UE5 Dumper stopped.` So `ue5_callDLL` really reached `UE5_Shutdown` — not the
  audit #4 B1 shape where a clean teardown is reported and never happens.

**Needs deliberate action:**

- ✅ **DONE 2026-08-18 — 5000 ms is comfortably enough.** Dissected `/Script/Engine.Actor` on
  **Elliot** (85,068 objects, dist 3262, `dxgi` proxy) from CE's Lua Engine: **844 ms** end to end
  for `dofile` + `createFromPath`, producing `name=Actor elements=129`, with **no** `Execution
  timeout` and **no** `[UE5Dissect WARN]` line. That whole figure *includes* the `UE5_FindObject`
  GObjects scan this row names as the slow candidate, so the budget has ~6× headroom here.
  ⚠ Conditions: Elliot at its **main menu**, 85 K objects — not the 250 K+ the row asks for.
  DragonSword would still be a stronger sample, so treat this as "no sign of strain at 85 K"
  rather than a bound proven at 250 K.
- ⛔ **The prescribed negative control DOES NOT WORK — attempted 2026-08-18, do not re-run as
  written.** Suspending the game process does **not** make `executeCodeEx` time out; it succeeds
  normally, so anyone following this row would see "no timeout" and wrongly conclude the path is
  fine while never having exercised it.

  | attempt | suspension | result |
  |---|---|---|
  | 1 | short (resume raced the call) | `elapsed=984 ms  ok=true` — confounded, discarded |
  | 2 | **held ~18 s**, call issued ~3 s in | **`elapsed=1422 ms  ok=true`**, structure built |

  Attempt 2 is decisive: the call started and finished entirely inside a suspension that provably
  outlasted it by an order of magnitude.

  **Why:** CE's `executeCodeEx` runs the target function on a **newly created remote thread**.
  `NtSuspendProcess` suspends the threads that *already exist*; a thread created afterwards runs.
  And the dissect's calls (`UE5_FindObject`, the walk exports) are pure memory work that needs no
  game thread, so nothing blocks.

  **This is the same trap the run plan already flags for `AA14–AA20` step 5** — *"needs the game
  thread only suspended; CE's whole-process pause hits the status-0 branch, not the 0xFF branch
  under test"*. Same cause, second row.

  **A working induction must make the call need something that is actually stopped**: suspend the
  **game thread specifically** and invoke through `Stark`'s ProcessEvent dispatch, which cannot
  complete without it. Rewrite the row that way before spending another session on it.

### 🟡 PARTIAL 2026-08-10 — GObjects layout fix (build 2782), DragonSword Awakening

**Verified in-game on build 2786, same day** — see [test-games.md](test-games.md) for the log lines
and numbers. Strict tier accepted `preset Default` with `Max=10551296` (impossible under the old
8.4 M cap, so the ceiling fix is directly proven); live `NumElements` **266 614 → 274 900** within
one session; the original repro (`DsClientLocalPlayer.<raw@0xAC0>` = 9338, Native-C) now returns.

**What is still unverified**, and why it is not just pedantry: that run resolved the `ObjObjects`
anchor (`GOBJ_V13` → `0x7FF62529F8C0`), where even the OLD relaxed `A/C` row reads the correct
`NumElements`. The second half of the fix — **relaxed row B stealing a row-E layout at the
`FUObjectArray` base anchor** — was therefore never exercised. The winning pattern/anchor is not
stable across runs on this title (build 2780 got the base via `GOBJ_ES53_1`), so a later session
can still land there.

- ⬜ On any future DragonSword session that logs GObjects at a base anchor (address ending `…F8B0`
  rather than `…F8C0`), confirm the preset line reads **`UE5-Extended`**, not `relaxed B`.
- ⬜ Regression watch across the other tested titles, since the relaxed tier gained a
  chunk-consistent first pass: confirm nothing that resolved before now logs
  `Could not detect layout, using default`. Relaxed pass 2 is byte-identical to the old
  behaviour, so this should be a formality.

### 🔴 NEW 2026-08-05 — two defects the DumperTest sample found on its first real use

**D3 — `FUObjectItem` stride detected as HALF its real size on a Development build.** Effort **M** ·
Risk med. **This is the one to fix next: it is upstream of D2 and of every result on this config.**

The tell is arithmetic, not a guess. `UE5_Init` prints its name-sanity probes:

```
Shipping      Sanity obj[0] … obj[1] … obj[2]      -> 10/10 resolved
Development   Sanity obj[0] … obj[2] … obj[4]      ->  5/10 resolved
```

**Only even indices resolve.** The Object Tree agrees to the decimal: *"12,588 named objects of
25,175 total, **50.0%**"*. Reading with a stride of 16 where the real one is 32 makes every second
entry garbage, and 50.0% is what that looks like.

The detector already said so and nothing acted on it:
`ObjectArray: FUObjectItem size tentatively set to 16 bytes, object-ptr offset +0x00 (**only 27
items validated**)`. On a healthy game that validation count is in the thousands. **"Tentative" plus
27 was the warning; the scan continued as though it were an answer.**

**It also explains D2's newest symptom.** With half the pool garbage, the Group Scan's
`Game classes only` / `Skip Engine/System noise` filter rejects nearly everything —
`0 matching objects in 17 ms (**scanned 0 objects**, 13 classes)` where the same scan on Shipping
walked 1731. So the group-scan investigation must **re-run on Shipping, or after D3 is fixed**;
measurements taken on this config are measuring the stride, not the matcher.

> ### ✅ Fixed build 2673 — **32 was not in the candidate list**
>
> `Aura.cpp`'s sweep tried `{ 16, 24, 20 }`. A real 32-byte item was therefore not a near-miss, it
> was **undetectable** — and worse, undetectable in a way that looks like partial success: a stride
> that DIVIDES the real one still lands on a genuine object every k-th probe, so 16 validated half
> the pool and the sweep settled there "tentatively".
>
> Ordering does not decide the winner (`ProbeAllStrides` scores every candidate and takes the best),
> so adding 32 is enough: against the real stride it scores `named ≈ all / bad ≈ 0`, while the alias
> scores `named ≈ bad ≈ half`.
>
> **The "tentative" warning now states its cost.** Its old wording read as routine and the scan
> carried on as though it were an answer. When the validated count is under a quarter of the probe
> budget it now says so as an ERROR, names the denominator (200 — *"27 items validated"* means
> nothing without it), and points at the actual cause: *a multiple of this stride would validate all
> of them, and a round "N% named" in the object tree is that alias.*
>
> **Verify:** re-run the Development package. **PASS** = `FUObjectItem size detected as 32 bytes`,
> `Name sanity: 10/10`, and an Object Tree that reads 100% named rather than 50.0%.
>
> ### ✅ VERIFIED — the evidence was already on disk, filed 2026-08-06
>
> Six post-2673 runs of the Development package, `Logs\DumperTest\`, all identical:
>
> ```
> offsets-*.log   ObjectArray: FUObjectItem size detected as 32 bytes
>                 (200 items with valid names, 200 total valid, 0 bad)
> init-*.log      UE5_Init: Name sanity: 10/10 objects resolved
> ```
>
> `detected as` (not `tentatively set to`), **200/200 validated with 0 bad** against a probe budget
> of 200, and 10/10 name sanity where the broken run resolved only the even indices at 5/10. The
> third criterion — the object tree reading 100% rather than 50.0% — follows from the same run:
> D2's group scan verified on this package, and the stride alias is precisely what had made that
> scan walk **0** objects.
>
> **Nobody had to re-run anything** — the ⬜ outlived its own answer by a day because the check was
> filed as "re-run the Development package" rather than as a grep against logs the package had
> already written. Where a marker is passive, state the grep, not the run.
>
> ### ✅ RE-CONFIRMED ON THE CURRENT PACKAGE `[DUMPERTEST-LOG-2026-08-17]`
>
> The evidence above is from the package built **2026-08-05**, and the packages were **rebuilt
> 2026-08-14** to add the audit-#5 containers — so it no longer described the binary on disk. Re-run
> as greps against `Logs\DumperTest\`, all five log criteria still hold on the 2026-08-14 build:
>
> * `FUObjectItem size detected as 32 bytes (200 items with valid names, 200 total valid, 0 bad)` —
>   `detected as`, not `tentatively set to`; 200/200 against a 200 budget, 0 bad.
> * `UE5_Init: Name sanity: 10/10 objects resolved` — not the 5/10 that means a halved stride.
> * `[SCAN:GObj] Module anchor set to 'DumperTest.exe'`.
> * **D1 specifically:** all **15** `REFUSED` lines name `EOSSDK-Win64-Shipping.dll`, and GNames
>   still validates at `0x7FF63CD568C0` — the same `0x7FF63C…` image as GObjects at `0x7FF63CE43620`.
>   The anchor rule rejected the decoys *without* costing the real answer, which is the pairing that
>   makes this evidence rather than either half alone. One run resolved it by `aob` and another by
>   `pointer_scan`; both in-module.
> * `[SUMMARY] DynOff: CPN=no FProp=yes TagFFV=yes Outer=+0x20 validated=yes`, and **zero**
>   `does not deref to a UWorld` in the whole folder.
>
> ~~⚠ **Still not directly observed: the Object Tree header ratio.**~~ **✅ NOW READ OFF THE SCREEN
> 2026-08-17 `[GRP4-UI-2026-08-17]`.** UE5DumpUI 1.0.0.3262 connected to **DumperTest Development** —
> the configuration both defects lived in — shows:
> ```
> Object Tree   Objects: 25,179 (showing 5,000)
> Loaded 25,179 named objects (of 25,179 total, 100.0%)
> ```
> **25,179 / 25,179 = 100.0%.** This is the reading the step wanted and the one the log could not
> supply, since `ObjectTreeViewModel` builds the header from a different denominator.
>
> It discriminates because **both** defects would move this number and in opposite-looking ways: D3's
> halved `FUObjectItem` stride would walk roughly half the pool, and D1's GNames landing in
> `EOSSDK-Win64-Shipping.dll` would leave most entries unnamed. A ratio of exactly 100.0% on
> Development rules out both. *(An earlier capture during load read `25,172 / 25,179` — read the
> header only after the tree finishes loading, or the shortfall is just the progress bar.)*


Both came out of the config-only A/B (**same source, Shipping vs Development**) that this file has
called the highest-value first cell since 2026-07-29. It produced them on day one.

**D1 — GNames resolves into `EOSSDK-Win64-Shipping.dll` on a Development package.** ✅ **FIXED
build 2661** — see the fix note at the end of this item. Effort **M** · Risk med. On the Shipping build of the *same source* everything resolves cleanly
(`validated=yes`, GWorld fine). On Development:

```
[GNames] GNAM_SF_2: 1 match(es), none validated
AOBScanAllModules: 2 matches in '...\Engine\Binaries\Win64\EOSSDK-Win64-Shipping.dll'
[GNames] GNAM_SAT425_3: 2 matches (multi-module), validated -> 0x7FFCEF5F8FC0
```

GObjects is at `0x7FF67517D5A0` (inside the game exe); GNames lands at `0x7FFCEF5F8FC0`, **a
different module entirely**. On a monolithic build that cannot be right. Every in-exe GNames
pattern missed — the tables are Shipping-tuned — and the multi-module fallback then matched a
data pattern inside a **third-party SDK DLL** whose pointer happens to reach a plausible name pool,
so `ValidateGNames` accepted it.

**The whole failure chain is downstream of this one address:**
`Cannot find Guid or Vector struct` → `validated=NO (DEFAULTS)` → the FField/FProperty offsets stay
at defaults that are wrong for this build (`Next=+0x18/Name=+0x20` vs Shipping's `+0x20/+0x28`) →
`GWorld does not deref to a UWorld — recovery failed` → **Start-from-GWorld and Value Search both
fail.** One misresolution, four visible symptoms.

*Multi-module is deliberate and must stay* — modular builds put GNames in `CoreUObject`, which is
why the winning pattern is named `GNAM_SAT425` (Satisfactory 4.25). The fix is not to remove it but
to **rank same-module-as-GObjects first, and refuse an unrelated third-party DLL** (`EOSSDK`,
redistributables) when GObjects resolved inside the main executable.

> ### ✅ Fixed build 2661 — a module ANCHOR, not a denylist
>
> GObjects resolves first, so by the time GNames/GWorld/GEngine scan we already know which module
> the engine's globals live in. `Genau.cpp` now records that as `s_moduleAnchor` (set however
> GObjects was found — the data-scan fallback anchors as well as the AOB), and the multi-module
> pass uses it two ways: candidates in the anchor's module are tried **first**, and if the anchor is
> the **main executable** — i.e. the build is monolithic — a candidate resolving anywhere else is
> **refused outright**, naming the module it came from.
>
> **Deliberately not a list of SDK names.** This repo has been bitten three times by a fix verified
> against its own list rather than against the world (B34's three CE filenames, B14's seven thread
> procs, B47's session). *"The engine globals are all in one module unless the build is modular"* is
> structural, needs no maintenance, and cannot go stale as new redistributables appear.
>
> **Multi-module support is untouched for modular builds** — a real modular build puts GNames in
> `CoreUObject.dll`, which is precisely why the pattern that mis-won here is named `GNAM_SAT425`
> (Satisfactory 4.25). When the anchor is a DLL, the fix only reorders; it refuses nothing.
>
> **① Log-derivable, and the target is on disk.** Re-run the DumperTest **Development** package.
> **PASS** = `Module anchor set to 'DumperTest.exe'`, then GNames resolving to an address in the
> same `0x7FF6…` range as GObjects, `validated=yes` in the DynOff summary, and Start-from-GWorld +
> Value Search working. **FAIL, but informatively** = `REFUSED 0x… — it is in 'EOSSDK-Win64-Shipping.dll'`
> followed by no GNames at all, which would mean the in-executable patterns genuinely have no
> coverage for a UE 5.4 Development build and the answer is a new AOB, not a ranking rule.
>
> ### ✅ VERIFIED 2026-08-05 14:24 — first re-run, and it corrected itself in one pass
>
> ```
> Module anchor set to 'DumperTest.exe' — later targets must resolve there unless this build is modular
> [GNames] GNAM_SF_1: REFUSED 0x7FFCEF5F8FC0 — it is in 'EOSSDK-Win64-Shipping.dll' ...
> [GNames] GNAM_V1: 166 matches, validated -> 0x7FF675090840
> ```
>
> | | before | after |
> |---|---|---|
> | GNames | `0x7FFCEF5F8FC0` (EOSSDK) | **`0x7FF675090840`** — same module as GObjects |
> | DynOff | `validated=NO (DEFAULTS)` | **`validated=yes`** |
> | GWorld | `does not deref to a UWorld`, recovery failed | resolves, no warning |
>
> `FField Next=+0x18 / FProp Offset=+0x44` are now **validated**, which settles a second question:
> those are the genuine offsets for a Development build and differ from Shipping's `+0x20/+0x4C`
> legitimately. Five refused patterns later, `GNAM_V1` won in batch 4 with the correct address.
>
> **Two follow-ups the same run exposed, both fixed in build 2666:**
> - the refusal logged **once per match** — 8–11 identical lines per pattern, five patterns deep.
>   Now one line per pattern carrying the count.
> - **the UI reported "Connection Error / The operation has timed out" on a successful injection.**
>   The injected DLL scans BEFORE opening its pipe (the proxy path is the opposite), so the pipe
>   appeared **8.8 s** after injection — 1 s auto-start delay plus a 7.8 s scan that got *longer*
>   because refusing EOSSDK made it run all 31 patterns. The UI attempted the connect exactly once,
>   immediately. It now retries for 45 s, asking an `IsConnectedProbe` whether it worked instead of
>   assuming, and says which attempt it is on.

**D2 — Group Scan cannot see the object's own scalar UPROPERTYs.** ✅ **VERIFIED 2026-08-17
`[D2-PIPE-2026-08-17]`** — steps 1-3 of the operational checklist all pass on DumperTest Development,
build 3262, over the pipe. Effort **M** · Risk med.

> ### ✅ VERIFIED 2026-08-17 — the object's OWN fields are what matched
>
> **Step 1.** `begin_group_scan` with two `Exact` slots, `1234567` and `424242`, returns 2 candidates,
> and the live one's slots are **`I32` @ offset 1600** and **`FrozenInt` @ offset 1708** — the
> derived class's own scalars, which is precisely what the defect could not see. Not
> `PrimaryActorTick.*`, not `CustomTimeDilation`. Both offsets agree independently with the
> `walk_class` reply taken the same session (`I32` 1600, `FrozenInt` 1708), so two detectors concur.
> `match_count` is on the wire as documented.
>
> **Step 3 — the "groups need `Unchanged`" case, and it landed exactly as written.** A broad first
> scan (`Bigger 0` / `Exact 0`) gives **366** objects; a refine to `Changed` / `Unchanged` leaves
> **2**, one of which is `DumperTestActor_0` showing
> **`Health.CurrentValue=79`** and **`PrimaryActorTick.TickInterval=0`** — the row this checklist
> predicted in advance. `Health.CurrentValue` falls 1 Hz and `TickInterval` never moves, so the pair
> is the hard case rather than an accident.
>
> **Step 2 — the old `perSlotCap` of 8 is provably gone.** The first refine logged
> `leaves entered=` 2/3/4/8/**9**; a deliberately leaf-heavy scan (`Exact 0` on both slots, 432
> objects) pushed it to 14 and **20**. A hard cap at 8 cannot produce a 20, which is the discriminator
> — the raw magnitude is not, since `entered` is bounded by how many fields actually matched.
>
> ⚠ **Step 4 is NOT verified.** The `Leaves/slot` clamp (8–4096) is a client-side UI control and
> UE5DumpUI cannot currently be granted to computer-use. Its wire half does hold: none of these
> requests carried `per_slot_cap`, matching "absent unless the user moves the control".
>
> ### ✅ Step 4 SETTLED 2026-08-17 `[D2-UI-2026-08-17]` — but its PREMISE was wrong
>
> **The control is a `ComboBox`, not a NumericUpDown**
> ([ValueSearchPanel.axaml:550](../ui/UE5DumpUI/Views/ValueSearchPanel.axaml)), bound to
> `PerSlotCapChoices` — the ten powers of two from 8 to 4096
> ([ValueSearchViewModel.cs:79](../ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs)). **So an
> out-of-range value is unreachable from the UI and there is no clamp to test**; the `if (value <
> Min)` guard at `ValueSearchViewModel.cs:89-90` is a defensive backstop the interface cannot
> exercise. Confirmed by stepping the control: from `16`, four Downs lands on `256`
> (16→32→64→128→256), i.e. the enumeration is exactly as built. **Fix the step's wording, not the
> code.**
>
> **What the step should be checking, and it PASSES.** With `Leaves/slot` moved to **16**, a Group
> First Scan on DumperTest (`424242` + `100`) put it on the wire, and the *DLL's own* `pipe-0.log`
> recorded the request verbatim:
> ```json
> {"cmd":"begin_group_scan", … ,"deadline_ms":25000,"auto_skip_noise":true,"per_slot_cap":16,"id":2}
> ```
> At the 256 default it is omitted ([DumpService.cs:2244](../ui/UE5DumpUI/Services/DumpService.cs):
> `if (perSlotCap != Constants.GroupPerSlotCap)`), which is what the headless run above observed from
> the other side. **Both directions now have evidence.**
>
> ⭐ **The incidental result is the valuable one: a known AOT hazard does NOT bite here.** CLAUDE.md
> lists *"`ComboBox.SelectedItem` bound to a boxed value"* among the patterns that compile and run
> untrimmed and fail **only** after trimming. This control is exactly that —
> `SelectedItem="{Binding GroupPerSlotCap}"` over an `int` — and it was driven **on the AOT-trimmed
> `dist` binary** (256 → 16 → 256, selection honoured, value reaching the wire). One instance of the
> hazard class is therefore live-clear on 3262.
>
> *Also confirmed in passing:* the `(+N)` `match_count` annotation renders — both result rows read
> `FrozenInt=424242, NetUpdateFrequency=100 (+2)` / `(+3)`. Scan cost `83 ms` over 1,815 objects.

> ### ✅ D2 (樣本心跳) PASS 2026-08-17 `[GRP4-UI-2026-08-17]` — the DLL and the game agree to the digit
>
> The sample prints its own values on screen, so the panel can be checked against the *game's* opinion
> rather than against itself. Two readings **34 s apart** — Live Walker on `DumperTestActor_0` first,
> then the HUD:
>
> | field | DLL (Live Walker) | game HUD, +34 s | verdict |
> |---|---|---|---|
> | `FrozenInt` — *must NOT move* | **424242** (hex `32790600`) | **424242** | identical |
> | `Health.BaseValue` — *must NOT move* | **100** (hex `0000C842`) | **100** | identical |
> | `TickCount` — *climbs at 1 Hz* | **815** (hex `2F030000`) | **849** | +34 over a 34 s gap |
> | `F32_Ticking` — *falls 10.25/tick* | **600.75** (hex `00301644`) | **252.25** | 600.75 − 34×10.25 = **252.25 exactly** |
>
> **This is stronger than "the numbers look right".** The two frozen fields match to the digit, and
> the two moving fields match the sample's own documented rates **exactly** over the measured
> interval — including `F32_Ticking`, where an arithmetic slip of a single tick would show. Every hex
> column round-trips too (`0x32F` = 815, `0x00067932` = 424242, `0x44163000` = 600.75).
>
> *Conditions:* DumperTest Development, dist **3262**, windowed, ~34 s between the two captures, no
> wrap occurred in `F32_Ticking` during the interval (it falls from 600.75, and the wrap is far
> below).
>
> ⚠ **Only the first 5 candidates are logged**, by design (`[SCAN:grp]` debug, off the hot path), and
> only DROPPED ones appeared — the two survivors produced no `KEPT` line. Do not read the absence of
> a KEPT line as a failure.
On the Shipping package (where the pointers ARE correct), a Group First Scan over
`DumperTestActor_0` matched only **container elements and base-class fields**:

```
PrimaryActorTick.TickInterval=0, CustomTimeDilation=1     <- AActor's own
Set_Int[0][0]=1337   Map_NameToInt.Value[0][0]=111   Arr_Int[0][0]=10
```

Not one of `I32`(1234567), `FrozenInt`(424242), `TickCount`, `Health.*`, `Opt_*` — all plain
scalars declared on the derived class, all of which the **single-value** scan finds without trouble
(`Opt_Int_Set` @0x468, `Set_Int` @0x358). Because the only leaves recorded are ones that never
change, a follow-up `Changed`/`Decreased` refine returns **0**, which is what made this look like a
Mode-B problem for three rounds.

~~**Not a leaf cap:**~~ **It WAS a cap — just not the one I checked.** `Aura.cpp`'s `kLeafCap = 4096`
is fine; the one that bit is `Orden::MatchGroup`'s **`perSlotCap = 8`**.
**The sample is not at fault** — its on-screen heartbeat shows `frames=5971 TickCount=101` climbing
and `Health.CurrentValue` falling, so the values genuinely change.
**Sharpest repro, no timing involved:** Group First Scan, both slots `Exact` — `1234567` and
`424242`. Both are static UPROPERTYs on the same object.

> ### 🔬 NOT fixed — instrumented instead (build 2669), and the reason matters
>
> **Reading the code did not find it, and three hypotheses had already been written and abandoned
> against this bug's silence.** What the code says, all of it verified line by line:
> `CollectGroupLeaves` (`Aura.cpp:7686`) collects **every** direct and struct-nested numeric scalar —
> so `I32`/`TickCount`/`Health.*` *are* in the object block, and `CustomTimeDilation` appearing in
> the results proves that path runs. `emitGroupCandidate` (`:8175`) stores **all** leaves that
> satisfied each slot, not one representative, and seeds `prevValue` from the leaf bytes (`:8185`).
> `RefineGroupCandidates` (`:8367`) re-reads each stored leaf and compares prev-value predicates
> against its own `prevValue`. Every step is right on its own.
>
> So the refine now **counts why leaves die** instead of only saying "0 surviving", which is the
> same output for six different causes:
>
> ```
> RefineGroup cand[N]: DROPPED (a slot has no surviving leaf) | leaves entered=42 kept=0 |
>   dropped: unreadable=0 bad-width=0 no-target-for-width=0 predicate-said-no=42
> ```
>
> It also names the one cause that is invisible today — `GroupCandidateFeasible` rejecting a
> candidate because every slot matched **the same leaf**, so no *distinct* assignment exists. First
> 5 candidates only, `[SCAN:grp]` debug, off the hot path.
>
> **Next run answers it:** a Group First Scan then a `Changed` refine, then
> `grep "RefineGroup cand" pipe-0.log`. `predicate-said-no=<everything>` means the comparison is
> wrong; `entered=` far below the object's field count means the leaves were never stored; the
> DISTINCT-assignment verdict means the matcher, not the predicate. Those are three different fixes
> and the log now separates them.
> **✅ RUN 2026-08-17 `[D2-PIPE-2026-08-17]`** — see the block below; and ⚠ **that grep target is
> wrong**: the marker is emitted under `[SCAN:grp]`, so it lands in **`scan-0.log`**, not
> `pipe-0.log`. Grepping the file this line names returns nothing on a run that produced the lines.
>
> ### ✅ ANSWERED + FIXED build 2680 — the diagnostic named it on its first run
>
> ```
> RefineGroup cand[0]: DROPPED (a slot has no surviving leaf) | leaves entered=8 kept=0 |
>   dropped: unreadable=0 bad-width=0 no-target-for-width=0 predicate-said-no=8
> ```
>
> **`entered=8` IS the answer.** `Orden::MatchGroup` kept `perSlotCap = 8` satisfying leaves per
> slot — and leaves arrive in **field-declaration order, base class first**. On any `AActor` the
> first eight are `PrimaryActorTick.*`, `CustomTimeDilation` and friends, so a derived class's own
> fields — `I32`, `TickCount`, `Health.*`, `FrozenInt`, the ones a user actually searches for —
> **never made the list.** The kept list is also what the refine re-reads, so a `Changed` pass
> compared only never-changing engine fields and pruned all 618 candidates. The screen showed the
> same thing once the values were identical: *both* slots reporting `Set_Int[0][0]=1337`.
>
> **One list was serving two purposes.** The assignment check needs a handful; the refine needs
> everything. The cap now sizes for the refine (**256**), truncation is **reported** instead of
> silent, and it is an opt-in `per_slot_cap` on `begin_group_scan` (clamped 8–4096) so an object
> with unusually many numeric fields can be raised without a rebuild.
>
> Regression test `Test_Orden_PerSlotCap`: 40 satisfying leaves must all be kept, and an explicit
> small cap must both bound the list **and** set the truncation flag. 972 dll tests green.
>
> **UI control shipped (build 2690):** a `Leaves/slot:` NumericUpDown beside the Timeout slider,
> group-mode only, 8–4096 step 8, clamped in the VM and again in the DLL, attached to the wire only
> when moved off the default so existing captures stay byte-identical
> (`BeginGroupScanAsync_PerSlotCap_AttachedOnlyWhenMovedOffTheDefault`).
>
> ### ✅ VERIFIED 2026-08-05 — Mode B works
>
> `Changed` + `Unchanged` → **2 surviving objects**, and the row is the case the whole feature
> exists for: `DumperTestActor_0 — Health.CurrentValue=23, PrimaryActorTick.TickInterval=0`. One
> value moving, one holding still, in the same object.
>
> ### 🟡 Related, and NOT a scan bug: the row showed a leaf the filter had not matched
>
> Reported the same session: `FrozenInt=424242` never appeared in the list, and filtering for
> `424242` returned two rows that visibly contained no such value. Both are the same cause, and
> neither is a wrong result.
>
> The **filter** (`Radar.cpp` `BuildGroupOrderedView`) walks **every** leaf of every slot — class,
> defining class, field name and value. The **row renderer** (`Fern.cpp GroupCandidateToJson`)
> emitted `matches[0]`. So the filter was right, and the row was showing a *different leaf of the
> same candidate*. `FrozenInt` was in the kept set all along; `matches[0]` is base-class-first, so
> an `AActor` field always won the display slot.
>
> Audit #4's 4a root cause again — *the report and the reality computed by different code paths* —
> and this time the user was told to distrust a correct answer.
>
> **A second, worse form of the same thing (build 2695).** Each slot reported its own `matches[0]`,
> which is not an ASSIGNMENT: when two slots kept the same leaf first, the row read
> `PrimaryActorTick.TickInterval=0, PrimaryActorTick.TickInterval=0` — a value apparently paired
> with **itself**, which is exactly what `MatchGroup` forbids and `HasDistinctAssignment` had
> already proven impossible. The match was valid; the row was not showing it. Reported as *"找出來
> 的沒和其它數值配，是自己配自己"*. The renderer now claims leaves greedily across slots so the row
> is a real assignment.
>
> **This is also the answer to "Unchanged + Changed cannot find `Health.BaseValue` +
> `Health.CurrentValue`".** It can, and did — `BaseValue` was in the Unchanged slot's kept list all
> along, but `matches[0]` is base-class-first so `PrimaryActorTick.TickInterval` occupied the
> display. Not a design limitation, and nothing about the scan needed changing.
>
> **Fixed build 2690:** when a server-side filter is active the row reports the leaf that **matched
> it**, using the same helpers the filter uses (`GroupTextContainsCI` / `GroupSlotValueString`, now
> exported from `Radar` rather than duplicated). Each slot also carries `match_count`, so a row can
> no longer imply a candidate matched on one field when it matched on thirty.
>
> ### ✅ FIXED build 2719 — the FOURTH report of this shape, and the first fix that is not zero-sum
>
> Reported after 2701: *"`TickCount=NNN, FrozenInt=424242` 沒出現"*. It had matched. From that
> session's own `ui-pipe-0.log` (17:40:25, `query_group_candidates`):
>
> | slot | predicate | `match_count` | `matched_offsets` |
> |---|---|---|---|
> | 0 | Changed | 2 | `[1288, 1304]` |
> | 1 | Unchanged | 36 | `[52, 100, …, 1284, **1308**, 72]` |
>
> `1288` = `Health.CurrentValue` (named in the payload), `1304` = `0x518` = **TickCount**,
> `1308` = `0x51C` = **FrozenInt** — independently confirmed by the same session's
> `ScrollToFieldByOffset: offset 0x51C -> field 'FrozenInt'`. Two valid assignments, one row;
> 2701's same-struct preference gave the row to the `Health` pair.
>
> **Every earlier fix changed WHICH witness wins, which is zero-sum** — whichever pairing is
> promoted, the other reads as missing. This one makes the others *visible* instead:
>
> - **`query_group_slot_leaves`** (new pipe command) names every leaf one slot of one candidate
>   kept, on demand. Before it, the only trace of the other 35 was `matched_offsets`, and a raw
>   integer cannot tell anyone that 1308 is `FrozenInt`. Each leaf comes back as a full slot
>   match, so Live / Addr / Pivot / Locate act on it unchanged. UI: **All fields** in the
>   expanded row.
> - **`match_count` is finally parsed** (on the wire since 2690, read nowhere). The master row
>   now reads `Health.CurrentValue=19 (+1), Health.BaseValue=100 (+35)`, and the detail row
>   `… → 0x504 — 1 of 36 matching field(s)` instead of the old nameless
>   `= unchanged: 36 candidate offset(s)`. Counted through `MatchingFieldCount`, so Snapshot
>   Group / SPC Group / Class Pivot get the same annotation from their own offset lists rather
>   than repeating this as a separate report.
> - **The witness rule moved to `Radar::PickGroupWitnessAssignment`**, beside the filter it must
>   agree with. It lived in `Fern.cpp`'s JSON encoder, which no test target compiles — that is
>   *why* it kept drifting. Now covered by `Test_Radar_PickGroupWitnessAssignment` (sibling
>   preference, filter-by-name, filter-by-value, distinctness, empty slot, out-of-range
>   descriptor). Audit #4 root cause 4a, closed at the source: one encoder (`GroupLeafToJson`),
>   one decoder (`ParseGroupSlotLeaf`), one picker.
>
> **Four defects found while building it — and one of them was a fix that was itself wrong.**
> `deep` gives one candidate PER CONTAINER BLOCK sharing one instance address, so a lookup by
> `instance_addr` alone answers an expanded deep row with another block's fields — the request
> now carries an optional `leaf_addr` tie-breaker (a candidate *index* cannot work: a refine
> rebuilds the vector), and an unmatched hint with several candidates at that address returns
> `stale_leaf_addr` instead of guessing. `query_group_slot_leaves` was missing from
> `LaneRoutingPipeClient.BulkCommands`, which would have blocked Live Walker behind a running
> refine holding `GroupSessionManager::mu_`.
>
> A deep leaf's `offset` is 0 by construction, so `→ 0x0` was being printed as its location.
> **The first fix — fall back to the absolute `Addr` — was a new bug.** That holds on the live
> path (`addr` is `GroupSlotMatch::leafAddr`) but NOT on the Snapshot path, which cannot capture
> an array element's heap address and stores `AddrPlusOffset(objAddr, 0)` = the owning object's
> base. A Snapshot Deep row would have named the **UObject header** as the value's location — a
> plausible, copyable, wrong address, worse than the obviously-unknown `0x0`. Now the producer
> states it (`HasLeafAddress`, set only by the live decoder) and the row omits the arrow when
> nothing true can be said. **This is audit #4's 4b root cause — a cheap proxy signal standing in
> for a predicate a sibling computes correctly — committed while fixing 4a.** Found by the
> adversarial review, not by me.
>
> ### The follow-up that mattered more than the fix: **no rule can pick a specific pairing**
>
> Reported against 2715: *"要嘛以 TickCount 為主、要嘛以 FrozenInt 為主，可是畫面上沒有以這
> 二個值為主的 pair"* — and the sharper form, *"第二張截圖沒 filter … 那個 pair 根本不在
> result set"*. The pair **was** in the result set (both leaves kept — `TickCount`=183 ∈ 0..1000
> and `FrozenInt`=424242 ∈ 0..1000000, same object, cap 256 ≫ 38; the log proves the slot-0 half
> outright). What is true is the stronger claim underneath: **there is no automatic rule that
> produces that pairing**, because among slot 1's 36 unchanged fields nothing distinguishes
> `FrozenInt` from `I16` or `FixedArr`. 2 × 36 = 72 valid assignments; the scan cannot know which
> one was meant. Every "improve the heuristic" answer is therefore still zero-sum.
>
> **So the fix is to make it ASKABLE, and to make the unasked case findable:**
> - **The group filter is now space = AND** (`Radar::SplitFilterTerms`) — it was the last keyword
>   box in the repo treating its input as one substring, in violation of CLAUDE.md's own rule, and
>   that is exactly why a two-field request could not be expressed. `tickcount frozenint` keeps the
>   candidate (term-level AND, field-level OR) **and** the witness picker gives each term its own
>   slot, so the row becomes the requested pairing. Term order does not decide slot order.
> - **The leaf list is ordered object's-own-fields-first** (`Radar::OrderGroupSlotLeaves`). Leaves
>   are collected base-class-first, so `All fields` opened with `PrimaryActorTick.*`,
>   `InitialLifeSpan`, `CustomTimeDilation`, `AttachmentReplication.*` … and `FrozenInt` sat past
>   row 30 of a 220 px scrolling box. The tier comes from `definingClassName == className`, NOT
>   from the offset — a high offset correlates with "declared late" but is not that predicate, and
>   substituting a proxy for a predicate the data already carries is how this area regressed once
>   already this session.
> - **"All fields" toggles** — a second press collapses (locally, no round trip); re-opening
>   re-queries so a live scan never shows a stale snapshot.
>
> ### ✅ VERIFIED in-game 2026-08-05 — and it produced one more rule
>
> `tickcount frozenint` in the Filter turned the row into `TickCount=45 (+1),
> FrozenInt=424242 (+35)` — the requested pairing, on both the Development and the Shipping
> package. `All fields` lists and collapses.
>
> The maintainer then generalised the case they had *not* filtered: **"don't use a 0 as the
> default displayed pair — a 0 has little real meaning in a game."** Correct, and it was the
> default row's worst habit (`PrimaryActorTick.TickInterval=0, InitialLifeSpan=0` while the
> object's real fields had matched too). **Non-zero now wins inside every selection rule**
> (`Radar::IsZeroValueText`) — a tie-break within each rule, never a rule of its own: an
> all-zero slot still shows one leaf, and a zero the user explicitly filtered for still wins.
> The field column was also widened (`All fields` truncated
> `MinNetUpdateFrequency = unchanged -> 0x174 (FloatProper…`).
>
> **Separately, and not this feature's problem — the sample's Shipping heartbeat, now REWRITTEN:**
> `UEngine::AddOnScreenDebugMessage` is `#if !(UE_BUILD_SHIPPING || UE_BUILD_TEST)` in full
> (5.4 `UnrealEngine.cpp:11397`), so no flag could restore it. Replaced with `ADumperTestHUD`
> (`AHUD::DrawHUD` → `DrawText`, installed via `ClientSetHUD` from **Tick**, not the 1 Hz timer
> and not a GameMode asset). Whole chain read in the 5.4 source first — see the dev-log entry.
>
> ✅ **VERIFIED 2026-08-12 — and NO re-cook was needed.** The claim above ("needs a re-cook +
> re-package", "this environment cannot compile UE") was **wrong about the artifact, not just about
> the environment**: the Shipping package already on disk was built at 20:15 on 2026-08-05, five
> minutes *after* the HUD commit `b3d8593` (20:10:50), so it had carried `ADumperTestHUD` all along.
> Launching it (`-windowed -ResX=1280 -ResY=720`, no `-DumperTestNoHud`) puts **all five lines** on
> screen in the *Shipping* package.
>
> `TickCount` climbing is the actual assertion, and three independent counters agree on the same
> tick count over one 14.2 s window — which is what separates "numbers changed" from "the 1 Hz timer
> runs":
>
> | field | T0 | T1 (+14.2 s) | contract | |
> |---|---|---|---|---|
> | `frames` | 4593 | 5444 | must ALWAYS climb | ✅ |
> | **`TickCount`** | **78** | **93** | climbs **only** if the 1 Hz timer runs | ✅ **+15** |
> | `Health.CurrentValue` | 22 | 7 | must fall | ✅ |
> | `Health.BaseValue` / `FrozenInt` | 100 / 424242 | 100 / 424242 | must **NOT** move | ✅ |
> | `F32_Ticking` | 201.000 | 47.250 | −10.25 per tick | ✅ Δ153.75 = 10.25 **× 15** |
> | `F64_Ticking` | 20019.625 | 20023.375 | +0.25 per tick | ✅ Δ3.75 = 0.25 **× 15** |
> | `RawDouble_Ticking` (native) | 50039.500 | 50047.000 | non-UPROPERTY | ✅ Δ7.5 = 0.5 **× 15** |
>
> **Lesson worth keeping:** the item sat ⬜ for a week behind "this machine cannot compile UE" when
> the binary that settled it was already in `For Testing\`. Before accepting a build-environment
> blocker, check the artifact's mtime against the commit that was supposed to go into it.
>
> **Incidental, and it costs a session if you don't know it:** `-ExecCmds="t.MaxFPS 30"` is **silently
> ignored in Shipping**. `UE_ALLOW_EXEC_COMMANDS` is `UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING` there
> (`Exec.h:13`) and 1 only otherwise, so `frames` climbed at ~60/s despite the cap. Use the
> **Development** package when a frame-rate cap actually matters.
>
> While verifying it, a **third** wrong Shipping assertion in the same file surfaced, pre-existing:
> `UE_LOG(..., Warning, ...)` does NOT survive Shipping (`Build.h:328` sets
> `NO_LOGGING = !USE_LOGGING_IN_SHIPPING`; `LogMacros.h:146-158` keeps only Fatal), so
> `[DumperTest] ADumperTestActor ready at 0x…` prints in Development only. All three misreads
> came from inferring a gate from a sibling instead of opening it.


**Z1 — zydis `a95bb71`: Path-2 native disassembly still resolves `[this+off]`.** ✅ **VERIFIED
2026-08-12** · Effort **S** · Risk low · **① log-verifiable**, one deliberate action.

> ### ✅ VERIFIED 2026-08-12 — DumperTest Development, DLL build 2794
>
> Property Search → `JumpZVelocity` on `CharacterMovementComponent` → **⇊ Funcs**. From
> `offsets-0.log`, eight Path-2 analyses:
>
> ```
>  8 instrs / 0 mapped     33 instrs / 0 mapped     17 instrs / 0 mapped
> 30 instrs / 0 mapped     31 instrs / 0 mapped     15 instrs / 0 mapped
> 27 instrs / 0 mapped      9 instrs / 1 mapped props   <- the one that resolved
> ```
>
> ### ✅ RE-CONFIRMED ON BUILD 3262 `[Z1-PIPE-2026-08-17]` — 497 analyses instead of 8
>
> Same sample, driven over the pipe instead of the UI, and at ~60× the volume: `walk_function_props`
> over every `UFunction` `find_instances` would return. **497 functions took the `disasm` path**
> (3 returned `none`, 0 took `bytecode`), `instrs` ran **min 7 / median 32 / max 98** — nowhere near
> zero — **zero decode errors**, and **8 functions mapped ≥1 property** (6×1, 1×2, 1×3).
>
> The mappings are *semantically* right, which is stronger than the bare N≥1 the criterion asks for:
> `GetPlaneConstraintNormal` → `PlaneConstraintNormal`, `GetPlaneConstraintOrigin` →
> `PlaneConstraintOrigin`, `IsActive` → `bAutoActivate`. A getter resolving to its own backing field
> is not something a mis-decoded `[this+off]` produces by chance.
>
> ⚠ **Two things worth knowing before re-running this.** (1) **`find_property_xrefs` does NOT
> exercise Path 2** — it is the bytecode path, and on this sample it reports
> `0 xrefs (scanned 9807 functions, 6 with script)` without emitting a single
> `AnalyzeNativeFunctionProps` line. Path 2 only runs via `walk_function_props` on a **script-less**
> UFunction. The checklist's "⇊ Funcs" step conflates them; they are separate commands.
> (2) `find_instances` capped at **500 with `truncated: true`**, so this is a SAMPLE of the UFunction
> pool. That is fine for an existence claim (N≥1 mapped) and would **not** have been fine for an
> absence claim.
>
> Against the criteria below: **zero decode errors** anywhere in the log folder, **at least one
> function with non-zero `mapped props`**, and `instrs` nowhere near 0. Path 1 ran too —
> `FindPropertyXrefs: 0 xrefs (scanned 9807 functions, 6 with script, 51ms)` — and 0 is expected on
> a stock template that has almost no Blueprint script, which the "NOT a failure" note below already
> covers.
>
> **One honest qualification:** the `instrs` distribution (8–33) skews **below** the v5 baseline of
> 17–65. That is the sample, not the decoder — the 9-instr function is precisely the one that
> **did** map a property, which is the opposite of a decoder bailing early, and a stock Third Person
> template's native getters are genuinely shorter than a commercial title's. If a future run shows
> the same skew *with* nothing mapping, that is a different result and worth chasing.
>
> ⚠ **Read the log LATE.** The first attempt in that session grepped ~20 s after the click, found
> nothing, and would have been recorded as a failure — the DLL had not flushed yet, and
> `offsets-0.log` grew from 6,048 to 7,885 bytes afterwards. Confirm the command was even sent
> (`grep find_property_xrefs ui-pipe-0.log`) before concluding anything from an empty grep.

The bump (`85d7518` → `a95bb71`, "Decoder patch for variable-position decoder-tree filters" #638)
is a decoder fix **plus a full table regen** — +34.9k/−45.7k lines. That is the same shape as the
v4→v5 bump, which was judged to warrant an in-game check for exactly this reason: the offline
tests decode byte sequences *we wrote*, and a table regen changes how *arbitrary game code*
decodes.

**What the offline evidence already covers** (so this check is not re-doing it): five
`Test_Denken_*` tests decode real x64 sequences through Zydis and all pass, including
`Test_Denken_ExcludesStackAndZeroDisp`, which exercises the `disp.size == 0` path the v5 migration
touched. 81 + 996 green, DLL builds clean.

**What it does NOT cover:** a real UE binary's compiler output.

**How to verify** — inject into any UE game, then run a Path-2 property xref (Interesting Funcs →
a native getter/setter, or Property Search's xref button) and grep **`offsets-0.log`** (category
`OARR` → `LF_Offsets`, `Sein.cpp` s_catMap):

```
AnalyzeNativeFunctionProps: 0x… exec=0x… -> N mapped props (U unmapped, I instrs, C calls)
FindPropertyXrefs: N xrefs (scanned … functions, … with script, …ms)
```

- **PASS** = `I instrs` is a plausible function length (the v5 baseline was 17–65 per function),
  `N mapped props` is non-zero on at least some functions, and there are **no decode errors**.
- **FAIL** = `instrs` collapses toward 0 (the decoder is bailing early) or every function reports
  `-> 0 mapped props` where the v5 run reported some.
- **NOT a failure:** mostly-empty results are Path 2's *nature*, not a regression — only native
  constant-`[this+off]` getters map at all; script-only properties have no machine code. The
  v5 verification run made the same point.

*Baseline to compare against: the 2026-06-23 v5 smoke test on SEED + TQ2 (both UE5) — 17–65
instrs/func, 1–5 `[this+off]` accesses, many `→ 1 mapped props`, TQ2 `2 xrefs`, zero decode
errors. See [[project-vendor-zydis-ue58-status]] in memory.*

> 🇹🇼 **繁體中文版：[pending-verification_zh-TW.md](pending-verification_zh-TW.md)** — a standalone
> translation of THIS section, reorganised by how much effort each check costs (seven of the ①
> items are free from any ordinary session). **This English section is canonical**: if the two
> disagree, this one is right, and edits land here first.
>
> **Procedure lives in [log-verification-checklist.md](log-verification-checklist.md)** — where to
> grep, which file each marker lands in, and which items need a deliberate in-game action versus
> which are free evidence from any ordinary session. THIS section is the status (⬜ / ✅); that one
> is the how. Two things worth knowing before you open a log: **there is no log level, nothing is
> filtered** (so `[DEBUG]` lines count), and **See-Through / Foreground-Lock evidence lands in
> `init-0.log`**, not `walk`/`pipe`, because their categories fall through `ResolveFile`.

### 🔎 Audit #4 items — split by HOW they can be verified

> **The rule, set 2026-08-04:** every audit-#4 fix is filed here classified into one of the two
> groups below **at the time it ships**. An item with no group is an item nobody can act on.
>
> **① Log-derivable** — provable by reading `%LOCALAPPDATA%\UE5CEDumper\Logs` after an ordinary
> session, or after one where a log line was *added for the purpose*. Prefer this: it needs no
> special skill and it leaves evidence. If an added line is heavy (per-object, per-tick), the commit
> that adds it must say so and mark it for removal once the item is ticked.
> Grep by **format string, never line number** — see
> [log-verification-checklist.md](log-verification-checklist.md).
>
> **② Manual-only** — needs a human at the keyboard doing something no log can cause (a click
> sequence, a specific game, a specific third-party install). Each of these carries its exact steps
> and the PASS/FAIL observation.
>
> **STATUS after the 2026-08-05 DumperTest sessions (builds 2622 → 2701):** the self-built sample
> closed **B28, V1a, V1c and NumericAll** — three of them ⬜ since builds 796/927/942 purely for
> want of a game containing the right UPROPERTY — and exposed **three dumper defects** nothing else
> had (D1/D2/D3 above, **all fixed and now all verified** — D3's ✅ filed 2026-08-06 from logs the
> package had already written). **13 ⬜ bullets remain.**
>
> **B4 (CE mailbox after the UI dies) is now the only open item that can produce silently wrong
> data** — the one it used to share that line with, the **drain straggler**, is no longer a
> verification item at all: four attempts deep, the phase instrumentation has proven it genuinely
> parked in `ReadFile` with both cancel APIs failing, so what remains is a structural code change,
> not a guess and not a check. **B8 is blocked** behind the PE-hook misdetection reproduced on
> stock UE 5.4.
>
> *Earlier line kept for the record:*
> **STATUS after five rounds of live testing (2026-08-04 → 08-05, builds 2622 → 2650):**
> **11 ✅ verified · 2 🟡 half (B8, Dump Explorer) · 14 ⬜ not yet exercised.**
> *(Dump Explorer's ⬜→🟡 came out of the "shipped but unproven" list below, not out of the 14 — an
> earlier revision of this line said 13 and was wrong. The 14 is the count of `- ⬜` bullets.)*
> Verified: B49, B31, B5(passive), B47, B35, B42, B36, **B34**, **B14+R5**, **B38**,
> the clean-scan report, and B8's main path.
>
> **The 2026-08-05 DQ7R pass moved three things and none of them were the three it aimed at:**
> the `Stop conn drain TIMEOUT` root cause fell out of a capture *already on disk* (see below —
> it needed no recurrence); **B47's earlier ✅ was found to be credited to a hand-injected session
> where the guard was not even compiled in**, and re-earned properly on that day's real proxy run;
> and **B28 was NOT tested** — the rows inspected were `StrProperty`, not FText. R8 was refuted
> outright by the maintainer (see [audit-2026-08-04-findings.md](audit-2026-08-04-findings.md)).
>
> **B14+R5 took three attempts, and the two failures are the most useful thing this audit
> produced.** Round 1: the guard was applied to an enumeration ("2 of 7 thread procs") that had
> counted wrong — a WER dump proved `std::terminate` on a thread no guard covered. Round 2: with
> guards on all ~15 entry points it crashed *again*, identically. That was the answer, not a
> setback — **there was never an exception.** `~std::thread()` on a joinable thread calls
> `std::terminate()` directly, and `UE5_Shutdown` is never called when a user closes a game, so
> every worker was still joinable at process exit. Fixed by making it a property of the TYPE
> (`Routine::SafeThread`) rather than a third list.
>
> **The lesson all of it shares, worth carrying into the remaining 14:** a fix verified against the
> *list* it was written from is not verified. B34 listed three CE filenames; B14 listed seven
> thread procs. Each was correct about every item on its list and wrong about the world. And when
> a fix does not take, re-read the EVIDENCE before adding more of the same fix — round 2 was
> effort spent on a mechanism that was never involved.
>
> ### ⚠ The three worth doing FIRST next session
>
> | # | Item | Why it leads |
> |---|---|---|
> | **1** | **B4** — CE mailbox survives a dead UI client | Fails **silently**: lookups answer 0 while reporting `scanned=<full pool>`, which reads as "the object isn't there". A CE-only session stays broken for its whole life. **Now the only open item that can produce wrong data.** |
> | **2** | **B16** — five dead coord-grid sort headers | Two minutes, and it needs nothing but the AOT build already in `dist`. Cheapest ⬜ on the list. |
> | **3** | ~~**B28** — CJK FText mojibake~~ | **✅ CLOSED 2026-08-05** on the DumperTest sample (8 FText fields, both directions). Only the STVoyager UTF-8 counter-check remains, and it is licensee-specific. |
> | — | ~~`Stop conn drain TIMEOUT`~~ | **ANSWERED, and no longer a verify item** — see the phase capture below. What is left is a structural fix. |
>
> The rest (B18, B19, B2, B25, B26, B13/B41 …) cannot produce wrong data or a crash, so they can wait.
>
> ### 🔍 `Stop conn drain TIMEOUT` — the invoke hypothesis is DEAD; do not "fix" it
>
> > **This entry briefly claimed the root cause was found. It was not, and the retraction is worth
> > more than the claim.** The reasoning was: `teleport_get_pose`/`teleport_get_pov` arrive at
> > 22:19:39.590/591, *"never answered"*, therefore the connections were inside a command. **The pipe
> > log has no response marker for ANY command** — 193 `Received`, zero `Sent` — so "no response
> > line" is not evidence of anything. 78 `teleport_get_pov` in that same file are equally
> > "unanswered" throughout a perfectly healthy session.
>
> **What the log DOES establish** (`pipe-20260804-221945.log`, build 2638):
> `Stop entry (conns=2)` → `cancels+wake done (0 ms)` → `conn drain TIMEOUT, 2 left (5000 ms)`.
> Two connection threads survived both `Tot::RequestShutdown()` and a `CancelIoEx` on every live
> connection handle (`Fern.cpp:481`, `:507-510`), then burned the full 5 s budget.
>
> **What reading the code eliminates — the invoke hypothesis, completely.**
> `UE5_Shutdown` (`Frieren.cpp:587`) calls **`Stark::Shutdown()` BEFORE `s_pipeServer.Stop()`**, and
> `Stark::Shutdown` drains the invoke queue setting every pending promise to `-7` (`Stark.cpp:328-340`).
> A pipe thread blocked in `EnqueueInvoke`'s `future.wait_for` is therefore **already released before
> `Stop()` is even entered** — the ordering exists for exactly this reason and the comment says so.
> So "make the Stark invoke wait observe `Tot::Requested()`" would be **a poll loop for a case that
> cannot occur on this path**. Considered and rejected 2026-08-05.
>
> > Rejected on its own merits too, for the record: honouring the full `Tot::Requested()` would let a
> > **latched `g_perCommand`** (set when one lane drops, cleared only on a fresh connect into an empty
> > registry) abort invokes on the *other* lane — manufacturing a new silent-failure bug of exactly
> > the B4 family. If it is ever wanted, it must key on `ShutdownRequested()` alone.
>
> **ANSWERED 2026-08-05 10:57 — the straggler line fired on the first proper repro, and it is the
> OTHER half.** Repro was exactly as filed (UI connected, untick the CE record):
>
> ```
> 10:57:00.157  Stop entry (conns=2)
> 10:57:00.157  Stop cancels+wake done (0 ms)
> 10:57:05.160  straggler: idle in ReadFile (the I/O cancel should have freed it), last cmd 'teleport_get_markers'
> 10:57:05.160  straggler: idle in ReadFile (the I/O cancel should have freed it), last cmd 'trigger_scan'
> 10:57:05.160  Stop conn drain TIMEOUT, 2 left (5002 ms)
> ```
>
> Both connections were **idle** (`inFlight == false`) — so nothing was stuck in a command, and the
> guess that started this whole thread was wrong in both directions. The cancel simply did not reach
> them.
>
> **Why a one-shot `CancelIoEx` misses.** `Fern::ReadLine` (`Fern.cpp:758-783`) reads **one byte per
> `ReadFile` call**, so a 40-byte command is 40 separate reads with 40 gaps between them. `Stop`
> fired `CancelIoEx` **once**, before the drain wait began. A thread sitting in a gap at that instant
> has no pending I/O to cancel (`ERROR_NOT_FOUND`) and then issues a **fresh** `ReadFile` that
> nothing will ever cancel — parked until the 5 s budget expires. With the Teleport panel polling
> twice a second on both lanes, landing in a gap is not a rare race: `Stop entry` came **146 ms**
> after the last command arrived.
>
> ### ✅ FIXED build 2650 — re-assert the cancel instead of firing it once
>
> `Fern::Stop` now slices its 5 s drain wait into `Grimoire::PIPE_STOP_CANCEL_REASSERT_MS` (100 ms)
> and re-issues `CancelIoEx` on every surviving connection each slice — the same *assert the state
> you want repeatedly* shape as the six re-assert workers, applied to teardown. Zero cost in the
> common case: with nothing left to drain the loop exits on its first wait with zero re-asserts.
> Safe under `m_connMutex` because a connection thread erases itself from `m_conns` **before**
> `CloseConnOnce` (`Fern.cpp:900-907`), so anything still in the registry has an open handle.
>
> A second line was added because the old log could say the threads were *"idle in ReadFile (the I/O
> cancel should have freed it)"* but **not whether the cancel had anything to free** — those are
> different bugs: `Stop cancel issued: N accepted, M had nothing pending`.
>
> ### ❌ That fix FAILED, and its own instrumentation said why — build 2651 has the real one
>
> Re-run 2026-08-05 12:55 (DumperTest, DLL build 2650), and the answer was in the line added for
> exactly this:
>
> ```
> Stop entry (conns=2)
> Stop cancel issued: 0 accepted, 2 had nothing pending
> straggler: idle in ReadFile ×2  (last cmd 'teleport_get_markers' / 'walk_world')
> Stop conn drain TIMEOUT, 2 left (5027 ms, 49 cancel re-asserts)
> ```
>
> **49 re-asserts, every one reporting nothing pending.** So it is not a missed window — my
> hypothesis is refuted by my own diagnostic. `CancelIoEx` cancels **asynchronous** requests; these
> pipe instances are created without `FILE_FLAG_OVERLAPPED`, so a thread parked in a blocking
> `ReadFile` has no pending IRP for it to find and it returns `ERROR_NOT_FOUND` every time, forever.
>
> **`CancelSynchronousIo` is the API for a synchronous operation blocking a known thread** — and it
> takes the **thread** handle, which only the serving thread can produce. Build 2651: each
> connection publishes a `DuplicateHandle` of its own thread, `Stop` calls `CancelSynchronousIo` on
> it alongside the (kept, harmless) `CancelIoEx`, and the handle is closed by the owner after it
> unregisters.
>
> **Same grep, same repro:** UI connected, untick the CE record → `grep "Stop conn drain"`.
> **PASS** = `satisfied, 0 left (… ms, N cancel re-asserts)`. **FAIL** = `TIMEOUT` again, which
> would mean the thread is not in `ReadFile` at all and the straggler line is wrong about it.
> ### ❌ 2651 FAILED TOO — stop guessing; build 2657 instruments instead
>
> Re-run 2026-08-05 13:25 on DLL build **2652** (which contains the CancelSynchronousIo fix, and
> no `could not duplicate serving-thread handle` warning, so the handles were published and the
> call was made):
>
> ```
> Stop cancel issued: 0 accepted, 2 had nothing pending
> straggler: idle in ReadFile x2   (last cmd 'teleport_get_markers' / 'refine_group_scan')
> Stop conn drain TIMEOUT, 2 left (5030 ms, 49 cancel re-asserts)
> ```
>
> **Three hypotheses, three refutations:** "stuck inside a command" (they are idle), "CancelIoEx
> missed the window" (49 re-asserts, all nothing-pending), "CancelSynchronousIo is the right API"
> (called, still timed out). Every one of them aimed at the same phrase — and that phrase is an
> **inference**. `inFlight` is set only around `DispatchCommand`, so a thread blocked in
> `WriteFile`, waiting on `writeMutex`, or **joining its watch threads in
> `StopWatchesForConnection`** is equally reported as "idle in ReadFile". A cancel does nothing for
> any of the latter.
>
> This is `feedback-fix-not-taking-reread-evidence` playing out verbatim: *when a fix does not take,
> re-read the evidence before adding more of the same fix.* Two were added.
>
> **Build 2657 replaces the label with an observation** — a per-connection `Phase`
> (Reading / Dispatching / Writing / StoppingWatches / Unregistering) stamped at every transition,
> reported with how long it has been there. `CancelIoEx` + `CancelSynchronousIo` are both kept:
> harmless, and correct for the case the phase may yet confirm.
>
> **Next run, same repro, one grep:** `grep "straggler" pipe-0.log`. It now names the real phase.
> `StoppingWatches` would mean the fix belongs in the watch-thread join, not in I/O cancellation at
> all — a different subsystem from the three already tried.
>
> *The re-assert loop is kept. It cost nothing (49 iterations of a failing syscall over 5 s) and it
> is what proved the diagnosis wrong quickly; a single shot would have looked like bad luck.*
>
> ### ✅ ANSWERED — the phase is `Reading`, three times over. **This is no longer a verify item.**
>
> Filed 2026-08-06 from captures already on disk. Three post-2657 runs, and the instrumentation
> said the same thing every time:
>
> ```
> 13:38:45  straggler: parked in ReadFile (waiting for the next command) for  73871 ms, last cmd 'get_object_list'
> 16:42:55  straggler: parked in ReadFile (waiting for the next command) for 264184 ms, last cmd 'walk_functions'
> 18:43:36  straggler: parked in ReadFile (waiting for the next command) for 145063 ms, last cmd 'query_group_slot_leaves'
>           Stop cancel issued: 0 accepted, 2 had nothing pending
>           Stop conn drain TIMEOUT, 2 left (5030 ms, 49 cancel re-asserts)
> ```
>
> **Phase `Reading`, parked 264 seconds.** Not `Dispatching`, not `Writing`, and **not
> `StoppingWatches`** — which was the hypothesis this instrumentation was added to test, and it is
> refuted too. The connection is genuinely blocked in a synchronous `ReadFile`.
>
> **Four attempts, each refuted by the diagnostic added for the previous one:**
>
> | # | Hypothesis | Refuted by |
> |---|---|---|
> | 1 | Stuck inside a command | `inFlight == false`; `Stark::Shutdown` already runs before `Stop()` |
> | 2 | `CancelIoEx` missed the window (2650: re-assert) | 49 re-asserts, every one `nothing pending` |
> | 3 | `CancelSynchronousIo` is the right API (2651) | called, handles published, still TIMEOUT |
> | 4 | "idle in ReadFile" is an inference, not an observation (2657: measure the phase) | the phase **is** `Reading` — so 1–3 were aimed at the right place and the wrong mechanism |
>
> **Root cause:** the pipe instances are created without `FILE_FLAG_OVERLAPPED`, so there is no
> pending IRP for `CancelIoEx` to find — `ERROR_NOT_FOUND`, forever, by construction.
>
> **What remains is a code change, not a verification.** Both remaining options are structural, and
> **neither is a fifth guess at the cancel API**:
> - close the connection handle from `Stop` so the blocking `ReadFile` returns an error, or
> - make the pipe overlapped.
>
> **When that fix ships**, the acceptance is unchanged and is one grep on the same repro (UI
> connected, untick the CE record): `grep "Stop conn drain" pipe-0.log` → **PASS** =
> `satisfied, 0 left (… ms, N cancel re-asserts)`.
>
> *Method note worth keeping: three consecutive fixes were written against the phrase "idle in
> ReadFile", which was a LABEL the code asserted, not something it had measured. Replacing the
> label with an observation cost one build and ended the thread.*
>
> ### ⚠ 2026-08-14 — audit #5/D5 says the ANSWER above is the wrong mechanism. Read this before fixing.
>
> The conclusion recorded above ("genuinely blocked in a synchronous `ReadFile`"; root cause = no
> `FILE_FLAG_OVERLAPPED`) accounts for attempt #2 failing but **not for attempt #3** —
> `CancelSynchronousIo` was called on a duplicated *thread* handle, which is exactly the API for a
> live thread blocked in synchronous I/O, and it *also* reported nothing-pending, 49 times.
>
> **A terminated thread explains both, and audit #5/D5's finding F1 shows the threads are terminated.**
> `Fern::Stop` has two logging call sites — `UE5_Shutdown` (`Frieren.cpp:588`, whose FIRST statement is
> `LOG_INFO("UE5_Shutdown: Cleaning up...")`) and `UE5_StopPipeServer`, which the shipped
> `scripts/UE5CEDumper.CT:772-780` only *probes* with `pcall(getAddress, …)` before calling
> `UE5_Shutdown` **alone**, deliberately. `grep -rn "Cleaning up"` over the whole
> `%LOCALAPPDATA%\UE5CEDumper\Logs` tree returns **zero**. So every `Stop entry` capture on disk —
> including the `conns=2` / `5029 ms` one this entry is built on — was reached from
> **`~Fern()` during `DLL_PROCESS_DETACH`**, i.e. after `ExitProcess` had already terminated the
> connection threads. A dead thread has no pending I/O for either cancel API to find, and it can never
> erase itself from `m_conns`, so the drain predicate is **unsatisfiable by construction** and the full
> 5 s budget burns every time.
>
> **Consequence for the two structural fixes proposed above: neither works on this path.** Closing the
> connection handle from `Stop` makes a *live* thread's `ReadFile` return an error — there is no live
> thread. Making the pipe overlapped has the same problem. The fix is not to run the drain at all when
> `Stop` is entered from the destructor: give `Stop` a `bool graceful`, skip the wait/joins/cancels on
> the DETACH path (the OS reclaims all of it — the reasoning `Heiter.cpp:288-301` already applies to its
> own DETACH body), and log which entry path was taken so future captures can be attributed.
>
> *This is attempt #5, and it is the first one aimed at a mechanism rather than at an API. Note what
> found it: not a new diagnostic, but reading the code that decides **who calls `Stop`** — a question
> none of the four earlier attempts asked, because the repro was assumed to be the CE untick it was
> written as, and no capture on disk is actually that repro.*
>
> ### ✅ There is now a ~30-second ON-DEMAND repro, with a negative control (2026-08-14, build 2812)
>
> Every capture in the four attempts above was **accidental**. This one is deliberate, headless, and
> takes half a minute on packaged `DumperTest` — use it as the acceptance test for whatever fix ships:
>
> 1. Launch `DumperTest.exe`, `scripts\inject-ue.ps1 -ProcessId <the -Win64-Shipping pid>`.
> 2. Connect a `NamedPipeClientStream` to `UE5DumpBfx` and send any command.
> 3. **Close the game with `CloseMainWindow()`** — `WM_CLOSE` → `ExitProcess` → `DLL_PROCESS_DETACH`.
>    **Not `Stop-Process -Force`**: `TerminateProcess` skips DETACH entirely, so a forced kill exits
>    fast and "proves" the bug is gone.
>
> | | Client at exit | `Stop entry` | Drain | Process exit |
> |---|---|---|---|---|
> | **B** | **held open** | `conns=1` | `TIMEOUT, 1 left (5030 ms, 49 cancel re-asserts)` | **6,046 ms** |
> | **A** | disconnected first | `conns=0` | `satisfied, 0 left (0 ms, 0 re-asserts)` | **1,105 ms** |
>
> One variable, 5.5× apart. **Run A as well as B** — without it, 6 s is indistinguishable from "how
> long a UE game takes to close", and A is also the regression guard: a fix that skips the drain must
> not make the already-correct `conns=0` path slower or noisier.
>
> **PASS for the fix** = case B reaches `Stopped` in well under a second, with the entry path named in
> the log so a future capture can be attributed to process-exit vs a CE Disable.
>
> ### ✅✅ FIXED build 2813 (2026-08-14) — attempt #5, and it passed its own acceptance test
>
> `Fern::Stop` takes `bool graceful = true`; `~Fern()` calls `Stop(false)`, which logs
> `Stop entry (process exit — skipping drain/joins, the OS reclaims this)` and returns before the
> cancel sweeps, the watch/scan joins and the 5 s drain. **Case B re-measured on the fixed build:
> 1,185 ms** (pre-fix 6,046 ms; pre-fix control A 1,105 ms) — a connection open at exit now costs
> nothing. The entry path is named in the log, so the attribution problem that made this take five
> attempts cannot recur.
>
> ### ✅ VERIFIED 2026-08-18 — the `graceful=true` path (CE Disable), DumperTest Development, dist 3262
>
> Unticked `Inject DLL + Start Pipe Server` with the UI **connected** (`conns=2`). All three PASS
> conditions met, and the whole shutdown took **174 ms**:
>
> ```
> 11:26:03.059 Mailbox: polling thread stopped
> 11:26:03.059 PipeServer: Stop entry (conns=2)          <- NOT "process exit"
> 11:26:03.059 PipeServer: Stop cancel issued: 2 accepted, 0 had nothing pending
> 11:26:03.060 PipeServer: AcceptLoop exiting
> 11:26:03.060 PipeServer: Stop cancels+wake done (0 ms)
> 11:26:03.060 PipeServer: Stop watches+scan joins done (0 ms)
> 11:26:03.060 PipeServer: Stop conn drain satisfied, 0 left (0 ms, 0 cancel re-asserts)
> 11:26:03.060 PipeServer: Stop accept join done (1 ms)
> 11:26:03.233 PipeServer: Stop monitor join done (174 ms)
> 11:26:03.233 PipeServer: Stopped
> ```
>
> `Stop entry` names `conns=2`, **not** `process exit` — so the destructor is not racing the explicit
> call, which was the FAIL signature. The drain says **satisfied** with **0 cancel re-asserts**, and
> `Stopped` follows. The 174 ms is entirely the monitor join (its poll is 200 ms, so this is one
> sleep), and every other phase is 0–1 ms.
>
> **This also closes the `executeCodeEx` basic-path step 3** ("untick the `.CT` record → `UE5_Shutdown`
> really runs, rather than the UI merely claiming unloaded"). `init-0.log` shows the DLL's own side:
>
> ```
> 11:26:03.057 [INIT]    UE5_Shutdown: Cleaning up...
> 11:26:03.059 [Grausam] Foreground lock DISABLED
> 11:26:03.060 [SENSE]   Diagnostics counters reset
> ```
>
> Real teardown of real state, two ms before the pipe server's own entry line — the ordering
> `UE5_Shutdown` → `Fern::Stop` that `Frieren.cpp:588` describes. CE's Lua console printed
> `[11:26:03] [UE5Dump] UE5 Dumper stopped.` and neither CE nor the game hung.
>
> ⬜ **B18's step 3 remains untestable on this sample** and is *not* claimed: it needs a title whose
> GObjects is **not** AOB-resolvable, so an Extra Scan is still running when the record is unticked.
> DumperTest resolves on the first pattern, so `Stop watches+scan joins done` had nothing to join —
> it reported `0 ms` because there was no scan, not because a long one was cancelled promptly.
>
> ⬜ does **not** mean "probably fine". It means nobody has looked. Most of the fourteen were
> simply not exercised (no wrapper installed, no UI killed mid-command, no Extra Scan).

#### ① Log-derivable — still open: B29 (log half) / B18 / B19 / B10 / B8 (🟡 deferred half)

- ✅ **`Fern::Stop` no longer waits for a client that may never come** (build 2569, B49) —
  **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.** The CE session hit the exact wedge condition, `Stop entry (conns=0)`,
  which is the case the old `CloseHandle` on a synchronous listen handle blocked on forever:
  `cancels+wake done (0 ms)` → `conn drain satisfied, 0 left (3 ms)` → `accept join done (3 ms)`
  → `monitor join done (58 ms)` → `Stopped`. 59 ms end to end against a PASS bar of ~100 ms.
  *Original instructions kept below for the next build.*
  **Already instrumented** — the fix shipped with per-phase logging precisely so this needs no
  special run. Play normally with the UI connected, then disconnect the UI and untick the CE record.
  Grep `pipe-0.log` for `PipeServer: Stop entry` and the phase lines that follow it.
  **PASS** = `PipeServer: Stopped` appears within ~100 ms of `Stop entry`, and `Stop conn drain`
  says `satisfied`. **FAIL** = no `Stopped` line at all (the old unbounded hang), or a phase line
  showing seconds. The old behaviour logged *only* `Stopped`, so the presence of `Stop entry` also
  confirms you are on the new build.

- ⬜ **CE-plugin double-inject guard rejects a foreign wrapper** (build 2577, B29) — *log half.*
  Any session where CE's plugin menu is used: grep `init-0.log` for
  `is loaded but is not ours`. That line only exists in the new code, and it fires for the exact
  case that used to be misread. **PASS** = the line names the foreign module and injection proceeds.
  (The manual half — actually installing a wrapper — is in ② below.)

- ✅ **UI log rolls at 8 MB instead of stopping** (build 2585, B31) — **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.**
  `Logs\UE5DumpUI\` holds `pipe-0.log` at **8,388,756 bytes** (the 8 MiB cap) *and*
  `pipe-0_001.log` at 4,055,182 bytes with a **newer** mtime (21:05 vs 20:53). The roll happened
  and writing continued into the new file — the silent-stop signature would have been the 8 MB
  file alone with a stale last line. *Original instructions below.*
  Free from any long session:
  `ls %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\`. **PASS** = files named `pipe-0_001.log` (or
  similar) exist alongside `pipe-0.log` once a category passes 8 MB, and the newest file's last line
  is recent. **FAIL** = a single `pipe-0.log` sitting at exactly ~8 MB with a stale last line — that
  is the silent-stop signature. Fastest way to reach it: Teleport → Auto refresh, left running.

- ✅ **Leftover-proxy reports land inside the app folder** (build 2585, B38) — **VERIFIED
  2026-08-04 22:49, build 2643.** `leftover-proxies-20260804-224903.txt` was written to
  `%LOCALAPPDATA%\UE5CEDumper\Reports\`, and the old `%LOCALAPPDATA%\Reports\` still holds only
  the pre-fix file from 2026-07-30. Log line: `Leftover report written: …\UE5CEDumper\Reports\…`
  *Original instructions below.*
  **Previously not exercised**
  — no Report has been run since the fix. Checked 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622: `%LOCALAPPDATA%\Reports\` does
  hold `leftover-proxies-20260730-210903.txt`, but that is dated **2026-07-30**, i.e. before
  build 2585, so it is the documented pre-fix leftover and **not** evidence of failure.
  Run a proxy-cleanup Report. **PASS** = the file appears under `%LOCALAPPDATA%\UE5CEDumper\Reports\`. **FAIL** = it
  appears in `%LOCALAPPDATA%\Reports\`. (Files written before 2585 stay in the old place by design.)

- ✅ **A CLEAN scan still produces a report** (build 2637) — **VERIFIED 2026-08-04 22:49.**
  Raised by the maintainer: a scan that finds nothing must still leave an artifact, because
  "scanned everything and found nothing" and "never ran / looked in the wrong place / failed
  silently" are otherwise indistinguishable a week later. `BuildReport` had always handled the
  empty case; `CanWriteOrphanReport => Orphans.Count > 0` made that text unreachable and greyed
  the button out. Now gated on `OrphanScanRan`, and the empty report states the coverage:
  *"No leftover proxy DLLs were found. 67 folder(s) were examined."*

  > **~~Open UX question~~ — CLOSED 2026-08-05 by the maintainer: keep the current behaviour.**
  > *Find leftovers* shows its findings on screen; *Report…* writes the file. Writing a file stays
  > an explicit act. The discoverability half was already handled in build 2645 — the scan result
  > now names the button verbatim (*"press "Report…" to save this result as a file"*) and the clean
  > case states its coverage. **No auto-write. Do not re-open.**

- ✅ **The `UE5_Init` guard did not break ordinary init** (build 2592, B5) — *passive half* —
  **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.** `Starting initialization...` and `Complete (UE…)` are one-for-one in
  all three games (DQ7R 5/5, Elliot 14/14, CE 1/1), and neither new line
  (`init already in progress`, `shutdown was requested during the scan`) appears anywhere.
  As stated below, that proves the guard is harmless, **not** that the race is fixed — the
  deliberate provocation is still open in ②. *Original instructions below.*
  *free from any session.* Grep `init-0.log` for `UE5_Init:`. **PASS** = `Starting initialization...` and
  `Complete (UE…)` alternate strictly one-for-one, and neither of the two new lines
  (`init already in progress`, `shutdown was requested during the scan`) appears. **FAIL** =
  a `Starting` with no matching `Complete` (the guard deadlocked — nothing should be able to cause
  this, which is why it is worth one grep per session), or two `Starting` lines in a row (still
  racing). Absence of the new lines proves only that the race did not *occur*; the deliberate
  provocation is in ② below.

- ✅ **Cheat Engine is never scanned as if it were the game** (build 2603, B34) — **VERIFIED
  build 2633**: `host process is 'cheatengine-x86_64-SSE4-AVX2.exe' — Cheat Engine is never a
  scan target`, and `scan-0.log` stayed at 121 bytes (header only) where the failing run left
  1.3 MB. *Earlier:* **FAILED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622, REFIXED build 2628, needs a re-test.**
  The capture shows `process: …\cheatengine-x86_64-SSE4-AVX2.exe` followed by
  `DllMain AutoStart: game process — calling UE5_AutoStart` — a 5.8 s AOB scan and the pipe
  opened **inside CE** (1.3 MB `scan-0.log` in that folder). Cause: the guard was an exact-name
  list and CE's real executable is the `-SSE4-AVX2` CPU-feature variant, which matched none of
  the three names. `g_isCEPlugin=0` too — the DLL was hand-injected, so the
  `CEPlugin_GetVersion` half could not help either. Now
  `Grimoire::IsCheatEngineExeName`, a case-insensitive **prefix** on the `cheatengine` stem
  (anchored at the start, so `MyCheatEngineClone.exe` is still allowed).
  **Re-test:** inject the DLL into CE by hand again. **PASS** = `host process is '…' — Cheat
  Engine is never a scan target` and **no** `scan-0.log` growth in that folder.
  Free from any
  session where the CE plugin is registered: grep `init-0.log` for `DllMain AutoStart:`.
  **PASS** = when the host is CE, either `CE plugin host — skipping auto-start` (the normal path,
  now reached because `CEPlugin_GetVersion` claims identity) or the new
  `host process is '…' — Cheat Engine is never a scan target`. **FAIL** = `game process — calling
  UE5_AutoStart` with `cheatengine-x86_64.exe` on the `UE5Dumper DLL loaded | … | process:` line
  two lines above. To provoke the original race: register the plugin but leave it **unticked**,
  then start CE.

- ⬜ **Extra Scan can be cancelled** (build 2603, B18). Needs a game where GObjects does NOT
  resolve by AOB, so Extra Scan actually runs long. Start it, then untick the CE record (or close
  the UI) while it is still going. **PASS** = `pipe-0.log` shows `PipeServer: Stop watches+scan
  joins done` within a second or so of `Stop entry`. **FAIL** = seconds of gap, or CE's window
  frozen until the sweep finishes — that is the unbounded join, and `UE5_Shutdown` runs on CE's own
  thread, which is why it freezes CE rather than just the game.

- ⬜ **Log retention no longer dies at the first undeletable file** (build 2603, B19). Provoke it:
  open any archived `%LOCALAPPDATA%\UE5CEDumper\Logs\<proc>\*.log` in a program that holds it open,
  and make sure at least one OTHER archive in the same folder is older than 21 days (backdate it).
  Start a game with the DLL. **PASS** = the backdated file is gone and the held one remains.
  **FAIL** = both remain — the sweep aborted at the held file, which it did on every launch because
  enumeration order is stable.

- ✅ **The proxy dedup guard says when it is not armed** (build 2603, B47) — **VERIFIED 2026-08-05,
  build 2645 — and the 2026-08-04 ✅ was credited to the WRONG SESSION.**
  > **The correction, because it is the same trap as B34 and B14.** The 08-04 note said *"DQ7R ran
  > through `version.dll` (a real proxy session, so the guard is compiled in)"*. It did not. That
  > line is inside `#ifdef UE5_PROXY_BUILD` (`Heiter.cpp:262-270`), and **not one 08-04 DQ7R session
  > logged `DllMain ProxyStart` or `Loaded real version.dll`** — every one was hand-injected, so the
  > guard was not in the loaded binary at all. Its absence proved nothing. *An absence is only
  > evidence once you have shown the producing code was present and running.*
  >
  > **The real evidence is the 2026-08-05 10:29:30 run**, which IS a proxy session —
  > `DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)` →
  > `Loaded real version.dll: C:\WINDOWS\system32\version.dll` — and
  > `first-loaded-wins guard is NOT armed` is absent there. `Local\…_<PID>` succeeded where `Global\`
  > needed a privilege the game does not have. PASS, for the right reason this time.
  *Original instructions below.* Any proxy session:
  grep `init-0.log` for `first-loaded-wins guard is NOT armed`. **PASS** = the line is ABSENT
  (`Local\` + PID succeeds where `Global\` needed a privilege the game does not have). Its presence
  is not a failure of this fix — it is the fix reporting a condition that used to be silent — but
  it is worth investigating if it appears.

- ✅ **The PERF split no longer measures its own probe** (build 2610, B35) — **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.**
  *This item had no verification entry when it shipped — a gap in the filing, found while
  sweeping these logs.* `grep 'PERF Snapshot capture'` gives
  `wall 5,256.2 ms … split dll 2,733.5 / ipc 692.4 / ui 1,830.3 ms`. The three parts sum to the
  wall time exactly, transport (dll+ipc = 3,425.9) is **less** than wall, and `ui` is a large
  non-zero. The pre-fix signature was the opposite: transport **exceeded** wall, so `ui` clamped
  to 0 and `ipc` absorbed the probe's own 93–125 ms round-trip. These are the numbers
  [multipipe-eval.md](multipipe-eval.md) reasons from.

- ✅ **CJK FText no longer renders as ASCII mojibake** (build 2599, B28) — **VERIFIED 2026-08-05 on
  the DumperTest sample, Shipping package, DLL build 2650.** All **eight** FText fields render as
  CJK in Live Walker, and every control holds:

  | field | rendered | role |
  |---|---|---|
  | `Text_Even2_OneNull` 統一 · `Text_Even2_TwoNull` 一言 · `Text_Even4_TwoNull` 統一言語 | correct | the trigger cases (even length, U+xx00) |
  | `Text_Odd3_OneNull` · `Text_Even6_NoNull` 日本語テスト | correct | length/parity controls |
  | `Text_Ascii` `DumperTest FText ASCII` | correct | **the other-direction control** — a fix that swung to always-UTF-16 would have broken this |
  | `Text_Localized` 統一言語 | correct | different `FTextHistory`, agrees with `Text_Even4_TwoNull` ⇒ the fault was never history traversal |
  | `Str_*` ×4, `Name_Cjk` | correct | FString + FNamePool paths, unaffected as expected |

  **This closes the one open item that could show the user WRONG DATA.** The counter-check on
  STVoyager's UTF-8 FText is a separate, licensee-specific case and stays open.

  > **Two observations from the same screen, neither of them B28:**
  > 1. `Text_Empty` renders as **`No`**. An `FText::GetEmpty()` should read as empty; `No` looks
  >    like a truncated `None` or a mis-typed render. Cheap to chase, cosmetic, but it is the empty
  >    display-string path and nothing else covers it. **NEW, unfiled.**
  > 2. The package under test was built from a **stale** `DumperTestActor.cpp` (退一步 where the
  >    repo had 走一步), so the odd-length control was not the documented one. It renders correctly
  >    either way, so B28's result stands — but see the identity-record note below; this is exactly
  >    what `capture_package_identity.py --project` now detects.

  *Original instructions below.*
  > **❌ NOT tested by the 2026-08-05 DQ7R pass, and the near-miss is worth recording so the next
  > attempt does not repeat it.** The rows inspected (`Name` / `DisplayName` / `ListName` = 忘名)
  > are **`StrProperty`** — FString, which goes through the UTF-16-only reader and **never had this
  > bug**. B28 lives in `ReadFTextString` alone. The hex confirms the FString path is fine and says
  > nothing about B28: `D8 5F | 0D 54 | 00 00 | 6F 00 | 78 00 | 00 00` = 忘(U+5FD8) 名(U+540D) NUL
  > 'o' 'x' NUL, `ArrayNum=6`, i.e. the game stores a fixed 6-TCHAR field with an **embedded NUL at
  > index 2**; the reader stops at the NUL and renders 忘名 — correct. Second miss: neither 忘
  > (U+5FD8) nor 名 (U+540D) has a **low byte of 0x00**, so this string could not have tripped the
  > trigger even as an FText.
  >
  > **What to do instead:** find a row whose Type column literally reads **`TextProperty`**. DQ7R's
  > 2026-08-05 walk logs contain **zero FText field reads** (the only `TextProperty` hits are the
  > class names `TextPropertyTestObject` / the `TextProperty` meta-class), so one has to be hunted:
  > Property Search for a TextProperty on a UI/dialogue/item-description class. Trigger characters
  > whose low byte IS 0x00, all common in JP/CN: **一** U+4E00 · **最** U+6700 · **言** U+8A00 ·
  > **退** U+9000 · **紀** U+7D00 — and the string must be an **even** number of characters.
  Affects **FText-typed values only** (`ReadFTextString`); FString goes
  through the UTF-16-only reader and never had the bug. **To test:** any game with Chinese/Japanese
  UI text — set the game to a CJK language, find an FText property in Live Walker or Property
  Search. **PASS** = the value reads as CJK. **FAIL** = short ASCII punctuation soup (`,{1`, `-N?e`)
  where CJK belongs. Worth checking specifically on a string with an **even** character count
  containing a `U+xx00` character (一, 第…一, 統一) — that is the exact trigger. Counter-check that
  the fix did not swing the other way: **Star Trek Voyager (UE5.6)** stores its FText as UTF-8, and
  its Chinese must still read correctly.

- 🟡 **Fly/Noclip no longer leaves the pawn ghosted** (build 2596, B8) — **MAIN PATH VERIFIED**
  > **⚠ READ THIS BEFORE RE-TESTING — the deferred half is NOT reachable by closing the game.**
  > Closing a game never calls Fly's disable at all: `UE5_Shutdown` does not run on game close
  > (proven — zero `UE5_Shutdown: Cleaning up` lines in any session), so `Dunste::SetEnabled(false)`
  > never executes and `DISABLED but the pawn's collision is still OFF` can never be printed.
  > Confirmed in the 22:33 Elliot run: Fly was ON, the game was closed, and there is **no
  > `Fly: DISABLED` line at all**. That run is a B14 test, not a B8 test.
  >
  > The deferred half needs the **Disable button clicked while the game thread is quiet**. The
  > 22:01 Elliot run did click Disable — and `SetActorEnableCollision(1) invoked` proves the game
  > thread was still ticking, so Elliot does not appear to idle when unfocused. Alt-tab duration is
  > not the variable; whether the title honours `t.IdleWhenNotForeground` is.
  >
  > **So this needs a game that actually goes quiet when backgrounded.** If none is to hand it is
  > reasonable to close it as accepted-unverified: the code path is the same one Schlacht has been
  > running in production since build 2364, and the main path is verified.
  (Elliot, 2026-08-04, noclip ON). The log shows the fixed ordering exactly:
  `Fly: worker stopped` → `Fly: SetActorEnableCollision(1) invoked` → `Fly: DISABLED`. Join
  before restore, and the restore is committed from the invoke *actually running*. **The
  DEFERRED path is still ⬜** — the game thread stayed responsive, so
  `DISABLED but the pawn's collision is still OFF` was never reached. To finish it, alt-tab
  away for >500 ms before clicking Disable on a title that idles when unfocused.
  *Original instructions:* The whole answer is in the
  log, and the trigger is the *ordinary* way to turn Fly off on an idle-when-unfocused title.
  **To test:** Teleport tab → Fly ON + Noclip → fly through a wall → **alt-tab to the UI** (wait
  >500 ms so ProcessEvent goes quiet) → click Disable. Grep **`walk-0.log`** for `Fly:` —
  NOT `init-0.log`: `Dunste.cpp` sets `LOG_CAT "FLY"`, which `Sein.cpp`'s `s_catMap` routes to
  `LF_Walk`. Confirmed against real logs 2026-08-06.
  **PASS** = `Fly: DISABLED but the pawn's collision is still OFF (game thread unresponsive)`,
  then — after you click back into the game — `Fly: game thread resumed after N ms — pawn collision
  restored`. **FAIL** = the old shape: a plain `Fly: DISABLED` and nothing else, after which the
  pawn falls through the world. Corroborate in-game: walk into a wall, it should stop you.
  Second, cheaper check on any Fly session: `Fly: collision disable deferred` may appear, but it
  must not repeat — it is rate-limited to once per stall.

- ⬜ **`WalkClassEx` memo — the win is already instrumented** (build 2596, B10). **Blocked on a
  BASELINE**, not on instrumentation: the retained logs hold exactly one
  `PERF Snapshot capture` line (`wall 5,256.2 ms`, 2026-08-04, post-fix), so there is nothing
  pre-2596 to compare it against. Either keep this number as the new baseline and compare the
  next capture of the SAME snapshot on the same game, or settle the correctness half alone
  (struct types / enum names / bool masks still populate). Snapshot capture is
  wrapped in a `DiagnosticsProbe`, so no new logging is needed: grep
  **`Logs\UE5DumpUI\view-0.log`** (or the game folder's `ui-view-*.log`) for
  `PERF Snapshot capture` — it is a UI-side probe, NOT in `pipe-0.log`. Corrected 2026-08-06. **PASS** = `wall … ms` is materially lower than the same capture on a
  pre-2596 build (the memo removes a 100–300 × `FieldInfo` deep copy per struct-array *element*),
  and correctness is unchanged — property grids still show struct types, enum names and bool masks,
  which are exactly the fields `WalkClassEx` adds on top of `WalkClass`. **FAIL** = those columns go
  blank (the memo would be serving a pre-enrichment entry), or a crash under a parallel scan (a
  handed-out reference being invalidated — the reason `try_emplace` landed first).

- ℹ️ **Attempted 2026-08-18 on DumperTest Development first — do not retry there** (it passed later, on
  Elliot; see the ✅ row below).
  The single-call-that-blocks-for-seconds requirement is **unmeetable on that package**, measured
  rather than assumed. Its GObjects pool is 25,179 objects, and the two heaviest whole-pool commands
  the UI can issue finish far inside `MonitorLoop`'s 200 ms poll:

  | command (all classes, deep, native-C, no noise filter) | UI-reported duration |
  |---|---|
  | `begin_value_scan` `NumericNoByte` / `Bigger` / `0` — 50,000 candidates | **113 ms** |
  | `begin_value_scan` `FString` / `Contains` / `"a"` — 1,060 candidates | **52 ms** |

  Both appear in `pipe-0.log` as single `begin_value_scan` commands, so this is one call, not
  chunking — it is simply over before a poll can land. **Move B4 to a large title**; GROUP 6 already
  says Elliot's 482 MB image "is what makes the race windows real; the sample is too small", and that
  reasoning applies here exactly. Recorded as *not tested*, per the run plan's rule 4.

- ✅ **DONE 2026-08-18 `[ELLIOT-B4-2026-08-18]` — CE mailbox survives a dead UI client** (build 2592,
  B4). **The arming line has never been captured before**; this run has it, on Elliot (85,068
  objects), dist 3262, DLL injected by its deployed `dxgi` proxy.

  **Two vehicles were tried, and the first one FAILED for a reason worth keeping** (below). The one
  that works is a **single synchronous** pipe command: `begin_value_scan`, ~700 ms with *Parallel
  scan* and *Batch read* unticked. The kill was fired **from the log** rather than on a timer
  (`tools/verify/kill_on_marker.py`) — a fixed sleep fired before the command started on the first
  two attempts and proved nothing:

  ```
  12:06:38.041 Received: {"cmd":"begin_value_scan","data_type":"FString","scan_type":"Contains",…}
  12:06:38.665 PipeServer: Client disconnected
  12:06:38.818 [WARN]  PipeServer: client gone mid-command (err=109) — aborting in-flight op
  12:06:38.826 [ERROR] PipeServer: Failed to write response
  12:06:38.826 PipeServer: per-command cancel cleared — no connection that raised it is still live
  ```

  * **The ARMING line is present** — `client gone mid-command (err=109)` (109 = `ERROR_BROKEN_PIPE`),
    777 ms after the command arrived. Per this row's own rule, that is what makes everything below
    mean anything, and it is the line every previous attempt lacked.
  * **The in-flight op really was aborted** (`Failed to write response`).
  * **The follow-up command reports a NON-ZERO count**: a fresh UI, reconnected, ran Instance Finder
    on `Actor` → **`Found 2 instances (scanned 85,068, non-null 84,410, named 84,410 (100.0%))`**.
    The FAIL signature — `0` answered while `scanned` shows the whole pool — is excluded.
  * ⚠ **`per-command cancel is latched` did NOT appear on the next command, and that is a PASS, not
    a miss.** The DLL cleared the cancel at disconnect (*"no connection that raised it is still
    live"*), so it never survived to poison a later command at all. The row was written expecting
    the *next* command to hit the latch and clear it; the shipped behaviour is stronger. **Reword the
    row rather than re-running it.**

  ### ⛔ Add a THIRD trap to the list below: `trigger_scan` is ASYNC
  The obvious vehicle — the multi-second startup scan — **cannot arm the latch**, and this was
  measured, not reasoned. Killing the UI 900 ms into a 3.4 s scan produced **no** arming line, and
  the pipe log says why:
  ```
  12:03:18.127 Received: {"cmd":"trigger_scan","id":2}
  12:03:18.127 trigger_scan: Starting async engine scan...
  12:03:18.128 RunScan: started
  12:03:18.636 Received: {"cmd":"scan_status","id":8}
  12:03:19.421 ... repeated 2x: scan_status
  12:03:21.525 RunScan: finished
  ```
  `trigger_scan` returns immediately and the UI **polls `scan_status`**. So for those 3.4 s the
  connection had no long command in flight — it is the same "thousands of short commands with gaps"
  shape as Dump All and Snapshot capture, which this row already warns about. **The scan looks like
  the ideal vehicle and is a trap.**

- ⬜ *(original instructions kept for the method)* **CE mailbox survives a dead UI client** (build 2592, B4). The evidence line is **cold** — once
  per latch, so it costs nothing to leave in. Needs a deliberate sequence but the whole answer is in
  the log, so it lives here: connect the UI, start something long (Property Search deep, or a full
  Instance Finder scan), **kill the UI process while it runs**, then use any CE-side lookup — the
  `.CT`'s Find Instance, or a teleport/GodMode hotkey on a game that resolves through the class-scan
  fallback. Grep `pipe-0.log` for `per-command cancel is latched`.
  **PASS** = that WARN appears **and** the command that follows it reports a non-zero result count.
  **FAIL** = the old signature: no WARN, and a lookup answering `0` with `scanned=<full pool>` —
  the message that made this bug read like "the object isn't there".
  > **⚠ Task Manager's Processes tab does NOT kill it.** "End task" there sends `WM_CLOSE` first and
  > only escalates if the app stops responding — so a responsive UI closes GRACEFULLY and the latch
  > is never set. Use the **Details** tab → *End process*, or `taskkill /F /IM UE5DumpUI.exe`.
  > **Measured 2026-08-06** (SEED BATTLE DESTINY REMASTERED, build 2738) on a session that did
  > exactly this: the UI still wrote `UE5DumpUI shutting down...` — a line `TerminateProcess`
  > cannot produce — and the server logged `Stop entry (conns=0)` /
  > `Stop conn drain satisfied, 0 left (0 ms, 0 cancel re-asserts)`. `g_perCommand` was never
  > latched, so the run proved nothing. **Absence of the WARN is not a FAIL** — check those two
  > lines first to tell "the guard worked" apart from "the test no-opped".
  > The other half matters just as much: **something long has to be IN FLIGHT** when the UI dies.
  > That session's last pipe traffic was 40 s before the close, so there was no command for the
  > disconnect monitor to latch a cancel against.
  >
  > **In normal use this only triggers on a real UI crash** — every orderly exit disconnects
  > cleanly — which is why it has stayed unverified and why it is worth keeping the cold WARN in.
  > It is NOT hard to provoke, though: one `taskkill /F` during a Deep Property Search is the
  > whole test.
  >
  > ### ⚠ Check the ARMING line first — `client gone mid-command`
  >
  > The latch has its own WARN, emitted immediately before it
  > ([`Fern.cpp:769`](../dll/src/Fern.cpp:769)): `client gone mid-command (err=…) — aborting
  > in-flight op`. **Grep for that BEFORE grepping for the B4 line.** Absent ⇒ `g_perCommand` was
  > never latched ⇒ the B4 WARN was right to stay silent and the run proved nothing. Only when it
  > IS present does the absence of the B4 line mean anything.
  >
  > ### The axis is not "long" — it is "ONE call that blocks for seconds"
  >
  > `MonitorLoop` sleeps **200 ms** between polls ([`Fern.cpp:732`](../dll/src/Fern.cpp:732)) and
  > peeks only connections whose `inFlight` is set (`:743`), so a single command has to still be
  > running when a poll lands. **A CHUNKED operation never arms it, however many minutes it takes**
  > — it is thousands of short commands with gaps in between.
  >
  > Two that look like the obvious choice and are **both traps** (each cost a real run on
  > 2026-08-06):
  > - **Dump All Metadata** — `DumpAllService` is a `do/while` over
  >   `GetObjectListAsync(offset, pageSize)` ([`DumpAllService.cs:115-133`](../ui/UE5DumpUI/Services/DumpAllService.cs:115))
  >   plus `WalkClassesBatchAsync` in chunks of 200 (`:262`). **Measured: `get_object_list` pages
  >   50–80 ms apart** (19:45:16.124 → .201 → .249 → .323) — no poll ever caught one. The client's
  >   death surfaced through the connection's own write instead (`Failed to write response` →
  >   `Client disconnected`, same millisecond) and no latch was set.
  > - **Snapshot capture** — `Renge.h:161-165` says it outright: `begin_snapshot` + `snapshot_chunk`
  >   stream `[offset, offset+limit)` **"like get_object_list"**. Same shape, same no-op.
  >
  > Use one of the **single blocking scans** instead — all in `Aura.cpp`, which holds 30 of the
  > DLL's `Tot::Requested()` checks precisely because these are the ops expected to run long:
  >
  > | Command | UI | Why it is long |
  > |---|---|---|
  > | `begin_value_scan` | Value Search, first scan | every object × every property; heaviest by default |
  > | `find_path_from_gworld` | 🌍 Locate in GWorld | BFS, and the toolbar **depth slider** is a direct cost knob |
  > | `find_refs_to_uobject` | Live Walker → Find Refs | reverse-scans the whole pool incl. nested structs/containers |
  > | `find_instances` | Instance Finder | full-pool scan |
  >
  > On a small pool (SEED BATTLE: 69,688 objects) even these can finish fast — which is why the
  > arming line, not a stopwatch, is the thing to check. Locate-in-GWorld with the depth slider
  > raised is the only one with a knob you can turn until it is slow enough.

#### ② Manual-only — still open: B29 (third-party-wrapper case) / B25

- ✅ **Symbol-export GWorld no longer claims to have an AOB** (build 2581, audit #4 B2) —
  **VERIFIED 2026-08-12 on Satisfactory** (UE 5.6, 137,391 objects, DLL build 2798). Both halves.

  The precondition held live: `scan-0.log` shows
  `TrySymbolExport: Found '?GWorld@@3VUWorldProxy@@A' in module 'FactoryGameSteam-Engine-Win64-Shipping.dll'`
  → `GWLD_EXP … [WINNER]`. GObjects (`GOBJ_EXP`), GNames (`GNAM_EXP_TOSTR`) and GEngine (`GENG_EXP`)
  resolve the same way — this build is modular, so **all four** exercise the gate at once.

  | half | evidence |
  |---|---|
  | toggle greyed | `get_pointers` returns `gworld_aob: ""` → `IsAobSymbolAvailable=false` → `CanUseAobSymbol=false` → `IsEnabled` binding at [`LiveWalkerPanel.axaml:231`](../ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:231). **Observed on screen**: the *AOB* item in Live Walker → Options renders dim while every sibling is white. |
  | export resolves | *Copy CE XML* on `GWorld → PersistentLevel`: 160,036 chars, **zero `??`**, **zero AOB markers** (`AOBScanModuleUE` / `aobscan` / the mangled name / `UE_GWorld`), root `<Address>1E4542EAEA0</Address>` literal with `+30` child offsets. |

  The mechanism is [`IsCeReplayableAob`](../dll/src/Himmel.h) suppressing the triple for
  `SymbolExport` / `SymbolCallFollow` / `CallFollow`, whose comment already cited this item.

  > ### 🔴 Found en route, and it was NOT cosmetic — fixed in build 2798
  >
  > The same payload reported `gworld_method: "aob"` next to `gworld_pattern_id: "GWLD_EXP"` and an
  > empty AOB triple: three fields disagreeing. [`Genau.cpp`](../dll/src/Genau.cpp) hardcoded
  > `"aob"` at all five sites whenever the scan returned non-zero, so every symbol-export and
  > CallFollow win was mislabelled, and `FindAll: Complete` printed `(aob)` for all four.
  >
  > **The trap:** the obvious fix — report the true mechanism — regresses the UI on its own.
  > `PointerPanelViewModel` asked `method != "aob"` to mean *"found via fallback"*, and
  > `ShowGWorldRecovered` asked it to mean *"found via a recovery path"*. Relabelling alone would
  > have raised a spurious **"found via fallback"** warning on all four pointers on Satisfactory
  > **and** badged its GWorld as **"recovered"** when nothing recovered anything. A symbol export is
  > the *strongest* result the scanner produces (priority 0, tried first, survives a recompile), not
  > a fallback.
  >
  > So both sides moved: the DLL reports `symbol` / `symbol_call_follow` / `call_follow` / `aob`
  > (`ScanMethodName`), and the panel asks a membership question (`IsDirectScan`) instead of an
  > equality one. 8 tests, including recovery paths still badging and an unknown future value
  > failing loud rather than silent. Measured before/after on the same game: all four went
  > `aob` → `symbol` / `symbol_call_follow`, with the AOB triple still empty.

- ⬜ **CE-plugin double-inject guard — the third-party-wrapper case** (build 2577, audit #4 B29).
  Ownership is now decided by PE ProductName, not file name. **Verified on real files here** (our 5
  binaries say `UE5CEDumper`; the 4 System32 counterparts say `Microsoft® …`), but the case that
  motivated the fix has no test material on this machine. **To test:** install ReShade (or drop any
  third-party `dxgi.dll`/`dinput8.dll` wrapper) into a UE game folder, attach CE, click
  *UE5CEDumper: Inject && Connect*. PASS = it injects normally, and the DLL log carries
  `'dxgi.dll' is loaded but is not ours`. FAIL = the old *"already loaded … no injection needed"*
  message, after which the UI cannot connect. Also worth eyeballing there: a game path with
  non-ASCII characters must now appear intact in that message (it used to render as `EVERSPACE? 2`).

- ✅ **Recycle-Bin refusal on a volume with no bin** (build 2621, B13/B41) — **VERIFIED 2026-08-12,
  end to end, both directions. It FAILED twice on the way and took THREE fixes** (builds 2799 + 2801):
  the detector could not see the condition it was named after, the refusal it then produced was
  silently dropped before reaching a row, and — found only because the negative control was run — the
  candidate folder was never examined at all. Post-fix re-measurement on the AOT build is at the
  bottom of this entry.

  The check never needed the UI: `VolumeHasRecycleBin` is upstream of every row, and it answered the
  question with `SHQueryRecycleBin(root) == S_OK` alone. That call reports on the bin's **contents**,
  not on its **policy**. Measured on two different fixed volumes with `NukeOnDelete=1` (a 10 GB iSCSI
  scratch volume, and the data drive with the bin switched off deliberately):

  | detector | result |
  |---|---|
  | registry | `HKCU\…\BitBucket\Volume\{guid}\NukeOnDelete = 1` |
  | **functional** — throwaway file, `SHFileOperation` + `FOF_ALLOWUNDO` (*exactly* what `MoveToRecycleBin` issues) | `rc=0`, `fAnyOperationsAborted=false`, bin item count **5 → 5**, **file gone** |
  | the shipped probe `SHQueryRecycleBinW(root)` | `hr=0x0`, `items=5` → **`VolumeHasRecycleBin` returned `true`** |

  So the shipped sequence was: probe says the bin works → `MoveToRecycleBin` proceeds → the shell
  returns success → the caller reports *"N files moved to the Recycle Bin"* → **the files were
  permanently destroyed.** That is verbatim the outcome
  [`WindowsPlatformService.cs`](../ui/UE5DumpUI/Services/WindowsPlatformService.cs)'s own comment
  says the refusal exists to prevent; the refusal simply never fired. `SHQueryRecycleBin` succeeds
  because the stale `$RECYCLE.BIN` folder and its leftover items are still on disk after the policy
  is turned off — emptiness and disabled-ness are different facts and it can only see the first.

  **Fix (build 2799):** the policy is now read from the registry *before* the shell is asked, via a
  pure [`RecycleBinPolicy`](../ui/UE5DumpUI/Core/RecycleBinPolicy.cs) that encodes Windows' real
  precedence — Group Policy `NoRecycleFiles` (machine, then user) → `UseGlobalSettings` +
  global `NukeOnDelete` → per-volume `NukeOnDelete`, with **absent ≠ 0** throughout. The
  `SHQueryRecycleBin` call is kept as a *second* gate (it still catches a volume the shell cannot
  service at all); both must pass. 18 unit tests cover every combination, including the two
  directions that are easy to get backwards under `UseGlobalSettings`.

  **Post-fix measurement, same machine, same session:** `T:` (`NukeOnDelete=1`) → `IsDisabled=true`
  → probe returns **false**, so the refusal fires. `C:` and `D:` (`NukeOnDelete=0`) → **true**.
  `D:`'s bin was **empty** at the time, which is the control that matters: an enabled-but-empty bin
  must still read as present, and any "fix" keyed off the item count would refuse every clean
  machine.

  ### ✅ The end-to-end half — DONE 2026-08-12. The prediction held, and the CONTROL found a second defect.

  **The blocker recorded in the previous handover was a miscount, not a bug.** `FakeGameT` *was*
  being detected the whole time — `Generic scan found 8 UE game(s)` already included it, and the
  panel showed the row. Nothing in `LooksLikeUeGameRoot` / `WalkDrive` / `IsExcludedBySteam` needed
  investigating. The fork written up for it (drop a `dummy.pak` and rescan) is moot; do not run it.

  **The rig, rebuilt with a real game** rather than a synthetic one — copy a small UE title wholesale
  instead of faking its shape, which removes every "is the detector confused?" question at once:

  ```
  T:  10 GB iSCSI volume, Fixed, $RECYCLE.BIN present, per-volume NukeOnDelete the ONLY variable
      T:\Light Maze\   <- Steam's "Light Maze" (215 MB, 27 files) copied whole from D:
  ```

  Deploy `version.dll` to it → delete everything Steam would own → `T:\Light Maze\LightMaze\
  Binaries\Win64\version.dll` is the sole survivor, which is exactly the leftover-after-uninstall
  shape. Re-scan drives so the game leaves the live list (it does, 9 → 8, so no `LiveGameFolder`
  veto can mask the result), then press *Find leftovers*.

  The VALID pair — same process, same bytes on disk, one registry DWORD between them. (An earlier
  pair, before the app was restarted, read **22 examined / 0 rows both ways** and is discarded: 22
  means the T: folder was not among the candidates, so those two runs measured nothing. See ② — that
  discrepancy is the whole finding.)

  | run | `T:` `NukeOnDelete` | folders examined | rows |
  |---|---|---|---|
  | 1 | **1** (bin off) | 23 | **0** — the refusal is computed, then dropped |
  | 2 | **0** (bin on) | 23 | **1** — `Recycle version.dll — folders left in place` |

  #### ① The predicted defect — now MEASURED, and fixed

  `PlanPrune` returns `NotOnFixedDrive`; the surface filter in
  [`ProxyDeployService.cs`](../ui/UE5DumpUI/Services/ProxyDeployService.cs) kept only
  `Deletable`/`FileOnly`, so the refusal never reached a row. The user was told **"No leftover proxy
  DLLs found (23 folder(s) examined)"** while our DLL sat on the one kind of volume where deleting it
  by hand cannot be undone. B13/B41's own PASS criterion was unobservable as shipped — exactly as
  predicted from code reading, and now watched happening.

  #### ② The defect the CONTROL found — and it is the one users hit daily

  In the FIRST pair (the discarded one above), flipping the bin back on was supposed to be a
  formality. It also returned 0, **which is what proved the bin-off run had measured nothing**: the
  T: folder was never a candidate. `CandidatesFromLogs` read our own `view-*.log` with
  `File.ReadLines`, which opens `FileShare.Read` and therefore **cannot open the live `view-0.log`
  that our own logger is holding**. The per-file `catch` swallowed the sharing violation, so the
  current session's entire deploy log contributed zero candidates; those 22 came from an *archived*
  `view-20260731-*.log`. Restarting the app rotated our deploy line into an archive too, the count
  went **22 → 23**, and only then was the folder actually examined. That single number is what
  separates "looked and found nothing" from "never looked" — without it, the discarded pair would
  have been recorded as a clean confirmation of the prediction.

  > **What this cost the user:** deploy a proxy, uninstall the game, press *Find leftovers* in the
  > same session → **nothing found**. It only appears after an app restart. `SteamShapeScan` hides
  > this for Steam titles (it sees them without the log), so it bites exactly the non-Steam
  > locations the log sources exist to cover.
  >
  > **Generalisable lesson 1:** `File.ReadLines`/`ReadAllLines` cannot read a file anything else
  > holds open for writing — including *our own* logs. Any future code that mines our logs must use
  > `ProxyDeployService.ReadLinesShared` (`FileShare.ReadWrite | FileShare.Delete`).
  >
  > **Generalisable lesson 2 — and it is the one that generalises furthest.** When the PASS criterion
  > is that something does **not** appear, the run that makes it appear is not optional, however much
  > it looks like a formality. **Absence is the cheapest result in the universe to produce by
  > accident**: a broken rig, a filter that never ran, a candidate list that never included the item —
  > all of them render as a clean-looking confirmation. Here the "formality" is the *only* thing that
  > distinguished a correct prediction from a measurement of nothing. The companion habit is to read
  > the **"examined N"** counter first: 22 → 23 was what separated *looked and found nothing* from
  > *never looked*, and no amount of staring at the 0 would have revealed it.
  >
  > This also overturns a lesson recorded on 2026-08-12 that read *"B13/B41 does not need the UI at
  > all — measure `VolumeHasRecycleBin` and you are done."* Measuring the gate proved only that the
  > gate answers correctly; **it said nothing about whether the user is ever shown anything**, and in
  > fact they were not. If the PASS criterion is a string on screen, the string has to be looked at.

  #### The fixes (build 2801)

  | # | change |
  |---|---|
  | ① | `OrphanVerdictRules.ShouldSurface` now keeps `NotOnFixedDrive`. The scan filter, the row's `IsActionable` and the removal re-check were three hand-written copies of two *different* predicates; they are now one pure pair in [`OrphanScanTypes.cs`](../ui/UE5DumpUI/Models/OrphanScanTypes.cs), so they cannot drift. |
  | ② | `ReadLinesShared` replaces `File.ReadLines` for the log sweep **and** for the Steam `.acf` read (same bug there: a manifest Steam holds open made `TryReadAcfInstallDir` report *unreadable*, which fails closed and silently refuses every Steam candidate — safe, but the feature just stops working). |
  | ③ | The recycler question moved **below** `ClassifyLeaf`, so a no-bin volume can no longer manufacture a refusal for a folder holding nothing of ours — and the refusal now carries the file list so the row can NAME the file. |
  | ④ | Honesty: a blocked row authorises nothing (`AuthorisedFiles` empty even if the verdict gate were relaxed), the report says *"NOT removable"* instead of *"to be recycled"*, and the status line counts blocked rows separately. |

  22 new tests, including the negative controls that make them mean something: the same folder with a
  working bin is still `Deletable`; a no-bin volume holding a *foreign* DLL is `ForeignFilePresent`,
  not a recycler refusal; `ShouldSurface` still drops all nine refusals that hold nothing of ours; and
  the `ReadLinesShared` test asserts `File.ReadLines` **throws** on the same handle first, or it would
  be asserting nothing.

  #### Post-fix re-measurement — build 2804, AOT/trimmed `dist\UE5DumpUI.exe` (54.3 MB), same rig

  Both directions, same process, one registry DWORD apart:

  | `T:` `NukeOnDelete` | examined | rows | the row says | checkbox |
  |---|---|---|---|---|
  | **1** (bin off) | 23 | 1 | *"This volume has no working Recycle Bin (removable/network, or the bin is disabled for it), so a delete here would be PERMANENT. Refused — remove the file by hand if that is what you want."* | **disabled** — clicking it does nothing, `Delete checked (0)` stays greyed |
  | **0** (bin on) | 23 | 1 | `Recycle version.dll — folders left in place` | **enabled** — ticks, `Delete checked (1)` goes live |

  The second row is the half that stops this being a probe that merely refuses everything: the SAME
  folder becomes actionable when, and only when, the bin is switched back on. Status line reads
  *"Found 1 leftover proxy DLL(s) — nothing removed yet. 1 cannot be removed from here — read the row
  for why."* Nothing was deleted at any point; the row was left unticked.

  **The rig is still on disk** (`T:\Light Maze\LightMaze\Binaries\Win64\version.dll`, T: back to
  `NukeOnDelete=1` as found) if this ever needs re-running. Rebuild it by copying any small UE game
  wholesale to a scratch volume — that is what made this tractable after the synthetic one wasted a
  session on a detection question that turned out not to exist.

- ✅ **VERIFIED 2026-08-19 `[B25-SYNTH-2026-08-19]` — the pre-4.11 refusal no longer fires on one
  PE field, and the UE3 refusal still does** (build 2621, checked on dist **1.0.0.3263**).
  **Both branches PASS**, on two purpose-built exes and no game at all. Rig:
  `tools/verify/b25_marker_exes.py` (compiles them through `cmd`+`vcvars64`, then asserts each
  artifact actually carries — and the other actually lacks — what its branch depends on, so a
  stripped literal cannot masquerade as a clean refusal-did-not-fire).
  - **Branch A — PASS.** `b25a_subfloor.exe`, a PE `PRODUCTVERSION 4,5,0,0` and nothing else:
    `DetectVersion: PE VERSIONINFO -> UE4.5 (treated as 400+minor)` then
    `DetectVersion: PE VERSIONINFO says UE 405, below the 411 floor — NOT accepting that on its own
    (it would refuse the whole scan). Corroborating against the memory string scan.`
    **No `SKIPPING the scan` anywhere**, and `FindAll: Complete — … UE=405` — the scan ran to the end.
  - **Branch B — PASS.** `b25b_ue3.exe`, no version resource + the literals `UnrealEngine3` and
    `SeqAct_Interp`: both markers hit (`0x16350`, `0x16360`),
    `PRE-UE4 engine POSITIVELY identified (2/4 markers, 2 needed) -> sentinel 300`, then
    `FindAll: PRE-UE4 engine (Unreal Engine 3) — SKIPPING the scan`.
  - ⭐ **The two branches are separated by a number, not just by a grep.** `scan-0.log` is
    **3,886 lines for A** and **10 lines for B** — A really did sweep the AOB tables and B really
    did refuse before starting. A pair of greps could both be satisfied by a scan that half-ran;
    the line counts cannot.
  - **Negative control, free and same-day**: a stock `python.exe` sleeper reaches the *identical*
    terminal branch and logs `pre-UE4 markers 0/4, below the 2 needed` (5,305-line scan log — it
    scans, like A). Same code path, markers absent ⇒ B's 2/4 is caused by the two literals and by
    nothing else about being a small synthetic exe.
  - ⚠ **Not covered, deliberately**: the UE-version-*override* route the original step offered as an
    alternative provocation. The PE-resource route exercises the same gate and needs no UI.

- ✅ **DONE 2026-08-18 — Duplicate GameEngine records no longer break each other** (build 2621, B26).
  Ran on DumperTest Development, dist 3262, AOBMaker bridge live (the row's precondition — verified
  first, so step 1 could not pass vacuously).
  - **Step 1 PASS.** First *Get GameEngine* → `Added 'Get GameEngine → symbol UE_GameEngine' to Cheat
    Engine via AOBMaker…`. Second click → *"'Get GameEngine → symbol UE_GameEngine' was already
    pushed to Cheat Engine this session — copied it as CE memory-record XML instead of adding a
    second record."* CE's list holds **exactly one** record under the `UE5CEDumper (DLL)` group.
  - **Step 2 PASS on its load-bearing assertion.** Pasted the XML to make a real second record,
    ticked both, unticked the **older** — the newer record's `UE_GameEngine` still resolved to
    **`0x7FF7AD323670`**, the `&GEngine` slot, *not* `??`. Enable also logs
    `[GameEngine] UE_GameEngine -> &GEngine slot 0x7FF7AD323670 (auto-follows)`, so the slot binding
    (rather than a snapshot buffer) is what this title takes.
  - ⚠ **The expected debug line is BRANCH-SPECIFIC and cannot appear here.**
    `Services/PointerQueryScriptGenerator.cs:238-250` emits *"another record owns … leaving it
    alone"* only under `mayFallBack` — the `allocateMemory` **buffer** flavour, where there is
    something to free. DumperTest resolves the `&GEngine` AOB, so its record takes the **slot**
    flavour (`:256-258`), whose `[DISABLE]` is two lines with no ownership guard because there is no
    buffer. **This checklist's wording should be scoped to the buffer flavour**; expecting the string
    on the slot flavour is a mis-specification, not a failure.
  - ✅ **The slot flavour's `[DISABLE]` was broken — FIXED 2026-08-19; see `[SLOTSYM-2026-08-18]` in "Pending live-game verification".**

### ✅ FIXED 2026-08-19 `[SLOTSYM-2026-08-18]` — the slot `[DISABLE]` now actually unregisters (writeup moved to "Pending live-game verification")

*Found while separating B26's two branches. The reproduction (untick the single record → still
`140701739398768`; one manual `unregisterSymbol` cleared it on the first call) is preserved for
history below, with one correction: the mechanism was NEITHER of the two this section originally
posited.*

**Mechanism (read from the code, not guessed).** The `:256-258` cite was the **GWorld** branch, which
already unregistered correctly. The record that reproduced the bug is the **GameEngine** target, which
takes the `mayFallBack` `[DISABLE]` branch — there, `unregisterSymbol('UE_GameEngine')` was nested
inside the buffer-only `if mem and mem ~= 0 and cur == mem` guard. On the slot sub-path there is no
buffer, so `mem = getAddressSafe('UE_GameEngine_buf')` is nil, both the `if` and `elseif` are skipped,
the symbol is never unregistered, and the trailing UNCONDITIONAL `dbg('… unregistered')` lies. So it
is a THIRD mechanism (unregister trapped in a buffer-only guard), closest to variant (a). **(b)
double-registration is refuted** — ENABLE does a single `registerSymbol` on op 2, which is why "one
manual `unregisterSymbol` sufficed".

**Fix (applied 2026-08-19).** Both slot ends (GWorld + the GameEngine slot sub-path) now go through
shared `CeLuaHygiene.AppendSlotSymbolRegister`/`AppendSlotSymbolRelease`: a per-symbol reference count
in a CE Lua global keeps the symbol for a second still-ticked record (an address marker can't — two
records resolve the IDENTICAL slot), the last holder unregisters in a bounded loop, and the message
re-reads `getAddressSafe` AFTER the unregister. Also removed an accidental duplicate
`AppendContractCheck`. Pinned by 6 new `PointerQueryScriptGeneratorTests` + a real-`lua` runtime
simulation. Live-check steps are under `[SLOTSYM-2026-08-18]` in "Pending live-game verification".

**Why it mattered.** A symbol that survives its record's disable is a **stale symbol across a game
restart**: `UE_GameEngine` kept resolving to the previous process's module base, and anything built
on it read dead memory.

- ✅ **The five dead coord-grid sort headers** (build 2610, B16) — **VERIFIED 2026-08-12**, on the
  AOT/trimmed `dist\UE5DumpUI.exe` (56.9 MB, build 2794) against the DumperTest Development package.
  All five reorder **and** reverse on the second click; Label (the non-regression control) still
  works. 10 of 10 observed orders matched the prediction made *before* clicking:

  | click | order (by row label) | | click | order |
  |---|---|---|---|---|
  | X ↑ | 3,4,1,5,2 | | X ↓ | 2,5,1,4,3 |
  | Y ↑ | 5,2,1,4,3 | | Y ↓ | 3,4,1,2,5 |
  | Z ↑ | 4,1,2,5,3 | | Z ↓ | 3,5,2,1,4 |
  | Yaw ↑ | 2,1,4,5,3 | | Yaw ↓ | 3,5,4,1,2 |
  | Dist ↑ | 1,4,5,3,2 | | Dist ↓ | 2,3,5,4,1 |

  > **The dataset was built so the test could fail.** Five rows were entered via *+ From fields* with
  > values chosen so that **X, Y, Z, Yaw, Dist and insertion order all induce six DIFFERENT
  > orderings**. With a lazier dataset — say monotonic coordinates — a sort that did nothing at all
  > would have reproduced insertion order and read as a pass on every column. Dist was cross-checked
  > independently: the grid's own values (0 / 4,205 / 3,734 / 891 / 3,590) matched hand-computed
  > distances from the live pose to the unit, so the column is genuinely computed, not a placeholder.
  >
  > **Not exercised: Group and Map.** *+ From fields* leaves Group empty and stamps every row with
  > the current map, so both columns held one value across all five rows and no ordering could be
  > observed. Label carried the load as the text-column control. Anyone re-running this should set
  > distinct groups (row editor → Group → Apply) to close that half.

- ✅ **Second launch raises the first window** (build 2610, B42) — **VERIFIED 2026-08-04 (maintainer).** Run `dist\UE5DumpUI.exe`, then run
  it again (double-click the exe, or the shortcut). **PASS** = the existing window comes to the
  front — including when it was minimized — and no second window appears. **FAIL** = nothing
  visibly happens, which is the old behaviour. Worth testing with the first instance **connected to
  a game**, since the window title carries the module name and a title-based search would miss
  exactly then.

- ✅ **Force submenu with nothing selected** (build 2610, B36) — **VERIFIED 2026-08-04 (maintainer).** Property Search → run a search →
  **right-click empty space below the rows**, or a row you have not left-clicked. **PASS** = no
  Force submenu. Left-click a BoolProperty row, right-click it: only Force ON / OFF. FAIL = all
  four actions at once. (Needs the Experimental toggle on for the submenu to exist at all.)

- ✅ **Close the game with a hold worker live** (build 2596, B14 + R5) — **VERIFIED build 2638**
  (DQ7R, bullet-time + See-through ON, closed from the game's own window: no event-log entry, no
  dump). Took THREE attempts and the first two failures are the whole lesson — see below.
  *Earlier:* **FAILED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622, SCOPE CORRECTED build 2628, needs a re-test.**
  DQ7R crashed at 21:05:06 on build 2622 (every fix present). The WER dump
  (`%LOCALAPPDATA%\CrashDumps\DQ7R-Win64-Shipping.exe.55564.dmp`) gives
  `0xC0000409` with **param[0] = 7 = FAST_FAIL_FATAL_APP_EXIT** — `abort()`/`std::terminate` —
  and the whole faulting stack inside `version.dll` + the CRT. **No `tick threw` line anywhere**,
  so no guard was even reached. Context: `pipe-0.log`'s last line is a `FindInstancesByClass`
  reporting `nonNull=35109` where the call 0.3 s earlier said `154964` — the game was freeing its
  object pool while we walked it.
  **The fix was right; its SCOPE was wrong.** The finding said "2 of 7 thread procs"; the DLL has
  ~15 places where a throw is fatal. Build 2628 adds `Routine::RunThreadGuarded` to all of them,
  the important one being `Stark::HookedProcessEvent` — it runs on the **game's own thread**,
  entered from game code with no handler for us, and allocates twice.
  **Re-test:** same steps below. **PASS** = no event-log entry. If it fires again, `init-0.log`
  now carries `UNCAUGHT exception … contained` naming the thread — that is what routing every
  entry point through one helper buys.
  *Note: the Elliot crash in the same event log is build **2567**, before B14 shipped — that one
  is the original bug, not a regression.*
  This is the exact repro that
  produced the live `0xC0000409` in build 2389, re-run against the loops that were still unguarded.
  **To test:** enable **two** holds whose workers were previously bare — Time Dilation (Hemmung) and
  Move Speed (Laufen) — plus See-through, then **disable See-through while the game is backgrounded**
  so its `PendingRestoreLoop` is actually waiting, and close the game from its own window.
  **PASS** = no crash, no WER minidump, nothing in the Windows Application event log. **FAIL** =
  exit code `0xc0000409` with a fault on a `version.dll` stack — that is an exception escaping a
  thread entry. If `init-0.log` carries `tick threw (…) — skipping (game tearing down?)`, the guard
  fired and did its job; its absence proves only that nothing threw this time.
  *Why it can't be tested here: the throw comes from reading a UFunction in a process that is
  actively freeing it — there is no way to stage that outside a real game shutdown.*

- ✅ **DONE 2026-08-18 `[ELLIOT-B5-2026-08-18]` — Provoke the concurrent `UE5_Init`** (build 2592, B5).
  Ran on **Elliot** launched through its deployed **`dxgi` proxy**, which is what makes the second
  caller reachable: `init-0.log` opens with
  `DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)`, so the pipe is live
  with both cached pointers still 0 — the row's precondition, confirmed rather than assumed.

  **All four PASS conditions met, in one log window:**
  ```
  12:18:20.543 UE5_Init: Starting initialization...
  12:18:20.543 UE5_Init: init already in progress on another thread — tid=23592 is waiting (guard working, not an error)
  12:18:20.545 UE5_Init: init already in progress on another thread — tid=34088 is waiting (guard working, not an error)
  12:18:23.773 UE5_Init: Complete (UE504, GObjects=0x149BFC140, GNames=0x149B18600, Objects=85068)
  12:18:23.773 UE5_Init: tid=23592 resumed after waiting (first caller succeeded — returning its result, no second scan)
  12:18:23.774 UE5_Init: tid=34088 resumed after waiting (first caller succeeded — returning its result, no second scan)
  ```
  Exactly **one** `Starting initialization...`; both waiters named by tid; both resumed on the first
  caller's result with **no second scan**. And the callers themselves completed normally — all three
  CE threads returned `r=1` after **3234 ms**, i.e. they blocked for the scan and shared its result
  rather than erroring or re-scanning.

  ### ⚠ How this was made deterministic — the naive staging does NOT work
  Two earlier attempts on this same row failed to produce the handshake **even though the timing
  looked right**, and both failures are worth keeping:
  1. **UI Scan, then a CE call.** The CE call landed *after* the 3.3 s scan finished and returned in
     **16 ms** off the cached result. GUI round trips are 2–6 s; the window is 3.3 s.
  2. **A CE loop calling `UE5_Init` every 120 ms.** One Lua thread issues `executeCodeEx`
     **synchronously**, so call #1 simply blocked for the whole scan (`call 1: BLOCKED 3234 ms`) and
     calls 2–60 all ran afterwards, logging `Already initialized`. Sixty attempts, never two callers
     at once. *This still proved the FAIL condition absent — one `Starting` line across 61 calls —
     but it cannot produce the waiting handshake.*

  What works is **three `createThread` calls fired together**, so the second and third genuinely
  enter `UE5_Init` while the first holds it. **Concurrency had to be constructed; it does not arise
  from doing things quickly.**

  ℹ️ *The row describes the second caller as a CE mailbox command (`Mimic::EnsureInitialized`);
  here it is a direct `UE5_Init` from CE Lua. Same entry point, same guard — but the mailbox flavour
  specifically is still unexercised.*

- ⬜ *(original instructions kept for the method)* **Provoke the concurrent `UE5_Init`** (build 2592, B5) — the active half of the passive check in
  ① above. Needs the **proxy** launch path, because that is what makes the second caller reachable:
  the proxy starts the pipe *without* scanning, so both cached pointers are 0 while the pipe is
  already live. **To test:** launch the game with a deployed proxy DLL, connect the UI, click Scan,
  and **while the scan is still running** trigger any CE-side mailbox command (tick the `.CT`, or a
  teleport hotkey) — that path calls `Mimic::EnsureInitialized`, which is the second `UE5_Init`.
  **PASS** = `init-0.log` shows `init already in progress on another thread — tid=… is waiting`
  followed by `resumed after waiting (first caller succeeded — returning its result, no second
  scan)`, exactly **one** `Starting initialization...`, and the CE command then works normally.
  **FAIL** = two `Starting` lines, or a `validated=yes` summary on a session where drill-down shows
  every property type unknown — that is the silent-corruption shape this fix exists to prevent.
  *Why it can't be tested here: it needs two real threads racing a multi-second scan inside a live
  game; the unit tests can only pin the flag semantics, not the timing.*

- ✅ **DONE 2026-08-18 — `.CT` DLL discovery survives a missing breadcrumb** (build 2576).
  Ran exactly as written: moved `%LOCALAPPDATA%\UE5CEDumper\dll-path.txt` aside, reloaded the `.CT`
  **from CE's Load Recent menu** (answering **No** to "save your last changes" — saying Yes would
  have written this session's test records into the repo's `scripts/UE5CEDumper.CT`), ticked `init`
  and then `Inject DLL + Start Pipe Server`.
  - **The discovery half PASSES.** With no breadcrumb file on disk, `UE5Dumper.dll` still loaded into
    `DumperTest.exe` and `\\.\pipe\UE5DumpBfx` came up. So the fallback chain reaches the DLL without
    the file the row is named after.
  - ⚠ **The `dll-path.txt` is recreated` clause is a MIS-SPECIFICATION — the `.CT` cannot do it.**
    `grep -c 'dll-path.txt", "w"' scripts/UE5CEDumper.CT` = **0**; the only occurrences are the path
    string and one `io.open(..., "r")`. The writer is the UI:
    `DumperDllPathStore.Record` (`Services/DumperDllPathStore.cs:80`, `File.WriteAllLines` at `:118`)
    with its single caller `App.axaml.cs:91`, i.e. **UI startup**. Verified end to end: the file was
    still absent after the successful `.CT` injection, and reappeared the moment the UI was
    restarted, containing `D:\Github\UE5CEDumper\dist`.
    ⇒ Rewrite the row's expectation as *"the DLL resolves without the breadcrumb; the breadcrumb
    returns on the UI's next start"*. **FAIL as written would have been reported against working
    code** — the same shape as working-lessons §2.4.
  - ⚠ Incidental: the rebuilt file has **one** entry where the original had two
    (`D:\tmp\UE5CEDumper_dist` is gone). `Record` seeds an MRU from the running exe's folder, so a
    deleted breadcrumb loses history rather than merging it. Harmless here, worth knowing before
    treating that file as a durable record.
  - ⛔ **The registry / recent-files half is STILL UNEXERCISED — a cheaper slot answered.** CE's
    console names the winner outright:
    ```
    [UE5Dump] DLL path: C:\Program Files\Cheat Engine\UE5Dumper.dll
    [UE5Dump] UE5CEDumper loaded as 'UE5Dumper.dll' but parked (initState=0) — restarting in place.
    ```
    So the chain resolved **CE's own install folder**, exactly the "only runs when every cheap slot
    misses" caveat this row already carried. Leave that half ⬜. See the finding below for what it
    resolved *to*.

### ⛔ NEW 2026-08-19 `[CACHEWIPE-DLL-2026-08-19]` — the DLL half of AC4/AC5 is still there (found while fixing the UI half)

> The C# side is fixed (audit L7, build 3262): a corrupt `UE5CEDumper.{Machine}.json` is now moved
> aside as `<name>.corrupt-<stamp>` before anything may write, and an Error names the file and the
> recovery step. **The DLL writes the same file and still does the old thing.** `Flamme.cpp:371`
> (and the identical `:519`, `:580`) parse with `allow_exceptions=false` and then
> `if (!root.is_object()) root = json::object();` — so a corrupt document is replaced in memory by
> an empty one and `WriteJsonAtomic` publishes a **one-game** file over it. Every other game's scan
> record, `ueVersionUserOverride`, `invokeTimeoutMs` and the DLL's own `versionDetectRev` stamp are
> gone, with nothing logged beyond the generic save line. It is the same defect, on the same file,
> in the process **more likely to hit it** (the game side writes on every scan).
>
> **Fix shape (small):** mirror the C# rule — on `is_discarded()` / not-an-object, rename the file
> to `<name>.corrupt-<stamp>` and `LOG_WARN` the path *before* building a fresh `root`; if the
> rename fails, **return without writing**. `Flamme` already has the pieces: `MakeTempPath` shows
> the naming idiom and `SweepOrphanTemps` shows the scoped, age-guarded directory walk. Keep the
> stamp format byte-identical to `AtomicFileHygiene.QuarantineNameFor` so one sweep can bound both
> sides' quarantine later. ⚠ No test target compiles `Flamme.cpp`, so the decision (`is this
> document a wipe candidate, and may I proceed without a quarantine?`) must go in `Flamme.h` beside
> `ShouldPublishAtomicWrite`, per the L4 precedent.
>
> Not folded into L7 because that batch is scoped to `ui/UE5DumpUI/Services/`, and a DLL change
> needs a `-Target DLL` build to mean anything.

### ⛔ NEW 2026-08-18 `[STALEDLL-2026-08-18]` — a 6-month-old `UE5Dumper.dll` sits in CE's install folder and the `.CT` will pick it

> **(b) FIXED 2026-08-19** — the `.CT` now reports the resolved DLL's SIZE beside its path, so a
> stale build no longer resolves silently; live-check under `[STALEDLL-2026-08-18]` in "Pending
> live-game verification". **(a) delete/refresh the stale file remains OPEN and maintainer-only.**

*Found only because deleting the breadcrumb for the B5 run pushed discovery one slot further down.*

| file | size | date |
|---|---|---|
| `C:\Program Files\Cheat Engine\UE5Dumper.dll` | **536,064 B** | **2026-02-19** |
| `D:\Github\UE5CEDumper\dist\UE5Dumper.dll` | 2,857,472 B | 2026-08-17 |

Different SHA-256, and a **5.3× size difference** — this is not a near-miss copy, it is a build from
six months and hundreds of builds ago, from before the mailbox contract moved.

**It did not actually load this time, and the reason matters.** A fresh `UE5Dumper.dll` was already
mapped into `DumperTest.exe` from the earlier injection, so the `.CT` took its *"already loaded but
parked — restarting in place"* branch. Proven, not assumed: `init-0.log` stamps **both** injections
(10:14 and 11:30) with `build=05a9af58-dirty`, and `git log 05a9af58` dates that commit **2026-08-17**
— the dist build. **Every result recorded this session therefore ran on the current DLL.**

**The hazard is a cleanly-launched game.** With no module already mapped and no `dll-path.txt` — the
state of any machine where the UI has not been run yet, which is precisely the state the breadcrumb
fallback exists to serve — the same resolution injects the **February** DLL. Symptoms would be a
contract-range refusal at best, and at worst the class of failure this session already saw twice
(`the contract symbol resolved to the wrong memory`), with nothing on screen naming a stale DLL as
the cause.

**Actions.** (a) **OPEN, maintainer-only:** delete or refresh
`C:\Program Files\Cheat Engine\UE5Dumper.dll` — machine-local, not something to do unattended.
(b) **DONE 2026-08-19:** the `.CT` now logs `DLL size: N bytes (X.X MB)` right after `DLL path:` (and
in the startup replay), via `ue5_dllSizeText`. The build stamp itself is not cheaply readable from the
`.CT` (not a C ABI export; DLL not injected yet at report time; CE Lua has no stat-by-path API), so
file SIZE is the honest signal that separates the ~0.5 MB Feb build from the ~2.7 MB current one.
Deferred idea: read the ACTUAL build stamp — would need a tiny data export (`g_buildNumber` /
`g_buildStamp`) or a `GetFileVersionInfo` PE-version-resource read; not worth a new export here.

### 🟡 FLAKY, not chased — `SnapshotViewModelTests.GroupMatch_MissingValue_ShowsErrorNoCandidates`

- **Flaky: `SnapshotViewModelTests.GroupMatch_MissingValue_ShowsErrorNoCandidates`** — failed ONCE
  in a full parallel run on 2026-07-23 (build 2318), then passed 25/25 three times in isolation and
  green on an immediate full re-run. Unrelated to the winmm/proxy work that was in flight. This test
  class has prior form for snapshot-DB concurrency flakes (see `feedback-ci-only-test-flakes`, and
  PR #451's concurrent-first-open fix), so the likeliest cause is another store-level race under
  parallel load rather than the assertion itself. **Not chased** — one observation is not a
  reproduction. If it recurs, capture whether `GroupCandidates` was non-empty or `GroupStatusText`
  empty, since those point at different halves. Effort **S** once reproducible.


### ⬜ Shipped + unit-tests-pass but unproven on real games — the long tail: Dump Explorer identity gate · Solide `capped` badge · Genau RIP decode b2544 · M1 / M2 / M3 / M4 / M5 · DLL LOW L1 / L5 / L8 / L10 / L12 · Solide L2 / L3 / L4 · V1a · NumericAll · V1c · b719 / b648 / b636 / b642 / b637 / b644

*Every ID this heading names is a live check that lives in the bullets below and nowhere else. The
heading exists because this list spent months parented to whatever `###` happened to precede it —
most recently `[STALEDLL-2026-08-18]` — so a heading-level scan of the register found none of them.*

✅ **The `M1`–`M5` ID COLLISION is RESOLVED 2026-08-19 — `M`-numbers now mean exactly one thing.**
Until today two families shared these letters: **audit #3**'s Schlacht/Tot/shutdown-race fixes (here)
and **audit #5 D4a**'s map/set-stride findings. A register addressed by heading-level grep cannot
carry colliding IDs — sooner or later one family's close gets recorded against the other — so the
container-geometry family was **renamed `M1/M2/M3` → `MG1/MG2/MG3`** ("Macht geometry"):

| was | now | what it is |
|---|---|---|
| `M1` (D4a) | **`MG1`** | `ComputeSetElementStride` drops the `TPair`'s trailing padding (`Macht.h:314`) |
| `M2` (D4a) | **`MG2`** | `ReadTSparseArray` reads `NumFreeIndices` at `+0x3C` not `+0x34` (`Macht.h:293`) |
| `M3` (D4a) | **`MG3`** | `ComputeMapValueOffset` guesses alignment for struct values (`Macht.h:332`) |

**`M1`–`M5` therefore mean audit #3 and nothing else**, and `MG1`–`MG3` mean the container geometry.
**Why that family and not this one** — the choice was measured, not preferred: audit #3's IDs are
cited **4 times in `dev-log.md`** (append-only, must never dangle) and **23 times in `docs/archive/`**
(rewriting dated evidence would falsify history), plus 33 in-source `dll/src` comments and the five
`### M1`…`### M5` anchor headings in [audit-2026-07-14-findings.md](audit-2026-07-14-findings.md).
The D4a family has **zero** dev-log and **zero** archive references. `MG` is two letters + digits, so
it still matches `check_audit_register.py`'s `ROW_RE` (`[A-Z]{1,2}\d+`) and the three rows stay in the
register — a three-letter prefix would have silently dropped them.

✅ **The code-comment residual is CLOSED 2026-08-19 — all 9 sites renamed.** `141e8119` was scoped
docs-only, leaving 9 comments still saying `M1`/`M2`/`M3` for the D4a family:
`dll/tests/dll_helpers_test.cpp:3228,3255`, `tools/ue-sample/…/DumperTestActor.h` (6),
`tools/ue-sample/…/DumperTestTypes.h:57`. All now read `MG1`/`MG2`/`MG3`. The three families that
legitimately share those letters were left **untouched** and verified so by grep: audit #3's Schlacht
comments (`Schlacht.cpp:589,633,643,658`, `Dunste.cpp:677`, `Fern.cpp:1215`, `Frieren.cpp:612` — they
KEPT their IDs, so renaming them would re-create the collision in the opposite direction), the
statistics term in `Linie.cpp:31` (Welford's running `M2`), and the `Map="M2"` test data in
`CoordCsvCodecTests.cs:346` / `CoordLuaParserTests.cs:81`. `A2` on `DumperTestActor.h:139,164` is
audit #5 **D3**/Aura's, which was never renamed, so it stands.

- **Dump Explorer cross-game identity gate** (build 2538+; UI/C#-only, no DLL or pipe change).
  The live match joins on bare class NAMES, and every UE title has `Object` / `Actor` / `Pawn` /
  `PlayerController`, so loading game A's `.jsonl` against game B did not fail — it "succeeded",
  marked those rows **in current game**, and Jump opened B's object under A's label. Now two-tier:
  different `module` → refuse and name both sides; same module + different `pe_hash` → still match
  (a pre-patch dump of this game is the normal use) but say "Different build — offsets may have
  moved"; missing `pe_hash` → match but never claim identity was checked. Identity is read at match
  time via `GetPointersAsync`, deliberately NOT fanned into the VM — `SetConnected(true)` can fire
  before an `EngineState` exists, and that window is the wrong-game bleed in C2 above.
  **What offline already settled — do not spend live time on it:** all four arms plus the
  probe-throws path (`DumpExplorerTests` ×5, both directions), and the refusal was verified to FAIL
  when the module comparison is neutered.
  **What ONLY a real game can prove:** that `EngineState.ModuleName` and the dump's `meta.module`
  actually agree on the SAME game — they come from different producers (live DLL vs
  `DumpAllService` at export time), and if one carries a path or different casing the gate would
  refuse a legitimate same-game match. Acceptance: (1) export a Dump All from game X, keep X
  connected, Re-check → matches with NO caveat; (2) load that file with game Y connected → refused,
  status names X and Y, every row unmatched, Jump offers nothing; (3) load an OLD dump of X after
  an X patch → matches WITH the "Different build" caveat. Case (1) is the regression risk — a false
  refusal there breaks the feature for its main use. **No log marker** for the pass; the refusal
  logs `DumpExplorer live match refused: dump module '…' != live module '…'`.
  🟡 **Case (1) has evidence (2026-08-05, DQ7R).** The maintainer loaded a **different session's dump
  of the same game** and it matched; `DumpExplorer live match refused` appears **zero** times across
  every DQ7R log. That is the regression risk retired — `EngineState.ModuleName` and the dump's
  `meta.module` do agree on the same game despite coming from different producers. **Cases (2) and
  (3) are still ⬜**: (2) load that dump with a *different* game connected → must refuse and name both
  sides; (3) load a pre-patch dump of the same game → must match **with** the "Different build" caveat.
  Note (3) needs an actual DQ7R patch to come along, so it is opportunistic, not schedulable.

- **Solide pool-truncation badge — `⚠ capped` / "cap reached, more exist unheld"** (build 2531+;
  DLL `Solide`/`Fern` + Property Search + Teleport Stealth card). `Aura` already computed
  `rset.truncated` and `Solide` was dropping it, so "0 live instances matched" and "matched more
  than `SOLIDE_MAX_INSTANCES`=256 and discarded the rest" were indistinguishable. Now plumbed to
  both `force_field` and `get_forced_fields`, and the Stealth card **withdraws** its
  "you are minimal to detection" claim when the pool was capped (that claim is false for every
  instance past the cap).
  **What offline already settled — do not spend live time on it:** the wire parse both ways incl.
  the older-DLL missing-key default (`SolideTruncationWireTests`, 4 tests), both VM messages in
  both directions (`PropertySearchForceTests` ×3, `TeleportViewModelTests` ×1), and the prune-guard
  swap being an exact no-op (`!rset.truncated` ≡ the old size test on this path, since
  `FindInstancesByClass` is called with the default `buildHistogram=false`). All 8 were verified to
  FAIL when the implementation is reverted — three separate negative controls.
  **What ONLY a real game can prove:** that the flag ever fires. It needs a class with **>256 live
  instances** where a Force hold is meaningful — projectiles, crowd NPCs, destructible props are the
  likely candidates; most gameplay classes never reach the cap, which is exactly why this went
  unnoticed. Acceptance: hold a field on such a class → the strip row shows `⚠ capped` next to
  `(256 held)` and the status line ends "cap reached, more exist unheld"; hold on a small class →
  neither appears. **No grep-able log marker** — the DLL logs nothing on truncation; the evidence is
  the badge and the status text. Secondary check: with the pool capped, `RemoveForce` must still
  restore cleanly (the base-prune guard is skipped while truncated — L4), so verify no field is left
  stuck at the forced value after Reset.
  ⬜ unverified.

- **Copy CE Field drills object-pointer arrays — leaf + GWorld-path spine + dup-crumb dedup — DONE +
  MERGED (PR #323, builds 1364-1379).** LEAF (`SpawnedAttributes[2]` → `CharacterAttributeSet` →
  `HealthPoint`), SPINE 2b (`PathStepToBreadcrumbs` splits a Locate-in-GWorld `PlayerArray[0]` hop into
  container + element), and DEDUP 2c (`DedupeConsecutiveBreadcrumbs` collapses a redundant consecutive
  container crumb in `ExportCeFieldXmlAsync` + `CleanBreadcrumbs`) all **LIVE-VERIFIED on Elliot AND the
  deeply-nested Gundam SEED chain** (nested + Collapse-chain). Unit-tested
  (`...ObjectArray_WithResolvedElement_DrillsElementGroup`, `...PathThroughObjectArrayElement_EmitsElementDerefNode`,
  `DedupeConsecutiveBreadcrumbs_*`, `..._DeepDistinctChain_Unchanged`). **(b) DONE + LIVE-VERIFIED
  (builds 1380-1388) — Back-nav onto a path-synthetic container crumb now re-hydrates the array element view.** The
  crumb's `ContainerField` is null (the `GWorldPathStep` carries no `ArrayDataAddr`/`ArrayCount`/element
  list), so Back-nav fell through to a parent re-walk and rendered the PARENT object grid (a silent
  mis-render — NOT a literal duplicate; the 2c dedup already covers the export-time crumb). "Give it a
  `ContainerField`" is infeasible (path step lacks the data) → `TryRepopulateSyntheticContainerAsync`
  LAZILY re-walks the parent + matches the field by name+offset + `RepopulateContainerView`, wired into
  all 4 re-display sites (NavigateToBreadcrumb, GoBack normal + pre-bookmark restore, LoadBookmark) +
  `RefreshAsync`'s container gate broadened. 7 new tests; C# 1648/0, AOT 46.5 MB. **(a) DONE +
  LIVE-VERIFIED (builds 1389-1390) — Map/Set (and interface-array) element hops in a GWorld-path spine
  now split into container + element crumbs.** The DLL `emit()` lambda was widened 6→8 args to thread `elemStride`
  (Map `pairStride` / Set `elemStride` / interface-array 16) + `elemValueOffset` (Map value's within-pair
  offset; 0 for set/key/interface) through `GraphEdge`/`GraphPathStep` → Fern `elem_stride`/`elem_value_offset`
  → C# `GWorldPathStep` → `PathStepToBreadcrumbs` (element crumb offset = `ElementIndex*stride + valueOffset`;
  container crumb strips the `.Key`/`.Value` suffix so Back-nav re-hydration matches). All emit callers
  updated (`GetRelatedObjects`/`AppendOwnedSubObjectLeaves`/test mock); object/class arrays keep the
  hardcoded-8 path. 6 new tests (5 C# + 1 dll round-trip); C++ 697/0, C# 1653/0, AOT 46.5 MB. Adversarial
  review confirmed Map/Set/Set offsets correct + reachable; accepted nits: struct-nested dotted base name
  doesn't re-hydrate (pre-existing, affects arrays too, CE math still correct) + int32 element-offset
  arithmetic (theoretical, `FieldOffset` is int by design).
- **Genau RIP decode: `Macht::IsRipRelativeModRM` (mod=00 half restored at 3 of 5 sites)**
  (build 2544+; DLL only). Three hand-rolled decode loops tested `(b & 0x07) == 0x05` and
  omitted the `mod == 00` half, so `mov rcx,[rbp-8]` / `lea rax,[rbp+0x20]` / `mov rax,rbp`
  were decoded as RIP-relative and the int32 read at `instr+3` was a disp8 plus the next
  instruction's bytes. All five sites now share one named predicate.
  **What offline already settled — do not spend live time on it:** the predicate itself
  (13 assertions incl. an exhaustive "exactly 8 of 256 ModR/M bytes qualify", verified to
  FAIL — 6 reds — when reverted to the r/m-only form). Also settled: this is **NOT** a
  wrong-answer bug at `ScanFunctionBodyForRipRef`, whose every caller is a GNames path gated
  by `ValidateGNamesAny` (it must decode the literal string `"None"` through a two-level
  pointer chain). Treat it as a correctness + scan-cost cleanup, not a fix.
  **What ONLY a real game can prove, and `sweep.sh` CANNOT:** `scan_patterns.java:137` skips
  every `Symbol*`/`CallFollow` signature (`GROUND-TRUTH.md` says so), and the two data scans
  are runtime-only and absent from the pattern harness — **a clean sweep diff here would mean
  "not measured", not "no regression".** The only evidence is the DLL's own scan log, same
  game, before vs after: the candidate/probe counts should go DOWN while **every resolved
  GObjects / GNames / GWorld address stays byte-identical**. The second half is the real
  acceptance criterion; a changed address is a regression, a lower count is the win.
  Passive — needs no special in-game action, just one injection each side.
  ✅ **VERIFIED 2026-08-19 `[GENAURIP-AB-2026-08-19]` — BOTH halves, on a non-game host.**
  Rig: `tools/verify/genau_rip_ab.py run notepad++`. Both DLLs were built **in the same
  session, from the same tree, by the same toolset**, differing ONLY in the two-line
  predicate — *not* "dist vs a checkout of build 2544", which would differ in ~700 builds of
  unrelated ways. Hint entry deleted before each side (a warm cache changes how many patterns
  are attempted — the quantity being compared).
  - **The win — candidate count went DOWN, deterministically.**
    `DataScanGObjectsCandidates: Found ` **4085** ` static pointers` (pre-fix) →
    **4083** (post-fix). **Reproduced exactly on 4 independent runs**, so −2 is signal, not
    variance. ⚠ The neighbouring `(N validation failures were suppressed)` counter is NOT
    stable run-to-run (3621/2777 across runs — it depends on live heap contents); its delta
    was a consistent −1, but **do not quote the absolute number as evidence**.
  - **The acceptance criterion — the resolved address did not move.** `GWorld` resolved to
    `0x7FF7480A03C8 (aob)` on **every one of the 4 runs, both sides**. Directly comparable
    because the host's module range was byte-identical across runs
    (`code=[0x7FF747B31000-0x7FF747F7837C]`), i.e. it was not rebased — checked rather than
    assumed, since the row warns that ASLR normally makes raw addresses meaningless.
  - ⚠ **THE HOST IS THE WHOLE EXPERIMENT, and the first choice was wrong.** All five call
    sites are RECOVERY paths (`DataScanGObjectsCandidates`, `FindGObjectsStaticStruct`,
    `ResolveSymbolExport`, `FindGNamesByStringRef` ×2), so **on a healthy game the AOB wins
    immediately and not one of them runs** — a game yields two identical logs for the worst
    possible reason. A `python.exe` sleeper fails every AOB and so drives all five, but
    **measured a flat null**: python.exe is a launcher stub whose main module has a code
    section of **0xE4C = 3,660 bytes** (the real code is in `python312.dll`, not the main
    module), so both sides returned an identical "Found 17 static pointers". That null is
    **manufactured by the host and is indistinguishable in the log from "the fix changed
    nothing"**. Notepad++ (~8.5 MB) is ~2,300× the code and is what produced the signal.
  - **Still not covered**: `GObjects`/`GNames` do not resolve on a non-UE host, so
    "addresses unchanged" is demonstrated for **GWorld only**. A UE title whose GObjects or
    GNames AOB *fails* (so recovery actually runs) would close that; DumperTest cannot,
    because all three resolve by AOB on the first pattern.

- **Audit #3 DLL fixes — M1–M5 + the DLL/Solide LOWs** ([audit-2026-07-14-findings.md](audit-2026-07-14-findings.md)).
  Shipped on `dev` (`408fd2d`, `7f3898f`, `3362636`); this section is their SINGLE owner — the audit
  doc and the Audit-#3 block above point here rather than each asserting a status of their own.
  Every one is a **race or a lifecycle-ordering fix**, which is precisely the class a unit test
  cannot reach: the bug needs a real game thread, a real disconnect, and real timing.
  - **M1 / M2 / M3 — Schlacht restore-set** (disable↔Tick race repopulating `hiddenActors`; disable
    while the game thread is stalled discarding the restore set; no un-hide on disconnect/shutdown).
    Acceptance: enable See-Through, then (a) toggle off during motion, (b) toggle off while the game
    is paused/stalled, (c) yank the UI connection and (d) close the game — in **all four** every
    hidden actor must become visible again. A single actor left invisible is the failure, and it is
    only visible on screen. ⬜
  - **M4 — Tot latch zombifying a Solide hold** during the disconnect window. Acceptance: start a
    force-field hold, disconnect the UI mid-hold, reconnect → `get_forced_fields` must still list the
    hold AND the value must still be held (a zombie job lists but stops re-asserting, so checking the
    list alone is not enough — read the value in CE). ⬜
  - **M5 — `UE5_Shutdown` worker-join ordering** (joined hold workers before stopping the pipe, so a
    mutator arriving in the window respawned an unjoined worker). Acceptance: with a hold active,
    close the game while the UI is still connected → no hang, no crash on exit. Evidence is the
    absence of a hang; there is no positive log line. ⬜
  - **DLL LOWs L1 / L5 / L8 / L10 / L12** (Solitar worker start/stop under `s_workerMutex`;
    Welford gap underflow on out-of-order PE timestamps; Grausam `GetWindowTextW` under `g_mutex`
    hanging the pipe thread; Grausam post-enable windows + shutdown teardown; Fern `str_params`
    malloc leak on a mid-loop JSON `type_error`). L8 and L12 are the ones with a user-visible
    symptom (pipe stall / leak under repeated failed invokes). ⬜
  - **Solide LOWs L2 / L3 / L4** (weak-ptr refusal no longer silent; substring class + fuzzy field
    match tightened; per-instance restore bases instead of one representative). L4's prune guard was
    touched again in build 2531 — see the Solide pool-truncation entry below, verify them together. ⬜

- ✅ **Value Search `TSet<T>` / `TMap<K,V>` scan (key: V1a)** — **VERIFIED 2026-08-05 (DumperTest,
  build 2650), ⬜ since build 927.** Scanning `4242` returned `DumperTestActor.Set_Int[1]`
  (IntProperty, Reflected, offset `0x358`) on both the live actor and the CDO; scanning `222`
  returned `DumperTestActor.Map_NameToInt.Value[1]` at `0x3A8`. Both render with the element index,
  which is what the row format promised. The sparse-walk geometry hands back the slots we expect.
  *Not yet exercised: container reallocation between scans (the degrade-don't-lie case).*
- ✅ **Value Search `TOptional<T>` scan (key: V1c)** — **VERIFIED 2026-08-05, ⬜ since build 942.**
  `24680` returned `DumperTestActor.Opt_Int_Set` (IntProperty, `0x468`), and — the criterion that
  actually matters because it is negative — **a scan for `0` did NOT surface `Opt_Int_Unset`**, so
  the `bIsSet` gate holds and an unset optional is not being read as a zero.
- ✅ **Value Search `NumericAll` (byte families included)** — **VERIFIED 2026-08-05, ⬜ since build
  796.** `-5` (Int8Property) and `255` (ByteProperty) both returned results with NumericAll
  selected. *The remaining half is a UX judgement, not a defect: whether the result volume for a
  1-byte value is usable. The panel's own orange warning says it will flood, and this sample cannot
  settle "usable" — that needs a real game's object count.*
- **Value Search `TSet<T>` / `TMap<K,V>` scan — original instructions** (build 927). Scan a known value held
  in a `TSet<int>` / `TMap<K,int>` UPROPERTY → rows must render as `Set[idx]` / `Map.Key[idx]` /
  `Map.Value[idx]`, and a Next Scan must prune. The sparse-walk geometry
  (`Ubel::GetSetElementStride` / `GetMapPairLayout`) is shared with the container-aware Address
  Finder and unit-tested; what is NOT provable offline is that live sets/maps hand back the slots
  we expect. Specifically watch a **container reallocation between scans** — element addresses are
  raw, so refine degrades exactly like `TArray` (the SEH-safe read drops the candidate); confirm it
  degrades rather than reporting a wrong hit. ⬜ unverified.
- **Value Search `NumericAll` (byte families included) (key: NumericAll)** (build 796-797). Select
  NumericAll and scan a value that genuinely lives in an `Int8Property` / `ByteProperty` → confirm
  the byte field is found, and that the orange result-volume warning
  (`ValueSearchViewModel.DataTypeWarning`) appears. `BuildNumericTargets`' range gating is
  unit-tested (`300` → no Int8/UInt8; `-5` → Int8 yes / UInt8 no); the live question is whether the
  result volume for a small value (0/1/255) is *usable* or drowns the panel — that is a UX
  judgement no test can make. ⬜ unverified.
- **Value Search `TOptional<T>` scan (key: V1c)** (build 942). Scan a known value held in a
  `TOptional<int/float/FString>` UPROPERTY → confirm the row appears under the optional's
  field name and a Next Scan prunes; confirm an **unset** optional doesn't surface on a
  scan for `0` (the `bIsSet` gate). Layout helper is unit-tested; the field walk needs a
  live game with optional UPROPERTYs.
- **Property freeze (Route B)** on a respawning-NPC game (build 719). Watch: tick FPS
  impact (50ms × N instances), rescan cadence at respawn, vtable-liveness guard on level
  transition, AOBMaker gating UX, multi-script coexistence. First candidate: Geri (UE
  4.27).
- **Build-648 ProcessEvent fix** re-verify on ES2 (UE 5.5) + Geri (UE 4.27): look for
  `GameThreadDispatch: validation OK — hook fired N times`; previously-`-5`-timing-out
  instance invokes should now succeed. Lower-priority extras: a UE 4.18-4.24 game (smaller
  vtable / lower slot) + a heavily-modified publisher fork.
- **Static-native PE fast path** (build 636) latency vs game-thread dispatch on an active
  session; confirm stateful UFunctions still route through dispatch (don't fall into the
  fast path by accident).
- **FPROPERTY_FLAGS offset fix** (build 642): sweep the 12+ tested games' Class Structure
  Return columns + confirm baked PARAMS no longer include ReturnValue as an input.
- **Verify Return Value diagnostic** (build 637/644): pointer-return shows `0x` prefix;
  FString-return shows the "see After: dump above" hint.
- **`walk_functions_batch` follow-up** — Effort: **S**. Sister to `walk_class_batch`;
  DumpAll still does `WalkFunctions` single-call per class. Same byte-equivalence safety
  net. **Skip unless profiling shows it as the new bottleneck.**

-----

## See-through (Schlacht) — "pass light/shadow through too?" — EVALUATED (mostly WON'T-DO)

**Question:** can See-through also let the occluder's **light/shadow** effects pass through, not just
its mesh? **Verdict: split by lighting type — dynamic is already handled; baked is infeasible from an
injected DLL.** (36-agent adversarial verify against UE engine source, 2026-07-09.)

- **Dynamic / real-time light (movable lights, Lumen GI+reflections, DF shadows/DFAO, HW ray tracing)
  — ALREADY passes through, no code change.** `AActor::SetActorHiddenInGame(true)` sets `bHidden` →
  `UPrimitiveComponent::ShouldRender()` false → `ShouldComponentAddToScene()` false (default flags) →
  the primitive is dropped from the render scene entirely, so it is absent from the **shadow-depth
  pass** too — the dynamic shadow vanishes with the mesh (community's `bCastHiddenShadow=true` recipe
  exists only because default hiding drops the shadow). UE5 does the same for the mesh distance-field /
  Lumen scene: `PrimitiveNeedsDistanceFieldSceneData()` has `IsDrawnInGame()` as a required OR-term,
  and `FScene::UpdatePrimitivesIsDrawn_RenderThread()` calls `DistanceFieldSceneData.RemovePrimitive()`
  + `LumenRemovePrimitive()` on the hide branch by default; the HW-RT gather also skips `!bDrawInGame`
  primitives. So on a Lumen/movable-light game, hiding the wall already removes its shadow, GI
  occlusion, and RT contribution.

- **Exception (fixable): a game that sets `bCastHiddenShadow=true` (or `bAffectIndirectLightingWhileHidden`)
  on world meshes** keeps the shadow/GI after hide (that flag's whole purpose is cast-while-hidden).
  **Only actionable enhancement:** alongside `SetActorHiddenInGame`, also invoke
  `UPrimitiveComponent::SetCastShadow(false)` / `SetCastHiddenShadow(false)` (and
  `SetAffectDistanceFieldLighting(false)` for Lumen/DF) on each of the hit actor's primitive components,
  restoring on un-hide. All are `BlueprintCallable` UFUNCTIONs reachable via the existing
  `UE5_CallProcessEventEx` ProcessEvent path; component enumeration already exists (`GetRelatedObjects`).
  Effort: **S** · Risk: low. **Do only if a real game shows a lingering shadow after See-through hides
  the mesh** (LIVE-VERIFY first, per the module's ethos). Won't help baked lighting.

- **Baked / static light (Static or Stationary mobility — the common case for UE4 & perf-sensitive UE5
  world geometry: locked-60fps, mobile, VR) — INFEASIBLE, WON'T-DO.** The wall's shadow is baked by
  Lightmass into the **receiving** surface's (floor / neighbouring wall) lightmap texture (and per-object
  distance-field shadow maps for Stationary), stored per-mesh in the `MapBuildDataRegistry` — it lives
  on the receiver, not the caster. `SetActorHiddenInGame` only toggles the caster's own primitive
  visibility; it cannot touch another mesh's lightmap, so a **"ghost shadow"** stays exactly where the
  wall was. Removing a baked shadow needs an editor-time **Build Lighting** (Lightmass is editor-only,
  stripped from shipping/cooked builds); no runtime API recomputes lightmaps. The only external "fix"
  is forcing the whole level to unlit/dynamic (`r.AllowStaticLighting 0` + restart — global, breaks all
  level lighting), which isn't worth it. This is why many games show a residual shadow after See-through
  hides the mesh — nothing we can do about it from a DLL.

*Parent: Schlacht Stage 1 (dev-log 2026-07-08 build ~1989; project-seethrough-occluders-schlacht).*

-----

## Output-monitor pin — "the game has no monitor-select UI" — EVALUATED (2026-07-23), NOT BUILT

**Question:** on a dual-monitor setup, when a game exposes no output-display setting, can we fix it
with **UE functionality**? **Verdict: the UE reflection layer has no concept of an output monitor —
the monitor-selecting step is Win32/DXGI. UE reflection only contributes the windowed↔fullscreen
toggle and the persistence.** And the hard part is not the initial move, it is that the game
**drifts back** — so the deliverable is a *pin*, not a one-shot move.

**What UE reflection does and does not give us**

- Stock UE has **no** monitor-index `UPROPERTY`, no BlueprintCallable monitor selector, and no cvar.
  (The `-monitor=N` recipe circulating since Froyok's 2018 post is an *engine source modification*,
  not stock behaviour.) `r.setres WxH[w|f|wf]` changes mode/resolution, never the screen.
- **Invokable today** (BlueprintCallable ⇒ in the reflection function table ⇒ reachable via
  `invoke_function`): `UGameUserSettings::SetFullscreenMode(int32)` (`EWindowMode` 0=Fullscreen /
  1=WindowedFullscreen / 2=Windowed), `SetScreenResolution`, `ApplyResolutionSettings(bool)`,
  `ApplySettings(bool)`, `SaveSettings()`.
- **NOT invokable:** `SetWindowPosition()` / `GetWindowPosition()` are **not** BlueprintCallable, so
  they are absent from the reflection function table. The backing `WindowPosX` / `WindowPosY` *are*
  config properties (default `-1` = centre) ⇒ writable via Property Search / Live Walker / Solide
  Force. That yields a no-code path (**write WindowPosX/Y → invoke `SaveSettings()` → restart**) but
  it needs a restart and collides with the documented UE 4.16+ "re-centres itself after the startup
  map loads" override.
- Why the move-then-fullscreen sequence works at all: UE `WindowedFullscreen` resolves via
  `MonitorFromWindow`, and DXGI exclusive fullscreen picks "the output containing most of the client
  area" when `pTarget` is NULL — **both follow the window**. So `SetFullscreenMode(2) → move the
  HWND → SetFullscreenMode(1)` lands on the target screen.

**Drift is event-driven, not continuous** — regain focus / alt-tab / `WM_DISPLAYCHANGE` /
swapchain reset. Unity's issue tracker documents exactly this symptom ("exclusive fullscreen always
opens on monitor 1 after regaining focus even when monitor 2 is set as primary"). So a pin does
**not** need a high-frequency poll.

**Three pin mechanisms, lightest first**

- **(a) Rewrite `WM_WINDOWPOSCHANGING` — the good one.** `Grausam.cpp` `SubclassProc` (~line 144)
  already subclasses the game WndProc and `Grausam.cpp` `FindGameWindow()` (~line 61) already resolves the HWND
  (`EnumWindows` + same PID + largest visible). Patching `WINDOWPOS.x/y` **before the move happens**
  is flicker-free and the game never notices. Any "detect it moved, move it back" scheme flickers and
  fights the game's own repositioning — which is the user-visible "it just snaps back" symptom.
- **(b) Low-frequency watchdog — the backstop.** ~4-5 Hz worker; if
  `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) != target`, `SetWindowPos`. Structurally
  **identical to the Solide / Hemmung / Laufen write-on-drift re-assert workers** — copy the shape.
  Covers paths (a) can't see (game switches mode via the swapchain, not via `SetWindowPos`).
- **(c) Hook `IDXGISwapChain::SetFullscreenState` — the real fix for exclusive fullscreen.** MSDN is
  explicit: `pTarget` **is** the output selector; NULL means "DXGI guesses from window placement",
  and on alt-enter **NULL is the only option DXGI has**. So for a true-exclusive-fullscreen game
  (a)+(b) are palliative — the game's next `SetFullscreenState(TRUE, NULL)` re-guesses. Substituting
  the user's chosen `IDXGIOutput*` is the cure. MinHook is already vendored (Stark/Grausam), but
  `Lugner_Dxgi.cpp` is a **pure export forwarder (asm thunks), not a
  swapchain vtable hook** — this is entirely new work, and per-API (D3D11 / D3D12 / Vulkan separately).

**This feature is not UE-bound (scope decision needed).** `Heiter.cpp` (`ProxyStart`, ~lines 57-86)
shows **proxy mode starts the pipe server immediately with no AOB scan**, and all three mechanisms
above are pure Win32/DXGI with zero UE reflection ⇒ injected via the `dxgi.dll` proxy this would work
in a **Unity** game too. Blocker: every UI panel currently assumes UE init succeeded, so a non-UE
process shows a wall of errors. Either accept "only this one card works, everything else is red" or
build a minimal non-UE mode — decide before advertising it as a capability.

**Try the no-code per-engine fixes first** (this class of game is rare; don't pre-build):

- **Unity:** `HKCU\Software\<Company>\<Product>` → **`UnitySelectMonitor`** (0-based), and the
  documented **`-adapter N`** launch arg. ⚠ *Engine-specific*: in **UE**, `-adapter` selects the **GPU
  adapter** and does nothing for monitor choice — the two engines are not interchangeable here, and
  `-adapter` is widely mis-recommended for UE.
- **UE:** `-windowed -WinX= -WinY= -ResX= -ResY=` (Steam launch options) or the same keys in
  `%LOCALAPPDATA%\<Game>\Saved\Config\Windows\GameUserSettings.ini` — subject to the 4.16+ recentre bug.
- **Engine-agnostic:** disable the unwanted display before launch (MultiMonitorTool / `DisplaySwitch`),
  re-enable after. 100% effective against "always picks output 0" games.
- **Why "set it as primary" fails:** enumeration order comes from the adapter's output connectors
  (`EnumDisplayMonitors` / DXGI output order); Windows exposes **no** way to reorder it, and the
  primary flag doesn't change it. That is why physically re-ordering the DP cables is the only clean
  non-tool fix.

**Prior art — check before building.** Special K already does this (Window Management X/Y offset,
retained across launches). For one or two games it is the faster answer. Our differentiators: the
zero-flicker (a) that Special K lacks, integration with the existing UI, and the (c) DXGI path —
Special K's own multi-display borderless-fullscreen limitation is still open (SpecialKO/SpecialK#87).

| Phase | Scope | Effort | Risk |
|---|---|---|---|
| **P1** | (a) `WM_WINDOWPOSCHANGING` + (b) watchdog + `EnumDisplayMonitors` listing + 2 pipe cmds (`list_monitors` / `set_game_monitor`) + one Teleport card. Borderless/windowed only | **M** | low |
| **P2** | (c) `SetFullscreenState` hook — covers exclusive fullscreen | **M-L** | med — swapchain vtable hooks read as overlay behaviour to some anti-cheat; per-graphics-API work |
| **P3** | Minimal non-UE-mode UI boundary (unlocks Unity/other engines) | **M** | med |

**Naming:** take **Böse** (barrier/guard) from the [naming-convention.md](naming-convention.md) roster —
the module's job is *holding the window in place*, a barrier, not a transfer (so not `Zart`, and
teleport semantics stay with Wirbel).

**Recommendation:** spend ten minutes on `UnitySelectMonitor` / `-adapter N` against the actual
offending game first. If that sticks, park this entirely — P1+P2 is M-L of work for a handful of
games. If it doesn't stick *and* more than one such game is on hand, P1 alone is cheap: it reuses
Grausam's subclass and Solide's re-assert shape, leaving only monitor enumeration and the
`WM_WINDOWPOSCHANGING` branch as genuinely new code.

*Parent: Grausam foreground-lock infrastructure (dev-log builds ~1950-1984;
project-foreground-lock-grausam). Sibling evaluation of the Schlacht see-through and Hemmung
time-control evals above.*

-----

## 4th proxy DLL — winmm.dll — ✅ SHIPPED build 2317, **archived**

Shipped as a free *slot* (dxgi/version are taken by ReShade and ASI loaders), not for coverage.
The census and rationale moved to
[archive/todo-closed-2026-08-build-2715.md](archive/todo-closed-2026-08-build-2715.md).

-----

## UE performance counters in the UI — EVALUATED (2026-07-23), tiered

**Verdict: the literal ask — surfacing UE's own `stat` counters — is impossible from an injected
DLL. But the two cheapest tiers are worth more than the literal ask, because they measure the thing
[multipipe-eval.md](multipipe-eval.md) already blames for UI lag and currently has zero telemetry for.**

- **Tier 0 — WON'T DO: UE's `stat` system.** Shipping builds compile with `STATS=0` (even the *Test*
  configuration defines `STATS 0` by default), and the console is **removed from the binary** in
  Shipping, not hidden. Re-enabling needs `FORCE_USE_STATS` and an engine recompile. Unreachable from
  an injected DLL — record as WON'T-DO so it isn't re-litigated.

- **✅ Tier 1 — DONE (build 2308).** New `Sense` module + `get_diagnostics` pipe command + a
  System-tab card. Records per-command dispatch cost (count / total / max / last) at Fern's existing
  `inFlight` chokepoint — which is exactly the head-of-line window — and reports `busy_percent`, the
  fraction of wall-clock a dispatcher was occupied. **That is the number Phase 1 was missing.**
  Also carries game-thread health from Stark and the GObjects count. *Original note kept below for
  the rationale.*

- **Tier 1 — our own health. Zero new machinery, highest value.**
  [multipipe-eval.md](multipipe-eval.md) already names DLL-side **serial-dispatch head-of-line
  blocking** as the root cause of UI lag and game-thread CPU starvation as the CE-mailbox risk — yet
  neither is measured, so Phase 1 would be decided blind. Free to collect: per-command Fern handling
  time + queue depth; Stark invoke queue depth / timeout count (`invoke_timeout_ms` is already
  reported over the pipe); per-worker tick count + write-on-drift hit rate for Solide / Hemmung /
  Laufen / Solitar / Schlacht; Aura `NumElements` over time (GC/leak indicator). **Linie already
  computes frame-cadence statistics** (per-UFunction fire counts + Welford mean/cv) — it just isn't
  presented as performance. Effort **S-M** · Risk none.

- **✅ Tier 2 — DONE (build 2308).** Working set / private bytes / CPU% / thread + handle counts,
  in the same `get_diagnostics` payload. On demand only (thread count walks a system-wide snapshot).
  CPU% is `-1` until a second sample exists to difference against, and the UI renders that as an em
  dash — "0%" would read as *idle*, which is a different and wrong claim.

- **Tier 3 — real FPS / frame time: hook `IDXGISwapChain::Present`.** The only engine-version-
  independent, accurate source (true frametime, 1% low, pacing, present mode). **Shares its entire
  hook infrastructure with P2 of the output-monitor-pin evaluation above** — these two must be
  decided together and funded once, not twice. Effort **M-L** (joint) · Risk med (overlay-shaped
  behaviour; per-graphics-API work).

- **Tier 3.5 — `GAverageFPS` / `GAverageMS` via AOB.** These are plain engine globals
  (`GAverageFPS = 1000/GAverageMS`), **not** gated by `STATS`, so they survive Shipping, and Himmel's
  128-pattern infrastructure could carry a signature. But it is a per-version/per-compiler signature
  to maintain and yields the engine's *smoothed average*, strictly worse than the Present hook. Keep
  only as the fallback if we decide never to hook DXGI.

- **Tier 4 — reflected time values.** `AWorldSettings::TimeDilation` (Hemmung already reads it) and
  `UWorld::TimeSeconds` / `RealTimeSeconds` / `DeltaTimeSeconds` (not `UPROPERTY` — needs DynOff
  probing). Caveat: `DeltaTimeSeconds` is the **game-thread** delta only (no render/GPU) and is
  polluted by time dilation — usable as context, **not** as an FPS readout.

**Status: Tier 1 + Tier 2 SHIPPED (build 2308). Tier 3 still deferred to the monitor-pin P2
DXGI-hook decision; Tier 0 remains WON'T-DO.**

**Follow-on deliberately NOT built: per-worker tick counters** for Solide / Hemmung / Laufen /
Solitar / Schlacht (tick count + write-on-drift hit rate). That is five modules touched for a number
that does not bear on the dispatch question — and the dispatch question is the one that blocked a
decision. Worth doing if a re-assert worker is ever suspected of burning game-thread time. Effort
**S-M** · Risk low.

**✅ Automatic PERF records — DONE (build 2320).** `Services/DiagnosticsProbe.cs` brackets **Copy CE
XML / Copy CE Field / Value Scan (First & Next) / Snapshot capture** with two `get_diagnostics`
snapshots and logs the delta as a `PERF` line in the `view` log. Better than the manual measurement
session it replaces: a deliberate test only covers the scenario somebody thought of, and only if they
remembered to reset first — this accumulates evidence from real use.

**✅ ANSWERED (2026-07-23, build 2324) — and the answer is "don't build Phase 1".** Measured on
Elliot (UE 5.4) + SEED (UE 4.27), 24,178 dispatches across 5 real Copy CE XML / Copy CE Field runs.
Full table and reasoning in [multipipe-eval.md](multipipe-eval.md) §10.

- **Dispatcher busy 29.8%** — idle ~70% of wall-clock, and the ratio holds (22-31%) across
  operations from 2.6 ms to 5.4 s. Non-blocking dispatch can only recover a slice of the busy 30%,
  and only if something were queued behind it — in a single-user export nothing is.
- **Worst SINGLE dispatch: 14.3 ms** out of 24,178. Phase 1's premise is a long-blocking command
  holding the read loop; no such command exists here.
- Phase 1 was already **shipped and reverted once** (build 1840) and a correct version needs
  overlapped/async pipe I/O. Not a trade worth making for this.

**The real lever is CALL COUNT.** `walk_instance` is 100% of dispatcher cost in every row, and one
Copy CE XML issued **20,357** of them: **0.088 ms in the DLL vs 0.208 ms of round-trip overhead —
2.4x the work is overhead.** Batching it at the established ~200/call chunk (as
`search_properties_batch` / `walk_class_batch` already do) would collapse 24,178 round-trips to
~121. **✅ SHIPPED build 2329 — `walk_instance_batch`.** The measurement said dll 27-30% / **ipc 59-73%** /
ui 0-10%, i.e. per-call round-trip overhead roughly 2x the actual walk, so the calls were collapsed
(chunk ~200). Built to the `walk_class_batch` precedent with all three safety layers: a DLL handler
that is a trivial loop over the single-call path, a shared serialiser/deserialiser pair, and an
equivalence test comparing both paths field-for-field. The CE export now walks breadth-first per
level. A failed batch — or a short/long reply, which would otherwise mis-pair results with addresses
— replays the chunk as single calls.

**✅ DONE + MEASURED (build 2335): 1.71x faster.** Copy CE XML on SEED went **5,893 -> 3,437 ms**,
dispatches **22,522 -> 1,355**, IPC **3,532 -> 1,278 ms**. `top:` names `walk_instance_batch`.
(Build 2329 had batched the wrong loop - the calls come from the STRUCT tree, not the
object-pointer drilldown; fixed with a breadth-first `PrefetchStructTreeAsync` feeding the
unchanged depth-first emit, since that emit's order IS the exported field order.)

**The 2.4-3.5x projection was wrong, and usefully so - IPC is not purely per-round-trip.** At the
old 0.157 ms/call, 1,355 calls should have cost ~212 ms of IPC; they cost **1,278 ms**. So of the
original 3,532 ms, ~2,253 ms was fixed per-round-trip cost (removed) and **~1,066 ms is
payload-proportional** (untouchable by batching - the same bytes still cross). `ui` rose 610 -> 653
ms for the same reason. Full table in [multipipe-eval.md](multipipe-eval.md) section 10.5.

**Next lever, if anyone wants more: BYTES, not messages.** Remaining 3,437 ms = dll 1,506 (real
work) + ipc 1,278 (mostly payload) + ui 653 (parse). Trimming fields the CE export never reads would
hit the payload-proportional IPC *and* the parse cost together. Note also that raising the batch
chunk would achieve nothing: average batch size is ~16.6 (fan-out-limited), not near the 200 cap.

**✅ MEASURED (build 2339) — `scripts/analysis/walk_payload_audit.py`.** Byte-accounted a real
Copy CE XML on SEED against a key-by-key map of what the exporters read (full table in
[multipipe-eval.md](multipipe-eval.md) section 10.6):

- Per-field keys (52.7% of the sample): **60.9% used / 18.6% CSX-only / 16.7% unused.**
- Inline array elements (20.3%): **43.9% used / 44.6% unused** — `elem.h` (element raw hex) alone
  is 9.0% of the whole payload and no exporter reads it.
- The per-instance header (`name` / `class` / `outer_*` / `props_size` / even `addr`) is **99%
  dead** — the export touches `result.Fields` and nothing else.
- Verdict: **~24% of the payload-scaling bytes are droppable outright, ~38% if CSX opts out of
  `hex` too.** Biggest single items: `elem.h`, `field.hex` (CSX-only), `field.value`,
  `field.array_inner_addr`.

**✅ SHIPPED (build 2351) — `lean: true`.** `walk_instance` / `walk_instance_batch` take a `lean`
flag that omits exactly those keys (drop list in [pipe-protocol.md](pipe-protocol.md); design notes
in [multipipe-eval.md](multipipe-eval.md) section 10.7). Subtractive only, so an older DLL that
ignores it stays correct. Wired to the CE XML export path ONLY — CSX shares the same
`ResolveDrilldownAsync` and genuinely reads `hex` / `bool_mask` / `bool_byte_offset`, so the default
stays full-fat. `WalkInstanceLeanTests` proves lean and full payloads produce **byte-identical XML**
(mutation-checked: blanking a key the exporter does read fails it).

**✅ IN-GAME VERIFIED (build 2353, SEED).** Same object exported before (DLL 2338) and after
(DLL 2353): **payload 1,982,875 -> 1,168,944 bytes over the same 134 batch responses = -41.0%**,
matching section 10.6's prediction. The XML is unchanged — 149,621 lines / 14,326 leaves both
sides, 15 differing lines and every one a per-session value (root address + FName ComparisonIndex,
name half identical). DLL serialise time -20% (146.7 -> 116-119 ms), consistent across both runs.

**Still open — the wall-clock.** On that small export `ipc` did NOT move (207 -> 213-216 ms) even
though the bytes nearly halved: at ~15 KB/response over 134 calls, IPC is dominated by fixed
per-call cost.

A **bigger lean run exists** (2026-07-23 22:09, SEED `BP_LifeGameInstance_C`, depth 4, 13,845 structs
/ 54 pointers): wall **2,086.6 ms**, 302 dispatches, split **dll 832.4 (39.9%) / ipc 704.3 / ui
549.9 ms**, and **10.16 MB of lean payload** across 241 batch + 65 single responses (~39 KB per batch
response — 4x the small run). It has **no before-side**, so it measures where the time sits now
(DLL-bound) rather than what lean saved. Two cheap ways to close it:
(a) re-run the same export against the pre-lean DLL (build 2338) for a true A/B; or
(b) export the **same object as CSX**, which goes through the same `ResolveDrilldownAsync` with
`lean:false` — caveat: CSX additionally drills object-arrays / DataTable rows, so its walk set is a
SUPERSET and the comparison is an upper bound, not an equality.
While at it, re-run the payload audit with `UE5DUMP_PIPE_LOG_FULL=1` for an untruncated sample — the
1024-char body-log cap makes the whole-payload split read a flattering 39%.

*Parent: multipipe-eval.md Phase 1 (non-blocking dispatch) needs Tier 1 to be decidable; Linie
(dev-log build 2156) already holds the cadence half.*

-----

## Speculative — pick if the active plan finishes ahead of schedule

Not yet committed to:

- **Invoke history / favorites panel** — auto-record (target, args, result) per
  invocation; one-click re-fire.
- **Dry-run-first invoke** — for never-called functions, invoke with zero/sentinel params
  first to detect a crash before committing real args.
- **CE table builder** — bundle selected pointer entries + AA scripts into a single `.ct`,
  auto-grouped by category (broader than the build-760 Interesting Funcs/Props batch).
- **Global hotkey binding** for shortlisted functions ("give 1000 gold" on Ctrl+G).
- **Property freeze — Route A (docs only)** — reuse CE XML/CSX export to land a pointer
  chain, user manually ticks Freeze in CE. Works today, no code; tradeoff is the chain
  binds to one resolved instance (breaks on respawn). Keep for one-shot static-singleton
  freezes so users don't have to wait for Route B.
