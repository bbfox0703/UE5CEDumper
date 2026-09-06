# Todo

Open work only. **Read this when deciding what to do next.**

> 🤝 **Coming back? Read [handover-2026-08-22.md](handover-2026-08-22.md) first.** It is the single
> entry point: current state, the grants and how to launch each fixture, the traps, and a ranked
> "start here". ⚠ **The build number and the gate count both drifted in this very paragraph**
> (it said `3315` and *twelve*; on 2026-08-24 they are **3350** and **13**). Do not read either from
> here — `cat dist/build_number.txt`, and take the gate count from `py tools/check_all.py`'s own
> `N gate(s) run` line. `dist/` is republished AOT-trimmed.
> ⚠ Its two predecessors are **archived**: [archive/handover-2026-08-20.md](archive/handover-2026-08-20.md)
> and [archive/handover-2026-08-19.md](archive/handover-2026-08-19.md). Everything in them that is
> still operationally true was carried forward; go back to them only for the *history* of the
> 2026-08-19/20 verification programme.

> ## ⛔ BEFORE YOU PLAN OFF ANY HEADING IN THIS FILE — READ THIS 2026-08-24 RECONCILIATION
>
> ⚠⚠ **A heading in this file is NOT evidence.** Closures are recorded under their own
> `✅ … [TAG-2026-08-NN]` block, often thousands of lines from the row they close, and the original
> `⬜`/`🟡` heading is **not** updated as a matter of course. On 2026-08-24 that misdirected five
> planning attempts in one session — the worst being a heading that read *"Step 2 is still open, on
> a NEW blocker"* over a step closed the next day, twice. See working-lessons **§1.ab**.
>
> A 7-agent sweep re-read all **40** open-marked sections against the whole file. **24 headings
> assert something the file itself records as closed.** They are listed below with the closure tag
> to grep. ⚠ **The markers were deliberately NOT flipped**: an agent sweep is ~half wrong before
> refutation, and mis-marking an OPEN row as closed is the dangerous direction. What IS
> machine-checked is that **every cited tag exists** — 21 of 24 carry a tag and all of them resolve
> in `docs/todo.md` or the archive; **0 missing**. The remaining 3 cite no tag and are unverified.
>
> ⭐ **The rule this replaces guesswork with:** before planning a row, `grep` its finding ids across
> the WHOLE file. And **when you close a step, edit its heading in the same commit** — that is the
> only thing that stops this table regrowing.
>
> | heading line | what it asserts | closure tag(s) to grep |
> |---|---|---|
> | 1757 | **AA12/AA13 step 3** the legitimately-empty case / ⬜ still needs **Cheat Engin | `AA12-STEP3-EMPTY-2026-08-23`, `AA2-STEP4-CHURN-2026-08-23` |
> | 1880 | ⬜ What is left — a package build, which is the maintainer's step | `AD4-CONTESTED-2026-08-23`, `C1-SPAWNER-EXISTS-2026-08-24`, `MG2-CONTAINER-2026-08-23`, `V1A-REALLOC-2026-08-23`, `V8-DLLHALF-2026-08-23` |
> | 3452 | 8 / `AC17` / **C** / **Needs a real mount point.** Mount a fixed volume into a | `VOLUMEROOT-2026-08-19`, `ZHTW-SWEEP-2026-08-22 *(archive)*` |
> | 3483 | survives real `.CT` traffic without throwing — is tested by two *distinct* com | `MB3-THROW-2026-08-23` |
> | 3998 | ⛔ V8 BLOCKED 2026-08-20 `[V8-ROWMAP-2026-08-20]` — the RowMap probe fails on D | `V8-DLLHALF-2026-08-23`, `V8-PAINTED-2026-08-23`, `Y11-OPAQUEDROP-2026-08-22` |
> | 4029 | ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L10 (T1e Views/app root): AF7 / | `AE4-TIMING-2026-08-24`, `GRIDRECYCLE-2026-08-21`, `L10-HEADLESS-2026-08-20`, `L10-OWNER-2026-08-21` |
> | 5136 | 🟡 L3 steps 2 + 3 — step 3's CONDITION HAS NEVER FIRED; step 2 is CE-only `[L3- | `L3-STEP2-CE-2026-08-21 *(archive)*`, `V11-SYM-2026-08-20` |
> | 6058 | 🟡 G11 steps 3–4 ANSWERED 2026-08-22 `[G11-TIERS-2026-08-22]` — Tier 2 has neve | `G11-AVOWED-2026-08-23`, `G11-STVOYAGER-2026-08-23` |
> | 6135 | 🟡 第 3 步 CE batch — opened 2026-08-22 `[STEP3-BATCH-2026-08-22]`, three rows re | `CTDISC-SLOTS-2026-08-22` |
> | 6531 | 🟡 ALL BUT TWO STEPS DONE 2026-08-20 `[PEHOOK-2026-08-17]` — a validation failu | `PEHOOK3C-STAGE-2026-08-23` |
> | 7487 | ⬜ NEW 2026-08-17 — G12 / G3: the offset family, and the apply_rescan gate | `DSA-2026-08-16`, `G12-PIPE-2026-08-17`, `G12S2-STAGE-2026-08-23`, `G3-STAGE-2026-08-23`, `G3-VOID-2026-08-20` |
> | 7689 | 🟡 GROUP 5 opened 2026-08-18 `[CE-2026-08-18]` — plugin bridge live, freeze rec | ⚠ **none cited — unverified** |
> | 8486 | 🟡 4-of-5 CLOSED 2026-08-19 — A6: Force now holds the class AND its subclasses | `A6-CDO-2026-08-22`, `A6-DERIV-2026-08-19`, `A6-DERIVE-2026-08-22`, `A6-SPAWN-DQ7R-2026-08-23` |
> | 8569 | 🟡 A5 + AE9 CLOSED, V6 corrected to a HALF-pass 2026-08-19 — the fourteen-MED b | `AF1-ENUMCOUNT-2026-08-23`, `AF2-CLASSCAP-2026-08-23`, `G1-AMBER-2026-08-24`, `U7-CJKCUT-2026-08-24`, `V6U8-FNAMEPAIR-2026-08-22` |
> | 9079 | 🟡 STEPS 1-4, 7, 8, 9 DONE — `[FREEZESCOPE-2026-08-18]` — Freeze must hold the  | `FZ6-CAP-2026-08-24` |
> | 10286 | 🟡 PARTIAL 2026-08-10 — GObjects layout fix (build 2782), DragonSword Awakening | `DSLAYOUT-BASEANCHOR-2026-08-23`, `DSLAYOUT-GREP-2026-08-24` |
> | 11304 | ① Log-derivable — still open: B29 (log half) / B18 / B19 / B10 / B8 (🟡 deferre | `B19-LOCKED-2026-08-22`, `B8-DEFERRED-2026-08-23`, `ELLIOT-B4-2026-08-18`, `LIVE-2026-08-23`, `NONASCIILS-2026-08-24` |
> | 12546 | ⬜ Shipped + unit-tests-pass but unproven on real games — the long tail: Dump E | ⚠ **none cited — unverified** |
> | 13968 | 1 / 把 `DetectVersion: PE resource failed, falling back to memory string scan`  | `UE3-GALGUN-2026-08-23` |
> | 14372 | 🟡 …and the LWC half's blocker is now MEASURED rather than assumed `[U3U17-LWC- | `U3U17-LWC-ELLIOT-OUT-2026-08-23` |
> | 14444 | 🟡 U3 / U17 —— struct 預覽的 LWC 寬度與 GAS 樣本（GAS 半 **CLOSED 2026-08-23**；LWC 半只差容器樣 | `U3U17-GAS-2026-08-23`, `U3U17-LWC-2026-08-24` |
> | 14453 | 🟡 G1 / X3 / U7 / AF2 — step 3 CLOSED 2026-08-23 `[AF2-CLASSCAP-2026-08-23]`; s | `G1-AMBER-2026-08-24`, `U7-CJKCUT-2026-08-24` |
> | 14545 | 🟡 G1 / X3 / U7 / AF2 —— 三個要碰到特定遊戲才看得到的顯示（步驟 3 **CLOSED 2026-08-23**；步驟 2 第二個宿主 | ⚠ **none cited — unverified** |
> | 15296 | ⬜ G12（heuristic 分支）—— 走 fallback 時 offset 仍正確 | `G12-PIPE-2026-08-17`, `G12S2-STAGE-2026-08-23` |
>
> ℹ️ Line numbers are as of 2026-08-24 and drift on every edit — match on the TEXT, not the number.
> The sweep also produced **27 blocked** items, each with a *measured* reason (no sample on this
> machine, premise unsatisfiable, structurally impossible), and **10 runnable**. Those live in the
> sections themselves; this table is only about headings that lie.

> ## ▶ If the ask is "carry on fixing bugs", do NOT start here
>
> The bug queue is **not** in this file. It lives in
> [audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md) →
> **§3b "▶ THE NEXT FIX SESSION STARTS HERE"**, which carries an ordered, already-vetted list of
> the next six fix groups (① – ⑥) with file:line and the reason each group is one job. Start at ①;
> no re-derivation is needed to begin.
>
> **What IS in this file, and is not in that one:**
> - [verification-register.md](verification-register.md) — **8 open batches** needing a running game (moved out 2026-09-03;
>   this is a DERIVED count and it has drifted to a stale 43, 36, 40 and 30 in turn; re-derive,
>   never hand-adjust:
>   `awk '/^## Pending live-game verification/,0' docs/verification-register.md | awk '/^## /&&!/^## Pending live-game/{exit}1' | grep '^### ' | grep -c ⬜`).
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
> ### ▶ OPEN FIXES INDEX — 5 items, and they are NOT in the count above
> **Read the split before quoting a number.** Of the **twelve** field-found defects this index
> carried on 2026-08-18, **eleven are fixed** and exactly one survives: `[STALEDLL]`(a), which is a
> maintainer-only file deletion. The one other row, `[SCANIDENTITY]`, was surfaced by the audit
> programme itself on 2026-08-19 and deliberately deferred — it is not a regression and states its
> own reason for waiting. So "12 → 1" is the honest headline for the original queue, and **5** is
> the honest row count of this table.
> ⭐ **`[STALEDLL]`(a) CLOSED 2026-08-22** — the maintainer deleted the stale DLL, and it was
> verified gone by a recursive sweep of both `Cheat Engine` install folders (0 `UE5Dumper*.dll`
> under either). That also **unblocks the `.CT DLL discovery` verification row**, which could
> only have produced a false negative while the stale file was present. The third, `[CADENCEBAND]`, was field-found on 2026-08-22
> and **downgraded to low the same day**: its only witness is our own 15 FPS test harness, and the
> one realistic scenario for a real game was tested and refuted. It stays listed because the
> arithmetic is real below 25 FPS, not because anything is known to be broken in the field.
> The fourth, `[FORCESTATUSCLIP]`, was found later the same day while running M1–M5 step 4.
> ⭐ **None of the five is a straightforward code fix**: one is a maintainer-only file deletion,
> one is an open product question, one is a design call, one is cosmetic with the same fact
> already reaching the user by a second, unclipped route, and the fifth (`[TREERECLICK]`, found
> 2026-08-22 while running AE2/AE3) is a **UI design call about ListBox semantics** whose obvious
> target — the ClassStruct dedupe — is measurably already correct. A fix session looking for work should
> read `## Pending live-game verification` instead.
>
> ⚠ **Four rows left on 2026-08-21, and not all in the same direction** — worth noticing, because a
> queue that only shrinks by fixes hides the other outcomes. `[RELAUNCHPIPE]` was **real** and is
> fixed + live-verified. `[VOLUMEROOT]` was **real**, fixed, and — against its own row's prediction —
> fully **verified**, no mount point required. `[PROPSEARCHCAP]` was a **deferred feature**, now
> built and live-verified on DumperTest. `[PROXYDEPS]` was **refuted**: there was no defect, and the
> tooling that reported one has been corrected so it cannot say it again. All four write-ups follow.
>
> ⭐ **THREE of the four sat deferred on a stated blocker that turned out to be false** — "only a
> real mount point can verify it" (a cross-volume junction does), "the deps listing shows a
> breakage" (it shows empty translation units), and "cannot be visually verified in an unattended
> session" (computer-use drove the whole thing, including a hand-corrupted settings file).
> **Re-test a deferral's premise before accepting it**, exactly as you would re-derive a finding's
> premise before fixing it. A deferral reason ages worse than the finding it defers.
> ⚠ `check_audit_register.py` reads **only** audit #5's table, so these are counted nowhere and are
> invisible to the gate. They carry **no severity tier** — the audits assigned those, these were
> found in the field. **Grep the tag** (stable; line numbers drift). Audits #3 and #4 are fully
> closed — #4's ten unmarked rows are its *refuted, do-not-re-raise* table, not open work.
>
> | tag | one-line defect |
> |---|---|
> | `[SCANIDENTITY-2026-08-19]` | Value-scan candidates are re-read across refines by raw address with no re-validation of the owning object's identity (audit #5 AB7, now ✅ as docs-only). The refused `SerialNumber` witness is wrong for a passive observer and §4.3's "witness input bytes" does not apply (the value is expected to change). The only real check is re-reading the UObject class pointer to catch a slot recycled by a *different* class — a behaviour-changing feature with an open product question (AA2: class-wide targeting can be by design) and no unit-test seam. Deferred; needs a maintainer decision + live game with mid-scan object churn. |
> | `[CADENCEBAND-2026-08-22]` | 🟡 **downgraded to low the same day — possibly not worth fixing.** The Live Funcs "periodic timer" classifier excludes per-frame callbacks with a hard `meanPeriodMs > 40.0`, i.e. **it assumes ≥25 FPS**: 0 of 6 flagged at 60 FPS, 4 of 6 at 15 FPS. ⚠ **The only witness is our own harness** — `launch_dumpertest.py` caps DumperTest at 15 FPS by house rule; no real game has been seen hitting it, and the one realistic scenario (profiling a backgrounded game) was **tested and refuted** — DumperTest holds a full 60 FPS while minimised. If ever fixed: not a bigger constant, the band must be relative to the observed frame period, and the *minimum* period is the wrong estimator (8.33 ms at 60 FPS, from a twice-per-frame callback) — the mode is right. |
> ⭐ **`[FORCESTATUSCLIP]` FIXED 2026-08-22** by `0276c05d`, and its row is **deleted** — that
> commit marked the write-up ✅ but left this index row saying OPEN, so the two halves of the
> register disagreed for a few hours. ⚠ The row's own prescription (`TextTrimming` on the
> `TextBlock`) would **not** have worked: a horizontal `StackPanel` gives each child its
> DESIRED width, so nothing constrains it and trimming is inert. The toolbar is a `DockPanel`
> now, with the status line as the fill child.
> ⭐ **`[Y11-OPAQUEDROP]` FIXED 2026-08-23** (build 3319) and its row is **deleted**. The dialog
> validated top-level params and then called `WriteStructParam`, which forwards each sub-field
> straight to `WriteParam` — whose opaque-type guard returns **silently**. So an opaque
> sub-field's typed value was dropped while FIRE still said `ProcessEvent OK`. The guard now
> lives beside the write it protects (`ParamBufferBuilder.TryValidateStructSubFields`) and the
> dialog refuses, naming the member: `ERROR: NewBrush.ImageSize: … cannot be built from a
> textbox …`. ⚠ It also closed a SECOND hole nobody had reported: an out-of-range **integer**
> sub-field silently masked to width — the W6/Y2/Y9/Y15/AE1 family surviving in the one place
> its fix had not been applied.
> ⭐ **`[TREERECLICK]` FIXED 2026-08-23** (build 3322) and its row is **deleted**. The cause was
> plain `ListBox` semantics — Avalonia writes `SelectedItem` only when it CHANGES, so clicking the
> already-highlighted node raised nothing and no walk reached the pipe. The fix is
> `MainWindowViewModel.ShowClassInClassStructAsync`, which **clears the tree highlight before**
> loading; all five cross-tab handoffs route through it. That fixes both halves at once: the tree
> stops claiming P is selected while the panel shows X, and the next click on P becomes a real
> change, so it loads. ⛔ Deliberately NOT a pointer handler on the tree — see the section.
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

## ✅ DONE 2026-09-05 (build 3374) — `TArray<TLazyObjectPtr>` no longer strides `0x20`

> **FIXED.** `InferScalarSize` and `ReadLazyObjectArrayElements` both route through
> `LazyGuidOffset` now: `LazyGuidOffset(elemSize) + 0x10`. Passing the caller's `elemSize`
> **in** is deliberate — a real `ElementSize` gets measured and latched, so the array path
> can finally emit the `payload envelope measured` line it structurally could not before;
> garbage still falls back to the version default, which is what the forced constant was
> actually guarding against. All three stale `= 0x20` comments deleted with it.
>
> ⚠ **Not live-verified, and it cannot be here**: no installed title has a
> `TArray<TLazyObjectPtr>` (OCTOPATH has 5 scalar lazy properties and zero arrays). Pinned
> offline instead, in `dll_core_test` — six checks including *"0x20 is returned by NO era"*
> and a 4.18 row asserting the same `0x1C` OCTOPATH reported live as its `ElementSize`.
> That offline route is the one this batch believed did not exist; see item 6 below.

<details><summary>original entry (kept — the failure analysis is why the fix took the shape it did)</summary>

**`TArray<TLazyObjectPtr>` still strides `0x20` — the one site audit A1 did not reach**

*Found 2026-09-05 by an offline audit of the A1 batch on the verification PC (adversarially
verified, then confirmed against the vendored engine source). **Effort: S. Risk: LOW** — it is the
same substitution `5eafd419` and `fffe5fcf` already made elsewhere. Not a regression: this site
predates the audit and was simply missed twice.*

`5eafd419` replaced the hardcoded `FWeakObjectPtr(8) + Tag(4) + pad(4) + FGuid(16) = 0x20` model
with a measured envelope, and `fffe5fcf` swept up two sites it had missed. **A third survives.**

* [`dll/src/Ubel.cpp:2976-2978`](../dll/src/Ubel.cpp) — `ReadLazyObjectArrayElements` **discards**
  the caller's `elemSize` and forces `elemSize = 0x20`, with a comment still stating the deleted
  model. `sizeof(TLazyObjectPtr)` is `0x1C` (≤5.2) or `0x18` (≥5.3) — **never `0x20`**, which the
  live OCTOPATH measurement confirms from the engine's own side (`ElementSize 0x1C`).
* [`dll/src/Ubel.cpp`](../dll/src/Ubel.cpp) `InferScalarSize` returns a hardcoded `0x20` for
  `LazyObjectProperty`, and `ValidateArrayElemSize` treats `InferScalarSize` as **authoritative**,
  overriding the engine's correctly-reported `ElementSize` whenever they disagree.

**Two consequences, and the second is the nastier one.**

1. Element 0 reads correctly; every index ≥ 1 drifts **4 bytes/element** (≤5.2) or **8** (≥5.3), so
   both the `FGuid` and the embedded `FWeakObjectPtr` land in the wrong element. `fv.arrayElemSize`
   is also what the CE XML exporter uses for per-element offsets, so an exported
   `TArray<TLazyObjectPtr>` table strides `0x20` on every UE version.
2. ⭐ **It makes the lazy half look unverifiable.** `LazyGuidOffset(0x20)` computes `0x20-0x10 =
   0x10`, which `PersistentPtrEnvelopeFor` **rejects** (it accepts only the tagged envelope or
   `0x08`), so `measured` at `Ubel.cpp:394` is false and the
   `TLazyObjectPtr payload envelope measured` line **cannot be emitted from the array path at all**.
   `[A1-ENVELOPE-2026-09-05]` calls that line "required", so an operator who exercises lazy by
   drilling into a `TArray<TLazyObjectPtr>` — the obvious way to find one — sees no line and scores
   a **correct** fix as FAILED. The line can only come from a **scalar** `LazyObjectProperty` walk
   (`Ubel.cpp:4088` / `:6150`), which is how it was obtained on both hosts.

**Fix**: delete the forced stride and the `InferScalarSize` special case; let the engine's
`ElementSize` through and route the offset via `LazyGuidOffset` as the other 11 call sites do.
**Then re-run** `py tools/verify/a1_softlazy_envelope.py <host>` on OCTOPATH (tagged, `0x0C`) and any
5.3+ title (untagged, `0x08`) — the rig already separates the two types, and a
`TArray<TLazyObjectPtr>` walk should then emit the line the scalar path emits today.

</details>

-----

## What the 2026-09-05 verification pass found *besides* the four rows it closed

*Produced on the verification PC, 2026-09-05, build 3371: an offline re-derivation of the fifteen
commits against the vendored engine, every finding then handed to an independent agent told to
**refute** it. 51 survived, 13 were killed. The stale citations went straight into
[`verification-register.md`](verification-register.md); what is left below is **code**, and none of
it is a regression — every item is a site the audit's own classification covers and its sweep did
not reach. ⚠ Each entry names the file:line, so **re-derive before acting**; line numbers drift.*

### 1. `bCasePreservingName` — A9 changed the contract but left seven sites on the old value

`ea844833` established that `sizeof(FName)` under CasePreservingName is **`0xC`, not `0x10`** — the
`0x10` was the UObject `Name`→`Outer` **slot**, which is a different quantity — and it rewrote the
header docs and the C# tests to the `0xC` contract. **Seven sites that A9's own classification puts
in the `sizeof` bucket were left at `0x10`.** Before the commit the tree was uniformly (and wrongly)
`0x10`; it now **contradicts itself** under CPN, which is strictly worse to debug.

* [`dll/src/Ubel.cpp:333`](../dll/src/Ubel.cpp) `ReadSoftObjectPath` steps `PackageName`→`AssetName`
  with `bCasePreservingName ? 0x10 : 0x08` — a textbook "step to an adjacent FName" — while
  `SoftObjectPathPayloadSize` **79 lines below in the same family** uses `0xC`, and
  `Grimoire.h`'s `FSoftObjectPathSizeFor` is built on `0xC`.
* [`dll/src/Ubel.cpp:4313`](../dll/src/Ubel.cpp) and `:4476` set `fv.softArrayFNameSize` to `0x10`
  under CPN, while [`Ubel.h:435`](../dll/src/Ubel.h) documents that field as a **`sizeof(FName)`**.
  ⚠ The header next to it explicitly forbids the naive reconciliation — *"Deliberately different
  from `SoftArrayFNameSize` above, which IS a sizeof. Do not make them consistent."* — so read both
  comments before touching either.
* `Ubel.cpp:3188` and three more in the same shape.

⚠ **Impact today is ZERO and that is exactly why it is easy to leave rotting**: `bCasePreservingName`
has only two writers (`Genau.cpp:3243/3247`, inside a live 20-object vote), no config/preset/UI can
force it true, and **12 titles have measured false with zero CPN**. The register was right to open
no row — a row would be unfalsifiable. It belongs here instead. **Effort S, risk LOW.**

### 2. ✅ DONE 2026-09-06 — A6's version table comment was wrong for six versions inside one printed band

> **FIXED** in `Grimoire.h`: the single `4.18-5.08 0x44 -> 0x70` row is now three —
> `4.18-4.24 0x44 -> 0x70`, **`4.25-5.02 0x4C -> 0x78`**, `5.03-5.08 0x44 -> 0x70` — with
> the delta `0x2C` called out as identical across all three, which is why the shipped code
> was never affected. The re-derivation command is in the comment.
>
> ⭐ **And the 5.08 endpoint is no longer an assertion.** It was written with no `5_08`
> template in existence (the highest was `5_07`); RE-UE4SS shipped
> `MemberVariableLayout_5_08_Template.ini` on 2026-09-05 and the vendor clone was updated
> 2026-09-06 — it measures `0x44 -> 0x70`, agreeing with what the comment claimed.
> Live corroboration for the delta the same day: OCTOPATH 4.18 stock `0x44 -> 0x70`, and
> DQ XI S `0x54 -> 0x80` — the same `0x2C` off a shifted base, the case no template covers.

<details><summary>original entry</summary>

**A6's version table comment is wrong for six versions inside a band it prints as one**

[`dll/src/Grimoire.h:419-422`](../dll/src/Grimoire.h) prints `4.18-5.08  Offset_Internal 0x44 →
FieldSize 0x70`. Measured against the UVTD templates, **4.25, 4.26, 4.27, 5.00, 5.01 and 5.02 all
have `0x4C → 0x78`**; only 4.18-4.24 and 5.03-5.07 have the pair as printed. ⭐ **The shipped code is
unaffected** — it reads only the *delta* (`0x2C`), which is right across the whole band, and the
live measurements confirm it (`0x54 → 0x80` on DQ XI S, `0x44 → 0x70` on OCTOPATH). But a reviewer
sanity-checking the derivation on a UE 5.0/5.1 game will find `0x4C/0x78`, conclude the table is
broken, and "fix" a correct one. Also: `5.08` is asserted with **no `5_08` template on this
machine** (highest is `5_07`). **Fix the comment, not the code. Effort XS, risk LOW.**

</details>

### 3. ✅ DONE 2026-09-06 — A6's two spread tests pinned the error terms independently and did not compose

> **FIXED** with three more assertions in `dll_helpers_test.cpp`, next to the two that were
> already there. They pin the compound case **and its direction**, which is the part the
> original pair could not express:
> * a **LOW** version miss stacked with a missed CPN composes to **`0xC`** — `0x50+0x2C+8 =
>   0x84` true, `0x50+0x28+0 = 0x78` guessed — and `0xC` is **outside** the
>   `{0, ±4, +8, −8}` spread, so the probe does **not** recover;
> * the counter-case, so this is not read as "any two errors escape": a **HIGH** version
>   miss partially **cancels** a missed CPN (`+4` then `−8`, net `−4`, still inside).
>
> ⛔ Still **not** an argument for widening the spread — `[A6-BOOLFIELD-2026-09-05]` and the
> register both say do not. It is an argument for not describing the spread as an
> unconditional net when it has a hole. `dll_helpers_test` 2,327 → **2,330**, 0 failures.

[`dll/tests/dll_helpers_test.cpp:5808-5813`](../dll/tests/dll_helpers_test.cpp) asserts
`guessAs420 - trueAt415 == 4` and `cpn - nonCpn == 8` **separately**. A version misdetected across
the 4.17/4.18 boundary **and** a missed CPN sum to `0xC`, which is **outside** the probe spread
`{0, ±4, +8, −8}`. RE-UE4SS ships a real-world shape that lands there (Kingdom Hearts 3: pre-4.18
tail order with `RepNotifyFunc 0x60` before `Offset_Internal`). ⛔ This is **not** an argument for
widening the spread — `[A6-BOOLFIELD-2026-09-05]` and the register both say do not — it is an
argument for not describing the spread as an unconditional net. **Effort S (a third test + a
comment), risk LOW.**

### 4. ✅ DONE + LIVE-VERIFIED 2026-09-06 — `set_ue_version_override` now re-derives the soft/lazy envelope

> **FIXED**: the handler clears `DynOff::SOFTPTR_PATH` and `DynOff::LAZYPTR_GUID` when a
> non-zero version is set, and logs that it did. `PersistentPtrEnvelopeFor` consults
> `latched` **before** `ueVersion`, so a latch taken under the old version outranked the new
> one for every call that cannot produce a fresh measurement — the override changed the
> version and not the layout it implies, which is the one thing an override is for.
>
> **Measured on OCTOPATH (UE 4.18), build 3379**, walking a live `KSTextManager` instance:
> ```
> before   TSoftObjectPtr payload envelope measured: +0x10 (ElementSize 0x28 - payload 0x18, UEver=418)
> after    TSoftObjectPtr payload envelope measured: +0x08 (ElementSize 0x28 - payload 0x20, UEver=505)
> ```
> The envelope re-derived `0x10 → 0x08` and the payload moved `0x18 → 0x20` (the `>= 501`
> `FTopLevelAssetPath` arm). ⭐ **Built-in negative control**: the walk taken *before* the
> override added **zero** new lines — the line only appears when the value actually changes,
> so the one that appeared is the re-derivation and not just walk traffic.
>
> ⚠ **Scope, and it is in the code comment too**: the override deliberately does **not**
> re-run `ValidateAndFixOffsets`. The rest of the `DynOff` family stays as the real scan
> probed it, because those are *measured from the running image*, not derived from a version
> number — re-deriving them from a hypothetical version would replace fact with guess. These
> two are different precisely because their fallback **is** version-derived.
>
> ⚠ This also matters for `tools/verify/a3_funcflags_override.py`, which uses the override as
> its A/B lever: `FunctionFlagsOffsetFor` is recomputed per call and was never affected, but
> anyone reusing that technique for soft/lazy before today would have measured the old latch.

`PersistentPtrEnvelopeFor` consults `latched` **before** it looks at `ueVersion`
([`Grimoire.h:278-279`](../dll/src/Grimoire.h)), and `set_ue_version_override`
([`Fern.cpp:1757-1760`](../dll/src/Fern.cpp)) sets only `g_cachedUEVersion` — it never clears
`DynOff::SOFTPTR_PATH` / `DynOff::LAZYPTR_GUID`. So after an override, any call that cannot produce
a fresh accepted measurement returns the **pre-override** latch. ⚠ This matters for A3's planned
`421→422→421` A/B and for any "override to test a hypothesis" step: the version changes, the
envelope does not. Either clear the two latches in the override handler or say so in the row.
**Effort S, risk LOW.**

### 5. ✅ DONE 2026-09-05 — comments that still stated the pre-fix layout as fact

[`Ubel.cpp:2958`](../dll/src/Ubel.cpp) — `// Element layout: FWeakObjectPtr(8B) + Tag(4B) + pad(4B)
+ FGuid(16B) = 0x20` — sits **immediately above** `ReadLazyObjectArrayElements`, i.e. above the code
in §*`TArray<TLazyObjectPtr>` still strides `0x20`* below, and `:6138` repeats it. Read in isolation
either says `+0x10` is the right answer, which is what an operator judging A1 will be doing.
**Delete with the stride fix. Effort XS.**

### 6. ✅ DONE 2026-09-06 — A5's "provable no-op" argument was incomplete (the conclusion still holds)

> **CORRECTED** in the register. The divisor halves are true (`16`@0 before `32`@2, `20`@3
> before `40`@4), but `PreferStride` fires on **any** equal score — `score == bestScore &&
> bestStride != 0 && stride < bestStride` — and `{16, 24, 32, 20, 40}` holds two
> **non-divisor** pairs whose smaller member sits **later**: `(24, 20)` and `(32, 20)`.
> The conclusion survives on a different argument, which is `Lineal.h`'s own: 24/20 and
> 32/20 are neither multiples nor divisors, so on a correct pool one of each pair lands
> off-item and loses on score long before a tie. **The decision not to open a row stands;
> the old sentence must not be cited as a proof.**

[`verification-register.md`](verification-register.md) closes A5's tie-break with *"a provable no-op
over `{16, 24, 32, 20, 40}` — every divisor pair already has the smaller candidate earlier in the
list"*. True of the divisor pairs `(16,32)` and `(20,40)`, but `PreferStride` fires on **any** equal
score, and the list holds two **non-divisor** pairs where the smaller sits **later**: `(24,20)` and
`(32,20)`. On a score tie in either, old and new select differently. ⚠ The *decision* not to open a
row stands; the **recorded reason** is not the reason. Fix the sentence so it is not cited later as
a proof. **Effort XS, doc only.**

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

## 🔎 Audit #3 fixes (build 2168) — closed; FOUR subsections still owe something

⚠ **This heading said "CLOSED, archived" while carrying 694 lines.** The rollup did move to
[archive/todo-closed-2026-08-build-2715.md](archive/todo-closed-2026-08-build-2715.md) in 2026-08,
but 2026-08-23 work then accreted under the stub. On 2026-09-03 that was resolved three ways:
20 closure writeups (455 L) → [archive/todo-closed-2026-09-03-build-3369.md](archive/todo-closed-2026-09-03-build-3369.md),
5 subsections describing the FIXTURE (66 L) → `tools/ue-sample/README.md` (they were the only
copy), and the four below **kept here because their own bodies still say work is owed**.

All 23 scheduled items shipped; the rest were refuted or downgraded to optional cleanup.
The rollup moved to [archive/todo-closed-2026-08-build-2715.md](archive/todo-closed-2026-08-build-2715.md);
the per-finding detail was always in [audit-2026-07-14-findings.md](audit-2026-07-14-findings.md).

### ⭐ The conversion silently unrooted the column's sort, and the repo's own guard caught it

`DataGridSortWiringTests.Every_user_sortable_XAML_column_is_binding_rooted_or_has_a_comparer` failed
the moment the XAML changed:

```
LiveWalkerPanel.axaml / FieldGrid : DataGridTemplateColumn SortMemberPath="TypeName" Binding="(none)"
```

Avalonia resolves `SortMemberPath` by **reflection**, and under trimming that metadata survives only
for a property some compiled binding roots. As a `DataGridTextColumn`, `Binding="{Binding TypeName}"`
rooted it and the column was safe *for free*. Converting it to a template column removed that
binding — so the header would have animated and **done nothing in the shipped trimmed build**, while
working perfectly in every JIT test host and every Debug run.

⭐ **The shape worth remembering: making a column PRETTIER can break its SORT, because both ride on
the same attribute.** Nothing about "add a tooltip" suggests "check the sort wiring".

Fixed by rule (2) — an explicit comparer, exactly as the Value column already does:

```csharp
["TypeName"] = DataGridSortComparers.Ordinal<LiveFieldValue>(r => r.TypeName),
```

⚠ Rule (1) was **not** used even though the cell template's `Text="{Binding TypeName}"` is a
compiled binding and plausibly roots the property. The guard only credits the *column's own*
`Binding`, deliberately: it exists because audit #5 found six instances of one defect and a sweep
turned up four more, one of them argued away in a comment. Arguing with it is how the tenth happens.

⭐ **Both directions observed, not reasoned.** Deleting the comparer reproduces the failure above
(1 failed / 4706 succeeded, and it is the only one); restoring gives **4707 / 0**.

⭐ **Verified on the AOT-trimmed publish** (v1.0.0.3334, sha 9d422400, DumperTest Shipping) — which
is the only place the sort claim can be tested at all:

| check | observed |
|---|---|
| tooltip on a clipped cell | cell `ObjectPropert` → tooltip **`ObjectProperty`** |
| tooltip on the case that prompted it | cell/tooltip **`DataTableRows`** |
| sort ascending | header ↑, rows reorder to `BoolProperty ×3 → DataTableRows → ObjectProperty → StrProperty` (was offset order) |
| sort descending | header ↓, exact reverse |

Both sort directions, in the trimmed binary, are what close the risk the conversion introduced.

⚠ **An operational trap worth recording, unrelated to the code**: the address box silently ate a
hex address as Bopomofo (`0x1C607092740` → `0風注音量器度假使出任`) because the Windows IME was in
Chinese mode. `Shift` did not toggle it and `systemKeyCombos` is ungranted, so Win+Space is
unavailable — **clicking the taskbar language indicator does work** and is the route to use.

ℹ️ Two more tests in `LiveFieldValueTooltipTests` — `TypeTooltip` carries the whole name and is
`null` when empty; the XAML binds it on the same `TextBlock` as `Text="{Binding TypeName}"`, and
`SortMemberPath="TypeName"` is still present so the conversion cannot silently drop sorting again.

### ✅ V8 DLL half CLOSED 2026-08-23 `[V8-DLLHALF-2026-08-23]`; the one look is still owed

`tools/verify/v8_datatable_cap.py`. The three UI strings are already pinned by C# tests — but those
assert the **ViewModel's** strings from **synthetic** input, so they say nothing about whether the
DLL hands the ViewModel a correct `N`. It did not: every one of them passed throughout
`[DTROWMAP-2026-08-23]`, while a 100-row table reported 8. This closes the half they structurally
cannot reach.

| # | check | measured |
|---|---|---|
| 1 | truncated case | `Table_Big` → `row_count 100`, **64** rows at the default; `limit=1000` → 100/100; Caption decodes on **all 100** |
| 2 | **negative control** | `Table_Small` → `8/8`, nothing truncated |
| 3 | **N follows the data** | `V8_RebuildBigTable(77)` → new object, `77/77`, and `77` with **64** rows at the default |
| 4 | **N is exact** | `V8_RemoveOneTableRow()` → `76/76` |

⭐ **Checks 3 and 4 are impossible on a commercial game and are what make 1 meaningful.** A constant
that happens to read 100 passes check 1 forever; only changing `N` on demand proves the number is
read from the data. Check 4 then rules out an off-by-one or a capacity-vs-count confusion, which
check 3 alone would not.

⭐ The page size is **derived at runtime** from `Ubel.h` and the `CMD_WALK_DATATABLE_ROWS` handler
rather than hard-coded in the rig. ⚠ The first version of that derivation searched `Fern.cpp`
unscoped and matched **another command's** `request.value("limit", 200)`, reporting a disagreement
that did not exist — a detector has to be right about *where* it reads, not only about what it
matches. Now scoped to the handler block.

⬜ **Still owed, and it is one look:** whether the three strings are actually **painted** — not
clipped, not covered. `[PARAMSSORT-2026-08-22]` is the precedent: a correct VM string in a
`TextBlock` with no `TextWrapping` truncated itself.

-----

### ⚠ One unreproduced anomaly, recorded because it is exactly the shape AA2 is about

On the run with the broken control, **one decoy of eight read `9999`**. Chased rather than filed:

* it was genuinely a decoy — `walk_instance` reported `class=DumperTestHolderDecoy`, and it was not
  in the holder list;
* it held 9999 steadily for 6 s;
* ⭐ **but the freeze was NOT writing it**: `write_mem` of `-1.0f` to `addr+0x290` stuck for 6 s. A
  re-asserting freeze would have restored 9999 within a tick. So it was written **once**, not held;
* **not reproduced in four subsequent attempts** — three replays of the identical
  destroy→decoys→holders sequence (0/8 each, 100/100 holders frozen), plus the forced-recycle run
  above (0/24).

So: one write of the frozen value into a non-derived class, seen once in five runs, un-reproducible,
and not an ongoing hold. **Not filed as a defect** — one observation with no reproduction is not a
finding, and I could not exclude an artifact of the run whose control was broken. Recorded here
because if it ever reappears this is the second sighting, and the reproduction recipe plus the
`write_mem` discriminator are written down.

⚠ ~~Step 4 is closed; step 1 (old-DLL contract refusal) and step 5 (AA3, permanent rescan
failure) remain open.~~ **STALE — all three closed.** Step 4 `[AA2-STEP4-CHURN-2026-08-23]`;
steps 1 and 5 `[AA2-CONTRACT-AA3-STOP-2026-08-23]` (todo.md:1262), whose body states *"AA2/AA3
is closed end to end"*. This sentence sits inside the older step-4 section and was superseded by
the newer one inserted above it — it re-opened the row for two readers before being caught.

### ✅ CLOSED 2026-08-23 `[SEETHRU-DQ7R-2026-08-23]` — the See-through non-regression is now a MEASUREMENT, not an argument

`[SEETHRUNOOP-2026-08-22]` rewrote hit→actor resolution to try `Actor` → `HitObjectHandle` →
**`Component`** and take the first that walks up to an `AActor`. The obvious risk was regressing the
two hosts where See-through already worked, and the entry admits what it had instead of evidence:

> ⭐⭐ **Why this cannot regress the builds where See-through already worked** (Tower of Mask, DQ7R —
> **neither runnable here **when this was written**, so this argument had to carry the weight — ⭐ **DQ7R has since been MEASURED, see `[SEETHRU-DQ7R-2026-08-23]`; Tower of Mask is still untested****)

That blocker expired. DQ7R has been driven repeatedly since 2026-08-20 (four runs on 2026-08-23
alone, `[A6-SPAWN-DQ7R-2026-08-23]`). Run today on **DQ7R, UE 4.27, build 3334**, in-world at
艾斯塔德島, with `tools/verify/seethrough_arms.py run`:

```
enabled            : hidden_count=1  hidden_actors=['0x2203EF5E680']
POSITIVE CONTROL   : 0x2203EF5E680  bHidden=true  (bit 5, mask 0x20)   ok
                     detector (2) can FIRE     : PASS
after disable      : active=False hidden_count=0
  (1) hidden_count == 0        : PASS
  (2) every one restored       : PASS   0x2203EF5E680 bHidden=false
```

⭐ **What it hid is the point, not that it hid something.** The actor resolves to:

```
Landscape_1   class Landscape
super chain   Landscape -> LandscapeProxy -> Actor -> Object
outer         PersistentLevel (Level)
```

A real `AActor` subclass — so `ResolveToActor` produced an actor from the trace hit on the host the
argument was worried about. `hidden_count=1` is expected and not a weak result: `pierce_count=1`, so
only the nearest occluder is hidden by design.

⭐ **Two independent detectors, and the second was shown able to fire before it was trusted.** The
rig refuses a vacuous pass by construction: it FAILS if `hidden_count` never rises, and FAILS if the
DLL names an actor whose own `bHidden` bit is not set while hiding. Both gates were exercised —
`bHidden=true` during, `false` after, read back off the instance rather than taken from the hider's
own tally.

⚠ **Honest scope.** This shows See-through **works on DQ7R with the current resolution code**. It is
not a differential against the pre-fix binary (that build is not deployed anywhere, and the fix is
three builds old). What the row actually feared — that the rewrite broke a host where it used to
work — is answered for one of the two named hosts. **Tower of Mask remains untested.**

ℹ️ **Two things this run had to fix before it could measure anything**, both worth remembering:

* **The deployed proxy was build 3322 while `dist` was 3334**, and a deployed proxy OWNS the pipe —
  `inject.py` refused (`STALE MODULE(S) ALREADY MAPPED`) rather than silently measuring the old
  binary. `py tools/verify/proxy_refresh.py refresh "DQ7R"` fixed it; the deployed file is now
  byte-identical to `dist/proxy/version.dll` (sha `983ade2d`). **10 of the 11 deployed proxies on
  this machine are still stale**, Geri's among them — refresh before any row that uses one.
* **`proxy_refresh.py` printed `(dist 3263)` for every refresh** — a hardcoded literal at `:122`,
  reporting a build it never read. It would have said "3263" while deploying 3334. Now derived from
  `dist/build_number.txt`. A verification tool quoting a number instead of deriving it is the exact
  failure the house rule exists to prevent, and it was inside the tooling.

## ✅ DumperTest fixture extension — SOURCE WRITTEN 2026-08-23, PACKAGED 2026-08-24

**Why this exists.** Four verification rows were parked on *"go find a commercial game that happens
to contain X"*: `AD4` (God Mode `ON (contested)`), `MG2` step 1+2 (`TSet<FName>` / `TSet<UObject*>` /
a `UDataTable`, and an un-capped container that loses an element), `V1a` step 1 (a container that
**reallocates** between two scans), and `V8` (a `UDataTable` with **>64 rows**). Manufacturing the
fixture is strictly cheaper than searching for it, and it is the same move that closed the missing
`FName` row on 2026-08-22 — see working-lessons §1.aa.

⚠ **The source lives OUTSIDE this repo** (`D:\Unreal Projects\DumperTest\Source\DumperTest`), so git
does not carry it and the other PC cannot see it. That is why the change is described here in full.

### ⚠ The CheatManager question, answered correctly (first answer was too broad)

The natural idea — expose the knobs as console commands and type them in-game — **works in two of
the three flavours and not the third**:

```
CheatManagerDefines.h:  #define UE_WITH_CHEAT_MANAGER (1 && !UE_BUILD_SHIPPING)
PlayerController.cpp:1107-1110  APlayerController::AddCheats  <- whole body inside that gate
```

So a `UCheatManager` is live in **Development** and **DebugGame**, and compiles to nothing in
**Shipping**.

⚠ **The first answer given was a flat "no", on the premise that DumperTest is exercised as a
Shipping package. The maintainer corrected it 2026-08-23: much of the past testing actually ran the
DEVELOPMENT exe.** The premise was wrong; the gate quoted above is right. Recorded because the
mistake is the reusable part — *"which build flavour is this claim about"* is a question this repo
has now got wrong twice (the other was the Shipping log-verbosity comment in the same file).

**The design choice is unchanged, and for reasons that survive the correction.** The knobs are plain
`UFUNCTION(BlueprintCallable)` driven through our own `invoke_function` pipe command, because that:

* works in **all three** flavours, so a row's evidence is not silently flavour-scoped;
* needs **no keyboard and no console UI**, so Auto + Computer Use can drive it unattended — a
  console command cannot be typed by a headless pipe rig;
* exercises **our own invoke path**, which is a thing under test, rather than the engine's.

A cheat manager would have been the weaker instrument even where it exists.

### What was added

| file | addition |
|---|---|
| `DumperTestTypes.h` | `#include "Engine/DataTable.h"`; `USTRUCT() FDumperTestTableRow : FTableRowBase` — `int32 Index` / `FName Label` / `float Value` / `FText Caption` (the caption carries the B28 CJK trigger, `\uXXXX`-escaped per the file's own rule). |
| `DumperTestActor.h` | `TSet<FName> Set_Name`, `TSet<TObjectPtr<UObject>> Set_Object`, `TObjectPtr<UDataTable> Table_Small` (8 rows) / `Table_Big` (**100** rows), `TMap<int32,int32> Map_Churn`, `TArray<int32> Arr_Churn`; non-UPROPERTY knobs `bContestDamage` / `ContestWrites` / `TableSerial`; 8 `BlueprintCallable` mutators + `private UDataTable* BuildTable(const TCHAR*, int32)`. |
| `DumperTestActor.cpp` | container seeds + runtime table construction in `BeginPlay`; the AD4 contention writer in `Tick`; the 8 mutator bodies + `BuildTable`. |

**The mutators** (all `Category = "DumperTest|<row>"`):
`MG2_RemoveOneMapEntry` · `MG2_RemoveOneSetEntry` · `V1a_GrowContainers(int32 Count = 64)` ·
`V1a_ShrinkContainers` · `V8_RebuildBigTable(int32 Rows = 100)` · `V8_RemoveOneTableRow` ·
`AD4_SetDamageContention(bool)` · `AD4_GetContestWrites()`.

⭐ **Every row gets its negative control from the same fixture**, which is the point of building it
rather than finding it: `Table_Small` (8) is the un-capped case that must render **no** ">64"
banner; `AD4_SetDamageContention(false)` is the un-contested session that must settle to plain green
`ON`, without which the amber `ON (contested)` reading means nothing; `Map_Churn` starts **under**
the 128 array limit so header-count and row-count are required to agree exactly.

⭐ **`AD4_GetContestWrites` exists so the contest can be shown live rather than assumed** — the same
role `FrameCount` plays in separating "no timer" from "no actor". A badge reading `ON (contested)`
while the counter is flat would be the badge lying, and there would be no way to tell otherwise.

### Two defects found in this code by review, before it ever compiled

Both are recorded because each is a shape worth recognising, not because they survived:

1. **Self-aliasing set removal.** `for (const FName& N : Set_Name) { Set_Name.Remove(N); break; }`
   hands `Remove` a reference **into the element it is about to destroy**. It happens to work today.
   Fixed by copying the `FName` out and removing after the loop.
2. **`NewObject` reusing an explicit name.** `V8_RebuildBigTable` re-created the table as
   `DumperTestTable_Big` under the same Outer, which tears the **old** object down while `Table_Big`
   still points at it. Fixed with a `TableSerial` suffix — which also makes a rebuild *visible* in
   the object list.

### Engine facts verified against UE 5.4 source before relying on them

- `UDataTable::AddRow` / `RemoveRow` / `GetRowNames` are at `DataTable.h:319/316/313`, i.e. **above**
  the `WITH_EDITOR` fence at 321 — runtime table building is real, no cooked asset needed.
- `RowStruct` (line 85) is reachable: `GENERATED_UCLASS_BODY()` leaves the section `public:`.
- `UCLASS(MinimalAPI)` is not a blocker — `StaticClass` is exported and all three methods carry
  `ENGINE_API`, so nothing here becomes a link error at package time.
- `TSet<FName>` and `TSet<TObjectPtr<UObject>>` both have engine UPROPERTY precedent
  (`MetaDataTagsForAssetRegistry`, `TemporarilyReferencedObjects`).

### ✅ The package build is DONE — closed 2026-09-03

Three configs are on disk at `D:\UE_Analyze_Data\For Testing\DumperTest`, all built
**2026-08-24**: `Development` (279,267,328 B), `DebugGame`, `Shipping`. `py tools/ue-sample/capture_package_identity.py <pkg> --project <proj> --check` reports
**"package matches the stored identity"**, i.e. what is on disk is the package this repo
recorded. A live run against it happened 2026-09-03 (build 3369, UE504, 25,213 objects).

⚠⚠ **This closes "was it built", and NOTHING ELSE. Two limits, both load-bearing:**

1. **The four rows' closures were measured against the 2026-08-23 package, and what is on
   disk is a 2026-08-24 re-cook.** No rig has been re-run against it. Their numbers are
   RUNTIME measurements, not constants — `MG2`'s 6/6→5/5, `V8`'s 100 / 77 / 76, `V1a`'s
   `Data 0x18B9D96BC40→0x18BA77009A0`. Symbol presence on the 08-24 build was confirmed;
   behaviour was not.
2. **The 2026-09-03 sessions are inert as evidence for this fixture.** Both were browse-only —
   `get_object_list` / `walk_instance` / `walk_functions` / `walk_world`, and **zero
   `invoke_function`**, which is the only route to the mutators. Every fixture symbol greps to
   0 across all of that day's logs. Do not cite those runs for any fixture row.

⛔ And before re-packaging, read `tools/ue-sample/README.md` rule 3: the live project at
`D:\Unreal Projects\DumperTest` is **weeks behind this repo's mirror** and carries none of the
spawner, so a naive rebuild from it destroys the fixture.

*Superseded text:* Nothing here has been compiled. Build the **Shipping** package to
`D:\UE_Analyze_Data\For Testing\DumperTest`, then the four rows run headless through
`invoke_function`. ⚠ Re-check the escaped caption survives the round trip: it is the one string in
the fixture whose corruption would look like a *B28 defect* rather than like a build problem.

## 🔎 Log-audit findings (P3R session 2026-08-26, build 3360) — all 3 FIXED in build 3362

*Found by a five-lens adversarial sweep of a real P3R session's logs (46 agents, every finding
refute-mandated; 19 of 40 were killed). Two of the four defects it surfaced were fixed the same
day in build 3361 — the `PERF` line's two denominators and the missing re-anchor log line, both
recorded in [dev-log.md](dev-log.md). The three below were fixed in build 3362, each after an
adversarial design review that changed the shape of two of them. Each was independently reproduced
before being written down.*

## ▶ Next up (genuinely actionable now)

- **✅ DONE + IN-GAME VERIFIED (builds 3363 + 3365) — proxy resolvers survive being called before
  our CRT exists, and no longer self-deadlock the loader** —
  OCTOPATH TRAVELER now starts with our `dxgi.dll` and the dumper attaches (2026-08-27, build 3366:
  `dxgi proxy: lazily forwarded 20/20`, pipe server up, UE 4.18, 406,060 objects). Full dossier:
  [audit-2026-08-26-dxgi-appcompat-crash.md](audit-2026-08-26-dxgi-appcompat-crash.md).
  ⚠ **It took two builds, and the reason is worth keeping.** 3363 fixed the crash (AppCompat shim
  calls `SetAppCompatStringPointer` before `_DllMainCRTStartup`; the resolver logged; logging
  allocated on a NULL `__acrt_heap`) — and the game then **hung**. 3365 fixed that: our own
  `LoadLibraryW` re-enters us **on the same thread** (loading the real dxgi raises
  `apphelp!SE_DllLoaded`, `AcGenral` resolves `dxgi.dll` back to US and calls our thunk again), and
  **SRWLOCK is non-recursive**, so the resolver's lock self-deadlocked and the loader lock was
  never released. That lock was audit #4 **B43**, which had removed it from the winmm twin and
  which §8.6 of that doc had recorded as *"deliberately out of scope"*. The deferral was wrong; the
  lesson is logged in [working-lessons.md §2.13](working-lessons.md).
  Rigs left behind: `tools/verify/proxy_precrt_gate.py` (maps with `DONT_RESOLVE_DLL_REFERENCES` so
  DllMain never runs, calls a thunk in a child process — pre-fix faults `0xC0000005` at the same
  RVA the minidumps name, fixed returns cleanly) and `tools/verify/hang_dump.py` (dumps and
  per-thread-triages a *hung* process; `minidump_triage.py` walks the FAULTING thread and a hang
  has none).
  ✅ **All four flavours now covered.** `version.dll` re-verified **in-game on DQ7R** (build 3366:
  `Loaded real version.dll`, UE 4.27, 190,395 objects) — and the marker landing *after*
  `pipe server started` is the direct evidence for the magic-static fix, since the first forwarded
  call came from a game thread and forwarded. `dinput8.dll` **has no host** — no modern UE title
  uses DirectInput8 — so its forward path is covered without a game by
  `proxy_precrt_gate.py --forward`, which does a normal `LoadLibraryW` (DllMain runs) and calls the
  export for real: dinput8 `DllCanUnloadNow` → `S_OK` where our stub answers `S_FALSE`. All four
  PASS; the verdict is the proxy's own log line, not the return value, because a dead forwarder
  answers a *documented failure value* that looks identical to success from outside.
  ⚠ **Genuinely still open:** `dinput8.dll` has never run inside a game, before or after this
  change, and the *pre-CRT* rig provably cannot discriminate pre/post fix for the two plain-C
  forwarder flavours (§9) — their pre-CRT gate is verified by construction only.
  *Parent: [audit-2026-08-26-dxgi-appcompat-crash.md](audit-2026-08-26-dxgi-appcompat-crash.md) §8/§9.*

- **✅ DONE 2026-08-27 — the doc defects the CLAUDE.md audit surfaced** —
  All five fixed, plus two more found while fixing them. One was **refuted**.
  1. ✅ **`pending-verification_zh-TW.md` header duplicated 6×** (313 → 198 lines). ⭐ The cause was
     `tools/verify/zhtw_rebuild_buckets.py` **inserting the charter unconditionally** — every
     `--apply` added another copy. Worse, its `CHARTER` and its `BUCKETS` blurbs were **stale**
     against the hand-corrected doc, so re-running would have re-introduced the old wording and
     deleted the `**目前 0 項。**` notes. Fixed all three: insertion is idempotent (and self-heals a
     duplicated file), and both the charter and the blurbs were back-ported. **Proven**: `--apply`
     on a copy now reproduces the doc byte-for-byte, and three consecutive runs leave the hash
     unchanged.
  2. ✅ `aob-block-library-eval.md` §6 step 3 said the specificity tool is not CI-run. It is.
  3. ✅ `dev-log.md`'s header said the archive boundary was ≤2168; the real one is ≤2747.
  4. ✅ `archive/README.md` was missing a row for `todo-closed-2026-08-25-build-3356.md`, and said
     *five* defects where `handover-2026-08-20.md` §3 says **seven**. ⭐ Fixing it exposed **two
     more unindexed archive files** (`dev-log-2026-05-pre-build-700.md`,
     `dev-log-2026-06-pre-build-1180.md`) — an inventory taken from the README alone had been
     under-reporting the archive. All three rows added; the folder now indexes cleanly.
  5. ✅ `teleport-coord-library-spec.md`'s status said "not yet verified in-game" long after the
     DLL flavour was verified; todo.md owns the status and the spec now points at it.
  ❌ **REFUTED — `corpus-preservation.md` has no internal contradiction.** Its `### Never drop`
  section is about **sole-landing AOB patterns**, not about recoverability, so it does not conflict
  with `### Recoverability — Nothing is unrecoverable`. The stale claim was only ever in CLAUDE.md's
  index row, and that row is gone.
  ⭐ **The pattern worth naming: three stale generators in one day.** `gen_proxy_forwarders.py`
  (winmm), `zhtw_rebuild_buckets.py` (twice — charter and blurbs). Each time a generated file was
  hand-corrected and the generator was not, so re-running it would silently revert the fix. **After
  hand-editing a generated file, back-port or the next `--apply` is a regression.**

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
  level list" and confirm the breadcrumb spine still drills to its HP.
  ⚠ **The `Actors[k]` in the *Original note* below is STALE — do not expect an element index.**
  Audit #5 F8 (build 3220) deleted the `ULevel::Actors` lookup: that field carries no `UPROPERTY`
  (`Level.h:429`), so the `level → actor` hop is **synthetic too** (`field_type: "LevelActor"`,
  `field_offset: -1`, `element_index: -1`), exactly like the `world → level` hop above it. Both
  are navigation anchors, not pointer derefs — see `docs/pipe-protocol.md`'s `ok_via_level` table
  and `dll/src/Aura.cpp:4085-4105`. *Original note:* `Aura::RecoverViaWorldLevel` now recovers a `not_reachable` actor through its owning `ULevel` (reached by the `ULevel::OwningWorld` back-reference, since an actor's Outer IS its level), emitting `world →(WorldLevel)→ ULevel → Actors[k] → actor [→ target]` with status `ok_via_level`. This makes ANY actor that belongs to the current world locatable + navigable in Live Walker (and a bounded tail BFS reaches an owned AttributeSet/HP), regardless of how its level was streamed in — closing the Elliot "weapons map, enemies don't" case. **Verify in-game:** on a streaming/WP title, 🌍 on a just-spawned enemy now lands (status note: "via the world's level list"); confirm the breadcrumb spine reaches the enemy and you can drill to its HP. Two honest residual limits (acceptable, not bugs): (1) the chain is NOT a clean CE static-pointer chain (the world→level hop is a back-reference) — it's for in-tool navigation; (2) a truly unreferenced actor not in ANY world level still returns `not_reachable` (correct). Edel (build 1400) remains the complementary path when the player references the target. *Parent: Related Objects Phase 1 in-game test (dev-log 2026-06-19); recovery dev-log 2026-06-20.*

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

- ✅ **Mimic: zero the ReturnValue slot before invoke — DONE 2026-08-24** (build 3349, `Mimic.cpp`), which is what unblocked `[B636-FASTPATH-2026-08-24]`. ⚠ Scope, measured rather than assumed: the defect was **mailbox-only**. Fern's pipe path already builds a fresh `std::vector<uint8_t> paramBuf(bufSize, 0)` per call (`Fern.cpp:5329`); only the mailbox reuses the persistent global `g_invokeMailbox.paramsData`, which is why a stale input could sit in the return slot. One insertion covers both mailbox branches because it runs before the `isStaticNative` split. Zero, not a `0xCD` sentinel: a partially-filled struct return reads sanely with zeros and as garbage with `0xCD`, and zeroing is what UE itself does. ⚠ Residual: a function that legitimately returns 0 is still indistinguishable by slot contents alone — the slot answers "was it written", not "did it run".
  <details><summary>original entry</summary>

  Effort: **S** · Risk: low. ES2
  showed Before/After dumps identical (stale `0x49`) so we can't tell "wrote 73" from
  "didn't touch ReturnValue". Overwrite the slot with a sentinel / zero before calling PE
  so the After dump is unambiguous. ~2-line patch in `Mimic.cpp` (both fast path + game-
  thread dispatch).

  ⭐⭐ **SECOND, INDEPENDENT SIGHTING 2026-08-23 — and it cost a measurement.** While attempting
  `b636`'s latency half on DumperTest, `Abs(-3.5)` returned `ok:true, result:0` and a parameter
  buffer of `-3.5, 0, -3.5`: the ReturnValue slot simply **mirrored the input**, so "the function
  ran and wrote -3.5" was indistinguishable from "the function never executed". The b636 number was
  discarded rather than published (see `[B636-NOACCIDENT-2026-08-23]`). ⚠ `PointerPanelViewModel`'s
  own docstring already states the hazard in the same words — *"was indistinguishable from a call
  that ran and wrote nothing — the return slot is untouched either way"* — so this is now
  **documented in three places and observed on two titles (ES2, DumperTest)**. It is no longer a
  nice-to-have: it is the thing blocking a verification row. Raising priority.

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
- This file stays canonical (CLAUDE.md's rule). ⛔ **Do NOT "mirror" it into the 繁中 checklist** —
  that instruction was retired 2026-08-22, because mirroring is exactly what turned that file into
  a second copy of this register (31 items, 20 carrying evidence). A row goes there **only if Auto
  + Computer Use cannot complete it end to end**; everything else stays here.

-----

## Live-game verification — moved to its own file

**→ [verification-register.md](verification-register.md)** — everything shipped but not yet proven
against a running game.

It lived here until 2026-09-03 and had reached **10,506 of this file's 13,143 lines (80%)**, which is
why todo.md stopped reading like a todo. It is a different kind of record: the work is already
written and the proof is what is owed, so it has its own lifecycle and does not belong in a list of
what to build next. Its charter — including *why* a manual UI play-test cannot replace it — is at the
top of that file.

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

## 4th proxy DLL — winmm.dll — ✅ SHIPPED build 2317, **archived**

Shipped as a free *slot* (dxgi/version are taken by ReShade and ASI loaders), not for coverage.
The census and rationale moved to
[archive/todo-closed-2026-08-build-2715.md](archive/todo-closed-2026-08-build-2715.md).

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

-----
## Evaluations that used to live here

Two finished evaluations were moved out on 2026-08-25 — they are decisions with verdicts, not open
tasks, and this repo already keeps evaluations as their own files:

* [output-monitor-pin-eval.md](output-monitor-pin-eval.md) — can a game with no monitor-select UI
  be pinned to one screen? (EVALUATED 2026-07-23, **NOT BUILT**)
* [ue-perf-counters-eval.md](ue-perf-counters-eval.md) — UE performance counters in the UI
  (EVALUATED 2026-07-23, tiered; Tier 0 **WON'T DO**)

Nothing was edited, only moved.

-----

## ✅ A7 CLOSED — but the 2026-08-25 closure was against the WRONG LOOP; really closed 2026-09-06 `[A7-FINDBYADDR-2026-09-06]`

> ⛔ **CORRECTION 2026-09-06, and it is the reason to distrust a green claim you did not derive
> yourself.** `[A7-CORETEST-2026-08-25]` below is right about everything except *which loop it
> tested*. The two `dll_core_test` blocks it added are labelled "A7" and drive **`Aura::ForEach`** —
> but ForEach **already had its poll**; it is one of the *siblings* A7 was written to match, and
> A7's own comment (`Aura.cpp:1892-1894`) names them. The loop A7 actually fixed is
> **`FindByAddress`** (`Aura.cpp:1867`), which hand-rolls `for (int32_t i = 0; i < count; ++i)` and
> never calls ForEach. The audit row said so plainly:
> *"`FindByAddress` is the **only** full-GObjects walk in the file with neither a `Tot::Requested()`
> poll nor a deadline"* (`docs/audit-2026-08-13-early-code-findings.md:278`).
>
> Measured 2026-09-06: `grep FindByAddress dll/tests/ tools/verify/` returned **zero hits**. The
> shipped fix had **no coverage of any kind** while this file and the register both recorded it as
> verified — a green claim computed by a different code path than the thing it claims about, which
> is the same shape as `[SEINSHARE-2026-09-05]` and the A2 vacuous-absence trap.
>
> **NOW REALLY CLOSED**, four checks in `dll_core_test` driving `FindByAddress` itself: an in-pool
> address at index 8000 is found as an **exact** match uncancelled, the *same* address returns
> `found == false` / `index == -1` under `Tot::g_perCommand`, and is found again after a reset.
> ⭐ **Proven non-vacuous by deleting the poll**: without it the block fails with
> `...reports no index rather than a stale one   got: 8000` — it found the object *while cancelled*.
> With the poll restored (byte-identical to HEAD), 30/0.
>
> ⚠ The anti-vacuity design matters here: an **uncancelled** lookup of an address that is not in the
> pool *also* returns `found == false`, so "not found" alone proves nothing. The assertion is the
> **flip of one fixed address under one changed flag**, which only means anything because the
> positive control establishes the address is findable first.

<details><summary>the 2026-08-25 entry (kept — its infrastructure reasoning is sound and is what made tonight's fix a 30-minute job)</summary>

**A7's live half CLOSED 2026-08-25 `[A7-CORETEST-2026-08-25]` — and the DLL core finally has a test target**

A7's fix (the `(i & 0xFFF)==0 && Tot::Requested()` poll in the GObjects walk) shipped long ago; what
stayed open was verifying it, and it was filed **blocked** for a measured reason: on every title
here the walk is far too fast to cancel by hand — DQ7R's 149,408 objects take 152 ms, OCTOPATH's
273,956 take 0.11 s.

⭐⭐ **The blocker was the OBSERVER again, and the fix was infrastructure the repo has wanted for
months.** `Macht` reads the **current process**, so a fake `FUObjectArray` built in a test's own
memory is, to `Aura`, indistinguishable from a real one. New target **`dll_core_test`** compiles
`Macht` + `Serie` + `Ubel` + `Radar` + `Denken` + `Flamme` + `Aura` + `Genau` — **~23,000 lines that
no test target had ever compiled** — and points `Aura::Init` at that fixture.

**The external surface was MEASURED by an incremental link probe, not guessed**, and it is small:
`Sein` ×5, `Stark::SetInvokeTimeoutMs`, and `g_cachedUEVersion` (defined in `Frieren.cpp`, the C ABI
layer). Defining the last one in the test is a *feature* — the test chooses the UE version the core
branches on. Zydis and `version.lib` come from CMake.

⚠ **The layout is FORCED, not detected** (`InitWithExtendedLayout`). A fixture whose layout was
auto-detected would be testing the detector, and a detector that guessed wrong would yield a pool of
zero objects — which reads exactly like *"the walk was cancelled"*. That is why the first case is a
**positive control**: `GetCount() == 16,384` and an uncancelled `ForEach` visiting all 16,384.
Without it every assertion below would pass against an empty pool.

| case | result |
|---|---|
| cancel set BEFORE the walk | 0 objects visited — the poll at `i == 0` fires |
| cancel set from INSIDE the callback at `i == 100` | stops at **exactly 4,096** — the next poll boundary, strictly after the cancel and strictly before the end |
| a fresh walk afterwards | all 16,384 again — the cancel is not sticky |

⚠ **Negative control:** replace the poll with `if (false)`. **3 assertions red, each reporting
`got: 16384`** — the walk runs to completion in all three cases. Reverted; `Aura.cpp` byte-identical
to HEAD.

ℹ️ **What this does NOT close, stated plainly.** The pool size is 16,384 and the poll granularity is
4,096, so this pins the MECHANISM, not the wall-clock responsiveness the row's prose talks about
("prompt shutdown instead of a multi-second hang"). Latency on a 500k-object commercial title is
still unmeasured — but the property that produces it is now covered, and it is covered by something
that can fail.

✅ **B18 CLOSED the same way, 2026-08-25 `[B18-CORETEST-2026-08-25]`.** Same shape — a
`Tot::Requested()` poll, in `Genau::ScanForTarget`'s AOB **batch boundary** — and the same reason it
was blocked: the scan finishes faster than a person can cancel it.

⭐ The (MA1) comment beside the poll is what shaped the test. Cancellation sits at the pattern
boundary and deliberately **not** inside `Macht`, because the largest indivisible unit is one
`AOBScanBatch` (measured ≤0.64 s on a 213 MB `.text`). So the thing to test is **not a duration** —
it is that the poll is consulted and that the report declares the results partial, which is what its
own log line demands (*"results are partial and MUST NOT be published"*).

The scan runs against the TEST PROCESS's own modules, with a pattern that cannot match, so the
uncancelled run is a full scan that finds nothing rather than an early success.

| case | result |
|---|---|
| uncancelled | `report.cancelled == false`, returns 0 |
| cancel pending before the call | `report.cancelled == **true**`, and **returns no address** |

⚠ **The control looked vacuous and I checked rather than assumed.** The whole run takes **0.08 s**,
which reads exactly like *"the scan never happened"*. It did — the test process simply has few,
small modules. The proof is the negative control, not the duration: replacing the poll with
`if (false)` reddens the cancelled case, which can only happen if the loop **reaches that line**.
Both cases take the same path up to the poll, so a control that passes there is a control that ran.
That reasoning is now written into the test, next to the assertion it justifies.

</details>
