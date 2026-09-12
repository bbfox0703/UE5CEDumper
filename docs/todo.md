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
> - [verification-register.md](verification-register.md) — **9 open batches** needing a running game (moved out 2026-09-03;
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

## ⛔ THE BLANK SWEEP — `[BLANK-0601-PLAN-2026-09-10]` the 50,451 lines no audit ever scoped

**Status: ✅ ALL FIVE WAVES SWEPT 2026-09-10 — 39 distinct defects confirmed (2 HIGH · 19 MED · 18 LOW), NONE repaired.** The whole-sweep table is at the end of `[BLANK-W5-2026-09-10]`. This section is the batching
plan and the resume ledger; a fresh session should start by reading the ledger table.

### What this is, and why it is the largest open item in the repo

`[A4-ASSESS-2026-09-09]` measured the coverage timeline and found a **31.3% hole**: every line
authored between **2026-06-01 and 2026-07-03** falls outside every audit's scope *by
construction*. Audit #5's predicate is "authored before 2026-06-01"; audit #3's window opens
2026-07-03. Nothing covers what sits between them.

Derive it, never quote it:

```
py tools/verify/audit_coverage.py timeline
py tools/verify/blank_sweep.py clusters
```

At HEAD (2026-09-10) that is **50,451 surviving production lines across 211 files** — and
**14,095 of them sit in 110 files that not one of the six audit documents so much as names**.

⭐ **It is real code, not rename churn.** 74.2% of the band sits in hunks of **≥25 contiguous
lines** (37,457 lines / 452 hunks); only 3.8% is 1–3-line fragments. Measured, not assumed —
the fragmentation histogram is what decided that this is worth an audit at all.

### The batching table — 14 clusters, and why clusters

⛔ **Not "N files per agent".** Every defect the 2026-09 fix pass actually found sat in the
**wire** — a field the DLL never creates, a consumer that never asks — where no per-file read
can reach. A batch that carries the DLL side, the pipe command and the UI consumer *together*
is the only shape that can see those. Line count is the **secondary** key.

⚠ **`band%` decides the agent's diet.** It is the share of the files an agent must open that is
actually never-audited code, and it runs **8% to 95%**. Handing an 8% cluster's whole files to
an agent means reading 5,613 lines to audit 451 — and then reporting findings in the 92% that
five earlier audits already cleared. Below 40% the agent gets **hunks**
(`py tools/verify/blank_sweep.py hunks <CLUSTER>` prints the exact line ranges); at or above,
whole files.

| cluster | band | whole | files | band% | UNNAMED | diet |
|---|---:|---:|---:|---:|---:|---|
| SNAPSHOT | 5,358 | 6,236 | 14 | 86% | **79%** | whole |
| PIVOT-SPC | 4,581 | 4,805 | 18 | 95% | **81%** | whole |
| TELEPORT | 8,084 | 12,415 | 19 | 65% | 15% | whole |
| AURA-GRAPH | 5,581 | 12,701 | 4 | 44% | 3% | whole |
| LIVEWALKER | 4,199 | 10,427 | 9 | 40% | 5% | whole |
| VALUESEARCH | 3,631 | 7,173 | 10 | 51% | 29% | whole |
| WIRE | 3,618 | 13,382 | 12 | 27% | 0% | hunks |
| EXPORT | 3,814 | 11,700 | 9 | 33% | 2% | hunks |
| APP-SHELL | 4,926 | 12,865 | 31 | 38% | 41% | hunks |
| OBJTREE | 2,676 | 9,940 | 23 | 27% | 12% | hunks |
| SCAN-CORE | 1,809 | 19,446 | 14 | 9% | 0% | hunks |
| WIRE-DTO | 1,220 | 3,508 | 31 | 35% | **79%** | hunks |
| CE-BRIDGE | 451 | 5,613 | 6 | 8% | 0% | hunks |
| DLL-OTHER | 503 | 3,985 | 11 | 13% | 9% | hunks |
| **TOTAL** | **50,451** | **134,196** | **211** | 38% | 28% | |

⚠ These numbers are **derived** — `blank_sweep.py clusters` re-prints them and fails loudly if
any file matches no rule. Do not hand-edit the table; re-run it.

### The five waves — sized for the 5-hour session limit

⛔ **ONE WAVE PER SESSION is the plan.** Run a second only if the first closed with obvious
headroom. **Never start a wave you cannot also finish, adjudicate and commit** — a wave killed
mid-flight loses its finders' work, and the finders are the expensive half.

| wave | clusters | band | finders | retires |
|---|---|---:|---:|---|
| **W1** | SNAPSHOT · PIVOT-SPC · WIRE-DTO · CE-BRIDGE | 11,610 | 4 | **63% of all UNNAMED lines** |
| **W2** | TELEPORT ×2 · VALUESEARCH | 11,715 | 3 | the area that already yielded 3 real defects |
| **W3** | APP-SHELL ×2 · OBJTREE | 7,602 | 3 | the UI shell + browse surfaces |
| **W4** | AURA-GRAPH ×2 · LIVEWALKER | 9,780 | 3 | the two big DLL cores |
| **W5** | WIRE · EXPORT · SCAN-CORE+DLL-OTHER | 9,744 | 3 | the remainder |

**16 finder agents total.** `TELEPORT ×2` / `APP-SHELL ×2` / `AURA-GRAPH ×2` are splits of one
cluster, not two batches: **both halves get the same brief and the same file list**, one reading
*DLL-publishes → UI-consumes* and the other the reverse. Splitting a cluster by file would
destroy the very cross-side visibility the clustering exists for.

⭐ **W1 first on purpose.** SNAPSHOT (79% unnamed), PIVOT-SPC (81%) and WIRE-DTO (79%) are the
code nobody has ever even mentioned, and `Models/` is exactly where "a field the DLL never sets"
lives. One wave retires 8,936 of the 14,095 never-named lines.

### Per-wave protocol

1. **Finders** — one agent per cluster (or half-cluster), all launched together.
2. **Refuters** — findings go out in batches of ~4 to adversarial refuters prompted to
   **default to refuted**. ~3 per cluster; ~10 per wave.
3. **I adjudicate the survivors by hand.** ⚠ Not optional and not a formality: on 2026-09-09 I
   hand-checked four phase-2 findings' *structure* correctly and got **all four verdicts wrong**
   (§1.w4 — the consequence decides, not the structure), and one of them is on the "do not
   re-raise" list twice.
4. **Write the ledger row + the fix list. Commit.** ⛔ Then stop, whatever time is left.
5. ⛔ **Record, do not fix.** Same discipline as the four-phase pass: a fix round against the
   *complete* list is cheaper and lands clean. Repairs come after W5.

### The brief every finder gets — non-negotiable

Calibration is the whole ballgame: the last agent sweep ran **~4 refuted for every 1 real**.

1. **Report the EFFECT, not the ATTEMPT.** A finding with no consequence is not a finding.
2. **§1.w4** — a discarded return is a *structure*, not a defect. Name what breaks for a user.
3. **The refuted lists are binding**: `audit-2026-08-04-findings.md` (bottom),
   `audit-2026-08-13-early-code-findings.md` §3 + §3345, `audit-2026-09-05-vendor-ue582.md`
   §424, `audit-2026-08-26-dxgi-appcompat-crash.md` §7. **Do not re-raise them.**
4. **Anti-vacuity**: "X is ABSENT" is satisfied for free by an empty run. A missing host is
   UNDECIDED, never a pass.
5. **Scope is the band's line ranges** — code outside them was cleared by five earlier audits.
   ⭐ **The one exception is a wire defect with one side in-band**: those are exactly what this
   sweep exists to find, and they cross the boundary by nature.
6. Every finding names **file:line · the effect · a failure scenario with concrete inputs ·
   whether any test or gate would have caught it**.

### Ledger — a fresh session resumes from here

⬜ pending · 🔄 running · ✅ swept (fix list written, committed)

| wave | status | finders | raw | survived refutation | confirmed | commit |
|---|---|---|---|---|---|---|
| W1 SNAPSHOT · PIVOT-SPC · WIRE-DTO · CE-BRIDGE | ✅ | 4 | 16 | 13 | **2H 4M 7L** | 2026-09-10 |
| W2 TELEPORT ×2 · VALUESEARCH | ✅ | 3 | 13 | 10 | **0H 6M 4L** | 2026-09-10 |
| W3 APP-SHELL ×2 · OBJTREE | ✅ | 3 | 8 | 6 (**5 distinct**) | **0H 3M 2L** | 2026-09-10 |
| W4 AURA-GRAPH ×2 · LIVEWALKER | ✅ | 3 | 12 (**10 distinct**) | 6 (+1 undecided) | **0H 5M 1L** | 2026-09-10 |
| W5 WIRE · EXPORT · SCAN-CORE+DLL-OTHER | ✅ | 3 | 8 | 5 | **0H 1M 4L** | 2026-09-10 |

⚠ **A `⬜` here is evidence; a heading anywhere else in this file is not** — see the 2026-08-24
reconciliation at the top. This table is updated in the same commit as the wave it describes,
which is what makes it trustworthy.

### ✅ W1 SWEPT 2026-09-10 `[BLANK-W1-2026-09-10]` — 16 raised, 13 confirmed, 3 refuted

4 finder agents over **11,610 never-audited lines / 69 files**, then 7 adversarial refuters
(default verdict REFUTED), then hand-adjudication. **Nothing was fixed** — record-only, per the
plan; repairs come after W5.

| | HIGH | MED | LOW | REFUTED |
|---|---|---|---|---|
| SNAPSHOT | SNAP-1 | — | SNAP-2 · SNAP-4 · SNAP-5 | SNAP-3 |
| PIVOT-SPC | — | PIV-1 · PIV-2 · PIV-3 | PIV-4 · PIV-6 | PIV-5 |
| WIRE-DTO | DTO-1 | DTO-3 | DTO-2 | — |
| CE-BRIDGE | — | — | CEB-1 | CEB-2 |

⚠ **CALIBRATION UPDATE, AND READ BOTH WAYS.** The brief told every agent to expect roughly **four
refuted for every one real** — the rate the last agent sweep ran at. W1 came back **13/16
confirmed**. Two readings, and the evidence favours the first:
1. **The blank was genuinely unswept.** These files had never been read by anyone; ordinary
   first-pass defects were simply still there. Two of the confirmed rows have their **origin
   commit inside the band** (PIV-2: Class Pivot shipped `e554639c` 2026-06-02, the session gate
   landed `534314f4` 2026-06-16 naming only Snapshot/SPC).
2. The refuters were soft. **Against this:** 3 outright refutations, three MED→LOW downgrades,
   four findings materially narrowed, and — the tell that they were not rubber-stamping —
   **SNAP-5's refuter proved the finding's own implied fix would be actively harmful.**

⭐ **Two of the seven refuters MEASURED instead of arguing**, which is why their verdicts are
worth more than the others: DTO-1's ran the serializer round-trip with three controls, and
CEB-1's decompiled the shipped `System.IO.Pipes.dll` to read `NamedPipeClientStream.TryConnect`.

#### The fix list — 13 rows, none repaired

**HIGH**

1. ✅ **`[W1-SNAP-FAULT]` a faulted snapshot chunk is stored and finalised as a complete, usable
   snapshot.** `dll/src/Aura.cpp:9804` is the **only one of seven** `ParallelGObjectsScan` call
   sites that does not fold the fault flag into a published stat — the other six all do:
   ```
   2800  stats->deadlineHit    = scan.incomplete();
   3052  stats->deadlineHit    = (scan.deadlineHit && matches.empty()) || scan.workerFaulted;
   3884  bool deadlineHit      = scan.incomplete();
   5978  out.stats.deadlineHit = scan.incomplete();
   6133  out.stats.deadlineHit = scan.incomplete();
   8251  result.stats.deadlineHit = scan.incomplete();
   9919  if (scan.workerFaulted)          <- the snapshot path: a log line, nothing else
   ```
   `Aura.cpp:9776` sets `result.scanned` to the FULL range regardless, so the C# pager steps over
   the hole; `SnapshotChunkResult` (`Aura.h:1695`) has no field to carry it and `Fern.cpp:2035`
   publishes none. `CompleteSnapshotAsync(..., !driftDetected, ...)` therefore writes
   `is_usable=1`, which means no ⚠ badge and auto-selection as a Diff/Group/SPC/Pivot source.
   ⭐ **This was KNOWN AND LEFT.** `git show -s e6360903` line 25, verbatim, and repeated as a
   comment at `Aura.cpp:9916-9918`: *"…still listening, so that chunk is about to be STORED with
   a hole in it."* The D2 fix shipped a corrected `Sein::Warn` string for it and no channel.
   ⚠ Not vacuous: `SnapshotChunkSize = 8192` vs `ScanThreadCount`'s `if (workItems < 8192)
   return 1` — 8192 is **not** < 8192, so a full chunk always runs multi-threaded and a fault
   leaves a genuinely PARTIAL result, not an empty one (`sw1_worker_fault.py`'s own TRAP 2).
   Reachable in production, not only from the harness: `UE5_SetObjectDecryption` is a shipped
   export on all four proxies and `docs/reversing-nonstandard-ue-games.md:175` documents the
   supported flow as the user passing in their own reversed routine, which `Aura.cpp:322` calls
   raw and unguarded under `/EHa`.
   **Fix shape** (from the finder, and it reuses machinery both sides already have): add the flag
   to `SnapshotChunkResult`, publish it, carry it on the C# DTO, OR it into the producer's
   usability verdict. ⛔ **Do not reuse `deadline_hit`'s wording.** D2's deliberate residual (the
   `⬜ Still open from the sweep` list after blind-spot sweep round 3) already records that mislabelling.
   *(This pointer first read "`docs/todo.md:1738`", a line number that had already drifted.)*
   ✅ **FIXED IN SOURCE 2026-09-10 — the fix pass's second row, in exactly the recorded shape.**
   - **DLL:** `SnapshotChunkResult` carries `workerFaulted`, set where `CaptureSnapshotChunk`
     used to only log the fault (`Aura.cpp`). `Fern` publishes it as `worker_faulted`, its own key
     and never `deadline_hit` wording (`docs/pipe-protocol.md` updated).
   - **UI:** the DTO parses it; an absent key on an older DLL reads as false. The capture ORs it
     into the usability verdict: `CompleteSnapshotAsync(..., isUsable: !driftDetected &&
     !faultDetected)`.
   - **Behaviour on a fault:** the capture keeps what it captured but stops at the faulted chunk. A
     faulting decrypt stub is usually deterministic, so later chunks would fault too.
   - **Status text:** names a worker FAULT, never a deadline or a cancel.
   - **Red before green:** `Capture_WorkerFaultedChunk_IsFinalisedUnusable_AndSaysFault` failed
     first (*"a faulted chunk must NOT be finalised usable, got True"*), then passed. Its control,
     `Capture_CleanChunks_StayUsable`, passes both ways.
   - **Results:** UI tests **4804 / 4804**; gates **21 / 21**; `build.ps1 -Target DLL
     -NoBumpBuildNumber` **SUCCESS** (`dist\UE5DumpUI.exe` left AOT, 54.8 MB).
   - **No DLL unit test:** `dll_core_test` can fault `ParallelGObjectsScan` only through its
     lambda, not inside `CaptureSnapshotChunk`. So the DLL half is proven by the build and by live
     check L2.
   - ⬜ **Live check deferred:** backlog L2 in `[FIXPASS-2026-09-10]`.

2. ✅ **`[W1-QUOTA-UNLIMITED]` "Unlimited" snapshot quota is never written, silently reverts to
   1 GB, and FIFO-deletes the user's snapshots.** `ExperimentalSettings.cs:31` sets
   `DefaultIgnoreCondition = WhenWritingDefault`, which compares against `default(int) == 0`, not
   against the `= 1024` initializer at `:19`. **MEASURED** on the real source-generated context:
   ```
   Unlimited (0): wrote Enabled=True SnapshotQuotaMb=0
                  { "enabled": true }
                  reloaded => Enabled=True SnapshotQuotaMb=1024
   controls: 2048 -> 2048   1024 -> 1024   512 -> 512
   ```
   `ExperimentalGate.cs:69` clamps only negatives, so 0 survives to `Save`. Eviction is permanent
   (`SnapshotStore.cs:787` skips only on `<= 0`; `:829-836` DELETEs in a committed transaction).
   ⛔ **It violates a written spec rule**: `docs/teleport-coord-library-spec.md:618` —
   `DefaultIgnoreCondition` **MUST NOT** be `WhenWritingDefault` where a legitimate type-default
   value can be saved. Five sibling sites already know the trap by name
   (`UiOptionsSettings.cs:22-25`, `CoordinateLibraryFile.cs:215`, `CoordinateLibraryStore.cs:23`,
   `CoordinateLibraryTests.cs:226`, `UiOptionsStoreTests.cs:123`).
   ⭐ **Blast radius checked and it is a lone case** — the other two context-level users are safe:
   `ClassDenylistSettings` holds `List<string> = new()` (reference-type default is `null`, an
   empty list is still written), and `AobUsageRecord`'s ints/bools carry no initializer at all
   (its only initialized non-string is `Version = 1`, and 0 is never a legitimate version).
   `AobMakerMessage`'s per-property `[JsonIgnore]`s are outgoing wire fields, not persisted state.
   ✅ **FIXED IN SOURCE 2026-09-10 — the fix pass's first row (HIGH first).**
   - **The fix:** `ExperimentalSettingsJsonContext` no longer sets `DefaultIgnoreCondition =
     WhenWritingDefault`, the spec-conforming repair. `AobUsageFile.Version` is marked
     `[JsonIgnore(Condition = Never)]`, which is behaviour-neutral and states the intent.
   - **The gate:** `tools/check_json_default_ignore.py` is now registered in `check_all.py`.
   - **Red before green:** `ExperimentalGateTests.SnapshotQuotaMb_Unlimited_Zero_SurvivesARoundTrip`
     failed first, *"Expected: 0 / Actual: 1024"*, the exact defect, then passed after the fix.
   - **Results:** UI tests **4802 / 4802**; `check_all.py` **21 gates, 0 failed**.
   - ⬜ **Live verification is deferred to the end of the fix pass**, as the maintainer directed:
     1. In an AOT build, pick *Unlimited*, restart the UI, and confirm `experimental.json` carries
        `"snapshotQuotaMb": 0` and the combo still reads Unlimited.
     2. Confirm no snapshot is FIFO-deleted on the next capture.
   - ⚠ **Not covered by this fix:** the escalation's second, latent hazard (`MbToLabel` maps a
     non-preset quota to "1 GB", and nothing pins it to `AutoSnapshotPlanner.QuotaPresetBytes`). It is
     still open; see below.

**MED**

3. ✅ **`[W1-SPC-JOINMODE]` SPC persists its own auto-chosen join mode and replays it as a fake
   user override.** `SpcQueryViewModel.cs:521`. Opening the SPC tab auto-ticks the two newest
   picks (`:472-478`) and calls `AutoSelectJoinMode` (`:481`), so `In-session` reaches
   `ui-options.json` with **zero user action**. On restart `ApplyOptions`
   (`MainWindowViewModel.cs:2532`) writes it through the public setter; the value differs from the
   `Strict` default so the changed-hook fires with the programmatic flag false and latches
   `_joinModeUserOverride`, which has exactly one write and one read and is never reset.
   `AutoSelectJoinMode` then early-returns forever and the documented cross-session fallback to
   Strict is dead. `docs/experimental-snapshot-spc-pivot.md:448` states the In-session key is only
   valid *"while the object lives"* — using it across launches joins on GObjects slot number.
   ⚠ MED not HIGH: the mode IS visible (combo, status line, per-pick `SessionShort`).
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B15).
   - Auto-selection only ever picks In-session or Strict, so the only fake override ever came from a
     persisted **In-session**, and In-session is launch-scoped by definition. It is now never written
     (`JoinModeForOptions` writes Strict in its place) and never restored
     (`RestoreJoinModeFromOptions` drops it, and anything outside `JoinModeOptions`).
   - A persisted **Loose** can only be the user's, so it still restores as an override, exactly as a
     combo pick does.
   - `MainWindowViewModel`'s `ApplyOptions` / `BuildOptions` call the two members.
   - **Tests.** The wiring lives in `MainWindowViewModel`, which no test constructs, so the red-first
     test is a SOURCE pin (`JoinMode_OptionsWiring_GoesThroughTheSessionSafeMembers`). The members'
     behaviour tests cannot compile before the fix, so their red is the mutation check:
     - an auto-chosen In-session is persisted as Strict;
     - a restored In-session no longer blocks the cross-session fallback;
     - a restored Loose is kept;
     - an unknown value is ignored.
   - ✅ **Review follow-up 2026-09-11** (adversarial review of B14–B21, `spc-restore-doc-strict`, LOW).
     - "The only fake override ever came from a persisted In-session" was incomplete. **Strict** is
       also what an auto In-session is written as, and the default and the auto fallback besides, so a
       persisted Strict cannot be told from a pick nobody made.
     - Restoring it through the setter latched the override. That was latent only because the one
       call site runs while the combo already holds Strict.
     - `RestoreJoinModeFromOptions` now skips Strict like In-session, so the doc holds in every order.
     - Restoring Strict after an auto In-session went red first. 1/1 mutants killed; UI 5088/5088.

4. ✅ **`[W1-PIVOT-SESSION]` Class Pivot row handoffs have no cross-session gate.** (FIXED IN SOURCE 2026-09-11, batch B14)
   `ClassPivotViewModel.cs:165/169` are `SelectedResult != null`; `_engineState` is assigned at
   `:226` and **read nowhere**, while both siblings compare `state.GameSessionId`. Open in Live
   Walker / Copy Address / the two Locates hand a dead process's `obj_addr` to the running game;
   `NavigateToAddressAsync:2812` validates format only and `CopyAddressAsync` validates nothing.
   Worse, `RefreshAsync:501-503` defaults to `Snapshots[0]`, so right after a reconnect the
   default selection IS a previous-launch snapshot. **Historical omission, not a decision** —
   `534314f4` (2026-06-16) added the gate to exactly four files, all Snapshot/SPC, and Class Pivot
   had shipped in `e554639c` (2026-06-02).
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B14, with the P6 detector registered in the same commit).
   - `CanUseResultRowActions` requires a selected row AND results that belong to the live session
     (`_currentSessionId`: set in `SetEngineState`, cleared in `ClearOnDisconnect`), the shape
     Snapshot Diff and SPC use. `CanLocateResult` / `CanLocateResultInGWorld` read it, and Open in
     Live Walker and Copy Address now bind it as `IsEnabled`.
   - The session is STAMPED on the results when they are set, not read from the picker. A DataTable
     run walks the live process whatever the snapshot picker shows, and the picker can move after
     a run.
   - `tools/check_session_gate.py` is registered in `check_all.py`, and the tree is 20/20 gated.
   - **Tests, red first:** `RowHandoffs_AreDisabled_WithNoLiveSession`,
     `RowHandoffs_AreDisabled_ForAPreviousLaunchSnapshot` (which also pins that the post-connect
     DEFAULT is the old launch) and `RowHandoffs_Close_OnDisconnect`.
     - Controls: the current launch, and DataTable rows under an old snapshot pick. The DataTable
       one is what kills a gate keyed on the picker.
     - The AE10 test keeps its point (the client GWorld flag plays no part) under a same-session
       connect; "selection is the only precondition" is exactly what this row ends.
     - `SetEngineState` now keeps its refresh task (`PendingRefresh`), a behaviour-neutral test seam
       as in SnapshotViewModel.

5. ✅ **`[W1-DISCOVER-ARRAY]` "Use →" on a struct-array discovery candidate ticks nothing.** (FIXED IN SOURCE 2026-09-11, batch B16)
   `ClassPivotViewModel.cs:1122`. `BuildDiscoverSql` puts `array_field, elem_index` in the
   identity key and renders `Array[N].Inner`, but `ListPivotFieldsAsync` is `… AND array_field IS
   NULL`, so the name can never match; both lookups return null with no branch that reports it,
   the class match still succeeds, and the pivot runs with 0–3 unrelated pre-ticked fields and a
   status line that reads like success. Array rows rank HIGH by construction —
   `SelectivityWeight = 3.0` rewards exactly the few-instance change an array element produces,
   and `PivotDiscoveryEngine` contains no occurrence of "array" anywhere.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B16, one commit with `[W1-ARRAYCOUNT]`).
   - Discovery already read `array_field` and the inner prop separately, and flattened them into
     the display name. `DiscoveryInput` / `DiscoveryCandidate` now carry `ArrayField` + `InnerProp`,
     and the engine copies them from the group's representative.
   - "Use →" on an array element pivots it through the **Snapshot Array** source: the class, then
     the candidate's OWN array (the array-field load auto-selects the first one), its inner prop,
     then Run.
   - Every lookup that finds nothing says so and runs nothing, the scalar path included: a pivot of
     the pre-ticked fields under a normal status line read as the candidate's own result.
   - Left as is: array elements still rank high by construction (`SelectivityWeight` rewards their
     few-instance change). That is ranking, not this row's silent failure.
   - **Tests, red first:** "Use →" on `Cargo[1].Quantity` (the fixture's `Bags` array sorts first, so
     the auto-pick is the wrong one) and the scalar "Ghost" prop. The two array refusals construct a
     candidate with the new fields, so they cannot compile before the fix; their red is the mutation
     check.
   - ✅ **Review follow-up 2026-09-11** (adversarial review of B14–B21: `discover-array-keyless` MED and
     `pivotfor-silent-miss-twin` LOW, both CONFIRMED).
     - **Keyless elements collapsed into ONE group.** `PivotArrayAsync` gave every element with no inner
       key (no FName / integer key, or a leaf container) the constant key `"(no key)"`. All elements of
       all owners fell together, under a status line saying "elem index group(s)". B16's Use → sent that
       population into it.
     - A keyless element now groups by its own index (`[N]`), which is what the status line and the
       array picker ("key = (elem index)") already said. The collapse itself dated from 0b2abf1f (C6).
     - **The C5 right-click handoff was the same silent miss, one caller over.** `PivotForAsync` said
       "Ready: … press Run Pivot" for a prop the shared helper never ticked (right-click hands off ANY
       field). It now says the prop is not pivotable. A prop that is the key field still reads Ready.
     - **Tests, red first:** a keyless two-owner array, and a handoff of an uncaptured prop.
       2/2 mutants killed; UI 5087/5087.
   - ✅ **Second review follow-up 2026-09-11** (the second adversarial review, of 4880a779: two LOW).
     - **"A prop that is the key field still reads Ready" held in Field mode only**
       (`pivotfor-identity-keyfield-silent-miss`, CONFIRMED).
       - A class with no good key opens in Identity mode, with the key picker on its alphabetically first
         field. That field groups nothing.
       - The shared helper still skipped ticking it, so a handoff of that very prop left it out of the pivot
         under a "Ready" status.
       - The helper now skips the key pick only in Field mode.
     - **"Only captured numeric fields can be pivoted" was false for a struct array**
       (`pivotfor-array-field-misreported-unpivotable`, PLAUSIBLE). A captured struct array pivots under the
       Snapshot Array source.
       - `PivotForAsync` now asks the store for the class's captured arrays before it says a prop cannot be
         pivoted, and points a struct array at that source.
       - It does not switch the source itself: the handoff still only prepares.
     - **Tests, red first:** an Identity-mode handoff of the key pick, and a struct-array handoff. A
       separate control pins that the keyless fixture really opens in Identity mode on "Amount", so a
       fixture drift fails there rather than passing the red test for free. 2/2 mutants killed; UI 5103/5103.
   - ✅ **Third review follow-up 2026-09-12** (review 3, of c294e314: two LOW, both CONFIRMED. A third
     finding, about the field-list pre-tick, was refuted as outside this row).
     - **An element handoff missed the redirect.** Value Search hands off a struct-array inner value by
       its display name (`Cargo[3].Quantity`, Radar's `FieldDisplayName`). That name matched no array, so
       the handoff still said "not a pivotable field". The part before the `[` now names its array
       (`ArrayFieldOf`).
     - **A class with only struct arrays was called missing.** With no scalar field it has no row in the
       class list, so the helper said it "is not in the selected snapshot". `PivotForAsync` now asks the
       store for its arrays and points them at the Snapshot Array source.
     - **Tests, red first:** an element-named handoff, and a class with no scalar field. 2/2 mutants
       killed; UI 5132/5132.
   - ✅ **Review 4 follow-up 2026-09-12** (of c5511519: LOW, CONFIRMED). The arrays-only branch ignored the
     handed-off prop. It named the first array BY NAME (the store orders by `array_field`), so
     ("CargoHold", "Cargo[3].Quantity") was sent to "Ammo". A prop that is no array at all got the same
     line, and was never told it is not pivotable. `ArraysOnlyStatus` now reads the prop as the class-found
     branch does: its own array when it names one; otherwise "not a pivotable field", with the class's
     arrays still pointed at. Red first with a two-array class; 4/4 mutants killed; UI 5154/5154.

6. ✅ **`[W1-CONTAINER-STALE]` TMap/TSet/TArray previews are frozen at the first walk — and the
   staleness reaches EXPORT.** `LiveFieldValue.cs:294/324`. `UpdateDisplay` takes the in-place
   `CopyLiveValuesFrom` branch on every same-object Refresh (`LiveWalkerViewModel.cs:6524-6544`),
   and `MapElements`/`SetElements` are `init`-only and absent from the copy list — they cannot
   even be assigned there. The DLL re-emits both on every walk (`Fern.cpp:1621-1641`, `:1657-1669`)
   and `DumpService` parses them; they are dropped. The class header states the broken invariant
   itself (`:104-106`).
   ⭐ **Two independent mechanisms, same symptom**: `ArrayElements` IS copied, but it is a bare
   `{get;set;}` with no `[ObservableProperty]`, assigned AFTER `ArrayCount` — count unchanged
   raises nothing at all, count changed raises while the OLD list is still in place.
   ⛔ **Why this is MED and not a cosmetic LOW:** `UpdateSelectedFields` stores the surviving row
   OBJECTS, and both exporters read those lists directly — `CeXmlExportService.cs:671/680/702/710/
   3329/3347/3490`, `CsxExportService.cs:551/560/612/652/732/840`. **A CE table or CSX export
   taken after a Refresh carries first-walk values**: wrong data leaving the app into another
   tool. Mitigation: refreshing while already inside the container view is clean (`:5131-5144`).
   ✅ **FIXED IN SOURCE 2026-09-11**, in one commit with `[P4-CONTAINER-BASE]` and `[P4-PTRCLASS]`,
   as planned.
   - **Plain-field backing:** `MapElements` / `SetElements` (and the six geometry and base members,
     see the fix trap under "Widenings") are init-only to the outside, backed by plain fields that
     `CopyLiveValuesFrom` refreshes.
   - **Order:** they are assigned FIRST and silently, then the observable members. After that, one
     explicit `DisplayValue` + `ValueTooltip` notification fires whenever an element list was
     replaced.
   - **Red → green:** map, set, array at the same count, and array that grows (4 tests), each red on
     the recorded mechanism first.
   - **Evidence and live check:** in the `[FIXPASS-2026-09-10]` ledger and backlog.

**LOW** — 7 rows: ✅ `[W1-GROUP-DENYLIST]` (FIXED IN SOURCE 2026-09-12, batch L23: the group status names how many classes the Diff denylist hid and where to see or clear them, on the no-match line too; red first) Group mode filters by a persisted denylist it gives no way
to see or clear (the *applying* is documented design — `docs/snapshot-group-match-spec.md:255`;
only the non-disclosure survives, and `GroupStatusText` already discloses the sibling
`PerSlotCapHit` cause) · `[W1-ARRAYCOUNT]` the Class Pivot array-field picker's element count is a
ROW count, inflated by inner numeric props, and `ArrayPivotStoreTests.cs:90` pins the wrong value
with a one-inner-prop fixture (✅ FIXED IN SOURCE 2026-09-11, batch B16: it counts distinct (owner, element) pairs now, red first with a two-prop fixture) · ✅ `[W1-PARTIAL-MARK]` (FIXED IN SOURCE 2026-09-12, batch L25: a new additive `partial_reason` column, `cap` / `disklow`, shown in the grid label and every picker line; the partial stays usable, so the auto-clean keeps it; red first) a cap/low-disk partial has no PERSISTED marker
(⛔ **the fix is a new marker, NOT `is_usable=0`** — see the refuted-fix note below) ·
✅ `[W1-PIVOT-LOADCTS]` (FIXED IN SOURCE 2026-09-12, batch L18: one CTS per list, pinned by a class load gated on its token) one shared `_loadCts` lets a field load cancel an in-flight class load with
no restart, leaving a stale picker · ✅ `[W1-DT-TRUNC]` (FIXED IN SOURCE 2026-09-12, batch L22: the Run keeps the load's "(showing N of M)", pinned red first) DataTable pivot Run overwrites its own
truncation notice with a bare row count, 17 lines above an array branch that gets it right ·
✅ `[W1-PIPEBUSY-LOG]` (FIXED IN SOURCE 2026-09-12, batch L26: on the connect timeout the bridge asks whether the pipe EXISTS, and a busy one is a Warn naming the likely holder; red first through an internal seam) pipe-busy is logged as "Cheat Engine not running" (see below) ·
✅ `[W1-WINMM-LOADMODE]` (FIXED IN SOURCE 2026-09-12, batch L27: the classifier names all four proxies, and a symmetry pin against Methode's `kProxyDllNames` keeps it so) `Fern.cpp:1408` omits `winmm.dll` from the proxy classifier so it never
earns a confirmed-proxy record.

#### ⛔ REFUTED — do not re-raise

- **SNAP-3** "the grid shows UTC while pickers show local". The mechanism is exactly as filed, but
  the column header is literally **"Captured (UTC)"** (`en.axaml:550`, mirrored for SPC at `:638`).
  Someone knew and said so; a canonical UTC sort column beside local-time pickers is disclosed
  design.
- **PIV-5** "SPC Group mode has no noise picker". Deliberate and documented: commit `d01b5861`
  (2026-06-23) says verbatim *"noise picker is diff-only"* and is the commit that added the
  `IsVisible="{Binding !IsGroupMode}"`; the SPC twin `be21af16` allocated six rows against single
  mode's seven, i.e. no row for a picker. A Single-mode denylist IS honoured by the group query
  (one shared `_excludedClasses`). The unread `TopContributors` half is a **structure** (§1.w4) and
  is symmetric across all three result models.
- **CEB-2** "the bridge does not establish WHICH Cheat Engine it reached". The finding's own
  load-bearing premise — *"nothing anywhere defines what available means"* — is **false**:
  `Core/IAobMakerBridge.cs:9-10` defines it as *"true if the last pipe connect succeeded"*, and
  `AobMakerBridgeService.cs:367` honours exactly that. AOBMaker's own client sends
  `GetAttachedProcess` and gates nothing on it either. An enhancement request, not a defect.

⛔ **AND ONE REFUTED FIX, which is rarer and more dangerous than a refuted finding.** SNAP-5's
obvious repair — mark a cap/low-disk partial `is_usable=0` for parity with drift — is **actively
harmful**: `DeleteUnusableSnapshotsAsync` (`SnapshotStore.cs:2419-2432`) auto-deletes unusable
snapshots before the next capture, destroying the partial the design deliberately keeps, and
`docs/audit-2026-07-14-findings.md:125` explicitly instructs that the partial-keep path
*"legitimately finalizes usable"*. The row needs a NEW marker.

#### Two gates this wave earned

- ✅ **`[W1-GATE-JSONDEFAULT]`** *(registered 2026-09-10 as `check_json_default_ignore`, in e8e52a4f
  with the `[W1-QUOTA-UNLIMITED]` fix; this marker was stale until 2026-09-11)* — fail any property under a `WhenWritingDefault` JSON context
  whose initializer differs from `default(T)`. It would have caught `[W1-QUOTA-UNLIMITED]` at
  commit time, the rule is already written down (`teleport-coord-library-spec.md:618`), and the
  whole-tree population is **one file**, so the gate ships green after one fix.
- ⬜ **`[W1-GATE-SESSIONGATE]`** — the three panels that hand a snapshot row's address to the live
  game must all compare `GameSessionId`. Two do; `[W1-PIVOT-SESSION]` is the third. A gate pins
  the symmetry the way `check_badge_prime_symmetry.py` does for badges.

#### ⚠ A documentation defect the sweep found by tripping over it

The **two-concurrent-Cheat-Engine hazard measured live on 2026-09-10** — CE is not
single-instance, only one process can own `\\.\pipe\AOBMakerCEBridge`, the losers retry-spam
`err=231` forever, and every CE window looks fine — **is in neither of the two documents a fresh
session is told to read.** It lives in `tools/verify/front_window.py`'s docstring and
`pipebusy_capacity.py`, plus scattered dev-log/register entries. `handover-2026-08-22.md:263`'s
"single-instance" line is about **UE5DumpUI**, not CE.

⭐ **This was not deduced, it was demonstrated**: a W1 refuter grepped both files for the hazard,
found nothing, and argued from that gap that two concurrent CE instances are outside the supported
state — the day after the maintainer hit exactly that, twice. An operational lesson that lives
only in a tool docstring is one grep away from invisible. ⬜ Move it into
`docs/working-lessons.md`, and reference it from the handover's CE section.

### ✅ W2 SWEPT 2026-09-10 `[BLANK-W2-2026-09-10]` — 13 raised, 10 confirmed, 3 refuted

3 auditors over **11,715 never-audited lines**; TELEPORT's 8,084 read **twice along opposite
arrows** over the same 19 files (DLL-publishes→UI-consumes and back) rather than split by file.
7 adversarial refuters. 10 agents, ~1.95M tokens. **Nothing fixed** — record-only, per the plan.

| | MED | LOW | REFUTED |
|---|---|---|---|
| TELEPORT-A | TPA-3 | TPA-4 | TPA-1 · TPA-2 |
| TELEPORT-B | TPB-1 · TPB-2 | TPB-4 | TPB-3 |
| VALUESEARCH | VS-1 · VS-2 · VS-4 | VS-3 · VS-5 | — |

⭐ **Reading TELEPORT twice paid for itself immediately**: both auditors, from opposite ends,
independently reported that two of the **2026-09 fix pass's own rows are incomplete**. Neither
re-filed them (the brief forbids it) — both raised them as notes. See the next block.

---

#### ⛔⛔ FP1 and FP2 ARE FIXED ON THE PIPE ONLY — the fix pass's own claims are half-true

This is the most important thing W2 produced and it is about **this project's own recent work**.
Both were verified by hand at HEAD, not taken from the agents.

**`[TPREL-ZEROPOSE-2026-09-10]` — one transport of three.**
```
Fern.cpp:6395-6397   bool landingKnown = true;  TeleportRelative(..., &tier, &landingKnown);   ✅ pipe
Mimic.cpp:1176       TeleportRelative(distance, horizontalOnly, p, &tier);                     ⛔ CE mailbox
Frieren.cpp:1346     TeleportRelative(distance, horizontalOnly != 0, p, nullptr);              ⛔ C ABI export
```
`Mimic.cpp:1177` then does `if (rc == 0) writePoseBlock(p, nullptr, 0, tier)` and
`Frieren.cpp:1347` does `Teleport_CopyPose(p, outNewPose6)` — both publishing the **zero-initialised
`Pose p{}`** as the landing, which is the original defect verbatim. `Wirbel.h:235-238` states the
contract at the function (*"publish nothing for the landing rather than zeros nobody measured"*)
and two of its three callers do not honour it.
⚠ **Correcting the agent's framing**: it said the header "claims all three transports were the
problem". It does not say that — it states the contract without scoping it. The substance stands;
the characterisation was loose.

**`[POSEATTACH-2026-09-10]` — the read warns, the SAVE cannot know, and the mode users actually
run never shows the warning.**
```
Wirbel.cpp:1366  GetPose      GetPoseImpl(..., outParentRelative)   ✅
Wirbel.cpp:1374  GetPoseFull  GetPoseImpl(..., outParentRelative)   ✅
Wirbel.cpp:1352  SaveLastImpl GetPoseImpl(m.P, ..., nullptr)        ⛔
Wirbel.cpp:1490  SaveMarker   GetPoseImpl(m.P, ..., nullptr)        ⛔
Wirbel.cpp:1757  BugItSave    GetPoseImpl(m.P, ..., nullptr)        ⛔
```
⭐ **The irony is exact.** The warning the fix shipped reads *"Do not save these as a marker"*
(`TeleportViewModel.cs:1066-1069`) — and the save path has **no way to know**. `struct Marker` has
no flag, `teleport_save_marker` (`Fern.cpp:6216-6234`) publishes none, and `TP_OP_SAVE` /
`TP_OP_GET_POSE` (`Mimic.cpp:1071-1086`) carry none.

**And the warning is invisible in normal use.** `RefreshPoseAsync` (manual ↻) sets the
`⚠ PARENT-RELATIVE` status at `:1066`. `RefreshPoseQuietAsync` — **the 500 ms auto-refresh, the mode
this tab is left in** — calls `ApplyPoseAndMovement(p)` at `:1017` with no warning at all, and any
later status message erases the one the manual path did set. The Current Pose card has a persistent
element for `MovementNote` and for the source chip, and **none** for the degraded-read state.

⬜ **Action: FP1 and FP2's register rows must record that they cover the pipe half only**, and the
residuals above become their own rows. ⛔ Do **not** close FP1/FP2 on a pipe-only measurement.

**Residual rows, filed 2026-09-11.** Only the save-path residual had become a row
(`[W2-MARKER-PARENTREL]`). The fix-pass inventory's completeness critic found the other two never
filed, so Track A's "P7: 0 new" counted a row that did not exist:
- ✅ **`[W2-POSEATTACH-QUIETPOLL]` MED** (FIXED IN SOURCE 2026-09-11, batch B22b) — `TeleportViewModel.cs:1013`/`:1017`.
  - `RefreshPoseQuietAsync`, the 500 ms auto path the tab is left in, calls `ApplyPoseAndMovement(p)`
    and never surfaces `ParentRelative`. Only the manual ↻ sets the ⚠ PARENT-RELATIVE status
    (`:1066`), and any later status erases it.
  - The pipe has published `parent_relative` since 242d48c4, so this is UI-only and testable
    offline.
  - The severity is inherited from `[POSEATTACH]`; the record gave this residual none of its own.
  - This is the only recorded P7 instance.
  - ✅ **FIXED IN SOURCE 2026-09-11** (batch B22b). The degraded read is now a STATE, not a message.
    - `PoseParentRelative` is set in `ApplyPose`, so the quiet poll, the manual ↻ and every other
      pose path share it. It shows as a persistent "⚠ parent-relative" chip beside the source chip.
    - A reply with no read metadata (`teleport_relative`, see `[W2-TPREL-MAP]`) keeps the last
      state instead of clearing it. The disconnect clears it with the rest of the pose.
    - The manual ↻'s status message stays as the fuller explanation; the chip is what survives.
    - **Tests, red first:** the quiet poll surfacing it, a directional TP keeping it, the disconnect
      clearing it, and the chip bound in the panel. A healthy read clearing it is the control.
- ✅ **`[A2-CABI-TELEPORT-PARENTREL]` LOW** (filed 2026-09-12 by review 5 of 76f93b94; FIXED IN SOURCE 2026-09-12, batch L44) — the C ABI pose getters carry no parent-relative flag.
  - `UE5_TeleportGetPose` (`Frieren.cpp:1282`) passes `nullptr` for `outParentRelative`, and
    `UE5_TeleportGetMarker` / `UE5_TeleportGetLast` copy `m.P` and drop `m.ParentRelative`. None has a parameter
    that could report it.
  - A `loadLibrary` / `callFunction` caller on an attached pawn whose world read failed gets rc 0 and
    parent-relative numbers, with no way to tell them from world coordinates.
  - Also: `BugItSave` records `s_bugItMarker.ParentRelative`, and nothing reads it (`BugItGo` uses only `m.P`).
  - ⚠ **Fix shape:** new exports (a flag out-parameter, or an `...Ex` variant), never a change to the existing
    signatures, which CE scripts call by position. The C ABI export count is pinned by `check_derived_counts`,
    and `docs/dll-spec.md` lists the exports.
  - ✅ **FIXED IN SOURCE 2026-09-12** (batch L44), exactly that shape.
    - `UE5_TeleportGetPoseEx`, `UE5_TeleportGetMarkerEx` and `UE5_TeleportGetLastEx` are the same getters plus
      `int32_t* outParentRelative` (nullable).
    - The pose read passes the flag `GetPose` already computes; the marker reads copy the stored `m.ParentRelative`.
    - The originals are unchanged. The export count is 60 → 63 at every derived site, and dll-spec gains the three rows.
    - **Red first:** an InvokeScriptTests source pin, because Frieren.cpp reaches no test target. It checks the three
      declarations, that the original signatures are intact, and each Ex's flag source.
    - ⬜ **Residual, unchanged:** `BugItSave` records `ParentRelative`, and `BugItGo` still reads only `m.P`.
- ✅ **`[W2-TPREL-TRANSPORTS]` LOW** (FIXED IN SOURCE 2026-09-12, batch B29b) — `Mimic.cpp:1176-1177`, `Frieren.cpp:1346-1347` (the table above).
  - Both call `TeleportRelative` without `&landingKnown` and publish the zero-initialised
    `Pose p{}` as the landing. The pipe half was fixed in 5058e971.
  - The mailbox half follows the `MAILBOX_CONTRACT` rules, and its live check needs CE.
  - ✅ **FIXED IN SOURCE 2026-09-12** (batch B29b). Both transports now pass `&landingKnown`. An unknown
    landing is published as NaN, never the zero-initialised Pose:
    - TP_OP_RELATIVE writes NaN doubles plus pose-flag bit1 (contract 4, see `[W2-MARKER-PARENTREL]`);
    - `UE5_TeleportRelative` fills `outNewPose6` with NaN, with `rc` still 0 because the move worked.

    The pin is the same source pin. 4/4 mutants killed; UI 5144/5144.

---

#### The fix list — 10 rows (each carries its own ✅/⬜; this heading read "none repaired" until 2026-09-11)

**MED** — 6 rows.

1. ✅ **`[W2-GRAVDIR-VERDICT]`** (FIXED IN SOURCE 2026-09-11, batch B21) `TeleportViewModel.cs:3184`. On a UE5.4+ game that fully supports
   arbitrary gravity, the Gravity Direction card states *"needs UE5.4+ (no reflected
   GravityDirection)"* and paints an amber **Unavailable** badge whenever a pawn/CMC does not
   resolve **at that instant** — main menu, loading, cutscene, spectator, vehicle pawn. A
   transient absence is reported as a permanent verdict about the user's engine.
   **Fix is display-side and the data is already on the wire**: split `!mp.HasCmc` from
   `mp.HasCmc && !g.Resolved`, and discriminate on `r.State`.
   ✅ **FIXED IN SOURCE 2026-09-11, the recorded display-side shape** (batch B21).
   - The readout splits `!mp.HasCmc` (no pawn / CMC at this instant: the badge stays Unknown and the
     text says to enter gameplay) from `mp.HasCmc && !g.Resolved` (the pre-5.4 verdict: Unavailable,
     "needs UE5.4+"). The ↻ status splits the same way.
   - The apply status states the pre-5.4 verdict only when BOTH signals agree: the set refused with
     -4 (`MR_ERR_REFLECT`, `Constants.LaufenErrReflect`) AND the fresh read shows a live CMC without
     the field. Neither is enough alone. `resolved` is false with no pawn as well. -4 is also returned
     when `ResolveCtx`'s pawn/CMC class lookup fails, or `SetGravityDirection`'s vector read does.
     That second half was caught by reading the DLL after a first cut keyed on -4 alone.
   - The badge gained a real Unknown state. The connect/disconnect reset's comment always said "back to
     Unknown", while the code painted the amber Unavailable.
   - **Tests, red first:** the readout without a pawn, and an apply without a pawn. Then a
     both-signals theory, red against the first cut: -4 with no live CMC, and -4 from a failed read.
     Its third row (-3, then a pre-5.4 CMC by the read) pins the code half. The pre-5.4 apply is the
     control, green both ways, beside the existing not-reflected readout test.
   - ⚠ Residual, not this row: any other refusal (`MR_ERR_WRITE` -10, a transient read failure) still
     lands on the generic "no pawn / no CharacterMovement" text. That wording predates B21.
   - ✅ **Review follow-up 2026-09-11** (adversarial review of B14–B21, finding `gravdir-locate-twin`,
     MED, CONFIRMED). **A third entrance B21 missed:** the card's Locate-in-GWorld command re-read
     the params, painted the right Unknown badge, then overwrote the status with "needs UE5.4+".
     Locate now takes the readout's split. A no-pawn Locate was red first, with the pre-5.4 Locate
     as the control. 2/2 mutants killed; UI 5079/5079.
2. ✅ **`[W2-TPREL-MAP]`** (FIXED IN SOURCE 2026-09-11, batch B22) `TeleportViewModel.cs:3281`. After a directional teleport the Current
   Pose Map row goes blank, because `teleport_relative`'s reply carries no `map` key. Every
   Coordinate Library row is then re-flagged as belonging to another map (`Dist` collapses to `—`,
   the summary reads `⚠ different map (you are on '')`) — and the durable half: an entry added with
   *Add from fields* at that moment is **written to disk with `map = ""`**.
   ⚠ **Second entrance, from the same auditor**: on a fresh connect nothing reads the pose at all
   (`SetConnected` calls only `RefreshMarkersAsync` + `PrimeHeldBadgesAsync`), so `PoseMap` is `""`
   until the user presses ↻ — *Add from fields* persists the same empty-map entry then too.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B22), on the UI side, so it holds against every DLL build.
   - `TeleportPose.MapAbsent` records that the reply carried NO `map` key, and `ApplyPose` then keeps
     the last-known map. The KEY decides: a reply of `map = ""` did report a map.
   - **Second entrance:** the connect prime now reads the pose (`RefreshCurrentMapAsync`, already
     quiet). *Add from fields* reads the map at add time, the rule the teleport guard already
     follows. With no map knowable (disconnected, no pawn), its status says the entry has NO map.
   - **Third entrance, found while fixing:** an UNKNOWN map (`""`, e.g. connecting in the main
     menu) meant "no filter" to the filter and the teleport guard, but "another map" to the row
     flag and the summary. One predicate, `IsOnCurrentMap`, now serves all four.
   - **Twin, fixed with it:** a reply with no `source` key relabelled the pose "raw", a claim about
     how it was read that nobody made. `SourceAbsent` keeps the last label.
   - **Tests, red first:** the ParsePose key test, a directional teleport keeping the map and the
     source, the add-time read, the connect prime, and an unknown map. A reply that does report a
     map is the control.
3. ✅ **`[W2-MARKER-PARENTREL]`** `Wirbel.cpp:1490` — the FP1 residual above, filed as its own row.
   ✅ **The pipe half, FIXED IN SOURCE 2026-09-12** (batch B29a). ⬜ The mailbox half is batch B29b.
   - `struct Marker` gains `ParentRelative`. `SaveMarker`, `SaveLastImpl` and `BugItSave` capture it
     from `GetPoseImpl`, which already reported it. The save path used to pass `nullptr`, so it could
     never know.
   - Fern publishes `parent_relative` on `teleport_save_marker` and on every `teleport_get_markers` entry,
     including the "last" sentinel. The key is absent on a healthy save, like `get_pose`'s own key.
   - **UI.**
     - `TeleportMarker` / `TeleportMarkerRow` carry the flag.
     - The row summary and the Last summary read "⚠ parent-relative (not world)".
     - The save status says the marker came from a PARENT-RELATIVE read, so recalling it drives the pawn
       to those numbers as if they were world coordinates.
   - **Tests, red first** against inert properties:
     - a parent-relative save (the status and the row);
     - a refresh that flags a marker and the Last slot;
     - the parse.

     A healthy save and a healthy marker are the controls. 5/5 mutants killed; UI 5139/5139.
   - ⚠ **Survivors by construction:** Wirbel.cpp's capture and Fern.cpp's publish, which no test target
     compiles. The real `UE5Dumper` build and the live check cover them.
   - ✅ **Review 5 follow-up 2026-09-12** (of 7490c24e: two LOW, both CONFIRMED).
     - **The pose-card chip never saw the save's flag.** `ApplyPose` trusts a reply's read metadata only when
       the reply carries `source`, and `teleport_save_marker`'s never does. So a degraded save showed
       parent-relative numbers under a chip that stayed off, and a healthy save left a stale chip on. The VM
       test faked a `source` the wire never sends.
     - Fern now ALWAYS sends `parent_relative` on the save reply. "Absent when healthy" looked exactly like an
       older DLL's silence. The parse records whether the reply carried it (`ParentRelativeKnown`), and the
       card honours a carried flag.
     - **Tests, red first:** the chip both ways through a source-less reply, the parse, and a Fern pin. An
       older DLL's silent reply keeping the chip is the control. 3/3 mutants killed; UI 5193/5193.
     - The C ABI transport this row once folded in is now its own row, `[A2-CABI-TELEPORT-PARENTREL]`.
   ✅ **The mailbox half, FIXED IN SOURCE 2026-09-12** (batch B29b, together with `[W2-TPREL-TRANSPORTS]`).
   ⚠ Until review 5 this line said "the mailbox and C ABI half". The C ABI carries only RELATIVE's NaN landing. Its
   three pose getters stay flagless, and are now their own row, `[A2-CABI-TELEPORT-PARENTREL]`.
   - ✅ **Review 5 follow-up 2026-09-12** (of 76f93b94: four LOW, two of them filed MED).
     - **The freeze-helper Lua rig broke.** The contract bump to 4 left `freeze_helper_test.lua` faking a
       contract-3 DLL, so every case expecting a freeze was refused as "the DLL is older than this script".
       The Lua suite is no gate, so "UI 5144/5144" never saw it. It fakes 4 now: 159/159 (measured on a
       patched copy before the commit).
     - **The pins let one-line mutants of the core fix through.** They now count each flag write per site,
       and pin the NaN landing on both transports, bit1, Wirbel's copy and the CE records' `% 2 == 1`
       predicate. 7/7 mutants killed; UI 5187/5187.
     - **The pose-block spec** (`docs/teleport-spec.md` §8) lacked the `[178]` byte, and the freeze helper's
       contract comment stopped at 3. Both are updated.
   - The pose block gains `paramsData[178]`, pose flags: bit0 parent-relative, bit1 RELATIVE's landing
     unknown. It is written by GET_POSE, SAVE, GET_MARKER, GET_LAST, BUGIT_SAVE and RELATIVE.
   - `BugItSave` gains an optional `outParentRelative`.
   - **Contract 3 → 4, ADDITIVE.** [178] was an unused output for every pose-block op (only CURSOR writes
     it, as its own usedCenter), so `MAILBOX_CONTRACT_MIN` stays at 1. Like version 2 it moves on meaning
     alone: the surface hash is unchanged, and `tools/check_mailbox_contract.py` records why.
     `CeMailboxLayout.ContractVersion` follows.
   - The CE Save / Get current coords / BugIt records read the flag. A parent-relative pose shows a
     PARENT-RELATIVE message and keeps the window open (`hadError`).
   - **Tests, red first:**
     - a theory over the three records;
     - a source pin on Mimic.cpp / Frieren.cpp (no test target compiles them) plus the contract claim.

     RECALL not reading the byte is the control. 4/4 mutants killed; UI 5144/5144.
4. ✅ **`[W2-ORDEN-FINDENTRY]`** (FIXED IN SOURCE 2026-09-11, batch B13) `dll/src/Orden.h:102`. A Group Scan slot with **Bigger** or
   **Smaller** silently skips every field of a width the target cannot be *encoded* at, even when
   every value of that width satisfies the comparison. Because a group candidate needs ALL slots at
   distinct leaves, one lost width class drops the whole object. The user sees zero or far fewer
   matches with no warning, no error and no log line.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B13, one commit with `[W2-GROUPMATCH-WIDTH]` and `[A4-AB4-UINT64]`).
   - `Orden::LeafSatisfiesSlot` looks the target up with `FindEntry` and hands the ENTRY to
     `ComparePredicate`, so an AlwaysTrue verdict is honoured. Between still needs a real encoding.
   - The group REFINE had the same `Find` (`Aura.cpp` `RefineGroupCandidates`). It now uses the
     single-value refine's two-shape compare.
   - ⛔ The raw-hole pre-filter (`AppendRawHoleLeaves`) keeps `Find` on purpose. It is the noise
     filter the unsafe-fix table names: a verdict there would turn every hole probe of that width
     into a leaf.
   - Fern already built group targets with the slot's own predicate, at first scan and at refine,
     so the verdict was always in the set. Only the lookup hid it.
   - **Tests, red first:** `Test_Orden_OrderedVerdictWidths` (3 ⭐, plus the Bigger 70000 and
     Between controls) and dll_core_test `GROUPREFINE` (2 ⭐, one control). The refine's leaves are
     the block's own statics, read through `ReadBytesSafe`, so the block is pure.
5. ✅ **`[W2-GROUPMATCH-WIDTH]`** (FIXED IN SOURCE 2026-09-11, batch B13) `GroupMatch.cs:167`. The same defect in the C# mirror, for
   Snapshot Group Match: *"no objects matched"* over a corpus that does contain the group,
   indistinguishable from a correct empty answer.
   ✅ **FIXED IN SOURCE 2026-09-11, in the same commit as its DLL twin** (both sides, as §9 requires).
   - When an ordered target has no encoding at an integer leaf's width, `LeafSatisfiesSlot` now
     returns `EveryValueSatisfies`: Smaller above the max, or Bigger below the min, holds for every
     value. That is the mirror of Radar's `Fit::AlwaysTrue`. Exact and Between keep the gate.
   - **Tests, red first:** `OrderedSlot_UnencodableTarget_*`: 5 theory rows and the group fact went
     red. The Bigger 70000, Smaller -5 and Exact rows are the controls.
6. ✅ **`[W2-GROUPMATCH-ENUM]`** (FIXED IN SOURCE 2026-09-11, batch B12) `GroupMatch.cs:84`. `WidthBytes` has no `EnumProperty` case, so an
   enum-backed state field (weapon type, quest stage, class) that IS a matchable leaf in the live
   group scan can **never** satisfy any slot in Snapshot Group Match. A user reproducing a live
   group across the corpus gets an empty result and no hint that a field was ineligible.
   ✅ **FIXED IN SOURCE 2026-09-11, after its root `[P3-SNAPNUM-ENUM]` and in the same commit.**
   - `EnumProperty` joins ALL THREE predicates together, mirroring the live matcher (Radar maps
     EnumProperty → UInt8 and admits it under `kNumericAll`, not `kNumericNoByte`):
     - `WidthBytes` (1);
     - `IsOneByte`, so `NumericNoByte` still excludes it;
     - `IsUnsigned` (0..255).
   - The recorded harmful fix, `WidthBytes` alone, is what the `NumericNoByte` control kills.
   - ⚠ **The first version of the Group Match tests was VACUOUS.** `Run()` rejects fewer than two
     slots, so a single-slot `Run` was false whatever the leaf, and the "control" passed for the
     wrong reason. They were rewritten on `LeafSatisfiesSlot`, with an anti-vacuity partner (an
     IntProperty leaf satisfying the same NoByte slot) and a real two-slot group. Their red is
     shown by the mutation check, not by a pre-fix run.

**LOW** — 4 rows: `[W2-MS-PROMISE]` Move Speed Apply promises *"the override applies once a pawn
exists"* when `Laufen` returned before storing anything, so the queued override silently does not
exist (`TeleportViewModel.cs:2169`; its two siblings at `:2588`/`:3053` word it correctly) — ✅ FIXED IN SOURCE 2026-09-11 (batch B21): the clause is deleted, the recorded safe fix. **A twin was found and fixed with it:** the time-dilation status promised the same for both levers, and `Hemmung::SetDilation` also returns before storing anything when its owner does not resolve ·
✅ `[W2-CEGEN-MODAL]` (FIXED IN SOURCE 2026-09-12, batch L37: both generators' `[DISABLE]` bails now go through `SilentReturn` / `dbg`, as Movement's already did, and `[ENABLE]` is unchanged; red first) the GodMode and Debug-Camera `[DISABLE]` blocks bail with `showMessage` instead
of the documented `SilentReturn`, so unticking pops a modal over a fullscreen game — **twice** on
the contract-check path (`ProtectionScriptGenerator.cs:64`) · ✅ `[W2-BETWEEN-PREVIEW]` (FIXED IN SOURCE 2026-09-12, batch L28: Value Search's Between preview parses with the DLL's grammar, so a bound the DLL refuses previews nothing. SPC keeps its own query's grammar, which is unchanged. Red first) the Between
live preview parses with `NumberStyles.Any`, so for FVector/FRotator/FTransform it concatenates the
three components into one fabricated number and for scalars it accepts thousands separators and
parenthesised negatives the DLL refuses (`RoundModePreview.cs:75`) · ✅ `[W2-DEADSCAN-LOADMORE]` (FIXED IN SOURCE 2026-09-12, batch L29: Load More is gated on a live session and the window status says the rows are the previous scan's; the rows stay, per the refuted-fix table below; single and group alike; red first) a
cancelled or failed First Scan leaves the previous session's rows on screen with a **Load More**
button whose DLL session has already ended — a dead control that produces no rows, no error and no
log line (`ValueSearchViewModel.cs:1031`).

---

#### ⭐⭐ FIVE OF THE TEN CONFIRMED ROWS HAVE AN UNSAFE OBVIOUS FIX

W2 added an `implied_fix_safe` field to the verdict schema, because W1 produced a *wrong fix* that
only surfaced by accident. It fired on **half the confirmed rows** — read these before repairing:

| row | the obvious fix | why not |
|---|---|---|
| `[W2-GROUPMATCH-ENUM]` ✅ B12 | add `"EnumProperty" => 1` to `WidthBytes` | ⛔ **actively harmful** — two sibling predicates key on the same string set; `IsOneByte` (`:93`) changes meaning with it |
| `[W2-GROUPMATCH-WIDTH]` ✅ B13 | fix the C# side | ⛔ `snapshot-group-match-spec.md` §9 says *"Do not fork per-feature SDR logic"*, and `GroupMatch.cs:109-112` declares itself a **mirror** — fix both sides or neither |
| `[W2-ORDEN-FINDENTRY]` ✅ B13 | mechanical `Find` → `FindEntry` | ⚠ safe at `Orden.h:102` and `Aura.cpp:9609`, **not** at `Aura.cpp:9155`, where `Find()` is a deliberate **noise filter** |
| `[W2-MS-PROMISE]` ✅ B21 | make `Laufen` arm on failure so the promise becomes true | ⛔ harmful — the safe fix is to delete the clause, matching its two siblings |
| `[W2-DEADSCAN-LOADMORE]` | clear the grid in the catch blocks | ⛔ would blank a 1,000-row result because the user mistyped or hit Cancel; those rows are still valid |
| `[W2-MARKER-PARENTREL]` | make `SaveMarker` refuse | ⚠ the **reporting** fix is safe (pass `&parentRel`, carry a bool on `struct Marker`, emit the key); a **refusing** fix is not |

#### ⛔ REFUTED — do not re-raise

- **TPA-1** *"`Wirbel` measures the pawn never reached the target, then returns `TP_OK`"*. Three
  independent routes. ⭐ **It is already on a refuted list** — `docs/todo.md:3130`, row 0 of the
  a4-p2 no-channel table: *"published-elsewhere — it is the same read `teleport_get_pose`
  returns"*. And the origin commit `29079c8c` calls the post-move check a **diagnostic** and names
  the log as its channel. ⚠⚠ **That same phase's own closing note at `docs/todo.md:3153` warns
  that an LLM agent re-reading unchanged code will keep reaching this plausible conclusion — and
  five days later one did.** The note was right; keep it.
- **TPA-2** *"the GodMode CE record unticks itself on the failure that DID arm the DLL"*. Settled
  against CE's own source, not by argument: `D:\Github\cheat-engine\...\memoryrecordunit.pas:2573`
  `setActive` → `:2671` `autoassemble(..., state=false)` **executes the `[DISABLE]` block**, which
  writes 0 and disarms `Solitar`. The repo already states this twice at HEAD.
- **TPB-3** *"the UI's Apply at 100% pins the knob while the CE path treats 100% as off"*.
  Documented design in three places: `Laufen.cpp:530`'s own comment scopes the sentinel to *"the
  single-call API the CE-Lua/mailbox path uses"*, `docs/pipe-protocol.md:1752-1756` publishes the
  pipe's two-command pair, and commit `1dc90354`'s body says the rule was created **for** the
  mailbox, not dropped from the pipe.

#### Calibration

10/13 confirmed, against W1's 13/16 and a predicted ~1-in-5. The brief's do-not-re-raise list held
for six of the seven named tags — the one leak (TPA-1) was caught by a refuted list, which is the
second line of defence working as designed. **The agents' restraint is visible in what they did
NOT file**: TELEPORT-A held back four adjacent observations as too close to `[POSEATTACH]`, and
raised the two genuine residuals as notes instead of findings, which is exactly the behaviour the
brief asked for.

### ✅ W3 SWEPT 2026-09-10 `[BLANK-W3-2026-09-10]` — 8 raised, 6 confirmed (5 DISTINCT), 2 refuted

3 auditors over **7,602 never-audited lines**. APP-SHELL's 4,926 read through **two lenses** over
the same 31 files — *persistence and lifecycle*, then *composition and cross-panel wiring* —
deliberately **not** W2's opposite-arrows shape, because APP-SHELL is not a wire cluster and the
productive division there is which question you ask, not which end you start from. 5 refuters.
8 agents, ~1.6M tokens. **Nothing fixed.**

| | MED | LOW | REFUTED |
|---|---|---|---|
| APP-SHELL-A | — | ASA-1 · ASA-2 | — |
| APP-SHELL-B | — | ASB-2 *(= ASA-1)* | ASB-1 · ASB-3 |
| OBJTREE | OT-1 · OT-2 · OT-3 | — | — |

⚠ **ASA-1 and ASB-2 are the SAME defect** — same file, adjacent lines, same two properties. Both
lenses reached it independently. That is corroboration, **not two rows**: the honest count is
**5 distinct confirmed defects**, and the ledger records 6/5.

---

#### ⭐⭐ This wave's largest output is NOT findings — it is MECHANICAL VERIFICATION

APP-SHELL-A filed only two LOW rows and spent its budget proving things instead, with scripts
rather than by eye. That is the right trade and the results close real open questions:

- **The ~90 model-default / VM-default pairs.** `UiOptionsSettings.cs:18-20` declares the
  invariant (every model default MUST equal the corresponding VM `[ObservableProperty]`
  initializer, because an older options file hydrates missing keys to the model default), and W1's
  WIRE-DTO agent flagged that `UiOptionsStoreTests` pins **3 of ~90**. A script parsed every
  property + initializer out of the 15 sub-classes and every backing-field initializer out of the
  15 mapped ViewModels, then resolved 4 name mismatches and 5 constant references by hand:
  ⭐ **ZERO disagreements. The other 87 hold.**
- **`ApplyOptions` ↔ `BuildOptions` coverage**: all 90 model fields are both written on load and
  read on save. The only asymmetry in the whole options pipeline is the change-tracking gap now
  filed as ASA-1.
- **The W1 `WhenWritingDefault` trap, swept tree-wide.** Only three context-level users exist:
  `ExperimentalSettings` (**is** `[W1-QUOTA-UNLIMITED]`, deliberately not re-raised),
  `AobUsageRecord` (no initialized non-string member whose 0/false is legitimate) and
  `ClassDenylistSettings` (only `List<string> = new()`, whose default is null, so an empty list is
  still written). ⭐ **Independently reproduces the blast-radius analysis recorded in W1 — no new
  instance exists.**
- **Resource integrity**: all **1,322** `x:Key` definitions unique within their dictionaries, and
  every `{StaticResource}` / `{DynamicResource}` / `Res.Get("...")` in the whole UI resolves. A
  missing key is a XAML **load failure**, not a blank label, so this is a crash class now measured
  rather than assumed. `MainTabIndex` 0..18 matches `MainWindow.axaml`'s 19 `TabItem`s in order, so
  the index-based cross-tab handoffs land on the tabs they name.
- **Hotkey failure reporting** — the shape the brief flagged as high-yield — came back **clean**:
  a failed `RegisterSpecific` returns false, sets the row's `Conflicted` flag, raises a banner
  naming the combo, and the cursor-hotkey ladder reports "Ctrl/Alt+F5..F8 are all taken" and unticks
  itself. **No hotkey is shown as bound while dead.**

---

#### ⛔⛔ `[W1-QUOTA-UNLIMITED]` ESCALATES — a second entrance that needs NO user action

W1 framed that HIGH row as *the user choosing "Unlimited"*. It is worse than that, and I verified
it at HEAD:

```csharp
// SnapshotViewModel.cs:1184-1186
if (bytes == null)                        // exceeds every preset → Unlimited
{
    if (_gate.SnapshotQuotaMb != 0) SelectedQuotaLabel = MbToLabel(0);
```

With **Auto-snapshot quota adjustment on**, `AutoSnapshotPlanner.RaiseQuotaBytes` returns `null`
once the retained set exceeds the 5 GB top preset, and `ApplyAutoQuota` then sets **Unlimited by
itself** → `_gate.SnapshotQuotaMb = 0` → the key is omitted from `experimental.json` → next launch
reloads **1024 MB** → `SnapshotStore` FIFO-deletes. ⭐ **A user who never opened the quota combo can
lose snapshots.**

⚠ **This machine's `experimental.json` currently reads `"snapshotQuotaMb": 5120` — one preset below
the trigger.** ⬜ Widen the W1 row rather than filing anew; the fix is the same one line.

⚠ **And a second, latent hazard in the same pair of methods:** `MbToLabel`
(`SnapshotViewModel.cs:545-553`) maps anything not exactly 512/1024/2048/5120/0 to `"1 GB"` and
`OnSelectedQuotaLabelChanged` writes that back — so `ApplyAutoQuota`, whose own doc says *"Raise
(never lower)"*, **would lower a non-preset quota to 1 GB**. Unreachable today *only* because
`AutoSnapshotPlanner.QuotaPresetBytes` happens to hold exactly those four values, and **nothing
pins the two lists together**. A hand-edited `experimental.json` already shows "1 GB" in the combo
while eviction runs at the hand-edited number.

---

#### The fix list — 5 distinct rows, none repaired

**MED** — 3 rows, all OBJTREE.

1. ✅ **`[W3-CONSOLE-REINVOKE]`** (FIXED IN SOURCE 2026-09-11, batch B11) `ConsoleViewModel.cs:486`. The sticky-instance self-heal fires on
   `!result.Success`, which is true for **every** non-zero ProcessEvent code — including
   **`-5` (game-thread dispatch timeout)**. `Stark.cpp:412-423` states verbatim that on `-5` *"The
   request stays queued"* and will execute when the game thread next drains, and
   `Fern.cpp:5580-5588` deliberately **leaks the FString buffers** for exactly that reason. So a
   `-5` makes the UI enqueue the **same exec command a second time** — and a stateful console
   command (give item, spawn, teleport, set) then runs twice.
   ⛔ **Fix is partly unsafe**: excluding `-5` is correct; the finding's repair also covers `-4`,
   and **that half must be refused** — a stale pin produces `-2`/`-4`, never `-5`.
   ✅ **FIXED IN SOURCE 2026-09-11, the safe half only.**
   - The self-heal skips its retry on `Constants.InvokeDispatchTimeoutResult` (-5). The constant
     is named, with a comment citing Stark.cpp's timeout path, where "The request stays queued".
   - It keeps the pin, because the queued call runs on it.
   - The status says the command is still queued and was not re-sent.
   - `-2` and `-4` still self-heal: the refused half.
   - **Tests.** `DispatchTimeout_on_a_pinned_invoke_is_not_resent_and_keeps_the_pin` was red
     first: it counts invocations, checks the status, and checks that the pin survives.
     `StalePin_minus4_is_still_retried` is the control, green both ways; the existing `-2`
     self-heal test is unchanged.
   ✅ **Review follow-up 2026-09-11** (the review of B09-B12; B11's share was 5).
   - **A timeout on the self-heal RETRY got no note.** The flag was taken from the first result,
     and the class-name retry goes through the same queued dispatch. So a stale pin (`-4`) followed
     by a retry that timed out (`-5`) said nothing about the queued call. The note now reads the
     FINAL result; the first one still only gates the retry. Red first:
     `DispatchTimeout_on_the_self_heal_retry_is_reported_as_queued`.
   - Pinned: the note stays off `-2` / `-4`, and an unpinned `-5` is reported and not re-sent.
   - **Two DLL-side twins are filed as their own rows below**, both pre-existing and outside this
     row's scope: `[W3-DUNSTE-QUEUED]` (MED) and `[W3-DEBUGCAM-QUEUED]` (LOW).
2. ✅ **`[W3-XREF-CAP]`** (FIXED IN SOURCE 2026-09-11, batch B25) `PropertyXrefDialog.cs:433`. `Aura::FindPropertyXrefs` and
   `FindFunctionsByClassParam` self-cap each worker at `maxResults` then `ConcatTruncate`, and
   **neither folds the cap into any published flag** — both set only
   `out.stats.deadlineHit = scan.incomplete()`, which covers the clock and worker faults but never
   the cap. The dialog renders a capped page as a complete answer.
   ⭐ **Same shape as W1's `[W1-SNAP-FAULT]`, one layer over**: a cap that six sibling sites publish
   and these do not.
   ⛔ **THE CHEAP FIX IS ACTIVELY HARMFUL** — do **not** copy `Aura.cpp:8255-8256`'s
   `if (size >= maxResults) deadlineHit = true;`. That is the very conflation P5 names.
   ⚠ *This line first cited "`docs/todo.md:1314`" as the open row, but that line never held one. The
   value scan's cap merge is recorded only as a rig note in `docs/verification-register.md` (D2,
   note (c), `Aura.cpp:8244`); see the P5 row of the Track-A table.*
   ⚠ **Four call sites, not one**: `InstanceFinderViewModel.cs:977` and
   `GameClassFilterViewModel.cs:356` also call `FindFunctionsByClassAsync(..., 200, ct)` and read
   only `Scan.DeadlineHit`. ⚠ And the only existing hint is **accidental and misleading**:
   `Fern.cpp:4809` reuses `functions_with_script` as "matched" and `Aura.cpp:6130` sums across
   workers that each self-cap, so a capped 8-thread scan can print *"(1,432 matched)"* beside 200
   rows. Do not mistake that for a disclosure when fixing this.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B25). The cap is its own published flag, never folded into
   `deadlineHit`: the recorded unsafe fix, and the P5 conflation, are what did not land.
   - **DLL.** `PropertyXrefStats` gains `capHit` and `cap`, the effective cap.
     - A pure helper beside `ConcatTruncate`, `MergedScanCapHit`, sets it before the merge moves the
       elements out.
     - It is set when a worker reached `maxResults`, or when the workers together found more than
       `maxResults`. A worker at its cap stopped there, so its range MAY be unfinished. The helper cannot
       tell that from a worker whose last object happened to be its 200th match, and flags both:
       conservative, which suits a `200+` cell. Every worker below its cap, with the total within it, is
       NOT capped. *(Corrected by review 3: this line first said a worker at its cap "was not finished".)*
     - Both scans set it, and Fern publishes `cap_hit` / `cap` in both handlers' `scan` objects.
     - An additive wire key: no contract bump, and the CE mailbox is untouched.
   - **UI, FIVE sites, not four.** The dialog, plus four batch loops. Property Search and Interesting
     Properties call `FindPropertyXrefsAsync(…, 200)` and hit the same cap; this row named two batch
     sites.
     - A capped cell shows its count as a lower bound (`200+ · …`). It is NOT a partial cell: a re-run
       asks for the same 200, so it cannot find more, and treating it as partial would re-scan it forever.
     - Its own status clause, `PartialResultNotice.BatchCapClause`, names the cap as the cause.
       `BatchPartialClause`'s "hit the scan deadline … a 0 means not found YET" fits neither the cause
       nor a capped row, which is never 0.
     - The dialog's status comes from a pure `XrefFormat.XrefDialogStatus`: "200+ function(s)", and
       "[CAP HIT — only the first 200 are listed; more may exist]".
     - In class mode the "(N matched)" sum reads as a lower bound when capped, since it adds up
       workers that each self-capped: the accidental "hint" this row warned about.
   - **Tests, red first** against inert stubs:
     - `dll_core_test`: the helper's cases, and a real `FindPropertyXrefs` over the fake object pool
       (five matching UFunctions: cap 3 is capped, cap 10 is not);
     - the cell, the clause and the dialog status as pure tests;
     - the batch loops that have a harness;
     - DumpService's parse over a mock pipe.

     11/11 mutants killed; dll_core_test 214/214; UI 5112/5112.
   - ⚠ **Documented survivors, by construction:** `FindFunctionsByClassParam`'s call site (it mirrors
     `FindPropertyXrefs` line for line, and no test builds UFunction param chains), and Fern.cpp's two
     serialisers (compiled by no test target). The real `UE5Dumper` build and the live check cover them.
   - ✅ **Review follow-up 2026-09-12** (review 3, of F7 / B23b / B24 / F4b / F5b / B25: 9 survived, 2
     refuted; this row's share was one LOW, `batch-cap-clause-overclaims`, CONFIRMED).
     - The batch status said rows "matched more than" the cap. The DLL knows only that a worker REACHED it,
       which means "more may exist", not "more exist".
     - It now reads "reached the 200-result cap … only the first 200 are listed, and more may exist", the
       dialog's own wording. The helper's comment dropped the same overclaim.
     - **Test, red first:** the clause claims only what the DLL knows. 1/1 mutants killed; UI 5130/5130.
3. ✅ **`[W3-BATCH-METHOD]`** (FIXED IN SOURCE 2026-09-11, batch B20) `InterestingFunctionsViewModel.cs:331`. `BatchFindFuncPropsAsync`
   consumes `res.Props` and `res.BudgetHit` and **never reads `res.Method`**. The DLL publishes
   four method tags and **two of them mean nothing was analysed at all** — `"none"`
   (`Aura.cpp:6257`, UFUNCTION_FUNC offset never resolved on this build) and
   `"blueprint_no_script"` (`Aura.cpp:6289`, refused so the shared interpreter is not disassembled
   and misattributed). The "Uses" column writes a bare **0**, indistinguishable from "analysed, found
   none". No DLL change and no contract bump needed — `method` is already published
   (`Fern.cpp:4855`) and already parsed (`DumpService.cs:1329`).
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B20; no DLL change, no contract bump).
   - `FunctionPropRefsResult.NotAnalysed` names the two tags that mean nothing was looked at. An
     unrecognised future tag is not treated as one.
   - The batch reads it: such a row's cell is `n/a` (`PartialResultNotice.NotAnalysedCell`), never a
     bare `0`, and the summary adds a clause counting them (`BatchNotAnalysedClause`).
   - The single-function `FunctionPropsDialog` already handled both tags; the batch was the only
     consumer that did not.
   - **Tests, red first:** a theory over `none` / `blueprint_no_script`. The control over `bytecode` /
     `disasm` keeps a bare `0`, green both ways.
   - ✅ **Review follow-up 2026-09-11** (adversarial review of B14–B21, `batchmethod-bytecode-unread`,
     LOW/PLAUSIBLE). B20's premise, that only two tags mean nothing was analysed, was incomplete.
     - `WalkFunctionPropertyRefs` set `method = "bytecode"` BEFORE reading the Script buffer, and
       returned empty refs when that read failed. That happens with a stale or freed allocation, or a
       mis-resolved `USTRUCT_SCRIPT`. The batch then wrote a bare `0` for a function nobody scanned.
     - The DLL now tags that case `bytecode_unreadable`. `NotAnalysed` includes it, and the
       single-function dialog says NOTHING was analysed, as it does for `blueprint_no_script`.
     - No contract bump: `method` is a pipe string the UI already reads; the mailbox is untouched.
     - **Tests, red first:** a third row in the batch theory, and a `dll_core_test` block. An
       unreadable Script buffer is tagged `bytecode_unreadable`; the control, a readable Script with
       no anchor opcode, stays `bytecode`. 2/2 mutants killed (one UI, one DLL); UI 5090/5090; dll_core_test 191/191.
   - ✅ **Review 3 follow-up 2026-09-12** (`bytecode-unreadable-doc-refusal`, LOW, CONFIRMED). Doc-only.
     - The model's doc still said "the last one is a REFUSAL" after `bytecode_unreadable` was appended, so
       it gave the new tag `blueprint_no_script`'s interpreter rationale.
     - It now names both refusals and each one's cause.
     - The DLL-side lists, Aura.h's `FunctionPropRefResult::method` and Fern's `walk_function_props`
       comment, now carry the two values.

**LOW** — 2 rows: ✅ `[W3-CAP-NOSAVE]` (FIXED IN SOURCE 2026-09-12, batch L30: both caps added to their persist sets, plus a symmetry pin over every BuildOptions line so the next instance fails a test; red first) `PropertySearchCap` and `ClassListCap` round-trip through
`ApplyOptions`/`BuildOptions` but are in **neither** persist set, so `Track()` never calls
`ScheduleOptionSave()` — raising a cap and touching nothing else writes nothing to disk (**found
independently by both APP-SHELL lenses**) · ✅ `[W3-DIP-PIXELS]` (FIXED IN SOURCE 2026-09-12, batch L31: the state machine is given the window's RenderScaling through `SetScale`, pushed with every `SetScreens`; the stash stays in DIPs and only the visibility test converts; red first with the AF21 geometry) `WindowRestoreState.PositionAcceptable`
passes DIP-sized `_pendW`/`_pendH` straight into `WindowPlacement.IsVisibleEnough`, whose header
states *"All coordinates are PHYSICAL pixels"* — the audit-#5 **AF21** unit fix landed in
`MainWindow` and never in this newer 100%-band twin. ⛔ The tempting mechanical fix is harmful; the
safe form is to give `WindowRestoreState` a scale it does not currently have.

#### Filed by the fix-pass review of 3561c93c (2026-09-11)

##### ✅ `[W3-DUNSTE-QUEUED]` MED — Fly / Noclip reads a queued collision-disable (`-5`) as refused, so it lands after the record says collision is ON (FIXED IN SOURCE 2026-09-12)

`Dunste.cpp:231` (`InvokeSetCollision`). The DLL-side twin of `[W3-CONSOLE-REINVOKE]`, breaking the
same contract.
- Every `rc != 0` maps to `CollisionApply::Refused`, `-5` included. The log even says "-5
  game-thread timeout … collision unchanged, will retry", and `Dunste.h:58-61` files `-5` under
  "the dispatcher did not run it … must NOT commit".
- But on `-5` the request is STILL QUEUED and will run (`Frieren.h:117-130`, `Stark.cpp:416-423`).
  `s_state.collisionOff` does not move, so nothing tracks a `SetActorEnableCollision(false)` that
  is going to execute.
- **Scenario** (the review's, re-read at source), on an idle-when-unfocused game, which
  `Dunste.cpp:606-612` itself calls the common case:
  1. Clicking the UI's Noclip checkbox takes focus from the game. The next worker tick still passes
     `IsGameThreadResponsive()`, because the last PE fire was under 500 ms ago.
  2. The disable is enqueued, the game thread has stopped, and after 5000 ms it returns `-5`:
     Refused, and `collisionOff` stays false.
  3. Still in the UI, the user unticks Noclip or disables Fly. With `collisionOff == false`, no
     restore is emitted.
  4. Back in the game, the queued disable drains. Collision is OFF with Fly OFF and nothing
     tracking it, and the pawn falls through the world.
- **Origin:** fc83923e (D1, 2026-09-08) turned the discarded return into `CollisionApply` and put
  every non-zero code, `-5` included, in the non-committing class. Before D1 the worker committed
  whenever the setter was found, which happened to be right for `-5`.
- ✅ **Probable fix:** a third outcome, "queued, will land", that commits the record, so the next
  opposite toggle emits the undo. Keep Refused for `-8` / `-7` / `-3` / `-2` / `-4`, and make
  `Dunste.h`'s doc agree with `Frieren.h`.
- ✅ **FIXED IN SOURCE 2026-09-12, the probable fix** (batch B31).
  - `CollisionApply::Queued` is the third outcome. The pure `Dunste::CollisionApplyFromRc` maps -5 to it
    and every other non-zero code (-8 / -7 / -3 / -2 / -4) to Refused. `ShouldCommitCollision` commits
    it, so the record moves and the next opposite toggle emits the undo.
  - `InvokeSetCollision` logs "QUEUED … it will run when the thread drains" instead of "collision
    unchanged".
  - `Dunste.h`'s doc now agrees with `Frieren.h`: -5 is no longer listed as "must NOT commit".
  - The two restore paths already commit anything that is not Refused. A queued restore drains AFTER
    the queued disable, so the order is right.
  - **Tests, red first** (`dll_helpers_test`, against the pre-fix mapping): -5 maps to Queued, and a
    queued request commits. The other codes staying Refused, and 0 → Applied, are the controls.
    2/2 mutants killed; dll_helpers_test 2708/2708; UI 5134/5134.
  - ⚠ **Survivor by construction:** Dunste.cpp's use of the mapping, which no test target compiles.
    The real `UE5Dumper` build and the live check cover it.
  - ⬜ The debug-camera twin `[W3-DEBUGCAM-QUEUED]` (LOW, next) is separate.
  - ✅ **Review 5 follow-up 2026-09-12** (of 9805fea8: two LOW, both CONFIRMED).
    - **The D1 live rig would have reported a false regression.** `d1_collision_refusal.py` forces its
      "refused" arm with a -5 timeout, and -5 is now Queued. So the record commits, and the rig's arm 1
      printed "the record was DROPPED, which is the D1 defect itself" on exactly what B31 intends. Arm 1 now
      asserts the QUEUED path: the QUEUED line, and no kept record. D1's refused arm has no live trigger
      left (-8 cannot occur on the pipe thread), so `Test_Dunste_ShouldCommitCollision` alone pins it.
    - **A dll_helpers_test comment** in the function B31 edited still listed -5 among the refusals. Fixed.

##### ✅ `[W3-DEBUGCAM-QUEUED]` LOW — `UE5_SetDebugCamera` folds a queued toggle (`-5`) into `-1`, and every caller invites a second toggle (FIXED IN SOURCE 2026-09-12)

`Frieren.cpp:1248`. The stateful `ToggleDebugCamera` goes through the queued `UE5_CallProcessEvent`,
and any `r != 0`, `-5` included, returns `-1`. Every consumer reads `-1` as "nothing happened":
- both view models say "no live CheatManager / unreadable state (enter gameplay first)";
- the CE Debug Camera record says the game refused the toggle, and UNTICKS itself.

With the game stalled, Force ON queues a toggle and reports failure. A second Force ON still reads
the state as OFF and queues another. On refocus both drain, ON then OFF, against an API documented
as idempotent.
- Pre-existing and outside B11's scope. The refuter judged it LOW: the re-send is prompted, not
  automatic. The 2026-08-13 audit refuted "escalate to the swap on `-5`"; it did not address this.
- ✅ **Probable fix:** return a distinct "queued" code, and have both view models and the CE script
  say "queued; it will run when the game thread is free — do not re-send", without unticking.
  ⚠ Refusing to enqueue while `Stark::IsGameThreadResponsive()` is false is NOT enough on its own
  (review of c8d409f4): its 500 ms grace lets the FIRST press through, the same mechanism as
  `[W3-DUNSTE-QUEUED]` step 1, so it only stops the second toggle. The distinct code is the only fix
  that covers both the misreport and the CE untick.
  ⚠ A new return code reaches the CE script, so check `Mimic.h`'s contract rules first.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L43), the probable fix:
  - `Stark::StatefulToggleFailure` (header-inline) passes a timed-out `-5` through as
    `kInvokeTimedOutStillQueued`; every other failure is still `-1`. `UE5_SetDebugCamera` returns it.
  - Mimic's `CMD_SET_DEBUG_CAMERA` puts "toggle queued -- do not re-send" in `errorMsg`.
  - Both view models show an amber **Queued** badge and "do not press Force again: a second toggle
    would undo the first" (`Constants.DebugCameraToggleQueuedResult`).
  - The CE record tests `-5` BEFORE `state ~= req`. `[ENABLE]` says it is queued and does **not**
    untick; `[DISABLE]` only `dbg()`s it (`[W2-CEGEN-MODAL]`). Neither closes the window.
  - Docs: Frieren.h, Mimic.h CMD 7, Fern's pipe reply, IDumpService, dll-spec, and
    `ue5_invoke_helper.lua` (its `[ENABLE]` example unticked on `-5` too).
  - **Not a contract bump**, following Mimic.cpp's MB3: a new negative result code is not a contract
    change, because every script treats non-zero as failure and renders `errorMsg`. `[W3-CONSOLE-REINVOKE]`
    already passes `-5` raw. An old `.CT` reads `-5` as a failure and unticks, which is today's
    behaviour, not a break. The surface hash does not move (comments only).
    ⚠ **Recorded, not resolved:** Mimic.h's own list says item 4, "Status / InitState values, or
    result-code meanings", IS contract. MB3 and item 4 disagree about new negative codes; one of the
    two should be reworded.
  - **Red first:**
    - dll_helpers_test DBGCAMQ pins the mapper against an inert stub, with `-4` / `-7` controls.
    - ConsoleViewModelTests and TeleportViewModelTests check the badge and the text.
    - DebugCameraScriptGeneratorTests pins that the queued branch comes first, never unticks and
      never closes. EveryEnableBailout's 8-line window cannot tell, because the failure branch's untick
      sits right below the queued message.
    - An InvokeScriptTests source pin covers Frieren and Mimic.

---

#### ⛔ REFUTED — do not re-raise

- **ASB-1** *"the experimental-tab lock covers 3 of 4 tabs; Detect Player Stats is missing"*. Every
  cited fact is true — `MainWindow.axaml.cs:734` really does list only three tags, and
  `"DetectStats"` really is consumed nowhere else — but **the harm does not exist**: the only
  toggle lives on the System tab, so the hidden tab is never the selected tab, and the lock is
  documented as session-only, making the same end state deliberately reachable.
  ⚠ *Citation slip in the finding worth noting: there is no `MainTabIndex.cs`; the enum is at
  `MainWindowViewModel.cs:22-47`.*
- **ASB-3** *"ColorPickerDialog's hue strip latches into permanent drag mode"*. The code shape is
  as cited and the dialog is production-reachable, but the load-bearing premise — *"a bare `Border`
  does not capture the pointer, unlike `Button`"* — **is factually wrong for Avalonia**. Settled by
  **decompiling the exact assemblies the csproj references** (Avalonia 12.1.1 /
  Avalonia.Win32 12.1.1, via `ilspycmd` out of the NuGet cache) and reading
  `MouseDevice.MouseDown`, `Pointer.Capture` and `WindowsMousePointer` — not by argument.

#### The `implied_fix_safe` field fired again — 4 of 6

W2 introduced it and it hit 5/10; W3 hits **4/6**. Across the two waves it has now flagged a
harmful or partly-harmful obvious repair on **nine of sixteen confirmed rows**. It is no longer an
experiment: ⬜ **make it a permanent part of the verdict schema for W4 and W5**, and treat any
confirmed row without it as un-triaged.

#### Calibration

6/8 confirmed, against W1's 13/16 and W2's 10/13. **The falling raw count is the signal to watch,
not the ratio**: APP-SHELL-A opened 46 files and filed two LOW rows, because it kept finding that
the code was right and said so with a script. Eleven `clean_areas` entries, several of them
measured tree-wide, are worth more than eleven speculative findings would have been.

### ✅ W4 SWEPT 2026-09-10 `[BLANK-W4-2026-09-10]` — 12 raised, 6 confirmed, 5 refuted, 1 undecided

3 auditors over **9,780 never-audited lines**, including `Aura.cpp`'s 4,610 across 269 hunks — the
**largest single never-audited block in the repository**. Aura read through two lenses over the
same 4 files: *is the computation right?* against *does the answer survive the journey out?*
6 refuters. 10 agents, ~1.9M tokens. **Nothing fixed.**

| | MED | LOW | REFUTED | UNDECIDED |
|---|---|---|---|---|
| AURA-ENGINE | — | — | AE-1 · AE-2 · AE-3 | — |
| AURA-WIRE | AW-1 · AW-3 | — | AW-4 | AW-2 |
| LIVEWALKER | LW-1 · LW-2 · LW-3 | LW-4 | LW-5 | — |

⚠ **Two cross-lens duplicate pairs** (AE-3≈AW-2 at `Aura.cpp:8621`; AE-2≈AW-4 at `:8906`), so
**10 distinct** were raised. All 6 confirmed rows are distinct.

---

#### ⭐⭐ THE ANSWER ON THE BIGGEST NEVER-AUDITED BLOCK: the algorithms are sound; the defects are all on the publishing side

**AURA-ENGINE filed three findings and all three were refuted.** **AURA-WIRE's two survived.** That
contrast, over the same four files read at the same time, is the wave's most useful result — and it
is the third independent confirmation that *"computed and never published"* is this codebase's
dominant defect shape, not *"computed wrongly"*.

The engine lens's fourteen `clean_areas` are the real product. The ones that close real risk:

- **`ParallelIndexRanges` partition arithmetic — VERIFIED COMPLETE AND DISJOINT** for every
  `nthreads` in [1,16] and every `count`. This was named in the brief as the highest-risk item in
  the wave (*"a boundary defect here is invisible in testing and silently loses objects"*). It is
  not there: the union of ranges is exactly `[0, count)`, no index visited twice, none lost at the
  last chunk.
- **`ConcatTruncate` genuinely reproduces the serial "lowest-index N"** — ranges ascending and
  disjoint, each worker self-capping, so tid-order concatenation is globally ascending; the
  truncation keeps the correct prefix even when a worker returned early or faulted mid-vector.
- **Cooperative cancel verified at ALL SEVEN call sites**, each with a *chunk-relative* stride so an
  off-aligned worker polls on its first iteration.
- ⭐ **`workerFaulted`: six of seven sites fold it; the seventh is the already-recorded
  `[W1-SNAP-FAULT]`. NO NEW UNFOLDED SITE EXISTS.** The brief explicitly invited "the same shape at
  a new call site" — the answer came back **negative**, which is exactly the kind of bounded
  result a sweep should be able to produce.
- **`GraphPath.h` (191 lines, 100% band, never named by any audit)** — no termination, cycle or
  connectivity defect in the BFS core. Cycles cannot loop (visited inserted at *discovery*), the
  visited cap is checked before emplace, depth semantics match the doc, and the pure core never
  emits a `found` path whose steps fail to connect root→target.
- **LWC: no width is derived from a UE version check anywhere in the band.** Every vector read takes
  its width from a reflected size and refuses anything else.
- **Memory safety: no bare `reinterpret_cast` deref of game memory anywhere**; every allocation
  sized from game memory is clamped (`scriptNum` to 1<<22, TArray count to `0x100000`, the Native-C
  window to `0x10000`).
- **Kismet-bytecode index arithmetic: every buffer index proved in range, no off-by-one** across
  four separate walkers.

---

#### The fix list — 6 rows, none repaired

**MED** — 5 rows.

1. ✅ **`[W4-STRIDE-TENTATIVE]`** (FIXED IN SOURCE 2026-09-12, batch B27) `Aura.cpp:1235`. `DetectItemSize` has **four** outcomes and
   publishes **one**. The packed verdict reaches the wire, the UI badge and every dump's
   `packed_unverified` stamp — but the **tentative** and **could-not-detect** verdicts are spent on
   log lines. A user cannot tell a confident detection from a guess.
   ✅ **FIXED IN SOURCE 2026-09-12** (batch B27). The verdict has its own field, `item_detect`, ORTHOGONAL to
   `item_layout_mode`. That is the recorded unsafe fix inverted: a guessed stride is still classed classic /
   unpacked57 / packed57.
   - **DLL.** `Aura::GetItemDetect()` returns one of four values, plus the validated count out of the
     200-probe budget:
     - `detected`: the preset hint, a direct pass, or the packed probe cleared the gate;
     - `tentative`: the weak fallback;
     - `undetected`: nothing validated, so the default stride is in use;
     - `forced`: `InitWithExtendedLayout`.

     Fern publishes `item_detect` / `item_detect_validated` / `item_detect_probes` beside
     `item_layout_mode` at all three sites (get_pointers, set_packed_consts, get_offsets). An additive wire
     key: no contract bump.
   - **Reset at ENTRY** (the recorded requirement below). The verdict, the layout mode, the object-pointer
     offset and the stride are reset at the top of `DetectItemSize`, BEFORE its early returns. A re-init that
     cannot read its chunk table now reads `undetected` / classic, never the previous candidate's packed
     layout.
   - **UI.** `EngineState` carries it; "" from an older DLL reads as no warning.
     - A top-bar badge beside the packed one names a tentative stride's "N of 200 probes validated", or says
       the stride was not detected.
     - Every dump's meta line gains `item_detect` and `stride_untrusted`, beside `packed_unverified`.
   - **Tests, red first** against inert accessors and properties:
     - dll_core_test re-initialises Aura on throwaway arrays: five clean items are `detected` (5
       validated), one lone item is `tentative` (1 validated), and the main pool is `forced`. A re-init with
       an unreadable chunk table, after packed mode was forced on, resets to classic and reads `undetected`.
     - The parse, the badge (and its raise), and the dump stamp.

     12/12 mutants killed; dll_core_test 237/237; UI 5129/5129.
   - ⚠ **Documented survivors:** the preset-hint and packed `detected` lines (no fixture reaches either),
     Fern's three publish sites (no test target compiles Fern.cpp), and the MainWindow mirror and AXAML
     badge (no MainWindow harness). The real `UE5Dumper` build and the live check cover them.
   - ⬜ **Not in this row:** CE XML / CSX exports do not yet carry a stride note the way they carry
     `PackedLayoutNotice`.
   - ✅ **Review 4 follow-up 2026-09-12, the reset pins** (of d76732d3: LOW, CONFIRMED). The reset-at-entry
     test pinned 2 of the 5 resets. The run before the unreadable re-init left the stride at 16, the
     object offset at +0x00 and a validated count nothing read. Those are the values the reset writes, so
     dropping any of those three passed, and the "12/12 mutants" above never tried them. A fixture now
     leaves all three unlike the defaults: five objects at stride 20 with the pointer at +0x08, which only
     the +0x08 pass detects. The unreadable re-init must put back 16, +0x00 and 0. Red against those three
     resets removed; 3/3 mutants killed; dll_core_test 260/260; UI 5152/5152.
   - ✅ **Review 4 follow-up 2026-09-12, the probe count** (of d76732d3: LOW, CONFIRMED).
     `item_detect_probes` was the 200-probe budget constant, but a pass won in a deep phase (P2-deep,
     P3-flat-deep) probes 100. On a UE4 title whose chunk opens with null slots, a tentative stride read
     "30 of 200" where its pass had validated 30 of 100: half the ratio the badge exists to show.
     - `DetectStrideForCurrentObjOffset` returns the winning phase's own probe count. The weak fallback keeps
       the strongest pass's, and the alias warning divides by it too. The preset hint publishes its 200, the
       packed probe its own count, and forced and undetected publish 0.
     - **Tests, red first:** a deep-phase detection reads 100, a deep-phase tentative stride reads "1 of
       100", and a forced stride and an unreadable re-init read 0. P1's detection still reads 200.
       7/7 mutants killed; dll_core_test 266/266; UI 5152/5152.
     - ⚠ **Documented survivors:** the preset-hint and packed counts (no fixture reaches either), the
       P0-flat / P3-flat assignments, and the alias warning (log-only).
2. ✅ **`[W4-RELATED-STOPS]`** (FIXED IN SOURCE 2026-09-11, batch B26) `Aura.cpp:9092`. `GetRelatedObjects` has **four** stop conditions
   (`maxResults` 128, `kMaxOwnedSubs` 128, `kMaxVisited` 200000, and an 8 s deadline *or*
   `Tot::Requested()`) and publishes **none** — it returns a bare `std::vector<RelatedObject>` with
   no stats struct and no member to carry one. The Related Objects panel renders a cut-off
   enumeration as the complete one.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B26). One flag PER CAUSE, five of them; not the recorded unsafe
   fix of one "stopped early" boolean (P5).
   - **DLL.** `GetRelatedObjects` takes an optional `RelatedObjectsStats*`, so Solide's caller is unchanged.
     - The flags are `resultCapHit`, `ownedCapHit`, `visitCapHit` and `deadlineHit`, plus `cancelled`:
       `Tot::Requested()` is split from the deadline it shared one `aborted()` with. The effective bounds
       travel with them.
     - A cap flag means a qualifying object was actually REFUSED. The caps are now asked only of an object
       that passed the ownership and class filters. The frontier loop no longer stops merely because the
       list is full, so a list that exactly fills its cap is reported complete.
     - Fern publishes them as `stops`, one key per cause. An additive wire key: no contract bump, and the CE
       mailbox is untouched.
     - A `RelatedObjectsLimits` seam carries the three bounds. Its defaults are the shipped 128 / 200000 /
       8000 ms, and only dll_core_test passes it.
   - **UI.** `RelatedObjectsResult.Stops`, null from an older DLL. The panel's status appends
     `PartialResultNotice.RelatedStopsClause`, which names each cause in its own words: a refused object
     means more EXIST, while a spent budget or a cancel means more MAY exist.
   - **Tests, red first** against the inert stats param:
     - dll_core_test walks a fake owned graph and trips each bound: the row cap (on an owned part and on the
       Class row), the owned cap, the pointer budget, the deadline, a cancel. Controls: a finished walk, and a
       list that exactly fills its cap.
     - The parse, the clause and the panel's status.

     15/15 mutants killed; dll_core_test 230/230; UI 5120/5120.
   - ⚠ **Documented survivor:** Fern.cpp's serialiser, which no test target compiles. The real `UE5Dumper`
     build and the live check cover it.
   - ⬜ **Not in this row:** Solide's `FindStealthMeter` still ignores the stats. A stealth meter on a
     129th owned object goes unscored, silently; that is a separate scope call.
   - ✅ **Review 4 follow-up 2026-09-12** (of d6a3af46: two LOW, both CONFIRMED).
     - **The new cap order was unpinned.** The fake graph never produced an edge that fails the filters
       once the list is full: its parts own nothing, and every edge of the target qualifies. A mutant that
       put the caps back above the filters passed all 16 checks. A second holder's Parts array now ends with
       a fifth element it does not own, enumerated after the four parts that fill the list. At a row cap of
       exactly 6, and at an owned budget of 4, that edge must not read as a refusal. Red against the pre-fix
       order; 2/2 mutants killed; dll_core_test 256/256; UI 5152/5152.
     - **Live check L32 step 2 was wrong.** It expected the PersistentLevel to fill the list, because "it
       owns every actor". This walk follows REFLECTED pointers only, and `ULevel::Actors` carries no
       UPROPERTY (`Aura.cpp` records this beside its ULevel lookup), so the level's actors are never
       reached. The step now asks for any object that lists 128 rows. A game without one records the step
       as not reachable, not as a failure.
3. ✅ **`[W4-RELATED-RACE]`** (FIXED IN SOURCE 2026-09-11, batch B17) `RelatedObjectsViewModel.cs:106`. `LoadAsync` clears before its
   `await` and appends after, with **no generation ticket** — the only VM in the cluster without
   one. Two overlapping loads both pass their `Clear()` and both `Add()`, so the grid holds object
   A's related graph concatenated with object B's under one header.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B17).
   - `LoadAsync` takes a generation ticket (`_loadGen`), as `ObjectTreeViewModel` and
     `InstanceFinderViewModel` do. A superseded load drops its result, never overwrites the newer
     load's status with its own failure, and never clears the newer load's `IsBusy`.
   - `ClearOnDisconnect` bumps the ticket too, so a load in flight at disconnect cannot bring the
     previous game's graph back (X5's promise, in the same VM).
   - ⛔ Not `if (IsBusy) return;`, as the unsafe-fix table says.
   - **Tests, red first:** four, over a gated fake that lets two loads finish out of order: the stale
     one lands last; lands first; fails; lands after a disconnect.
   - ✅ **Review follow-up 2026-09-11** (adversarial review of B14–B21: two MED findings, both CONFIRMED).
     - **`related-disconnect-busy-stuck`, a regression B17 introduced.** `ClearOnDisconnect` bumped the
       ticket but never took over `IsBusy`, and the superseded load's finally skips it by design. So a
       load in flight at disconnect left `IsBusy` stuck on. `ClearOnDisconnect` now clears it: whoever
       bumps the generation owns the busy flag (the trap `SupersedeClassSearch` documents).
     - **`related-detect-unticketed`.** `DetectTargetAsync` had no ticket:
       - its unconditional finally cleared a newer load's `IsBusy`;
       - its post-await auto-load overwrote a later handoff;
       - a detect in flight at disconnect repopulated the candidates.

       It now takes a ticket and checks it after the detector. The latest action owns the panel, so a
       Detect also supersedes a load handed off before it.
     - **Tests, red first:**
       - a disconnect during a load;
       - a handoff during Detect;
       - a Detect landing after a disconnect;
       - a Detect that finds nothing, superseding an earlier load.

       4/4 mutants killed; UI 5083/5083.
4. ✅ **`[W4-BOOKMARK-DT]`** (FIXED IN SOURCE 2026-09-11, batch B19) `LiveWalkerViewModel.cs:4173`. `PersistedCrumb` carries
   `IsContainerView` and **not** `IsDataTableView`, so a bookmark saved on a DataTable row view can
   never be restored — and the failure is reported as *"the game may have restarted"*, blaming the
   user's session for a serialisation gap.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B19): the safe `PersistedCrumb` half, plus a GUARDED re-walk.
   - `PersistedCrumb.IsDataTableView` is written and read back; older files read it as false.
   - A DataTable row view restored from the FILE has no rows (they are live-only), so the load path
     re-walks them at the saved address. Two guards make that safe:
     - the DLL refuses an address that is not a DataTable (`Ubel::WalkDataTableRows`);
     - the row struct must be the one saved (`DataTable<RowStruct>` is the view's class name).
     A different table at that address, or a refusal, now reads "the DataTable at its saved address is
     gone or has changed", never "the game may have restarted".
   - The restored crumb gets the in-session shape (rows plus the synthetic RowMap field, shared with the
     live load through `SyntheticRowMapField`), so Refresh and Back treat it the same.
   - ⚠ The unguarded re-walk is the recorded-dangerous half, and is not what landed.
   - ⬜ Left as is: across a GAME restart the DataTable crumb still stops the spine re-resolution, so
     the saved address is used and the guards report it gone. Re-resolving the table from the live
     world first would be a separate change.
   - **Tests, red first:** the flag survives the file; a file-shaped crumb re-walks its rows; a table
     that changed, or an address the DLL refuses, is reported honestly. The in-session control uses its
     cached rows and makes no walk.
   - ✅ **Review follow-up 2026-09-11** (adversarial review of B14–B21, finding `bmdt-refresh-bypass`,
     MED, CONFIRMED).
     - **The problem.** When the guard rejects, the file-shaped crumb (no rows) stays at the end of the
       spine. Refresh's DataTable branch then re-walked that saved address with NO row-struct check.
     - So "the unguarded re-walk is not what landed" held for the load path only: the wrong table was
       one Refresh away (the button, an Array Limit nudge, or the auto-refresh).
     - **The fix.** Refresh now refuses a DataTable crumb with no rows, in the bookmark's own words. An
       in-session crumb always holds rows, and so does an accepted re-walk.
     - **Tests.** A changed table and a refused address went red first. 2/2 mutants killed; UI 5085/5085.
5. ✅ **`[W4-LOOKUP-FILTER]`** (FIXED IN SOURCE 2026-09-11, batch B18) `InstanceFinderViewModel.cs:620`. A reverse-address lookup empties
   `_allInstances` on purpose and adds its single result straight into the bound collection; the
   next `ApplyInstanceFilter` re-projects unconditionally from the now-empty backing list, so a
   leftover keyword **permanently erases the result while the status line still reports a match**.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B18).
   - The lookup result goes into the BACKING list (`_allInstances`) as well as the bound one, so every
     later re-projection sees it. A leftover keyword that does not match now hides it with the usual
     "1 hidden by filter" note, and clearing the keyword brings it back.
   - That also takes the harm out of the recorded unsafe fix: re-entering the filter only erased the
     result because the backing list was empty. The lookup still does NOT clear the keyword.
   - **Tests, red first:** the result survives a filter pass; hidden by a leftover keyword, it is
     reported, and it comes back when the keyword is cleared.
   - ✅ **Review follow-up 2026-09-11** (`lookup-allinstances-doc-stale`, LOW): `_allInstances`' doc
     still said only the class search fills it and the lookup clears it. It now says the lookup
     REPLACES it, and names `_hasActiveClassSearch` as the "is a class search active" signal. Doc only.

**LOW** — 1 row: ✅ `[W4-HEXSORT]` (FIXED IN SOURCE 2026-09-12, batch L32: the seven ADDRESS columns sort numerically through a `ulong` accessor and `DataGridSortComparers.Hex`, and so does an eighth outside the cluster, Class / Struct's field Address, which the new address-column pin found. The two `HexValue` columns stay text: each is a raw byte dump in memory order, where text order is memcmp order and the refuted ulong parse fails, and they are exempted with that reason. Red first) nine address/hex `DataGrid` columns in this cluster sort as **text**
(`InstanceFinderPanel.axaml:226/:136/:388/:391`, `LiveWalkerPanel.axaml:651/:654/:657/:455/:930`).

---

#### ⛔⛔ ALL SIX CONFIRMED ROWS HAVE AN UNSAFE OBVIOUS FIX — 6 of 6

`implied_fix_safe` hit **5/10** in W2, **4/6** in W3 and now **6/6**. Cumulative: **15 of 22**
confirmed rows across three waves carry a harmful or partly-harmful obvious repair. Highlights:

| row | the obvious fix | why not |
|---|---|---|
| `[W4-STRIDE-TENTATIVE]` | add a `"tentative"` value to `item_layout_mode` | ⛔ wrong shape — that field is a 3-value **layout** descriptor and tentativeness is **orthogonal**; a tentative detection is still classed |
| `[W4-RELATED-STOPS]` | one boolean for "we stopped early" | ⛔ that is **P5**, the conflation `docs/todo.md:1314` is already open about and `[W3-XREF-CAP]` flags — **four** conditions fire here and `Tot::Requested()` is one of them |
| `[W4-RELATED-RACE]` ✅ B17 | `if (IsBusy) return;` | ⛔ `DetectTargetAsync` sets `IsBusy = true` **then** awaits `LoadForAddress`, so that guard deadlocks the legitimate path |
| `[W4-BOOKMARK-DT]` ✅ B19 | the finding's own recommended second half | ⛔ dangerous — only the `PersistedCrumb` half is safe |
| `[W4-LOOKUP-FILTER]` ✅ B18 | clear `InstanceFilterText` inside the lookup | ⛔ actively harmful — it is an `[ObservableProperty]`, so the assignment re-enters the filter |
| `[W4-HEXSORT]` | wire `DataGridSortComparers.Hex` onto `HexValue` | ⛔ unsound — `ulong.TryParse` with `NumberStyles.HexNumber` fails on the dump formats actually present |

⭐ **This is now the single most important input to the coming fix pass**: on current evidence the
obvious repair is wrong more often than it is right. ⬜ **No row gets repaired without reading its
`implied_fix_safe` first.**

---

#### 🟡 UNDECIDED — `[W4-DEEPWALK-750MS]` `Aura.cpp:8621`, and the experiment is cheap

Snapshot capture bounds each object's deep container walk with a **750 ms wall-clock** backstop and
publishes nothing — the one non-deterministic limit in a subsystem whose `WalkLeafLimits` header
(`:2426-2435`) states the determinism rationale **verbatim**, *"unlike a wall-clock deadline"*.
The mechanism is verified; the harm's reachability at HEAD is not, and the finding's only evidence
for it is a **pre-fix** measurement of the very population the fix bounds.

⭐ **The refuter designed an experiment needing no rebuild and no instrumentation**: capture snapshot
A of a frozen game state on an idle machine, then snapshot B of the **same frozen state** under
synthetic CPU+IO load, and compare per-key element counts across the two SQLite databases. If they
differ, the backstop is reachable in practice and this becomes a real row.

#### ⛔ REFUTED — do not re-raise

- **AE-1** (filed HIGH) *"`RecoverViaWorldLevel` returns `found=true`/`ok_via_level` for a path that
  stops at the owning actor"*. ⭐ **Already on a binding refuted list** —
  `docs/audit-2026-08-13-early-code-findings.md:2722` — and the refuter **proved it is the same
  site, not a drifted line number**: `git show 5560b107:dll/src/Aura.cpp` line 3838 is
  byte-identical to today's `:4341`. Also deliberate twice over: the comment three lines above says
  *"landing on the owner is useful"*, and `git log -S` finds commit `65f0c75f`, whose body
  enumerates it under *"Honest limits (not bugs)"* and describes the exact UI coupling the finding
  called a violation as the stated design.
- **AE-2 / AW-4** *"`AppendOwnedSubObjectLeaves` never got the abort/visited guard its twin was
  given"* — rejected on both lenses.
- **AE-3** *"the 750 ms backstop truncates at a different element each run"* — the engine lens's
  framing was refuted; the wire lens's framing of the same site survives as the UNDECIDED above.
  ⚠ Same line, two framings, two different verdicts: keep both facts.
- **LW-5** *"Batch Find Func caches a thrown scan as `—`"*.

#### Calibration

6/12 confirmed — the lowest ratio of the sweep (W1 13/16, W2 10/13, W3 6/8), and the **first wave
where a HIGH was refuted**. That is the do-not-re-raise machinery working: the brief named five
recorded rows with their exact lines, and the refuters caught the sixth from a list the brief only
pointed at. ⚠ The falling ratio does **not** mean the code is cleaner — it means the code was
already picked over by three earlier waves, exactly as designed.

### ✅ W5 SWEPT 2026-09-10 `[BLANK-W5-2026-09-10]` — 8 raised, 5 confirmed, 3 refuted — THE JUNE BLANK IS CLOSED

3 auditors over the **last 9,744** never-audited lines: WIRE (all three DLL transports in one
cluster), EXPORT, SCAN-CORE+DLL-OTHER. Each auditor also **piloted the eight-shape pattern
catalogue** (`pattern_sweep`, mandatory). 5 refuters. 8 agents, ~1.8M tokens. **Nothing fixed.**

| | MED | LOW | REFUTED |
|---|---|---|---|
| WIRE | — | — | WR-1 · WR-2 |
| EXPORT | EX-1 | EX-2 · EX-3 | — |
| SCAN-CORE | — | SC-2 · SC-3 | SC-1 |

---

#### ⭐⭐ The pattern-catalogue pilot worked — and it produced BOUNDED NEGATIVES

Instances each auditor reported per shape, **before** refutation:

```
cluster      P1   P2   P3   P4   P5   P6   P7   P8
WIRE          2    0    3    0    0    0    0    0
EXPORT        1    1    2    0    0    0    1    0
SCAN-CORE     1    0    1    0    0    0    0    0
```

- **P3 was enumerated MECHANICALLY, not sampled.** The WIRE auditor extracted every module symbol
  each transport references — **`Fern` 209, `Mimic` 36, `Frieren` 90** distinct `Ns::Symbol`s —
  and intersected them: **18 functions are reachable from all three** (16 feature-level). For
  those plus the 7 that `Mimic` reaches *through* a `Frieren` export, it compared every call
  site's argument list and every published result field. That is the systematic version of W2's
  headline, and it is now a population with a known size.
- ⛔ **The FP1 residual goes one hop further than W2 recorded.** `Mimic.cpp:1074`
  (`TP_OP_GET_POSE`) and `Frieren.cpp:1280` (`UE5_TeleportGetPose`) both pass `nullptr` for
  `outParentRelative` — the degraded-read flag is **unrequested at the transport level**, not only
  inside `Wirbel`'s save paths. ✅ The mailbox half folded into `[W2-MARKER-PARENTREL]` (B29b). ⬜ The C ABI
  half is `[A2-CABI-TELEPORT-PARENTREL]` (review 5).
- **P3 is W5's dominant confirmed shape** — three of five confirmed rows (EX-1, EX-2, SC-2), all
  *a prior fix that reached some of its consumers and not the rest*.
- **P1: "the pipe is mostly GOOD at this."** `search_properties` publishes both `truncated` and
  `aborted`; `list_all_functions` publishes `truncated` + `aborted` + `limit`; `find_by_address`
  publishes a full `container_scan` stats block precisely so a clean miss can be told from a
  cut-off. **The confirmed P1 rows are outliers, not the rule** — which is what makes them fixable
  by copying the local convention.
- ⭐⭐ **P5 HAS A TEMPLATE, AND THE FIX PASS SHOULD COPY IT.** `begin_group_scan` publishes
  `deadline_hit`, `per_slot_cap_hit` and `per_slot_cap` as **three distinct fields for three
  distinct causes**, with an in-code comment explaining why they must not merge
  (`Fern.cpp:3609-3616`). That is the correct shape for every P5 row on the fix list. And one merge
  that *looks* like P5 is **correct**: `LI_OUT_TRUNCATED` folds cap+abort into one bit because
  `Mimic.h:243-246` defines it as *"the returned set is a PREFIX and more instances exist unheld"* —
  true of both causes, and the only thing a CE Lua freeze loop can act on.
- **P2 re-confirmed tree-wide a THIRD independent time** — the EXPORT auditor's `1` is the recorded
  `[W1-QUOTA-UNLIMITED]` row, not a new one. Three measurements (W1, W3, W5) now agree the
  whole-tree population is exactly one file.
- **P4, P6, P8: zero instances across all three clusters.** P6 has one UNDECIDED over an empty host:
  the entire watch subsystem has no consumer (`DumpService.WatchAsync`/`UnwatchAsync` are referenced
  only by their interface declaration and nothing subscribes to `IPipeClient.EventReceived`), so
  `interval_ms` having a 50 ms floor and **no ceiling** is a latent gap, not a defect.

---

#### The fix list — 5 rows, +1 filed while fixing B23 (each carries its own ✅/⬜)

**MED** — 2 rows.

1. ✅ **`[W5-CEXML-FSTRING]`** (FIXED IN SOURCE 2026-09-11, batch B23; its TArray half reached a live game only through the Live Walker's element fetch until `[W5-STRARRAY-ELEMENTS]`) `CeXmlExportService.cs:3414`. CE XML **drops the whole FString
   family** — `StrProperty` / `Utf8StrProperty` / `AnsiStrProperty` — when it is a **TMap key or
   value or a TArray element**, while the same type exports as a working CE String everywhere else.
   `MapInnerTypeToCeField` has no arm for them and returns `null`; `EmitMapProperty`'s
   `if (ceVal != null) EmitLeaf(...)` then emits **nothing**, and the per-element folder is written
   out empty. ⚠ P3 at the path level: the UE5.5 string-type work reached the scalar path and not the
   container-element path.
   ⛔ **Obvious fix is unsafe**: adding the three arms routes them into `EmitLeaf`, which cannot
   write `Length` / `Unicode` / `CodePage` / `ZeroTerminate` — a CE String needs those.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B23), the safe way: NOT arms in `MapInnerTypeToCeField`.
   - Every container path checks `IsStringProperty` first and calls `EmitContainerStringLeaf`, which
     is `EmitStringLeaf` with `Offsets=[0]`: the scalar path's encoding, one Data-pointer hop.
     `MapInnerTypeToCeField` now carries a ⛔ comment saying why the three types are absent.
   - **Four entrances, not two:**
     - the TArray element;
     - the TMap key, in BOTH the element group and the record-flatten branch;
     - the TMap value;
     - a struct-array element's member (`:3211`), found while fixing: it became a placeholder folder.
   - TSet already worked (its element reaches `EmitFields`) and is the control.
   - **FText folded in**, as the note below asked: a `TextProperty` arm with the scalar path's 8-byte
     hex (the ITextData pointer). No clean CE String encoding exists for it.
   - Copy CE Field's fabricated tail reaches string arrays too, by the generic path's rule.
   - **Tests, red first:**
     - the array theory (all three string types, with their Unicode / CodePage flags and the
       Offsets hop);
     - the map key and value, the flatten key, the struct member;
     - the FText array and the fabricated tail.
     The TSet element is the control. ⚠ The FText test's first form counted `8 Bytes` across the
     whole document and the export's root is 8 bytes too. It was red for the right reason (1, not
     2); it now pins each element, and its red is shown by the mutation check.
   - ⚠ **Corrected by the review of 110cbb4e (MED, CONFIRMED): the TArray half was NOT inert.** The walk
     sends a string array no inline elements, but the Live Walker's array drill then FETCHES them through
     `read_array_elements`, and Copy CE Field from that view reaches this branch, fabricated tail included.
     What the walk itself lacked (so a top-level Copy CE XML still wrote a placeholder) is
     `[W5-STRARRAY-ELEMENTS]`. The first form of this bullet said "inert" without tracing that route.
1b. ✅ **`[W5-STRARRAY-ELEMENTS]`** MED (filed 2026-09-11 while fixing B23; FIXED IN SOURCE 2026-09-11, batch B23b) `Ubel.cpp:2223`.
   `IsScalarArrayType` admits no string type, and no other walker phase reads a `TArray<FString>` /
   `<FUtf8String>` / `<FAnsiString>`. So the walk sends such an array with its count and NO elements.
   - ⚠ **Corrected by the review of 110cbb4e.** This row first said "the Live Walker shows no elements
     for it". It does show them: its drill fetches them through `read_array_elements`, which ran them
     through `ReadArrayElements`. That reader decodes raw element bytes, so each row showed the 16-byte
     header's hex with an EMPTY value. That fetch is the half of this row a user actually saw.
   - A top-level Copy CE XML of the object had no inline elements to iterate, so it wrote a placeholder.
   - Fix: a string phase in the walker, like Phases D–J. It decodes each element with `ReadFString` /
     `ReadFUtf8String` and is capped by the Array Limit, like every other phase.
   - Rather than widening `IsScalarArrayType`: its reader (`ReadArrayElements`) decodes raw element
     bytes, and a string's bytes are a header, not the text.
   ✅ **FIXED IN SOURCE 2026-09-11** (batch B23b, the recorded shape).
   - **Phase L**, `Ubel::IsStringArrayType` + `ReadStringArrayElements`:
     - it decodes each 16-byte header with the existing `ReadFString` / `ReadFUtf8String`;
     - the stride is pinned to the header, as Phase D pins 8, so a garbage `FPROPERTY_ELEMSIZE` cannot
       move it;
     - an unreadable header is `"???"`, never `""`, by the D3/D5 rule. A readable header whose text does
       not read still comes back `""`, exactly as a scalar FString field does.
   - Both walker branches call it after Phase K: FProperty mode, and the UE4 < 4.25 UProperty mode. The
     walk now carries a string array's elements inline.
   - **Fern's `read_array_elements` dispatches string inners to it.** That is the half a user saw: the
     Live Walker's drill fetches the elements through this command, and before the fix `ReadArrayElements`
     decoded every element's header bytes as a scalar, showing hex and an empty value.
   - ~~`InferScalarSize` has no string arm, so the walker keeps the engine's own element size: 16 for an
     FString inner. No change was needed there.~~ *Wrong, found by review 3. That holds only when the
     engine's value is right, which is the case the pinned stride exists NOT to rely on. See the follow-up
     below.*
   - **No UI change was needed.** CE XML's element leaves (B23), CSX's scalar-element path (a Data
     pointer with a string child) and the Live Walker's inline-vs-fetch routing all handle decoded string
     elements.
   - **Tests, red first** (against inert stubs, so they failed on behaviour):
     - a `dll_core_test` reader block: all three types, a garbage element size, the limit cap and an
       unreadable header;
     - a pool-faking `STRARRAYWALK` block pinning that the FProperty-mode walk calls the reader;
     - a CSX pin (green both ways) that a string array exports per element.

     5/5 mutants killed; dll_core_test 204/204; UI 5091/5091.
   - ⚠ **Two documented survivors, by construction:**
     - the UE4 UProperty-mode call site, which no test drives (it mirrors the FProperty one line for
       line);
     - Fern's dispatch, because Fern.cpp is compiled by no test target.

     Both are covered by building `UE5Dumper` and by the live check.
   - ✅ **Review follow-up 2026-09-12** (review 3, of 6b48e776: two LOW, both CONFIRMED).
     - **The stride was pinned inside the reader only.** The walk still PUBLISHED the inner's raw
       ELEMSIZE, and the UI lays element rows out by it: `Offset = i * ArrayElemSize`, CE XML's
       per-element leaves, CSX.
       - An in-range garbage value put every element after [0] at the wrong address.
       - A zeroed one made CSX place [1] at +1.

       Phase L now publishes the fixed 16-byte stride (`kStringArrayStride`) in both walker branches.
     - **Fern refused the drill fetch before its string dispatch.** `read_array_elements` checked
       `elem_size` (> 0, ≤ 256) first, so a zeroed or oversized value never reached the pinned reader.
       The string dispatch is now decided first, and those checks guard only a scalar stride.
     - **Test, red first:** a pool-faking `STRARRAYSTRIDE` block whose inner ELEMSIZE reads 0x18. The walk
       still decodes both elements, and now publishes 16. 1/1 mutants killed; dll_core_test 239/239; UI 5132/5132.
     - ⚠ Survivors, as before: the UProperty-mode publish (no test drives that branch) and Fern (no test
       target compiles it).

**LOW** — 4 rows.

2. ✅ **`[W5-CSX-DELEGATEPAD]`** (FIXED IN SOURCE 2026-09-12, batch L03a) `CsxExportService.cs:111`. The `[D4B-DELEGATEPAD]` fix reached the
   DLL readers, CE XML and `ue5_dissect.lua`, and **never reached `CsxExportService`** — every CSX
   offset is the raw `field.Offset`. Wrong on checked builds (UE 5.3+ with `DO_CHECK`).
   ⛔ A blanket `+ field.DelegatePad` is wrong: **Multicast fields also carry `DelegatePad = 8`**.
   ✅ **FIXED IN SOURCE 2026-09-12** (batch L03a), and not as a blanket add. Every element passes through
   `EmitElement`, which now tells the two delegate shapes apart.
   - **Unicast:** the 8-byte leaf is part OF the payload (the `FWeakObjectPtr`), so it moves to
     `Offset + DelegatePad`.
   - **Multicast:** the raw block is the WHOLE field, detector included, so it stays at `Offset`.
     - Its drill was hung on that raw block, and CE follows a child structure only from a POINTER
       element (`StructuresFrm2.pas`: `isPointer` is `vartype = vtPointer`). So the drill was never
       reachable, on either build.
     - It is now its own Pointer element on `InvocationList.Data`, at `Offset + DelegatePad`.
   - **Tests, red first:** a unicast leaf at +0 and +8, and a multicast drill on a checked build and on
     Shipping. 5/5 mutants killed (with `[A4-PUSHCE-UNPADDED]`); UI 5162/5162.
   - ✅ **A `TArray<FScriptDelegate>`'s ELEMENT leaves** were a separate row, `[A4-DELEGATE-ARRAY-PAD]`, now also
     fixed (L03b).
3. ✅ **`[W5-INSTEXPORT-TRUNC]`** (FIXED IN SOURCE 2026-09-12, batch L24) `CeXmlExportService.cs:1311`. `GenerateInstanceXml` computes the
   60,000-entry truncation flag and publishes it as `LastExportTruncated` with an explicit contract
   (`:208-212`: *"The caller reads this right after the synchronous Generate\* call"*). Two of three
   production callers honour it (`LiveWalkerViewModel.cs:4380`, `:4729`); **Instance Finder's
   drops it**, so a truncated table is exported without a word. P1/P7.
   ⛔ Copying LiveWalker's handling verbatim **breaks in four ways**, starting with a warning text
   that is wrong at this call site.
   ✅ **FIXED IN SOURCE 2026-09-12, not by copying** (batch L24). The four ways, and how each is handled:
   - **The text:** Live Walker names its own levers (Drill Depth, Copy CE Field). This panel's are
     Collapse Pointer Nodes and the DropDown Limit, so those are what it names.
   - **The status:** this method blanked `StatusText` after the copy, so it now sets it instead.
   - **The thread:** the flag is `[ThreadStatic]` and a clipboard await follows the Generate call, so it
     is read before that await.
   - **The failure path:** a refused clipboard copied nothing, and its message is unchanged.
   - **Tests, red first:** a 61,000-field instance exports "Copied, but TRUNCATED…" naming this panel's
     levers. The control: a 3-field instance adds nothing. 3/3 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5236/5236.
   - ⚠ **Survivor by construction:** the read-before-await ordering. The stub completes on one thread.
4. ✅ **`[W5-OFFSETS-UNMEASURED]`** (FIXED IN SOURCE 2026-09-12, batch L15, for the C ABI and the stale TRUE; ⬜ the MAILBOX half is split off as `[W5-OFFSETS-MAILBOX]`, L45) `Genau.cpp:4286`. `ValidateAndFixOffsets` computes `allMeasured`
   and a 16-entry reason and publishes them as `DynOff::bOffsetsValidated` /
   `g_offsetsFallbackReason` — and **only the pipe** (`Fern.cpp:5055-5057`) carries the verdict. The
   C ABI and the CE mailbox, **which build CE structures from those very offsets**, do not. P3.
   ⛔ Making `UE5_Init` return false on `!allMeasured` is harmful — `Grimoire.h`'s *"TWO flags,
   deliberately"* block explains why.
   ✅ **FIXED IN SOURCE 2026-09-12** (batch L15), for the C ABI and the stale TRUE. `UE5_Init` still returns true, as the
   note above requires.
   - A new export, `UE5_GetOffsetsVerdict(reasonBuf, bufLen)`, returns 1 when the offsets were measured and 0 when they
     were not, with the reason (`probe-not-run` before any detection). It is `int32_t`, not `bool`, because `executeCodeEx`
     reads all of RAX.
   - `scripts/ue5_dissect.lua` warns once per distinct reason before building a structure on unmeasured offsets.
   - Both early give-ups now store `validated=false` through one helper, `PublishOffsetsGiveUp`, and `UE5_Shutdown` forgets
     the verdict (`DynOff::ResetOffsetsVerdict`). Together these close the widening below.
   - ✅ **`[W5-OFFSETS-MAILBOX]`** (L45, FIXED IN SOURCE 2026-09-12): the CE **mailbox** carries the verdict now, taken
     the additive way Mimic.h's rules prescribe — a new **Cmd**, not a new field and not a new status meaning.
     - `CMD_OFFSETS_VERDICT = 16`. Output: `result` = 1 measured / 0 not, and the reason (null-terminated, `""` when
       measured, else e.g. `probe-not-run`) in `paramsData[0..127]`.
     - `MAILBOX_CONTRACT` 4 → 5; `MAILBOX_CONTRACT_MIN` stays 1, so every saved `.CT` stays valid. The surface hash
       moves this time (a new enum member, unlike versions 2 and 4), and `check_mailbox_contract.py` records why.
     - **Init-EXEMPT**, the second exemption after `CMD_FOREGROUND`: gating it would answer `-10` ("DLL not
       initialized") exactly when the honest answer, `probe-not-run`, matters most. `Test_Mimic_CommandRequiresInit`
       enumerates the command space and counts exemptions, so the decision had to be made there.
     - `CeMailboxLayout` gains `CmdOffsetsVerdict` and bakes `ContractVersion` 5; the tool's closed registry learns the
       new Cmd.
     - `ue5_invoke_helper.lua` gains `getOffsetsVerdict()` → measured, reason. It keeps `UE5_SCRIPT_CONTRACT = 1`, so it
       still runs against an older DLL: that DLL answers "Unknown command" (`-1`), reported as `dll-too-old`, never as
       measured.
5. ✅ **`[W5-DENKEN-DEADGUARD]`** (FIXED IN SOURCE 2026-09-12, batch L16) `Denken.cpp:221`. The *"bail to save budget in a followed impl"*
   guard is **dead in every reachable state** — `TryFollow` increments `ctx.callsFollowed` before
   recursing, so the inner branch can never be taken.
   ⛔ The obvious repair (drop `&& ctx.callsFollowed == 0`) is a **behaviour regression**.
   ✅ **FIXED IN SOURCE 2026-09-12** (batch L16), by removing the guard rather than "repairing" it. Its
   place carries a comment stating what the code does.
   - The regression is now pinned. `Test_Denken_FollowedImplOutlivesItsAlias` follows an impl whose
     RCX dies (`mov ecx, 5`), and the impl's later `[rdx+0x30]` read must still be recorded.
   - **A test-gap item**, so the red was taken against the obvious repair applied as a source mutant.
     2/2 mutants killed; dll_helpers_test 2721/2721, dll_core_test 304/304; UI 5215/5215.

#### ⛔ `implied_fix_safe`: 5 of 5 again

W2 **5/10**, W3 **4/6**, W4 **6/6**, W5 **5/5** — **twenty of twenty-seven** confirmed rows across
four waves carry a harmful or partly-harmful obvious repair.

#### ⛔ REFUTED — do not re-raise

- **WR-1** *"a cancelled noise-classification flags a GAMEPLAY class as engine package"*. Mechanism
  real, harm unreachable: commit `bea9009c` (2026-09-07) took the process-wide cancel flag out of the
  pipe path, and the only delivering trigger left is followed by a disconnect that resets every
  picker.
- **WR-2** — the WIRE auditor's own *"one new P3 instance"*: the 2026-09-07 cancelled-reply fix
  landed on three loops, and `find_instances` / `search_objects` never set `rset.aborted` while
  `Mimic.cpp:966-967` folds `truncated || aborted`. Harm unreachable for the same `bea9009c` reason.
  ⚠ **The contract mismatch itself is real and stays recorded as a latent gap**: the pipe publishes
  only `truncated`, and the mailbox folds an `aborted` its producer never arms.
- **SC-1** *"Denken's budget flag is the one that never fires"*. ⭐ **Settled by measurement on the
  real log corpus**: 4,315 `AnalyzeNativeFunctionProps` lines pulled from
  `%LOCALAPPDATA%\UE5CEDumper\Logs` across DQ7R, DumperTest and other titles — **0** hit the
  instruction budget, **0** hit the chunk window. The fourth verdict this sweep settled by reading
  the real artifact instead of arguing.

---

#### ⛔⛔ THE JUNE BLANK IS CLOSED — the whole sweep in one table

| wave | lines | raised | confirmed | HIGH | MED | LOW |
|---|---:|---:|---:|---:|---:|---:|
| W1 | 11,610 | 16 | 13 | 2 | 4 | 7 |
| W2 | 11,715 | 13 | 10 | 0 | 6 | 4 |
| W3 | 7,602 | 8 | 5 *(6, one cross-lens duplicate)* | 0 | 3 | 2 |
| W4 | 9,780 | 12 *(10 distinct)* | 6 *(+1 undecided)* | 0 | 5 | 1 |
| W5 | 9,744 | 8 | 5 | 0 | 1 | 4 |
| **total** | **50,451** | **57** | **39 distinct** | **2** | **19** | **18** |

Plus two **escalations** of recorded rows that are worth as much as new ones: `[W1-QUOTA-UNLIMITED]`
has a second entrance needing no user action (W3; this machine sits one preset below the trigger),
and FP1/FP2 are fixed on the pipe only — now traced one hop further, to the transports' own get-pose
calls (W2, W5).

**Nothing has been repaired.** Next, in order: **P1** (the pattern sweep — tool shipped, control
green), the remaining shapes, then **one fix pass grouped by shape across both blanks**, reading every
row's `implied_fix_safe` first.

### What this sweep does NOT cover

- **The `>2026-08-03` band** — 31,784 lines, 19.7%, also never inside an area audit. Larger per
  line than what W3 covers. Not planned here; it is the *next* blank.
- **The 4,648-line / 80-file hard core** from the audit #3 re-check (`Grausam.cpp` first) and
  the **3,409 lines / 31 files** of the audit #4 window that no later document names. Both are
  separate rows on the fix list and are **not** subsumed by this plan.

-----

## ⛔ THE SECOND BLANK — `[BLANK-AUG-PLAN-2026-09-10]` the 31,784 lines after audit #4 closed

**Status: PLANNED, NOT STARTED.** Planned while W4 of the June sweep was still running, at the
maintainer's direction, because the June sweep's confirmed defects fall into **repeating shapes**
and a fix pass that repairs a shape everywhere at once is far cheaper than one that repairs it
twice.

### What it is — measured, not estimated

```
py tools/verify/blank_sweep.py --band aug months
py tools/verify/blank_sweep.py --band aug clusters
```

**31,784 surviving production lines across 242 files (19.7% of the tree)**, authored after audit
#4's window closed on 2026-08-03. By author-month:

```
  2026-08     27516   86.6%  ##################################
  2026-09      4268   13.4%  #####
```

⭐ **86.6% is a single month**, and it is the month that followed audit #4 — heavy fix and feature
work. ⚠ **Audit #5's own fixes are inside this band**: #5 audited pre-2026-06-01 code and its
repairs were written in August, so the repairs have never themselves been audited.

⛔⛔ **THE ONE STRUCTURAL DIFFERENCE FROM THE JUNE BLANK, AND IT CHANGES THE PLAN.** `jun` is a
**closed** 32-day interval: sweep it once and it is done forever. `aug` runs to **HEAD** and
accumulates every commit made since — **sweep it and it starts refilling the next day.** A
one-shot area sweep of this band therefore has a shelf life. That is the argument for Track A
below being permanent machinery rather than another read-through.

| cluster | band | whole | files | band% | UNNAMED | notes |
|---|---:|---:|---:|---:|---:|---|
| APP-SHELL | 6,695 | 22,514 | 69 | 30% | **2,966 (44%)** | biggest; `CeLuaHygiene.cs` 742/864 |
| SCAN-CORE | 4,469 | 20,177 | 17 | 22% | 28 | `Ubel.cpp` 1,586 · `Genau.cpp` 840 |
| WIRE | 3,429 | 13,337 | 11 | 26% | 0 | `Fern.cpp` 1,232 · `Frieren.cpp` 797 |
| DLL-OTHER | 2,865 | 5,726 | 15 | **50%** | 130 | the only whole-file cluster |
| CE-BRIDGE | 2,471 | 9,359 | 17 | 26% | 70 | 5.5× its June size |
| AURA-GRAPH | 2,126 | 12,641 | 4 | 17% | 77 | |
| VALUESEARCH | 1,892 | 6,939 | 7 | 27% | 112 | |
| LIVEWALKER | 1,781 | 10,277 | 7 | 17% | 84 | |
| OBJTREE | 1,623 | 9,506 | 20 | 17% | 91 | |
| WIRE-DTO | 1,509 | 4,595 | 32 | 33% | **821 (54%)** | |
| TELEPORT | 1,360 | 13,593 | 17 | 10% | 116 | |
| EXPORT | 1,146 | 12,021 | 9 | 10% | 0 | |
| SNAPSHOT | 283 | 5,753 | 9 | 5% | 130 | |
| PIVOT-SPC | 135 | 3,319 | 8 | 4% | 122 | |
| **TOTAL** | **31,784** | **149,757** | **242** | 21% | 4,747 | |

⚠ **band% is 21% here against 38% in June**, so **thirteen of fourteen clusters get HUNKS**, not
whole files. Only `DLL-OTHER` (50%) is read whole.

---

### ⭐⭐ TRACK A — the PATTERN sweep, and it goes FIRST

The June sweep's confirmed defects are not independent bugs; they are **instances of a small number
of shapes**. Each shape can be searched for mechanically across the **whole tree** — not just this
band — which is both cheaper than reading and strictly more complete than any reader.

⛔ **Two of these are already on the fix list as gates.** That is the point: **a gate IS a permanent
tree-wide pattern sweep**, and it is the only form of this work that survives the band continuing
to grow. Build the gates first and Track B shrinks.

| # | shape (from the June sweep's confirmed rows) | mechanical search | status |
|---|---|---|---|
| **P1** | **computed and never published** — a fault flag, a cap, a refusal, a method tag. *The single most common shape.* | `tools/verify/pattern_p1.py`: **P1b** log calls whose message carries a degradation fact (225) + **P1a** result-struct members no transport names (15). ⛔ *This row first said `pipe_wire_parity.py` "already does this" — false: that tool measures the MIRROR (published, never read) and would miss four of P1's five confirmed instances, which never become reply keys at all.* | ✅ **SWEPT 2026-09-10** — 240/240 ruled, **7 confirmed, all LOW**, 1 refuted; see `[PATTERN-P1-2026-09-10]` below |
| **P2** | **serializer drops a legitimate value** — `WhenWritingDefault` vs a non-`default(T)` initializer | JSON contexts × property initializers | ✅ **REGISTERED 2026-09-10** in the fix-pass commit that repaired both instances (`[W1-QUOTA-UNLIMITED]` fixed; `AobUsageFile.Version` marked `Never`). `tools/check_json_default_ignore.py`, selftest 7/7, tree now clean. Built and measured earlier as a detector; see `[PATTERN-P2-2026-09-10]` below. |
| **P3** | **fix landed on 1 of N transports** — a contract stated at a function, honoured by one of three callers | every function with an optional out-param → do `Fern` / `Mimic` / `Frieren` all pass it? | ✅ **SWEPT 2026-09-10** — 112/112 ruled, **6 confirmed (2 MED · 4 LOW)**, 0 refuted; see `[PATTERN-P3-2026-09-10]` below. Tool: `tools/verify/pattern_p3.py`, five axes, control 7/7. ⛔ *This row first said "new gate" — wrong: twins legitimately differ (a pipe-only feature, an exporter that does not need a field), so P3's legitimate population is NOT empty and it is a SWEEP, like P1. It was also widened from "transports" to "twins": the sweep found the same shape between code-path arms, exporter siblings and callers of one function.* |
| **P4** | **`init`-only member absent from a copy path** | types with a `Copy*From` method → members it never assigns | ✅ **SWEPT 2026-09-10** — 44/44 ruled, **4 confirmed (1 HIGH · 2 MED · 1 LOW)**, 0 refuted; see `[PATTERN-P4-P7-P8-2026-09-10]` below. All of `LiveFieldValue`'s own `init` members; it has the only copy path that has any (`SpcQueryViewModel.CopyGroupCellsFrom` has none). ⚠ The population missed 10 of 54 members; the one that matters (`StructDataAddr`) was found by reading. |
| **P5** | **cap conflated with deadline/cancel** | `deadlineHit =` assignments and `>= maxResults` sites | ✅ **SWEPT 2026-09-10** — 86/86 ruled (35 DLL/pipe · 29 UI · 22 UI supplement), **2 confirmed, both LOW**, 1 refuted, 1 overridden to recorded; see `[PATTERN-P5-2026-09-10]` below. Tool: `tools/verify/pattern_p5.py`, control 6/6; its UI axis first missed 36 flag reads, now fixed. ⚠ *This cell first said "partly covered by `docs/todo.md:1314`", a line that never held a P5 row. The value scan's cap-in-`deadline_hit` is filed nowhere, and **needs no filing**: its one consumer names the cap and gives the cap's remedy.* |
| **P6** | **control outlives its backing session** | panels that hand an address to the live game vs those comparing `GameSessionId` | ✅ **REGISTERED 2026-09-11 (fix pass B14)** — `tools/check_session_gate.py`, selftest 5/5; 20 snapshot-address handoffs, all 20 gated since B14 gated Class Pivot's 4 in the same commit. See `[PATTERN-P6-2026-09-10]` below. |
| **P7** | **warning only on the manual path** | a status set in `X()` and not in its `X*QuietAsync` sibling | ✅ **SWEPT 2026-09-10** — 5/5 ruled, **0 new**: the tree-wide residue is the one recorded instance (Teleport's pose poll). Every other auto path runs its manual path's own code. See `[PATTERN-P4-P7-P8-2026-09-10]` below. |
| **P8** | **repaint never fires** — no `[ObservableProperty]`, or assigned *after* the property whose `[NotifyPropertyChangedFor]` was to repaint it | AST over the VMs | ✅ **SWEPT 2026-09-10** — 9/9 ruled, **1 confirmed (LOW)**, 0 refuted, plus `[W1-CONTAINER-STALE]` widened to `ValueTooltip`; see `[PATTERN-P4-P7-P8-2026-09-10]` below. Tool: `tools/verify/pattern_p8.py`; 3 of its 9 rows were name-collision artifacts (limit recorded in its header). |

⚠ **P9 — "a promise in a status line the code did not keep"** has no mechanical form and stays a
reading job. It is the reason Track B still exists.

⭐ **Run Track A against the WHOLE TREE, including the June band.** June was swept by *readers*;
a pattern matcher will reach instances a reader's attention did not, and the June band is now the
best-labelled corpus we have for validating each matcher (every confirmed row is a known positive —
**a matcher that does not re-find its own June instances is broken**, which is the red-before-green
control for this work).

---

#### ✅ P1 SWEPT 2026-09-10 `[PATTERN-P1-2026-09-10]` — 240/240 ruled, 7 confirmed (ALL LOW), 1 refuted

`tools/verify/pattern_p1.py` enumerated the shape tree-wide (225 log calls whose message carries a
degradation fact + 15 result-struct members no transport names). 6 adjudicators ruled **every
row**, 5 refuters took the candidates. 11 agents, ~1.95M tokens. **Nothing fixed.**

| batch | rows | ALREADY-RECORDED | PUBLISHED-ELSEWHERE | LOG-ONLY-BY-DESIGN | NOT-A-DEGRADATION | DEFECT-CANDIDATE |
|---|---:|---:|---:|---:|---:|---:|
| DETECT-GENAU | 54 | 9 | 23 | 3 | 15 | 4 |
| DETECT-INIT | 25 | 6 | 13 | 2 | 4 | 0 |
| WALK | 33 | 1 | 12 | 13 | 4 | 3 |
| SCAN + P1a | 41 | 4 | 15 | 10 | 11 | 1 |
| GAMEPLAY | 34 | 6 | 19 | 5 | 1 | 3 |
| INFRA | 53 | 0 | 7 | 30 | 9 | 7 |
| **total** | **240** | **26** | **89** | **63** | **44** | **18** |

⭐ **Coverage is complete and reconciled, not assumed**: every batch returned exactly its expected
number of rulings (the workflow logged any mismatch; there was none), and the 18
`DEFECT-CANDIDATE` rulings map onto the 8 filed findings with nothing left unfiled — checked by hand
(Genau :567/:751/:2304 → P1G-1; Flamme's seven rows → P1X-1; and so on).

⭐ **The known-positive control held end to end, through the agents and not only through the tool**:
`Aura.cpp:1235` and `Aura.cpp:9920` were both present in their batches and both ruled
`ALREADY-RECORDED` (`[W4-STRIDE-TENTATIVE]`, `[W1-SNAP-FAULT]`) by the adjudicators themselves.

---

#### ⭐⭐ The result: P1's tree-wide residue is ALL LOW — the pattern sweep BOUNDED the shape

**196 of 240 rows (82%) are fine** — published by another route, log-only by genuine constraint, or
not a degradation at all — and **the seven survivors are all LOW**. Every P1 instance of MED or
HIGH severity in this tree had already been found by the June area sweep.

That is the two-track design working as intended, and it is worth stating as a method result: **the
area sweep finds the severe instances, because a reader follows consequences; the pattern sweep
proves the shape is bounded, because a matcher cannot skip a row.** Neither substitutes for the
other. What P1 bought is not seven more defects — it is the right to say *there is no eighth serious
one hiding in a log line*.

⚠ **And the adjudicators did not simply follow the brief's steer.** The SCAN brief called Radar's
*"skipping TArray with Num=%d"* (`Aura.cpp:7770/7817`) a **strong candidate**. It was ruled
`NOT-A-DEGRADATION`, correctly: the guard fires only on `Num < 0` or `Num > 10,000,000`, which the
in-code comment classifies as freed memory or an `OptionalProperty` misread as a `TArray` — there is
no real value inside such a header to lose. **My steer was wrong and the agent overruled it with the
code**, which is the behaviour the brief asked for.

---

#### The fix list — 7 rows, all LOW, none repaired

1. ✅ **`[P1-GENAU-ABORT]`** (FIXED IN SOURCE 2026-09-12, batch L01) `Genau.cpp:567` (+ `:751`, `:2304`). Three sweeps poll
   `Tot::Requested()` and each spends its abort on a log line, then returns a result that reads as
   "nothing found" — `DataScanGObjectsCandidates` (void), `FindGObjectsStaticStruct` and
   `FindGNamesByStringRef` (both return 0). `bScanCancelled` never sees them, so `UE5_Init`'s latch
   guard (`Frieren.cpp:583`) can **latch a partial init after a per-command cancel** — and GNames
   cannot be rescanned afterwards. ⚠ Fix shape: record the abort **at the bail** as an out-flag and
   OR it into `bScanCancelled`; never re-derive it from `Tot::Requested()` later.
   ✅ **FIXED IN SOURCE 2026-09-12, the recorded shape** (batch L01, with `[A2-GNAMES-PTRSCAN-ABORT]`).
   - **Each abort is recorded at its bail.**
     - `DataScanGObjectsCandidates`, `FindGObjectsByDataScan`, `CollectGObjectsCandidates` and
       `FindGObjectsStaticStruct` take an optional `bool* outCancelled`.
     - The two GNames tiers set `s_gnamesReport.cancelled`. `FindGNames` resets that report before
       calling them, and `FindAll` already ORs it into `bScanCancelled`.
   - **Inside `FindAll`**, the data-scan fallback feeds `s_gobjectsReport.cancelled`.
   - **After `FindAll`**, `UE5_Init`'s two recovery sweeps (static struct, then heap candidates) OR their
     aborts into `ptrs.bScanCancelled`. That is the predicate the latch guard reads, and until now it
     could never see them.
   - ⛔ `ExtraScanGWorld`'s bail is left out. That is the recorded harm: it would refuse a complete init
     over a non-critical pointer.
   - **Tests, red first:**
     - a `GENAUABORT` block in dll_core_test. Each sweep, run with a pending cancel over the test exe's
       own sections, records its abort; an uncancelled candidate sweep and pointer scan are the
       controls.
     - A source pin on Frieren's recovery, since no test target compiles Frieren.cpp.

     6/6 mutants killed; dll_core_test 249/249; UI 5149/5149.
   - ⚠ **Survivor by construction:** `FindGObjects`' call site. A pending cancel is always recorded by the
     tier-1 AOB scan first, so no deterministic test reaches the fallback's bail alone.
   - ✅ **Review 5 follow-up 2026-09-12** (of 785b1730: two LOW, both CONFIRMED).
     - **Two sweeps had no uncancelled control.** `FindGObjectsStaticStruct` and `FindGNamesByStringRef` were
       checked only with a cancel pending. A bail store hoisted above its poll, set on EVERY call, passed every
       test, and on a real game it would refuse the init latch on every scan. Both now have an uncancelled
       control, red against exactly that hoist; 2/2 mutants killed; dll_core_test 279/279; UI 5187/5187.
     - **Live check L40 could not work.** A UI disconnect never cancels `UE5_Init` (its thread is unbound, and
       the monitor cancels only an in-flight command). L40 now says so and keeps only its control live.
2. ✅ **`[P1-ENUMNAMES]`** (FIXED IN SOURCE 2026-09-12, batch L02) `Genau.cpp:5476`. When `DetectUEnumNames` fails it latches
   `bUEnumNamesFailed` for the process, and **no exit publishes it**. `list_enums` then answers `ok`
   with every UEnum's entries empty, and the USMAP and CE exports ship without enum names — none of
   them saying why. ⛔ **Partly harmful as filed**: publishing the permanent-fail latch unconditionally
   is wrong, because `ForEach`'s void abort (`Aura.cpp:1467`) lets a *cancelled* detection latch FAILED.
   ✅ **FIXED IN SOURCE 2026-09-12, the unharmful half first** (batch L02).
   - **`Aura::ForEach` returns `bool`:** false when a cancel cut the walk short. Every other caller ignores
     it, as before.
   - **`DetectUEnumNames` does not latch on a cancel.** If any candidate search was cut short it returns
     false and touches neither flag, so the next enum lookup retries. Only a search that ran to completion
     latches FAILED.
   - **Then the real failure is published.**
     - `list_enums` carries `enum_names_failed`.
     - The UI's `ListEnumsDetailedAsync` carries it with the long-ignored `truncated`. It is a default
       interface member, so fakes are untouched.
     - The USMAP export's progress line names both: "enum member names are unavailable on this build …" and
       "the enum list was cut short …".
   - **The widening.** Under the latch, `GetEnumEntries` no longer claims "truncated read, retry pending"
     (false there, and unthrottled: one line per enum field per walk).
   - **Tests, red first:**
     - dll_core_test: a cancelled ForEach says so, and a cancelled `DetectUEnumNames` latches nothing, so the
       next, uncancelled search really runs and, finding nothing, latches FAILED itself. (Before the fix it
       could not: the FAILED latch also sets `bUEnumNamesDetected`, and the next call returned true at its
       first line.) An uncancelled ForEach is the control.
     - The parse, and the export note.

     5/5 mutants killed; dll_core_test 253/253; UI 5152/5152.
   - ⚠ **Survivors by construction:** Fern's key, which no test target compiles, and Ubel's quieted
     warning, which is log-only.
   - ⬜ **Not in this row:** the CE exports' per-field enum dropdowns still come back empty under the latch
     with no note. They read the entries from the walk, not from `list_enums`.
   - ✅ **Review 5 follow-up 2026-09-12** (of 7e5a71fc: one MED, CONFIRMED by both skeptics, and four LOW).
     - **MED, a regression L02 introduced.** A cancelled `DetectUEnumNames` sets neither flag, and
       `ResolveEnumValue` ignored its return. It read on with the DEFAULT Names offset and format, and cached
       that answer, which nothing erases. On a UE5.6+ game every enum the cancelled walk touched kept an empty
       table for the process, and `enum_names_failed` stayed false. Before L02 the cancel latched FAILED,
       which cached nothing. Now `ResolveEnumValue` reads and caches nothing until detection has run to
       completion, and `GetEnumEntries` treats that as the retry it is.
     - **The USMAP warning was a passing progress line,** overwritten one round-trip later. The export ended
       on a bare "USMAP exported". `GenerateUsmapAsync` now collects it (`EnumWarning`), and the final status
       and the log carry it.
     - **Test gaps:** the parse test set both flags (a swapped key passed), and nothing pinned the export's
       use of them. The survivors list above was wrong to omit it.
     - **Live check L41** asked one game for both halves, which need opposite games. It is split.
     - **Tests, red first:**
       - dll_core_test: a cancelled lookup caches nothing, against a control where a completed detection
         does cache;
       - the export's warning, through a stub that reports a FAILED latch, and a healthy control;
       - a per-flag parse theory.

       5/5 mutants killed; dll_core_test 277/277; UI 5187/5187.
     - ⚠ **Survivors by construction:** `GetEnumEntries`' quiet branch (log-only), and the
       MainWindowViewModel status line (no export harness).
3. ✅ **`[P1-UPROP-DELEGATE]`** (FIXED IN SOURCE 2026-09-12, batch L06) `Ubel.cpp:4697`. The UProperty-mode (UE4 < 4.25) delegate-array arms
   still **drop the readers' refusal `error`** — commit `e16d2052` fixed exactly this on the FProperty
   arm, and that arm's comment calls the old behaviour a defect in so many words. ⭐ **P3 at the
   code-path level**: a fix that reached one of two twins. ✅ **The only fully SAFE fix in the batch**:
   copy the two shipped, verified `else if (!r.ok && !r.error.empty())` branches into the UProperty
   arm.
   ✅ **FIXED IN SOURCE 2026-09-12, exactly that** (batch L06). Both UProperty-mode arms (Phase J unicast,
   Phase K multicast) now write the reader's refusal into the field's value, as their FProperty twins do.
   **Red first:** dll_core_test walks a fake UProperty-mode class (`bUseFProperty = false`). Its two array
   UProperties carry 20-byte inners, neither 16 nor 24, so both readers refuse, and each field must now name
   its refusal. 2/2 mutants killed; dll_core_test 283/283; UI 5195/5195.
4. ✅ **`[P1-WALK-UNREADABLE]`** (FIXED IN SOURCE 2026-09-12, batch L07) `Ubel.cpp:3981`. `WalkInstance` knows the object is gone
   (`IsAddrReadable` false, logged *"not readable (freed?)"*) and returns an `InstanceWalkResult`
   carrying **only `addr`** — no `stale`, no error. Live Walker shows a silently blank grid.
   ⚠ Fix shape: a **distinct** `unreadable` key in both lean and full replies, **not** folded into
   `stale` (which means something narrower).
   ✅ **FIXED IN SOURCE 2026-09-12, in that shape** (batch L07, with `[A4-REROOT-STALE-WARNING]`).
   - `InstanceWalkResult.unreadable` is set at the readability bail. Fern sends `unreadable`, like `stale`,
     in BOTH lean and full replies, as its own key.
   - The UI parses it (`IsUnreadable`). Live Walker says "⚠ This object is no longer readable (freed?)", and
     never retries fill-gaps on it.
   - **Tests, red first:** a dll_core_test walk of a reserved, uncommitted page (a readable control), the
     parse, a Fern pin that the key is not lean-gated, and the VM status. 6/6 mutants killed; dll_core_test 287/287; UI 5204/5204.
5. ✅ **`[P1-SPARSEDELEGATE-REFS]`** (FIXED IN SOURCE 2026-09-12, batch L08) `Aura.cpp:3966`. Find References silently drops sparse-delegate
   bindings whose InvocationList cannot be located and reports a complete, clean sweep
   (`deadline_hit=false`) — so when that binding was the only reference, Live Walker says *"No
   references found — likely held by a non-reflected pointer"*, blaming the game for our gap. An
   aggregate channel already exists (`ContainerScanStats`).
   ⚠ *Widened 2026-09-10 (`[PATTERN-P5-2026-09-10]`): the same hint also prints on a PARTIAL scan,
   i.e. `deadline_hit=true` from a deadline or a worker fault (`LiveWalkerViewModel.cs:2694`). One fix
   covers both: never blame the game unless the scan was complete.*
   ✅ **FIXED IN SOURCE 2026-09-12, both paths** (batch L08).
   - The DLL counts the unreadable delegates into the aggregate channel,
     `ContainerScanStats.sparseUnlocated`, and the reply publishes it as `scan.sparse_unlocated`.
   - The UI parses it, and `IsComplete` honours it.
   - The empty-result status blames the game only after a COMPLETE scan: no deadline, no faulted worker,
     no unreadable delegate. Otherwise it says what was missed.
   - A non-empty result names the unreadable delegates too.
   - **Tests, red first:** a dll_core_test sweep over a planted sparse-delegate map (two unreadable
     delegates and one readable one; exactly two counted), the parse, a Fern pin, the status rule, and
     both Live Walker status lines. 9/9 mutants killed; dll_core_test 291/291; UI 5212/5212.
6. ✅ **`[P1-SEETHRU-NOPRODUCER]`** (FIXED IN SOURCE 2026-09-12, batch L05) `Schlacht.cpp:361` (+ `:443`). On a build missing
   `SetActorHiddenInGame`, `LineTraceSingle` or `KismetSystemLibrary`, or when a hit cannot be
   resolved to an actor, See-through does **nothing at all** — and Tick still ends `STR_OK` with
   `hasTarget = true`, so the card reads **"Active — nothing blocking the view"**. The header already
   defines `STR_ERR_REFLECTION` for exactly this case. ⛔ Partly harmful as sketched; the right shape
   is to refuse at enable (`-3` from `SetEnabled`), which reaches all three exits through the return
   code.
   ✅ **FIXED IN SOURCE 2026-09-12, in the recorded shape** (batch L05).
   - `SetEnabled(true)` asks `ProbeProducers` whether the build can hide anything: `LineTraceSingle` on
     the KismetSystemLibrary CDO (with its `OutHit`), and `AActor::SetActorHiddenInGame`.
   - If either is missing it returns `STR_ERR_REFLECTION` (-3) BEFORE a worker starts. The pipe's `state`,
     the mailbox result (the CE script shows the error and unticks) and the `code` on every later poll all
     carry it.
   - The card reads "Unavailable — not supported on this game build", with no retry offer (unlike the hook
     refusal: this one is the build).
   - ⬜ **Not in this row:** a hit that resolves to no actor is per-tick. It keeps its one-shot warning in
     `CollectOccluders` (`[SEETHRUNOOP]`).
7. ✅ **`[P1-SEETHRU-GIVEUP]`** (FIXED IN SOURCE 2026-09-12, batch L05) `Schlacht.cpp:626`. After the 5-minute restore give-up
   (`PENDING_RESTORE_MAX_MS`) the card still promises *"Click back into the game and they
   reappear"* — `hidden_count > 0` with `active = false` means both "restore pending" and "restore
   abandoned", and the pipe cannot tell them apart. The log names the real remedy; the card does not.
   ✅ **FIXED IN SOURCE 2026-09-12** (batch L05).
   - The give-up branch records `restoreAbandoned`, and every `SetEnabled` clears it (either direction
     re-decides).
   - `GetStatus` and the pipe publish `restore_pending` (the waiter is running) and `restore_abandoned`,
     both additive.
   - The card keeps "click back into the game and they reappear" for a pending restore. For an abandoned
     one it gives the log's remedy: turn See-through on and off again with the game running.
   - **Tests, red first:**
     - the VM: the -3 refusal, the abandoned card, and a pending control;
     - the parse;
     - source pins for `Schlacht.cpp` and `Fern.cpp`, which no test target compiles.

     With `[P1-SEETHRU-NOPRODUCER]`: 9/9 mutants killed; UI 5183/5183. The real `UE5Dumper` build compiles both
     DLL halves.

`implied_fix_safe`: **5 of 7** unsafe or incomplete; one fully safe (#3), one mostly safe (#7).
⭐ **The one fully safe fix is the one that copies an already-shipped, already-verified fix** — which
is itself a useful rule for the fix pass.

#### ⛔ REFUTED — do not re-raise

- **P1X-1** *"a failed override save is dropped and the reply still says `persisted: true`"*. The
  shape is real — `Fern.cpp:1832`/`:1878` publish `data["persisted"] = persist`, which is the
  *request* flag echoed back, unrevisited since `2c6b7e54` — but the failure trigger is asserted, not
  demonstrated, and `Flamme.h` documents both savers as *"Never throws"*. Route 5 + P1-b.

#### Widenings to rows already recorded

- ✅ **`[W5-OFFSETS-UNMEASURED]` has a latent stale-TRUE.** (fixed with the row in batch L15: both give-ups store false, and `UE5_Shutdown` resets all three) The two early returns in
  `ValidateAndFixOffsets` (`:3459`, `:3859`) rely on `bOffsetsValidated`'s *initial* false and never
  store false, and `UE5_Shutdown` never resets `bOffsetsValidated`, `bOffsetsProbeRan` or
  `g_offsetsFallbackReason`. A second `UE5_Init` (CE Disable → Enable) taking an early return after a
  validated run would report **`validated = true` alongside a non-empty `fallback_reason`, over
  default offsets**. Practically unreachable today; ⬜ store false explicitly in the same fix.
- ✅ **`[W4-STRIDE-TENTATIVE]`'s fix must reset at entry** (done, batch B27: the verdict and the layout are reset at the top of `DetectItemSize`). `DetectLayout`'s early returns (`:1061`,
  `:1077`) fire before `s_layoutMode` / `s_itemObjOffset` / `s_itemSize` are touched, so on a re-init
  (heap-fallback loop `Frieren.cpp:299`, restore `:349`, `apply_rescan` `Fern.cpp:5215`)
  `item_layout_mode` can describe a **previous** candidate.
- **Two stale comments**, wording only: `Genau.cpp:4277-4283` (G7) still says `apply_rescan`
  "flips NO→YES", impossible since the G3 gate (`Fern.cpp:5231-5250`); and `Ubel.cpp:6080` labels
  `supported=false` *"UE < 5.0 unsupported"* when its only producer is now a key-shape probe that
  fires on any version.

---

#### ✅ P3 SWEPT 2026-09-10 `[PATTERN-P3-2026-09-10]` — 112/112 ruled, 6 confirmed (2 MED · 4 LOW), 0 refuted

`tools/verify/pattern_p3.py` — five axes of *"a fix that reached some of its twins"*. 4 adjudicators
ruled every row, 3 refuters took the candidates. 8 agents, ~1.25M tokens. **Nothing fixed.**

| batch | rows | ALREADY-RECORDED | NOT-A-TWIN | LEGITIMATE-DIFFERENCE | DEFECT-CANDIDATE |
|---|---:|---:|---:|---:|---:|
| OUTPARAMS + VALIDITY | 13 | 5 | 5 | 3 | 0 |
| TYPEMAPS | 29 | 2 | 9 | 12 | 6 |
| CONSUMERS | 57 | 3 | 38 | 15 | 1 |
| TAGS | 13 | 3 | 6 | 4 | 0 |
| **total** | **112** | **13** | **58** | **34** | **7** |

⭐ **Coverage reconciled**: every batch returned its exact row count, and the 7 candidate rulings map
onto the 6 findings with nothing unfiled (`SnapshotNumeric.Render` is the display half of P3M-02,
same root, same fix site).

⭐ **The known-positive control held through the agents — nine of nine.** The workflow checked each
named positive's ruling individually, not just the row count: `TeleportRelative.outLandingKnown`,
`GetPoseImpl.outParentRelative`, `bOffsetsValidated`, `MapInnerTypeToCeField`, `WidthBytes`,
`DelegatePad`, `[TPREL-ZEROPOSE-2026-09-10]`, `[POSEATTACH-2026-09-10]` and `[D4B-DELEGATEPAD]` all came
back `ALREADY-RECORDED`.

⚠ **Zero refutations, and why that is not soft refuting.** In P3 the *adjudicators* were the filter:
**92 of 112 rows were dismissed** — 58 not-a-twin, 34 a legitimate difference — each with a named
reason. What reached the refuters was already solid, and they logged the routes they tried and the
evidence they used. P3C-1's refuter is the clearest case: trying route 4, it found commit `860245b0`'s
body stating that guessed fields *"are excluded from all exports"* — the attempt to refute turned up
the contract the SDK exporter breaks.

---

#### ⭐⭐ The method result: a WRITTEN twin contract is what decides P3, in both directions

Every one of the six confirmed rows has **a written statement that the two places are twins**, and one
of them breaks it: Y11's doc names "two paths" and Y16 names the CE form as the third; `GroupMatch`'s
width helpers say they *mirror SnapshotNumeric's declared-type set*; the invoke dialog's
`IsEmptyOnlyParam` groups FString with the opaque family; `PropertyScoringTable`'s doc says delegates
count as non-value; the SDK's container-inner map already mirrors five scalar arms; `860245b0` says
guessed fields are excluded from *all* exports.

And the 34 `LEGITIMATE-DIFFERENCE` rulings were decided by written statements too — a documented
pipe-only command, `Mimic.h`'s definition of `LI_OUT_TRUNCATED`, a transport that says it cannot
carry a fact. **Where the relationship is written down, P3 is decidable; where it is not, the tool's
pairing was usually noise.** That is also why the fixes in this batch are the safest of the whole
sweep: when the twin contract is explicit, "copy the fix into the twin" has something to be checked
against.

---

#### The fix list — 6 rows, none repaired

**MED**

1. ✅ **`[P3-INVOKE-Y11-CEFORM]`** (FIXED IN SOURCE 2026-09-11, batch B06 — see the fixed note at the end of this item) `InvokeScriptGenerator.cs:553`. Three invoke paths build the same
   ProcessEvent params from the same `FunctionInfoModel`. **FIRE** has audit #5's Y11 unwritable-param
   gate (`ParamBufferBuilder.TryValidateScalar` + `IsEmptyOnlyParam` / `IsRefusedParam`, called at
   `InvokeParamDialog.cs:664`); the **interactive CE invoke form does not** — an FText param is sent
   zeroed, and a typed container or delegate value is written as a raw int32. Y11's own doc says
   "two paths"; Y16 already names this form as the third.
   ⚠ **Safe only through the SHARED predicates**, never a hand-copied type list — the twin shares the
   contract (same params, same zero-filled buffer, same ProcessEvent), so the fix is to call what FIRE
   calls.
   ✅ **FIXED IN SOURCE 2026-09-11, through the shared predicates** (batch B06, one commit with
   `[P3-INVOKE-STRUCT-FSTRING]`).
   - **The form's FIRE handler** now opens with a gate, `InvokeScriptGenerator.AppendUnwritableParamGate`,
     classified by `ParamBufferBuilder.IsRefusedParam` / `IsEmptyOnlyParam`, the calls FIRE makes.
     - An FText is refused whatever the box holds.
     - An empty-only type (TArray/TMap/TSet, delegates, TFieldPath, TOptional, the layout-less
       struct) is refused only when the box holds typed text.
     - It runs BEFORE the idle wait and the zero-fill, so a refusal sends nothing. It bails the way
       the busy-mailbox bail does: says so, leaves the form open, no untick.
   - **Only the TEXT test is Lua** (`_isZeroDefault`, the twin of `IsZeroDefaultText`), because
     the text is typed at FIRE time. Its exact spelling is pinned and was run through a Lua
     interpreter against that predicate's cases, 16/16.
   - **The write loop** skips every `IsUnwritableParam` type, so the zero-fill is the value sent.
   - **The labels** say so up front: `CANNOT BE SENT` / `EMPTY ONLY`.
   - **Tests (red first):**
     - `InvokeScriptTests.CeForm_WritesAParamExactlyWhenFireWould`: parity over 34 type names,
       with 10 red (every non-struct unwritable type).
     - The gate, the FText refusal and the predicate spelling: 8 red.
     - The controls (scalars, pointer, in/out FString not gated) were green throughout.
   - ✅ **Review follow-up 2026-09-11 (adversarial review of d8a7f44f + d5e9148d + 9abc03c8: 8 survived, 1 refuted).**
     - **Copy AA Script, the third path, had no gate:** a typed TFieldPath / TOptional value was
       baked as a raw int32, and this row's doc claimed the helper refused them. The dialog now runs
       `TryValidateInputsForInvoke` (FIRE's shared predicates) before generating.
     - **The gate's Lua was pinned by substrings only.** `CeForm_TheEmptyOnlyGate_IsWellFormedLua`
       and `CeForm_TheFTextRefusal_IsOneWellFormedStatement` now pin its shape.
2. ✅ **`[P3-SNAPNUM-ENUM]`** (FIXED IN SOURCE 2026-09-11, batch B12) `SnapshotNumeric.cs:17` (+ `Render` `:169`). No `EnumProperty` arm, so
   every enum field captured since **AB14** made enums scannable gets `numeric_value NULL`: every SPC
   numeric predicate and every Group Match slot skips it, and the grid shows raw hex. The DLL side
   (`Radar.cpp:287` `kNumericAll`, `:407` EnumProperty→UInt8) and snapshot *capture* both have it; the
   C# decoder does not. Reachable under the non-default `NumericAll` capture scope.
   ⭐⭐ **This is the ROOT of `[W2-GROUPMATCH-ENUM]`.** `GroupMatch.WidthBytes`' own header says it
   *mirrors SnapshotNumeric's declared-type set* — so the W2 row is the mirror of this one, and fixing
   `WidthBytes` alone (which W2's refuter already showed is unsafe in isolation) would leave SPC
   broken. ⬜ **Fix order: SnapshotNumeric first**, then the Group Match mirror.
   ✅ Safe on its own: an `EnumProperty` arm in `TryFromHex` **and** `Render`, decoding unsigned at the
   captured `byteLen` (the DLL always stores 1 byte, `Radar.cpp:407`).
   ✅ **FIXED IN SOURCE 2026-09-11, exactly that shape** (batch B12, one commit with `[W2-GROUPMATCH-ENUM]`).
   - Both `TryFromHex` and `Render` gained an `EnumProperty` arm. It reads the zero-extended
     little-endian value at the captured length, so an enum is never `-1`.
   - **Tests, red first:** `TryFromHex_DecodesAnEnumUnsigned` (3) and
     `Render_ShowsAnEnumAsItsNumber_NotRawHex` (2).
   - ⚠ **Known limitation, a decision for this row** (the review of 25904d02). Corrected by the
     review of f023a35a, which found three claims in the first version of this note wrong.
     - `numeric_value` is decoded once, at insert. A NumericAll snapshot captured between ab0fd6a6
       (2026-08-19, when enums became capturable) and B12 keeps it NULL for its enum rows.
     - The grid shows their numbers, because `Render` decodes the stored hex. The NUMERIC readers
       still skip them: SPC's numeric predicates (Increased / Decreased / Exact / Between / ≥ / ≤),
       Group Match's absolute and Increased / Decreased slots, Diff direction, and Class Pivot's
       change Discovery (its ↑/↓ arrow and its ranking read `numeric_value`; the row still appears,
       because its HAVING clause compares hex). Changed / Unchanged compare `hex` and are unaffected.
       (Discovery was missing from this list until the review of 31f79d66.)
     - Not backfilled, by choice. The hex IS present, so a one-time UPDATE, or a lazy decode where
       `numeric_value` is NULL at the load sites, would work; neither was done.
     - ⚠ The first version claimed "SPC runs in SQL, so a read-time fallback could not cover it"
       (false: the load sites decode rows in C#), and cited a "recapture, not migrate" policy plus the
       build-1827 precedent. That policy is about schema bumps, and at build 1827 the rows were
       ABSENT rather than undecoded (`docs/archive/dev-log-2026-07-pre-build-2200.md`); neither
       covers a derived column.
     - Until a backfill lands: take fresh snapshots.

**LOW**

3. ✅ **`[P3-INVOKE-STRUCT-FSTRING]`** (FIXED IN SOURCE 2026-09-11, batch B06) `ParamBufferBuilder.cs:353`. On the FIRE path a **top-level**
   string param goes through `InvokeStringParam` and the DLL builds the FString by value — but an
   FString-family **struct sub-field** takes the scalar route and is written as a raw int32 over
   `FString.Data`: a third hole beside Y11-OPAQUEDROP, and a code-path twin inside one builder.
   ✅ Safe with one caveat: refuse a *non-empty* string member in `TryValidateStructSubFields`; an
   all-zero FString `{null,0,0}` is the valid empty and must still pass.
   ✅ **FIXED IN SOURCE 2026-09-11, exactly that shape.**
   - `TryValidateStructSubFields` refuses any typed text in a string member, named. The comparison
     is UNTRIMMED, because a space is a string too.
   - The empty box passes.
   - `WriteStructParam` leaves a string member zeroed, so a caller that skips the gate drops the
     text instead of stamping an integer over `Data`.
   - The dialog maps a cleared (null) string box to `""`, not `"0"`: the gate would read `"0"` as
     the text "0".
   - `TryValidateScalar` is unchanged. It still accepts strings, because a TOP-LEVEL string is built
     for real (`Y11_StringAndPointerParamsAreStillAccepted` stays green).
   - **Tests:** `AuditL11HonestyTests.StructFString_*`.
     - All three string types, the texts `42` / `0` / space, and the write-never pin: 7 red first.
     - The empty-member control was green before and after.
   - ✅ **Review follow-up 2026-09-11 (adversarial review of d8a7f44f + d5e9148d + 9abc03c8: 8 survived, 1 refuted).** Copy AA Script still baked a struct's string members as CE-allocated FStrings,
     including inside an OUT struct, where the callee's assignment frees memory UE never allocated.
     `CollectBakedValues` now skips them (the zeroed slot is the empty FString, as FIRE leaves it),
     and typed text is refused by the gate above. Pinned from the source (red first).
4. ✅ **`[P3-SCORING-MCDELEGATE]`** (FIXED IN SOURCE 2026-09-12, batch L33: the UE4 name added to the set, and nothing more; red first) `PropertyScoringTable.cs:397`. `IsNonValueType` holds
   `DelegateProperty`, `MulticastInlineDelegateProperty` and `MulticastSparseDelegateProperty` and
   misses the **UE4 ≤ 4.22** name `MulticastDelegateProperty`, so old-UE4 delegates escape the
   non-value penalty the map's own doc (`:394-396`) says they get. The calibration games are all 4.23+,
   which is why nobody saw it. ✅ Safe: one name, a private predicate, one consumer.
5. ✅ **`[P3-SDK-INNERS]`** (FIXED IN SOURCE 2026-09-12, batch L04) `SdkExportService.cs:334`. The scalar path (`MapCppDeclCore` `:278-303`)
   spells `TSoftClassPtr<>`, `TLazyObjectPtr<>`, `FScriptDelegate` and `FFieldPath`; the
   container-inner map declares all four as `uint8_t`. ⚠ Partly safe: copying the four scalar arms is,
   the rest of the finding's sketch is not.
   ✅ **FIXED IN SOURCE 2026-09-12** (batch L04): the four scalar arms, copied, and nothing more.
   - A container inner of `SoftClassProperty` or `LazyObjectProperty` now spells what the scalar path
     spells, with its class when the walk carries one.
   - So does a `DelegateProperty` or `FieldPathProperty` inner.
   - **Red first:** an Array of each, an Array carrying its class, a Map value and a Set element.
6. ✅ **`[P3-SDK-GUESSED]`** (FIXED IN SOURCE 2026-09-12, batch L04) `SdkExportService.cs:561`. CE XML (`:2178`) and CSX (`:102`) both skip
   Live Walker's **Guess?** rows; the SDK header export declares them, and their `?0x…` names **do not
   compile**. The contract is written: commit `860245b0` — guessed fields are *"excluded from all
   exports"*. ✅ **Safe and measured**: filter `!f.IsGuessed` in `EmitClassHeaderFromLive`. Confined to
   the live path; the schema path (`walk_class`) can never see guessed rows.
   ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L04).
   - `EmitClassHeaderFromLive` filters `!f.IsGuessed`, so a guessed row's bytes become padding, as they do
     in CE XML and CSX.
   - **Red first:** a header over a class with a `?0x…` row.
   - With `[P3-SDK-INNERS]`: 5/5 mutants killed; UI 5176/5176.

`implied_fix_safe`: **4 safe or safe-with-caveat, 2 partly** — the safest batch in the sweep, for the
reason given above.

#### Widenings to rows already recorded

- ✅ **`[W2-GROUPMATCH-ENUM]`** — its root is `[P3-SNAPNUM-ENUM]`; fix that first. (Both done 2026-09-11, batch B12.)
- ✅ (done in B23) **`[W5-CEXML-FSTRING]` should also carry `TextProperty`.** `MapInnerTypeToCeField` has no FText arm
  either: `TArray<FText>` becomes a group placeholder and an FText map value leaves an empty folder.
  Low value (`:2581` notes FText has no clean CE encoding) — fold into that row's fix.

#### Leads — noticed while reading, NOT verified, NOT filed

- ⬜ **CE XML scalar `DelegateProperty`** — `MapCeField` has no arm for it, so a **bound** single-cast
  delegate (which `Ubel` stamps with `ptrValue`, making it navigable) becomes an empty placeholder
  folder — the very outcome `MapCeField`'s own comment (`:3966-3973`) calls a defect for pointer types.
  A code-path twin with no mechanical form.
- ⬜ **DataTable exports stop at 64 rows with no note** (P1/P7, not P3). `DataTableRowData` is a fixed
  64-row page (`IDumpService.cs:125`) while `DataTableRowCount` is the true total, and
  `BuildContainerLimitWarning` (`LiveWalkerViewModel.cs:1993-2015`) checks only Array/Map/Set — so
  *Copy CE XML* and *Export CSX* both export at most 64 rows of a larger DataTable silently. Audit #5's
  V8 fixed the Live Walker badge for this path, not the export note. **Checked only by grep.**

#### What the adjudicators measured about the tool — and the one fix it forced

- ⛔ **A recall bug, found and fixed.** The type-map axis matched braces with a bare counter;
  `SdkExportService` emits C++ source full of `"{"` / `"};"` literals, so one method's body swallowed a
  later one — spotted because `EmitClassHeaderFromLive`, which has no type switch, inherited
  `MapFunctionParamType`'s exact lacks-list. The same bug run the other way (an unbalanced `"}"`
  literal) would end a body early and **hide** a map. Fixed with a literal- and comment-aware scanner,
  then **measured**: 29 rows → 28, the one removed row was the fabricated one, **none added, none
  changed**. No map had been hidden; the adjudicated bound stands. Control 7/7 after the fix.
- The validity axis **inverts reality** for flags published through a renamed cache global; consumers
  over-reports four ways (43 of its 57 rows were gaps against an exporter that is not a live-value
  twin); tags over-counts shared callees and pipe-only commands. All four are now in the tool's header.

---

#### ✅ P2 MEASURED 2026-09-10 `[PATTERN-P2-2026-09-10]` — tree-wide population: exactly 2, one defect (recorded), one benign

`tools/check_json_default_ignore.py`. **A detector written as a gate and deliberately NOT registered in
`check_all.py`**: the sweep is in its record-don't-fix phase, so the tree stays red by design and the
detector is registered in the **same commit** that repairs its instance, during the fix pass.

| row | verdict |
|---|---|
| `ExperimentalSettings.SnapshotQuotaMb = 1024` (`ExperimentalSettings.cs:19`) | ⛔ **the defect** — `[W1-QUOTA-UNLIMITED]`, already recorded (and escalated in W3: `ApplyAutoQuota` sets 0 by itself) |
| `AobUsageFile.Version = 1` (`AobUsageRecord.cs:94`) | benign — 1 ≠ 0, so it is always written today; the detector flags it because nothing *states* that it must never be omitted |

⭐ **Fourth independent measurement, same answer.** W1 (by hand), W3 and W5 (by agents) and now a
mechanical detector all agree the whole-tree population is one real instance.

⭐ **The detector's selftest caught TWO bugs in its own parser before either could mislead**, and the
second is the one worth remembering:
1. Properties were matched one per line with a `$` anchor, so two declarations on one line merged
   into one property with a garbled initializer (the enum case flagged `Mode.First` and missed
   `Mode.Third`).
2. The first fix for that kept the regex's `^…$` anchors under `finditer`, where without `re.M` they
   can only match a one-line class body. The selftest failed three cases — and **the tree run went
   GREEN with the known positive missing**. Read on its own, that `exit 0` would have recorded P2 as
   clean. It is the second time in this stream a broken matcher reported green (P1's triage filter
   was the first) and the second time a control caught it.

Final: selftest **7/7**, tree **red with exactly the 2 rows above**.

✅ **APPLIED 2026-09-10 (fix pass, first row).** All four items below landed in one commit: the
test went red first, then green; UI tests 4802 / 4802; gates 21 / 21. The recipe is kept for the
record:
- `ExperimentalSettings`: drop `DefaultIgnoreCondition = WhenWritingDefault` from its context — the
  **spec-conforming** fix (`docs/teleport-coord-library-spec.md:618`: "MUST NOT be WhenWritingDefault";
  follow the `UiOptionsSettings` / `BookmarkFile` dialect). `enabled: false` will then be written too,
  which old and new builds both read identically.
- `AobUsageFile.Version`: add `[JsonIgnore(Condition = JsonIgnoreCondition.Never)]` — **behaviour-neutral**
  (it is already always written) and it states the intent. ⚠ Do **not** change that context's
  `DefaultIgnoreCondition`: the per-machine hint cache is shared with the DLL's `Flamme`, and writing
  every zero-valued field would change a cross-language file for no gain.
- Register the detector in `check_all.py` in the same commit.
- The red-before-green test, written and measured red-by-construction, then backed out when the
  maintainer confirmed this is still the finding phase — add it to `ExperimentalGateTests.cs` **before**
  the fix and watch it fail:

```csharp
[Fact]
public void SnapshotQuotaMb_Unlimited_Zero_SurvivesARoundTrip()
{
    // [W1-QUOTA-UNLIMITED]. 0 means "Unlimited", chosen by the user (the quota combo) or BY ITSELF
    // (ApplyAutoQuota, once the retained set outgrows the 5 GB preset). Under WhenWritingDefault the
    // key was OMITTED -- 0 is default(int) -- the next launch reloaded the = 1024 initializer, and
    // SnapshotStore FIFO-deleted the snapshots the user had opted to keep. The test above
    // round-trips 2048 only, which serializes fine and could never see it.
    var gate = new ExperimentalGate(_platform);
    gate.SnapshotQuotaMb = 0;
    Assert.Equal(0, new ExperimentalGate(_platform).SnapshotQuotaMb);

    // The flag beside it must survive the same round-trip, in both states.
    gate.IsEnabled = true;
    var reopened = new ExperimentalGate(_platform);
    Assert.True(reopened.IsEnabled);
    Assert.Equal(0, reopened.SnapshotQuotaMb);
    reopened.IsEnabled = false;
    Assert.False(new ExperimentalGate(_platform).IsEnabled);
    Assert.Equal(0, new ExperimentalGate(_platform).SnapshotQuotaMb);
}
```
⚠ `ExperimentalGateTests.cs` is **CRLF** in the working copy (150 CRLF, 0 LF, measured 2026-09-10) —
insert with CRLF, never mix.

---

#### ✅ P6 MEASURED 2026-09-10 `[PATTERN-P6-2026-09-10]` — 20 snapshot-address handoffs: 16 gated, 4 ungated (all Class Pivot, recorded)

`tools/check_session_gate.py`. **A detector written as a gate and deliberately NOT registered in
`check_all.py`** — red by design until the fix pass gates Class Pivot, and registered in that same
commit.
✅ **Registered 2026-09-11 (batch B14)**, in the commit that gated Class Pivot. The table below is the
measurement at registration time; Class Pivot's row is now gated too.

| panel | address handoffs | verdict |
|---|---:|---|
| Snapshot Diff | 4 | ✅ gated — `IsEnabled` → `CanUseDiffRowActions` → `GameSessionId` |
| Snapshot Group | 4 | ✅ gated — **in XAML only**; the RelayCommands carry no `CanExecute` |
| SPC single | 4 | ✅ gated — `CanUseResultRowActions` → `_currentSessionId` |
| SPC Group | 4 | ✅ gated — **in XAML only**, through the shared `CanUseResultRowActions` |
| **Class Pivot** | **4** | ✅ **gated since B14** — `CanUseResultRowActions` → `_currentSessionId`. (Was ⛔ ungated, `[W1-PIVOT-SESSION]`: Open-in-Live-Walker and Copy Address had no `IsEnabled` at all, and the two Locates were gated on `SelectedResult != null`, which never reaches the session.) |
| Detect Player Stats | — | not counted: `LocateInGWorld(row.ClassName)` passes a **class name**, valid in every launch (confirmed at the invoke sites, not taken from the comment) |
| Live Walker bookmarks | — | out of scope: a restore re-resolves through the GWorld spine and checks the saved class name before saying "loaded" — an identity check, not a session gate |

⭐⭐ **THE DETECTOR HAD THREE BUGS, AND TWO OF THEM LEFT THE HEADLINE NUMBER RIGHT.** Each was found the
same way — by checking the run against the hand-made map above **row by row, never by its total**:

1. **Attribution by file.** Every class declared in a file was mapped to the whole file, so the helper
   and row classes beside each ViewModel (`PivotFieldPick`, `NoiseRowVm`, `SpcSnapshotPick` …) re-reported
   their neighbour's commands against the wrong panel. The run said **15** ungated; the truth is **4**.
2. **Readers from a class's own body only.** SPC reaches snapshots through its helper
   `SpcSnapshotPick.Meta`, so **SPC dropped out of scope entirely** — and because SPC is gated, the count
   of ungated handoffs *stayed correct*. An ungated SPC would have vanished without a trace.
3. **Readers found transitively, payloads not checked.** The Pointer panel came into scope and its
   **nine copies of the LIVE global pointers** (GObjects, GNames, GWorld …) were reported as snapshot
   handoffs. An address now counts only when it is rooted at the command's own row parameter or a
   `Selected*` row.

Plus one the selftest caught: `[RelayCommand(CanExecute = nameof(X))]` holds a nested `)` that `[^)]*`
could not cross, so such commands were never seen. Final: selftest **5/5**, tree = exactly the table.

⚠ **The general lesson, stated because it has now happened in P1, P2 and P6**: a total that matches the
expectation is not a verification. P1's filter hid both known positives and still produced a
plausible count; P2's detector went green with its known positive missing; two of P6's three bugs left
the number right. **Every one was caught by comparing individual rows with an independent answer.**

⬜ **For the fix pass:** gate Class Pivot's four handoffs the way both siblings do — a `CanUse…` property
comparing the selected snapshot's `GameSessionId` with the live session, bound as `IsEnabled` on all four
buttons. The tell that this was always intended: `_engineState` is assigned at
`ClassPivotViewModel.cs:226` and **read nowhere**, where both siblings store `state.GameSessionId` at the
same point and compare it. ⚠ Expect the buttons to be **disabled by default after a reconnect**:
`RefreshAsync:501-503` defaults the picker to `Snapshots[0]`, which is then a previous-launch snapshot —
that is the gate working, not a regression. Register the detector in the same commit.

🟡 **Lead, not filed:** a bookmark whose saved address is recycled in a new launch by **another instance
of the same class** passes the class-name identity check, and loads that other instance under the
bookmark's label. Narrow, and not measured.

---

#### ✅ P4 / P7 / P8 SWEPT 2026-09-10 `[PATTERN-P4-P7-P8-2026-09-10]` — 58/58 ruled, 5 confirmed (1 HIGH · 2 MED · 2 LOW), 0 refuted

Two adjudicators ruled every row, one per batch. Each defect candidate then went to an adversarial
refuter, which defaults to REFUTED; none was refuted. `[P4-OTHER-INSTANCE]`'s branch guard and
navigation path were then re-read by hand. These are source reads only: nothing was built, launched
or measured, because CE and a game were in use by another session. **None of the five is repaired.**

| batch | rows | ruled | defect rows → findings | legitimate | recorded | n/a | known positives |
|---|---:|---:|---:|---:|---:|---:|---|
| P4 — `LiveFieldValue`'s own `init` members | 44 | 44 | 9 → 4 | 33 | 2 | 0 | `MapElements`, `SetElements` → ALREADY-RECORDED ✅ |
| P7 — auto / timer paths that do real work | 5 | 5 | 0 | 3 | 1 | 1 | Teleport pose poll → ALREADY-RECORDED ✅ |
| P8 — computed reads of a plain member | 9 | 9 | 3 → 1 | 0 | 3 | 3 | `ArrayElements` (both axes) → ALREADY-RECORDED ✅ |

**P7's residue across the whole tree is the one recorded instance.** Every other auto path runs its
manual path's own code: Live Walker's auto-refresh, the System-tab diagnostics timer, and the
Snapshot auto loop, whose cap and low-disk note lands in a `StatusText` that the countdown never
overwrites.

⭐⭐ **ONE COMMIT, FIVE DEFECTS.** All four P4 findings came from the same change, and so did
`[W1-CONTAINER-STALE]` before them: `[LWREFRESH-2026-08-21]` (ce6f5426). Before that commit,
`Fields[i] = newFields[i]` replaced every row object, so every `init` member was fresh. The fix for
the one-row grid drift copies values onto the surviving rows instead. That is correct for the drift,
but it silently narrowed "fresh" to the members `CopyLiveValuesFrom` assigns. The class header
(`LiveFieldValue.cs:104-108`) states the premise that all four break: the `init` members are
"exactly what the same-layout branch checks". What the branch at `LiveWalkerViewModel.cs:6524-6525`
actually checks is the field **count** and **`Fields[0].Name`**, and nothing else.

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

##### ✅ `[P4-CONTAINER-BASE]` MED — `ArrayDataAddr` / `MapDataAddr` / `SetDataAddr` stay at the first walk's buffer (FIXED IN SOURCE 2026-09-11)

`LiveFieldValue.cs:206/263/315`. A container's DATA pointer moves when the container reallocates,
and is omitted while it is unallocated (`Fern.cpp:1515/1611/1651`). A refresh copies `ArrayCount`
and `ArrayElements`, but not the base. Drilling the refreshed row then computes each element's
address from the stale base (`PopulateArrayContainerFields :1478-1517`). The result is **current
values shown next to addresses in the freed allocation**. An inline edit of a scalar element writes
into freed heap and still prints `Written: …` (`:5406`, `:5418`).

- **Scenario:** a `TArray<FInventorySlot>` at Num = Max = 4 grows on a pickup and is reallocated.
  Refresh, drill the array, edit `[2].Count`.
- A refresh from **inside** the container view re-walks and is correct (`:5131-5144`).
- ✅ **Safe for arrays:** make `ArrayDataAddr` copyable. Its elements are already copied from the
  same walk, and nothing binds it, so no notification is needed.
- ✅ **Safest for all three:** re-walk the parent at drill time in `NavigateToContainerAsync`, the
  way `RefreshAsync`'s container branch does.
- ⛔ **Do NOT copy `MapDataAddr` / `SetDataAddr` on their own.** `MapElements` / `SetElements` are
  still first-walk (`[W1-CONTAINER-STALE]`), so a fresh base would pair **old sparse indices with the
  live buffer**. Edits would then corrupt a live entry instead of freed memory, which is worse. Those
  two must move in the same change as the W1 fix.
- **Also needed:** `ArrayCount`, `MapCount` and `SetCount` need
  `[NotifyPropertyChangedFor(nameof(IsContainerNavigable))]`. Without it, a realized row that goes
  from 0 to N elements never shows its `[]` button.
- ✅ **FIXED IN SOURCE 2026-09-11, BOTH safe shapes, with `[W1-CONTAINER-STALE]`:**
  - **(1) Refresh:** the three data addresses are refreshed by `CopyLiveValuesFrom`, but only
    together with the element lists and the map/set stride and value offset. The ⛔ note above is
    honoured: never a base on its own.
  - **(2) Drill:** `NavigateToContainerAsync` now calls `RereadContainerRowAsync` before opening a
    non-DataTable container.
    - It re-walks `CurrentAddress` with the crumb's `ClassAddr`, the same walk as `RefreshAsync`'s
      container branch.
    - It requires the same address, the same class and a row with the same Name / Offset /
      TypeName / Size, and copies the fresh values onto the row.
    - It then re-checks `IsStillOnParent`, and that the container is still non-empty.
    - On failure it opens what the row held and **appends** a "could not re-read" warning after
      the drill, so the drill's own truncation notice cannot overwrite it.
  - This closes the **no-refresh variant** as well: the game reallocates after the walk, and the
    user drills without a Refresh.
  - The three counts gained the `IsContainerNavigable` notification.
  - **Red → green:**
    - the array reallocated on a refresh, then drilled;
    - a map gaining its first entries (geometry, base, `IsContainerNavigable`);
    - an array and a map reallocated since the walk, then drilled with no refresh;
    - the failed re-read path.
  - `ContainerTruncationTests`' stub now answers the parent re-read. Its own header already said "a
    real drill always has a walked parent instance".
  - ⚠ **The batch's adversarial review** (3 lenses, 15 agents, refuters defaulting to REFUTED):
    **7 survived, 5 refuted.** It was repaired in a follow-up commit.
    - **Blanked address:** the re-read copied a raw walk row, and those are never address-stamped.
      That blanked the grid row's Address, and with it Hex and +CE, whenever the row stayed on
      screen: the container emptied, or the drill threw. The row now keeps its own address.
    - **Find Refs owner auto-drill:** it is fire-and-forget. A re-read there repeated the walk
      `UpdateDisplay` had just applied, and in a real pipe let the drill's truncation notice
      overwrite the "← Back returns to" hint, the only way back out of a re-rooted spine. That path
      now skips the re-read.
    - **Deadline:** the re-read carries `RefreshAsync`'s deadline. It was refuted as a finding, and
      adopted because it costs nothing.
    - **Pins:** for the post-re-read parent check (a gated stub), the "empty now" branch, the
      class / address / offset gates one fact at a time, and the array and set
      `IsContainerNavigable` notifications. Two comments that overstated were corrected (when the
      DLL publishes a data address).
    - Each deleted check was **mutation-checked** against its pin, with each file restored
      byte-exact.
  - **Refuted, and recorded here so they are not re-raised as this change's defects** (both are
    pre-existing, and the change does not worsen them):
    - an unbound sparse delegate's `ArrayInnerType` / `ArrayElemSize`, which stay init-only;
    - an unset→set `TOptional<Struct>`'s `Struct*` members, which stay init-only.

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

##### ✅ `[P8-BOOKMARK-TIP]` LOW — re-saving into an occupied bookmark slot leaves its tooltip naming the previous target (FIXED IN SOURCE 2026-09-12)

`LiveWalkerViewModel.cs:3641`. `SaveBookmarkToSlot` writes the plain members `SavedAddress`,
`SavedObjectName` and `SavedClassName`, then sets `IsOccupied = true`. That setter raises
`TooltipText` only `if (SetProperty(...))` (`:6859`), and `true → true` raises nothing. The label
repaints, but the hover keeps A's class, name and address, while a click goes to B. Save mode has no
occupied-slot guard (`:3670-3673`), so this is the normal re-save gesture. The existing tests call the
getter directly and only ever save into empty slots, so they cannot see it.

- ✅ **Safe fix:** raise `nameof(TooltipText)` explicitly at the end of `SaveBookmarkToSlot`, through
  a slot method. Also correct the setter's comment at `:6857-6858`.
- ⛔ **Unsafe:**
  - raising on a same-value `IsOccupied` set, which `BookmarkTests.cs:55-65` pins as not happening;
  - replacing the slot object.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L34), the recorded safe fix:
  - `SaveBookmarkToSlot` ends with `slot.RefreshTooltip()`, a new slot method that raises `TooltipText`;
  - the setter's comment now says it refreshes on a flip only;
  - the same-value setter behaviour is unchanged.
  - **Red first:** a VM-level re-save into slot 0 with a second object must raise `TooltipText` and name the new target.

##### Widenings of recorded rows

- **`[W1-CONTAINER-STALE]` reaches a second reader: `ValueTooltip`** (`LiveFieldValue.cs:457`, bound at
  `LiveWalkerPanel.axaml:624`). That hover exists because the 200 px cell clips long array previews.
  A fix must raise **both** `DisplayValue` and `ValueTooltip`. `LiveFieldValueTooltipTests` pins the
  pair only at the attribute level, so a hand-written `OnPropertyChanged` must pair them by hand.
  ✅ *Done 2026-09-11: the hand-written notification raises both, and every staleness test asserts
  both.*
- **`[W1-CONTAINER-STALE]` fix trap:** `MapStride`, `MapValueOffset` and `SetStride` are published
  only when the container had elements, and the three data addresses only when non-null. Suppose a
  W1 fix makes `MapElements` / `SetElements` copyable but leaves these `init`. The fresh elements are
  then paired with a **zero stride**, which falls back to the client-side guess that audit #5 V2
  retired. **Six members must travel with the element lists.** ✅ *Done 2026-09-11: all six travel
  together in `CopyLiveValuesFrom`. `Refresh_MapGainsItsFirstEntries_…` pins the zero-stride trap
  (it failed first with "Expected 24, Actual 0").*

##### What the sweep did not cover, and the tools' limits

- ⚠ **The P4 population missed 10 of the 54 `init` members.** Every one has a name containing
  `StructType` / `StructClass` / `StructData` / `StructName` / `RowStruct`, and the enumerator was an
  uncommitted scratch script. Nine of the ten are layout or synthetic-row-only. The tenth,
  **`StructDataAddr`, is the sharpest consequence of `[P4-OTHER-INSTANCE]`**, and it was found by
  reading, not from the list.
- ⚠ **`pattern_p8.py` produced 3 artifact rows out of 9, with its control green.** Its `.P =` match
  goes by member name, not type, so `GroupSlotMatch.ClassName` writes (`DumpService.cs:2191/2429`) were
  attributed to `PropertySearchMatch`. It also counted a read (`PropertyName = match.PropName`,
  `PropertySearchViewModel.cs:375`) as a write. These are false positives, which adjudication
  removes; they cannot hide a real row. The limit is recorded in the tool's header.
- **Stale comment, not a finding:** `OnFieldsRebuilt` (`LiveWalkerViewModel.cs:763-775`) still
  describes the in-place branch as `Fields[i] = newFields[i]` raising Replace. ✅ *Corrected
  2026-09-10 in the `[P4-OTHER-INSTANCE]` commit.*

##### Leads, not filed (unmeasured)

- **DataTable objects.** The RowMap row is appended after `UpdateDisplay`, so every refresh of one
  takes the **rebuild** branch and scrolls to the top. An auto tick that lands before the previous
  row load finishes could take the in-place branch, and both loads pass the `:6662` guard, which
  would append two RowMap rows.
- **Snapshot auto loop.** The dataset cap measures the whole db+WAL file (`SnapshotStore.cs:546`).
  Once the file is above the cap, every later auto capture may stop after one chunk and still count
  as captured. Each stop IS disclosed. Whether FIFO eviction brings the file back under the cap was
  not traced.
- **Live Walker status messages.** `RefreshAsync` opens with `ClearStatus()`, so with Auto on, a
  message from another action stays up for at most one interval (≥ 6 s). No warning whose loss matters
  was found.
- **Pointer panel.** `ResetDiagnosticsAsync`'s "Reset done" is erased at once by the refresh it
  awaits (`PointerPanelViewModel.cs:1480-1483 → :1458`). It is a confirmation, not a warning.

⬜ **For the fix pass:** `[P4-OTHER-INSTANCE]` and `[P4-GUESS-SHIFT]` share one gate and land
together. ✅ *The gate landed 2026-09-10, and the same-object staleness trio on 2026-09-11.
`[P8-BOOKMARK-TIP]` remains, independent.*
🟡 **Lead, LOW, not filed (unmeasured):** Back / Forward / a breadcrumb jump into a container view
re-renders from the crumb's cached `ContainerField` (`RepopulateContainerView`). That is the grid
row object, as last read at drill time or at a refresh of the parent grid. A Refresh from INSIDE
the container view re-walks, but hands `RepopulateContainerView` a fresh field object and leaves the
crumb's `ContainerField` as it was. So a later Back into that view can show element addresses from
before a reallocation. Fix shape: the `RereadContainerRowAsync` treatment on those re-entry paths. After the gate, the copy path runs only for a same-object, same-layout refresh. Then
`[W1-CONTAINER-STALE]`, `[P4-CONTAINER-BASE]` (the six members above) and `[P4-PTRCLASS]` are
same-object staleness, and they land as one change right after. `[P8-BOOKMARK-TIP]` is independent.

---

#### ✅ P5 SWEPT 2026-09-10 `[PATTERN-P5-2026-09-10]` — 86/86 ruled, 2 confirmed (both LOW), 1 refuted, 1 overridden to recorded

Three adjudicators ruled every row. Refuters then took every candidate, defaulting to REFUTED. After
that, three things were settled by hand: both confirmed rows, and one disagreement between batches.
These are source reads only, because CE and a game were in use by another session. **None of this is
repaired.**

| batch | rows | ruled | defect rows → outcome | correct merge | not a conflation | recorded | known positive |
|---|---:|---:|---|---:|---:|---:|---|
| DLL flags + pipe keys | 35 | 35 | 1 → 1 confirmed | 0 | 24 | 10 | `ScanForValue`, `FindInContainersDeep` → ALREADY-RECORDED ✅ |
| UI renderings | 29 | 29 | 3 → 1 confirmed, 1 refuted | 7 | 17 | 2 | `ScanSuffix` (`InstanceFinder:647`) → ALREADY-RECORDED ✅ |
| UI supplement — rows the first population missed | 22 | 22 | 1 → overridden to recorded (below) | 2 | 19 | 0 | none mechanical; the widening is pinned by the tool's control |

⭐⭐ **P5 IS MOSTLY BOUNDED BY ITS OWN CONSTANTS.**
- **Deadlines the user cannot change.** Every scan whose flag folds cancel + clock + fault into
  `deadline_hit` runs on a `constexpr` deadline (`Aura.cpp:2670/2961/3589/5850/6073`). So every
  rendering says "retry" or "re-run", which is right for all three causes.
- **The user's own cancel never reaches these texts.** The DLL has no cancel command, and every VM
  catches its `OperationCanceledException` with its own "cancelled" line. A DLL-side cancel comes
  only from a dropped connection, and that reply is never delivered (`Tot.h:78-110`).
  ⚠ *Holds for the scan VMs P5 covered, NOT for every VM. Proxy Deploy's Deploy / Undeploy have no
  `OperationCanceledException` handling at all, and a cancel there crashes the UI
  (`[A3-DEPLOY-CANCEL]`, `[TRACKB-A3-2026-09-10]`).*
- **Every neutral pipe key is single-cause** (`truncated` / `aborted` / `budget_hit`). Where a producer
  has both a cap and an abort, they go out as separate keys.

The two real instances are both places where **advice** was written for only one cause.

⚠ **The value scan's cap folded into `deadline_hit` needs no filing.** Its only consumer
(`ValueSearchViewModel.cs:1026`) names it: *"(Ns deadline / result cap) — raise the Timeout slider or
narrow the predicate"*. "Narrow the predicate" is the cap's remedy, and Max is visible in single mode.
⛔ Do not "fix" this half. Only its group twin gives the wrong advice (`[P5-GROUP-ADVICE]`).

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

##### ⛔ Refuted or overridden — do not re-raise

- **REFUTED: `PropertyXrefDialog.cs:430` "[DEADLINE HIT — partial]" for a worker fault** (P5U-2).
  - The harm rested on "a Refresh repairs a fault", which the source does not support. These scans
    rebuild no reused index, so a stub that faults on an index faults again.
  - The fault-as-deadline label itself is D2's residual.
  - ⛔ Its proposed fix, reusing `DeadlineClause`, is **unsafe**. It adds "retry to continue", a
    promise neither cause can keep: the 30 s scan restarts from index 0.
  - If this text is ever re-worded, name both causes without promising anything, e.g.
    *"[⚠ partial — the 30 s budget ran out, or a scan worker faulted (see offsets log)]"*.
- **OVERRIDDEN to ALREADY-RECORDED: Live Walker Find Refs' "DEADLINE HIT, retry to continue" for a
  worker fault** (P5S-1).
  - The refuter CONFIRMED it as new, reading D2 as covering only `DeadlineClause`.
  - But `DeadlineClause` has **one** call site, so D2's "8 emit sites" cannot mean it. `git grep
    DeadlineHit e6360903` lists **exactly eight** fault-reachable renderings, and both
    `LiveWalkerViewModel.cs:2679` and `PropertyXrefDialog.cs:430` are among them.
  - D2 accepted this label for all eight. Its residual text now names them.
  - ⚠ **Its by-product is real, and is filed as a widening, not under P5.** When the scan is partial,
    `:2694` still says *"No references found — likely held by a non-reflected pointer"*, blaming the
    game for a scan that did not finish. `[P1-SPARSEDELEGATE-REFS]` records the same wrong blame on
    the `deadline_hit=false` path. Its fix should also suppress the hint whenever `DeadlineHit` is set.
    ✅ It does: batch L08 put both paths under one status rule.

##### Tools and records corrected on the way

- ⚠ **`pattern_p5.py`'s UI axis dropped 36 of 64 flag reads.** It missed every ternary broken across
  lines and every flag passed to a renderer as an argument, including `ScanSuffix`, the only
  `DeadlineClause` caller. Fixed in 8a3f65ad (control 6/6). The 22 rows it missed were adjudicated as
  the supplement above.
- **Its remaining limits**, now written in its header:
  - The flags axis misses a flag assigned to a local and copied later. `FindReferencesToUObject` was
    reached through its wire row instead.
  - `FindInContainersDeep`'s cancel cause arrives through `scan.deadlineHit` and is not expanded.
  - `per_slot_cap` numbers are pulled in by key name.
  - The "text names" heuristic was wrong on **~12 rows**. Treat it as a pointer to look, never as
    evidence.
- **D2's residual said "`DeadlineClause` and its 8 emit sites".** `DeadlineClause` has one. It is now
  re-worded to list the eight renderings, so "not through `DeadlineClause`" can no longer be read as
  "not recorded".
- The brief's first claim, that `todo.md:1314` recorded the value scan's cap, was false. Fixed in
  70f13d74.

##### Leads, not filed (unmeasured, and not checked against every record)

- **A comment contradicts the code** at `Aura.cpp:3881-3883`. It says Find Refs' sparse-delegate pass
  is gated on `scan.deadlineHit`, but the gate at `:3899` tests the local `incomplete()`. So a worker
  fault also skips that pass. That is harmless if intended, but both the comment and e6360903's body
  say otherwise.
- **P1-shaped stops that are published nowhere:**
  - Find Refs' 32-match cap (`ConcatTruncate`, `Aura.cpp:3875`). "Found 32 reference(s)" does not
    say it was capped; this is the `[W3-XREF-CAP]` shape.
  - `pe_profile_get`'s `emitted < limit` bound (`Fern.cpp:4247`).
  - `SearchByName`'s and `ListClasses`' aborts (`Aura.cpp:1562-1564`, `:5484-5486`). These run on
    connection-bound threads and abort only when their own reader is gone, so they are likely
    harmless.
- **Z10-shaped, verify before filing:** Console and Interesting Functions advise *'tick "Game classes
  only"'* on a row-cap stop even when it is already ticked (`ConsoleViewModel.cs:290-292`,
  `InterestingFunctionsViewModel.cs:655-656`).
- **The class-picker badge** says *"the result list was capped"* on Value Search when a timeout or a
  fault set it. That is harmless, because its acted-on claim is "lower bound". But if it ever gains
  advice, it must not say "raise Max".

⬜ **For the fix pass:** two independent text-level changes, neither needing a wire change.
`[P5-GROUP-ADVICE]` is one string. `[P5-PIVOT-FETCHCAP]` is one flag and one sentence, C# only.

---

### TRACK B — the area sweep, 4 waves

Same protocol as the June waves: finders → adversarial refuters (default REFUTED,
`implied_fix_safe` mandatory) → hand adjudication → ledger row + commit. **One wave per session.**

| wave | clusters | band | finders | shape |
|---|---|---:|---:|---|
| **A1** | APP-SHELL ×2 | 6,695 | 2 | two lenses, as W3 |
| **A2** | SCAN-CORE ×2 · DLL-OTHER | 7,334 | 3 | the DLL core — `Ubel` (the UStruct walker) and `Genau` (the offset finder) barely appeared in June |
| **A3** | WIRE · CE-BRIDGE · WIRE-DTO | 7,409 | 3 | ⭐ the whole wire surface in ONE wave, so P1/P3 can be judged across all three transports at once |
| **A4** | AURA-GRAPH · VALUESEARCH · LIVEWALKER · OBJTREE · TELEPORT · EXPORT · SNAPSHOT+PIVOT-SPC | 10,346 | 4 | ⭐ **every one of these was already swept in June** — same files, different lines, so the recorded rows are a map and the agents start with context |

⭐ **A3 is deliberately shaped around the patterns, not around size.** Putting `WIRE`, `CE-BRIDGE`
and `WIRE-DTO` in one wave means a single agent set holds the pipe, the mailbox, the C ABI exports
and the DTOs simultaneously — which is exactly what it takes to see P3 (a fix on one transport of
three), the shape that produced the June sweep's most important result.

### Track B ledger — a fresh session resumes from here

| wave | status | finders | raw | survived refutation | confirmed | commit |
|---|---|---|---|---|---|---|
| A1 APP-SHELL | ✅ | 3 (+0 gap-fill) | 7 | 6 | **0H 1M 5L** | 2026-09-10 |
| A2 SCAN-CORE ×2 · DLL-OTHER | ✅ | 4 (+0 gap-fill) | 11 | 9 | **0H 2M 7L** | 2026-09-10 |
| A3 WIRE · CE-BRIDGE · WIRE-DTO | ✅ | 4 (+0 gap-fill) | 13 | 12 | **0H 4M 8L** | 2026-09-10 |
| A4 AURA-GRAPH · VALUESEARCH · LIVEWALKER · OBJTREE · TELEPORT · EXPORT · SNAPSHOT+PIVOT-SPC | ✅ | 5 (+0 gap-fill) | 18 | 14 | **0H 4M 10L** | 2026-09-10 |

### ✅ A1 SWEPT 2026-09-10 `[TRACKB-A1-2026-09-10]` — APP-SHELL: 7 raised, 6 confirmed (1 MED · 5 LOW), 1 refuted

- **Scope: 6,695 never-audited lines in 69 files.** Re-derived at the start of the wave, and unchanged
  since the plan, because nothing but docs and tools has been committed since.
- **Three lenses, not the plan's two:**
  - **A** persistence, lifecycle and fault handling, and **B** composition, wiring and promises. Both
    read the same 60 files.
  - **C** took the 9 CE Lua emitter files. These have their own rulebook in CLAUDE.md, and a generic
    lens would have spread thin over them.
- **Coverage was reconciled, not assumed.** Each finder returned the list of files it opened: 60/60,
  60/60 and 9/9. So the gap-fill stage never had to run.
- **Cost:** 7 agents, ~1.7M tokens.
- **Constraints:** source reads only, because CE and a game were in use by another session.
  **Nothing fixed.**
- **Hand adjudication:** every confirmed row was re-read at its source before being recorded here.

⭐⭐ **FIVE OF THE SIX CONFIRMED ROWS ARE REPAIRS THAT STATE A CLAIM THEY DO NOT KEEP.** The brief
predicted this: the band is August/September fix-pass code, and no audit has ever read a fix pass.

| row | the claim | where it is stated |
|---|---|---|
| `[A1-COORD-RESURRECT]` | the `.bak` fallback is for a **corrupt** main file | `CoordinateLibraryStore.cs:109`, commit 93bb0f5e |
| `[A1-COORD-BACKUP]` | a `.preclear.bak` / `.preimport.bak` makes a clear or an import recoverable | the B6 fix (d98e143b), `en.axaml:1267-1269` |
| `[A1-LOG-RESUME]` | after startup, "slot 0 is free for the new session" | the B31 fix, `LoggingService.cs:286-288`, `:298` |
| `[A1-SLOTSYM-FAILED]` | refcounting makes "a second live record's symbol survives" true | `CeLuaHygiene.cs:803-804` |
| `[A1-LUA-WAIT]` | a real deadline, "iteration count only as fallback" | `CeLuaHygiene.cs:191`/`:640`, commit 45eb7c2f |

The sixth, `[A1-DETECT-REPUBLISH]`, is a fix (X5) that an older, still-open gap (L18) undoes; the fix
never accounted for it.

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
  - ⬜ Residual, unchanged: inside ONE session that rolls, the compression sweep can still call the live rolled file
    idle after a quiet hour. That is the "different rule" above, and it is not built.

##### ✅ `[A1-DETECT-REPUBLISH]` LOW — a Detect Player Stats run in flight at disconnect republishes the old game's rows (FIXED IN SOURCE 2026-09-12)

`DetectStatsViewModel.cs:250`. X5's `ClearOnDisconnect` (`:110-112`) promises that a reconnect never
shows the previous game's fields. But `DetectAsync` has no cancellation (July **L18**, still open), and
its per-class probe catch (`:206-209`) swallows every pipe failure. So a run that was suspended at the
disconnect finishes anyway and republishes its rows at `:250-255`.

- **Deterministic window:** with the snapshot signal on, the multi-hundred-ms
  `TryLoadDecreasedFieldsAsync` await (`:170`). The probe-loop window is an ordering race.
- **Why LOW:** the feature is experimental-gated, the rows are labelled "reference only", and the
  handoffs pass class names only.
- ✅ **Safe fix:** a generation guard, in the `InstanceFinderViewModel` mould. Bump it in
  `ClearOnDisconnect`, check it after every await, and bail without touching the results or the
  status.
- ⛔ **Unsafe or insufficient:** L18's own fix, a CTS cancelled from the tab.
  - The per-class catch swallows the `OperationCanceledException`, so the loop still publishes.
  - A `ThrowIfCancellationRequested` outside that catch makes the outer catch print "Detect failed"
    over the reset.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded generation guard** (batch L19, with
  `[A4-LW-DISCONNECT-PARENT]`).
  - `_detectGen` is bumped in `ClearOnDisconnect` and compared after every await: the batch, the
    snapshot signal, each class's instance lookup and walk, and each loop turn.
  - A superseded run bails without touching the rows or the status. The outer catch is guarded too,
    so a superseded failure cannot print "Detect failed" over the reset.
  - No CTS.
  - **Tests, red first:** a run gated in its first await is disconnected and then resumed, once with a
    result and once with a pipe failure. The rows and the reset status stay untouched. 7/7 mutants
    killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5218/5218.

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

##### ⛔ REFUTED — do not re-raise

- **`A1-A-2`** `ProxyOrphanScanner.cs:472`: "the already-gone branch drops the prune stop reason".
  - `RemoveOrphanProxyAsync` re-plans from disk first. A folder whose file of ours has gone classifies
    as `NoFilesAtAll` and is *Skipped* before any prune happens.
  - What remains is a millisecond race inside one `Task.Run`.
  - "Never listed again" is the documented rule (`OrphanScanTypes.cs:88-94`).
  - ⛔ Do not add `NoFilesAtAll` to `ShouldSurface`: every empty `Binaries\Win64` in every library
    would then be listed.

##### Measured clean — worth as much as the rows

- **Deletes that reach user folders are scoped right.**
  - `ProxyOrphanScanner`: non-actionable plans carry their files for display only. Removal re-plans
    from disk and refuses a non-actionable verdict. The prune is non-recursive and deepest-first.
  - `AppDataFolderMaintenance` / `AppDataRetentionPolicy`: a delete needs the literal `{prefix}.`
    lead-in inside the enumerated folder, and `maxAgeDays <= 0` deletes nothing. Migration moves a
    whole group, never overwrites, and rolls back on failure.
  - Log compression deletes nothing, and re-measures success with `GetCompressedFileSizeW`.
  - `VolumeRoot`'s four callers each fail to a safe sentinel.
- **The fault spine fails closed.**
  - `InputLayerFaultClassifier` (476 lines, never named by any audit) rethrows on every truncation,
    no-stack and classifier-throw path, and the AOT build keeps the type names its markers match on.
  - `DispatcherFaultGuard` fixes its verdict before logging.
- **The CE Lua emitters use only real CE API.** Every call was checked against
  `D:\Github\cheat-engine`'s `LuaHandler.pas` registrations. `processMessagesPaintOnly`, which is
  absent from CE 7.5, is feature-tested rather than called.
- **The mailbox layout matches `Mimic.h` field for field:** offsets, command ids, status and init
  values, magic, and `ContractVersion 3`.
  - Every bail branch leaves its record in the state its shape requires, and the success-close is
    unreachable after every bail.
  - The round-2 `onUnreadable` fix holds at all five callers. Teleport's two omissions are correct for
    its shape.
- **Composition.**
  - Every cross-tab handoff lands on the tab it drives, and is wired exactly once.
  - `AppComposition`'s 13 parameters line up with the constructor (B27 closed).
  - The in-band keyword boxes all follow the space-AND rule.
  - `Constants.cs` adds no per-game root file.

##### Leads, not filed (unmeasured or doc-only)

- ⚠ **A byte-corrupted test fixture — the CLAUDE.md NUL class.** `ProxyOrphanScannerTests.cs:614`
  holds a literal `0x0B` byte where the `\v` of `\version.dll` was meant (verified by byte scan;
  committed in 4f2f2dec). No current assertion reads that path:
  `BuildReport_ListsEveryPathAndTheFileDetail` passes on `DllNames`. So the test would not notice if
  the authorised-file line vanished from the report.
- **The mailbox gate pins only `ContractVersion` on the C# side.** `check_mailbox_contract.py` hashes
  the DLL surface, but nothing compares these against `Mimic.h`: `CeMailboxLayout`'s offsets and
  command ids, `InvokeScriptGenerator`'s private offsets, or Teleport's inline `0x331` /
  `0x340-0x358`. The tests pin them to their own literals. Today they all agree. But a layout change
  made by the gate's own procedure would bump the version and pass, while UI scripts kept writing the
  old offsets.
- **`PointerQueryScriptGenerator.cs:161-162`** (out of band): the ENABLE-side stale-buffer clear may
  free a buffer that belongs to another live record. It is the same two-record family as
  `[A1-SLOTSYM-FAILED]`.
- **`CeReadinessLua`** (out of band, `:79`/`:84`).
  - An unreadable mailbox (the game has exited) is reported as *"AOB scan may be wedged"*. That is the
    guess-instead-of-read shape the MUST rule forbids.
  - The 250 ms poll does not pump messages, so CE freezes for up to 25 s.
- **`en.axaml` contradicts itself or overclaims.**
  - `:143` and `:156` disagree on the Value Search timeout ("10–60 s, default 15" vs "10–90 s,
    default 25").
  - `:429` says the Self-Test "confirms the ProcessEvent hook is on the correct vtable slot", which
    `SelfTestAdvice`'s own doc says it cannot establish.
- **`AppDataRetentionPolicy.cs:13-15`**: the parameter doc says "unused" means max(write, access), but
  the method it points to deliberately uses write time only. A maintainer who "restores" the doc's
  version would silently disable the `Snapshots\` sweep.
- **SDK / USMAP exports** write straight to the final file name (`MainWindowViewModel.cs` ~3512/3660).
  A disconnect mid-write can leave a truncated file, where DumpAll uses `.partial`.
- **Live Funcs' Diff baseline survives a reconnect to a different game** (`ResetOnDisconnect` keeps
  `_baseline`). Also, `SetBaseline` sets `DiffMode = true` expecting a refresh, which does not fire
  when it is already true.
- **Game Class Filter:** a disconnect cancels the batch, and its catch then writes "Find Func cancelled
  at N/M" on an emptied grid.
- **Detect Stats' ✓ column** sorts on a two-state bool under a three-state badge.
- **Smaller doc and code mismatches:**
  - `PropertyScoringTable.cs:93-95` claims "Max" does not fire on `ChannelMaxIndex`, but the tokenizer
    splits that name so it does.
  - `ProxyImportAnalyzer.cs:20-25`'s doc contradicts `:57-65`.
  - `DumperDllPathStore` writes UTF-8 without a BOM; CE-side ANSI path handling for a non-ASCII folder
    was not checked.
  - `TeleportScriptGenerator.cs:126-127` has a ~15 ms boundary where a busy flag suppresses the
    command's own error message.
  - Stale comments at `CeLuaHygiene.cs:324-329`, `MainWindow.axaml.cs:290` and
    `Constants.cs:199-261`.

⬜ **For the fix pass:**
- `[A1-COORD-RESURRECT]` and `[A1-COORD-BACKUP]` land together (same store).
- `[A1-LUA-WAIT]` joins the P3 group, as a twin of AA29.
- `[A1-SLOTSYM-FAILED]` belongs with `[B30-REOPEN]`'s family: every `[DISABLE]` must be safe to run
  after an ENABLE that applied nothing. That rule deserves a shared rig case for every toggle.

### ✅ A2 SWEPT 2026-09-10 `[TRACKB-A2-2026-09-10]` — the DLL core: 11 raised, 9 confirmed (2 MED · 7 LOW), 2 refuted

**7,334 never-audited lines in 32 files.** This covers SCAN-CORE (the walker `Ubel`, the offset finder
`Genau`, `Himmel`, `Sein`, the four `Lugner` proxies, `Macht`, `Linie`) and DLL-OTHER (15 files, read
whole).

- **Finders: 4, not the plan's 3.** The walker got two lenses, one for layout across engine versions
  and one for memory safety, caches and threads. It is the heart of the DLL and barely appeared in
  June.
- **Coverage was reconciled.** Every finder opened every assigned file (5/5, 5/5, 12/12, 15/15), and
  no gap-fill ran.
- **Known positives:** both open recorded rows whose sites sit in the Genau hunks,
  `[W5-OFFSETS-UNMEASURED]` and `[P1-GENAU-ABORT]`, were reported by the Genau finder. Those hunks
  were demonstrably read.
- **Cost:** 11 agents, ~2.6M tokens.
- **Constraints:** source reads only, because CE and a game were in use by another session.
  **Nothing fixed.** Every confirmed row was re-read at its source, and S1a-02's premise was
  re-checked by hand in the vendored RE-UE4SS templates.

⭐⭐ **The layout claims were settled against REAL engine source, not argued.** The refuters read
`vendor/UnrealEngine` at the 5.8.2 tag and every tag back to 5.2, all 31 `vendor/RE-UE4SS`
member-layout templates, the vendored UHT, and CE 7.5's Pascal source. They also modelled one arithmetic
chain in Python. That is how one MED was **refuted** — UHT turns every bitfield container into
`UhtBoolType.UInt8` — and how both MEDs were **confirmed**.

⭐ **Seven of the nine are again a repair or a comment that states a claim the code does not keep**,
the same shape as A1:

| row | the claim | where it is stated |
|---|---|---|
| `[A2-UFUNC-TAIL-4X]` | the UFunction tail is "stable across all UE versions" | `Ubel.cpp:1468-1470` |
| `[A2-TOPTIONAL-INTRUSIVE]` | pointer / weak optionals are unset-by-null, and containers carry a trailing flag | `Ubel.cpp:5708-5715`, `technical-notes.md:586-603` |
| `[A2-STRUCT-PREVIEW-BOOLMASK]` | the shared struct decoder is "now the ONLY one" | `Ubel.cpp:1982` |
| `[A2-LAZY-LATCH-GUESS]` | "a real ElementSize gets MEASURED and latched" | commit 0c0660b0, `Ubel.cpp:391-393` |
| `[A2-WALKCLASSEX-UNMAPPED]` | an unmapped class gets the shared EMPTY ClassInfo, "never a cached one" | `Ubel.h:157-162`, commit 902e6702; and A10b's "FIXED" |
| `[A2-HEAP-ANCHOR-TEXT]` | "the data-scan fallback anchors just as well as the AOB" | `Genau.cpp:1666`, `verification-register.md:7940` |
| `[A2-METHODE-MANUALMAP]` | CE's `InjectDLL` TRUE "can be true on a real failure" | commit b491b1dc (AB2), `working-lessons.md:2408-2417` |

##### ✅ `[A2-UFUNC-TAIL-4X]` MED — on UE 4.11-4.17, `ParmsSize` is really `NumParms`, and invoke buffers are undersized inside the game (FIXED IN SOURCE 2026-09-11)

`Ubel.cpp:1472-1474`. `FunctionFlags` itself is measured (`DynOff::FunctionFlagsOffsetFor`, which is
correct for these versions). But the three fields behind it are read at a hardcoded `+4/+6/+8`, under a
comment that calls this "stable across all UE versions".

The RE-UE4SS templates put a `uint16 RepOffset` first on **every version from 4.11 to 4.17**: at 4.11,
`FunctionFlags` is 0x88, `RepOffset` 0x8C and `NumParms` 0x8E. The field disappears at 4.18, where
`NumParms` is 0x8C. Re-checked by hand in `MemberVariableLayout_4_11/4_17/4_18_Template.ini`. On 4.11-4.17
the three reads therefore land one field late:

| field read | what it actually gets |
|---|---|
| `numParms` | `RepOffset`'s low byte |
| `parmsSize` | `NumParms` |
| `returnValueOffset` | the real `ParmsSize` |

- **The consequence is an invoke that corrupts the game's heap.** `Fern`'s `invoke_function` sizes its
  buffer from `max(caller, fi.parmsSize)` (`Fern.cpp:5459-5473`). The UI's own buffer comes from the
  listed `parms_size` (`InvokeParamDialog.cs:596`), and it silently skips every param past it. So both
  are the same small number. ProcessEvent then reads inputs from, and writes out-params and the return
  value into, heap memory past the end of that buffer. `Wirbel`, `Schlacht` and `Dunste` size their
  feature invokes the same way.
- **Reachable on supported titles.** NEKOPALIVE is UE 4.11, "the verified floor", and Extinction is
  4.15 (`test-games.md`). There is no version gate on the invoke path.
- The CE mailbox path does **not** overflow: its return clear is bounded by a fixed slab. It only
  reports a wrong `parmsSize` to CE.
  ⚠ *Corrected by wave A3 (`[A3-CEFORM-4X-STALESLAB]`). The CE invoke FORM bakes that wrong
  `parmsSize` as its zero-fill span, and `Mimic` runs ProcessEvent on the persistent 1024-byte slab. So
  struct and out-FString slots past NumParms carry the previous command's bytes, and an out-FString
  assignment then frees a stale pointer inside the game. The root fix above cures it.*
- ✅ **Safe fix:** shift the tail by the `RepOffset` width when the **engine version is below 418**.
  Put it in a `constexpr` beside `FunctionFlagsOffsetFor` so `dll_helpers_test` can pin the
  4.17 / 4.18 boundary. Defence in depth: invoke sizing may also take the max with the param walk's
  `max(offset+size)`.
- ⛔ **Unsafe:**
  - "when `funcFlagsOff == 0x88`, read `+6/+8/+0xA`". 0x88 is ALSO the offset for 4.18-4.21, which
    have no `RepOffset`, so this would break OCTOPATH and DQ XI S.
  - Sizing from the walked params ALONE. A failed walk yields 0, which recreates the zero-length-buffer
    overflow documented at `Fern.cpp:5441-5452`.
- **Same family, a lead:** the UProperty-mode param `StructProperty` / `PropertyClass` reads in
  `WalkFunctions` use a fixed `UPROPERTY_OFFSET+0x2C` (`:1632`, `:1651`). For 4.11-4.17, c0b4e709's
  own measured table says 0x28. Fix the two together.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (batch B07). It is one commit with
  `[A3-CEFORM-4X-STALESLAB]` and the lead above.
  - **The shift.** `DynOff::FunctionTailShiftFor(ueVersion)` sits in Grimoire.h beside
    `FunctionFlagsOffsetFor`.
    - It is 2 on 4.11-4.17 and 0 otherwise.
    - It is keyed on the VERSION, never on `funcFlagsOff == 0x88`.
    - An unknown version (0) keeps the old reads.
  - **The reader.** `ReadFuncFlagsAndParams` shifts its tail by it. It is the one reader
    WalkFunctions and `ResolveFunctionInfo` share, so everything that lists or invokes functions
    gets the real `ParmsSize`, Fern's invoke sizing included.
  - **The lead.** WalkFunctions' two UProperty-mode subclass reads use
    `DynOff::UPropertySubclassStartFor`.
    - For a known version it is exactly `UBoolPropFieldSizeFor`'s measured delta: 0x28 on
      4.11-4.17, 0x2C from 4.18, plus the CPN slot.
    - An unknown version keeps +0x2C.
  - **Tests, red first.**
    - `dll_core_test` UFUNCTAIL: a 4.15 UFunction read back numParms 52 / parmsSize 3 /
      rvo 0x30, exactly the table above. 3 red.
    - `dll_core_test` UFUNCWALK: a pool-faking WalkFunctions walk. Both subclass reads came back
      empty at 4.15. 2 red.
    - The 4.18 and UE 5.5 controls were green throughout.
    - `dll_helpers_test` pins the 4.17/4.18 boundary, the unknown and below-floor versions, and the
      subclass start against `UBoolPropFieldSizeFor` at every known version, CPN both ways.
  - ⬜ **Not done (the row calls it optional):** Fern's invoke sizing does not also take the max
    with the walked params' `max(offset+size)`. The root fix makes `ParmsSize` right, and the CE
    form (below) carries the walked-max hardening.
  - 🟡 **Lead, not filed:** the Array / Map / Set UProperty-mode probes in WalkInstance also start
    at a fixed `UPROPERTY_OFFSET + 0x2C`. Their `{0, ±4, ±8, ±0x10}` spread already covers 0x28 and
    the CPN +8, so only the probe ORDER differs on 4.11-4.17. Left as is.
- ✅ **Review follow-up 2026-09-11 (adversarial review of d8a7f44f + d5e9148d + 9abc03c8: 8 survived, 1 refuted).**
  - **Missed twin:** `Aura.cpp` `ParamTargetType` (FindFunctionsByClassParam), which calls itself
    WalkFunctions' mirror, kept the flat `UPROPERTY_OFFSET + 0x2C`. It now uses
    `UPropertySubclassStartFor`. `dll_core_test` UFUNCWALK asks `CountClassParams` over the same
    fakes: 4.15 was red first, 4.18 the control.
  - 🟡 **Lead, recorded and NOT changed:** a reviewer argues the CPN +8 does not apply on 4.11-4.17.
    RE-UE4SS ships no CasePreserving template below 4.27, so nothing measured settles it, and two
    derivations disagree:
    - the reviewer's: the delta stays 0x28;
    - a field-by-field layout: RepNotifyFunc grows by 4 and moves Offset_Internal, and FieldSize
      moves +8 absolute, so the delta is 0x24.

    `UBoolPropFieldSizeFor` has always applied +8, and its compound-miss pins encode that. It needs
    a 4.11-4.17 CPN build or template, not a guess.

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

##### ✅ `[A2-STRUCT-PREVIEW-BOOLMASK]` LOW — the shared struct preview ignores the bool bit mask (FIXED IN SOURCE 2026-09-11)

`Ubel.cpp:2015` → `PreviewScalarValue` (`Ubel.h:820`), which returns `p[0] != 0`. So every packed bool
in a byte previews the whole byte. `AActor::ReplicatedMovement`'s `bSimulatedPhysicSleep` and
`bRepPhysics` share a byte, and a physics actor previews both as `true`. The drill-down rows are
correct. `ReadStructArrayElements` (`:2728-2733`) applies the mask, so the same struct decodes
differently as a field than as an array element. The hand-copied `TOptional<struct>` preview (`:5933`)
repeats the defect, which contradicts "now the ONLY one" (`:1982`).
- ✅ **Safe fix:** use `mask != 0 ? (b & mask) != 0 : b != 0`, the array reader's fallback. Collect
  the FieldMask in `WalkFFieldChain`, because on UE5 `WalkClass` never collects it. Route the TOptional
  copy through the shared decoder.
- ⛔ **Unsafe:** a bare `(p[0] & mask) != 0`. Mask 0 means "unresolved", so every bool on a title
  where the probe misses (DQ XI S) would read `false`.
- ✅ **FIXED IN SOURCE 2026-09-11, the safe shape** (batch B05).
  - `PreviewScalarValue` takes the mask: `mask != 0 ? (b & mask) != 0 : b != 0`.
    `InterpretStructByLayout` passes it.
  - `WalkFFieldChain` now collects it on UE5 (`ProbeBoolLayout`).
  - The hand-copied `TOptional<struct>` preview is gone and routes through the shared decoder, so
    the claim "now the ONLY one" is true again.
  - `dll_helpers_test` pins the packed bit set and clear, mask 0 falling back to the byte, and
    native `0xFF`, plus `ClassifyBoolLayout`'s cases.
- ✅ **Review follow-up 2026-09-11:** the call site is now pinned too.
  - `dll_core_test` BOOLLAYOUT: two packed bits in one byte, plus a mask-0 field, previewed through
    `InterpretStructByLayout`.
  - Dropping the mask argument turns it red (mutation check).

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

##### ✅ `[A2-METHODE-MANUALMAP]` LOW — Methode reports a SUCCESSFUL CE force-load as "Injection failed", and blames CE's BOOL (FIXED IN SOURCE 2026-09-12)

`Methode.cpp:385-416`. From CE's source: `ForceLoadModule` re-raises on every failure
(`CEFuncProc.pas:768-811`), so `ce_InjectDLL` TRUE after `EInjectError` means the forced load
SUCCEEDED. That load is CE's manual PE mapper. It never links into the PEB, so the post-inject module
walk cannot see it, and the user reads *"Injection failed — the DLL is not mapped … CE's result cannot
be trusted"*.
- **Measured on `dist\UE5Dumper.dll`:** CE's mapper processes neither the TLS directory nor `.pdata`.
  So no SEH or C++ exception inside the image can be dispatched, and `ReadSafe`'s everyday `__except`
  makes a game crash the realistic outcome.
- Reachable through CE's own "Always force load modules" setting.
- ✅ **Safe fix:** word the TRUE-but-absent message as ambiguous, and name that setting. Optionally
  read HKCU `Always Force Load` before injecting and warn up front; that is the only point where the
  case is distinguishable. Correct the comment and `working-lessons.md:2408-2417`, but not the
  append-only dev-log.
- ⛔ **Unsafe:** saying "CE manual-mapped it" whenever TRUE and absent. The APC path and a
  `GetExitCodeThread` failure also return TRUE with nothing mapped. Do not try to support manual
  mapping.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L13).
  - The TRUE-but-absent case has its own message, which says it means one of two things: a failed load,
    or CE's manual map. It names Settings -> "Always force load modules" (the label and the HKCU value
    `Always Force Load` were read in CE's source), warns that a manually mapped image cannot handle
    exceptions, and says to untick the setting and inject again.
  - The FALSE case keeps the "Injection failed" text.
  - The comment and `working-lessons.md` are corrected; the dev-log is untouched.
  - Not done, as the row allows: reading `Always Force Load` before injecting.
  - **Test, red first:** a Methode.cpp source pin. 2/2 mutants killed; dll_core_test 304/304, dll_helpers_test 2716/2716; UI 5214/5214.
  - ⚠ **Survivor by construction:** the branch's wiring. No harness drives the plugin's inject path.

##### ⛔ REFUTED — do not re-raise

- **`A2-S1a-01`** "packed bools in a uint32 container never get a mask — c0b4e709's FieldSize==1
  tightening was a regression". **False premise**:
  - the vendored UHT turns EVERY bitfield container into `UhtBoolType.UInt8`
    (`UhtUInt32Property.cs:76-78` and its three siblings), so FieldSize is 1;
  - ByteOffset is always 0 (`DetermineBitfieldOffsetAndMask`);
  - live evidence agrees: `Wirbel::SetMouseCursor` writes `bShowMouseCursor` (a `uint32 :1`) through
    the `fieldSize == 1` probe, verified on TQ2 and DQIII HD-2D (ec016229).
  - ⛔ Its implied fix (accept 2/4/8) would reopen the false pointer-byte latch c0b4e709 closed.
- **`A2-D-2`** "a same-PID pipe holder is treated as go, so a second DLL image runs a full duplicate
  init". The facts are right, but the second image still **declines to serve**. `AlreadyOurs` creates
  no second server, so `IsOurModule`'s fail-open argument holds; only its "returns INIT_SKIPPED" clause
  is stale. The mailbox thread and the feature-state split pre-date the change. What is left is an
  eager, unrequested scan in a contrived two-image state.

##### Measured clean — worth as much as the rows

- **The four proxies forward every export, measured with `pefile` against this machine's System32**:
  - version 17/17, dinput8 6/6, dxgi 20/20, winmm 180/180;
  - 0 missing, 0 wrong ordinals (winmm's NONAME `@2` is deliberately unforwarded);
  - the tables, thunks and `.def` files agree;
  - the real DLL loads only via `SystemDllPath`, with truncation refused;
  - loader-lock rules hold per the dxgi appcompat audit;
  - the `.asm` thunks keep RSP aligned and preserve every argument register.
- **`Grimoire`'s `FunctionFlagsOffsetFor` / sweep and `UBoolPropFieldSizeFor` were re-derived from all
  31 RE-UE4SS templates:** every row matches. `Lineal` is byte-identical to the vendored
  `UObjectArray.h`. `Methode`'s plugin ABI matches `cepluginsdk.h`.
- **`Renge.h`:** all 100 command constants are referenced in `Fern` and present in the UI. No orphan.
- **Walker memory safety:**
  - no `catch(...)` or `__try` anywhere in `Ubel.cpp`;
  - every allocation and loop bound sized from game memory is clamped;
  - the `s_walkClassCache` LRU copies out under its mutex, and no band hunk adds an eviction to the
    reference-returning caches;
  - the blind-spot DLL fixes (`[D5-LAZYGUID]`, the multicast header reads, the sparse "(0 bindings)"
    claim) hold as promised.
- **`Sein`:** thread-safe; every failed open reroutes; retention is by age, stamped from each file's
  own mtime.
- **`Flamme`:** never trusts a cached pattern as truth; it re-validates every hint.

##### Leads, not filed (unmeasured, or near-misses the finders held back)

- **Layout:**
  - `Macht.h:408-410` claims TSet call sites "need no change". That is false for `alignof(T) >= 16`
    (a struct with FQuat / FTransform), which is strided 8 bytes short per element.
  - `WalkDataTableRows` (`:7050-7061`, out of band) reads a ByteProperty's enum at `FENUMPROP_ENUM`,
    not `FBYTEPROP_ENUM`, and has no 2-byte arm.
  - `ReadEnumRawValue` picks signedness from width, not from the underlying property.
- **Caches enriched before `CorrectSubclassOffsets` recalibrates are never invalidated,** so
  `objClassName` / `enumName` can stay stale for the process on a layout where +0x2C is wrong.
  Relatedly, `Ubel.cpp:5327-5328` claims the family write "is not a data race" while the readers never
  take the lock.
- **`[P1-ENUMNAMES]` widening:** when `bUEnumNamesFailed` is latched, `GetEnumEntries` warns "truncated
  read, retry pending", which is false. The warning is unthrottled: one line per enum field per walk.
- **`Sein` rotation:** a tailer that holds `-0.log` without delete-share makes the rename fail. The
  truncating reopen then silently destroys the previous 8 MB.
- **`Flamme`:** the scan thread and the pipe threads share `<cache>.tmp.<pid>` with no serialization.
- **`Dunste.SetEnabled(true)`** kills a pending collision restore before `ResolveCtx` can fail, and
  never re-arms it.
- **`Genau` Step 6.5:** can mark as measured an `FFIELD_NAME=0x28` that Step 5 had disproved. No known
  title reaches it.
- **Gate gap:** `check_all.py` does not run `gen_proxy_forwarders.py winmm --check`, though the winmm
  files are generated.
- **Stale comments:**
  - four places still say no test target compiles `Ubel.cpp` (`dll_core_test` has since 2026-08-25);
  - `VersionNeedleScan.h:257-260`;
  - `Heiter.cpp:464` vs `Grimoire.h:937`;
  - `GetCachedStructFields :2580-2587`.

⬜ **For the fix pass:**
- ✅ `[A2-UFUNC-TAIL-4X]` + the `WalkFunctions` +0x2C lead form one "UE 4.11-4.17 layout" change: a
  version-keyed constexpr pinned at the 4.17 / 4.18 boundary. (Done 2026-09-11, batch B07.)
- ✅ `[A2-TOPTIONAL-INTRUSIVE]` + the Find Refs twin + `technical-notes.md` land together. (Done 2026-09-11, batch B08.)
- `[A2-WALKCLASSEX-UNMAPPED]` + the `GetCachedStructFields` twin land together, with the `VirtualFree`
  test.
- `[A2-GNAMES-PTRSCAN-ABORT]` joins `[P1-GENAU-ABORT]`.
- `[A2-LAZY-LATCH-GUESS]` goes with the soft-path discriminator.
- `[A2-CRC-PATH-LS]` is worth a `%ls`-in-log gate so the next instance is caught mechanically.
  ✅ It has one (batch L11).

### ✅ A3 SWEPT 2026-09-10 `[TRACKB-A3-2026-09-10]` — the wire: 13 raised, 12 confirmed (4 MED · 8 LOW), 1 refuted

- **Scope: 7,409 never-audited lines in 60 files** — WIRE (`Fern`, `Frieren`, `Mimic`, `Stark`, `Tot`,
  the UI pipe client), CE-BRIDGE (proxy deploy, `ParamBufferBuilder`, the CE script generators) and
  WIRE-DTO (32 files, 25 never named by any audit).
- **Four finders:** one per cluster, plus **A3-X, a cross-transport lens** that held the pipe, the
  mailbox, the C ABI and the UI parser at once, which is the reason the plan put these in one wave.
- **Coverage reconciled:** 11/11, 11/11, 17/17, 32/32 files; no gap-fill.
- **Every lens hit its known positive:** `[P1-GENAU-ABORT]`'s latch guard, the FP1/FP2 pose residuals,
  the `[B30-REOPEN]` fix, `[W1-CONTAINER-STALE]` / `[P4-*]`.
- **Cost:** 12 agents, ~2.9M tokens.
- **Constraints:** source reads only, with no CE, game, UI or build, because CE was in use by another
  session. **Nothing fixed.** All four MEDs were re-read at their source by hand.

⭐⭐ **THREE OF THE FOUR MEDs BREAK THE FIX THEY SIT IN.** `[A3-B30-STALE-FLAG]` defeats
`[B30-REOPEN]`, fixed earlier TODAY. `[A3-ST1-SUPER-DRAIN]` defeats ST1's "our calls stop entering the
detour at all". `[A3-DEPLOY-CANCEL]` defeats AE20's "the token-taking commands carry a whole cancelled
reporting path". The fourth, `[A3-BOOL-NATIVE-NOWRITE]`, breaks the rule AA1 wrote down, and AA1 names
the very function that breaks it as its correct implementation. **Nine of the twelve are fix-pass
claims not kept**, the same shape as A1 (5/6) and A2 (7/9).

⭐ **The wire itself is measured clean where the plan feared it most.**
- **`Mimic.h` vs every mirror.** A3-X checked it against `CeMailboxLayout`, the generators' inline
  offsets and both Lua helpers, for **layout AND meaning: 0 mismatches**. That answers A1's lead: the C#
  side is still pinned only by `ContractVersion`, but today it agrees.
- **Fern reply keys vs `DumpService` parse sites, diffed mechanically:** 34 commands (A3-X) and the
  band DTOs (A3-D), **0 name or type mismatches**.
- **Reach re-derived at HEAD.** `Fern` reaches 374 module symbols, `Mimic` 43 and `Frieren` 140; 23 are
  reachable from all three and 89 from two or more. The in-band multi-exit functions were compared and
  **only `[A3-ST1-SUPER-DRAIN]` survived**. P3 on the transports is now bounded.

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

##### ✅ `[A3-B30-STALE-FLAG]` MED — the `[B30-REOPEN]` ownership flag survives a table reload, so "already serving" can still tear the pipe down (FIXED IN SOURCE 2026-09-12)

`CeInjectScriptGenerator.cs:264`, and identically `scripts/UE5CEDumper.CT:804`/`:832`.
`UE5_StartedByThisRecord` is ONE global in CE's Lua state, and that state lives for the whole CE
session.

1. Tick the inject record, which sets the flag.
2. File > Open, without merging. CE frees the records and **never runs `[DISABLE]`**; the refuter
   checked `OpenSave.pas`, `addresslist.pas` and `MemoryRecordUnit.pas`.
3. Tick the reloaded record. The "already loaded and serving" branch (`:168-176`) shows its message and
   defers an untick **without clearing the flag**.
4. The untick runs `[DISABLE]`, which passes the stale guard and calls `UE5_Shutdown`.

That is B30's exact symptom, on both shipped artifacts. The live before/after of the B30 fix ran in a
fresh CE session, where the flag was nil.

- ✅ **Safe fix, in BOTH artifacts:** set `UE5_StartedByThisRecord = false` in the SERVING branch
  before its deferred untick, or use a consume-once skip marker. Either one fails safe, leaving the
  pipe up. Pin the ordering in `CeInjectScriptGeneratorTests`.
- ⛔ **Unsafe:**
  - per-`memrec.ID` keying: CE reloads records with their saved IDs;
  - an owner token in the mailbox: a contract bump that makes every saved `.CT` refuse, for a problem
    Lua can solve;
  - fixing the generator only;
  - going back to the symbol probe, which was the original B30.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix, in BOTH artifacts** (batch B28).
  - The serving branch sets `UE5_StartedByThisRecord = false` before its deferred untick: in the
    generator's `[ENABLE]`, and in the `.CT`'s `ue5_inject`, before the `return false` that makes
    `[ENABLE]` untick.
  - The untick still runs `[DISABLE]`, which now refuses on the cleared flag and leaves the pipe up.
    That is the fail-safe direction.
  - ~~The stale-flag scenario always lands in the SERVING branch.~~ False, found by review 4 (below): the
    `.CT` bails out on a missing DLL BEFORE its serving check, and a process switch reaches the parked
    branch.
  - None of the unsafe fixes landed: no per-ID keying, no mailbox owner token (so no contract bump), and
    not the generator alone.
  - **Tests, red first:** one ordering pin per artifact. The flag must be cleared inside the serving
    branch, and before its untick (generator) or its `return false` (`.CT`). 2/2 mutants killed; UI 5134/5134.
- ✅ **Review 4 follow-up 2026-09-12** (of 691a797e: one MED, one LOW, both CONFIRMED). The serving-branch
  clear covered one route of three.
  - **MED, the `.CT`'s DLL-not-found bail-out.** It runs BEFORE the serving check. A cancelled file picker
    returned false with the previous table's flag still true. The deferred untick ran `[DISABLE]`, the guard
    passed, and `UE5_Shutdown` tore the serving pipe down: B30's exact symptom.
  - **LOW, a process switch.** CE unticks every record on a process switch WITHOUT running `[DISABLE]`
    (`MainUnit.pas`: `addresslist.disableAllWithoutExecute`, which only clears `factive`). The next tick can
    reach the PARKED branch, whose failed `UE5_AutoStart` defers an untick too, in both artifacts.
  - **The fix:** clear the flag at `[ENABLE]` ENTRY, before the first bail-out, in both artifacts. An enable
    runs only on an unticked record, and an unticked record owns nothing. The success path stays the one
    claim. The serving-branch clear is kept: now redundant, its pin still holds.
  - **Tests, red first:** the clear precedes the first bail-out, in the generator and in `ue5_inject`.
    2/2 mutants killed; UI 5156/5156.

##### ✅ `[A3-BOOL-NATIVE-NOWRITE]` MED — editing a native bool in Live Walker writes nothing and reports "Written" (FIXED IN SOURCE 2026-09-11)

`FieldValueConverter.cs:63` + `LiveWalkerViewModel.cs:5388-5392`/`:5418`. Hand-verified at source:
- The DLL publishes a bool mask only when FieldMask is a single bit (`Ubel.cpp:5427`).
- A native bool — every Blueprint bool, and plain `UPROPERTY() bool bFoo;` — has FieldMask `0xFF`, so
  no mask is sent, and `DumpService` parses it as 0.
- `ApplyBoolMask(cur, 0, v)` returns `cur` for both values. The editor writes back the byte it just
  read and prints `Written: bFoo = true`, and the row still reads false after the refresh.
- AA1 wrote the rule "absent mask = native bool = whole byte" and named `ApplyBoolMask` as a correct
  implementation of it. It is not.
- The same no-op hits map-value, array-element and struct-sub-field bool rows.
- ✅ **Safe fix:**
  1. The DLL recognises the native layout explicitly (`FieldSize==1 && FieldMask==0xFF`) and sends an
     ADDITIVE key, e.g. `bool_native:true`. That is pipe-only, like `bool_bit`, needs no contract bump,
     and leaves older UIs and CSX unaffected.
  2. The UI writes `0x01` / `0x00` only for a confirmed native bool, does the masked read-modify-write
     for a single-bit mask, and REFUSES honestly when the mask is unresolved.
  3. The UI reads the byte back and reports a mismatch instead of "Written".
- ⛔ **Unsafe:**
  - "treat mask 0 as the whole byte". Mask 0 also means "probe missed" (DQ XI S, where every packed
    bitfield reads as mask 0), so that turns a harmless no-op into the AA1 corruption of up to 7
    sibling bools;
  - stamping `0xFF` for true, which C++ that XORs a bool reads as still true.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (fix-pass batch B05, "bool mask, end
  to end", one commit with `[A3-FIRE-STRUCT-BOOLMASK]` and `[A2-STRUCT-PREVIEW-BOOLMASK]`).
  - **DLL:** `Ubel::ClassifyBoolLayout` names the native layout (`FieldSize 1, ByteOffset 0,
    ByteMask 0x01, FieldMask 0xFF`, i.e. SetBoolSize's `ByteMask = true; FieldMask = 255`).
    ⚠ The first version required ByteMask 0xFF, which no engine writes, and was green only
    because its test pinned the same tuple. The review follow-up below corrected it.
    - The field walk, the UE4 UProperty chain and `WalkClassEx`'s enrichment all record it.
    - Fern publishes the additive `bool_native: true`: pipe-only, kept in lean mode, no contract
      bump.
  - **UI:** `FieldValueConverter.PlanBoolWrite` decides the write.
    - A native bool writes `0x01` / `0x00`; a single-bit mask does a read-modify-write.
    - Anything else is REFUSED with a reason: mask 0 means a missed probe, never native, and a
      whole-byte write there is the AA1 corruption.
    - Every write is READ BACK, and a mismatch is reported instead of "Written".
    - The rows the VM builds itself for map values, array elements and set elements are native
      bools (UE containers of bool always are).
    - An older DLL, with no key, now refuses instead of silently no-opping.
  - **Tests:** `LiveWalkerBoolWriteTests`, red first (5 of 6) on the recorded mechanism:
    - native true and false, the unresolved refusal, the read-back mismatch, and container
      element rows;
    - the read-modify-write control, which was green before and after;
    - planner cases.
  - The DumpService parse of `bool_native` was also red first, then green.
- ✅ **Review follow-up 2026-09-11** (adversarial review of 68a404d6: 10 survived, 1 refuted).
  - ⭐ **The HIGH survivor, reported by three lenses: the fix did not work on any real game.**
    - `ClassifyBoolLayout` required ByteMask 0xFF for native. Every engine's `SetBoolSize` (the
      reviewers read PropertyBool.cpp for 4.11 → 5.8) writes `ByteMask = true; FieldMask = 255`,
      i.e. 0x01.
    - So NO bool classified native, and every native-bool edit was REFUSED.
    - It went green because the unit test pinned the same wrong tuple, and every UI test injected
      `BoolNative` directly.
    - **Fixed:** native = {1, 0, 0x01, 0xFF}, with ByteMask held strict so an all-0xFF read is not
      taken for one.
  - **The missing pins, which are why the defect could hide, now drive the real bytes:**
    - `dll_helpers_test`: the real tuple, with all-FF as Unresolved.
    - `dll_core_test` BOOLLAYOUT: `ProbeBoolLayout` fed SetBoolSize's bytes, plus
      `InterpretStructByLayout`'s per-field mask.
    - `dll_core_test` BOOLNATIVE: a pool-faking block that drives `WalkInstance`, Live Walker's
      own probe loop, for native, packed and unresolved.
    - Red first: helpers 2, core 3.
  - **UI pins:** the masked read-back in both directions, and map-value and set-element rows that
    are native and write the value byte. Green pins; the mutation check kills each.

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

##### ✅ `[A3-FIRE-STRUCT-BOOLMASK]` LOW — FIRE writes a packed-bool struct sub-field as a whole byte (FIXED IN SOURCE 2026-09-11)

`ParamBufferBuilder.cs:381`. This is the write-side twin of `[A2-STRUCT-PREVIEW-BOOLMASK]`, and the AA1
mask never reached it: no mask exists at any tier of the invoke wire.
- `FHitResult`'s `bBlockingHit` and `bStartPenetrating` share a byte. Typing one bit either zeroes its
  sibling or lands on bit 0, and the dialog still says OK.
- The Copy AA Script path (`ue5_invoke_helper.lua`) has the same defect.
- ✅ **Safe fix, only with ALL of:**
  - AA1's accept set: a single-bit mask gets the read-modify-write, and mask 0 / `0xFF` keep today's
    write;
  - collecting the mask in the UE5 FField walk first (A2);
  - an additive key that defaults to 0;
  - changing the Copy AA Script path in the SAME commit.
- ⛔ **Unsafe:** deduping rows by offset, or refusing such structs.
- ✅ **FIXED IN SOURCE 2026-09-11, with ALL four recorded conditions** (batch B05, one commit).
  - **Accept set:** a single-bit mask gets the read-modify-write; mask 0 / `0xFF` keep today's
    whole-byte write.
  - **Mask collection:** the mask is collected in the UE5 FField walk (`WalkFFieldChain` now probes
    it). The invoke params' `struct_fields` carry it on the additive key `bool_mask`, which
    defaults to 0.
  - **Copy AA Script, in the SAME commit:**
    - `BakedParamValue.BoolFieldMask` carries the mask, and `CollectBakedValues` flattens it into
      the row;
    - the generator emits `mask=0xNN`;
    - `ue5_invoke_helper.lua`'s `bool` arm read-modify-writes that bit with CE's `readBytes`.
  - **Rows are not deduped by offset, and such structs are not refused.**
  - **Red → green:** `InvokeBoolMaskTests` (two packed bits sharing a byte; clearing only its own
    bit; the baked row's mask; the dialog's flattening, pinned from the source) and
    `scripts/tests/invoke_helper_test.lua`'s three new cases (2 red first, 97/97 now). The
    whole-byte controls were green both ways.
- ✅ **Review follow-up 2026-09-11, the READ side.**
  - The mask reached the UI, but FIRE's post-call readout and the struct-return grid still decoded
    a packed bool as its whole byte.
  - Both now go through `InvokeParamDialog.DecodeStructSubField`: a single-bit mask reads its own
    bit, and mask 0 / 0xFF keep the byte, like `PreviewScalarValue`.
  - Tests: `InvokeParamDialogTests` / `StructReturnDecoderTests` `*_PackedBools_ReadTheirOwnBit`,
    red first.
  - ✅ **Review follow-up 2026-09-11 (adversarial review of d8a7f44f + d5e9148d + 9abc03c8: 8 survived, 1 refuted):** those cases sat at buffer offset 0, so reading `buf[sf.Offset]` instead of the
    absolute offset survived. `*_PackedBools_AtANonZeroOffset` pins the absolute offset in both
    decoders; they are green pins, killed by a mutant.

##### ✅ `[A3-CEFORM-4X-STALESLAB]` LOW — an ADDENDUM to `[A2-UFUNC-TAIL-4X]`, correcting that row (FIXED IN SOURCE 2026-09-11)

`InvokeScriptGenerator.cs:396`/`:420`. The A2 row said the CE mailbox path "only reports a wrong
`parmsSize`". But the CE invoke form bakes that wrong `parmsSize` as its zero-fill span, and `Mimic`
runs ProcessEvent **on the persistent 1024-byte slab**, which LIST_INSTANCES (every Freeze rescan), the
pose handlers and the pointer query all dirty.
- So on 4.11-4.17, struct and out-FString slots past NumParms carry the previous command's bytes.
- An out-FString assignment then frees a stale pointer inside the game.

A2's root fix cures it. The hardening is safe only as `max(PARMS_SIZE, max(Offset+Size))` clamped to
1024, never the walked size alone.

- 🟡 **Unfiled lead:** the CE form has **no 1024 clamp on any version**, and `Mimic` never refuses
  `ParmsSize > 1024` on the DLL side.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded hardening** (batch B07).
  - The CE form's zero-fill span is now `InvokeScriptGenerator.ZeroFillSpan`:
    `max(ParmsSize, max(Offset+Size))` over every param, the return slot INCLUDED.
  - It is clamped to `CeMailboxLayout.ParamsDataBytes`, 1024. That is Mimic.h's
    `paramsData[1024]`, read back by `InvokeScriptTests.ParamsDataBytes_MatchesMimicH`.
  - It is never the walked size alone.
  - Both call sites use it. That includes the direct no-input path: a return FString slot is the
    same stale-free hazard.
  - `PARMS_SIZE` still reports the DLL's number.
  - **Tests:** `InvokeScriptTests.ZeroFill_*`.
    - Red first (3): a wrong ParmsSize, the direct path's return slot, and the clamp.
    - The right-ParmsSize control was green both ways.
  - The 🟡 lead above is half closed. The CE form now clamps to the slab on every version; the
    DLL side still never refuses `ParmsSize > 1024`, which stays a lead, not filed.
  - ✅ **Review follow-up 2026-09-11 (adversarial review of d8a7f44f + d5e9148d + 9abc03c8: 8 survived, 1 refuted).**
    - **The clamp covered only the zero-fill.** A param at or past +1024 was still WRITTEN past the
      slab, and the call fired. `InvokeScriptGenerator` now computes the unclamped `RequiredSpan`
      and, past the slab, emits a refusal before the first round-trip: it says so and unticks.
      `ParamsPastTheSlab_*` was red first, and `ParamsInsideTheSlab_*` is the control.
    - **The DEBUG return decode was bounded by ParmsSize only**, which is 0 when unknown. It is now
      bounded by the slab in both the form and Copy AA Script (`BakedScript_DebugReturnPrint_*`).
      ⚠ That test was added with the fix, not before it, so it was never observed red; the
      mutation check is what shows it bites.

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

##### ✅ `[A3-PTR-NAV-REPAINT]` LOW — a pointer that gains a target after refresh shows its name but no → button (FIXED IN SOURCE 2026-09-11)

`LiveFieldValue.cs:158-161`. `_ptrAddress` notifies `DisplayValue` / `ValueTooltip` / `EditableValue`,
but not `IsPointerNavigation`. That property drives the → button and the Ptr copy column. A pointer that
was null at the first walk therefore repaints its name after an auto-refresh, and cannot be drilled.
This is a P4-CONTAINER-BASE twin that P8 could not see, because `PtrAddress` IS observable.
- ✅ **Safe fix:** add the `[NotifyPropertyChangedFor]` attributes. Land them in the same-object
  staleness bundle, next to `[P4-CONTAINER-BASE]`'s `IsContainerNavigable` notifications.
- ✅ **FIXED IN SOURCE 2026-09-11, the safe shape, as the bundle's follow-up.**
  - ⚠ **It should have landed IN the bundle** (aa71e997), as this row and the A3 fix-pass notes say.
    The fix-pass inventory's completeness critic caught the miss.
  - **How it was missed:** the implementer's check for a bound navigability flag grepped for
    `IsNavigable`, which is unbound. The bound flags are `IsPointerNavigation` and
    `IsStructNavigation`.
  - **The fix:** `_ptrAddress` now raises both, plus `IsNavigable`.
  - **Red → green:** `Refresh_PointerGainsATarget_ShowsTheDrillButton` failed first (no
    `IsPointerNavigation` notification), then passed.

##### ✅ `[A3-RECYCLE-GUID-FAILOPEN]` LOW — `RecycleBinPolicy` says a failed volume-GUID lookup fails closed; it fails open (FIXED IN SOURCE 2026-09-12)

`RecycleBinPolicy.cs:75`/`:78-85`. On a failed lookup the per-volume value is null. `IsDisabled` tests
`== 1`, so null reads as "bin enabled", and the verdict rests on `SHQueryRecycleBin` alone, which the
policy's own header measured as blind to NukeOnDelete. A leftover proxy DLL could then be hard-deleted
while the UI says "moved to the Recycle Bin", which is the B13/B41 false claim. `ReadDword`'s doc
("Null is load-bearing") is false too.
- **Measured on this machine:** no fixed volume here fails the lookup. The exposure is SUBST and
  RAM-disk-style volumes, and that path is unmeasured.
- ✅ **Safe fix:** refuse only when the lookup failed AND the per-volume flag would decide: no
  NoRecycleFiles policy, and `UseGlobalSettings != 1`.
- ⛔ **Unsafe:** refusing unconditionally, or changing null semantics inside `IsDisabled`.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L38), the recorded safe fix. The failed lookup travels as its own fact,
  `volumeLookupFailed`; null semantics are untouched.
  - `IsDisabled` refuses on it only where the per-volume flag would decide: no policy, and not `UseGlobalSettings`.
  - `WindowsPlatformService` passes `volumeLookupFailed: guid.Length == 0`.
  - The two false docs are corrected: `VolumeGuidFromVolumeName`'s and `ReadDword`'s "null is load-bearing".
  - **Red first:** a failed lookup fails closed where the volume flag decides; under the global setting or a policy it
    changes nothing (control); a source pin checks the caller.

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

##### ⛔ REFUTED — do not re-raise

- **`A3-W-START-RESURRECT`** — "a shutdown landing mid-auto-start is undone: the pipe restarts".
  - The mechanics are right, but the end state is FR1's **recorded, chosen option**: "keep serving but
    re-arm the mailbox poller"; "an aborted scan publishes INIT_FAILED" (audit-2026-08-13:552-562,
    commit cfaa5cdb).
  - It is only reachable through the autorun menu's Shutdown after a >25 s wedged scan.
  - ⛔ Do NOT remove `Tot::ResetShutdown` from `Fern::Start`, and do not gate `:890` without the
    maintainer reversing FR1.
  - The one safe piece: re-test `Tot::ShutdownRequested()` after the claim watcher's sleep.

##### Leads, not filed (unverified by a refuter unless stated)

- **Pipe-claim watcher never re-arms:** `s_claimWatcherStarted` (`Frieren.cpp:2298`) is never reset.
  After a Disable/re-enable, a deferring `UE5_StartPipeServer` never claims the pipe when the other
  holder exits. That is RELAUNCHPIPE's permanent deferral again.
- **Two-lane stall banner can stick ON:** `RaiseStalledIfChanged` fires outside its lock, and the
  consumer posts the event ARGUMENT. That is X7's symptom via a microsecond race.
- **The router's reconnect-both teardown** cancels the SURVIVING lane's requests with `TrySetCanceled`,
  so a bare OCE catch reads a game exit as a user cancel (AC10's promise, on that lane).
- **`Fern::RunScan` ignores `UE5_Init`'s return** and publishes `scanned:true` over an aborted scan: a
  P3 twin of FR1/D5, reachable only via the unbound RunScan thread.
- **`Mimic::StopThread` runs before `Stark::Shutdown`.** A CE invoke pending on a paused game adds
  3 s to Disable and repopulates the mailbox.
- **Invoke error texts:**
  - `-3` renders as "(ProcessEvent offset not found)", but also means "distrusted";
  - `DeferAndWatch` returns INIT_READY where `Mimic.h` defines INIT_SKIPPED (log wording only).
- **`get_ce_pointer_info`'s `flat_layout` is never parsed, and the call has no production caller:**
  UNDECIDED over an empty host.
- **CE generators:**
  - `FreezeScriptGenerator`'s `FREEZE_KEY` is shared by two freeze records on the same field, so
    enabling the second silently stops the first;
  - TimeDilation's `[DISABLE]` uses SilentReturn, so a busy mailbox leaves the cheat running with the
    record unticked;
  - several generators hand-roll closes or unticks (B15-class exposure against the MUST rule; they
    behave the same today).
- **`RecycleBinPolicy` leads:** whether Explorer still honours `UseGlobalSettings`; `MoveToRecycleBin`
  lacks `FOF_WANTNUKEWARNING`, so a file larger than the bin is nuked with rc=0.
- **`ContainerGeometry.SetStrideOf`** repeats A2's TSet `alignof >= 16` claim, so the UI inherits the
  DLL's under-stride. Fix the two together.
- **`AddressHelper.TryNormalizeAddress`** (out of band) treats a hex-letter base as a module name and
  never compares that name to the game module. Its consumers only read.
- **Latent:** `list_enums` / `pe_profile_get` emit `truncated` and nothing reads it. The harm is
  unreachable while handlers stay connection-bound.
- **Stale comments:**
  - four sites still say `g_perCommand` clears only at the first accept;
  - `Frieren.cpp:3` "~30 C ABI exports";
  - `ConnectWithRetryAsync`'s give-up line renders green.

⬜ **For the fix pass:**
- `[A3-BOOL-NATIVE-NOWRITE]`, `[A3-FIRE-STRUCT-BOOLMASK]` and `[A2-STRUCT-PREVIEW-BOOLMASK]` form one
  **"bool mask, end to end"** change: collect the mask on UE5, add `bool_native`, and fix every write
  and preview path in one commit.
- `[A3-B30-STALE-FLAG]` lands in both artifacts together, and its live check needs CE (**ask first**).
- `[A3-ST1-SUPER-DRAIN]` needs a live check, which also needs CE (**ask first**).
- `[A3-DEPLOY-CANCEL]` and `[A3-RADIO-MIDDEPLOY]` are one Proxy Deploy change.
- `[A3-PTR-NAV-REPAINT]` joins the same-object staleness bundle.
- `[A3-CEFORM-4X-STALESLAB]` rides on `[A2-UFUNC-TAIL-4X]`.

### ✅ A4 SWEPT 2026-09-10 `[TRACKB-A4-2026-09-10]` — the feature surfaces: 18 raised, 14 confirmed (4 MED · 10 LOW), 4 refuted — TRACK B IS CLOSED

- **Scope: 10,346 never-audited lines in 81 files, across seven clusters:** AURA-GRAPH, SNAPSHOT,
  PIVOT-SPC, VALUESEARCH, LIVEWALKER, OBJTREE, EXPORT and TELEPORT.
- **Five finders, not the plan's four:**
  - A4-1: Aura + Snapshot + Pivot/SPC
  - A4-2: Value Search
  - A4-3: Live Walker
  - A4-4: the browse panels + the exporters
  - A4-5: Teleport + the gameplay modules
- **Coverage reconciled:** 21/21, 7/7, 7/7, 29/29 and 17/17 files; no gap-fill.
- **Every lens hit its known positive:** `[P1-SPARSEDELEGATE-REFS]`, `[P5-GROUP-ADVICE]`,
  `[A3-CONTAINER-4096-ADVICE]` + the LWREFRESH loop, `[P3-SDK-GUESSED]` and `[P1-SEETHRU-GIVEUP]`.
- **Cost:** 15 agents, ~3.7M tokens.
- **Constraints:** source reads only, with no CE, game, UI or build. **Nothing fixed.** All four
  MEDs were re-read at their source by hand.
- **Evidence beyond reading:**
  - decompiled Avalonia 12.1.x (DataGrid and Base);
  - fetched CUE4Parse's enum readers;
  - vendored UE 5.8 unversioned serialization, Dumper-7 and RE-UE4SS;
  - Python models of the CDOSCOPE preview loop, the AB4 verdict chain and the export anchor/clean order.

⭐⭐ **THIRTEEN OF THE FOURTEEN ARE FIX-PASS CLAIMS NOT KEPT.** Each fix is named with its tag in the
rows below; they are CDOSCOPE, AB4, V4, e88190ba, e0dec505, X5, CEPATHS, audit #5 W1, Z10,
D4B-DELEGATEPAD, BADGEPRIME and AF5. Across Track B the count is **34 of 41**. See the close-out at the
end of this section.

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

##### ✅ `[A4-EDIT-STALE-PENDING]` MED — reopening an edited cell and closing it without typing writes the PREVIOUS edit into the game again (FIXED IN SOURCE 2026-09-11)

`LiveFieldValue.cs:578-583` + `LiveWalkerPanel.axaml.cs:448-459`. `_editableValue` is set only by the
editor's TwoWay binding and is never reset. Since LWREFRESH the row object survives the post-commit
refresh, so the last typed text survives with it. The refuter decompiled Avalonia to confirm two
things:
- **Opening the editor does not overwrite it:** `BindingExpression.StartCore` publishes to the
  target before subscribing.
- **Committing does not push the TextBox either:** the template column's `CellEditBinding` is null.

**Scenario:** type 250 into Health and commit; the game drops Health to 57; double-click the cell and
press Enter without typing. 250 is written again, with *"Written: Health = 250"*. An older variant
needs no refresh at all: Escape, then reopen and Enter. Under `[P4-OTHER-INSTANCE]`, instance A's text
is written into instance B. *(That cross-instance variant is closed by the `[P4-OTHER-INSTANCE]`
gate, 2026-09-10: a different object now gets fresh rows. The same-object variants stand.)*
- ✅ **Safe fix:** reset the pending value when an edit BEGINS. `FieldGrid_BeginningEdit` calls a
  `LiveFieldValue.ResetPendingEdit()` after its `!IsEditable` check. This also closes the Escape
  variant.
- ⛔ **Unsafe:**
  - resetting in `CopyLiveValuesFrom` only: it misses the Escape variant, and a mid-edit refresh
    would drop typed text;
  - going back to row replacement;
  - comparing against the current value, which silently drops a deliberate re-type.
- 🟡 **UNDECIDED, same loop:** the in-place branch clears the `IsEditing` latch unconditionally
  (`:6562`) while an editor may now survive the copy.
- ✅ **FIXED IN SOURCE 2026-09-11, the recorded safe shape** (fix-pass batch B02).
  - **The fix:** `LiveFieldValue.ResetPendingEdit()`, called by `FieldGrid_BeginningEdit` right after
    the non-editable veto. This also closes the Escape variant.
  - **Deliberately kept:** the refresh's copy does not reset the pending text, and there is no
    "same as current" comparison. The first is pinned on `CopyLiveValuesFrom`. The second is
    pinned on the model AND, since the review follow-up, in `FieldGrid_CellEditEnded`'s commit
    decision, where the unsafe comparison would naturally go.
  - **The batch's adversarial review:** 3 survived, all LOW, all about the tests and comments; 2
    refuted. It was repaired in a follow-up commit, and the production code is unchanged.
    - **The hook pin was weak:** it compared raw substring positions, so it stayed green with the
      call moved INTO the veto block or commented out. It now reads code lines only (the
      `CodeOnly` rule) and requires the call after the veto block's end.
    - **A new pin reads `FieldGrid_CellEditEnded`:** the commit is exactly "a non-empty pending
      value", and nothing current is read.
    - **Comments corrected:** the pending text is also written when the bool ComboBox is picked, not
      only when the user types; and `GetPendingEditValue` does not "fall back to the getter".
    - **Mutation-checked:** 4 mutants (the call moved into the veto, the call commented out, the
      guard removed, a comparison added) were all killed, and each file was restored by sha256.
  - **Tests:** the code-behind hook cannot be driven by a VM test, so it is pinned by reading the
    code-behind back (the `ClassListCapTests` pattern). That pin failed first ("must call
    `ResetPendingEdit()`"), then passed. The semantics are pinned on `LiveFieldValue`: a reset
    empties the pending text, the editor still opens on the current value, and a deliberate
    re-type of the current value is still committed.
  - 🟡 **Still undecided:** the `IsEditing` latch above is not part of this fix, and still needs
    its live experiment.

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

##### ✅ `[A4-USMAP-CONTAINER-ENUM]` MED — a container's TEnumAsByte inner is exported to USMAP as a bare ByteProperty (filed 2026-09-12 by review 3; FIXED IN SOURCE 2026-09-12)

`UsmapExportService.cs` `WriteInnerPropertyTypeFromField` + `Ubel.cpp:1298-1318`. B24's Arm 3 writes a
TEnumAsByte as the canonical `[26][0][enumName]`, but only at the top level. A `TArray` / `TSet` / `TMap` /
`TOptional` whose inner is a TEnumAsByte still goes out as `[8][0]`. The walker's container branches set the
inner's type, struct and object class, never its enum: nothing named `inner_enum` exists in `dll/` or `ui/`.
- **Why it matters:** in the vendored UE 5.8, an array of an enum-carrying byte cannot bulk-serialize
  (`PropertyArray.cpp:121-126`), and each element serializes BY NAME (`PropertyByte.cpp:66-112`). A
  consumer handed a plain 1-byte ByteProperty inner reads 1 byte against an 8-byte FName and misaligns.
  Dumper-7 (`MappingGenerator.cpp:195-213`, the `:226` recursion) and RE-UE4SS (`Generator.cpp:90-92`,
  `:256-263`) both emit `[8][26][0][E]`.
- B24's header called a container enum desync "doubtful". That holds for an FEnumProperty inner, which we
  already write as `[26]`. For a TEnumAsByte inner the same FName fact makes it certain.
- ✅ **Fix shape:**
  - the walker reads the container inner's `FBYTEPROP_ENUM` (Array / Optional, Set, and the Map key and
    value) and publishes it as a new additive key, e.g. `inner_enum`;
  - `WriteInnerPropertyTypeFromField` gains the Arm 3 branch.

  A UI-only fix cannot work, because the enum name is not on the wire.
- By comparison the top-level Arm 3 is display-only: a top-level TEnumAsByte serializes as an integer.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded fix shape** (batch B32).
  - **DLL.** `WalkClassEx` reads each container inner's own UEnum, with the same validation as the field's
    own `enumName`: `FBYTEPROP_ENUM` for a TEnumAsByte, `FENUMPROP_ENUM` for an EnumProperty. It stores it
    in four new `FieldInfo` fields: `innerEnumName` (Array / Optional), `elemEnumName` (Set), and
    `keyEnumName` / `valueEnumName` (Map).
  - Fern publishes them as `inner_enum` / `elem_enum` / `key_enum` / `value_enum`. Additive keys, and the
    CE mailbox is untouched.
  - **UI.**
    - `FieldInfoModel` carries the four enum names.
    - `WriteInnerPropertyTypeFromField` gains the Arm 3 branch, so a TEnumAsByte inner writes `[26][0][E]`.
    - Each caller now passes the inner's OWN enum: arrays used to pass the field's always-empty
      `EnumName`, and sets and maps passed `""`. An EnumProperty inner therefore carries its real name
      instead of "None".
    - `RegisterPropertyNames` registers all four names, and the band header records the fix.
  - **Tests, red first:**
    - a pool-faking `CONTAINERENUM` block in dll_core_test (an Array's TEnumAsByte inner, a Set's
      EnumProperty element, a Map's TEnumAsByte key);
    - the byte-exact USMAP shapes for an array and a map;
    - the parse.

    A plain byte array, and a Map value with no enum, are the controls. 6/6 mutants killed; dll_core_test 243/243; UI 5148/5148.
  - ⚠ **Survivor by construction:** Fern.cpp's four keys, which no test target compiles.
  - ~~⬜ The offline Dump All JSONL lacks the inner enums, so a USMAP built from an offline dump lacks them.~~
    False (review 5): no USMAP is ever built from an offline dump. `GenerateUsmapAsync` reads live `walk_class`
    replies only, so extending the JSONL would change no USMAP output.
  - ✅ **Review 5 follow-up 2026-09-12** (of 53baca47: three LOW, one refuted by one skeptic).
    - **The Set and Optional arms were unpinned.** Reverting either to its exact pre-fix code passed every
      test. Byte-exact tests for both now. 2/2 mutants killed; UI 5195/5195.
    - ⚠ **Still unpinned, and said so:** the DLL's Map-value and Optional-inner enum reads. The CONTAINERENUM
      fixture has an IntProperty value and no OptionalProperty; a later fixture should add both.
    - **L39's example was wrong.** No collision component declares a `TArray<TEnumAsByte<E>>`; it now names one
      that exists.

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

##### ✅ `[A4-AB4-BETWEEN]` LOW — `Between` still drops a width either bound cannot encode, in both group matchers and the single-value scan (FIXED IN SOURCE 2026-09-12)

Filed 2026-09-11 (batch B13), to close `[A4-AB4-UINT64]`'s records gap. `Radar.h`'s
`BuildNumericTargets` comment and `working-lessons.md` §5 cause 3 both say "see todo.md" for it, and
until now it was not here.
- **Shape:**
  - `Between -5 10` has no unsigned encoding for its lower bound, so every UInt16/UInt32 field
    holding 0..10 is skipped.
  - `Between 10 70000` has no Int16 encoding for its upper bound, so every Int16 field ≥ 10 is
    skipped.
  - Nothing says so.
- **Where:**
  - The two bounds are built by independent `BuildNumericTargets` calls at four `Fern.cpp` sites:
    single-value first scan and refine, group first scan and refine.
  - Every consumer requires BOTH bounds to encode: `Orden.h` and `Aura.cpp`'s scans and refines.
  - The snapshot mirror does the same in `GroupMatch.LeafSatisfiesSlot`'s Between arm, which calls
    `TargetFitsWidth` on both bounds.
- ✅ **Probably safe, unlike Smaller/Bigger:** clamp each bound into the width's range.
  - Between is INCLUSIVE, so a clamped bound loses no row: every uint16 ≥ -5 is every uint16 ≥ 0.
  - Skip the width only when the range misses it entirely.
  - It needs the two bounds built jointly, with reversed bounds normalised first.
  - ⛔ Fix both matchers or neither (`snapshot-group-match-spec.md` §9).
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L42), both matchers.
  - **DLL.** `Radar::BuildNumericBetweenTargets` builds the two bounds jointly.
    - Reversed bounds are normalised first.
    - Each bound is clamped into every integer width's range, on exact int64 / uint64 readings, with ±∞ for a float
      bound beyond both. A width the range misses entirely gets no entry.
    - It emits only `Encoded` entries, never `AlwaysTrue`, because `ComparePredicate`'s entry overload would accept a
      verdict without reading the upper bound.
    - It shares `BuildNumericTargets`' parse, moved verbatim into `ParseNumericBound`.
    - All four Fern sites call it, after their per-bound checks, which keep their messages.
    - The consumers are unchanged: they already take an `Encoded` entry from both sets.
  - **C#.** `GroupMatch.LeafSatisfiesSlot`'s Between arm asks whether the range overlaps the width (`BetweenOverlapsWidth`)
    instead of `TargetFitsWidth` on each bound. `BetweenMatch` already compares doubles.
  - **Red first:** dll_helpers_test's BETWEEN block. It covers -5..10 on unsigned, 10..70000 on Int16, reversed bounds,
    a range that misses a width, 64-bit exactness, a float bound beyond 64 bits, and only-`Encoded`. The same rows are in
    GroupMatchTests, and a source pin covers the four Fern sites.
  - ⬜ **Lead, unverified (found while mapping this row):** `Aura.cpp` `AppendRawHoleLeaves` prefilters with
    `sp.targets.Find(w)`, which hides a Smaller/Bigger `AlwaysTrue` entry. Native-C raw-hole group leaves may therefore
    still lose the verdict that `[W2-ORDEN-FINDENTRY]` restored elsewhere.

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

##### ✅ `[A4-DELEGATE-ARRAY-PAD]` LOW — CE XML / CSX element leaves of a `TArray<FScriptDelegate>` read the checked-build access detector (FIXED IN SOURCE 2026-09-12)

`CeXmlExportService.cs:2890-2912`, `:4133-4140`, and CSX `ConvertArrayPointerElementsToFields :706`. The
band comment says the field projection folds `DelegatePad`, "so nothing here needs to know". That is
true for FIELDS and false for ELEMENTS. The DLL's sixth D4B site states the contrary for exactly this
type.
- ⛔ **Unsafe:**
  - "add `field.DelegatePad`", which is 0 on an ArrayProperty;
  - setting the pad on the ARRAY field, which moves the group header off `Data`;
  - gating on `ArrayInnerType == "DelegateProperty"` alone, which breaks the correct multicast path.
- ✅ **Safe fix:** a DLL-sent per-element pad, applied only when `TypeName == ArrayProperty`, to the
  live loop, the fabricate tail and CSX.
- ✅ **FIXED IN SOURCE 2026-09-12, the recorded safe fix** (batch L03b).
  - **DLL.** Both walk modes publish `arrayElemDelegatePad` for every `TArray<FScriptDelegate>`, through
    `Ubel::DelegateArrayElemPad`.
    - It is the same `DelegatePadFromElementSize(elemSize, 8 + SizeofFName())` the reader's stride uses,
      clamped to 0.
    - It is published even when no element was read: an empty array still gets fabricated rows.
    - It is never folded into `delegatePad`.
    - Fern sends it as `array_elem_delegate_pad`: additive, and only when non-zero.
  - **UI.** `LiveFieldValue.ArrayElemDelegatePad` is applied only when `TypeName == ArrayProperty`, in
    three places: CE XML's element leaves, its fabricated tail (both through `ElemDelegatePad`), and CSX's
    `ConvertArrayPointerElementsToFields`.
    - The array group header stays on the field offset.
    - The CE XML struct-flatten projection copies the new pad.
    - The band comment's "nothing here needs to know" now says "for a FIELD".
  - **Tests, red first:**
    - dll_core_test: the helper at 24 / 16 / 20 and under CasePreservingName, and a fake walk over a 24-byte
      and a 16-byte delegate array;
    - the parse;
    - CE XML leaves, the fabricated tail, and a multicast control;
    - CSX leaves and a multicast control.

    10/10 mutants killed; dll_core_test 275/275; UI 5169/5169.
  - ⚠ **Survivors by construction:** Fern's key (no test target compiles Fern.cpp), the UProperty-mode walk
    site (no UProperty fixture), and the struct-flatten copy (no nested fixture).

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

##### ⛔ REFUTED — do not re-raise

- **`A4-2-2`** "the All-fields token is global, so slot B's click drops slot A's list". The command is
  a default `AsyncRelayCommand`, which disables every bound button while one fetch is pending, so the
  second click cannot happen. The token's justifying comment is inaccurate; correct it.
- **`A4-4-02`** "ClassStruct / ObjectTree ClearOnDisconnect leaves IsLoading stuck". The in-flight
  load's failure continuation is Posted BEFORE the reset, and at higher dispatcher priority (Normal vs
  Default, from decompiled Avalonia.Base). So the ticketed `finally` clears the flag first. Lead,
  unmeasured: ClassStruct's `ClearOnDisconnect` never calls `ClearError`.
- **`A4-5-2`** "ClearAll's busy bail still clears the markers". The mechanism is RECORDED (the "Clear
  all markers can raise three dialogs" row, d515ec19, byte-identical code). **But its recorded fix is
  insufficient**: a flag at the top of the slot loop cannot stop slot 0's `:266` write after the modal.
  The safe repair is `if hadError then break end` right after each wait. That row is amended in place.
- **`A4-5-3`** "TeleportRelative's landing publishes a parent-relative pose unflagged". `pattern_p3.py
  outparams` at HEAD lists exactly these sites, and `[PATTERN-P3-2026-09-10]` ruled them
  ALREADY-RECORDED under `[POSEATTACH]`.

##### Leads, not filed

- **Float Exact twin:** live Value Search matches `513.36` against a float `513.36f`; Snapshot Group
  / SPC never do. `SnapshotNumeric.ExactMatch` compares the float against the user's double.
  Unmeasured.
- **Value Search on a two-lane disconnect:** a game crash mid-scan may read "First Scan cancelled."
  (AE23-shaped). The experiment is written in the result.
- **Group refine:** it re-targets slots in place before knowing every slot parses (`Fern.cpp:3674-3707`).
- **Live Walker refresh inside a container view** shrinks a full-length scalar array back to
  ArrayLimit rows. `[P4-CONTAINER-BASE]` calls that branch correct only below the limit.
- **Solide SOLIDE-REFUSAL:** a refused re-arm has already changed the job's value. Unreachable from the
  UI today.
- **Laufen's B22 refusal** lands on the "no pawn" message, widening `[W2-MS-PROMISE]`. (✅ Covered by B21: that message no longer promises anything.)
- **`UsmapExportService.GenerateUsmapAsync`** drops classes whose walk throws, without counting them
  (P1).
- **Interesting Functions / Properties / Console / Property Search `ClearOnDisconnect`** do not
  supersede an in-flight Task.Run scoring pass. The window is sub-second.
- **Serie off-by-one** at `maxChunks`. Effect near nil.
- **A third `[A2-TOPTIONAL-INTRUSIVE]` twin** in Value Search's V1c gate. String scans only.
- **Stale comments** say no test compiles `Aura.cpp`.

---

#### ⭐⭐ TRACK B CLOSE-OUT — the >2026-08-03 blank is swept

| wave | lines | finders | raised | confirmed | refuted | fix-pass claims not kept |
|---|---:|---:|---:|---|---:|---:|
| A1 APP-SHELL | 6,695 | 3 | 7 | 6 (1M 5L) | 1 | 5 / 6 |
| A2 DLL core | 7,334 | 4 | 11 | 9 (2M 7L) | 2 | 7 / 9 |
| A3 the wire | 7,409 | 4 | 13 | 12 (4M 8L) | 1 | 9 / 12 |
| A4 feature surfaces | 10,346 | 5 | 18 | 14 (4M 10L) | 4 | 13 / 14 |
| **Total** | **31,784** | **16** | **49** | **41 (0H 11M 30L)** | **8** | **34 / 41** |

- **Coverage:** every assigned file in every lens was opened (the reconciliation never needed a
  gap-fill), and every known positive was reported.
- ⭐ **The finding of the whole track: 34 of the 41 are repairs that state a claim they do not keep.**
  The claim sits in a comment, a commit body, a doc row, or a test that pins it. The recurring
  mechanism is a fix that reached some of its twins and not the others: the third producer, the third
  handler, the other exit, the sibling panel, the batch form of a per-row button. The June band was
  new code, and its defects were mostly P1 / P3. This band is FIX code, and its defects are mostly P9.
  **A fix pass is not evidence; the claim it writes down is a new thing to verify.**
- **The single fix pass comes next, LAST and grouped by shape.** It covers the June blank, Track A and
  Track B. ⚠ **Live confirmations that need Cheat Engine wait for the maintainer's go-ahead** (another
  session was using CE throughout Track B):
  - `[A3-ST1-SUPER-DRAIN]`, `[A3-B30-STALE-FLAG]`;
  - the USMAP asset-parse step;
  - the UNDECIDED `IsEditing` latch;
  - each row's written experiment.

### 🔧 FIX PASS `[FIXPASS-2026-09-10]` — one commit per row, HIGH → MED → LOW; live checks DEFERRED to the end

**The maintainer's direction, 2026-09-10:**
- Fix one row at a time, HIGH first.
- **Verify everything live together at the END.**
- Every fixed row adds its live check to the backlog below, and nothing here is closed until its
  check passes on a running game.

⚠ **Before the live round:**
- A full `build.ps1`, then `-Mode Publish -NoBumpBuildNumber` (AOT), with size and SHA checked.
  Per-row work tests through `dotnet test --project ui/UE5DumpUI.Tests/...` only, so `dist\` stays
  AOT.
- ONE `dev-log.md` entry for the pass.
- Any step that needs Cheat Engine is announced to the maintainer first.

#### Review 6 (2026-09-12) — the fix pass adversarially reviewed

A read-only 14-agent review of `76f93b94..HEAD` (24 commits, 126 files, +6844 / −631): six area
finders, then one skeptic per top finding, each defaulting to REFUTED. **11 raw findings, 8 verified,
4 confirmed** (3 distinct defects), 4 refuted, and 3 lower-ranked LOWs reported unverified and then
checked by hand. Nothing in the review built or ran anything.

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

##### ✅ `[A1-VERDICT-STALEMB]` MED — `getOffsetsVerdict` drops the AA19 stale-mailbox latch (found 2026-09-12 by review 6; FIXED IN SOURCE 2026-09-12)

`scripts/ue5_invoke_helper.lua`'s new wrapper (`[W5-OFFSETS-MAILBOX]`, batch L45) clears
`_ue5_invoke_busy` unconditionally after a timeout and never sets `_ue5_invoke_stale_mb`. The next
`invokeUFunction` therefore passes its entry guard and writes className / funcName / params into a
mailbox the DLL still owns — the exact overwrite audit #5 AA19 exists to prevent, and which
`invokeUFunction` itself latches against.
- ⚠ **Two corrections from the verifier, both worth keeping:** the wrapper clears `status` BEFORE
  waiting, so at the deadline `waitDone` reads STATUS_IDLE and reports *"the DLL never picked this up
  (stale g_invokeMailbox address?)"* — a guessed diagnosis at the moment the DLL is actively using the
  mailbox. And the consequence of the overwrite is not always "reported OK": the completing handler
  publishes DONE + cmd=IDLE, so the next invoke can be dropped entirely while its caller reads the
  OTHER command's result.
- **`dbgCamMailbox` has the identical pre-AA19 shape** and predates this fix pass, so the fix belongs
  to both wrappers rather than to the new one alone.
- ⬜ **Also recorded (LOW, same wrapper):** against an *uninitialised* pre-contract-5 DLL the mailbox
  answers `-10` ("DLL not initialized"), not `-1`, so reporting `dll-too-old` is the wrong diagnosis.
- ✅ **Safe fix:** mirror `invokeUFunction` — latch `_ue5_invoke_stale_mb` on a timeout, release
  `_ue5_invoke_busy` only when the mailbox is ours again, re-test the latch on entry, and tell `-10`
  apart from `-1`.
- ✅ **FIXED IN SOURCE 2026-09-12** (batch L48), taken as a fix to the SHAPE: one shared
  `simpleMailboxCall` carries the entry re-test, the latch and the latch-aware release, and **both**
  small wrappers go through it — the new verdict one and `dbgCamMailbox`, which had the same
  pre-AA19 shape and predates the fix pass. It also clears `errorMsg` (AA18) and writes `cmd` LAST,
  so no wrapper can get that order wrong.
  - `-10` is now `dll-not-initialised`, distinct from `-1` `dll-too-old`: on a pre-contract-5 DLL the
    command is not init-exempt, so `-10` is what arrives first, and telling the user to update a
    current DLL is the wrong instruction.
  - **Red first:** the CE helper is Lua, so InvokeScriptTests pins the shape — the shared helper, both
    call sites, the latch, the latch-aware release and the `-10` branch.
  - ⬜ **Left as it was:** `invokeUFunction` keeps its own inline copy of the guard. It also frees
    string buffers and carries its own refusal text, so folding it in was out of scope for a LOW-risk
    repair; the duplication is recorded rather than hidden.

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

#### ⛔ REFUTED — do not re-raise (review 6)

- **`[W1-GROUP-DENYLIST]` "the note reports the denylist's SIZE, not how many classes it hid"**. The
  code reads as filed, but in this app's vocabulary a denylisted class IS a hidden class: the noise
  picker's column is `Hide`, `en.axaml` labels the persisted list *"Active denylist for this game"* and
  *"Hidden classes (Pivot only)"*, and `ClassPivotViewModel.HasHiddenClasses` is the same Count-based
  meaning, shipped earlier. The operative half of the note is *"switch to Diff mode to see or clear
  it"*, and that block is `IsVisible="{Binding !IsGroupMode}"`.
- **`[W3-DIP-PIXELS]` "`_scale` is a snapshot, so a cross-DPI drag discards the position again"**. The
  staleness is real and the fix is incomplete on that path, but it is **not a regression**: with a
  stale scale of 1.0 the guard computes exactly the pre-commit expression, so no state is discarded
  that was not already discarded. `_screens` has identical cadence from the same two call sites.
- **`[A1-SLOTSYM-FAILED]` "the test pins substring ORDER, not the ownership gate"**. The shipped
  behaviour is correct and was walked through end to end. ⬜ The **test-strength half stands as a
  lead**: the refuter agrees the two named mutants (`if false then`, `_mine = (_hs ~= nil)`) would
  leave the pin green, because both keep the pinned substrings in order.
- **`[A4-AB4-BETWEEN]` "the pin counts call sites, so a mis-wired bound stays green"**. All four
  Fern.cpp sites pass two distinct sets in lo-then-hi order today, and the count of 4 is complete —
  the other five `ScanType::Between` mentions are the vector and single-value branches, which build no
  `NumericTargetSet`.

#### Ledger

| # | row | sev | commit | offline evidence |
|---|---|---|---|---|
| 1 | `[W1-QUOTA-UNLIMITED]` | HIGH | e8e52a4f | red → green test; UI 4802/4802; gates 21/21; P2 detector registered |
| 2 | `[W1-SNAP-FAULT]` | HIGH | `git log --grep W1-SNAP-FAULT` | red → green + clean control; UI 4804/4804; gates 21/21; `-Target DLL` builds |
| 3 | `[P4-OTHER-INSTANCE]` | HIGH | `git log --grep P4-OTHER-INSTANCE` | red (3/4) → green; the gate's adversarial review found a jump-to-top regression in the draft; fixed red (4/15) → green 15/15; UI 4819/4819; gates 21/21. A second review found the row-count check unpinned; a test was added and mutation-checked (17/17, follow-up commit) |
| 4 | `[P4-GUESS-SHIFT]` | MED | same commit as 3 (one shared gate, as planned) | the cross-gap test red → green; the `?`-suffix refinement test red → green |
| 5 | `[W1-CONTAINER-STALE]` | MED | `git log --grep W1-CONTAINER-STALE` | `LiveWalkerRefreshStalenessTests`: 9/9 red on the recorded mechanisms → 10/10 green (+ the failed-re-read path); UI 4831/4831; gates 21/21. **Review follow-up:** 7 survived / 5 refuted; its 2 defects red → green, 7/7 mutants killed (restored by sha256); 18/18; UI 4839/4839; gates 21/21 |
| 6 | `[P4-CONTAINER-BASE]` | MED | same commit as 5 (the six members travel with the lists) | refresh + drill-time re-read; `ContainerTruncationTests` 19/19 |
| 7 | `[P4-PTRCLASS]` | LOW | same commit as 5 (same copy path) | red → green |
| 8 | `[A3-PTR-NAV-REPAINT]` | LOW | `git log --grep A3-PTR-NAV-REPAINT` (the bundle's follow-up; it belonged in 5's commit) | red → green |
| 9 | `[A4-EDIT-STALE-PENDING]` | MED | `git log --grep A4-EDIT-STALE-PENDING` | code-behind hook pin red → green; semantics + the two recorded-unsafe controls pinned. **Review follow-up:** 3 LOW survived (pin strength, commit-half control, comments); the pins were strengthened and a `CellEditEnded` pin added; 4/4 mutants killed |
| 10 | `[A4-NAV-BACKFIRST-GRAFT]` | MED | `git log --grep A4-NAV-BACKFIRST-GRAFT` | `LiveWalkerNavStampTests`: 5/5 gated interleavings red → green; 5 negative controls green throughout; NavRace / ForwardNav / staleness / gate / truncation / search-nav classes green. **Review follow-up:** 9 survived / 4 refuted; the render discard, the refresh identity + no-restamp, the export / bookmark guards and the re-root ticket landed; 6 new tests red → green, 20/20; UI 4867/4867; gates 21/21 |
| 11 | `[A4-PARENT-CRUMB-VTABLE]` | MED | `git log --grep A4-PARENT-CRUMB-VTABLE` | both recorded scenarios red → green; NavStamp / ForwardNav / NavRace / GWorldActorChain classes green. **Review follow-up:** 6 survived / 3 refuted; GWorld-root skip + option (b) clean-then-anchor; 2 defects red → green, 2 pins + Forward; 4/4 mutants killed |
| 12 | `[A3-BOOL-NATIVE-NOWRITE]` | MED | `git log --grep A3-BOOL-NATIVE-NOWRITE` | `LiveWalkerBoolWriteTests` 5/6 red → green (the read-modify-write control green both ways) + the `PlanBoolWrite` theory; `DumpServiceTests` `bool_native` parse red → green; `dll_helpers_test` `ClassifyBoolLayout`. DLL + 4 proxies + both C++ test exes built via `build_dll.py`, exit 0; UI 4892/4892; gates 21/21. **Review follow-up:** 10 survived / 1 refuted. The HIGH was that native is SetBoolSize's {1,0,01,FF}, not {1,0,FF,FF}, so no real bool classified native. It is fixed; helpers + `dll_core_test` BOOLLAYOUT / BOOLNATIVE were red first (2 + 3). The read side and the masked read-back / map / set pins also landed. 4/4 DLL + 6/6 UI mutants killed; UI 4952/4952 |
| 13 | `[A3-FIRE-STRUCT-BOOLMASK]` | LOW | same commit as row 12 (batch B05) | `InvokeBoolMaskTests` 7/7 red → green; `invoke_helper_test.lua` 2 new cases red → green, 97/97; ParamBufferBuilder 120/120, InvokeScript 134/134, CeLuaHygiene 76/76, CeMailboxBailout 262/262. **Review follow-up:** the read side (post-call readout + return grid) now decodes the bit; 2 red first; 2/2 mutants killed. **Review follow-up (d8a7f44f + d5e9148d + 9abc03c8):** packed bools at a non-zero offset pinned in both decoders; 1/1 mutant killed |
| 14 | `[A2-STRUCT-PREVIEW-BOOLMASK]` | LOW | same commit as row 12 (batch B05) | `dll_helpers_test` `PreviewScalarValue` packed set/clear, mask-0 fallback and native `0xFF` cases; the TOptional hand copy routed through `InterpretStructByLayout`. **Review follow-up:** the call site is pinned in `dll_core_test` BOOLLAYOUT; its mutant was killed |
| 15 | `[P3-INVOKE-Y11-CEFORM]` | MED | `git log --grep P3-INVOKE-Y11-CEFORM` | `InvokeScriptTests.CeForm_*`: parity over 34 type names (10 red), gate / FText / predicate spelling (8 red), controls green throughout. The Lua `_isZeroDefault` was run through a Lua interpreter, 16/16. 4/4 mutants killed; UI 4946/4946. **Review follow-up (d8a7f44f + d5e9148d + 9abc03c8):** Copy AA Script now runs FIRE's gate (source pin, red first); the gate's Lua shape is pinned; 2/2 mutants killed. **Review follow-up 2 (cc430176 + cd73ec38):** the Copy AA pins now match the refusal's shape and the exact skip statement |
| 16 | `[P3-INVOKE-STRUCT-FSTRING]` | LOW | same commit as row 15 (batch B06) | `AuditL11HonestyTests.StructFString_*`: 7 red → green, the empty-member control green both ways; 3/3 mutants killed (refusal, trimmed compare, write skip). **Review follow-up (d8a7f44f + d5e9148d + 9abc03c8):** Copy AA Script skips struct string members (source pin, red first); 1/1 mutant killed |
| 17 | `[A2-UFUNC-TAIL-4X]` | MED | `git log --grep A2-UFUNC-TAIL-4X` | `dll_core_test` UFUNCTAIL, 3 red: numParms 52 / parmsSize 3 / rvo 0x30 at 4.15. UFUNCWALK, 2 red: both subclass reads empty at 4.15. The 4.18 / UE 5.5 controls stayed green; `dll_helpers_test` pins the boundary, unknown version and subclass start. 7/7 DLL mutants killed; DLL + 4 proxies built; helpers 2663/0, core 136/0. **Review follow-up (d8a7f44f + d5e9148d + 9abc03c8):** `ParamTargetType` (FindFunctionsByClassParam) twin fixed, UFUNCWALK `CountClassParams` red first; 1/1 mutant killed; the CPN x 4.11-4.17 delta is recorded as an unmeasured lead |
| 18 | `[A3-CEFORM-4X-STALESLAB]` | LOW | same commit as row 17 (batch B07) | `InvokeScriptTests.ZeroFill_*`: 3 red → green; the right-ParmsSize control green both ways; the `ParamsDataBytes_MatchesMimicH` pin. 4/4 mutants killed; UI 4957/4957. **Review follow-up (d8a7f44f + d5e9148d + 9abc03c8):** a param past the slab now refuses the whole script (2 red first); the DEBUG return prints are slab-bounded; 2/2 mutants killed; UI 4969/4969. **Review follow-up 2 (cc430176 + cd73ec38):** the untick is pinned to the refusal itself, and a small-ParmsSize row pins the walked span |
| 19 | `[A2-TOPTIONAL-INTRUSIVE]` | MED | `git log --grep A2-TOPTIONAL-INTRUSIVE` | `dll_core_test` OPTLAYOUT (pool-faking): 9 red, green after the fix; the set / set-empty / Find Refs-set controls and the UNREADVAL TOptional cases green throughout. `dll_helpers_test` pins `ClassifyOptionalLayout`. 6/6 DLL mutants killed; DLL + 4 proxies built; helpers 2678/0, core 157/0. **Review follow-up 2 (cc430176 + cd73ec38):** the Lazy alignment regression fixed (2 red first) and 5 missing pins added; 8 DLL + 4 UI mutants killed; UI 4984/4984 |
| 20 | `[A3-DEPLOY-CANCEL]` | MED | `git log --grep A3-DEPLOY-CANCEL` | `ProxyDeployConcurrencyTests`: 5 red → green (Deploy / Undeploy cancelled mid-run, the saved pick, the one-game final-refresh cancel, Refresh's red "Refresh failed"); the no-cancel control green throughout. 5/5 mutants killed, incl. the recorded-unsafe re-run with the cancelled token; UI 4976/4976. **Review follow-up:** that re-run was killed for Deploy only (every Undeploy test ran with `ThrowOnCancelledRefresh` off, and nothing checked that the post-cancel refresh landed). The Remove flag, a one-game Remove twin, landed-refresh + `ErrorMessage` asserts and the neutral colour are now pinned; Update All's cancel refreshes too (red first); 4/4 mutants killed; UI 5005/5005 |
| 21 | `[A3-RADIO-MIDDEPLOY]` | LOW | same commit as row 20 (batch B09) | the AXAML pin (red first); the binding compiles in the UI build; 1/1 mutant killed. **Review follow-up:** the pin also refuses an `IsEnabled` on the foreign-overwrite checkbox and on the radios' panel; 2/2 mutants killed. **Second follow-up:** the foreign checkbox's own panel is pinned; 1/1 mutant killed |
| 22 | `[A1-COORD-RESURRECT]` | MED | `git log --grep A1-COORD-RESURRECT` | `ClearAll_ThenLoad_DoesNotResurrectTheLibrary` red first; `Load_CorruptMainFile_RecoversFromBackup` stays green. 1/1 mutant killed; UI 4981/4981. **Review follow-up:** `Delete` rolls a parseable main to `.bak` first (a transient lock at Load had let Clear all lose the newest revision; red first), and the resurrect test pins that `.bak` survives. **Second follow-up:** `Delete` refuses when the roll fails (red first), and its parse guard is pinned |
| 23 | `[A1-COORD-BACKUP]` | LOW | same commit as row 22 (batch B10) | both backups after a `.bak` recovery + a Save over a corrupt main: 3 red first (against the old API), the rolling-backup control green both ways. 3/3 mutants killed; the view model's snapshot hand-off is compile-covered only. **Review follow-up:** `Save` moves an unparseable main aside (bounded `.corrupt-*` copies) instead of destroying it (red first); pins for the passed-in library, `ZTolerance` and a three-save roll; 6/6 mutants killed across both rows; UI 5009/5009. **Second follow-up:** the quarantine copies instead of moving, and the prune spares the fresh copy (red first); 3/3 expected mutants killed; the 4th (move instead of copy) is the documented survivor; UI 5033/5033 (one suite run over the three second-round follow-ups together) |
| 24 | `[W3-CONSOLE-REINVOKE]` | MED | `git log --grep W3-CONSOLE-REINVOKE` | `DispatchTimeout_on_a_pinned_invoke_is_not_resent_and_keeps_the_pin` red first (invocation count, status, surviving pin); `StalePin_minus4_is_still_retried` the control for the refused half. 3/3 mutants killed; UI 4983/4983. **Review follow-up:** the queued note reads the FINAL result, so a timed-out self-heal retry is reported (red first); pins for `-2` / `-4` and an unpinned `-5`; 3/3 mutants killed; UI 5011/5011. Filed `[W3-DUNSTE-QUEUED]` and `[W3-DEBUGCAM-QUEUED]` |
| 25 | `[P3-SNAPNUM-ENUM]` | MED | `git log --grep P3-SNAPNUM-ENUM` (batch B12) | `TryFromHex_DecodesAnEnumUnsigned` (3) + `Render_ShowsAnEnumAsItsNumber_NotRawHex` (2) red first. 2/2 mutants killed. **Review:** snapshots captured before this build keep NULL enum values for the numeric readers; recorded as a decision, not backfilled (the policy first cited does not cover it, corrected by the review of f023a35a) |
| 26 | `[W2-GROUPMATCH-ENUM]` | MED | same commit as row 25 (batch B12) | the first tests were VACUOUS (a one-slot `Run` is always false) and were rewritten on `LeafSatisfiesSlot` + a real two-slot group, so their red is the mutation check, not a pre-fix run. 3/3 mutants killed, including the recorded harmful partial (`WidthBytes` without `IsOneByte`), which the NumericNoByte control catches; UI 4994/4994 |
| 27 | `[W2-ORDEN-FINDENTRY]` | MED | `git log --grep W2-ORDEN-FINDENTRY` (batch B13) | `Test_Orden_OrderedVerdictWidths` 3 ⭐ + dll_core_test `GROUPREFINE` 2 ⭐ red first; the Bigger 70000 and Between controls green both ways. 3/3 mutants killed: Orden's verdict, its Between guard, the refine's verdict. **Review follow-up:** GROUPREFINE pins the verdict itself (an unsigned 0, an Int16 32767), which a zeroed-bytes refine passed |
| 28 | `[W2-GROUPMATCH-WIDTH]` | MED | same commit as row 27 (batch B13) | 5 theory rows + the group fact red first; 3 controls. 3/3 mutants killed: the verdict, the two sides swapped, Exact admitted; UI 5003/5003 |
| 29 | `[A4-AB4-UINT64]` | LOW | same commit as row 27 (batch B13) | 5 ⭐ red first; 4 controls, one of them the narrow boundary. 3/3 mutants killed, including `>= 2^63` weakened to `>`. Filed `[A4-AB4-BETWEEN]` for the records gap. **Review follow-up:** "-0" and negative fractions that round to 0 keep the unsigned widths (red first, 3 ⭐); the C# mirror accepts ±Infinity as the DLL does (red first, 2 rows); 3/3 mutants killed; UI 5033/5033 (one suite run over the three second-round follow-ups together) |
| 30 | `[W1-PIVOT-SESSION]` | MED | `git log --grep W1-PIVOT-SESSION` (batch B14) | 3 red first (no session, a previous launch, disconnect); 2 controls (the current launch, DataTable rows under an old pick). `check_session_gate` registered, 20/20 gated. 7/7 mutants killed, the two AXAML ones through the registered gate; UI 5016/5016 |
| 31 | `[W1-SPC-JOINMODE]` | MED | `git log --grep W1-SPC-JOINMODE` (batch B15) | the source pin red first (the wiring is in `MainWindowViewModel`, which no test constructs); 4 behaviour tests whose red is the mutation check. 6/6 mutants killed; UI 5021/5021 |
| 32 | `[W1-DISCOVER-ARRAY]` | MED | `git log --grep W1-DISCOVER-ARRAY` (batch B16) | "Use →" on an array element and the scalar "Ghost" prop red first; the two array refusals take their red from the mutation check. 7/7 (one first tried in a form that did not compile; its compiling form went red) mutants killed across both rows; UI 5026/5026 |
| 33 | `[W1-ARRAYCOUNT]` | LOW | same commit as row 32 (batch B16) | `ListArrayFields_CountsElements_NotInnerPropRows` red first (6 rows, 3 elements); the old one-prop test kept, its comment corrected |
| 34 | `[W4-RELATED-RACE]` | MED | `git log --grep W4-RELATED-RACE` (batch B17) | 4 red first over a gated fake (the stale load lands last / first / fails / lands after a disconnect). 4/4 mutants killed; UI 5039/5039 (one suite run over B17 and B18 together) |
| 35 | `[W4-LOOKUP-FILTER]` | MED | `git log --grep W4-LOOKUP-FILTER` (batch B18) | 2 red first (a filter pass; a leftover keyword, then cleared). 1/1 mutant killed; UI 5039/5039 (one suite run over B17 and B18 together) |
| 36 | `[W4-BOOKMARK-DT]` | MED | `git log --grep W4-BOOKMARK-DT` (batch B19) | 4 red first (the flag through the file, the re-walk, a changed table, a refused address); the in-session control green both ways. 6/6 (one first tried in a form that did not compile; its compiling form went red) mutants killed, the unguarded re-walk among them; UI 5044/5044 |
| 37 | `[W3-BATCH-METHOD]` | MED | `git log --grep W3-BATCH-METHOD` (batch B20) | a theory over the two not-analysed tags red first; the analysed control green both ways. 3/3 mutants killed; UI 5048/5048 |
| 38 | `[W2-GRAVDIR-VERDICT]` | MED | `git log --grep W2-GRAVDIR-VERDICT` (batch B21) | the readout and the apply without a pawn red first; the pre-5.4 apply the control. 9/9 mutants killed across both rows; UI 5057/5057 |
| 39 | `[W2-MS-PROMISE]` | LOW | same commit as row 38 (batch B21) | Move Speed without a pawn red first, plus the Hemmung twin (both time lanes, red first). The clause is deleted, not made true |
| 40 | `[W2-TPREL-MAP]` | MED | `git log --grep W2-TPREL-MAP` (batch B22) | 5 tests red first (the ParsePose key, the directional TP keeping map and source, the add-time read, the connect prime, an unknown map); the reported-map control green both ways. 11/11 mutants killed; UI 5063/5063 |
| 41 | `[W2-POSEATTACH-QUIETPOLL]` | MED | `git log --grep W2-POSEATTACH-QUIETPOLL` (batch B22b) | 4 tests red first (the quiet poll, a directional TP keeping the state, the disconnect clearing it, the chip bound in the panel); a healthy read clearing it is the control. 5/5 mutants killed; UI 5068/5068 |
| 42 | `[W5-CEXML-FSTRING]` | MED | `git log --grep W5-CEXML-FSTRING` (batch B23) | 7 tests red first (the 3-row array theory, map key + value, flatten key, struct member, FText array, fabricated tail); the TSet control green both ways. 10/10 mutants killed; UI 5077/5077. The walk's own TArray elements came with `[W5-STRARRAY-ELEMENTS]` (row 43, B23b); the review of 110cbb4e corrected "inert" |
| 43 | `[W5-STRARRAY-ELEMENTS]` | MED | `git log --grep W5-STRARRAY-ELEMENTS` (batch B23b) | the dll_core_test reader block and the STRARRAYWALK walker block red first against inert stubs; a CSX pin green both ways. 5/5 mutants killed, 2 documented survivors (the UE4 UProperty-mode call site, and Fern.cpp, which no test target compiles); dll_core_test 204/204; UI 5091/5091 |
| 44 | `[A4-USMAP-ENUM-UNDERLYING]` | MED | `git log --grep A4-USMAP-ENUM-UNDERLYING` (batch B24) | a Size theory (2/4/8 red first; 1 and the 3-byte fallback green both ways) and the TEnumAsByte shape red first; the plain-byte control green both ways; the round-trip reader now reads the underlying type. 4/4 mutants killed; UI 5098/5098 |
| 45 | `[W3-XREF-CAP]` | MED | `git log --grep W3-XREF-CAP` (batch B25) | dll_core_test (the merge helper, and FindPropertyXrefs over the fake pool) and the UI (cell, clause, dialog status, batch loops, the parse) red first against inert stubs. 11/11 mutants killed; dll_core_test 214/214; UI 5112/5112. Five UI sites, not four |
| 46 | `[W4-RELATED-STOPS]` | MED | `git log --grep W4-RELATED-STOPS` (batch B26) | dll_core_test (a fake owned graph tripping each bound) and the UI (parse, clause, panel status) red first against an inert stats param. 15/15 mutants killed; dll_core_test 230/230; UI 5120/5120. Five flags, one per cause |
| 47 | `[W4-STRIDE-TENTATIVE]` | MED | `git log --grep W4-STRIDE-TENTATIVE` (batch B27) | dll_core_test (re-inits over throwaway arrays: detected, tentative, undetected, forced, and the reset at entry) and the UI (parse, badge, dump stamp) red first against inert accessors. 12/12 mutants killed; dll_core_test 237/237; UI 5129/5129. Its own field, not a fourth layout mode |
| 48 | `[A3-B30-STALE-FLAG]` | MED | `git log --grep A3-B30-STALE-FLAG` (batch B28) | one ordering pin per shipped artifact (generator + `.CT`), red first: the serving branch clears the flag before its untick. 2/2 mutants killed; UI 5134/5134. The recorded safe fix; no contract bump |
| 49 | `[W3-DUNSTE-QUEUED]` | MED | `git log --grep W3-DUNSTE-QUEUED` (batch B31) | dll_helpers_test red first against the pre-fix mapping: -5 is Queued, and a queued request commits; the other codes stay Refused. 2/2 mutants killed; dll_helpers_test 2708/2708; UI 5134/5134 |
| 50 | `[A3-ST1-SUPER-DRAIN]` | MED | `git log --grep A3-ST1-SUPER-DRAIN` (batch B30) | a source pin, red first: the fail-open branch goes through `Stark::CallAddressAsOwnSEH`, which holds the own-PE-call mark in an outer frame. 2/2 mutants killed; UI 5135/5135. The recorded safe fix; no contract bump |
| 51 | `[W2-MARKER-PARENTREL]` (pipe half) | MED | `git log --grep W2-MARKER-PARENTREL` (batch B29a) | the UI, red first against inert properties: a parent-relative save (status and row), a refresh flagging a marker and the Last slot, and the parse. 5/5 mutants killed; UI 5139/5139. The mailbox half is B29b |
| 52 | `[W2-TPREL-TRANSPORTS]` + `[W2-MARKER-PARENTREL]` (mailbox half) | LOW + MED | `git log --grep W2-TPREL-TRANSPORTS` (batch B29b) | the CE records' flag read (a theory) and a source pin on Mimic.cpp / Frieren.cpp, red first. 4/4 mutants killed; UI 5144/5144. Contract 3 → 4, additive (MIN stays 1) |
| 53 | `[A4-USMAP-CONTAINER-ENUM]` | MED | `git log --grep A4-USMAP-CONTAINER-ENUM` (batch B32) | dll_core_test (a fake Array / Set / Map whose inners carry a UEnum), the byte-exact USMAP shapes and the parse, red first. 6/6 mutants killed; dll_core_test 243/243; UI 5148/5148 |
| 54 | `[P1-GENAU-ABORT]` + `[A2-GNAMES-PTRSCAN-ABORT]` | LOW | `git log --grep P1-GENAU-ABORT` (batch L01) | dll_core_test (each sweep with a pending cancel records its abort at the bail) and a Frieren source pin, red first against inert out-flags. 6/6 mutants killed; dll_core_test 249/249; UI 5149/5149 |
| 55 | `[P1-ENUMNAMES]` | LOW | `git log --grep P1-ENUMNAMES` (batch L02) | dll_core_test (a cancelled ForEach says so; a cancelled search latches nothing) and the UI (parse, export note), red first. 5/5 mutants killed; dll_core_test 253/253; UI 5152/5152. The harmful half as filed (publish the latch as-is) did not land |
| 56 | `[A4-PUSHCE-UNPADDED]` + `[W5-CSX-DELEGATEPAD]` | LOW | `git log --grep A4-PUSHCE-UNPADDED` (batch L03a) | the UI: a caller-level batch-push test, and CSX offsets for a unicast leaf and a multicast drill, red first. 5/5 mutants killed; UI 5162/5162. Not a blanket `+ DelegatePad`: the multicast raw block stays at the field offset |
| 57 | `[A4-DELEGATE-ARRAY-PAD]` | LOW | `git log --grep A4-DELEGATE-ARRAY-PAD` (batch L03b) | dll_core_test (the helper, and a fake walk over a padded and an unpadded delegate array) and the UI (parse, CE XML leaves and tail, CSX leaves, multicast controls), red first against an inert member / helper / property. 10/10 mutants killed; dll_core_test 275/275; UI 5169/5169. The pad is per ELEMENT and ArrayProperty-only, never on the array field |
| 58 | `[P3-SDK-INNERS]` + `[P3-SDK-GUESSED]` | LOW | `git log --grep P3-SDK-INNERS` (batch L04) | the UI: each copied inner spelling (Array, with its class, Map, Set) and a header over a guessed row, red first. 5/5 mutants killed; UI 5176/5176. Only the four scalar arms: the rest of the INNERS sketch was not safe |
| 59 | `[P1-SEETHRU-NOPRODUCER]` + `[P1-SEETHRU-GIVEUP]` | LOW | `git log --grep P1-SEETHRU-NOPRODUCER` (batch L05) | the UI (the VM card for a -3 refusal, an abandoned and a pending restore; the parse) and source pins for Schlacht.cpp / Fern.cpp, red first. 9/9 mutants killed; UI 5183/5183. Refused at ENABLE, the recorded shape; the per-hit case stays out |
| 60 | `[P1-UPROP-DELEGATE]` | LOW | `git log --grep P1-UPROP-DELEGATE` (batch L06) | dll_core_test, red first: a UProperty-mode walk over a refusing delegate array and a refusing multicast array. 2/2 mutants killed; dll_core_test 283/283; UI 5195/5195. The recorded safe fix, copied verbatim from the FProperty twins |
| 61 | `[P1-WALK-UNREADABLE]` + `[A4-REROOT-STALE-WARNING]` | LOW | `git log --grep P1-WALK-UNREADABLE` (batch L07) | dll_core_test (an unreadable walk is marked; a readable control), the parse, a Fern lean/full pin, and the Live Walker status on a re-root (freed, freed with a way back, unreadable) plus the compose rule, red first. 6/6 mutants killed; dll_core_test 287/287; UI 5204/5204. Its own key, not folded into `stale`; composed, never overwritten |
| 62 | `[P1-SPARSEDELEGATE-REFS]` (+ the PATTERN-P5 widening) | LOW | `git log --grep P1-SPARSEDELEGATE-REFS` (batch L08) | dll_core_test (a planted sparse-delegate map: two unreadable delegates counted, the readable one not), the parse, a Fern pin, the status rule, and both Live Walker status lines, red first. 9/9 mutants killed; dll_core_test 291/291; UI 5212/5212. Aggregate channel only: no per-entry wire change |
| 63 | `[A2-WALKCLASSEX-UNMAPPED]` (+ the `GetCachedStructFields` twin) | LOW | `git log --grep A2-WALKCLASSEX-UNMAPPED` (batch L09) | dll_core_test, red first: a decommitted page is refused by WalkClassEx and memoized by neither cache; re-committed, it walks. 3/3 mutants killed; dll_core_test 297/297; UI 5212/5212. The recorded safe fix: the read verdict threaded out, a once-per-address log guard |
| 64 | `[A2-LAZY-LATCH-GUESS]` | LOW | `git log --grep A2-LAZY-LATCH-GUESS` (batch L10) | dll_core_test at a mis-resolved 504, red first: a real 0x1C is kept and latches +0x0C, `ResolveInnerSize` reads it, the reader latches nothing from the size it is handed; garbage still falls back, unlatched. 3/3 mutants killed; dll_core_test 304/304; UI 5212/5212. The recorded safe fix; the `InferScalarSize` entry kept |
| 65 | `[A2-CRC-PATH-LS]` (+ its gate gap) | LOW | `git log --grep A2-CRC-PATH-LS` (batch L11) | A dll/src-wide gate test, red first: no `%ls` in a `Sein::` / `LOG_` call. It found eight sites (CrashReportClient ×2, the VERSIONINFO key, the pipe name, both proxies ×2), all converted. 4/4 mutants killed; dll_core_test 304/304; UI 5213/5213. Byte-identical for ASCII |
| 66 | `[A2-HEAP-ANCHOR-TEXT]` | LOW | `git log --grep A2-HEAP-ANCHOR-TEXT` (batch L12) | dll_helpers_test, red first: a no-module anchor is Heap; a heap anchor refuses a foreign candidate with its own verdict and admits the producer; the truth table is 16 rows. 3/3 mutants killed; dll_helpers_test 2716/2716, dll_core_test 304/304; UI 5213/5213. The enum form; the switch tail fails closed; None wording byte-identical |
| 67 | `[A2-METHODE-MANUALMAP]` | LOW | `git log --grep A2-METHODE-MANUALMAP` (batch L13) | A Methode.cpp source pin, red first: the TRUE-but-absent text is ambiguous and names "Always force load modules"; the old verdict on CE's BOOL is gone. 2/2 mutants killed; dll_core_test 304/304, dll_helpers_test 2716/2716; UI 5214/5214. The recorded safe fix; comment and working-lessons corrected |
| 68 | `[A3-MIMIC-INIT-FASTPATH]` | LOW | `git log --grep A3-MIMIC-INIT-FASTPATH` (batch L14) | dll_helpers_test (`Mimic::InitFastPathOk`: both globals set WHILE an init scans does not take the fast path) + source pins for both ends of the wiring, red first. 3/3 mutants killed; dll_helpers_test 2719/2719, dll_core_test 304/304; UI 5215/5215. The recorded narrowest fix; the contract hash is unmoved |
| 69 | `[W5-DENKEN-DEADGUARD]` | LOW | `git log --grep W5-DENKEN-DEADGUARD` (batch L16) | dll_helpers_test pins the behaviour the obvious repair would break: a followed impl is decoded past the death of its this-alias. Red against that repair as a source mutant. 2/2 mutants killed; dll_helpers_test 2721/2721, dll_core_test 304/304; UI 5215/5215. The dead guard removed; behaviour unchanged |
| 70 | `[A4-CDOSCOPE-ANCESTOR]` + `[A4-CDOSCOPE-NESTED-PREVIEW]` | LOW | `git log --grep A4-CDOSCOPE-ANCESTOR` (batch L17) | dll_core_test, red first: `Aura::PreviewAncestorsOf` credits every preview class from the super up, and a nested row sharing a direct row's class is not previewed. 3/3 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5215/5215. Both recorded safe fixes; the swap not restored |
| 71 | `[A4-LW-DISCONNECT-PARENT]` + `[A1-DETECT-REPUBLISH]` | LOW | `git log --grep A4-LW-DISCONNECT-PARENT` (batch L19) | AuditL11HonestyTests, red first: a disconnected walker keeps no Parent, References header or function list; a Detect run in flight at the disconnect, resumed with a result or a failure, touches neither the rows nor the status. 7/7 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5218/5218. Both recorded safe fixes; no CTS |
| 72 | `[A4-STEALTH-PRIME]` | LOW | `git log --grep A4-STEALTH-PRIME` (batch L20) | TeleportViewModelTests, red first: a still-held meter primes Holding, a Property Search force is not claimed (Unknown), and a disconnect says Unknown; nothing forced reads Off. Gate 17d reads the tuple form (a third selftest control). 4/4 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5221/5221. The recorded safe fix |
| 73 | `[A4-PIVOT-CROSSGAME-ID]` + `[W1-PIVOT-LOADCTS]` | LOW | `git log --grep A4-PIVOT-CROSSGAME-ID` (batch L18) | One cross-game test each for Class Pivot, Snapshot and SPC on the real per-game store (the other game's newest or default wins; Class Pivot never serves the other game's class list), and a class load gated on its token that a field load must not cancel, red first. 5/5 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5225/5225. The recorded safe fix, all three twins; no clear in SetEngineState |
| 74 | `[A4-GAMEONLY-ADVICE]` + `[P5-GROUP-ADVICE]` + `[A3-CONTAINER-4096-ADVICE]` | LOW | `git log --grep A4-GAMEONLY-ADVICE` (batch L21) | Red first: the two Game Only pins inverted with controls, a group cap stop that must not advise "refine", and a scalar drill capped by the DLL that must not blame the slider. 6/6 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5229/5229. Every advice names a lever the panel has and has not used |
| 75 | `[W1-DT-TRUNC]` + `[P5-PIVOT-FETCHCAP]` | LOW | `git log --grep P5-PIVOT-FETCHCAP` (batch L22) | ClassPivotViewModelTests, red first: a capped DataTable Run keeps "(showing 2 of 500)"; `PivotRunStatus` gives the fetch cap its own sentence and "≥" counts, with both caps able to show; the group cap alone is the control. 4/4 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5233/5233. The fetch cap has its own flag, landed with the change that stopped folding it |
| 76 | `[W1-WINMM-LOADMODE]` | LOW | `git log --grep W1-WINMM-LOADMODE` (batch L27) | A symmetry pin, red first: Fern's load_mode classifier must name every proxy file name Methode's `kProxyDllNames` lists (all four). 2/2 mutants killed, one of them a different proxy dropped; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5234/5234 |
| 77 | `[W5-INSTEXPORT-TRUNC]` | LOW | `git log --grep W5-INSTEXPORT-TRUNC` (batch L24) | AuditL11HonestyTests, red first: a 61,000-field Instance Finder export says "Copied, but TRUNCATED…" with this panel's levers, never Live Walker's; a small one adds nothing. 3/3 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5236/5236. The four copy hazards handled, not copied |
| 78 | `[W1-PIPEBUSY-LOG]` | LOW | `git log --grep W1-PIPEBUSY-LOG` (batch L26) | AobMakerInjectTableFileTests, red first through an internal seam (pipe name, 150 ms timeout, existence probe): a busy pipe is a Warn "EXISTS but no instance was free", an absent one stays the Debug "not running". 2/2 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5238/5238. The public constructor is unchanged |
| 79 | `[W1-GROUP-DENYLIST]` | LOW | `git log --grep W1-GROUP-DENYLIST` (batch L23) | SnapshotViewModelTests, red first: a group match with a Diff denylist says "1 class(es) hidden by the Diff denylist (switch to Diff mode…)"; without one it says nothing about it. 2/2 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5240/5240. Disclosure only: the applying is documented design |
| 80 | `[W1-PARTIAL-MARK]` | LOW | `git log --grep W1-PARTIAL-MARK` (batch L25) | SnapshotStoreTests + SnapshotViewModelTests, red first: the reason round-trips and the partial survives `DeleteUnusableSnapshotsAsync`; a capped capture persists `cap` and stays usable; a mid-capture low-disk stop persists `disklow`, not `cap`; the grid label and the picker line both carry it; a clean capture carries none. 7/7 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5244/5244. A NEW column, per the refuted-fix note: `is_usable=0` would auto-delete the partial |
| 81 | `[P3-SCORING-MCDELEGATE]` | LOW | `git log --grep P3-SCORING-MCDELEGATE` (batch L33) | PropertyScoringTableTests, red first: a stat-named `MulticastDelegateProperty` gets `NonValueTypePenalty`; the three split spellings keep it (control). 1/1 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5248/5248. One name in a private predicate, one consumer |
| 82 | `[W3-CAP-NOSAVE]` | LOW | `git log --grep W3-CAP-NOSAVE` (batch L30) | ClassListCapTests.cs `UiOptionsPersistSymmetryTests`, red first: every `BuildOptions` line copying from a tracked view model must name a property in that view model's persist set (one explicit alias, `Spc.JoinModeForOptions` → `SelectedJoinMode`). It failed on exactly the two caps and found no third. 2/2 mutants killed; dll_core_test 311/311, dll_helpers_test 2721/2721; UI 5249/5249 |
| 83 | `[W5-OFFSETS-UNMEASURED]` | LOW | `git log --grep W5-OFFSETS-UNMEASURED` (batch L15) | dll_core_test, red first: a give-up after a validated run stores validated=false (the first give-up is driven for real, over a pool with no Guid / Vector); the shutdown reset forgets all three; the verdict reason reads `probe-not-run` before detection, even beside a stale reason, and is never empty for an unmeasured run. InvokeScriptTests source pins cover the second give-up, the `UE5_Shutdown` call, the export's both-flags rule and the dissect warning. 8/8 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5251/5251. The export count is 59 → 60 at every derived site. The mailbox half is L45 |
| 84 | `[W2-BETWEEN-PREVIEW]` | LOW | `git log --grep W2-BETWEEN-PREVIEW` (batch L28) | RoundModePreviewTests, red first: `1,2,3~4,5,6`, `1,000~2,000`, `(5)~10` and `5-~10` preview nothing in every scope, because the DLL refuses each of them. Control: SPC's absolute window keeps `NumberStyles.Any`, because `SpcQueryViewModel` Lo()/Hi() parse it that way. The kit's first draft assumed SPC used `SnapshotStore.TryParseValue`, which is the snapshot GROUP matcher's parse, and a read of the real call site refuted that. Whether SPC itself should accept `(5)` is a separate question, not filed. 4/4 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5256/5256 |
| 85 | `[W2-DEADSCAN-LOADMORE]` | LOW | `git log --grep W2-DEADSCAN-LOADMORE` (batch L29) | ValueSearchTests, red first: a First Scan, and a Group First Scan, that fails after a live session keeps its row, offers no Load More, and says the rows are the previous scan's. 5/5 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5258/5258. The grid is NOT cleared, per the refuted-fix table |
| 86 | `[W3-DIP-PIXELS]` | LOW | `git log --grep W3-DIP-PIXELS` (batch L31) | WindowRestoreStateTests, red first, with AF21's 3840 px / 225% / x=-1707 geometry: a position reachable in physical pixels is kept, and a genuinely off-screen one (x=-2809) is still rejected (control). A source pin checks that ManagedDialogWindow pushes `SetScale(RenderScaling)` with every `SetScreens`. 3/3 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5261/5261 |
| 87 | `[P8-BOOKMARK-TIP]` | LOW | `git log --grep P8-BOOKMARK-TIP` (batch L34) | BookmarkTests, red first: re-saving into an occupied slot raises `TooltipText`, and the hover names the new target. 2/2 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5262/5262. `BookmarkSlot_SetSameValue_DoesNotNotify` still holds |
| 88 | `[A1-LOG-RESUME]` | LOW | `git log --grep A1-LOG-RESUME` (batch L35) | LogRetentionTests, red first: rolled `-0_NNN.log` files beside a `-0.log`, or alone, are archived at startup oldest first; another category's are untouched. 2/2 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5264/5264. Residual: the in-session compression rule, not built |
| 89 | `[A3-RECYCLE-GUID-FAILOPEN]` | LOW | `git log --grep A3-RECYCLE-GUID-FAILOPEN` (batch L38) | RecycleBinPolicyTests, red first: a failed volume-GUID lookup fails closed where the per-volume flag would decide; `UseGlobalSettings` or a policy still decides on its own (control); a source pin checks the platform caller. 3/3 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5267/5267 |
| 90 | `[A3-COORD-NONFINITE]` | LOW | `git log --grep A3-COORD-NONFINITE` (batch L39) | CoordCsvCodecTests + CoordLuaParserTests, red first: `NaN`, `Infinity`, `-Infinity` and `1e400` in a CSV row, and `x=1e400` in a Lua entry, are rejected and visible, not stored as the origin. 1/1 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5272/5272 |
| 91 | `[W2-CEGEN-MODAL]` | LOW | `git log --grep W2-CEGEN-MODAL` (batch L37) | ProtectionScriptGeneratorTests + DebugCameraScriptGeneratorTests, red first: `[DISABLE]` contains no `showMessage` but still has a `dbg` reason, and `[ENABLE]` still announces and unticks (control). 6/6 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5276/5276. Scoped to the two named generators; no repo-wide `[DISABLE]` pin was added |
| 92 | `[A2-CABI-TELEPORT-PARENTREL]` | LOW | `git log --grep A2-CABI-TELEPORT-PARENTREL` (batch L44) | InvokeScriptTests source pin, red first: three `...Ex` exports carry `int32_t* outParentRelative` from `GetPose`'s flag and `m.ParentRelative`; the original signatures are unchanged. 3/3 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5277/5277. The export count is 60 → 63. Residual: BugItGo ignores the stored flag |
| 93 | `[W4-HEXSORT]` | LOW | `git log --grep W4-HEXSORT` (batch L32) | DataGridSortWiringTests adds a fourth rule, red first: every address-like sortable column must have `DataGridSortComparers.Hex` wired, even when its Binding roots it. Two `HexValue` exemptions carry reasons, with a stale-exemption check. DataGridSortComparersTests pins the five new `ulong` accessors. 5/5 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5279/5279. The pin found a tenth column outside the cluster (Class / Struct's field Address), fixed here too |
| 94 | `[A1-SLOTSYM-FAILED]` + `[A1-LUA-WAIT]` | LOW | `git log --grep A1-LUA-WAIT` (batch L36) | CeLuaHygieneTests, red first:
- both waits have one deadline, with the iteration count only in the `else` of the getTickCount branch;
- the slot-symbol register records its holder, and the release checks ownership before it decrements.
CeMailboxBailoutTests' old `local _over = _st == nil or` pin now names the new shape. 4/4 mutants killed; dll_core_test 320/320, dll_helpers_test 2721/2721; UI 5281/5281. Shape pins only: the Lua RUN is a CE live check |
| 95 | `[A4-AB4-BETWEEN]` | LOW | `git log --grep A4-AB4-BETWEEN` (batch L42) | dll_helpers_test BETWEEN block, red first against the old two-build behaviour: unsigned and Int16 bounds are clamped per width, reversed bounds are normalised, 64-bit values are exact, a float bound beyond 64 bits still bounds, and only `Encoded` entries are emitted. GroupMatchTests carries the same rows, and an InvokeScriptTests source pin covers the four Fern sites. 6/6 mutants killed; dll_helpers_test 2742/2742, dll_core_test 320/320; UI 5289/5289 |
| 96 | `[A2-TOPTIONAL-VALUESCAN]` | LOW | `git log --grep A2-TOPTIONAL-VALUESCAN` (batch L41) | dll_helpers_test V1C block, red first against inert helpers: a trailing flag gates at `sizeof(T)`; intrusive FString / FName / FText carry their sentinels; Unknown and sentinel-less intrusive optionals are skipped; the three sentinel byte tests each have a control. An InvokeScriptTests source pin covers the V1c wiring, and the loose rule is removed. 4/4 mutants killed; dll_helpers_test 2742/2742, dll_core_test 320/320; UI 5290/5290. Refine-path lead recorded |
| 97 | `[W3-DEBUGCAM-QUEUED]` | LOW | `git log --grep W3-DEBUGCAM-QUEUED` (batch L43) | Red first: dll_helpers_test DBGCAMQ (the mapper, against an inert stub, with -4 / -7 controls); Console and Teleport VM tests for the Queued badge and text; DebugCameraScriptGeneratorTests (the queued branch is first, never unticks, never closes); an InvokeScriptTests source pin for Frieren and Mimic. 5/5 mutants killed; dll_helpers_test 2746/2746, dll_core_test 320/320; UI 5294/5294. Not a contract bump (MB3); the item-4 conflict is recorded |
| 98 | `[A2-TOPTIONAL-STRUCT-DESCENT]` | LOW | `git log --grep A2-TOPTIONAL-STRUCT-DESCENT` (batch L40) | Red first in dll_core_test OPTLAYOUT: a reset `{ UObject* }` / `{ TArray<UObject*> }` struct optional reports neither its pointer nor its array (controls: set ones report both); an Unknown layout is not descended; the container cache entry carries `setFlagOffset` 24. An InvokeScriptTests source pin covers Find Refs' loops (each kind gated twice). 5/5 mutants killed; dll_core_test 327/327, dll_helpers_test 2746/2746; UI 5295/5295. `CollectSchemaLeaves` lead recorded |
| 99 | `[W5-OFFSETS-MAILBOX]` | LOW | `git log --grep W5-OFFSETS-MAILBOX` (batch L45) | Red first: dll_helpers_test pins `CMD_OFFSETS_VERDICT` = 16, the contract range 5 / 1, the new init exemption and the exemption COUNT (the test enumerates the whole command space on purpose). An InvokeScriptTests source pin covers Mimic.cpp's case + handler and the Lua wrapper, which no test target compiles. 4/4 mutants killed; dll_helpers_test 2748/2748, dll_core_test 327/327; UI 5296/5296. Contract 4 → 5, MIN stays 1: additive, so every saved `.CT` stays valid |
| 100 | `[A2-SENTINEL-OVERREAD]` | LOW | `git log --grep A2-SENTINEL-OVERREAD` (batch L46) | Review 6's first confirmed finding. Red first in dll_helpers_test's V1C block: the FString / FName / FText / None spans (16 / 4 / 8 / 0) against an inert helper that claims 16 for every sentinel; the InvokeScriptTests source pin requires `SentinelBytesNeeded` at the gate and refuses the flat `sizeof(v16)` read. 3/3 mutants killed; dll_helpers_test 2752/2752, dll_core_test 327/327; UI 5296/5296 |
| 101 | `[A2-TOPTIONAL-REFINE]` | MED | `git log --grep A2-TOPTIONAL-REFINE` (batch L47) | Review 6's MED, and the lead L41 recorded without tracing. Red first in dll_core_test **REFINEOPT**: a reset trailing-flag optional and an intrusive unset one are dropped by an Unchanged refine; a SET optional, an intrusive set one and an ordinary leaf survive (three controls). An InvokeScriptTests source pin covers the descriptor stamping. 4/4 mutants killed; dll_core_test 332/332, dll_helpers_test 2752/2752; UI 5296/5296. The verifier's two corrections are in the row |
| 102 | `[A1-VERDICT-STALEMB]` | MED | `git log --grep A1-VERDICT-STALEMB` (batch L48) | Review 6's second MED. One shared `simpleMailboxCall` for both small wrappers, carrying the AA19 latch; `-10` told apart from `-1`. Red first: InvokeScriptTests pins the helper, both call sites, the latch, the latch-aware release and the `dll-not-initialised` branch (the CE helper is Lua, so no test can run it). 4/4 mutants killed; dll_core_test 332/332, dll_helpers_test 2752/2752; UI 5297/5297 |
| 103 | `[A1-REVIEW6-PINS]` | LOW | `git log --grep A1-REVIEW6-PINS` (batch L49) | Review 6's record-keeping half. The init-gate comments in Mimic.cpp and dll_helpers_test name both exemptions (source pin, red first); `DllLogCalls_NeverFormatAWideString` counts its scanned files and matched log lines, so a scan that stops matching fails instead of passing everything. 4/4 mutants killed — two of them the guard-the-guard's own red; dll_core_test 332/332, dll_helpers_test 2752/2752; UI 5298/5298 |

#### Live-check backlog — run at the end of the pass

| # | row | the check | needs |
|---|---|---|---|
| L1 | `[W1-QUOTA-UNLIMITED]` | 1. On the AOT build, pick *Unlimited* and restart the UI. `experimental.json` must hold `"snapshotQuotaMb": 0`, and the combo must still read Unlimited. 2. Capture once: no snapshot may be FIFO-deleted. ✅ **PASSED 2026-09-12** — AOT `dist\UE5DumpUI.exe` (55.0 MB, sha `e278e02a`) against DumperTest Shipping, DLL `1.0.0.3546`, 24,497 objects. Picking *Unlimited* rewrote `experimental.json` `{"enabled": true, "snapshotQuotaMb": 5120}` → `"snapshotQuotaMb": 0` immediately; it survived a clean `WM_CLOSE` + relaunch with the combo still reading Unlimited. Snapshot *2026-09-12 08:07:06* (1034 objects / 19,765 fields) still listed afterwards; `snapshots.5516D015081C5000.db` 4.23 MB holding exactly ONE row (`id=1, is_usable=1, partial_reason=''`); usage reads "4.2 MB (no limit)". Nothing FIFO-deleted. | UI + any game |
| L2 | `[W1-SNAP-FAULT]` | Arm a faulting object-decryption stub during a snapshot capture. Adapt `tools/verify/sw1_worker_fault.py`, and note that the armed scan must be the process's FIRST parallel scan. Then check that the snapshot shows as ⚠ unusable, is excluded from SPC/Pivot, and that the status names a worker FAULT, not a deadline. ✅ **PASSED 2026-09-12** — fresh DumperTest Shipping (pid 30208, DLL `1.0.0.3546`, 24,497 objects) + the AOT UI. **Control first:** an unarmed capture finalised `id=2, 1700 objects / 60,089 fields, is_usable=1, partial_reason=''` — clean captures stay usable, so the armed result below is not "everything is marked unusable". **Armed:** `id=3, object_count=0, field_count=0, is_usable=0, partial_reason=''` (correctly NOT `cap`/`disklow` — a fault lands in `is_usable`, per `Constants.cs:278-282`). Status text verbatim: *"Captured 0 objects, 0 fields — ⚠ a DLL scan worker FAULTED on part of the object list; the snapshot is partial and marked UNUSABLE (excluded from SPC/Pivot, auto-removed before next capture) — the DLL's logs name the fault"* — contains FAULT, contains no "deadline". `offsets-0.log` carries **16** `ParallelGObjectsScan: worker tid=N [a,b) faulted` lines, tid=0–15 covering `[0,8192)` with no gap — one whole chunk — timestamped 10:04:12.607–608, bracketed by this rig's own `Custom decryption function SET` 10:04:06.456 and `CLEARED (identity)` 10:04:51.458. Only that one chunk was attempted, so the capture really did **stop** at the faulted chunk. Excluded from the **SPC Query** sequence picker, the **Class Pivot** "Snapshots (tick 2-4)" + Pivot-target pickers, and the Diff Old/New pickers — all three list only 08:07:06 and 10:01:37. ⚠ **Two honest limits.** (1) The capture was **EMPTY, not partial**: the window is only ~150 ms (4 chunks, 10:01:37.982 → .105, because `SnapshotChunkSize >= ScanThreadCount`'s 8192 threshold), which cannot be entered from outside the process — a first attempt armed 6 s late because `sw1_worker_fault.py` resolves the module BEFORE counting its `--delay`. So the arm has to precede the capture, and then all 16 workers of chunk 1 fault. The "some rows survived" variant is therefore NOT exercised. ✅ **CLOSED 2026-09-12 on EVERSPACE 2 with a save loaded** — the partial variant now exists. ES2 (UE **506**, pid 37112, **1,154,902 objects**; its stale `version.dll` proxy was renamed aside first so only the current build could load, then `dist\UE5Dumper.dll` was injected — `load_mode: injected`). The scale is what makes it possible: **142 chunk calls over 47 s** (12:27:58.638 → 12:28:45.603) instead of DumperTest's 4 chunks in ~150 ms, a window ~310× wider, so the arm lands mid-capture by construction rather than by luck. **Control (unarmed):** 39,265 objects / 2,590,603 fields, `is_usable=1`, 727 MB. **Armed:** decryption pointer SET 12:30:05.709 → CLEARED 12:30:13.711, with **16** `ParallelGObjectsScan: worker tid=N [a,b) faulted` lines (tid 0–15 covering one chunk's `[0,8192)`) at 12:30:08.457, inside that bracket. **Result — a genuine PARTIAL:** `object_count=38,682`, `field_count=2,580,486`, **`is_usable=0`** — i.e. **0 < 38,682 < 39,265**, rows really survived AND the snapshot is still flagged, with the ⚠ badge and the FAULT status. (The arm landed 43 s into the 47 s capture rather than the 12 s I aimed for — the Bash call started after the click — which only makes the point sharper: 98.5% of the control's rows were kept and it was still finalised unusable.) (2) The row's "armed scan must be the process's FIRST parallel scan" is inherited from the value-scan rig, where the no-fault-on-a-second-scan effect was attributed to ScanForValue's index builder; `CaptureSnapshotChunk` has no such reuse. This run was a fresh process regardless. Rig: `l2_arm_now.py` (arm-first-then-click, address from `l2_arm.py --dry-run`; it is per-process). | DumperTest + UI; no CE |
| L3 | `[P4-OTHER-INSTANCE]` | Open two actors of one class through Instance Finder: A, then B, without clicking 🌍 GWorld in between. Drill a struct row on B. The breadcrumb address must equal B's base + the offset, not A's. Edit one struct field and read it back on B, and A must be unchanged. **Scroll check:** on one object with Auto on, scroll down, and the grid must NOT jump to the top on a tick. ✅ **PASSED 2026-09-12** — DumperTest Shipping, DLL `1.0.0.3546`, AOT UI. `StaticMeshActor` has 31 live instances; opened A = `0x29254B67C00` (index 24370) then B = `0x29254B67EC0` (index 24368) through Instance Finder with no 🌍 GWorld in between. **Addressing:** with B open, `PrimaryActorTick` (StructProperty, 0x28) resolved to `0x29254B67EE8` and `bNetTemporary` (0x58) to `0x29254B67F18` — B's base + offset both times, never A's; every one of 16 `walk_instance` RX frames echoed `"addr":"0x29254B67EC0"` / `"struct_data_addr":"0x29254B67EE8"`. **Write:** `bCanBeDamaged` (0x5A, bit 2, mask 0x04) false → true produced exactly ONE `write_mem` in the whole session — `{"addr":"0x29254B67F1A","bytes":"24"}` → `ok:true`. A's corresponding byte address `0x29254B67C5A` appears **zero** times in the log. An independent pipe re-read of both instances afterwards: **B `hex=24` / true, A `hex=20` / false — unchanged**, and B's sibling bits in the same packed byte (`bBlockInput`, `bCollideWhenPlacing`) still false, so the read-modify-write set bit 2 and nothing else. **Scroll:** with Auto on at 6 s, 7 ticks before the write (ids 102–114, 08:17:56 → 08:18:32) and 9 after it, all with the grid held at 0x59/0x5A — it never jumped to the top, and the toolbar showed "paused (editing)" during the edit, confirming `IsEditing` suppresses the tick (LiveWalkerViewModel:5688). The row-reuse gate at `LiveWalkerViewModel.cs:6993` is what permits the hold. | DumperTest + UI; no CE |
| L4 | `[P4-GUESS-SHIFT]` | Guess? on, and Auto on, on a DumperTest object whose gap holds a float that changes (e.g. a draining stat). Scroll down. On ticks where the value moves between clean (100.0) and non-clean (87.3), the grid must stay put. Where the guessed layout really moves, every row's Address must still be its own base + Offset. ✅ **PASSED 2026-09-12** — DumperTest Shipping (pid 34812, DLL `1.0.0.3546`) + AOT UI, on `DumperTestActor_0` @ `0x24F5D41E020` (index 24464), Guess? on, Auto 6 s. The gap float is `?0x6B4_float` = the fixture's non-UPROPERTY `RawFloat_Ticking`, driven 300.25 − 3.25/s and wrapping at ≤ 3.25 (~92 s cycle), flanked by `?0x6B0_I32` (`RawInt_Ticking`, +7/s) and `?0x6B8_double`. **Half 1 — stays put:** layout stability was measured over the wire with a key that mirrors `HasSameRowLayout` exactly — it trims a trailing `?` for guessed rows, so `Float?` ↔ `Float` is the SAME layout — giving **0 layout changes in 29 transitions** over 30 s while 7 values moved every tick and the float crossed the clean values 232 / 219 / 206 / 193 / 180. In that verified-stable window the grid held at `0x661 bPlainBool` across **3 consecutive Auto ticks (21 s)**. **Half 2 — a real move:** the layout genuinely moves when the float drops below ~27, where the guesser reads the 8 bytes at 0x6B0 as one `?0x6B0_double` instead of `?0x6B0_I32` + `?0x6B4_float`, splitting back on the wrap. That window is only ~8 s per ~92 s cycle, so it was **held deterministically** by writing `10.0` to base + 0x6B4 every 0.35 s (`l4_hold_merged.py`) — gating the state rather than racing it. In the held moved layout every row's Address was its own base + Offset (0x6A8 → `…6C8`, 0x6B0 → `…6D0`, 0x6B8 → `…6D8`, 0x6C0 → `…6E0`), and the merged row's value `524288.0001` is exactly those 8 bytes (`RawInt_Ticking` + `0x41200000`) read as a double — i.e. the row showed its OWN data, not the neighbour's value-and-address the fix's comment warns about. ⚠ **Correction to my own first reading:** the "grid jumps to the top" seen before the layout was measured is NOT a defect — it is the legitimate rebuild when `HasSameRowLayout` correctly returns false on a real move. ⚠ **A vacuous check was discarded, not reported:** an earlier pipe-side "address check" compared **zero** fields, because `walk_instance` carries no per-field address (the UI computes the Address column itself as base + the row's own Offset) — it printed a pass over nothing. The addresses above were read from the grid instead. | DumperTest + UI; no CE |
| L5 | `[W1-CONTAINER-STALE]` `[P4-CONTAINER-BASE]` `[P4-PTRCLASS]` | On a DumperTest actor with a TArray, a TMap and a TSet that change at runtime (e.g. an inventory):
1. **Refresh:** the previews and hovers update; a CE XML and a CSX export taken after the refresh carry the NEW values.
2. **Drill without a Refresh:** grow the array past its capacity so it reallocates, then drill it with NO Refresh. Element addresses must sit in the new buffer: compare with CE's view of the array's data pointer. Edit one element and read it back.
3. **Pointer retarget:** retarget a pointer to another class and export with drilldown ≥ 1; the target must be walked with its own class. | DumperTest + UI; CE only to cross-check the data pointer — announce first |
| L6 | `[A4-EDIT-STALE-PENDING]` | On DumperTest, with a float that the game changes:
1. **Reopen without typing:** type 250 into it and commit. Once the game has moved the value, double-click the cell and press Enter WITHOUT typing. There must be no "Written:" status, and the value stays the game's.
2. **Escape variant:** edit, press Escape, reopen, press Enter. Nothing is written.
3. **Control:** typing the value the cell already shows DOES write.
Watch the `IsEditing` latch experiment (UNDECIDED, same loop) in the same session. ⚠️ **PARTIAL 2026-09-12 — steps 1 and 3 PASSED, step 2 NOT RUN.** DumperTest Shipping (pid 34812, DLL `1.0.0.3546`) + AOT UI, on `F32_Ticking` (FloatProperty, 0x6A0 @ `0x24F5D41E6C0` = base + 0x6A0), which the fixture drives −10.25/s and wraps at ≤10.25. Auto was turned OFF and Refresh driven by hand, so the grid could not move under the cursor mid-edit. **Step 1 — reopen without typing:** typed 250 and committed (`write_mem 0x24F5D41E6C0 = 00007A43`, status *"Written: F32_Ticking = 250"*, `EDIT … = 250`); let the game move it (Refresh showed 344.5, the game's, not 250); re-opened the cell, clicked into the editor so the TextBox genuinely had focus, and pressed Enter **without typing** → **no write** (`write_mem` stayed at 2, `EDIT` stayed at 2), the status line was EMPTY, and the value stayed the game's 344.5. **Step 3 — the control:** the identical open-and-focus procedure, but typing the value the cell already showed, DID write (`write_mem` 1 → 2, bytes `00803443` = 180.5f, status *"Written: F32_Ticking = 180.50"*). That control is what makes step 1 non-vacuous: same procedure, the only difference is whether anything was typed. ⛔ **Step 2 (the Escape variant) could NOT be driven and is NOT claimed either way:** synthetic Escape never reaches the cell editor — pressed twice with the caret verifiably inside the box, the editor stayed open still holding `777`. An earlier attempt that appeared to write `7770` is explained by that (the editor never closed, so Enter committed the box that was still open), NOT by a stale pending value, so it is **not** evidence of the defect. Step 2 needs a human keystroke, or a test seam. Two environment behaviours worth knowing, neither a product finding: clicking another row **commits** an open editor (wrote 777), and a `Padding` row refuses editing outright (an editor opens but no write and no `EDIT` entry follow). **`IsEditing` latch (UNDECIDED):** observed suppressing auto-refresh — the toolbar reads "paused (editing)" — and releasing cleanly afterwards; no stranded latch in this session. | DumperTest + UI; no CE |
| L7 | `[A4-NAV-BACKFIRST-GRAFT]` | On DumperTest, on a slow-to-walk object (a large actor, or several levels deep):
1. **Back:** press Back and, before the grid changes, click → on a row of the level you just left. It must refuse with "belongs to the view you just left", and Copy CE XML afterwards must carry the RIGHT chain.
2. **Repeat** for a breadcrumb jump, Forward and Parent.
3. **Control:** ordinary drills, drills inside a container view, and the Find Refs owner auto-drill all still work. ⚠️ **PARTIAL 2026-09-12 — step 3 PASSED; steps 1-2 NOT RUN, and DumperTest is the wrong host for them.** DumperTest Shipping (pid 34812, DLL `1.0.0.3546`) + AOT UI, Guess? on, Auto off. **Step 3, all three controls pass:** *ordinary navigation* — drill `Payload` → `DumperTestPayload`, address-box `Go`, `Back` ("Returned to the previous view"), `Forward`, `Parent` (`DumperTestActor_0` → its Outer `PersistentLevel`, Outer shown as `World ThirdPersonMap`) and a breadcrumb jump all work; *container view* — `Arr_TuneBlocks [3 x DumperTestTuneBlock]` lists [0]/[1]/[2] at `…A660`/`…A678`/`…A690`, a 0x18 stride matching the offsets, and drilling element **[1]** opens `BlockName = Tune_1` (the right index) at `0x24EB097A678` with `Tunes` at `…A680` = +8; *Find Refs owner auto-drill* — "References to DumperTestActor_0 (4) [scanned 24497/24497 in 51ms]", and Open on `DumperTestSubsystem.SpawnedActor` navigated to the owner with the status *"Opened DumperTestSubsystem — held the previous object in 'SpawnedActor'"*, selecting the holding row at `0x24EA8F08478` = base + 0x38 whose ptr is `0x24F5D41E020`. ⛔ **Steps 1-2 (the Back / breadcrumb / Forward / Parent races) could NOT be driven here, and this is a FIXTURE limit, not a pass or a failure.** The row asks for "a slow-to-walk object", and DumperTest has none: the largest object (`DumperTestActor`, 189 fields with gaps) walks in **4.4 ms** measured over the pipe, and even a 1049-field walk took 6.8 ms. Three attempts at clicking → on the departing view inside one `computer_batch` (two clicks back-to-back, no model round trip between them) all landed on the ALREADY-REPAINTED destination. Loading the DLL with continuous 265 ms parallel scans did not widen the window either, which incidentally shows Fern does **not** serialise command dispatch across connections. ⚠️ **CORRECTION 2026-09-12, measured — my own "move it to ES2" advice was WRONG.** A bigger game does not widen this window: `walk_instance` costs time proportional to the OBJECT's field count, not the game's object count. On EVERSPACE 2 **with a save loaded** (1,154,902 objects, UE 506) the fattest walks measured were `SkeletalMeshComponent` **6.5 ms** (404 fields) and `PlayerController` 6.0 ms (208 fields) — against DumperTest's 4.4 ms. Eleven times the objects bought ~2 ms. What *does* scale with game size is the reference scan: `find_refs_to_uobject` 51 ms → **506 ms**, `find_functions_by_class` → 106 ms. ⬜ **So steps 1-2 need a different lever, not a different game:** either a navigation whose destination is a REFS-backed view rather than a plain walk, or a test seam that can hold the destination walk open. | DumperTest + UI for step 3; **steps 1-2 are not host-limited — see the correction above** |
| L8 | `[A4-PARENT-CRUMB-VTABLE]` | On DumperTest:
1. **Outer off the spine:** open an actor through Instance Finder, press Parent, then Copy CE XML. The table must be anchored on the parent's own address, with no `+0` dereference of the actor. Load it in CE: the records read the parent's real fields, not vtable garbage.
2. **Outer on the spine:** from GWorld, drill Actor › RootComponent and press Parent. You land on the Actor crumb, and Forward returns to RootComponent. Copy CE XML stays GWorld-rooted (restart-stable). | DumperTest + UI + **CE to load the table — announce first** |
| L9 | `[A3-BOOL-NATIVE-NOWRITE]` `[A3-FIRE-STRUCT-BOOLMASK]` `[A2-STRUCT-PREVIEW-BOOLMASK]` | On DumperTest, with the NEW DLL:
1. **Native bool:** edit a Blueprint bool in Live Walker. It flips in the game, the row reads the new value, and the status says "Written".
2. **Packed bitfield:** edit a `uint8 b:1` bool. Only its bit changes; read the sibling bools back in CE or via a Refresh.
3. **Unresolved:** a bool whose mask shows as 0 must be REFUSED with the reason.
4. **Preview and invoke:** a struct holding two packed bools (e.g. `FHitResult`, or a DumperTest struct) previews each bit separately. FIRE with both ticked sets both bits. Copy AA Script run in CE sets both bits too.
5. **Read-back:** a field the game recomputes each tick reports the read-back mismatch, not "Written". | DumperTest + UI; **CE for the Copy AA Script and bit read-back steps — announce first** |
| L10 | `[P3-INVOKE-Y11-CEFORM]` `[P3-INVOKE-STRUCT-FSTRING]` | On DumperTest:
1. **CE invoke form:** generate it for a UFunction that takes a `TArray` (or a delegate) and one that takes an `FText`. The labels read `EMPTY ONLY` / `CANNOT BE SENT`. Type `5` into the TArray box and press FIRE: the form says "nothing was sent", stays open, and the game logs no call. Clear it to `0`, press FIRE, and the call goes out with an empty array. The FText function refuses every FIRE.
2. **App FIRE:** on a struct param that has an `FString` member, typing text into that member refuses with its name. Leaving it empty fires, and the callee sees an empty string, not a garbage pointer. | DumperTest + UI; **CE for step 1 — announce first** |
| L11 | `[A2-UFUNC-TAIL-4X]` `[A3-CEFORM-4X-STALESLAB]` | On a **4.11-4.17** title (NEKOPALIVE 4.11 or Extinction 4.15, per `test-games.md`), with the NEW DLL:
1. **ParmsSize:** list a class's functions. A UFunction with params reports a `parms_size` of at least its last param's offset + size, not a small number equal to its param count, and `num_parms` matches the param list.
2. **Invoke:** FIRE a function that has an out-param or a return value. It completes, and the game survives several repeats; the old buffer was undersized inside the game.
3. **Param types:** in UProperty mode, the invoke dialog shows each Object param's class and each Struct param's struct name.
4. **CE form:** use Copy CE Invoke Script on a function with an out FString. The script's zero-fill loop covers the whole param span. Run it twice after a Freeze rescan: no crash. | a 4.11-4.17 title + UI; **CE for step 4 — announce first** |
| L12 | `[A2-TOPTIONAL-INTRUSIVE]` | On DumperTest **5.4** and **5.8** (the fixture's `Opt_*` fields; add `Opt_Str_Unset` if it is still missing), with the NEW DLL:
1. **Object optional:** set it to an actor, and the row shows the actor. After `Reset()` it reads `(unset)` with no → navigation. Set it to null, and it reads `(set: null)`.
2. **Container optional (5.8):** an unset `TOptional<TArray<…>>` reads `(unset)` whatever its neighbour holds, and a set one reads set.
3. **String optional:** on 5.4, an unset `TOptional<FString>` reads `(unset)`, not `""`. On 5.8 the same holds through the intrusive sentinel.
4. **Find Refs:** while the optional is reset, Find Refs to the actor finds no hit on it; once it is set, the hit is back. | DumperTest 5.4 + 5.8 + UI |
| L13 | `[A3-DEPLOY-CANCEL]` `[A3-RADIO-MIDDEPLOY]` | In the Proxy Deploy panel, against two or three installed (NOT running) games:
1. **Cancel during Deploy:** select the games, Deploy, and click "Cancel operation" at once. The app stays up; the result reads `Deploy cancelled — deployed: N, failed: M`; the grid shows the games that WERE written as deployed.
2. **One game:** repeat with ONE game selected. Still no crash.
3. **Cancel during Remove and Refresh:** Remove reads `Remove cancelled — …`; a cancelled Refresh reads `Refresh cancelled` in neutral colour, not a red "Refresh failed".
4. **Radios:** during a Deploy, the four proxy-type radios are greyed out, while the LKG and foreign-overwrite checkboxes are not.
5. **Update All:** cancel an Update All mid-run. The rows of the games it had already written show the new version without a manual Refresh. | UI only, no game running |
| L14 | `[A1-COORD-RESURRECT]` `[A1-COORD-BACKUP]` | Teleport's coordinate library on any connected game:
1. **Clear all stays cleared:** save two or three entries (so a `.bak` exists), then Clear all. Reconnect, and restart the app: the library is still empty, and `…preclear.bak` holds the cleared entries.
2. **Corrupt main:** with the app closed, overwrite `teleport-coords.<game>.json` with garbage and start it. The library loads from `.bak`. Now Clear all: `…preclear.bak` holds the recovered entries, not garbage.
3. **Corrupt main, then a save:** corrupt the main file as in step 2, start the app and save any entry. A `teleport-coords.<game>.json.corrupt-<stamp>` file holds the garbage, and `.bak` still holds the good library. ✅ **PASSED 2026-09-12** — DumperTest Shipping (pid 23232, DLL `1.0.0.3546`) + AOT UI. The Shipping key `dumpertest-win64-shipping` had **no** library yet, so the run started from a clean slate; the pre-existing `dumpertest` (Development-key) files from 2026-08-12 were deliberately left untouched. Every assertion below was read off the **filesystem**, not the grid. **1. Clear all stays cleared:** saved 3 entries (`ThirdPersonMap 1/2/3` @ 900/1110/92.013) → main 808 B plus `.bak` 573 B. The Clear-all dialog states the mechanism itself (*"A .preclear.bak copy is written first, so this can be recovered by hand"*); after confirming, the **main file was REMOVED** and `…json.preclear.bak` (808 B) held all 3. A **reconnect** and then a full **app restart** both left the main file absent — the `.bak` did not resurrect it. **2. Corrupt main:** with the app closed, the main file was replaced by 61 B of garbage. On start the view log recorded **"Coordinate library: loaded 3 entries for 'dumpertest-win64-shipping'"** and the UI read (3) — recovered from `.bak`. Clear all then rewrote `…preclear.bak` (13:52) with **valid JSON carrying the 3 recovered entries, not the garbage**, and the garbage was quarantined to `…json.corrupt-20260912-055212465`. **3. Corrupt main, then a save:** corrupted again with a *different* 40 B string, started, saved one entry → library (4). The new garbage landed in **`…json.corrupt-20260912-055415002`** holding `### STEP3 GARBAGE -- not json at all ###` verbatim; the main file became valid JSON with 4 entries; and **`.bak` still held the untouched good library** (3 entries, still 808 B, mtime 13:47). The two quarantine files carry distinct stamps and their own garbage, so neither clobbered the other. | any connected game + UI |
| L15 | `[W3-CONSOLE-REINVOKE]` | On a game whose game thread can be stalled (a loading screen, or a pause long enough to exceed the invoke timeout):
1. **Timeout:** run a STATEFUL exec command from the Console tab (one that adds an item or spawns something) while the thread is stalled, so it reports the dispatch timeout. The status says "still queued … not re-sent", and when the game resumes the effect happens ONCE, not twice.
2. **Stale pin:** after a level change, a pinned command still self-heals (`re-resolved …`). ⚠️ **PARTIAL 2026-09-12 — the TIMEOUT half of step 1 PASSED; the "ONCE, not twice" half is NOT measurable on any fixture we have; step 2 not attempted.** **Host: DumperTest _Development_** (pid 30140, UE 504, 25,231 objects) — ⚠ recorded on `dev` alone, and it had to be: Shipping has **no live `UCheatManager`** (CDOs only), and ES2 Shipping is the same, while `dev` has `CheatManager` live at `0x15CF39FB280`. **The stall:** `suspend.py suspend-tid DumperTest 40012` — the UE game thread only (tid 40012, the process main thread). A whole-process suspend is the wrong instrument here: it stops Fern and Mimic too, so the DLL never picks the command up and you measure the wrong branch. **✅ Timeout half:** with the thread frozen, running `SpawnServerStatReplicator` from the Console gave exactly *"ProcessEvent error code -5 (game-thread dispatch timeout) — **still queued: it will run when the game thread is free (not re-sent)** (instance 0x15CF39FB280)"*, and the UI raised its "⚠ Game thread paused — the game isn't ticking … function-invoke features will time out" banner. **⛔ "Once, not twice" — the instrument is invalid, not the feature.** Baseline live `ServerStatReplicator` = 0; after the resume, still 0. **The control is what settles it:** the same command with the thread RUNNING returned *"ProcessEvent OK"* and still left **0** instances. So it reports success with no observable effect, and the UI says why: *"UCheatManager subclasses are often body-stripped in cooked Shipping (Result=0 + no in-game effect). Try a game-specific exec or BC function for verification."* Without that control I would have read "0 after resume" as "the queued command never ran" — a false failure. **No game-specific exec is available to replace it:** DumperTest declares **no** `UFUNCTION(Exec)` at all, and ES2's 93 execs are engine ones plus `ESGameInstance` achievement commands that must not be run. `Summon` needs a param, and the Console's inline path is unimplemented (*"inline args not yet supported — opening param dialog"*); from the command box it fails *"Either instance_addr or class_name is required"*, and from the row it is the same stripped CheatManager. ⬜ **To finish:** a fixture carrying a game-specific exec whose effect is countable — adding one to DumperTest would do it, since it has none today. ⚠ The UI's warning cites `memory feedback_ucheatmanager_stripped`, which does **not** exist in this machine's memory directory. | a game + UI |
| L16 | `[P3-SNAPNUM-ENUM]` `[W2-GROUPMATCH-ENUM]` | On a game with an enum-backed state field (weapon type, quest stage):
1. **Capture:** take two FRESH snapshots under the NumericAll capture scope (one captured before B12 keeps NULL enum values, so the numeric predicates skip it). The diff grid shows the enum as a number, not raw hex, and SPC `Increased` / `Exact` find it.
2. **Group Match:** a Snapshot Group Match with the enum's value in one slot (NumericAll) plus a neighbouring int in another finds the object. Under NumericNoByte the enum slot finds nothing, exactly as the live Group Scan does. ✅ **BOTH STEPS PASSED 2026-09-12.** DumperTest **Shipping** (pid 42036, DLL `1.0.0.3546`) + the **AOT-trimmed** UI (`dist\UE5DumpUI.exe` 55.0 MB, sha256 `e278e02a`; the non-trimmed one is ~107 MB), UE504 / 24,497 objects. ⚠⚠ **The obvious gate is the WRONG gate, and this is the trap to remember:** the capture has a SECOND, persisted axis the database does not record — the **`Type` / `numeric_family`** ComboBox beside Scope. `snapshots`' columns are `id, label, captured_at, pe_hash, game_session_id, ue_version, object_count, field_count, scope, is_usable, partial_reason` — **no family, no game_only, no auto_skip_noise, no native_c** (measured). A capture with Scope=NumericAll and Type=*Floats only* would store `scope='NumericAll'` and hold **zero** enum rows, so a scope-only gate passes on a corpus that cannot exercise the fix, and BOTH the positive and its control then come back empty. So all five controls were **read off the panel and recorded before capturing**: Scope=**NumericAll**, Type=**All numeric**, Game objects only=**ON**, Auto detect Engine/System noise=**OFF**, Native-C (raw)=**OFF**, Per-game quota=**Unlimited**; and the PRIMARY gate is a row count: **1,719 `EnumProperty` rows in EACH capture, every one with a NON-NULL `numeric_value`** — which is `[P3-SNAPNUM-ENUM]`/B12 itself, since pre-B12 those are NULL. **Arming:** `Grade` was written **10** (`0x0A`) before capture A and **200** (`0xC8`) before capture B, on the **LIVE actor only** — the CDO was left at `Elite`/`02` as an in-run control. ⭐ **10 and 200 are chosen so BOTH cells are unambiguous:** a pre-fix renderer shows `0A` → `C8` and the fixed one `10` → `200`, so no cell is one character from its broken form (a `2` → `7` pair would have rested half the evidence on a leading zero). **Uniqueness gate:** exactly **one** row on a DumperTestActor holds 200 in capture B — `Grade / EnumProperty / off 1634`. ⭐ The `FixedArr[1] = 200` collision a review warned about (`DumperTestActor.cpp:208` seeds `(i+1)*100`) **does not materialise, and now as a MEASUREMENT rather than an inference**: `FixedArr` is captured as a single `IntProperty` holding **100**, i.e. `FixedArr[0]` only — the capture never expands `ArrayDim`. **STEP 1:** the diff (Old=`L16-A-grade10`, New=`L16-B-grade200`, both 715 objects / 16,896 fields / NumericAll) reports **8 changed · +0 added · −0 removed**, and the row reads **`DumperTestActor | Grade | Old 10 | New 200`** — decimal numbers, **not** `0A`/`C8`. The CDO's `Grade` is absent from the changed list. SPC then: with `Compare=Any` + `Field=Grade` the query returns **4** rows (CDO `Grade 2→2`, CDO `WideGrade 192→192`, live `Grade 10→200`, live `WideGrade 192→192`); switching to **`Increased`** narrows it to **exactly 1** — the live `Grade 10→200` — and an **`Exact` 200** value filter likewise returns **exactly 1**. ⭐ The 4-row `Any` result is what makes the 1-row answers evidence: three unchanged enum rows were available to be wrongly kept. (`Join` auto-selected **In-session**, both captures sharing session `1DD428159E9` — the `[W1-PIVOT-SESSION]` behaviour, seen again here.) **STEP 2:** Value1 = **NumericAll** `Exact 200` + Value2 = NumericNoByte `Exact 1234567` (`I32`) → **`1 object(s) matched · scanned 715`**, Matched values **`Grade=200, I32=1234567`**. Flipping ONLY Value1's scope to **NumericNoByte** → **`No object holds all 2 values across 2 snapshots.`** ⚠⭐ **That empty control is NOT, by itself, evidence of the fix** — a fully pre-fix build returns empty under BOTH scopes, because `WidthBytes` had no `EnumProperty` case at all. The evidence is the POSITIVE. And to show the empty answer is the enum leaf being out of scope rather than NumericNoByte being dead, a third arm was run: Value1 = NumericNoByte `Exact 8899001122334455` (`I64`) + the same Value2 → **2 objects matched**, `I64=8899001122334455, I32=1234567`. ⚠ **Recorded in passing, worth its own row:** `WideGrade` — the deliberate **4-byte** `EnumProperty`, real value `Wide_Base = 24000 = 0x00005DC0` — is captured as **192 = `0xC0`**, its LOW BYTE. That follows `kNumericAll`'s own comment (*"enums are 1-byte in the overwhelming majority, resolved to UInt8"*), and DumperTest built `Wide_Base`/`Wide_Target` to share the low byte `0xC0` precisely to catch it — so a 4-byte enum is not findable by its real value in a snapshot. | a game + UI |
| L17 | `[W2-ORDEN-FINDENTRY]` `[W2-GROUPMATCH-WIDTH]` `[A4-AB4-UINT64]` | On a game with an actor that holds unsigned numeric fields (UInt16/UInt32/UInt64Property):
1. **Live Group Scan:** a two-slot group, `Bigger -5` plus the value of a known int on the same actor, finds it. The Bigger slot's **All fields** list includes the unsigned fields, and a Refine with the same values keeps the actor.
2. **Snapshot Group Match:** the same group over a NumericAll snapshot finds the same actor.
3. **Single-value scan:** `Bigger -5` over NumericNoByte now returns UInt64Property fields as well as UInt16/UInt32. ✅ **STEPS 1 and 3 PASSED 2026-09-12; step 2 still ⬜ (needs a NumericAll snapshot + the UI).** DumperTest **Shipping** (pid 42036, DLL `1.0.0.3546`, 24,497 objects, UE 504), driven over the pipe. ⚠⚠ **The fixture cannot host this row alone, and the workaround matters more than the gap:** DumperTest declares exactly ONE multi-byte unsigned UPROPERTY — `uint16 U16` (`DumperTestActor.h:319`); there is no `uint32` and no `uint64`. But the process carries the whole engine, and UE ships **`//Script/Engine/IntSerialization`**, a serialization test class whose ONE object holds every width at once (`UnsignedInt16/32/64Variable` at off 40/44/48, `SignedInt8/16/32/64Variable` at 56/58/76/64, `UnsignedInt8Variable` at 72). It is a CDO, and the value scan **deliberately includes CDOs** (`Aura.cpp:7605` — *"we want instances + CDOs, not the UClass entries themselves"*), so `game_only=false` reaches it. Nothing instantiates it at runtime; every field was restored to 0 afterwards. ⭐ **The era correction that decides what step 3 is even allowed to claim:** the only lookup a single-value scan exercises is `multiResolve` (`Aura.cpp:7548`), and `git log -L 7548,7556:dll/src/Aura.cpp` returns exactly ONE commit — **`c23ca1a3` (2026-08-16, AB4)**, a month before B13. So the UInt16/UInt32 rows below re-confirm **pre-existing** AB4 behaviour and are **NOT** L17 evidence; L17's step-3 assertion is the **64-bit half only**. **STEP 1 (live group scan)** — slot0 `Bigger -5` + slot1 `Exact 777777`, `game_only=false`: the host is found (1 candidate), and slot 0's **All fields** list is `UnsignedInt16Variable`/`UnsignedInt32Variable`/`UnsignedInt64Variable`/`SignedInt32Variable` — **all three unsigned present**. ⭐ **The list is only evidence because two fields are missing from it:** `SignedInt16Variable = -12345` and `SignedInt64Variable = -999` have REAL encoded Int16/Int64 entries for -5 and genuinely fail `> -5`, so their **absence proves the predicate is still being applied** rather than the slot having stopped filtering. (A first pass left every host field at 0, so all six leaves passed and the list proved nothing — that pass is discarded.) A `refine_group_scan` with the same values keeps the host (total 1 → 1). **STEP 3 (single-value, NumericNoByte)**, four arms, every one complete (`truncated` unset, `deadline_hit=false`, 0 native-C rows): **3a `Bigger -5`** total 78,036 → **UInt64Property 83** (witness `UnsignedInt64Variable = 123456`) — *this* is the `[A4-AB4-UINT64]` claim, since `IntegerMemberRange` had no UInt64 arm at all pre-B13 and "no verdict possible" kept the old skip; UInt16 118 / UInt32 303 are the c23ca1a3 behaviour, labelled not counted. **3b `Bigger 70000` (CONTROL)** total 1,210 → **UInt16Property 0** — 70000 is above `UINT16_MAX`, so NO UInt16 value can satisfy it and dropping them all is correct, while UInt32 10 / UInt64 2 survive. This is the arm that kills the obvious wrong fix: a blanket *"unencodable ⇒ always true"* passes 3a and **fails** 3b. **3c `Smaller 9223372036854775808` (= 2⁶³)** → **Int64Property 28**, including DumperTestActor's own `I64 = 8899001122334455` — the **Int64 arm of the same one-line table fix**, discriminating on this fixture today because `std::stoll` throws `out_of_range` on that literal, so pre-fix Int64 got no entry and every `Int64Property` was skipped. **3d `Smaller -9223372036854775809` (CONTROL)** → **all six integer types 0** (total 20, Float/Double only): the scalar is below `INT64_MIN`, no value can satisfy it, and nothing is returned. ⭐ **Internal cross-check nobody had to trust a claim for:** `Int64Property` is **27** in 3a and **28** in 3c, and the one-row difference is exactly `SignedInt64Variable = -999` — it fails `> -5` and passes `< 2⁶³`. ⚠ Every absence claim went through `PipeClient.check_complete` first. A first pass asked for `Bigger -5` with `max_results=50000`, got back `total=50000` — **the cap** — and **UInt64Property 0**, which would have been written up as a failure; the rarer control (`Bigger 70000`, total 1,210) returning UInt64 is what exposed the truncation. `max_results` has no clamp in `CMD_BEGIN_VALUE_SCAN` and is **per-thread** (`Aura.cpp:7569`). | a game + UI |
| L18 | `[W1-PIVOT-SESSION]` | With snapshots captured in an EARLIER launch of the game:
1. **Reconnect:** restart the game, connect, open Class Pivot and run a pivot on the default snapshot (the old one). Open in Live Walker, Copy Address and both Locates are greyed out.
2. **Current launch:** capture a new snapshot and pivot it. The four buttons are enabled, and Open in Live Walker lands on the object.
3. **Disconnect:** close the game. The buttons grey out. ✅ **PASSED 2026-09-12** — DumperTest Shipping (launch C, pid 34812, DLL `1.0.0.3546`) + AOT UI, with snapshots already on disk from two EARLIER launches (08:07:06 and 10:01:37). All three states were read from the same zoom of the same toolbar, so they are directly comparable. **1. Old-launch snapshot → greyed:** the Pivot target defaulted to *Snapshot 2026-09-12 10:01:37* (an earlier launch); pivoting `StaticMeshComponent` gave "1 groups from 36 instances · key=RayTracingGroupId", and with that result group selected **Open in Live Walker, Copy Address and both Locate icons were all greyed** — while **Run Pivot stayed bright blue** in the same toolbar, which is the contrast that stops this being "everything is disabled". **2. Current-launch snapshot → enabled:** captured *Snapshot 2026-09-12 11:40:16* (1,700 objects / 59,987 fields) in launch C, re-pivoted the same class, and the same four controls turned enabled (Open in Live Walker and Copy Address bright, both Locate icons purple-outlined). **Open in Live Walker landed on the object** — it switched tabs and opened `Default__StaticMeshComponent` (Outer `Package /Script/Engine`) with its field grid populated. **3. Disconnect → greyed again:** killing pid 34812 flipped the top bar to `Connect | Disconnected` and the same four controls went grey, Run Pivot still enabled. ⚠ Worth knowing for a re-run: leaving and re-entering the Class Pivot tab clears the result selection, so the buttons grey out for that reason alone — re-run the pivot and re-select the group before judging. | a game + UI |
| L19 | `[W1-SPC-JOINMODE]` | SPC's join mode across a restart:
1. **Auto In-session is not kept:** open the SPC tab with two snapshots from ONE launch (the combo shows In-session), close the app, and check `ui-options.json`: it holds `Strict`, not `In-session`.
2. **The fallback works after a restart:** restart, capture a snapshot in a NEW launch, and tick one old and one new pick. The combo moves to Strict.
3. **A user's Loose is kept:** pick Loose, restart: still Loose. ✅ **PASSED 2026-09-12** — DumperTest Shipping across two launches (D pid 17348, E pid 14104, DLL `1.0.0.3546`) + AOT UI, with the SPC tab's Session column making each snapshot's launch visible. **3. Loose is kept:** picked `Loose`, closed the app (`ui-options.json` rewritten 11:48:35 with `selectedJoinMode: "Loose"`), relaunched — the combo came back **Loose**. That also serves as the **control for step 1**: the writer is demonstrably not hardcoded to Strict. **1. Auto In-session is not kept:** on a FRESH app session with two same-launch picks restored (11:47:17 + 11:47:28, both session `1DD4268F17E`) and the combo untouched, Join auto-showed **In-session**; closing the app rewrote the file (mtime 11:51:19 → **11:52:48**, so it genuinely wrote) with **`Strict`**, not In-session. **2. The fallback works after a restart:** launch E captured *Snapshot 11:54:57* (session `1DD426A465`); ticking one old (11:47:17, `1DD4268F17E`) and one new (11:54:57, `1DD426A465`) moved the combo from In-session to **Strict** on its own, with the combo never touched. ⚠ **The trap that cost me two attempts, and anyone re-running this will hit it:** `AutoSelectJoinMode()` starts with `if (_joinModeUserOverride) return;`, and `OnSelectedJoinModeChanged` latches that flag the moment the user touches the combo — for the rest of that **app session**. So after any manual pick the auto never runs again, and In-session cannot be observed until the app is restarted. `JoinModeForOptions` maps In-session → Strict on write, and `RestoreJoinModeFromOptions` drops both Strict and In-session on read, so only an explicit Loose survives — which is exactly why the Loose control is the right one to build the evidence on. | UI + a game (for the second launch) |
| L20 | `[W1-DISCOVER-ARRAY]` `[W1-ARRAYCOUNT]` | Class Pivot on a game with a struct array that changes (an inventory, cargo, a party list):
1. **Discover:** capture before and after an action that changes one array element, run Suggest Targets, pick the `Array[N].Inner` row and press Use →. The source switches to Snapshot Array, the right array is selected, and the pivot shows the changed element's key.
2. **Element count:** the array-field picker's count equals the array's element count, not elements × inner props. | a game + UI |
| L21 | `[W4-RELATED-RACE]` | Related Objects on a connected game: hand off one object from Instance Finder and, while it loads, hand off a second one (or pick another detected candidate). The grid shows only the second object's graph under its header, and the busy indicator stays on until the second load finishes. ⛔ **NOT RUN 2026-09-12 — the race window does not exist, measured on the largest host available.** On EVERSPACE 2 with a save loaded (1,154,902 objects, UE 506) a Related load is **1 ms**: hand-off A `TX 12:35:44.276 → RX .277`, hand-off B `TX 12:35:45.026 → RX .027`. Two Loads fired back-to-back inside ONE input batch still landed **750 ms apart — 750× the load time** — so A had long finished before B began. The end state *was* the row's end state (only B's graph under B's header: `Self = BaseSkeletal0 : SkeletalMeshComponent`, 3 related objects, no trace of A) — but that is exactly what two SEQUENTIAL loads produce, so it is **not evidence** and is not claimed. The reason it is this fast: `get_related_objects` is a shallow Self/Class/Outer lookup taking only `addr` + `max_results` (`DumpService.cs:1016`) — there is no deep or refs-backed mode to slow it down. (The 506 ms scan measured on ES2 is `find_refs_to_uobject`, which this panel does not use.) ⬜ **Needs a test seam that can hold the first load open** — a bigger game cannot supply this window, and neither can faster clicking. | a game + UI |
| L22 | `[W4-LOOKUP-FILTER]` | Instance Finder on a connected game: search a class, type a keyword, then Look Up the address of an object the keyword does not match. The result shows; editing the keyword hides it with "1 hidden by filter", and clearing the keyword brings it back. ✅ **PASSED 2026-09-12** — DumperTest Shipping (pid 34812, DLL `1.0.0.3546`) + AOT UI. Searched class `StaticMeshActor` → **31 instances**, whose names split into `StaticMeshActor_UAID_…` and plain `StaticMeshActor`. **The filter is demonstrably live first, which is what makes the rest non-vacuous:** typing keyword `UAID` left exactly one row and reported **"30 hidden by filter"**. **1. The looked-up result shows:** with `UAID` still in the box, Look Up `0x24EA9FA7EC0` — a plain `StaticMeshActor` (index 24368) that does NOT match the keyword — and the row was listed and selected anyway, the "hidden by filter" note disappeared, the preview panel populated (`PrimaryActorTick` @ `0x24EA9FA7EE8` = base + 0x28 ✓), and the status read *"Exact UObject match [scanned (incl. deep descent) 24,497/24,497 in 101ms]"*. **2. Editing the keyword hides it:** changing the keyword to `ZZZ` emptied the grid and the count line reported **"1 hidden by filter"** in orange — the exact wording the row asks for. **3. Clearing brings it back:** emptying the keyword re-listed `0x24EA9FA7EC0 | StaticMeshActor | 24368` and cleared the note. | a game + UI |
| L23 | `[W4-BOOKMARK-DT]` | Live Walker on a connected game with a DataTable (items, weapons):
1. **Same game session:** open the DataTable, drill into its RowMap, save a bookmark, restart the APP (not the game) and load the bookmark. The row list comes back, and Refresh keeps it.
2. **After a game restart:** load the same bookmark. The status says the DataTable at its saved address is gone or has changed, not "the game may have restarted". ✅ **PASSED 2026-09-12 on EVERSPACE 2** (UE 506, injected). ES2 carries 77 `DataTable` instances; used `DT_ShipModules_MiningShip` @ `0x1D702BCFB30`. ⚠ **Worth knowing before hunting for RowMap:** `UDataTable::RowMap` is **not** a UPROPERTY, so a reflected walk over the pipe shows no RowMap field on any of the 77 — the UI synthesises it as a `DataTableRows` row at 0x30 (`{DataTable: 1 rows, …}`), and that synthetic row is what makes the drill possible at all. **1. Same game session:** drilled RowMap → `RowMap [1 x ShipModule]` listing `[0] base` @ `0x1D702BCFA80`; saved to bookmark slot 1 (the slot's label changed from "1" to **RowMap**, and `Bookmarks\bookmarks.0D281EF60A6D5000.json` was written, 1,150 bytes); **restarted the APP**, reconnected, clicked slot 1 → *"Bookmark 1 loaded"* with the breadcrumb and the row list restored, and a **Refresh kept it** (same breadcrumb, same row, same address). **2. After a game restart:** killed and relaunched ES2 (new pid; 79,830 objects at the title screen against 1,154,902 with the save, so unmistakably a fresh process), reconnected, loaded the same bookmark → **"Bookmark 1: the DataTable at its saved address is gone or has changed — re-create it"**. It names the DataTable at its saved address and says nothing about the game having restarted, which is the distinction the row asks for. | a game + UI |
| L24 | `[W3-BATCH-METHOD]` | Interesting Functions on a connected game: run the batch "Props" over a mix of native functions and Blueprint functions. A function the DLL could not analyse shows `n/a` in the Uses column, not `0`, and the status line counts them as NOT analysed. ✅ **PASSED 2026-09-12** — DumperTest Shipping (pid 34812, DLL `1.0.0.3546`) + AOT UI. Load gave **2,968 functions from 370 of 1,571 classes** (scanned 24,497); the ⚡ Props batch confirmed "Analyse 2968 functions?" and ran over all of them — a genuine mix, not a contrived one. **Status line:** *"Props done: 191/2968 use class fields (5 cached). ⚠ **17 of 2,968 function(s) were NOT analysed** (native code this build cannot disassemble, or a Blueprint with no readable bytecode) — their …"* (the tail is clipped at the window edge; `PartialResultNotice.BatchNotAnalysedClause` ends it `their "n/a" is not a 0.`). **The cell:** `ControlRigShapeActor::OnTransformChanged`, flags `BE,Event`, 1 param (96B) → Uses reads **`n/a`**, not `0`. **The control is inside the same run**, which is what makes this non-vacuous: of those 2,968, **2,945** were analysed by native disassembly and show a real `0` where they touch no class field (e.g. `GranularSynth::SetAttackTime`, `ModularSynthComponent::SetAttackTime`, both `BC,Native` → "Props done: 0/2 use class fields"), and **6** were analysed from bytecode and show real numbers (`ExecuteUbergraph_ABP_Manny_C` → `9 · MovementComponent,…`). So `n/a` and `0` are visibly different outcomes in one batch. **Cross-checked against the DLL independently:** replaying the panel's own call (`list_all_functions game_only=true` — the same one `InterestingFunctionsViewModel:474` makes) and running `walk_function_props` on each returned `disasm` 2,945 / `blueprint_no_script` **17** / `bytecode` 6 — the 17 matches the UI exactly, and `blueprint_no_script` is one of the three refusals `FunctionPropRef.NotAnalysed` keys on. ⚠ **A false trail worth recording:** the batch's own tooltip says *"Most rows show 0 (native funcs / no field access)"*, which reads as though native functions are the unanalysable ones. They are not — `Aura.cpp` analyses natives by x64 disassembly (`disasm`); the refusal is `blueprint_no_script`, i.e. FunctionFlags READ as non-native yet no script, which is what `BlueprintImplementableEvent`/`Event` stubs hit. Chasing native functions for `n/a` finds only honest zeroes. | a game + UI |
| L25 | `[W2-GRAVDIR-VERDICT]` `[W2-MS-PROMISE]` | On a UE5.4+ game, in the main menu or a loading screen (no pawn):
1. **Gravity Direction:** press ↻ and Apply. The badge stays Unknown and the text says to enter gameplay, never "needs UE5.4+". In gameplay the card works; on a pre-5.4 game it says Unavailable.
2. **No promises:** Apply Move Speed and both time levers. None says the override "applies once" a pawn or world exists. | a game + UI |
| L26 | `[W2-TPREL-MAP]` | A connected game with a Coordinate Library for the current map:
1. **Directional TP:** use TP facing. The Current Pose Map row keeps the map name, and the library rows keep their distances — no "⚠ different map (you are on '')".
2. **Fresh connect:** reconnect the UI and do NOT press ↻. The map shows at once. *Add from fields* saves an entry carrying the map (its Map column).
3. **Main menu:** connect with no pawn. The library rows are not flagged as another map's. ✅ **STEPS 1-2 PASSED 2026-09-12**; ⬜ step 3 not reachable on this fixture. DumperTest Shipping (pid 23232, DLL `1.0.0.3546`) + AOT UI, with a 4-entry library for the current map `ThirdPersonMap` (all `Dist 0`, the pawn standing on the saved spot). **1. Directional TP:** *Teleport forward* (Distance 100 uu, "Horizontal only" ticked) moved the pawn from X **900 → 1000.000** with Y/Z unchanged — so the step size is confirmed, not assumed. Afterwards the Current Pose row still read **`Map: ThirdPersonMap`** (kept, not blanked), and all four library rows kept their Map column and now showed **`Dist = 100`**, exactly the configured step. No *"⚠ different map (you are on '')"* anywhere. **2. Fresh connect:** Disconnect → Connect, and **↻ was never pressed** — the Map row read `ThirdPersonMap` at once. *+ From fields* then created `ThirdPersonMap 5` from the (0,0,0) fields and it **carries the map**, confirmed in three independent places: the grid's Map column, the detail line *"ThirdPersonMap 5 — ThirdPersonMap — 1,497 uu away"*, and the on-disk entry `map='ThirdPersonMap'`. ⬜ **Step 3 needs a no-pawn state on a game that ALSO owns a library for its own key.** DumperTest boots straight into `ThirdPersonMap` with a pawn and has no main menu, and the CheatManager pawn-destroying execs cannot supply one: Shipping has no live `CheatManager`, and in a cooked build its bodies are stripped (working-lessons §1.1a). The natural host is EVERSPACE 2 — its title screen genuinely has no live `PlayerController` — but its library would have to be built in-game first, so it belongs to a future ES2 trip. | a game + UI |
| L27 | `[W2-POSEATTACH-QUIETPOLL]` | The `[POSEATTACH-2026-09-10]` case: a pawn attached to a vehicle / mount / moving platform whose world-space read fails.
1. **Auto only:** turn Auto (0.5s) on and do NOT press ↻. A "⚠ parent-relative" chip shows beside the source chip, and it stays while other status messages come and go.
2. **Clears:** detach (or disconnect). The chip goes on the next read. | a game + UI |
| L28 | `[W5-CEXML-FSTRING]` | ⚠ Needs CE. A game with a `TMap` keyed or valued by an FString, and a struct array with an FString member:
1. **Map / member:** Copy CE XML and paste into CE. The key, value and member rows are CE Strings showing the live text (Unicode for FString; UTF-8 CodePage for FUtf8String).
2. **TArray<FString>** (with `[W5-STRARRAY-ELEMENTS]`, B23b): both a top-level Copy CE XML of the object and a Copy CE Field from inside the drilled array write CE Strings showing the live text. | a game + UI + CE |
| L29 | `[W5-STRARRAY-ELEMENTS]` | A game object with a `TArray<FString>` (a name list, tags, dialogue lines):
1. **Live Walker:** the array shows its elements, each with its text, and drilling in shows the text too, including past the Array Limit. Before B23b the drill showed hex with an empty value.
2. **Copy CE XML** (⚠ needs CE): each element is a CE String showing its live text. This is `[W5-CEXML-FSTRING]`'s TArray half.
3. **Export CSX:** each element is a pointer with a Unicode String child. | a game + UI (+ CE for 2) |
| L30 | `[A4-USMAP-ENUM-UNDERLYING]` | In the throwaway CUE4Parse console: parse an asset whose object has a non-uint8 enum UPROPERTY (a `: uint32` enum such as `ENiagaraCoordinateSpace`), once with our `.usmap` and once with a Dumper-7 one. The property values must match, and the properties AFTER the enum must too (the misalignment was what the old Byte underlying type caused). A `TEnumAsByte` field shows its enumerator name, not a number. | a game + UI + CUE4Parse |
| L31 | `[W3-XREF-CAP]` | A connected game with a hot field or class (a property most Blueprint functions touch, or a class many functions take):
1. **Dialog:** Find Funcs on it. With more than 200 hits, the status reads "200+ function(s)" and "[CAP HIT — only the first 200 are listed; more may exist]"; a deadline, if one also hit, is reported separately.
2. **Batch:** run Find Funcs in Property Search, Interesting Properties, Instance Finder and Game Class Filter over rows that include it. Its cell reads `200+ · …`, the status names the cap, and a re-run does not re-scan that row. ✅ **STEP 1 PASSED, STEP 2 PASSED IN INSTANCE FINDER, 2026-09-12** — EVERSPACE 2 with a save loaded (1,154,902 objects, UE 506, pid 37112, injected). **Host picked by measurement, not by guess:** `find_functions_by_class` was probed over 11 common classes; `Object`, `PlayerController` and `Class` each returned exactly **200** — i.e. sitting on the cap — while `SceneComponent` gave 111 and `Texture2D` 151. Used `PlayerController` @ `0x7FF4BCBF7840`. **1. Dialog** (Find Func on the instance row) verbatim: *"**200+ function(s)** take this class — scanned 28,644 funcs (**248+ matched**) over 1,154,902 objects in 102ms **[CAP HIT — only the first 200 are listed; more may exist]**"* — and **no deadline clause**, correctly, since none hit; the row only requires one "if one also hit". **2. Batch** (Instance Finder's ⚡ Find Func): the Funcs cell reads **`200+ · SetEyeTrackedP…`**; the status reads *"Find Func done: 1 classes scanned across 1 rows. ⚠ 1 of 1 class(es) reached the 200-result cap — their counts (shown as N+) are lower bounds: only the first 200 are listed, and more may exist"*; and re-running it gave *"Find Func done: **0 classes scanned across 1 rows (1 reused/cached)**"* — the row was **not** re-scanned. ⬜ **Not run: the same batch in Property Search, Interesting Properties and Game Class Filter** — they share this batch path, but sharing code is not evidence and they are not claimed. | a game + UI |
| L32 | `[W4-RELATED-STOPS]` | A connected game, the Related Objects panel:
1. **A normal actor:** the status is "N related object(s)." with no ⚠ clause.
2. **The row limit, where an object reaches it.** Not the PersistentLevel: this walk follows REFLECTED pointers only, and `ULevel::Actors` carries no UPROPERTY, so the level's actors are never reached, although each one's Outer is the level. Use any object whose list fills 128 rows (a candidate, not a promise: a World on a level-streaming game, through its `StreamingLevels`). There the status names the row limit ("full at its 128-row limit and more related objects exist") only if a further owned object was refused, and names no time budget unless the walk also timed out. If nothing reaches 128 rows, record the step as not reachable on this game, not as a failure: dll_core_test pins the cap (review 4). ✅ **PASSED 2026-09-12 on EVERSPACE 2 with a save loaded** (1,154,902 objects, UE 506, pid 37112, injected). Both steps read off the SAME status line, so they are a direct contrast. **1. Normal object:** a `SkeletalMeshComponent` (`0x1D93E487090`) → *"3 related object(s)."*, **no ⚠ clause**. **2. The row limit:** the candidate the row suggests — a World's `StreamingLevels` — does **not** reach it here (Worlds and Levels sampled returned <10), so instead of guessing in the UI I swept 2,400 objects spread evenly across the whole array over the pipe and asked `get_related_objects` with `max_results=200` for each. That found the real hosts: **`WG_Inventory_Slot_C` (a UMG widget) with 131 related objects** at `0x1DA9F7CC080` and `0x1DAE48F7910` (also `WG_ItemAttribute_C` 102, `WG_MenuAction_Icon_C` 74). Loading `0x1DA9F7CC080` gave exactly **"128 related object(s). ⚠ Not the whole graph: the list is full at its 128-row limit and more related objects exist."** — the truncation is real rather than coincidental, since the object independently measures 131 > 128. | a game + UI |
| L33 | `[W4-STRIDE-TENTATIVE]` | A connected game:
1. **Normal game:** `get_pointers` carries `item_detect: "detected"` with a validated count near its `item_detect_probes` (200; 100 when only a deep phase found items). No stride badge appears, and a Dump All meta line reads `"stride_untrusted":false`.
2. **A game on the forced static-stride path** (Obsidian-style UE 5.3): `item_detect` is `"forced"` and there is no badge.
3. A tentative or undetected game is rare. If one turns up, the orange "stride is a guess" badge names its validated count. ✅ **STEP 1 PASSED 2026-09-12** (steps 2-3 need other games — see below). DumperTest Shipping (pid 34812, DLL `1.0.0.3546`) + AOT UI. `get_pointers` returned `item_detect: "detected"` with `item_detect_validated` **200** of `item_detect_probes` **200** — not merely "near" its probe count but equal to it — alongside `item_layout_mode: "classic"`, `item_size: 24`, `item_packed: false`, `item_obj_offset: 0`. **No stride badge:** the top bar carried only Disconnect / *Connected — UE504 (24497 objects)* / Address / AOBMaker / Options / Export / Tools / version, which is correct by construction — `EngineState.IsStrideUntrusted` is `detect is "tentative" or "undetected"`, and `PointerPanelViewModel.ShowStrideGuessBadge` keys on it. **Dump All:** *Export → Dump All Metadata (.jsonl)* wrote `out\DumperTest-Win64-Shipping-dump-20260912-113630.jsonl` (3,872 lines, 9.9 MB) whose `kind: "meta"` first line reads **`"stride_untrusted": false`** beside `"item_detect": "detected"`, `"item_layout": "classic"`, `"object_count": 24497`, `"ue_version": 504`, `"dumper_build": 3546`. ⬜ **Step 2 needs Avowed** (Obsidian-style UE 5.3 forced static-stride) — not this fixture; ⬜ **step 3 is opportunistic** by the row's own wording ("rare. If one turns up") and cannot be staged on demand. | a game + UI |
| L34 | `[A3-B30-STALE-FLAG]` | **CE: announce first.** On a game with the dumper loaded, for BOTH the UI-generated inject record and `scripts/UE5CEDumper.CT`:
1. Tick the inject record and connect the UI.
2. File > Open the same table without merging.
3. Tick the reloaded record. It shows "already loaded and serving" and unticks itself.
4. **The UI must stay connected:** no `UE5_Shutdown` in the DLL log.
5. **Process switch** (review 4): with the record ticked, attach CE to another process and back; CE unticks the record without running `[DISABLE]`. Tick it again: it reads serving and unticks itself, and the UI stays connected. (The `.CT`'s cancelled-picker route is pinned by source. It is hard to reach live while the UI has recorded the DLL's folder.)
**Control:** a fresh CE session with the proxy serving, which gives the same message and no teardown. | CE + a game + UI |
| L35 | `[W3-DUNSTE-QUEUED]` | A game that idles when unfocused (the common case, per `Dunste.cpp`), with Fly on:
1. Tick **Noclip** in the UI. Focus leaves the game; wait over 5 s.
2. The DLL log reads "QUEUED (rc=-5 …)", not "NOT applied".
3. Still in the UI, untick Noclip (or turn Fly off). Return to the game.
4. **The pawn must stand on the floor:** collision ends ON, and both requests drain in order. ✅ **PASSED 2026-09-12** — DumperTest Shipping launched **`--idle`** (pid 25712, DLL `1.0.0.3546`) + AOT UI. **The precondition was measured, not assumed, and it needs the flag:** with the UI focused, a plain launch kept ticking (`TickCount` 2033 → 2037 in 4 s) while the `--idle` launch froze solid (`TickCount` 54 → 54 and `F32_Ticking` 447 → 447 over 6 s). ⚠ **The log file is `walk-*.log`, not scan or offsets** — `Dunste.cpp` declares `LOG_CAT "FLY"` and `Sein.cpp`'s table maps `{ "FLY", 3, LF_Walk }` ("Dunste fly worker (movement-related)"); grepping the wrong file here would read as a silent failure. **Fly ON first** (`[FLY] Fly tick …: noclip=0 collOff=0 mode=5(want 5)` — MOVE_Flying). **1-2.** Ticking Noclip with the UI focused, then waiting 10 s, produced *"Fly: SetActorEnableCollision(0) **QUEUED (rc=-5, the game thread is busy)** -- it will run when the thread drains; the record is committed so the next toggle undoes it"*, and **`NOT applied` appears 0 times** in the whole log. **3.** Unticking Noclip logged `Fly: noclip = 0` then *"Fly: collision restore deferred — game thread not responding; **retrying every tick until it does**"*. **4.** 26 s later: *"Fly: SetActorEnableCollision(1) **applied (rc=0)**"*, fly ticks back to `noclip=0 collOff=0`, **and an independent detector agrees** — reading the pawn (`0x1F9BFC031A0`) over the pipe gives `bActorEnableCollision = true` with Z = **92.0126**, the spawn floor height, so the pawn is standing on the floor rather than fallen through. ⚠ **Stated precisely:** there is no separate *"SetActorEnableCollision(0) applied"* line — the disable was queued and then **superseded** by the restore, which is exactly what the QUEUED message says happens ("the record is committed so the next toggle undoes it"). So "drain in order" holds in the sense that matters: the restore was never lost, never applied before the disable, and the end state is collision ON. Also worth knowing: `front_window front` reported `DID NOT TAKE` on the game, so the queue drained from the retry loop catching a tick rather than from a full foreground switch. **The mechanism behind "in order" is structural, not incidental:** both toggles reach the DLL through the single `fly_set` pipe command, neither does the collision work on the pipe thread (`SetNoclip` only flips a flag; the ~60 Hz fly worker notices `noclip != collisionOff` and invokes), and every invoke lands in Stark's **one `std::queue<InvokeRequest>` with one consumer**, drained front-to-back inside the hooked `ProcessEvent`. ⚠⚠ **The QUEUED line is a NARROW window, and a re-run that misses it will look like a failure:** both Fly paths pre-gate on `Stark::IsGameThreadResponsive()` (500 ms since the last hook fire), and that gate emits the *"deferred — game thread not responding"* line **instead of** ever attempting the invoke. So `QUEUED (rc=-5)` can only appear while the stall is still younger than 500 ms — i.e. the tick must go in promptly after focus leaves. That is exactly the shape seen here: the tick at 14:23:49 got **QUEUED**, the untick 40 s later got **deferred**. | a game + UI; no CE |
| L36 | `[A3-ST1-SUPER-DRAIN]` | ⚠ **`tools/verify/st1_queued_drain_sideeffect.py` cannot do step 2 as it stands** (review 5). It freezes, queues one invoke and resumes at once, and its PASS does not depend on this fix. Extend it, or drive step 2 by hand:
1. Freeze the game thread (`tools/verify/suspend.py suspend-tid`) and queue a `SetActorHiddenInGame` on an actor.
2. While it is frozen, make a direct invoke of a Native|Static UFunction on an actor, e.g. `APawn::GetMovementBaseActor`: over the pipe with `direct_call: true`, or through the mailbox (CE: announce first). Either reaches `UE5_CallProcessEventDirect`'s fail-open branch.
3. **`bHidden` must NOT flip while the thread is frozen.** It flips only after the resume, when the game thread drains. | a game (+ CE for the mailbox route) |
| L37 | `[W2-MARKER-PARENTREL]` | A game with an attached pawn (a vehicle, mount or moving platform) where the world-space read fails:
1. The pose card shows "⚠ parent-relative".
2. Save Marker 1. The status names the PARENT-RELATIVE read, and the row reads "⚠ parent-relative (not world)".
3. Reconnect the UI: the flag survives in the row (it lives DLL-side).
4. **Control:** a normal on-foot save gives a plain "Marker 1 saved.". | a game + UI; no CE |
| L38 | `[W2-TPREL-TRANSPORTS]` + `[W2-MARKER-PARENTREL]` (mailbox) | **CE: announce first.** Regenerate the CE records (contract 4):
1. On an attached pawn whose world read fails, fire the Save / Get current coords / BugIt records. Each shows the PARENT-RELATIVE message and keeps its window open.
2. A contract-3 .CT still runs against the new DLL (MIN 1).
3. A new record against an OLD DLL refuses with "update the DLL".
4. **Control:** an on-foot save closes cleanly. | CE + a game |
| L39 | `[A4-USMAP-CONTAINER-ENUM]` | A game with a `TArray<TEnumAsByte<E>>` UPROPERTY. Not on collision components, which declare none (review 5): the Engine's `FPredictProjectilePathParams.ObjectTypes` (GameplayStaticsTypes.h) is one, or a game's own:
1. Export USMAP and load it in FModel.
2. The array's elements show enum NAMES, and the properties after it in the same object stay aligned.
3. **Control:** a plain `TArray<uint8>` stays a byte array. | a game + UI + FModel; no CE |
| L40 | `[P1-GENAU-ABORT]` + `[A2-GNAMES-PTRSCAN-ABORT]` | ⚠ **The abort half is not reachable from the UI** (review 5).
- A disconnect never cancels `UE5_Init`. `trigger_scan` runs it on an unbound `RunScan` thread, whose `Tot::Requested()` reads only the per-command and shutdown flags, and the monitor sets the per-command flag only for a connection with a command IN FLIGHT. The 500 ms `scan_status` polls finish between breaks.
- A shutdown mid-scan tears the DLL down, leaving nothing to re-scan with.
- dll_core_test (GENAUABORT, with an uncancelled control for every sweep) and the recovery pin cover the abort paths.
**Live, the control only:** on a game whose GObjects needs the recovery path (Avowed-style) or whose GNames falls to the string-ref / pointer-scan tiers, an uninterrupted scan latches normally, with no "aborted" line. | a game + UI; no CE |
| L41 | `[P1-ENUMNAMES]` | Two games, because the halves need opposite ones (review 5):
1. **A game where UEnum::Names is not located** (the DLL log reads "DetectUEnumNames: FAILED"). Export USMAP. The FINAL status reads "USMAP exported — ⚠ enum member names are unavailable on this build …", and the log carries the same warning.
2. **A game where it IS located.** Disconnect the UI during the first enum-bearing walk (a class with many enum fields). The log reads "search cancelled … not latching FAILED". Reconnect: enum values resolve to names again, including the enums the cancelled walk touched. | a game + UI; no CE |
| L42 | `[A4-PUSHCE-UNPADDED]` + `[W5-CSX-DELEGATEPAD]` | **CE: announce first.** A UE 5.3+ CHECKED build (Development, e.g. DumperTest `dev`), on an instance with a multicast delegate field:
1. **Batch push:** select the delegate row and press "+CE Field (flat)". The record's address equals the per-row +CE's (the field address + 8), and it reads the InvocationList data pointer, not 0.
2. **CSX:** export the instance's CSX with drilldown 1 and load it in CE's Structure Dissect. A unicast delegate's leaf sits at its field offset + 8. A multicast's raw block sits at the field offset, and its "/ InvocationList" pointer at + 8 expands.
**Control:** a Shipping build, where both sit at the field offset. | CE + a game + UI |
| L43 | `[A4-DELEGATE-ARRAY-PAD]` | **CE: announce first.** A UE 5.3+ CHECKED build with a `TArray<FScriptDelegate>` field holding a bound delegate (DumperTest `dev`, if its fixture has one; otherwise record the check as not reachable):
1. `walk_instance` carries `array_elem_delegate_pad: 8` on that ArrayProperty, and no `delegate_pad` on it.
2. Copy CE XML of the instance. Each element leaf sits at `index * 24 + 8` from the dereferenced Data, and CE reads the bound object's FWeakObjectPtr there, not 0. A Copy CE Field with a fabricate count pads the extra rows the same way.
3. The CSX export's element leaves sit at the same offsets.
**Control:** a Shipping build, where the key is absent and nothing moves. | CE + a game + UI |
| L44 | `[P3-SDK-INNERS]` + `[P3-SDK-GUESSED]` | A connected game and the UI:
1. **Guessed rows:** in Live Walker, open an instance whose class shows "Guess?" rows and export its C++ header. No `?0x` name appears, and those bytes are `Pad_` members.
2. **Container inners:** export the SDK on a game with a `TArray<TSoftClassPtr<…>>`, `TArray<TLazyObjectPtr<…>>` or `TArray<FScriptDelegate>` field (find one in a class dump). The declaration spells the element type, not `uint8_t`. | a game + UI |
| L45 | `[P1-SEETHRU-NOPRODUCER]` + `[P1-SEETHRU-GIVEUP]` | A connected game with See-through:
1. **Enable** on a normal game. It turns ON (the producer probe passes), and the DLL log has no "refusing to enable".
2. **Give-up:** with an occluder hidden, pause the game (background it without Keep Foreground), turn See-through off in the UI, and keep the game paused over 5 minutes. Refresh: the card says the restore gave up and to turn See-through on and off again. Do that with the game running: the actor reappears, and the card reads plain OFF.
3. The -3 refusal is not reachable on a stock build; the source pin covers it. | a game + UI |
| L46 | `[P1-UPROP-DELEGATE]` | A UE4 < 4.25 game (UProperty mode) with a `TArray<FScriptDelegate>` or a multicast array:
1. Live Walker shows its elements, or its count, as on an FProperty title.
2. The refusal text ("(delegate array — unexpected … element size N, not read)") appears only for an ElementSize the readers do not recognise. A stock build does not produce one, so dll_core_test covers it. | a UE4 < 4.25 game + UI |
| L47 | `[P1-WALK-UNREADABLE]` + `[A4-REROOT-STALE-WARNING]` | A connected game and Live Walker:
1. **Unreadable:** open an object, let the game destroy it (a projectile, a UI widget that closes), then Refresh or re-open its address from the Go box. The status reads "⚠ This object is no longer readable (freed?)", not a blank grid.
2. **Freed across a re-root:** re-open a recycled address from the Go box or a cross-tab handoff after browsing elsewhere. The status keeps the freed/recycled warning AND "← Back returns to …". | a game + UI |
| L48 | `[P1-SPARSEDELEGATE-REFS]` | A UE 5.x game with sparse delegates, e.g. an actor bound through `OnActorBeginOverlap`:
1. Run Find References on an object bound only through a sparse delegate. With a readable storage the binding is listed; the `offsets` log line "had no readable InvocationList" names any delegate that was not read.
2. When that line appears, or the scan hits its deadline, the status must not say "likely held by a non-reflected pointer". | a UE5 game + UI |
| L49 | `[A2-WALKCLASSEX-UNMAPPED]` | No live trigger: the defect needs a transient read fault on a class pointer, which dll_core_test makes by decommitting a page.
1. **Regression only:** Class Pivot, Property / Value Search and a CE export still see every normal class's fields.
2. `walk-0.log` holds at most one "is not readable at +0x…" line per address. | a game + UI |
| L50 | `[A2-LAZY-LATCH-GUESS]` | A UE 5.0-5.2 game with a `TArray<TLazyObjectPtr>` (rare, so any lazy array will do):
1. Walk it in Live Walker. Every element's GUID is read, not only element 0.
2. `offsets.log` has at most one "TLazyObjectPtr payload envelope measured" line, whose ElementSize is the engine's own (0x1C on 5.0-5.2, 0x18 from 5.3). | a UE5 game + UI |
| L51 | `[A2-CRC-PATH-LS]` | A game installed under a folder with a non-ASCII name (a copy is enough), shipping a CrashReportClient:
1. `scan.log`'s "DetectVersion: CrashReportClient at '…'" line shows the path, not an empty record.
2. A proxy deploy there logs "Loaded real version.dll: …" with the path. | a game copy + UI or proxy |
| L52 | `[A2-HEAP-ANCHOR-TEXT]` | This PC, where `GWLD_V3` matches inside Bitdefender's `atcuf64.dll`, on a game whose GObjects falls back to the data scan:
1. `scan.log` says "Module anchor set on the HEAP".
2. The atcuf64 refusal reads "GObjects validated on the HEAP", never "never validated this run".
3. GWorld is still NOT taken from atcuf64. | a data-scan game + CE or proxy |
| L53 | `[A2-METHODE-MANUALMAP]` | **CE: announce it first.** On a throwaway target (DumperTest), with CE's plugin loaded:
1. Tick CE Settings -> "Always force load modules" and inject through the plugin. The message must be the new two-possibility text, naming the setting.
2. Untick the setting, restart the target, inject again. The normal "DLL injected" message must appear. | CE + DumperTest |
| L54 | `[A3-MIMIC-INIT-FASTPATH]` | **CE: announce it first.** A game with a CE mailbox hotkey (e.g. God Mode in the .CT):
1. Fire it repeatedly while `UE5_Init` runs.
2. A command landing after the "Module anchor set" line but before init's end must produce a "UE5_Init: init already in progress … waiting" line, then succeed first time.
3. No one-off DynOff error that succeeds on retry. | CE + a game |
| L55 | `[W5-DENKEN-DEADGUARD]` | No live trigger, and no behaviour change.
1. **Regression only:** a Live Funcs or Interesting Properties native-xref run finds the same fields it did before the removal, on any game. | a game + UI |
| L56 | `[A4-CDOSCOPE-ANCESTOR]` + `[A4-CDOSCOPE-NESTED-PREVIEW]` | A game and Property Search:
1. **Ancestor:** search `BaseEyeHeight`, a Pawn field, with live Characters present. The Pawn row shows "(subclass instance)", not "(CDO default)", and Freeze on it reports the same instances.
2. **Nested:** a Deep search that matches a direct field AND a nested `Slots[].Count`-style leaf of the same class. The nested row shows no preview. | a game + UI |
| L57 | `[A4-LW-DISCONNECT-PARENT]` + `[A1-DETECT-REPUBLISH]` | Two games (or one game restarted) and the UI:
1. **Live Walker:** root on an actor with an Outer, so Parent is enabled, and open its functions. Kill the game; reconnect to the other. Parent is disabled, there is no References header, and the Functions list is empty, including after typing in its filter.
2. **Detect Player Stats:** with the snapshot signal on, click Detect and kill the game mid-run. After the reconnect the panel shows the reset text and no rows. | two games + UI |
| L58 | `[A4-STEALTH-PRIME]` | A game where the stealth meter auto-finds (experimental gate on):
1. Detect, then Hold @0. Restart the UI with the game still running and reconnect. The Stealth card reads "Holding @0", and Reset releases it.
2. With only a Property Search Force active, the card reads "Unknown", and turning the experimental gate off does not release that Force. | a game + UI |
| L59 | `[A4-PIVOT-CROSSGAME-ID]` + `[W1-PIVOT-LOADCTS]` | Two games with a few snapshots each, one UI session:
1. In game A pick an older snapshot in Class Pivot, and tick picks in Snapshot and SPC.
2. Connect to game B. Each tab shows B's newest or default, and Class Pivot's class list is B's.
3. An Extra Scan on B keeps B's picks.
4. Switch snapshot and immediately pick a class. The class list still arrives for the new snapshot. | two games + UI |
| L60 | `[A4-GAMEONLY-ADVICE]` + `[P5-GROUP-ADVICE]` + `[A3-CONTAINER-4096-ADVICE]` | A game and the UI:
1. A capped Interesting Functions load (Game Only on) advises nothing about Game Only. Console with it off says `tick "Game Only"`.
2. A capped group First Scan advises more or more distinctive values, never "refine".
3. Open a `TArray<float>` over 4,096 long. The status says it is capped per fetch, not "raise the Array Limit slider". | a game + UI |
| L61 | `[W1-DT-TRUNC]` + `[P5-PIVOT-FETCHCAP]` | A game and Class Pivot:
1. **DataTable:** a table over 64 rows. Select it; the status says "(showing 64 of N)". Run; the status still says it.
2. **Fetch cap:** rare (about 800 owners × 256 elements × 10 ticked props). If one is reached, the status reads "≥ … groups … from ≥ …" plus the fetch-cap sentence, not "(capped at 5,000)". | a game + UI |
| L62 | `[W1-WINMM-LOADMODE]` | A game with the winmm proxy deployed (Proxy Deploy tab):
1. Connect. The load mode reads `proxy:winmm.dll`.
2. The per-game confirmed-proxy record now appears, as it does for version / dinput8 / dxgi. | a game + UI |
| L63 | `[W5-INSTEXPORT-TRUNC]` | A game, Instance Finder, and an instance whose CE XML export is huge (a dense object with Collapse Pointer Nodes off):
1. Copy CE XML. The status says "Copied, but TRUNCATED at the 60,000-entry export cap", naming Collapse Pointer Nodes and the DropDown Limit.
2. A normal instance copies with no warning. | a game + UI |
| L64 | `[W1-PIPEBUSY-LOG]` | **CE: announce it first.** Two Cheat Engine instances, both with the AOBMaker plugin, and the UI:
1. With the second CE holding the pipe, trigger any AOBMaker action (e.g. open the Interesting Functions tab). `init.log` has a WARN "…AOBMakerCEBridge EXISTS but no instance was free…", not the Debug "Cheat Engine not running".
2. With no CE running, the Debug line still reads "not running". | CE ×2 + UI |
| L65 | `[W1-GROUP-DENYLIST]` | A game with snapshots and the UI:
1. Hide a class in Diff mode's noise picker.
2. Switch to Group mode and match. The status says "1 class(es) hidden by the Diff denylist (switch to Diff mode to see or clear it)", on a no-match result too. | a game + UI |
| L66 | `[W1-PARTIAL-MARK]` | A game and the UI:
1. Set Max dataset to 512 MB and capture more than that. The saved-snapshots grid label ends "(partial: stopped at the size cap)", and so does the snapshot's line in every Diff / Group / SPC / Pivot picker.
2. Restart the UI. The marker is still there (it is persisted), and the snapshot is still listed (the auto-clean kept it).
3. An older DB opens without error and gains the column. | a game + UI |
| L67 | `[P3-SCORING-MCDELEGATE]` | Optional; needs a UE4 ≤ 4.22 game, and none is in the calibration set. In Interesting Properties, a `MulticastDelegateProperty` with a stat-like name ranks below the numeric field of the same name. | a UE4 ≤ 4.22 game + UI |
| L68 | `[W3-CAP-NOSAVE]` | UI only, no game:
1. Raise Property Search's Max and change nothing else.
2. Close the UI. `ui-options.json` holds the new value, and the value is back on relaunch.
3. Repeat with the Classes tab's Max. | UI only |
| L69 | `[W5-OFFSETS-UNMEASURED]` | CE and a game. ⚠ Announce CE use first.
1. On a game whose scan log says `validated=yes`, `UE5_GetOffsetsVerdict` returns 1, and `dissect.createFromClass` prints no warning.
2. On a build whose scan log says `validated=NO` (the UE 5.8 fixture), the first dissect prints exactly one `[UE5Dissect WARN] UE property offsets were NOT measured (…)` line. It names the same reason as `get_offsets`' `fallback_reason`, and a second dissect prints nothing.
3. After CE Disable, the verdict reads `probe-not-run` until the next Enable's scan finishes. | CE + a game |
| L70 | `[W2-BETWEEN-PREVIEW]` | UI only (the preview renders without a game):
1. In Value Search → Between, an FVector bound pair `1,2,3` / `4,5,6` shows no preview (it used to show `→ 123~456`).
2. Int32 `1,000` / `2,000` shows no preview.
3. An SPC absolute Exact `1,000` still previews `→ 1000`. | UI only |
| L71 | `[W2-DEADSCAN-LOADMORE]` | A game and the UI:
1. First Scan something with more than one page of results, so Load More shows.
2. Start another First Scan and Cancel it. The rows stay, Load More disappears, and the window status says they are the previous scan's.
3. Repeat in Group mode. | a game + UI |
| L72 | `[W3-DIP-PIXELS]` | UI on a HiDPI monitor (the AF21 rig's 3840 px at 225%):
1. Open a managed dialog (Find Func). Drag it until about a third hangs off the LEFT edge (x ≈ -1707). The right edge cannot show this, per AF21's correction.
2. Maximize it, then restore it. It comes back to that position instead of snapping to the last fully visible one. | UI + HiDPI |
| L73 | `[P8-BOOKMARK-TIP]` | A game and the UI, in Live Walker:
1. Bookmark object A into slot 1.
2. Navigate to object B, then ★ and slot 1 again.
3. Hover slot 1. It names B, and a click goes to B. | a game + UI |
| L74 | `[A1-LOG-RESUME]` | UI only:
1. Leave a `pipe-0_00N.log` in the UI's log folder. Generate one with a >8 MB session, or copy one in.
2. Start the UI. The file is archived under its own date, and the session writes `pipe-0.log`. | UI only |
| L75 | `[A3-RECYCLE-GUID-FAILOPEN]` | Needs a SUBST or RAM-disk volume. This path is unmeasured, and this check measures it.
1. `subst X: <dir>`, and put a leftover proxy DLL there.
2. Run Proxy Deploy's cleanup. It refuses to "recycle" the DLL, failing closed, instead of claiming "moved to the Recycle Bin".
3. On a normal fixed volume, the DLL is still recycled. | UI + a SUBST volume |
| L76 | `[A3-COORD-NONFINITE]` | UI only. In the Teleport card's coordinate library, import a CSV with a row whose x is `NaN`, and one whose x is `1e400`. The preview lists both as rejected rows on column x, and neither is imported. | UI only |
| L77 | `[W2-CEGEN-MODAL]` | CE and a game, with the GodMode and Debug Camera records. ⚠ Announce CE use first.
1. With the DLL not injected, untick each record. No dialog appears over the game. With `UE5_DEBUG=1`, the Lua Engine shows the dbg reason.
2. Tick one with the DLL not injected. The ENABLE dialog still appears, and the record unticks. | CE + a game |
| L78 | `[A2-CABI-TELEPORT-PARENTREL]` | CE and a game with a vehicle or mount. ⚠ Announce CE use first.
1. With the pawn attached, force the world read to fail if possible. `UE5_TeleportGetPoseEx` fills the pose and sets the int32 flag buffer to 1.
2. On foot, the flag reads 0.
3. Save a marker while attached. `UE5_TeleportGetMarkerEx` returns the same flag.
4. The three original getters still return the same values as before. | CE + a vehicle/mount game |
| L79 | `[W4-HEXSORT]` | A game and the UI:
1. Sort each Address column (Instance Finder's instances, container matches and fields; Live Walker's field Address and Ptr, Find Refs' Owner Addr and Functions' Address; Class / Struct's field Address) on a result set that mixes 12- and 13-character addresses. The order is numeric.
2. The Hex column still sorts as text, which is memory order. | a game + UI |
| L80 | `[A1-SLOTSYM-FAILED]` + `[A1-LUA-WAIT]` | CE and a game. ⚠ Announce CE use first.
1. Make two PointerQuery "Get GWorld" records. Tick A, which succeeds.
2. Tick B while its ENABLE fails (for example, before the DLL is injected). B unticks, and A's `[UE_GWorld]+offset` records still resolve. Untick A, and `UE_GWorld` is unregistered.
3. In CE's Lua Engine, run an emitted idle or status wait against a busy mailbox with `getTickCount` present. It gives up at the real millisecond deadline, not after N sleeps. | CE + a game |
| L81 | `[A4-AB4-BETWEEN]` | A game and the UI:
1. Value Search, NumericNoByte, Between -5 10 finds a UInt16 field holding a small value. It used to skip it.
2. Between 10 70000 finds an Int16 field near 32767.
3. Repeat both as a group slot, and as a snapshot Group match. | a game + UI |
| L82 | `[A2-TOPTIONAL-VALUESCAN]` | A UE 5.5+ game with a `TOptional<FString>` field, if one can be found. Value Search, FString, Exact "": an UNSET intrusive optional is not a candidate. A set optional holding text is still found. | a 5.5+ game + UI |
| L83 | `[W3-DEBUGCAM-QUEUED]` | Stall the game thread: unfocus a game that pauses its tick, with the foreground lock off. Console **Force ON** shows the amber Queued badge and "do not press Force ON again"; on refocus the camera turns ON **once** and stays ON. The CE Debug Camera record, ticked while stalled, shows the "queued" message and stays ticked; on refocus the camera is ON. | a game with ToggleDebugCamera + CE + UI |
| L84 | `[A2-TOPTIONAL-STRUCT-DESCENT]` | A UE 5.x game with a `TOptional<FStruct>` holding an actor pointer or array, if one can be found (the Property Search types filter shows `OptionalProperty`). **Find Refs** to that actor while the optional is SET: one hit. Reset it in game: no hit. **Address Finder** on an element of the array inside it: found while set, not after a reset. | a 5.x game + UI |
| L85 | `[W5-OFFSETS-MAILBOX]` | **CE:** with the DLL injected into a game whose scan log says `validated=yes`, run `getOffsetsVerdict()` in CE's Lua Engine: `true, ""`. Then on a game whose offsets fall back (or before any scan, in proxy mode): `false` plus the reason, and `probe-not-run` when nothing has probed yet. Against a contract-4 DLL the same call says `dll-too-old`, never `measured`. | CE + a game + an old DLL build |
| L86 | `[A2-SENTINEL-OVERREAD]` | Needs a 5.5+ game with an intrusive `TOptional<FName>` as an object's LAST field, which is rare enough that the offline pins may be the whole story. If one is found: Value Search, FName, Exact the held name — the SET optional is a hit on every instance of the class, not just on those whose allocation is far from a page edge. | a 5.5+ game + UI |
| L87 | `[A2-TOPTIONAL-REFINE]` | A game with a trailing-flag `TOptional<int32>` (UE4 or pre-5.5 shapes are the common ones): First Scan the held value, then reset the optional in game and run **Next Scan → Unchanged**. The row disappears. Control: with the optional still set, the same Next Scan keeps it. ✅ **PASSED 2026-09-12** — DumperTest **Shipping** (pid 42036, DLL `1.0.0.3546`, 24,497 objects, UE 504), driven over the pipe. ⚠ **The "pre-5.5" framing is not a constraint:** UE **5.4** reflects `TOptional<int32>` as an `OptionalProperty` and it is a **trailing-flag** one, so our standard fixture hosts this row — `walk_instance` on the live `DumperTestActor` gives `Opt_Int_Set` at off **1528**, `hex=`**`6860000001000000`** = value 24680 (`0x6068`) at +0 and **`bIsSet` at +4**. ⭐ **Writing that flag byte IS the row's "reset in game", not a stand-in for it** — `MarkUnset` is defined as *clear the flag, zero nothing*, which is exactly what the defect turns on; and it is STRICTER than a gameplay action because the value bytes can then be **proved** untouched. Three arms, all required: **A (control)** optional still SET → `Unchanged` refine **keeps** it (session total 2 → **2**, `Opt_Int_Set` present at `0x2875531E618`). **B (armed)** write `00` to `0x2875531E61C`, nothing else → the same `Unchanged` refine **drops** it (total 2 → **1**). **C (instrument)** the armed refine **kept 1 other candidate**, so it did not merely empty, and the value bytes at the dropped address are **byte-identical to baseline** (`hex=6860000000000000` — only byte 4 moved). ⭐ **C is what makes this red-before-green without a pre-fix DLL:** the value did NOT change, so the old address-only re-read would have evaluated `Unchanged` as **TRUE** and kept the row; being dropped is a behaviour only the new `optionalFlagOffset` gate (`Aura.cpp:8581`) can produce. Also observed and worth keeping: the walk flips to `value=(unset)` while the bytes still read 24680 — the exact *"shows a value for a slot the engine considers empty"* symptom, now reported correctly. The candidate's `field_type` comes back as `IntProperty` (V1c emits the inner type on a Direct anchor), which is why the descriptor, not the field, has to carry the gate. Flag restored to `01` afterwards; the fixture was left as found. | a game + UI |
| L88 | `[A1-VERDICT-STALEMB]` | **CE: announce it first.** With the DLL injected, wedge the game thread (a loading screen, or a game that stops ticking unfocused) and call `getOffsetsVerdict()` in CE's Lua Engine until it times out. The NEXT `invokeUFunction(...)` must REFUSE with "the previous mailbox call timed out and the DLL is STILL holding the mailbox", not run. Once the game thread returns, the following call works without a re-inject. | CE + a game |
| L89 | `[A1-REVIEW6-PINS]` | **None.** Comments and a test guard only — nothing observable on a running game. Recorded so the backlog's row count and the ledger stay in step. | — |

#### Batch plan — the inventory of 2026-09-11

**Source:** a 7-agent inventory of every open confirmed row: five region readers, a grouper and a
completeness critic.
- **Reconciliation:** every region reconciled EXACTLY with its recorded totals.
- **Total at inventory time:** 96 open rows (33 MED · 63 LOW). The staleness trio has since landed,
  and two residuals were filed. That leaves **95 − 3 = 92 open**, and derive it again before you
  quote it.
- ⚠ **Critic corrections applied:**
  - `[A3-PTR-NAV-REPAINT]` belonged IN the staleness bundle and was missed. It landed as that
    bundle's follow-up.
  - The two FP residuals are now filed (`[W2-POSEATTACH-QUIETPOLL]`, `[W2-TPREL-TRANSPORTS]`).
  - Two stale markers were fixed (`[W1-GATE-JSONDEFAULT]`, and the Order item 3 "A4 next").
- **Grouping:** by shape, with every recorded "land together / fix before" coupling kept. The full
  per-row fields are in the inventory JSON (session scratch); the durable facts are the rows
  themselves.

**MED batches, in fix order** (✅ landed · ⬜ open; CE = its live check needs Cheat Engine):

| batch | rows | CE |
|---|---|---|
| ✅ B01 same-object staleness | `[W1-CONTAINER-STALE]` `[P4-CONTAINER-BASE]` `[P4-PTRCLASS]` `[A3-PTR-NAV-REPAINT]` | |
| ✅ B02 edit pending | `[A4-EDIT-STALE-PENDING]` | |
| ✅ B03 nav stamp | `[A4-NAV-BACKFIRST-GRAFT]` | |
| ✅ B04 Parent crumb | `[A4-PARENT-CRUMB-VTABLE]` | CE |
| ✅ B05 bool mask end to end | `[A3-BOOL-NATIVE-NOWRITE]` `[A3-FIRE-STRUCT-BOOLMASK]` `[A2-STRUCT-PREVIEW-BOOLMASK]` | |
| ✅ B06 invoke Y11 gate | `[P3-INVOKE-Y11-CEFORM]` `[P3-INVOKE-STRUCT-FSTRING]` | CE |
| ✅ B07 UFunction tail 4.x | `[A2-UFUNC-TAIL-4X]` `[A3-CEFORM-4X-STALESLAB]` | CE |
| ✅ B08 TOptional | `[A2-TOPTIONAL-INTRUSIVE]` | |
| ✅ B09 proxy deploy | `[A3-DEPLOY-CANCEL]` `[A3-RADIO-MIDDEPLOY]` | |
| ✅ B10 coord library | `[A1-COORD-RESURRECT]` `[A1-COORD-BACKUP]` | |
| ✅ B11 console re-invoke | `[W3-CONSOLE-REINVOKE]` | |
| ✅ B12 snapshot enum | `[P3-SNAPNUM-ENUM]` then `[W2-GROUPMATCH-ENUM]` | |
| ✅ B13 group width | `[W2-ORDEN-FINDENTRY]` `[W2-GROUPMATCH-WIDTH]` `[A4-AB4-UINT64]` | |
| ✅ B14 Class Pivot session gate | `[W1-PIVOT-SESSION]` + register `check_session_gate` | |
| ✅ B15 SPC join mode | `[W1-SPC-JOINMODE]` | |
| ✅ B16 pivot array fields | `[W1-DISCOVER-ARRAY]` `[W1-ARRAYCOUNT]` | |
| ✅ B17 related race | `[W4-RELATED-RACE]` (before B26) | |
| ✅ B18 lookup filter | `[W4-LOOKUP-FILTER]` | |
| ✅ B19 bookmark DataTable | `[W4-BOOKMARK-DT]` | |
| ✅ B20 batch method | `[W3-BATCH-METHOD]` | |
| ✅ B21 teleport card text | `[W2-GRAVDIR-VERDICT]` `[W2-MS-PROMISE]` | |
| ✅ B22 teleport pose map | `[W2-TPREL-MAP]` | |
| ✅ B22b quiet-poll warning | `[W2-POSEATTACH-QUIETPOLL]` (filed 2026-09-11) | |
| ✅ B23 CE XML FString | `[W5-CEXML-FSTRING]` | CE |
| ✅ B23b string-array elements | `[W5-STRARRAY-ELEMENTS]` (filed 2026-09-11 while fixing B23) | |
| ✅ B24 USMAP enum | `[A4-USMAP-ENUM-UNDERLYING]` | |
| ✅ B25 xref cap | `[W3-XREF-CAP]` | |
| ✅ B26 related stops | `[W4-RELATED-STOPS]` | |
| ✅ B27 stride tentative | `[W4-STRIDE-TENTATIVE]` | |
| ✅ B28 B30 stale flag | `[A3-B30-STALE-FLAG]` | CE |
| ✅ B29 pose parent-relative (B29a pipe + UI; B29b mailbox; the C ABI getters are `[A2-CABI-TELEPORT-PARENTREL]`) | `[W2-MARKER-PARENTREL]` + `[W2-TPREL-TRANSPORTS]` | CE |
| ✅ B30 ST1 super drain | `[A3-ST1-SUPER-DRAIN]` | CE |
| ✅ B31 queued collision | `[W3-DUNSTE-QUEUED]` (filed 2026-09-11 by the review of 3561c93c) | |
| ✅ B32 container enum | `[A4-USMAP-CONTAINER-ENUM]` (filed 2026-09-12 by review 3) | |

**LOW-only batches, after the MEDs** (43):
- ✅ **L01:** `[P1-GENAU-ABORT]` `[A2-GNAMES-PTRSCAN-ABORT]`
- ✅ **L02:** `[P1-ENUMNAMES]`
- ✅ **L03:** `[W5-CSX-DELEGATEPAD]` `[A4-DELEGATE-ARRAY-PAD]` `[A4-PUSHCE-UNPADDED]` (CE), in two batches: L03a and L03b.
- ✅ **L04:** `[P3-SDK-INNERS]` `[P3-SDK-GUESSED]`
- ✅ **L05:** `[P1-SEETHRU-NOPRODUCER]` `[P1-SEETHRU-GIVEUP]`
- ✅ **L06:** `[P1-UPROP-DELEGATE]`
- ✅ **L07:** `[P1-WALK-UNREADABLE]` `[A4-REROOT-STALE-WARNING]`
- ✅ **L08:** `[P1-SPARSEDELEGATE-REFS]`
- ✅ **L09:** `[A2-WALKCLASSEX-UNMAPPED]`
- ✅ **L10:** `[A2-LAZY-LATCH-GUESS]`
- ✅ **L11:** `[A2-CRC-PATH-LS]`
- ✅ **L12:** `[A2-HEAP-ANCHOR-TEXT]`
- ✅ **L13:** `[A2-METHODE-MANUALMAP]` (CE)
- ✅ **L14:** `[A3-MIMIC-INIT-FASTPATH]` (CE)
- ✅ **L15:** `[W5-OFFSETS-UNMEASURED]` (CE). Its mailbox half is L45.
- ✅ **L16:** `[W5-DENKEN-DEADGUARD]`
- ✅ **L17:** `[A4-CDOSCOPE-ANCESTOR]` `[A4-CDOSCOPE-NESTED-PREVIEW]`
- ✅ **L18:** `[A4-PIVOT-CROSSGAME-ID]` `[W1-PIVOT-LOADCTS]`
- ✅ **L19:** `[A4-LW-DISCONNECT-PARENT]` `[A1-DETECT-REPUBLISH]`
- ✅ **L20:** `[A4-STEALTH-PRIME]`
- ✅ **L21:** `[A4-GAMEONLY-ADVICE]` `[P5-GROUP-ADVICE]` `[A3-CONTAINER-4096-ADVICE]`
- ✅ **L22:** `[W1-DT-TRUNC]` `[P5-PIVOT-FETCHCAP]`
- ✅ **L23:** `[W1-GROUP-DENYLIST]`
- ✅ **L24:** `[W5-INSTEXPORT-TRUNC]`
- ✅ **L25:** `[W1-PARTIAL-MARK]`
- ✅ **L26:** `[W1-PIPEBUSY-LOG]` (CE)
- ✅ **L27:** `[W1-WINMM-LOADMODE]`
- ✅ **L28:** `[W2-BETWEEN-PREVIEW]`
- ✅ **L29:** `[W2-DEADSCAN-LOADMORE]`
- ✅ **L30:** `[W3-CAP-NOSAVE]`
- ✅ **L31:** `[W3-DIP-PIXELS]`
- ✅ **L32:** `[W4-HEXSORT]`
- ✅ **L33:** `[P3-SCORING-MCDELEGATE]`
- ✅ **L34:** `[P8-BOOKMARK-TIP]`
- ✅ **L35:** `[A1-LOG-RESUME]`
- ✅ **L36:** `[A1-SLOTSYM-FAILED]` `[A1-LUA-WAIT]` (CE)
- ✅ **L37:** `[W2-CEGEN-MODAL]` (CE)
- ✅ **L38:** `[A3-RECYCLE-GUID-FAILOPEN]`
- ✅ **L39:** `[A3-COORD-NONFINITE]`
- ✅ **L40:** `[A2-TOPTIONAL-STRUCT-DESCENT]` (filed 2026-09-11 by the review of cc430176)
- ✅ **L41:** `[A2-TOPTIONAL-VALUESCAN]` (filed 2026-09-11 by the review of cc430176)
- ✅ **L42:** `[A4-AB4-BETWEEN]` (filed 2026-09-11 by B13)
- ✅ **L43:** `[W3-DEBUGCAM-QUEUED]` (filed 2026-09-11 by the review of 3561c93c) (CE)
- ✅ **L44:** `[A2-CABI-TELEPORT-PARENTREL]` (filed 2026-09-12 by review 5 of 76f93b94) (CE)
- ✅ **L46:** `[A2-SENTINEL-OVERREAD]` (found 2026-09-12 by review 6)
- ✅ **L47:** `[A2-TOPTIONAL-REFINE]` (found 2026-09-12 by review 6)
- ✅ **L48:** `[A1-VERDICT-STALEMB]` (found 2026-09-12 by review 6) (CE)
- ✅ **L49:** `[A1-REVIEW6-PINS]` (found 2026-09-12 by review 6)
- ✅ **L45:** `[W5-OFFSETS-MAILBOX]` (split off 2026-09-12 by L15: the CE mailbox does not carry the offsets verdict, and publishing it is a `MAILBOX_CONTRACT` change) (CE)

⚠ **L18's trap text** ("L18's CTS alone is insufficient") refers to the July row L18 (DetectAsync
has no cancellation), not to the batch L18 above.

#### Live experiments recorded in the finding phase — each joins the backlog when its row is fixed

⚠ Items marked **CE** need Cheat Engine: announce first. Every other row carries its own `experiment`
in its section; copy it into the backlog when the row is fixed.

- `[A3-ST1-SUPER-DRAIN]` — L36. The rig cannot do its step 2 yet (review 5); the pipe's `direct_call: true`
  reaches the same branch without CE, and the mailbox route needs CE.
- **CE:** `[A3-B30-STALE-FLAG]` — tick the inject record, reopen the `.CT` without merging, tick
  again. The pipe must survive.
- `[A4-USMAP-ENUM-UNDERLYING]` — parse an asset with a non-uint8 enum using our `.usmap` and a
  Dumper-7 one, in the throwaway CUE4Parse console. The property values must match.
- **UNDECIDED:** the Live Walker `IsEditing` latch cleared by an in-flight refresh (next to
  `[A4-EDIT-STALE-PENDING]`). Needs a DumperTest actor with a ticking float, Auto on.
- **UNDECIDED:** `[W4-DEEPWALK-750MS]`, whose row names the experiment.

### Order, and why

1. ✅ **Finish the June sweep** — done 2026-09-10: 50,451 lines, 39 distinct confirmed defects.
2. ✅ **Track A — the pattern sweeps** — done 2026-09-10. All eight shapes were run tree-wide: P1,
   P3, P4 / P7 / P8 and P5 as adjudicated sweeps, P2 and P6 as detectors. **20 new confirmed
   (1 HIGH · 4 MED · 15 LOW)**; P2's and P6's instances were already recorded. The gate-shaped
   detectors (P2, P6) are built and deliberately NOT registered until their instances are repaired.
3. ✅ **Track B A1–A4** — done 2026-09-10. **41 confirmed (0 HIGH · 11 MED · 30 LOW)**, 34 of them
   fix-pass claims not kept; see the close-out under `[TRACKB-A4-2026-09-10]`. **A1 done 2026-09-10**
   (`[TRACKB-A1-2026-09-10]`: 1 MED · 5 LOW, five of them fix-pass claims not kept). **A2 done
   2026-09-10** (`[TRACKB-A2-2026-09-10]`: 2 MED · 7 LOW, settled against vendored engine source).
   **A3 done 2026-09-10** (`[TRACKB-A3-2026-09-10]`: 4 MED · 8 LOW; three of the MEDs break the fix they
   sit in). **A4 done 2026-09-10** (`[TRACKB-A4-2026-09-10]`: 4 MED · 10 LOW).
4. ⬜ **ONE fix pass, LAST** — covering the June blank, Track A and Track B together, grouped **by
   shape, not by file**, so each shape is repaired ONCE with its complete instance list. That is the
   maintainer's stated reason for planning the second blank at all.

⛔ **ORDER CORRECTED 2026-09-10, at the maintainer's direction.** This list first put the fix pass
BEFORE Track B, which contradicted its own rationale: Track B reads the same band and can find more
instances of the very shapes being fixed, which would force a second fix pass per shape. It also
matches the standing instruction for this whole stream -- record now, repair together later.

⚠ **What waiting costs, stated so it is a decision and not an accident.** Two HIGH rows keep their
risk until the fix pass. `[W1-QUOTA-UNLIMITED]` deletes snapshots permanently, and this machine's
`experimental.json` was measured at `5120` -- ONE step below the point where `ApplyAutoQuota` sets
"Unlimited" by itself. ⭐ **No-code mitigation until the fix lands: leave *Auto-adjust quota* off
and do not pick *Unlimited*.** ✅ *Fixed in source 2026-09-10 (the fix pass's first row). The
mitigation still applies to any installed build older than the one that carries the fix.* `[W1-SNAP-FAULT]` stores a faulted chunk as complete, but it needs a
faulting object-decryption stub, which in practice means active reversing work on an encrypted title.
✅ *`[W1-SNAP-FAULT]` fixed in source 2026-09-10 (the fix pass's second row).*

⚠ **Do not start Track B before Track A.** Every instance a gate finds is an instance an agent does
not have to be paid to read for — and on current numbers the gates would have caught **at least
four** of the June sweep's confirmed rows outright.

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
> `TArray<TLazyObjectPtr>` (OCTOPATH has 5 scalar lazy properties and zero arrays, ES2 has
> 3 and zero). Pinned offline in `dll_core_test` instead.
>
> ⛔ **CORRECTION 2026-09-06 — the first pin did NOT cover the function with the bug in it.**
> It pinned `InferScalarSize`, an arithmetic helper; the `elemSize = 0x20` line lived in
> `ReadLazyObjectArrayElements`, and grepping `dll/tests/` and `tools/verify/` for that name
> returned **nothing**. Calling the fix "pinned" on that basis is the same mistake audit A7
> made — a test that names a **neighbour** of its subject. A second block now drives the
> array reader itself against a `{ Data, Num, Max }` triple in the test's own memory
> (`Macht::ReadTArray` only sanity-checks Count/Max, so it is a real TArray to the code).
>
> ⭐ **Red-tested**: putting `elemSize = 0x20;` back fails exactly two checks, and the failure
> text is the diagnosis — `first wrong element: 1 got {C0000001-D0000001-FFFFFFFF-FFFFFFFF}`.
> Element 1 picks up the TAIL of its own GUID followed by the next element's
> objIdx/serial: an 8-byte drift, exactly `0x20` against the true `0x18`. Element 0 passes,
> because both strides read it correctly — which is why "the count came back right" would
> have proved nothing. `Ubel.cpp` restored byte-identical afterwards.

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

### 1. ✅ DONE 2026-09-06 — `bCasePreservingName`: A9 changed the contract and left EIGHT sites on the old value

> **FIXED, and the count was 8 not 7** — re-derived site by site rather than taken from the
> finding. Twelve raw `bCasePreservingName ? 0x10 : 0x08` ternaries existed; **four were
> CORRECT** (a `TPair<FName, 8-aligned-T>` value offset — `Aura.cpp:3749/6336`,
> `Ubel.cpp:6370/6509`) and **eight were wrong** (`Ubel.cpp:333` stepping to an adjacent
> FName, `:3204/:3306/:5329/:5734/:5801` FScriptDelegate strides, `:4329/:4492` the
> `softArrayFNameSize` field that `Ubel.h:435` documents as **12**).
>
> ⭐ **The fix is not eight edits, it is making the question un-confusable.** `Grimoire.h`
> had stated the rule for a long time — and it was copied wrongly anyway, because *both*
> answers are spelled `bCasePreservingName ? … : 0x08` and the expression does not say
> which question it answers. `Aura.cpp:3755` sat four lines below one of the correct ones
> getting it right. So the rule is now two named functions, `DynOff::SizeofFName()` and
> `DynOff::FNameSlotIn8Aligned()`, and **all 20 sites** (the 12 plus the 8 that already had
> `0x0C`) go through them — `grep 'bCasePreservingName ?' dll/src/` now returns only log
> strings and `UFIELD_NEXT`, which is a different quantity.
>
> Pinned by `Test_DynOff_FNameSlotVsSizeof`: the two coincide at 8 when CPN is off (which is
> *why* a wrong site is invisible on every title measured), diverge by exactly the 4 bytes of
> padding when it is on, and `SizeofFName()` is never `0x10`. `dll_helpers_test`
> 2,330 → **2,337**.
>
> **Live regression check** (20 sites across 3 files, 13 of them in `Ubel.cpp`): OCTOPATH
> 4.18, build 3380 — soft `+0x10` / lazy `+0x0C` unchanged, and soft paths still render as
> `/Game/UI/MainMenu/BP/CharacterStandingPanel.WidgetArchetype`. Behaviour is identical by
> construction on the non-CPN branch, and this confirms it.

<details><summary>original entry</summary>

**`bCasePreservingName` — A9 changed the contract but left seven sites on the old value**

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

</details>

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

## 🔎 Audit #3 RE-CHECK — measured 2026-09-08 (build 3423). Verdict: don't re-run it, sweep its blind spot

**Why this exists.** audit #3 is the only audit on this repo not produced by Opus 5, and its own
doc never records that. Measured from the git trailers: `abf1ce09` (the findings doc) through
`e75009b2` (the tracker close) is **24 commits, 22 of them `Co-Authored-By: Claude Fable 5`**,
including **all 12 named fix commits**; the 2 Opus 4.8 commits in the same window (`f66e6025`,
`40817bad`) are unrelated fstruct-invoke feature work carrying no finding id. For contrast:
audit #4 = Opus 5 / 48 agents, audit #5 = Opus 5 / 279 agents (summed from the per-segment counts;
the doc prints no total), audit #6 = Opus 5 / 100 agents. audit #3 states no agent total —
5 finders + a 10-cluster re-verification + 3 HIGH-lens skeptics is what its Method paragraph names.

Re-checked by a 12-agent workflow (4 evidence + 2 miss-hunts + 6 refute-mandated skeptics), every
`file:line` re-read at HEAD because audit-doc line numbers have drifted hard (`Schlacht::SetEnabled`
437 → 667; `TeleportViewModel` gate-off 2909 → 4397).

### ✅ What held — this is why the answer is NOT "re-run audit #3"

- **23 of 23 shipped fixes are still live at HEAD.** 22 `present`, 1 (`L5`) relaxed `>` → `>=`
  **deliberately** by `[CADENCEGAP-2026-08-22]`; its underflow guard is intact. Nothing was silently
  reverted in ~1,250 builds. Three changed shape and stayed correct: `L3`'s prescribed
  `exactMatch=true` became `Aura::FindInstancesDerivedFrom` (A6) with the substring hazard still
  closed by `ClassChainMatchesLower`'s per-level full-string compare; `M4`'s inline
  `MarkBackgroundWorker` moved into the shared `Routine::ReassertLoop`; `L4`'s prune guard was
  re-expressed `results.size() < cap` → `!rset.truncated`.
- **All 9 dropped items re-derived → 0 reopens.** `M6`'s family-uniformity premise still holds (the
  post-audit `Dunste` is *also* absent from both cleanup paths, reinforcing it); `L6`'s Welford
  `m2 >= 0` proof survives even the `gap == 0` samples the `>=` change re-admitted; `L20`/`L21` are
  moot (later fixes plumbed the ct and moved to `ObjectTreeFilter`). ⭐ **Fable 5's adversarial
  judgement was sound — the refutations were right.** The gap is in *coverage*, not in reasoning.

### ⛔ What it missed — 5 confirmed (1 more claim refuted), and they share ONE shape

Each survived a skeptic mandated to refute, checking all five collapse routes (didn't exist at 2168 /
out of scope / later code / audit #3 did raise it / different mechanism).

| sev | miss | found by |
|---|---|---|
| 🔴 | **Schlacht::Tick recorded INTENT, not effect** — `InvokeSetHidden` returns `bool`, both call sites drop it in one line each. See-through was a **no-op on non-Actors** while `hiddenActors`, `hiddenCount`, the pipe's `hidden_count`, the UI status string and `"disabled (N restored)"` all reported success | `d48441e7` 2026-08-22 `[SEETHRUTALLY]` — **6 weeks later** |
| 🔴 | **`SnapshotStore.OpenAsync` ran DROP-based schema init on every open, no mutual exclusion** — a concurrent first-open DROPs a just-captured snapshot | `3c637c56` 2026-07-22 (8 days later) |
| 🔴 | **DumpExplorer live match joins on bare class names, no game-identity gate** — another game's `.jsonl` does not fail, it "matches" | `5389fde4` 2026-08-01 |
| 🟠 | **Solide `Int8Property` written signed, read unsigned** — the drift loop can never converge; rewrites the same byte every 300 ms reporting permanent drift | audit #5 `AF8`, `ec72d7c0` |
| 🟠 | **PipeClient converts an UNEXPECTED pipe death into a token-less OCE** | audit #5 `AC10`, build 3262 |
| ⬜ | *refuted:* "Solide `ApplyToInstance` discarded the write result" — the refusal path it needs was added by `ec72d7c0` **5 weeks after** the audit; at 2168 the discarded bool had no reachable failure | — |

⭐ **The shape, and it is not random.** The first three were **inside functions audit #3 itself
rewrote that day.** `M1`/`M2`/`M3` all reason about the *contents* of `s_state.hiddenActors` and
`0f6f6e07` rewrote that disable path — nobody asked whether the set's contents were ever **true**.
`L11` quotes `InvokeSetHidden`'s false return and treats it as the **safe** outcome. `L21` filed an
INFO-tier helper-reuse nit in `DumpExplorerViewModel` ~60 lines below a bug that gives a confident
wrong answer on every UE title (every game has an `Object`, an `Actor`, a `Pawn`).

⭐ **And the audit's own cross-cutting root cause rested on a false premise.** `H1` states in
writing *"Unexpected pipe DEATH is safe because ReadLoop faults pendings with IOException, not
OCE"* — falsified by the two catch filters 30 lines away in the same method. The shared
`IsUserCancel(ct)` helper it recommended was **never built**, so the same shape was re-found later
as `AC10`.

### 📐 The coverage gap is structural, and no later audit touches it

audit #5's own header says #3 and #4 were both scoped *"files changed since baseline X"*; #4's
baseline **is** #3's endpoint `af2ce50`, and #5's predicate is *authored before 2026-06-01*. So:

- audit #3's window (`88ee170..af2ce50` = **2026-07-03 .. 07-15**, 164 commits, 229 non-doc files,
  +23,814/-958) has **22,853 lines still alive at HEAD, of which 100% have never been re-swept at
  line level by any later audit.** Not an estimate — tautological under `git blame`: a line still
  blamed into that range is a line no post-`af2ce50` commit touched, so #4 covers exactly zero of
  them by construction. Derive it, never quote it:
  `git rev-list 88ee170..af2ce50 | sort > S` then per file
  `git blame --line-porcelain HEAD -- <f> | grep -E '^[0-9a-f]{40} ' | awk '{print $1}' | grep -c -F -x -f S`
  (cross-validated against a second pass classifying by porcelain `author-time`: identical).
- ⛔ **HARD CORE: 80 files / 4,648 lines are unchanged since `af2ce50` AND cited by no later audit
  document at all** — never modified, never re-read, never mentioned. `dll/src/Grausam.cpp` is
  279/279 lines from that window, byte-identical since, and its only audit-#5 appearances are as a
  *precedent* for a Stark fix and inside a refutation.

### ⚠ Verification asymmetry — the audit's only HIGH is its least-verified fix

- **13 of 23 have a real headless rig** (all DLL-side, run on DumperTest 2026-08-21..24 with
  negative controls): `M1`/`M2`/`M3` `[M123-RESTORESET-2026-08-24]`, `M4`, `M5`, `L1`, `L2`,
  `L3`+`L4`, `L5`, and the rest of the DLL LOW batch.
- **10 have a unit test ONLY and appear NOWHERE in `verification-register.md`** — grep of the
  register for these ids is **zero hits**: `H1`, `M7`, `M8`, `M9`, `M10`, `L13`–`L17`, all UI-side.
  `H1` — a truncated snapshot committed as usable — rests on
  `Capture_DisconnectMidStream_DoesNotSaveUsablePartial` and has never met a real pipe. This
  violates the register's own single-owner rule at `:10164`.
- 7 were never fixed (the `L7`/`L9`/`L11`/`L18`/`L19`/`L20`/`L21` downgrades), re-derived above.

### ⬜ Actionable, in priority order

1. **⏳ IN PROGRESS — pattern sweep for the named blind spot, repo-wide**: a function that declares
   `bool`/an error code whose call sites drop it, and any count/status/log reported from *intent*
   rather than from re-reading the effect. Purely static, one screen per site, and **both #3 and #4
   walked past the Schlacht instance**. This is a *pattern* sweep, not an area audit — re-running
   audit #3 is the wrong spend given 23/23 and 0 reopens above.
2. **⬜ Sweep the 4,648-line hard core** (80 files, list derivable with the `git blame` recipe
   above), `Grausam.cpp` first — the lowest-coverage code in the repo.
3. **⬜ Give the 10 UI-side fixes register rows**, `H1` first with a real acceptance test.

### ⚠ Two stale register rows found on the way (fix when next editing the register)

`docs/verification-register.md:10451` still marks **Solide `L3`/`L4` ⬜**, and `:9894`'s long-tail
heading still names them unproven — both superseded by `[SOLIDE-L3L4-2026-08-23]`
(held=130 where substring predicts 108; 12/12 base and 12/12 **derived** forced, 0/8 decoys).

## 🔎 Blind-spot sweep ROUND 1 — 2026-09-08, build 3423. 5 confirmed, and the instrument was 76% blind

Round 1 of action (1) filed by the Audit #3 re-check above: hunt the shape both #3 and #4 walked past —
a discarded `bool`/status return (**shape A**), and a count/log/pipe field/UI string computed from the
ATTEMPT rather than from re-reading the EFFECT (**shape B**). 21 agents: 7 module finders over a
mechanical candidate list → 2 refute-mandated skeptics per group → a completeness critic.

**Raw: 61 defect claims → 13 skepticised (top 2 per group) → 5 confirmed, 8 refuted.**
Kill rate 62%, in line with this file's own "an agent sweep is ~half wrong before refutation".

### ✅ Confirmed (all re-read at HEAD; none is a list row — see the instrument section)

| sev | site | what lies |
|---|---|---|
| 🟠 MED | `dll/src/Dunste.cpp:217` `InvokeSetCollision` drops `UE5_CallProcessEventEx`'s `int32_t` and returns "the setter was **found**" as if it meant "collision **changed**" | `LOG_INFO("Fly: SetActorEnableCollision(%d) invoked")` + the `return true` all three call sites commit `s_state.collisionOff` from. **`-8` is not hypothetical** — ⚠ **but only via ONE of the two threads; corrected by round 2, see below.** The `WorkerLoop` arm (`Dunste.cpp:482` → the `:530` call) stands: `IsResponsiveFromLiveness(Unknown) == true` by contract (`Stark.h:129-133`), so the worker proceeds with the hook *inactive*, and `Frieren.cpp:2146` then returns `-8` for the marked thread. The `PendingRestoreLoop` arm (`:576` → `:612`) is **REFUTED**: that loop only starts from `StartPendingLocked()` at `Dunste.cpp:729`, inside the `else` of `if (Stark::IsGameThreadResponsive())` — i.e. only when `Stalled` — and `ClassifyGameThreadLiveness` (`Stark.h:112-113`) returns `Stalled` only past `if (!hookActive) return Unknown;`. **`Stalled` ⟹ hook active**, and `-8` (`Frieren.cpp:2146`) is only reached when `IsHookActive()` is **false**, because `:2109` dispatches to `EnqueueInvoke` first. Re-verified by hand at HEAD, not taken from the agent |
| 🟡 LOW | `dll/src/Aura.cpp:155` — a scan worker chunk that **throws** is swallowed by `catch(...)` which never sets the shared `deadlineHit` atomic | `Aura.cpp:8078` → `Fern.cpp:3224 data["deadline_hit"]` — the ONLY wire field that tells the UI the result set is truncated. A partial value scan renders as a complete one |
| 🟡 LOW | `dll/src/Ubel.cpp:3362` — `ReadMulticastDelegateArrayElements` drops both InvocationList header `ReadSafe`s | an unreadable element is published as the positive string `"(0 bindings)"` and counted in `readCount`; `Macht::ReadTArray` validates `Count`/`Max` but **never probes `Data`**, so a freed buffer passes the gate |
| 🟡 LOW | `ui/…/ViewModels/MainWindowViewModel.cs:1731` and `TeleportViewModel.cs:2006` — the CE AA-script clipboard fallback drops `CopyToClipboardAsync`'s `Task<bool>` | StatusText says *"copied as CE XML — paste into Cheat Engine's address list"* when nothing reached the clipboard. `IPlatformService.cs:49` states the contract verbatim (*"Returns true only when the text actually reached the clipboard"*) and **`InvokeParamDialog.cs:921-929` already handles it correctly** — the correct model exists in-tree |

⚠ **Two of these are families, not sites.** The clipboard defect has a **third** instance
(`LiveWalkerViewModel.cs:6209`) and the `Dunste` one a second (`Dunste.cpp:612`, PendingRestoreLoop
logging *"pawn collision restored"* off the same unchecked invoke) — both sit in the 30-item
unverified tail, both filed HIGH by their finder, neither yet skepticised. Fix by family.

### ⛔ Refuted (8) — do not re-raise

Both **Schlacht** follow-ups died, which is the useful negative result: `d48441e7`'s fix holds.
`Schlacht.cpp:366` (`Invoke` result dropped) and `:744` (`(N restored)` from the attempt set) were
each killed on ≥4 routes — `:744`'s premise was **factually false at HEAD**, because `d48441e7`
changed `restore` to the *applied* set. Also refuted: `Radar.cpp:1818` (the return **is** bound at
`:1803`), `Aura.cpp:6386` (`Macht::ReadSafe` writes the failure into the out-param before returning —
the result IS consumed), `Macht.h:354`, `Fern.cpp:6424` (documented, comment at `:6421-6423`),
`Frieren.cpp:1209`, `Wirbel.cpp:1128` (the comment at `:1093-1104` **forbids** consuming those
returns — *"Do NOT trust K2_SetActorLocation's return here"* — and the block ends in a re-read).

### ⛔⛔ The instrument was wrong three times, and the negative control caught it every time

⭐ **This is the transferable part.** The scanner was calibrated against the historical Schlacht defect
at `d48441e7^` — *"if it cannot see the bug that motivated the sweep, the list is worthless"*. It
failed that control **three** times, each for a different reason, and the third was found only because
the completeness critic went looking:

| pass | shape it could not see | C++ sites |
|---|---|---|
| 1 | the defect line starts with `for` — leading `if`/`for`/`while` heads were skipped wholesale | 65 |
| 2 | (heads peeled — control PASSES, and this is where the sweep was launched from) | **137** |
| 3 | **qualified calls**: the lookbehind `(?<![A-Za-z0-9_>.:])` rejected `Macht::ReadSafe(…)`, `p->f(…)` | **281**, of which **213 (76%) qualified**, 24 int-kind, 32 multi-line |

C# is worse: **17 → 115** (106 qualified). And pass 3 has its **own** blind spot, found by the same
control: statements inside a **lambda passed as a call argument** are invisible (depth never returns
to 0) — **66 bodies / 3,598 lines** in `dll/src`, of which `Aura.cpp` alone is 2,873, i.e. the entire
parallel-scan machinery. Pass 2 (line-based) sees those; pass 3 (statement-based) sees qualified and
multi-line. **Neither dominates — the UNION is the list.**

⭐ **The lesson, stated plainly: passing ONE negative control validated ONE axis, and was read as
validating the instrument.** The historical call happened to be *unqualified* and in the same
translation unit, so it could never have exercised the qualification rule. This is audit #4's own
lesson 1 (*"a fix verified against the list it was written from is not verified"*) wearing different
clothes. Reusable scanners: `scratchpad/discard_scan{,2,3}.py` (each keeps its calibration in `main`).

⭐ **The deciding measurement for round 2: 0 of the 5 confirmed findings came from a mechanical-list
row.** Only 2 of 13 skepticised candidates were list rows and **both were refuted**. List yield
**0/154**; hand-grep yield **5/5**. More triage of list output buys nothing — the next round must buy
hand-grep in unvisited regions.

### 📐 What round 1 structurally could not reach (measured by the critic, not estimated)

- **22 of 44 DLL modules had zero candidate rows** = 8,803 lines. Among them, four *siblings of the
  historical defect* — the same "hold a flag across a class tree" pattern: **`Edel` 439 + `Grausam`
  307 + `Hemmung` 550 + `Solide` 775 = 2,071 lines**. (`grp_feat` covered Schlacht/Renge/Laufen only,
  so `Grausam` was named in the prompt as *"read it in full"* and still produced nothing — treat it
  as un-swept, not as clean.)
- The **proxy family** `Lugner*` = 1,098 lines, zero rows, and 2 of them do emit log-count lines.
- **C#: 261 of 272 files untouched**, and **zero C# reporting channels were ever collected** — the
  channel loop in `discard_scan.py` is `for p in cpp_files`. Uncollected: **531 `StatusText =` writes**
  (613 counting Message/Summary/Result text), plus 25 `.axaml` files / 10,678 lines never scanned.
- The **CE Lua emission layer never scanned at all**: 18 generators + `CeXmlExportService` +
  `CeLuaHygiene` = 10,308 lines with **101 `showMessage(`/`print(` sites**, plus `scripts/*.lua` 2,762
  lines and `UE5CEDumper.CT` 929 lines.

### ⬜ Round 2 — ranked by the critic, and the ranking follows the 0/154 measurement

1. **CE Lua emission layer + the C# status surface.** Highest value *and* cheapest to adjudicate: a
   hit is a **CLAUDE.md rule violation with a named test file** (`CeMailboxBailoutTests` /
   `CeLuaHygieneTests`), not a judgement call — *"a bail-out that applied NOTHING must untick the
   record"*, *"never report a mailbox failure by guessing"*. Method: diff the other 17 generators
   against `SeeThroughScriptGenerator.cs`, which is the clean reference (re-reads `OffResult` at `:92`,
   gates on `state < 0` at `:94`). These land on a **user-visible claim**, i.e. MEDIUM not LOW.
2. **The 213 qualified-call statements**, split because the halves need different questions:
   (a) non-`ReadSafe` (~54) → ask shape A directly; (b) `ReadSafe` (165) → the discard **is** the
   documented idiom (the identical `ReadPtrAt` body appears verbatim in `Edel.cpp:53`, `Solide.cpp:79`,
   `Solitar.cpp:88`, `Hemmung.cpp:66`), so ask instead *"does the un-set out-param feed a COUNT or a
   published field?"* — which is exactly the confirmed `Ubel.cpp:3362` shape, with `Ubel.cpp:1933/1935`
   the same question unanswered.
3. **The 30-item unverified tail LAST** — but promote the two family duplicates named above now.

### ⬜ Proposed gate #17 — `tools/check_effect_result.py` (design only, not built)

A blanket "bool results must be consumed" rule is **noise**: 209 of 256 C++ bool discards are
read/query calls (`ReadSafe` 165 alone) and it would open with ~209 waivers. What is maintainable:
an **allowlist of ~25 effect-appliers** (`InvokeSetHidden`, `InvokeSetCollision`, `TeleportPawnTo`,
`SetEnabled`, `SetGodMode`, `SetDilation`, `ApplyToInstance`, …) in `tools/effect-appliers.tsv`,
matched by the union splitter so it sees `Ns::f()`, `p->f()` and multi-line calls, **baselined** like
gate #4's `aob-specificity-baseline.tsv` rather than zero-tolerance. Cost at introduction: **23 sites**;
steady state one baseline line per new site. ⚠ Exclude the param-buffer packers (`WriteVecParam`,
`WriteFloatParam`) — they build a ProcessEvent argument buffer, they are not effects. `[[nodiscard]]`
has better semantics but is unused repo-wide and needs a build-output decision the gate harness avoids.

## 🔎 Blind-spot sweep ROUND 2 — 2026-09-08, build 3423. 10 confirmed, and the proposed gate is refuted

Round 2 went where round 1's 0/154 list yield said to go: **hand-grep in unvisited regions**, not more
triage. 28 agents over the CE Lua emission layer (10,308 lines) + the standalone `.lua`/`.CT` (3,691),
the C# status surface (477 writes / 23 files), the 213 qualified-call sites round 1's scanner could not
express, the four never-swept sibling modules (2,071 lines), and the two promoted families.

**Raw: 112 defect claims → 18 skepticised → 10 confirmed, 8 refuted.** ⭐ **Again 0 of the confirmed
came from a candidate row** — `celua-A` states it outright: the rows are `[ENABLE]`/`[DISABLE]`
literals, while *the defect is the ABSENCE of an untick, which no claim-site list can surface.*

### ✅ Confirmed (10)

| sev | site | what lies |
|---|---|---|
| 🟠 MED | `scripts/UE5CEDumper.CT:879` — the **shipped** `.CT`'s inject record unticks with `memrec.Active = false` **immediately inside `[ENABLE]`** | CE's `TMemoryRecord.setActive` (`memoryrecordunit.pas:2573`) opens `if state=fActive then exit;` and only assigns `fActive` **after** `autoassemble` returns, so the untick **no-ops** and the row stays ticked over an injection that never happened. Then `[DISABLE]` runs a real `UE5_Shutdown` against a proxy this script never injected — which is **audit #4 B30**, named in the file's own comment three lines above |
| 🟡 LOW | `scripts/ue5_freeze_helper.lua:170` — the same immediate untick in **SAMPLE 1**, the block the file sells at `:137` as *"copy this whole thing"* | and its parity claim at `:132-133` (*"the same shape UE5DumpUI's own generated scripts use"*) is **false at HEAD**: `FreezeScriptGenerator.cs` emits `CeLuaHygiene.DeferredUntickLua` at `:93/:106/:243/:256`. A user who copies the sample reproduces `[FREEZESTUCK-2026-08-18]` |
| 🟡 LOW | `ui/…/Services/InvokeScriptGenerator.cs:134` — `AppendIdleWait` called **without `onUnreadable`** | so `CeLuaHygiene.cs:186` folds "mailbox unreadable" into "busy", and a **dead game process** is reported as *"Another CE script or a previous invoke is still mid-command; try again in a moment."* Straight against the MUST-rule *"never report a mailbox failure by guessing"* |
| 🟡 LOW ×2 | `LiveWalkerViewModel.cs:4362` and `:4866` | *"Copied: N objects, M XML lines."* / *"CE AA script copied"* over a discarded clipboard write |
| 🟡 LOW | `Views/PropertyXrefDialog.cs:487` | drops **both** AOBMaker `Task<bool>` results and paints a green success label |
| 🟡 LOW | `dll/src/Aura.cpp:6386` | reports `"(0 bindings, sparse)"` for a delegate whose `bIsBound` byte **read 1**, when the InvocationList header reads faulted |
| 🟡 LOW | `dll/src/Ubel.cpp:3051` | `TArray<TLazyObjectPtr>` publishes a **fabricated all-zero FGuid** and counts it in `readCount` when the element reads fault |
| 🟡 LOW ×2 | `TeleportViewModel.cs:1847` / `:2344` | Stealth *"Reset"* reports a release it never sent when the candidate is null (**the DLL keeps holding**); Time-dilation Apply promises an override *"applies once a pawn exists"* when `SetDilation` stored nothing |

⭐ **One root cause covers three of the CE Lua rows**: `CeLuaHygiene.AppendIdleWait`'s `onUnreadable`
parameter (`CeLuaHygiene.cs:163`, used at `:186` as `onUnreadable ?? onBusy`) is passed by only **2 of
its 5** call sites. The toggle-shaped wrapper `AppendIdleWaitOrBail` (`:540-564`) always distinguishes
correctly, so every generator that goes through it is clean. **The fix is 3 arguments, not 3 rewrites.**

### ⚠ A round-1 claim, corrected — and the correction is re-verified by hand

Round 1's `Dunste.cpp:217` MEDIUM **stands**, but its reachability argument was **half wrong** and the
row above has been edited in place. `-8` needs `IsHookActive() == false` (`Frieren.cpp:2109` dispatches
to `EnqueueInvoke` before the `-8` at `:2146`), and `PendingRestoreLoop` only starts when the thread is
`Stalled`, which **implies hook active** — so the `:576`/`:612` arm cannot reach `-8` at all. The
`WorkerLoop` arm survives *because* `IsResponsiveFromLiveness(Unknown) == true` by documented contract
(`Stark.h:129-133`), so the worker proceeds with the hook inactive. ⭐ **The refuting agent was right
and the round-1 write-up was wrong; this was re-read at HEAD before editing, not taken on trust.**

### ⛔ Refuted (8) — do not re-raise

`Dunste.cpp:530` (a **duplicate** of round 1's own confirmed row — three call sites, all already
enumerated there) · `Dunste.cpp:612` (above) · `Schlacht.cpp:366` and `Wirbel.cpp:1128` — **refuted a
second time, independently**, which is the strongest evidence yet that `d48441e7` and the
*"Do NOT trust K2_SetActorLocation's return here"* comment are both right ·
`Schlacht.cpp:640` · `Wirbel.cpp:619` · `TeleportScriptGenerator.cs:126` ·
`LiveWalkerViewModel.cs:5389`.

### ⛔ A test-coverage hole that let two of these survive two audits

`CeMailboxBailoutTests.cs:317-335` builds its corpus from
`Directory.EnumerateFiles(services, "*ScriptGenerator.cs")` filtered on the literal `"[ENABLE]"`.
`CeXmlExportService.cs` emits `sb.AppendLine("[ENABLE]")` at `:1334`, `:1537` and `:1709` — it passes
the **content** filter and is excluded purely by the **filename glob**. The suite's own guard
`emitters.Count >= 12` still passes, **so the hole is invisible**. Same shape as this file's
`[PROXYDEPS]` lesson: the assertion counts what it collected, not what exists.

### 📐 Coverage after both rounds (derived, not estimated)

- **dll/src is spent.** 237 of the 293-site shape-A population examined (**81%**); the last two DLL
  arms (72 qualified/int-kind sites + 2,071 sibling lines) returned **0 between them**; both DLL
  confirmations are LOW and both are `ReadSafe` siblings. Round 1 got 5 from the DLL, round 2 got 2.
  **A third DLL sweep is not worth an agent.** Residue, if ever wanted: `Fern.cpp` 26, `Wirbel.cpp` 26,
  `Frieren.cpp` 18.
- **Never walked back to their producers by either round:** 536 `LOG_*` sites in `dll/src` (234 carrying
  a count/outcome token) and **50 outcome-shaped pipe fields in `Fern.cpp`**. The canonical Schlacht
  defect lied through exactly one of those 50.
- **The C# dropped-bool population — 104 sites — was never enumerated by either round.** Round 2's C#
  axis was the *status surface*, a different cut. The three C# findings it did produce were incidental.

### ✅ The sibling-module null is REAL this time, and the distinction matters

Unlike round 1's Grausam null, the 0 here is evidence of clean code, proved three ways: the shape-A
density in that family is ~1 site per 115 lines (**denser** than the DLL average — the agent was not
reading empty ground); four of the modules (`Flamme` HintCache, `Linie` fire-counts, `Sense` telemetry,
`Methode` CE menu glue) have **no claim channel to lie through**; and `Dunste`, the one real effect
applier, is written *against* this shape on purpose — `:220` logs *"invoked"*, not *"applied"*, and
`Fern.cpp:6006-6007` discards three `int32_t` returns but publishes `flyStatusJson(st)` built from a
**re-read** of live state. That is the corrected Schlacht pattern already in place.
⚠ What reading genuinely **cannot** settle here belongs in the register, not in a sweep: whether
`Dunste`'s MOVE_Flying re-assert **wins the race** against the engine is observable only on a game.

### ⛔ Gate #17 as proposed in round 1 is REFUTED — do not build it

Scored against the evidence: an allowlist of ~25 **effect-appliers** over `dll/src` catches **0 of
round 2's 10** (the two DLL rows are `Macht::ReadSafe` — a *reader*; six are C#; two are `memrec.Active`
placement in `.lua`/`.CT`) and **0 of round 1's 5**. Worse, the allowlist *is* the failure mode this
repo has already measured: `tools/check_property_family.py`'s own header records that G12 shipped
*"Both writers now go through here"* when **there were three**, and concludes *"a prose invariant plus
a hand-counted writer list is what failed; counting them mechanically is the fix."*

**What the evidence supports instead — three small gates, none needing curation:**

1. **17a — CE Lua untick placement.** Population 17, mechanically separable with **zero curation**:
   flag `memrec.Active = false` lexically inside an `[ENABLE]` block and **not** inside a
   `createTimer`/`OnTimer`/`OnClose` closure. Catches `CT:879`, `freeze_helper.lua:170` **and the
   still-unreported `ue5_invoke_helper.lua:45`**; auto-refutes `TeleportScriptGenerator.cs:220/:281`,
   `BakedScriptGenerator.cs:431` (inside `t.OnTimer`) and `CoordLibraryScriptGenerator.cs:645`
   (`OnClose`, documented). **No baseline needed** — the correct count outside
   `scripts/tests/untick_bailout_test.lua` is **0**. Cheapest of the three and the only one that
   catches a MEDIUM.
2. **17b — C# dropped `bool`/`Task<bool>` with a nearby success claim.** No allowlist: the return type
   is in the declaration. Population 104, ≥24 within reach of a claim.
3. **17c** — the `Fern.cpp` outcome-field cut, if the first two prove maintainable.

### ⬜ ROUND 3 — finish two ENUMERATED families, 59 sites. Not another sweep.

Both first members were **3-for-3**, and both lists already exist:

- **`CopyToClipboardAsync`** — **42** dropped sites in 21 files; **54 of 56 call sites discard the
  `Task<bool>`**; only `InvokeParamDialog.cs:921` and `PropertySearchViewModel.cs:736` check it.
  **≥20 of the 42 sit within 22 lines of a success-shaped status write.** 4 confirmed so far.
  ⚠ Already-visible residue in a file round 2 *opened*: `PropertyXrefDialog.cs:458`, a green
  *"Copied:"* over a dropped write — **the family is not exhausted**.
- **AOBMaker bridge `Task<bool>`** — **17** dropped sites (`NavigateHexViewAsync` 9,
  `NavigateDisassemblerAsync` 4, `CheckAvailabilityAsync` 2, `CreateAAScriptAsync` 1,
  `CreateMemoryRecordAsync` 1), 8 of them in `PointerPanelViewModel` alone. 2 confirmed.
- Plus **one targeted read, not a sweep**: `scripts/ue5_invoke_helper.lua:45`, the third immediate
  `[ENABLE]` untick — in a region round 2 reported 100% swept.

⭐ **A round-3 agent reads 59 sites, not 44,000 lines.** That is the whole argument for doing it, and
against a third sweep.

## 🔎 Blind-spot sweep ROUND 3 — 2026-09-08. Both families CLOSED, and the sweep is FINISHED

Round 3 did no searching — it classified. 20 agents over the two families rounds 1–2 left enumerated
but unfinished. **86 sites reviewed → 36 defect claims → 12 skepticised → 8 confirmed, 4 refuted**
(2 of the 4 refuted purely as **duplicates** of already-confirmed rows, which is the process working).

### ✅ Final counts, re-derived by grep at HEAD — and every earlier count was wrong

| family | sites | CONSUMED | DEFECT | dropped-no-claim |
|---|---|---|---|---|
| `IPlatformService.CopyToClipboardAsync` | **57** (62 raw grep − 5 decl/comment) | **2** | **29** | 26 |
| `IAobMakerBridge` (7 methods) | **55** | **42** | **0** | 13 |

⚠ **Three independent counts, three different answers**: round 2's agent said 56 / 17, this session's
own scanner said 57 / 53, the closing grep says **57 / 55**. Only the clipboard number survived.
That is audit #4's B34 lesson landing again — *a rule applied to an enumeration that counted wrong* —
and it is why round 3's brief ordered agents to re-count from code. They found **10 sites the scanner
missed** (all `CONSUMED`, so no findings were lost, but the enumeration was wrong by 6/10 in one file).

The 2 consumers among 57 clipboard sites are `PropertySearchViewModel.cs:736` and
`InvokeParamDialog.cs:921`. **Everything else drops it**, and the contract
(`Core/IPlatformService.cs:30-49`) says the return is the *only* failure signal — it never throws — so
every `catch (Exception ex) { _log.Error(...) }` around these calls is **dead code for a real
clipboard failure**.

### ⭐ Why the two families diverge 51% → 0%, which is the load-bearing finding

It is **not** code quality, and **not** line count. `IAobMakerBridge`'s callers were written *against a
fallback*: every `CreateAAScriptAsync` site has a clipboard branch to fall to, so the bool **had** to be
tested to pick the branch — 42/55 consumed, **0 defects**. `CopyToClipboardAsync` is the **last link
with nothing to fall back to**, so testing it buys the author only an honest message — 55/57 dropped,
29 defects.

⭐ **The predictor is: a TERMINAL call with no fallback branch, whose documented contract makes the
return the only failure signal.** In all of C# that description fits exactly one interface method.
Recorded as a method lesson in [working-lessons.md](working-lessons.md) §2.x.

### The 29 clipboard defects come in three sub-shapes, and only one is dangerous

- **(a) DELIVERY copy — the clipboard IS the deliverable** (a CE script/XML the user is told to paste
  into Cheat Engine). **14 sites, 14 of them defects, zero dropped-no-claim in this sub-population.**
  Losing one means the user pastes **stale clipboard content into CE and runs it**.
  `InstanceFinder:834/1043` · `LiveWalker:4362/4701/4866/6209/6308` ·
  `MainWindow:1731/1940/1998/3175` · `Teleport:2006/2048/4103`.
- **(b) CROSS-FILE claim through an `Action<string>` event — architecturally unrecoverable.** 4 sites
  (`MainWindowViewModel.cs:1289/1508/1559/1654`) whose claim lives in *another file*
  (`LiveFuncsViewModel.cs:395`, `InterestingPropertiesViewModel.cs:545`, …). The event is
  `Action<string>`, not `Func<string,Task<bool>>`, and the raiser sets its status **synchronously
  before the async handler has run** — so no call-site fix and no single-file gate can reach these.
  Fixing them is an **API change first**.
- **(c) The convenience copies** — an address, a name, a class name. 26 dropped-no-claim, LOW at worst:
  the user re-clicks.

⭐ **A fourth in-tree correct model, and the best one**: `PointerPanelViewModel.cs:1114/:1142`
`ReportSymbolRegistration(success, …)`. Its doc at `:1146-1177` records that **both sites once branched
the bool only to pick `_log.Info` vs `_log.Warn`, "so the panel looked identical whether CE had
registered the symbol or not"** — i.e. the maintainer has already fixed one instance of this exact
family, in this file, and **the fix was a shared reporter helper**, not 14 edits.

### ⛔ Gate 17b as scoped is REFUTED — ship a narrower one

"A discarded `bool`/`Task<bool>` whose success is then claimed" has **no maintainable
implementation**, measured rather than argued:

1. **Naive discard is 47% false positive** — 55 hits, 29 defects, 26 legitimate. Correct count is 26,
   i.e. a **baseline**, which `check_ce_untick_placement.py:28-33` refuses by name.
2. **Adding "…and success is claimed" needs the exact allowlist 17a exists to avoid** — the 29 claims
   land in **9 distinct sinks** (`StatusText`, `LookupStatusText`, `GroupStatusText`, `DiffStatusText`,
   `CoordStatus`, `_statusLabel.Text`, `_resultLabel.Text`, `_log.Info`, and a **colour literal**
   `#4EC9B0`), and a tenth arrives with the next panel. Word-matching is worse: all 26 legitimate sites
   carry `_log.Error("Failed to copy …")` within 6 lines.
3. **It structurally cannot see 4 of 29 (14%)** — sub-shape (b), the only ones not fixable at the call
   site.

**✅ 17b — "no discarded DELIVERY copy".** Match a `CopyToClipboardAsync` whose result is discarded
**and whose argument is a generated script/XML** (`CheatTableBuilder.WrapAaScriptXml`,
`CeXmlExportService.Generate*Xml`, or a local named `xml`/`script`). **Measured: 14 hits, 14 defects,
0 false positives.** The discriminator is real, not lucky — all 26 legitimate sites copy an address, a
name or a class name, and **none** copies a script. Correct count **0** after the 14 are fixed, and it
stays 0 by construction. **No baseline**, for the same reason 17a needs none: *pick a predicate whose
legitimate population is EMPTY, rather than one whose legitimate population must be enumerated.*

⬜ **Optional stronger form — a decision, not a gate proposal**: split the API. Keep
`Task<bool> CopyToClipboardAsync` for delivery, add an explicitly-named fire-and-forget sibling for the
26 convenience copies. 17b then becomes *"no discarded `CopyToClipboardAsync` anywhere"* — correct
count 0, no argument heuristic, and **the sibling's name is its own negative control**. Coverage goes
48% → 100%. It is an API change first.

### ⛔ THE SWEEP IS FINISHED. Do not run a round 4.

Not a judgement — **one grep**. Every `bool`/`Task<bool>`-returning method on every
`ui/UE5DumpUI/Core/I*.cs` interface was enumerated: **17 methods** (`IAobMakerBridge` ×6,
`IPlatformService` ×6, `IProxyDeployService` ×4, `ILogCompressionService` ×1). Two were the families
just closed (112 sites). Every call site of the other nine was checked: `DeployAsync` (×2),
`UndeployAsync`, `MoveToRecycleBin`, `IsOurProxyDll`, `TryAcquireSingleInstance` — **all consumed**;
three have no discarding call site at all. **Outside the two families the entire C# shape-A population
is 1 site and 0 defects** (`App.axaml.cs:49 ActivateExistingInstance`, which shuts the process down two
lines later and has nowhere to make a claim). **There is no third family.**

⭐ **Round 2's "68 Services files / 24,651 lines" framing was the wrong predictor and cost nothing only
because it was checked before being spent.** The two families sit in the *same layer, the same files,
often the same method*, at 51% and 0% defect density. Line count predicted nothing.

**Sweep totals across all three rounds: 23 confirmed** (1 🟠 MED in a shipped artifact, fixed with
gate 17a; the rest LOW), **20 refuted**, one round-1 claim corrected, one gate shipped, one gate
designed and one gate design refuted.

### ✅ FIXED 2026-09-08 — sub-shapes (a) and (b) are closed; gates 17a + 17b hold them

1. ✅ **All 14 delivery copies** (sub-shape (a)), in three batches: `89887dee` (5) · `cb80ae95` (9),
   through `Helpers/ClipboardDelivery` — a shared reporter on the `ReportSymbolRegistration` model,
   not 14 hand-written branches. **Gate 17b `tools/check_clipboard_delivery.py`** now holds the count
   at 0. Red-before proved for all 14 without hand-waving: stashing batch 3 names 9, and running the
   gate's scanner over `git show 89887dee^` names 7 — 9 + 7 − 2 overlap = **exactly the 14 the critic
   predicted, site for site**.
2. ✅ **The API split, sub-shape (b)** — `7de16071`. `RequestCopyText` is now
   `Func<string, Task<bool>>`. ⭐ **The defect there was ORDERING, not a dropped bool**: as an
   `Action<string>` the handler was an async lambda, so `Invoke` returned at its first `await` and the
   VM's `StatusText = "Copied …"` ran **before the clipboard was touched** — even a handler that knew
   it failed had nowhere to put the answer. `MainWindowViewModel`'s own comment said it out loud
   (*"Status text already set by the VM"*). 17 edit points, 7 files, no logic redesign; AXAML bindings
   untouched because CommunityToolkit strips the `Async` suffix. Also removed 5 `async void`-shaped
   handlers.
3. ⬜ **Still open: the round-3 tail** — the ~11 convenience copies that *do* carry a claim
   (`ClassPivot:1201`, `DumpExplorer:309`, `RelatedObjects:177`, `Snapshot.Group:285`,
   `SnapshotViewModel:1536`, `SpcQuery(.Group)`, `InstanceFinder:1020`, `FunctionPropsDialog:386`,
   `PropertyXrefDialog:458`, `TeleportViewModel:1483`). Already classified — they need fixing, not
   more verification. ⚠ These are **deliberately outside gate 17b's predicate**: they copy an address
   or a name, and folding them in is what would turn a 0-baseline check into a 26-line waiver list.
   Their claims should simply stop asserting a copy that was not checked.

⭐ **A gate's own negative control earned its keep three times in one session.** 17a's marker anchor
missed `<AssemblerScript>[ENABLE]` and hid the one LIVE defect; relaxing it then matched a trailing
comment and hid another; 17b's first draft read a leading `if (!sentToCe)` head as consumption —
the exact shape of 5 of its 14 targets — and reported **CHECK OK over a tree that still had them**.
Every one was green before the control said otherwise. See [working-lessons.md](working-lessons.md)
§1.2a.

### ✅ FIXED 2026-09-08 — the 5 DLL rows + the CE Lua one; 2 more gates shipped

The sweep's DLL half is closed. Fixes were **derived and adversarially checked before being
written**, because this repo's own history says a verdict is not authority on the repair
(AB4: the diagnosis was right and the prescribed fix could never have fired). All five came
back `sound-with-corrections`; **none was an AB4**, and every correction was in the residual
risk or the blast radius — the same place the 2026-08-16 MED re-derivation found them.

| row | commit | what it stopped claiming |
|---|---|---|
| **D1** `Dunste.cpp` 🟠 MED | `fc83923e` | `InvokeSetCollision` returned *"the setter was FOUND"* and all three call sites read it as *"collision CHANGED"* |
| **D2** `Aura.cpp` | `e6360903` | a scan worker that THREW let the run report **complete** — `deadline_hit` is the only wire field saying a result set is truncated |
| **D3+D5** `Ubel.cpp` | `c45c7ce8` | an unreadable delegate array published the affirmative `"(0 bindings)"`; a faulted `FGuid` read published a fabricated all-zero GUID |
| **D4** `Aura.cpp` | `2a8b257b` | a sparse delegate whose `bIsBound` byte **read 1** reported `"(0 bindings, sparse)"` |
| CE Lua | `a55ef2b5` | Invoke's idle wait reported a **dead game process** as a **busy mailbox** |

⭐ **D1 was bigger than filed, and the checker is why.** The dropped `int32_t` **re-opens the
hole audit #4 B8 was written to close**: `SetEnabled(false)` invoked the restore, ignored the
answer and cleared `collisionOff`/`collisionPawn` unconditionally, so a refused restore left
the pawn ghosted **and** wiped the record that would have started `PendingRestoreLoop` — the
failure B8 describes as *"what made the pawn fall through the world"*. It reaches that through
the **dispatcher** rather than through `IsGameThreadResponsive`, which is why B8 did not cover
it. And B8 had to survive, so a bool was never the answer: **absent** (permanent — retrying
cannot conjure a setter, so it commits) and **refused** (transient — it must not) are
different failures. Hence `CollisionApply` + the pure `ShouldCommitCollision`.

⭐ **D4 found a FOURTH site nobody had filed**: `FindReferencesToUObject`'s sparse pass carried
the byte-identical dropped pair, and on a fault it `continue`s — so the binding is silently
**absent** from Find Refs, and an absence reads as *"nothing points here"*, a **stronger** claim
than a wrong count.

**Two gates shipped** — both on the rule that earns them: *pick a predicate whose LEGITIMATE
population is EMPTY, rather than one whose legitimate population must be enumerated.*
`check_ce_untick_placement` (17a) and `check_clipboard_delivery` (17b). `check_all.py` runs 18.

⚠ **Three gate bugs were caught by their own negative controls, each after the check was
already green** — 17a's marker anchor twice, and 17b reading a leading `if (!sentToCe)` head as
consumption, which is the shape of 5 of its 14 targets. Recorded in
[working-lessons.md](working-lessons.md) §1.2a.

### ⬜ FILED, not forgotten — the Fly report publishes the WISH, not the FACT

Found by D1's checker alongside the defect, and deliberately **not** folded into the fix
because it is a pipe-contract + UI change rather than a correctness one:

- `Fern.cpp` publishes `data["noclip"]` from `s_state.noclip` — **what was asked for**. The
  fact (`collisionOff`, and now whether the invoke was actually *applied*) reaches **no**
  channel: not the pipe, not `FLY_OP_GET_STATE`.
- ⚠ And `collisionOff` **cannot simply be published as the fact**: B8 gives it *"intended, and
  retrying cannot help"* semantics, so a game whose pawn class has no setter records `true`
  while the pawn is fully colliding. Publishing it would ship a **new** lie. It needs a
  tri-state (`COLL_ON` / `COLL_OFF_APPLIED` / `COLL_NOT_APPLIED`).
- `FlyStatus.Noclip` is **already dead on the C# side**: `ApplyFlyReadout` never reads it, and
  the `✈ Fly ON (noclip)` badge is built from the local checkbox — **it never asks the DLL at
  all**.
- ⭐ The in-tree model is `Solitar.cpp`'s `GetGodMode()`, which resolves `bCanBeDamaged` on the
  live pawn every call and returns the **observed** bit; and `Fern.cpp`'s fly handler already
  discards three `int32_t` returns but publishes `flyStatusJson(st)` from a **re-read** — the
  corrected pattern is next door.

### ⬜ Still open from the sweep

- The **~11 convenience copies that carry a claim** (`ClassPivot:1201`, `DumpExplorer:309`,
  `RelatedObjects:177`, `Snapshot.Group:285`, `SnapshotViewModel:1536`, `SpcQuery(.Group)`,
  `InstanceFinder:1020`, `FunctionPropsDialog:386`, `PropertyXrefDialog:458`,
  `TeleportViewModel:1483`). ⚠ Deliberately outside 17b's predicate — folding them in is what
  turns a 0-baseline check into a waiver list. Their claims should stop asserting an unchecked
  copy; that is a different fix from routing them through `ClipboardDelivery`.
- `TeleportViewModel:1847` / `:2344` (Stealth Reset reports a release it never sent; Time
  dilation promises an override `SetDilation` stored nothing).
- ⚠ **D2's deliberate residual**: a worker fault now reports through `deadline_hit`, so the UI
  says *"DEADLINE HIT, this scan is partial"* for a cause that was not a deadline. The partial
  claim is true and the log names the real cause; a second wire field would mean a protocol
  change plus `PartialResultNotice.DeadlineClause` and its 8 emit sites, and that string is
  pinned by 3 test files.
  ⚠ *Corrected 2026-09-10 (`[PATTERN-P5-2026-09-10]`). `DeadlineClause` has **one** call site
  (`ScanSuffix` → `InstanceFinderViewModel.cs:647`), both then and now. The **eight** are the
  fault-reachable `deadline_hit` renderings at e6360903. They are listed here so that "not through
  `DeadlineClause`" is never again read as "not recorded":*
  - *`ScanSuffix`;*
  - *the four batch cells: `GameClassFilterViewModel.cs:362`, `InstanceFinderViewModel.cs:978`,
    `InterestingPropertiesViewModel.cs:241`, `PropertySearchViewModel.cs:814`;*
  - *`LiveWalkerViewModel.cs:2679`;*
  - *`ValueSearchViewModel.cs:1025`;*
  - *`PropertyXrefDialog.cs:430`.*

  *The group scan's `:1448` is not one of them: that loop is serial and has no fault cause.*
- ⚠ **What no test reaches**: D4's three walker early-returns need a live `FSparseDelegateStorage`
  and the AOB resolver, and every D1 call-site path needs a running game with the PE hook down.
  The pure cores are pinned; the call sites are reviewed, not tested. Live rows for these belong
  in [verification-register.md](verification-register.md), not here.

## ✅ Live verification round 1 — 2026-09-08, DumperTest dev (UE 5.4), DLL 3457

First live pass over the blind-spot sweep's DLL fixes. Engine confirmed really up before
anything was believed: **25,229 objects**, offsets `validated: true` — not the coherent
zeros a dead engine reports.

### ✅ D5 `[D5-LAZYGUID-2026-09-08]` — `tools/verify/d5_lazyguid_unread.py`

| state | `Arr_LazyPtr` elements |
|---|---|
| baseline | 3 × a real GUID + its resolved `DumperTestHolder` name |
| `Data` → `0x1000` | 3 × **`???`** |
| restored | byte-identical to baseline |

Pre-fix the middle row was `{00000000-00000000-00000000-00000000}` ×3 — **indistinguishable
from a genuinely unset `TLazyObjectPtr`**, which is a legitimate all-zero FGuid. That is why
the repair had to be the read's own answer and not a display change.

### ✅ D3 `[D3-DELEGATEARR-2026-09-08]` — `tools/verify/d3_delegate_array_unread.py`

| state | `Arr_MulticastDelegates` elements |
|---|---|
| baseline | 2 × `(0 bindings)` — READ, and genuinely empty |
| `Data` → `0x1000` | 2 × **`???`** |
| restored | identical to baseline |

⭐ **The baseline row is load-bearing here, unlike D5's.** Pre-fix the corrupted case rendered
the *same* `(0 bindings)` as the healthy one, so a rig checking only the corrupted state
could not tell a working fix from a broken walk.

### 🟡 D4 — the fix FIRES, and immediately surfaced a real defect underneath it

`OnActorHit` (now bound) reads **`(sparse, bound — invocation list unreadable)`**. That is the
new string, replacing what would have been `(0 bindings, sparse)` — so the repair does what it
was written to do: it turned a silent false claim into a visible "I could not read this".

⚠ **But a delegate with one live subscriber should not be unreadable.** The walker found the
entry in `FSparseDelegateStorage` (`ownerFound && nameFound`, or the guard above would have
taken a different branch) and then failed to read its `InvocationList` header. Filed below.

### ⛔ THE FIXTURE HAD NO HOST FOR D3 OR D4 UNTIL TODAY

Asked of the RUNNING game, per `tools/ue-sample/README.md` rule 1 — never answer "does
fixture X exist?" by grepping the mirror:

- **D5** `Arr_LazyPtr` — present, verified above.
- **D4** — 16 `MulticastSparseDelegateProperty`, **all reading `(sparse, unbound)`**. With
  `bIsBound == 0` Ubel rejects before `Aura::WalkSparseDelegateBindings` is ever called, so the
  fixed path was unreachable. A probe over the level found **no bound one anywhere**.
- **D3** — **absent**. 16 `ArrayProperty` on the actor and not one with a `Multicast*` inner,
  so `ReadMulticastDelegateArrayElements` had no host at all.

Added to the fixture (both documented in `tools/ue-sample/README.md`, which
`check_ue_sample_values` gates — it failed the moment the UPROPERTY landed undocumented, which
is the gate doing its job): `Arr_MulticastDelegates` (2 elements, unbound on purpose — the row
under test is the element HEADER read) and `OnActorHit` bound to an empty `D4_OnActorHitProbe`
in `BeginPlay`.

⚠ **Packaging: Development only.** `capture_package_identity.py` then exits 1 **by design** and
names the reason — `Shipping` and `DebugGame` are now STALE, built before the source edit. Do
not read a value from those two until they are repackaged.

-----

### ✅ D4b — CLOSED 2026-09-09 `[D4B-DELEGATEPAD-2026-09-09]`. Original text below.

**Surfaced by D4's own fix, which is the point of it.** `OnActorHit` is bound to exactly one
subscriber and the walker locates it in storage, yet the `InvocationList` header read faults.
Pre-fix this was invisible — it rendered as `(0 bindings, sparse)`, i.e. *"this delegate
provably has no subscribers"*, over a delegate that has one.

Where to start: `Aura.cpp` Phase 3 derefs the `TSharedPtr` at `sharedPtrAddr` to get `mcdAddr`
and then reads `{Data, Num}` at `mcdAddr + 0x00/0x08`. One of those two assumptions is wrong
for 5.4 — either the TSharedPtr layout (is the object pointer really at +0?) or the
`FMulticastScriptDelegate` shape. ⭐ The fixture can now falsify either: `OnActorHit` is bound
on demand, and `read_mem` will show what is actually at those addresses.

### ✅ D3b — CLOSED 2026-09-09, SAME ROOT CAUSE `[D4B-DELEGATEPAD-2026-09-09]`. Original text below.

`ReadMulticastDelegateArrayElements` opens with `constexpr int32_t elemSize = 16;` (an
`FMulticastScriptDelegate` modelled as one `TArray` header). The live walk reports
`array_elem_size: 24` for `Arr_MulticastDelegates` on UE 5.4.

If 24 is right, **element [1] and beyond are read at the wrong offset** — [0] would be correct
and every later index would drift, which is the exact fingerprint audit A1 found on the lazy
row (`docs/…` — *"element 0 read correctly while every index ≥1 drifted"*).
⚠ **Today's rig cannot tell**: both fixture elements are empty, so a right-stride read and a
wrong-stride read of zeros produce the same `(0 bindings)`. Falsifying it needs elements with
**different** contents — bind one element and not the other, then check which index reports the
binding. Not attempted tonight.

## ✅ verification-register — the stream's rows, written at last `[RECON-REGISTER-2026-09-09]`

The reconciliation measured that `git log … -- docs/verification-register.md` returned **nothing**
for the whole work stream, while `todo.md:1605` said the live rows belonged there. Now written.

⭐ **What went in is the RESIDUE, not a victory lap.** The register's charter is *"everything
shipped but not yet proven against a running game"*, so the things that WERE proven live — D1, D3,
D4, D4b/D3b, D5, the sixth delegate site, the pad derivation across five engine versions — get no
row; they have `✅ [TAG]` blocks here instead. One new `### ⬜` batch, nine rows:

| | |
|---|---|
| **SW1** | D2 — no fixture exists and the repo says so; nothing in the pipe can make a worker throw |
| **SW2** | the 14 clipboard delivery sites — gate 17b executes nothing, and 9 shipped with no test changes |
| **SW3** | CE Lua untick ×3 + Invoke's `onUnreadable` — assertions over generated text no interpreter runs |
| **SW4** | the CE-side delegate pad — never pasted into Cheat Engine, and only a CHECKED build can test it |
| **SW5** | `PropertyXrefDialog`'s three push branches |
| **SW6** | the new refusal arms, reachable only on an engine whose delegate ElementSize is neither candidate |
| **SW7** | the `(stale)` narrowing's stale ARM — the unbound half is verified, the collected-target half is not |
| **SW8** | `GetMapPairLayout` — proven, but OFFLINE by construction; listed so nobody re-opens it expecting a boot |
| **SW9** | UE4 has no READ test — the survey walks class tables and nothing spawns the fixture actor |

⚠ Every row names the observable on **both** sides, because the register's charter says a row whose
acceptance names only what the screen shows is under-specified. The pinned
`open_verification_batches` count went 7 → 8 and both copies were updated **from the tree** —
`check_derived_counts` failed first and named the second copy, inside the register itself.

### ⭐ And four names removed from the long-tail heading — all already closed

`Dump Explorer identity gate` (PASS at :6291), `Solide L2` (`[SOLIDE-L2-2026-08-21]`), `Solide L3`
and `L4` (`[SOLIDE-L3L4-2026-08-23]`). The sweep found this in round 3 and wrote *"fix when next
editing the register"* — and then never edited it. ⚠ Each was verified against its own closure
before removal rather than taken from the report that flagged them; the report said "two stale
names" and the file had four.

## ✅ Two of the reconciliation's four, and the gate holes measured `[RECON-TAIL-2026-09-09]`

Taken fastest-first, as asked.

### 1. `PropertyXrefDialog` — a green label over a push that never happened

```csharp
await bridge.CreateMemoryRecordAsync(...);      // Task<bool>, DISCARDED
await bridge.NavigateDisassemblerAsync(bareHex); // Task<bool>, DISCARDED
_statusLabel.Text = $"Pushed {x.FunctionName} → CE disassembler @ {codeAddr}";  // green
```

⛔ **The `catch` below could never save it**: `AobMakerBridgeService` returns **false** rather
than throwing when the CE side is gone (`ReconnectAsync` fails → `IsAvailable = false` →
`return false`). So the sweep's round 3 recorded *"IAobMakerBridge: 0 DEFECT, family closed"*
while this site was open.

⚠ The two calls are **not interchangeable**, so the message now says which failed: the record is
what the user right-clicks, the navigation is what they look at. A record with no navigation is a
usable half (amber); a navigation with no record is not (red).

⭐ **Swept, not spot-fixed**: every `Task<bool>` on `IAobMakerBridge` was checked at every call
site. The only other discards are `CheckAvailabilityAsync()`, which is called for its side effect
— the next line reads `IsAvailable`. Those are legitimate and were left alone.

### 2. Gate 17a — a trailing comment decided the verdict, in BOTH directions

The check skipped whole-line comments but never stripped **trailing** ones, so the raw line fed
every pattern:

* `memrec.Active = false  -- deferred, see createTimer above` was **exempted**, because
  `SAME_LINE_TIMER` matched the word inside the comment. A real immediate untick, waved through
  by a sentence about one.
* `foo()  -- memrec.Active = false` would have been **flagged**, on a line that emits no untick.

`marker_of` already strips trailing comments one function up, for exactly this reason; the body
did not. Now stripped once and used for every test, while the *reported* snippet stays the raw
line so a human still sees what is in the file. **16 selftests** (was 13); mutation — restore the
raw line and exactly the two new cases go red, one per direction. Tree unchanged at 13 sites.

### 3. Gate 17b — the hole was real, and MEASURING it is what kept the fix honest

`DELIVERY_ARG` matched the identifiers the 14 known defects happened to use
(`xml|script|aaScript|ceXml`), so a future payload named `lua` / `payload` / `snippet` would have
walked past. *"The correct count is 0 from here on"* was true of those names, not of the shape.

⚠ **Widening a predicate is exactly how this kind of gate becomes an allowlist** — the thing round
1's design was refuted for. So it was widened FIRST and the tree re-run before anything was
committed to: the extra names produce **two** new hits, and both are the **declaration** and the
**definition** of `CopyToClipboardAsync` itself, whose parameter is named `text`. **No real call
site appears.** The legitimate population is still empty, so the names stay and the two signatures
are excluded by construction.

⚠ The declaration skip was written the wrong way round first — `statement_of` returns the text
**before** the call (that is how `CONSUMED` works), so anchoring on the call itself matched
nothing and both cases still reported a hit. The selftest is what said so. It now anchors at the
end of the prefix; a real call cannot collide, because `Task<bool> t = _platform.Copy…` has a
prefix ending in `t = `, not in `>`.

**15 selftests** (was 10). Two mutations, each red on its own two cases and no others: narrowing
the names back fails the `lua`/`payload` cases; dropping the declaration skip fails the two
signature cases. Tree unchanged at **46 sites, 0 violations**.

### ⬜ Not done, and why

* **verification-register rows** — next, and a different kind of work: the register has a charter
  and rows name an acceptance test, so this is writing, not patching.
* **~166 unadjudicated claims** — the slowest of the four by a wide margin. It is a sweep round,
  not a fix: 209 filed across three rounds, 43 skepticised, and the sweep then closed itself
  (`todo.md:1478`, *"THE SWEEP IS FINISHED. Do not run a round 4."*). Unadjudicated is neither
  fixed nor cleared.

## 🔎 The ~166 unadjudicated claims, SLICED `[CLAIMS-SLICE-2026-09-09]`

### ⛔ First, what they are NOT: a list

209 claims were filed across three sweep rounds — **61 + 112 + 36** — and **43** were skepticised
— **13 + 18 + 12**. Only those 43 were written up, as the Confirmed / Refuted tables above. The
other **166 exist as counts and nothing else**: the finder agents' output was never stored, and
`docs/evidence/` holds none of it. ⚠ So "adjudicate the 166" cannot mean walking a list, and any
plan that says it does is describing work that cannot start.

⭐ What IS walkable is the POPULATION they were drawn from, and round 1's own plan
(`todo.md`, "1. / 2. / 3." under the round-1 section) already split it by the QUESTION each half
needs. Re-measured at HEAD rather than quoted — the published 213 / 54 / 165 were build 3423:

| slice | population at HEAD | the question | status |
|---|---|---|---|
| **A** CE Lua emission layer | 16 generators → **5** candidates + 3 controls | does it report the ATTEMPT, or re-read the EFFECT? | ✅ **1 defect / 8**, fixed |
| **B** discarded qualified calls, non-`ReadSafe` | **133** (declarations, logging and `std::` excluded) | shape A — is an effect's status return dropped? | ✅ **scored, 7 walked**, 3 defects fixed; the GATE stays refused |
| **C** discarded `Read*Safe` | **135**, of which **15** reach a published field | does the UN-SET out-param feed a COUNT or a PUBLISHED field? | ✅ **all 15 adjudicated**, 3 defects fixed |
| **D** the 30-item "unverified tail" | no list exists | — | ✅ **closed by decision** — a round 4 the sweep forbade |

⚠ **B's 133 is my filter, not a reproduction of the published "~54".** Mine counts bare qualified
calls with declarations, `Sein::Info/Warn/Debug/Error` and `std::`/`memcpy` excluded; theirs was a
different cut of a 213-statement set. Neither number is wrong; they are not the same measurement,
and quoting one as the other is how counts drift in this file.

### ✅ Slice A — adjudicated 2026-09-09: 1 defect in 8, found and fixed

The mechanical survey is what made this cheap. Of 16 `*ScriptGenerator.cs`, only **5** write a
mailbox command without obviously re-reading `OffResult` afterwards, measured by four marks
(`AppendMailboxWait` / `AppendIdleWait` / a `readInteger(... OffResult` / a `< 0` gate). Those 5
plus 3 clean controls went to one adjudicating agent each, against the reference
(`SeeThroughScriptGenerator.cs:85-100` — wait, re-read `OffResult`, gate on `state < 0`, deferred
untick) and against CLAUDE.md's two rules verbatim.

**7 of 8 came back CLEAN** — including `CoordLibrary`, whose zero `OffResult` re-reads was the
strongest mechanical signal in the survey, and which turned out to re-read through a shared
`call()` helper the survey could not see. The four things that explain the survey's suspicion —
each of which has killed a real claim in this repo before — are why the marks are not verdicts:
`CeLuaHygiene` already emits the diagnosis and the untick for some callers; a MOMENTARY action's
deferred untick is the correct shape and not a missing one; a bail-out BEFORE the command is
written has no effect to re-read; and a re-read spelled `~= 0` is still a re-read.
⭐ **The eighth is a real defect, and it is in the file whose OTHER arm is the clean model.**
`TeleportScriptGenerator.GenerateClearAll` — the "Clear all markers" row — never read
`OffResult`; the file's sole occurrence is at `:165`, inside the single-op `Generate`. And
`AppendMailboxWait` cannot cover it: the helper polls `OffStatus` **only**, while `Mimic.cpp`'s
`SetError` writes the negative code to `result` and THEN sets `status = STATUS_DONE` — so a
REJECTED command satisfies that wait exactly like a successful one. The row therefore auto-closed
the Lua Engine window (this repo's documented clean-success-ONLY signal) and unticked silently.
Fixed, with a red-before-green test (`Teleport_clear_all_reads_the_RESULT_back_not_just_the_status`
— removing the re-read fails exactly that one test of 76).

#### ✅ VERIFIED LIVE 2026-09-09 — `[TG1-CLEARALL-RESULT-2026-09-09]`

Build 3496, AOT-trimmed `dist` (54.8 MB), DumperTest Development, UE 5.4, 25,231 objects, real
Cheat Engine attached, the row pushed by the **shipped** path (`Add action records to CE` →
`AOBMaker Connected` → "Added 27/27"). Rig: `tools/verify/tg1_clearall_result_gate.py`.

⭐ **THE MANUFACTURE MAKES THE DLL REJECT IT FOR ITS OWN REASON.** The emitted loop writes the
slot itself (`writeQword(mb + 0x18, slot)`) and `HandleTeleport` reads it back **after** it has
seen `cmd != 0` — there is no atomic snapshot. So a tight `WriteProcessMemory` loop puts **99**
in that word inside the sub-millisecond window, and `Wirbel::ClearMarker`'s shipped bounds check
(`slot < 0 || slot >= TELEPORT_SLOTS`) returns `TP_ERR_EMPTY_MARKER` (-6). Nothing is faked: not
the result word, not the status, not the error string. It won on the first tick (282,956 writes
at ~159 µs each). ⛔ Not through the pipe's `write_mem` — a JSON round-trip is ~0.2 ms, the same
order as the window itself.

| observable | clean control | manufactured |
|---|---|---|
| DLL `pipe-0.log` | `op=6 slot=0 -> rc=0`, `slot=1 -> rc=0`, `slot=2 -> rc=0` | **`op=6 slot=99 -> rc=-6`, and that ONE line only** |
| CE dialog | none | **`[Teleport] slot 0 was NOT cleared (code -6)`** |
| Lua Engine window | **auto-CLOSED** | **STAYED OPEN** |
| the record | unticked | unticked (correct — momentary, deferred untick) |

⭐⭐ **THE MAILBOX IS THE WITNESS THAT STATES THE BUG.** Read live at the moment of failure:

    cmd=0  status=1  result=-6  op=6  slot=99
    errorMsg='Teleport: op=6 slot=99 failed code=-6'

**`status=1` is `STATUS_DONE` while `result=-6`.** That is exactly the publication order the fix
exists for — `SetError` writes the code to `result` and THEN sets `status`, and
`AppendMailboxWait` polls `OffStatus` **only**. The pre-fix row was satisfied by this state and
called it success. It is no longer a code-reading argument; it is a measurement.

⭐ **ONE LINE, NOT THREE, IS THE PROOF THE `break` FIRED** — the loop is `for slot = 0, 2`, so a
gate that merely warned would have left `slot=1` and `slot=2` lines behind it. And the dialog says
**slot 0** (what the Lua asked for) while the DLL says **slot=99** (what it read): that divergence
is what proves the clobber landed and that CE is not echoing our write back at us.

⛔ **THE AUTO-CLOSE ARM NEEDED ITS OWN CALIBRATION, AND ALMOST WENT UNMEASURED.** At `DEBUG == 0`
the row prints nothing, so CE never opens the Lua Engine window on its own — and
`getLuaEngine().Close()` on a window that was never shown is a no-op. "The window stayed open"
would then have been vacuous. So the window was opened by hand first and the pair run BOTH ways:
clean → gone from the window list; manufactured → still there. The close is armed; it is the
error path that makes it unreachable.

⭐ **RESTORATION IS PROVEN BY THE PATH, NOT BY A READ-BACK.** `restore` writes 8 zero bytes and
reads them back (`slot word read-back: 0 OK`, the `mutate_guard.py` discipline), but the real
proof is the third tick: `slot=0/1/2 -> rc=0` again with the window auto-closing again. Nothing
persistent was ever armed — `+0x18` is a per-command INPUT word every later command rewrites, and
the bounds check runs BEFORE `s_markers[slot]` is indexed, so 99 is refused, never dereferenced.

⚠ **WHAT THIS DOES NOT CLAIM.** The manufacture proves the GATE works when the DLL rejects the op.
It does not make the defect reachable in normal use — the paragraph below still stands, and that
is why this is filed as a latent defect fixed, not as a live bug found.

⚠ **LATENT, NOT LIVE — and the two refutations disagreed about exactly this.** The
code-side refutation could not kill it; the user-visible one could, and was right on the facts:
`Wirbel::ClearMarker` returns `TP_OK` for every slot in `[0, TELEPORT_SLOTS)` and the loop is
`for slot = 0, 2`, so the op cannot fail today; and `SetError(-10, "DLL not initialized")` gates
every CMD_TELEPORT including the SAVE that is the only writer of `s_markers`, so whenever it can
fire there are no markers to clear. Fixed anyway, because **nothing enforces either of those** —
they are properties of today's `ClearMarker`, not guarantees — and the claim was being made
from the ATTEMPT while three lines away the same file made it from the EFFECT.

⭐ **AND THE RESULT CALIBRATED ITSELF**, which is the part worth keeping. Going in, the worry
was the one the sweep's own instrument section records three times — *"if it cannot see the bug
that motivated the sweep, the list is worthless"* — and there was no live positive left in this
family to calibrate against, round 1's having been fixed into gate 17b's population. The Teleport
result answers it: the same adjudicator, given the same prompt, called seven files clean and then
separated two paths **inside one file**, keeping `Generate` and condemning `GenerateClearAll`.
An instrument that can do that is not one that says "clean" by default.

### ✅ Slice C — ADJUDICATED 2026-09-09: 15 of 15, 12 CLEAN, 3 defects fixed

`tools/verify/claims_readsafe_outparam.py`. ⛔ For this population "the bool was discarded" is NOT
the question — the discard is the documented idiom (the identical `ReadPtrAt` body appears
verbatim in `Edel.cpp`, `Solide.cpp`, `Solitar.cpp`, `Hemmung.cpp`) and a blanket rule opens with
~135 waivers. The question round 1 wrote down, and the one the tool ranks by, is:

> does the UN-SET out-param go on to feed a **COUNT**, or a **PUBLISHED field**?

Because that is the shape confirmed here four times in 2026-09: the delegate readers publishing
`(unbound)` off a faulted `objIdx`; `GetMapPairLayout` guessing an alignment off a 0 struct addr;
`WalkInstance`'s two inlined copies doing the same silently; and `delegate_pad` set but never
emitted. Ranked at HEAD: **PUBLISHED 15 · COUNT/SIZE 18 · CONTROL-FLOW 30 · PASSED-ON 35 ·
no further use 37**.

⚠ **The tool is a RANKER, not a verdict**, and its own output says so. Most of the 15 are
`rawElemSize` / `rawKeySize` / `rawValSize`, which flow straight into `ValidateArrayElemSize` — a
real guard that overrides an invalid size for every type `InferScalarSize` knows. The delegate
family is the one it deliberately does NOT override, and that gap is already closed downstream by
`DelegatePadFromElementSize` refusing 0 (`[SW6-STRIDEREFUSAL-2026-09-09]`).

#### The three that are NOT that shape — adjudicated by hand, 2026-09-09

| site | out-param | verdict | why |
|---|---|---|---|
| `Aura.cpp:6366` | `scriptNum` | ✅ **CLEAN** | the very NEXT statement is `if (!scriptData \|\| scriptNum <= 0 \|\| scriptNum > (1<<22))`, which tests for exactly the 0 a faulted read leaves, and falls back to the x64-disassembly path while honestly setting `out.method = "disasm"`. **This is the exemplar of the correct shape** — the discard is fine precisely because the guard is one line away. |
| `Ubel.cpp:2233` | `enumPtr` | ✅ **CLEAN** | 0 means "no enum", and every consumer guards on it: `Fern.cpp:1687` emits `enum_addr` only `if (fv.enumAddr != 0 && !fv.enumEntries.empty())`, and `Aura.cpp:5071` treats a 0 as *not yet resolved* and RETRIES the read. A faulted read costs a dropdown, not a false claim. |
| `Ubel.cpp:4307` | `objPtr` / `ifacePtr` | ⛔ **DEFECT (LOW), fixed** | both reads were discarded and the hex column was then built unconditionally, so an UNREADABLE `InterfaceProperty` rendered `0000000000000000 0000000000000000` — an affirmative claim about memory nobody could read, indistinguishable from a genuinely null interface. |

⭐ The fix for the third mirrors the correct shape **already in the same file**:
`ReadDelegateArrayElements` builds its hex INSIDE `if (Macht::ReadBytesSafe(...))`. The field now
publishes hex only when both reads succeeded, and otherwise says
`"(interface — unreadable at +0xN, not read)"`.

⚠ 1 of 3 is roughly the kill rate this file predicts for an agent sweep, and it held for a
hand pass too. The two CLEAN verdicts are worth as much as the defect: they are what stops the
next reader from "fixing" a guard that is already one line below the read.

#### ✅ VERIFIED 2026-09-09 — `[IFACEREAD-2026-09-09]`, and the live arm alone could NOT have done it

⛔ **The live arm is real evidence and it is NOT sufficient — say both.** Run on DumperTest
(build 3487, `dist` sha `C70AAE75`), 33 declared `InterfaceProperty` fields, walking a live
`GeometryCollectionComponent`: `CustomRenderer` published
`hex='0000000000000000 0000000000000000'` and carried no refusal string. That shows the fix did
not break the readable path — and **nothing more**. `CustomRenderer` is a *genuinely null*
interface, so its zeros are correct; readable-null and unreadable are the two states the defect
CONFUSES, and a live game hands you only the first. Reporting that run as "PASS" without this
paragraph would have been the claim-from-the-attempt the whole slice exists to catch.

⭐ **So the deciding arm is manufactured**, `dll/tests/dll_core_test.cpp` `IFACEREAD`, built the
way TMAPGEOM was and for the same reason: a wholly-dead pointer never reaches this branch
(`WalkInstance`'s `IsAddrReadable` gate bails first), so only a **partial** fault exercises it.
Two pages reserved, one committed, the instance at the base, and a fake UE4 pool + FField chain so
`GetFieldTypeName` answers `InterfaceProperty` — then four cases at four offsets, one class blob
each (`s_walkClassCache` is keyed by class address and nothing erases it):

| offset | state | must publish |
|---|---|---|
| `+0x100` | bound, both halves readable | `1122334455667788 99AABBCCDDEEFF00`, no refusal |
| `+0x200` | **readable NULL** — the live case | `0000000000000000 0000000000000000`, no refusal |
| `+0xFF8` | ⭐ **half** — ObjectPointer reads, InterfacePointer faults | no hex, refusal naming `+0xFF8` |
| `+0x1000` | wholly unreadable | no hex, refusal naming `+0x1000` |

**RED-BEFORE-GREEN, both fixes, each mutation restored in a `finally`** (79 checks green at HEAD):

- *mutant `gate`* — hex built unconditionally, i.e. the pre-fix shape → **5 FAIL**, and they print
  the defect verbatim: the half case `got: 1122334455667788 0000000000000000` (a REAL pointer
  followed by eight bytes it never read) and the discriminator
  `got: 0000000000000000 0000000000000000 vs 0000000000000000 0000000000000000`.
- *mutant `offset`* — `%X` back to `%d` → **2 FAIL**, `got: +0x4088` / `got: +0x4096`.

⭐⭐ **The one check that states the defect** is `readable-NULL and UNREADABLE are no longer the
same output`. Every other assertion here can be satisfied by some partial fix — this one cannot,
because it asserts the two states are *tellable apart*, which is the entire user-visible claim and
exactly what the live run could not decide. ⛔ Its companion is the **anti-vacuity** guard: every
⭐ assertion is of the form "hexValue is EMPTY", which a walk that produced NO FIELDS satisfies for
free — so the extractor asserts `r.fields.size() == 1` before returning, on every case.

⚠ **AND THE FIXTURE FOUND A SECOND DEFECT — IN THE FIX.** The refusal first shipped as
`"+0x" + std::to_string(fi.Offset)`, i.e. **decimal digits behind a hex prefix**: offset `0xFF8`
rendered as `+0x4088`. The refusal exists to stop the walker making an unbacked claim about an
address, so stating the wrong address *in the refusal* is the same class of defect it was written
to remove. Found only because the assertion names the offset the field is actually at — a test
that had merely grepped for "unreadable" would have gone green over it. Now `snprintf("%X")`.

⚠ `IFACEREAD` calls `Serie::InitUE4` and therefore joins TMAPGEOM in this file's **pool-faking
tail**; it installs its OWN chunks so it depends on nothing TMAPGEOM leaves behind, but nothing
that needs the real pool may be appended after either. The banner on TMAPGEOM now says so.

#### ✅ The remaining 12, adjudicated 2026-09-09 — `[UNREADVAL-2026-09-09]`

Re-ranked at HEAD first: **133** discarded `Read*Safe` statements, **14** PUBLISHED (was 15 —
`Ubel.cpp:4307` left the population when the InterfaceProperty fix consumed its two returns).
Twelve had not been walked. **10 CLEAN, 2 DEFECT**, and reading the two defects found the same
shape at four more reads the ranker never scored.

**The eight size sites are ONE verdict, not eight** — `Ubel.cpp:4396`/`4607` (array inner),
`4781`/`4782`/`4969`/`4970` (map key+value), `5155`/`5236` (set element). Every one feeds
`ValidateArrayElemSize`, and a faulted read's 0 goes exactly two ways there: for a type
`InferScalarSize` knows it is **overridden** by the authoritative constant, and for one it does not
(StructProperty, Delegate, Soft…) it returns **0, the "don't know" sentinel** — which every
consumer already gates on. Traced end to end rather than asserted: the array readers gate
`fv.arrayElemSize > 0` (phases B/D/E/F); `Fern.cpp:1519` will not emit `array_elem_size` at 0;
`ReadSoftObjectArrayElements` warns and derives a fallback for anything under 0x18;
`ReadDelegateArrayElements` **refuses** (`[SW6-STRIDEREFUSAL-2026-09-09]`);
`CeXmlExportService.cs:3300`/`:3474` return early on `<= 0`; and `LiveWalkerViewModel.cs:1557`
says out loud *"element data could not be read"*. ✅ **CLEAN** — 0 is honest here, not a claim.

⚠ One asymmetry noted and deliberately left: `Fern` gates `array_elem_size` on `> 0` but emits
`map_key_size` / `map_value_size` / `set_elem_size` unconditionally, so a 0 does cross the wire
for maps and sets. It is not a defect — every consumer gates — and changing it would drop a field
older UI builds read. Recorded so the next reader does not have to re-derive it.

| site | out-param | verdict | why |
|---|---|---|---|
| `Ubel.cpp:5515` | `enumPtr` | ✅ **CLEAN** | `if (enumPtr)` is the very next statement — the `Aura.cpp:6366` exemplar shape |
| `Ubel.cpp:6839` | `rowStructAddr` | ✅ **CLEAN** | `if (!rowStructAddr) { result.error = "RowStruct not found or null"; return; }` four lines below, and the message does not claim WHICH |
| `Ubel.cpp:5522` | `rawVal` | ⛔ **DEFECT (LOW), fixed** | ByteProperty-with-UEnum published `hexValue = "00"` and the **NAME of enumerator 0** for a byte nobody could read |
| `Ubel.cpp:5732` | `ptr` | ⛔ **DEFECT (LOW), fixed** | TOptional published `"(unset)"` — an affirmative claim that the option provably holds no value |

⭐⭐ **AND THE RANKER SAW ONE READ OF TEN.** `Ubel.cpp:5732` is one of **six** discriminator reads
in that TOptional block; the other five feed a bare `isSet = (...)`, which none of the tool's tiers
score, so they came back `unused?`. The `EnumProperty` handler 200 lines above has **four more** of
the identical shape, and the tool never saw them either — its `LASTARG` regex takes the call's last
argument, and those are written `{ uint8_t v = 0; Macht::ReadSafe(addr, v); rawVal = v; }`.
⚠ **This is the ranker working as documented, not failing**: its header says it reports *where the
question is worth asking*. It pointed at the right function; a human read the function. A tool that
had "covered" the file would have closed nine live reads as clean.

⛔ **THE TWO DIRECTIONS, because the TOptional block gets BOTH wrong.** Four arms
(object / weak / text / trailing-flag) fall to `isSet = false` on a fault and publish `"(unset)"`.
The **FString and FName arms fail the other way**: their unset sentinels are `-1` and `0xFFFFFFFF`,
so a faulted read's 0 reads as **SET**, and the field then publishes `""` ("set but empty") or
`"(set)"`. Both are claims about memory nobody could read. The refusal is therefore evaluated
**before** `isSet` is consulted at all — whichever way the sentinel test happened to fall.

⭐ **THE TELL WAS INSIDE EACH HANDLER.** All three build their hex column inside
`if (Macht::ReadBytesSafe(...))` while building the VALUE unconditionally — so the two columns
already disagreed. And `BoolProperty`, three blocks above the enum handlers in the same loop, has
always had it right. The fix makes the value side match what the hex side always did.

**THE FIX**, and it is one definition rather than four: `Ubel::DescribeUnreadableField(what,
offset)` in `Ubel.h`, header-inline and pure so `dll_helpers_test` pins it. The InterfaceProperty
site now routes through it too, which is what puts the `%X`-not-`%d` lesson somewhere it cannot be
re-learned per site. The enum handlers **keep** `enumAddr` / `enumEntries` — the `UEnum*` was read
from the FField, not from the instance, so the CE DropDownList metadata is still sound; only the
value is refused, and `enum_name` / `enum_value` are already gated on `enumName`.

**RED-BEFORE-GREEN — 6 mutations, each restored in a `finally` and byte-compared** (108 checks
green at HEAD, was 79; `dll_helpers_test` 2428, was 2424):

| mutant | result |
|---|---|
| `enum` — drop the EnumProperty refusal | **2 FAIL**, `got: 0` and `got: 0 vs 0` |
| `byteenum` — drop the ByteProperty refusal | **3 FAIL**, incl. `got: 00` |
| `optional` — drop the TOptional refusal | **2 FAIL**, `got: (unset) vs (unset)` |
| `enumhex` — drop the enum hex gate alone | ⚠ **GREEN** |
| `enumboth` — drop BOTH, i.e. the pre-fix shape | **3 FAIL**, `got: 00` + `got: 0 vs 0` |
| `offsetfmt` — `%X` → `%d` in the shared formatter | **4 FAIL in `dll_helpers_test` + 5 in `dll_core_test`**, across all four families |

⚠ **`enumhex` GOING GREEN IS A RESULT, AND IT IS REPORTED RATHER THAN TIDIED AWAY.** The enum hex
gate is unreachable on its own, because the refusal `continue`s before it. It is not dead code —
under the `enum` mutant it is precisely what keeps the hex column empty — but it is a *second*
gate, and only `enumboth` reproduces the shipped defect. Stating that is the difference between
"6 mutations, all red" and what actually happened.

⭐ `offsetfmt` is the cross-check that the single definition is genuinely single: **one** edit,
one line, goes red in the helper test AND in all four page-edge families (`IFACEREAD` +
`UNREADVAL`'s three), printing `+0x4088` / `+0x4096` — the decimal digits behind a hex prefix that
started this.

⚠ **WHAT THIS DOES NOT CLAIM.** No live arm. A partial fault is manufactured, and — as `IFACEREAD`
established — it has to be: a wholly-unmapped instance bails at `WalkInstance`'s readability gate
for a different reason, so a live game can only ever hand you the readable-null side of the very
ambiguity being fixed. The `UNREADVAL` fixture therefore carries a readable-**nonzero** and a
readable-**zero** control for each of the three families, so a "fix" that simply blanked the field
would fail its own controls.

⚠ `UNREADVAL` joins `TMAPGEOM` and `IFACEREAD` in this file's **pool-faking tail** — it calls
`Serie::InitUE4` with its own chunks. Nothing needing the real pool may follow any of the three;
the `TMAPGEOM` banner now names all three.

#### ✅ LIVE REGRESSION ARM 2026-09-09 — `tools/verify/unreadval_live_arm.py`

Run on **BOTH flavours**, `shipping` first per handover §4 rule 5. Build 3508, UE 5.4, fresh
`dist` DLL over the pipe (no UI): **Shipping** 24,497 objects · **Development** 25,231.

⛔ **THE RISK THIS ARM EXISTS FOR IS NOT THE FAILURE CASE.** The fix adds a gate to three
handlers that run on every walk; the failure it guards is rare, but a gate that fired
**spuriously** would blank every enum in the game. The manufactured `UNREADVAL` controls prove
only that it does not fire on three synthetic fields.

| family | fields seen | published a value | genuinely 0 | refusals |
|---|---|---|---|---|
| `EnumProperty` | 1263 | **1263** | 779 | **0** |
| `ByteProperty` | 1113 | **1113** | 680 | **0** |
| `OptionalProperty` | 15 | **15** | 7 | **0** |

**2391 fields over 2105 live instances, not one refusal**, and 1466 of them genuinely read 0 —
the state the refusal must never be confused with. The **Shipping** run agrees:
**2356 fields over 2074 instances, 0 refusals**, 1445 genuinely 0 (`EnumProperty` 1246/1246,
`ByteProperty` 1095/1095, `OptionalProperty` 15/15).

⚠ **AND THE TWO RUNS ARE CORROBORATION, NOT TWO INDEPENDENT MEASUREMENTS OF THE OFFSET SHAPE** —
say which, because that is the whole reason the Shipping-first rule exists. Measured, not assumed:
the runtime `get_offsets` table is **byte-identical** across the two flavours on this fixture
(`ffield_class` 8, `ffield_name` 32, `ffield_next` 24, `ffieldclass_name` 0,
`fproperty_elemsize` 52, `fproperty_flags` 56, `fproperty_offset` 68, `ustruct_childprops` 80,
`ustruct_propssize` 88, `use_fproperty` true). So on **DumperTest 5.4** the Shipping run confirms
the Development one rather than probing a different layout; the rule's premise is about REAL
titles, and this fixture does not exhibit the difference it warns about. A row that needs an
offset-shape difference still needs a real title. ⭐ `CharacterMovementComponent.MovementMode`
is itself one of them, publishing `MOVE_Walking` / hex `01` through the exact `ByteProperty`-
with-UEnum handler that was fixed.

⭐ **THE SAMPLE IS TARGETED *AND* BROAD, because targeting alone is a lottery.** The first run
strided the object pool and reported **zero** `OptionalProperty` — the only two classes that
declare one are `DumperTestActor` (**2** instances in 25,231 objects) and
`WorldPartitionRuntimeCellData`. ⛔ **The anti-vacuity guard failed that run**, which is the only
reason it was noticed instead of published as "no refusals anywhere". The rig now asks
`search_properties` which classes declare each family, pulls their instances, and strides the
pool on top.

⚠ **AND ONE DETOUR WORTH KEEPING.** The tally first reported **17 fields with neither value nor
hex**, which reads exactly like a walker declining silently. It is not: when the walked object IS
a `UClass`/`UScriptStruct`, `WalkInstance` (`Ubel.cpp:4031`) takes a **definition branch** that
emits field METADATA and deliberately reads no values — its own comment says the offsets describe
*instances* of the struct, not the metaobject. It returns before any type handler runs. The tell
was the ratio: **77 of 96 fields on one such object were valueless across FOURTEEN type
families**, of which this fix touched three. The rig now excludes `is_definition` walks and
reports them separately (924 of them). ⭐ It also explains why a live game cannot reach the
refusal: the only objects whose high-offset reads fault are metaobjects, and those never get
past the definition branch.

⚠ **WHAT THIS DOES NOT CLAIM.** The refusal path itself is untouched by this arm — that is
`dll_core_test`'s `UNREADVAL` block, and it has to be. Nor does it say anything about how the UI
renders a refusal.


### ✅ Slice B — ADJUDICATED 2026-09-09: 7 sites, 2 defects fixed, and the gate stays refused

⛔ **THE PARAGRAPH THAT USED TO BE HERE WAS WRONG, AND THIS FILE CONTAINED THE REFUTATION.** It
said the work was writing an **allowlist of ~25 effect-appliers** into `tools/effect-appliers.tsv`
and baselining a gate on it. But `#### ⛔ Gate #17 as proposed in round 1 is REFUTED — do not
build it` is *four hundred lines above*, and `tools/check_clipboard_delivery.py`'s own header
repeats the verdict in shipped code: *"the round-1 proposal here was an allowlist of ~25 'effect
appliers'; it scored 0 of 10 findings"*. Slice B was written off round 1's plan without
reconciling it against round 2's ruling on that plan. ⚠ **Two sections of one file disagreeing is
the same failure mode as a stale count** — and the reason it survived a day is that the slice
table's "status" column said *needs the list*, which reads like work rather than like a decision
already taken.

⭐ **SO IT WAS SCORED RATHER THAN ARGUED.** The allowlist was built ONCE, from the module
**headers** (every public entry point whose return says whether an effect landed; readers and the
ProcessEvent param-buffer packers excluded by name, exactly as round 1 specified) — **33 names** —
and run. `tools/verify/claims_effect_applier.py` is that score, kept so the next person to propose
the allowlist re-runs it instead of re-arguing it.

| | round 1 predicted | measured at HEAD |
|---|---|---|
| discarded call sites | 23 | **7** |
| defects | — | **2** (+1 hygiene, see the Wirbel correction below) |
| waivers a gate opens with | "one baseline line per new site" | **4 — 57% false positive** |

⛔ **AND THE FOUR CLEAN ONES ARE THE ARGUMENT, not the leftovers.** Three of them are in ONE
handler — `Fern.cpp`'s `fly_set` — which drops `SetSpeed` / `SetPreset` / `SetNoclip` and is
correct anyway, because the next thing it does is `Dunste::GetStatus(st)` and it answers from
`st`. That is this repo's rule (*report the EFFECT, not the ATTEMPT*) followed exactly, and a
checker keyed on "the status was discarded" **flags the handler that follows it best**. Seven
sites do not buy a curated TSV, a baseline file and a gate; they buy an afternoon of reading.

| site | verdict | why |
|---|---|---|
| `Fern.cpp:1232` `Schlacht::SetEnabled(false)` | ✅ **CLEAN** | last-client teardown — there is no client left to make a claim to, and the comment already calls it a no-op when see-through was never enabled |
| `Fern.cpp:6021` `Dunste::SetSpeed` | ✅ **CLEAN** | cannot fail (clamps, returns `FR_OK`), and the response re-reads `GetStatus` |
| `Fern.cpp:6023` `Dunste::SetPreset` | ✅ **CLEAN** | **can** fail (`FR_ERR_REFLECT`), but the response publishes `st.preset` — so a rejected preset shows the OLD one. The effect, not the attempt |
| `Fern.cpp:6025` `Dunste::SetNoclip` | ✅ **CLEAN** | same; `st.noclip` is re-read |
| `Dunste.cpp:726` enable | ⛔ **DEFECT, fixed** | see below |
| `Dunste.cpp:750` disable restore | ⛔ **DEFECT, fixed** | see below |
| `Wirbel.cpp:618` deep-force | ⚠ **HYGIENE, not a defect — see below** | `rewrote++` ran whether or not the write landed, but round 2 **refuted this exact line** |

#### ⛔ The two Fly defects — `[SLICEB-FLY-2026-09-09]`

`Dunste::SetEnabled` writes **one byte** (`UCharacterMovementComponent::MovementMode`) and that
write **is** the effect; `active` / `baseCaptured` / `capturedPawn` are bookkeeping. Both call
sites dropped `Macht::WriteBytes`' result, which returns false when `VirtualProtect` refuses a
freed or unmapped page (a pawn destroyed between `ResolveCtx` and the write) or the memcpy faults.

- **Enable** returned 1, logged `Fly: ENABLED` and started the worker over a pawn that never left
  its old MovementMode. Now rolls the bookkeeping back and returns `FR_ERR_WRITE` **before**
  `StartWorkerLocked()`, so nothing is armed.
- **Disable is the worse half**: it cleared `active`, stopped the worker and logged
  `Fly: DISABLED` while the pawn stayed in `MOVE_Flying` — the feature reporting OFF over a pawn
  still flying, with nothing tracking it. That is the `[FREEZESTUCK-2026-08-18]` shape. Now warns
  with the address and returns `FR_ERR_WRITE`; the completion log says which of the two happened.

⭐ **`FR_ERR_WRITE = -10, // raw write failed` WAS ALREADY IN THE ENUM WITH NO PRODUCER**, which is
what a dropped status usually looks like from outside. ⭐⭐ **And the exemplar is in the same
file**: the worker's drift correction writes the same byte as
`if (Macht::WriteBytes(...)) ++s_state.driftCount;`, and the collision restore forty lines below
the disable path handles its own failure with a warning, a kept record and a polling retry.

⚠ **FOLLOWING THE FIX OUT TO THE USER FOUND TWO MORE, IN C#** — and they would have gone on lying
after the DLL started telling the truth. `ApplyFlyAsync` keyed its `✈ Fly ON` on **`st.HasCmc`**
(*"a CharacterMovement was RESOLVED"*), and `ResetFlyAsync` said `"Fly OFF."` **unconditionally**.
`FlyStatus.State` has carried the code across the wire the whole time. Both now report the effect.

**RED-BEFORE-GREEN on the C# half, each mutation restored in a `finally`** (4781 tests, was 4778):
`applyclaim` (key `Fly ON` off `HasCmc` again) → **1 FAIL**, `Assert.DoesNotContain: Sub-string
found`; `resetclaim` (unconditional `"Fly OFF."`) → **1 FAIL**, `Assert.Contains: Sub-string not
found`. Each mutant reds exactly its own test.

⛔ **THE DLL HALF REACHES NO TEST TARGET.** `Dunste.cpp` and `Wirbel.cpp` are among the
**21 of 31** `dll/src/*.cpp` that reach NO test target — the trap CLAUDE.md's `-Target Test`
warning documents — so a green `-Target Test` measures nothing about them, and the C# tests only
pin what the UI does *when the DLL reports* a failure, not that it reports one. That gap is why
the live arm below exists.

#### ✅ LIVE ARM 2026-09-09 — `tools/verify/sliceb_fly_arm.py`, 10/10 on DumperTest

Build 3508, UE 5.4, over the pipe with no UI, **10/10 on `shipping` AND on `dev`** — identical
table on both, including the `1 → 5 → 1` restore. Shipping is the one that counts (handover §4
rule 5); the `dev` run is the second opinion. ⛔ **The risk the fix carries is not the failure
case — it is the EARLY RETURN it adds to a path the user hits on every toggle.** A gate that
fired spuriously would stop Fly working at all.

⭐ **THREE WITNESSES, COMPUTED BY DIFFERENT CODE, AND THEY AGREE AT EVERY STEP**: `state` (the
value the fix decides), `active` (the bookkeeping the fix rolls back on failure), and
`current_mode` — `MovementMode` **read back from the CMC**, which is the effect itself and the
only one that is not our own bookkeeping.

| step | state | active | current_mode |
|---|---|---|---|
| baseline | — | false | 1 (`MOVE_Walking`) |
| enable | **1** | true | **5 (`MOVE_Flying`)** |
| disable | **0** | false | **1 — restored to the baseline** |
| enable / disable again | 1 / 0 | true / false | 5 / 1 |

The second cycle is not padding: the disable path sets `baseCaptured = false`, so a gate that
fires only on the SECOND enable is invisible to a single toggle.

⛔ **AND THE FAILURE ARM IS UNREACHABLE FROM OUTSIDE THE PROCESS — MEASURED, NOT ASSUMED.**
`tools/verify/sliceb_fly_fail_probe.py` is shipped as the evidence for that negative claim.
`Macht::WriteBytes` fails only when `VirtualProtect` is refused, i.e. the page is
MEM_FREE/MEM_RESERVE, and there are exactly two ways to arrange it:

1. **Decommit the page holding `cmc + modeOff`** — dead by measurement: `cmc = 0x2A3948CF010`,
   `modeOff = 513`, so `cmc + 0x10` and `cmc + modeOff` are on the **same page**, and
   `ResolveCtx` reads `cmc + 0x10` via `Ubel::GetClass`. Every page trick returns
   `FR_ERR_REFLECT` first — a different arm, and a false green if mistaken for this one.
2. **Point `modeOff` itself at unmapped memory** by patching `FPROPERTY_OFFSET` on the
   MovementMode FField. `ResolveCtx` resolves `modeOff` by REFLECTION and never reads the
   instance there, so it would still return `FR_OK` and the write would fail first. Tried, and
   blocked:

        patched to 4194304 (read-back 4194304)
        walk_instance sees offset=513 value='MOVE_Walking'      <- the CACHED layout
        fly_set enable -> state=1, active=true, current_mode=5  <- the write still landed
        RESTORED: offset reads 513  OK

⭐⭐ **THAT NULL RESULT REPRODUCES `[CLASSCACHE-FRONTED-2026-09-09]` FROM A SECOND, UNRELATED
EXPERIMENT.** `FindField` → `WalkClass` is memoised in a 2048-entry LRU that nothing invalidates,
so a patched FField is invisible to both `Dunste` and the walker. That note predicted this would
block "that whole family of layout experiments" and named only the two scalar delegate-refusal
arms; it now also blocks the Fly write arm **and** a live `ByteProperty`-refusal arm. The
`--force` / debug-only `invalidate_class_cache` it proposes would unblock all four at once —
which turns that open row from a tidiness item into the thing standing between four verification
arms and a measurement.

⚠ **THE SANITY GUARD IS WHAT KEPT THIS SAFE.** The first attempt read **675** at `FField+0x4C`
and refused to write: `Grimoire.h`'s `FPROPERTY_OFFSET = 0x4C` is a **compile-time default**, and
Genau derives the real one per title (`get_offsets` → **68**). Without the guard that would have
been a wild 4-byte write into a live game object.

⭐ **RESTORATION PROVEN BY THE PATH, not only by the read-back**: after the mutation was reverted,
both `sliceb_fly_arm.py clean` (10/10) and `unreadval_live_arm.py` (2391 fields, 0 refusals) were
re-run green on the same process.

⚠ **STILL NOT CLAIMED**: that the DLL returns `FR_ERR_WRITE` when the write fails. That arm has
no route from outside the process, and saying so is the honest end of this row rather than a
green tick over an untested branch.

⚠ **NOT FIXED, and deliberately**: `SetEnabled(false)` when `ResolveCtx` fails still returns 0
without restoring anything. That is not a dropped status — there is no pawn to write to — but it
does mean "Fly OFF." is said over a mode that was never restored because the pawn is gone. Left
because the alternative is claiming a failure we cannot distinguish from a legitimately absent
pawn, which is the defect this slice exists to remove, pointed the other way.

#### ⚠ CORRECTION, same day — `Wirbel.cpp:618` was REFUTED in round 2, and I re-raised it

⛔ **Round 2's `Refuted (8) — do not re-raise` list contains `Wirbel.cpp:619`**, and at build 3423
line 618 was the `Macht::WriteBytes` and 619 the `rewrote++` — *the same two lines*. The scorer
surfaced it as a fresh hit and it was fixed and committed as a DEFECT before the refuted list was
checked. Found while walking slice D, which is the section that carries that list.

⭐ **AND THE REFUTATION IS SOUND ON REACHABILITY, which is the part worth writing down.** No reason
was recorded for this row, so it was re-derived: **`ReadBytesSafe(c.root, buf, 0x400)` succeeds one
instruction above the loop, and every write target is inside that same window.** So the page is
mapped and readable at that moment, and `Macht::WriteBytes` can only fail if the region is freed
BETWEEN the read and the loop — the actor destroyed mid-teleport. That is a far narrower race than
the Dunste pair, where `ResolveCtx` and the write are separated by a mutex acquisition and a
resolve.

**The one-line change is KEPT, reclassified**, because it is strictly more correct at zero cost and
because the consequence is not only the count: `rewrote` selects between
`"deep-force rewrote %d world-transform vector(s)"` and the `else` branch's **`visual may not
move` WARNING**, so an all-refused run printed a confident N and suppressed the only signal an
operator debugging that title has. ⚠ But it is filed as **hygiene, not a finding**: Slice B's
confirmed count is **2**, and the round-2 verdict stands as written.

⚠ **THE PROCESS LESSON, and it is mine**: a mechanical scorer surfaces refuted rows as fresh hits,
because a refutation lives in prose and the code still matches the pattern. `claims_effect_applier.py`
now carries a `VERDICTS` table for exactly this reason — but it was written AFTER the fix, so it
could not have caught this one. **Check the refuted lists before fixing what a scanner hands you**,
and remember that line numbers move: the row said `:619` and today's hit says `:618`.

### ✅ Slice D — CLOSED BY DECISION 2026-09-09, not by adjudication

The row said *"the 30-item unverified tail · no list exists · a re-sweep, not an adjudication"*.
Both halves of that are true, and together they settle it: **the work Slice D names is a round 4,
and the sweep closed itself against exactly that** — `todo.md:1478`, *"THE SWEEP IS FINISHED. Do
not run a round 4."*

⭐ **THE TWO MEMBERS THE TAIL WAS EVER NAMED BY ARE BOTH RESOLVED**, and they were promoted out of
it on purpose — round 1's plan says *"the 30-item unverified tail LAST — but promote the two family
duplicates named above now"*:

| the named member | state at HEAD |
|---|---|
| `LiveWalkerViewModel.cs:6209` — the third clipboard instance | ✅ **CLOSED.** It is one of round 3's 14 **(a) DELIVERY** sites, all fixed, and `check_clipboard_delivery` now enforces the class: *"46 clipboard call site(s), every DELIVERY copy checks its result."* |
| `Dunste.cpp:612` — `PendingRestoreLoop` logging *"pawn collision restored"* | ⛔ **REFUTED** by round 2, on a route re-read by hand: that loop starts only from `StartPendingLocked()` inside the `else` of `if (Stark::IsGameThreadResponsive())`, i.e. only when `Stalled`, and `Stalled` ⟹ hook active, while `-8` is reached only when `IsHookActive()` is false |

⛔ **AND THE REMAINING ~28 CANNOT BE WALKED, for the reason the slice header already gives**: the
finder agents' output was never stored and `docs/evidence/` holds none of it, so "the tail" is a
COUNT, not a list. Re-deriving it means re-running the finders — a fourth sweep.

⭐ **THE EXPECTED YIELD OF THAT IS MEASURED, NOT GUESSED, AND IT IS THE STRONGEST ARGUMENT HERE.**
All three rounds recorded the same result independently: **0 of the confirmed findings came from a
candidate-list row** — round 1 (*"none is a list row"*, list yield 0/154), round 2 (*"Again 0 of the
confirmed came from a candidate row"*), round 3. What produced findings every time was **reading a
specific function**, which is what slices A, B and C did — and between them they turned up 6
defects from 30 sites read by hand.

⚠ **WHAT WOULD RE-OPEN IT**, stated so this is a decision and not an abandonment: a new claim whose
site is NAMED, or a new module landing in `dll/src` that no round covered. `[SLICEB-FLY-2026-09-09]`
is itself an example — `Dunste` is a gameplay module that arrived after round 1's module list was
drawn, and reading it directly found two defects that no tail row would have named.

## ✅ `[CLAIMS-SLICE-2026-09-09]` — the four slices are closed

| slice | outcome |
|---|---|
| **A** CE Lua emission layer | 8 read → **1 defect**, fixed and live-verified (`[TG1-CLEARALL-RESULT-2026-09-09]`) |
| **B** discarded effect-appliers | 7 read → **2 defects** + 1 hygiene, fixed; the proposed gate re-scored and still refused |
| **C** discarded `Read*Safe` | 15 read → **3 defects**, fixed, two manufactured fixtures (`[IFACEREAD]`, `[UNREADVAL]`) |
| **D** the 30-item tail | **closed by decision** — a round 4 the sweep forbade, with a measured yield of 0 |

**30 sites read by hand → 6 defects and 1 hygiene fix**, against three agent sweeps that filed 209
claims and confirmed 23. ⚠ That comparison is not a claim that reading beats sweeping — the sweeps
are what produced the POPULATIONS these slices walked. It is the narrower claim the rounds
themselves kept making: **the candidate ROWS were worthless and the populations were not.**

## 🔎 Audit #4 ASSESSMENT — measured 2026-09-09 `[A4-ASSESS-2026-09-09]`

**Why this exists.** The maintainer re-read the audit #3 re-check and asked the same question of the
**next** audit: *does audit #4 have an unaudited blank like #3's, and is any of its verification
soft?* — with a named suspicion: **much of it should have been driven by hand, and instead it was
closed by reading logs**, so nobody looked at whether the UI↔DLL wire actually works, whether either
side fails to notify when it should, or whether a field is **sent and never received**.

⛔ **SCOPE — this section is an ASSESSMENT, not findings.** Nothing below is a defect claim. Every
number is derived at HEAD by a script kept in this session's scratchpad and reproduced by the
commands quoted inline. **Derive, never quote.**

The target is unambiguous: audit #5 (`audit-2026-08-13-early-code-findings.md`) is the large
old-code audit, so the one before it is **audit #4** (`audit-2026-08-04-findings.md`, build 2554).
⚠ Its predecessor is dated **2026-07-14**, not June.

### ⛔ 1. The blank is REAL and it is BIGGER than audit #3's

Audit #4's window is `af2ce50..7cc3d5e2` (build 2168 → 2554, 2026-07-15 .. 08-03, **225 commits**).
Same `git blame` recipe as the audit #3 re-check — a line still blamed into a range is a line no
later commit touched, so any audit whose baseline is at or after that range covers **zero** of them
by construction.

| | lines alive at HEAD | files |
|---|---|---|
| whole window | **30,579** | 167 |
| production (`dll/src` + `ui/UE5DumpUI`) | **14,418** | 92 |
| tests / tools / scripts / axaml | 16,161 | 75 |

(audit #3's comparable number was 22,853.)

⛔ **31 PRODUCTION FILES / 3,409 LINES ARE NAMED BY NO LATER AUDIT DOCUMENT AT ALL** — not audit #5,
not #6, not the dxgi audit, not the MED re-derivation, not this file's sweeps. Largest first:

```
489  ui/UE5DumpUI/Services/CoordCsvCodec.cs      204  ui/UE5DumpUI/Services/DiagnosticsProbe.cs
352  ui/UE5DumpUI/Services/CoordLuaParser.cs     196  dll/src/Sense.cpp        <- a whole DLL module
331  ui/UE5DumpUI/Views/TeleportPanel.axaml      180  ui/UE5DumpUI/Services/CeAutorunScriptGenerator.cs
208  ui/UE5DumpUI/Models/CoordinateLibraryFile.cs 177 ui/UE5DumpUI/Views/OrphanCleanupConfirmDialog.cs
154  Services/CeInjectScriptGenerator.cs         151  Models/OrphanScanTypes.cs
123  Models/OrphanProxy.cs                        99  Models/DiagnosticsResult.cs
 94  dll/src/Sense.h                              84  Helpers/LiveWalkerNavShortcuts.cs
 81  Services/PointerQueryScriptGenerator.cs      74  Core/IProxyDeployService.cs
 74  ViewModels/DumpExplorerViewModel.cs          70  Services/JsonNum.cs
 68  Services/SnapshotStore.cs                    57  Services/PipeTransportStats.cs
 (+ 11 more under 40 lines)
```

⚠ **"named at all" is a GENEROUS proxy in both directions** — being mentioned in an audit doc is not
being audited, so 3,409 is a **lower bound** on the blank.

**Why nothing covered it**: audit #5's predicate is *authored before 2026-06-01* and this window is
07-15 onward — **disjoint by construction**, exactly the relation the audit #3 re-check found between
#3 and #4. #6 is the vendor UE 5.8.2 pass. The 2026-09-08 blind-spot sweep was a **pattern** sweep on
one shape (a dropped `bool`), not a line-level re-read.

#### ⭐⭐ And the same measurement, run over the WHOLE tree, found something larger than the question asked

Every surviving production line at HEAD, bucketed by which audit's scope could have contained it:

| band | lines | share | who scoped it |
|---|---|---|---|
| before 2026-06-01 | 48,785 | 30.4% | audit #5 |
| **2026-06-01 .. 07-03** | **50,346** | **31.3%** | ⛔ **NOBODY** |
| 2026-07-03 .. 07-15 | 16,500 | 10.3% | audit #3 (never re-swept) |
| 2026-07-15 .. 08-03 | 13,633 | 8.5% | audit #4 (never re-swept) |
| after 2026-08-03 | 31,407 | 19.5% | ⛔ no area audit — one pattern sweep only |
| **total** | **160,671** | | |

⛔ **50.8% of surviving production code has never been inside any area audit's scope**, and the
single biggest block is a band **nobody has ever named**: 2026-06-01 .. 07-03. Audit #5 stops at
06-01; audit #3's baseline is post-b1872 (07-03). The only pass that could have touched any of it is
the 4-agent build-974 pass of 2026-06-10 — which audit #5's own header dismisses as *"one tenth of
audit #4's 48-agent adversarial effort"*, and which by construction could not see code written after
that date.

### ⛔ 2. The verification softness is real, and the register already recorded it happening

- The audit doc says it in its own words: *"**What is NOT done: the verification.** … **None of it
  has been run on a real game.**"*
- **49 numbered bug findings** in the summary tables. **22 of them are not mentioned anywhere in
  `verification-register.md`** — 45%:
  `B27 B3 B9 B30 B11 B12 B15 B17 B20 B21 B22 B23 B24 B32 B33 B37 B39 B40 B43 B44 B46 B48`.
  This is the same violation of the register's single-owner rule the audit #3 re-check found (10
  UI-side fixes with a unit test only).
- The register's audit #4 section holds **22 bullets: 16 ✅ · 5 ⬜ · 1 🟡**. Of the 16 closed:
  **5 by a scripted rig · 3 by manual operation · 6 by READING LOGS ONLY · 2 state no evidence kind.**

⭐ **And the register itself already documents three log-reading false greens** — the maintainer's
suspicion is not a hypothesis, it is on file:

1. **B47's earlier ✅ was credited to a hand-injected session where the guard was not even compiled
   in**, and had to be re-earned on a real proxy run.
2. **B28 "was NOT tested"** — the rows inspected were `StrProperty`, not FText.
3. One ✅ **was credited to the WRONG SESSION** (register `:8955`).

…plus **B14+R5 needing three attempts**, whose lesson the audit wrote down itself: *a fix verified
against the LIST it was written from is not verified.*

### 🟡 3. The UI↔DLL wire — the loud axis is CLEAN, the silent axes are UNMEASURED

⭐ **Say the good news first, because it is real.** The coarse axis measures perfect in both
directions:

```
99 CMD_* constants declared · Fern dispatches on 99 · the C# UI sends 99
  sent by the UI, handled by no DLL branch : 0
  handled by the DLL, never sent by the UI : 0
  request-parameter keys sent by the UI that no DLL request.value() reads : 0
```

That is expected: a wrong **command name** fails LOUDLY (`unknown command`), so it cannot survive.

⛔ **What has no mechanism at all is the silent half.** The CE Lua ↔ DLL mailbox is version-gated
**and** hash-gated (`tools/check_mailbox_contract.py`). The **named pipe — the primary channel, 99
commands, 358 reply keys — is gated by nothing**, and there is no shared constant table across the
language boundary; the C# side hand-types the strings:

```csharp
var req = new JsonObject { ["cmd"] = "walk_instance", ["addr"] = addr };
```

A wrong key there does not throw. It yields a default, or a null, and the reply says `ok: true`.

⛔⛔ **This shape has landed THREE TIMES IN THE LAST TWO DAYS**, which is what turns it from a
tidiness concern into the highest-value target here:

1. **`[SW7]`** — a scalar `DelegateProperty`'s `delegate_pad` was computed correctly and **never
   serialised**, so every CE path built chains without it.
2. **the `l12` rig** — sent `addr=` where `Fern` reads `instance_addr`: **2,000 requests measured
   nothing and reported cleanly.**
3. **`FlyStatus.Noclip`** — the DLL publishes it, `ApplyFlyReadout` **never reads it**, and the
   `✈ Fly ON (noclip)` badge is built from the local checkbox.

⭐ **And this assessment found two more of exactly that shape, previously unrecorded:**

```
Fern.cpp:6013   data["mode_resolved"] = st.modeResolved;
Fern.cpp:6016   data["cmc_addr"]      = Renge::AddrToStr(st.cmcAddr);
```

`grep -rn 'mode_resolved\|cmc_addr' ui/ tools/ scripts/ dll/tests/` returns **zero** — nothing in the
tree reads either, and `FlyStatus` has no corresponding member. ⚠ They may be deliberate diagnostics;
that is an adjudication, not a verdict. **21 DLL-published reply keys** currently have no in-tree
reader (the `ffield_*` / `ustruct_*` / `fproperty_offset` family among them) and each needs a
hand-read, exactly like the ~166 claims did.

⛔ **And the third class the maintainer named — "should have notified and did not" — is reachable by
NO static probe and by NO log read.** A DLL state change with no push, or a reply the UI parses and
never re-renders, is only visible with the UI running against a live game. That is precisely the half
audit #4 never did.

### ⚠ 4. What this assessment did NOT measure — read before planning off it

- **Whether audit #4's 52 fixes are still live at HEAD.** The audit #3 re-check's headline was
  23/23; the equivalent number for #4 does not exist yet. That is phase 1 below.
- The 21 dead reply keys are **candidates**, not findings — none is adjudicated.
- ⚠ **The key-parity numbers above are GLOBAL, not per-command, so they UNDERSTATE the risk.** A key
  that is valid for command X and a silent no-op for command Y matches in a global comparison. The
  `l12` failure lived in exactly that layer.

### ⬜ The plan — four phases, run ONE AT A TIME, fixes deferred to the end

Ordered by evidence-value per unit cost. ⛔ **Fixes are NOT applied inside a phase** — each phase
records what it found and the next one starts; the repairs are a separate pass with their own
adversarial check, because this repo's history says a verdict is not authority on the repair (AB4).

| # | phase | shape |
|---|---|---|
| **1** | audit #4's 52 fixes re-checked at HEAD + the 22 findings with no register row | static, fan-out |
| **2** | the pipe wire, three axes: per-command key pairing · dead reply keys · reads-that-never-arrive | static, produces populations |
| **3** | ⭐ the **two-sided LIVE arm** — UI *and* DLL on DumperTest **Shipping**, comparing what the DLL sent against what the UI displayed. The only way to reach "should have notified and did not" | live + rig |
| **4** | a `check_pipe_contract.py` gate — **only** on a predicate whose legitimate population is measurably EMPTY (the 17b lesson) | gate design |

⚠ **The rule audit #4 earned applies to this plan too**: *a fix verified against the list it was
written from is not verified.* A wire sweep checked only against the keys a grep produced is not
checked — every phase needs a negative control.

⛔ **Not recommended: re-running audit #4 as an area audit.** The #3 re-check measured 23/23 fixes
still live and 0 reopens, and concluded the value was in *coverage*, not in re-reasoning. Nothing
above contradicts that for #4; the blank and the wire are where the evidence is.

### ✅ PHASE 1 DONE 2026-09-09 — 52/52 re-checked at HEAD; nothing reverted, and two live defects found anyway

25 agents (8 re-check · 13 refute-mandated skeptics · 4 coverage), 0 errors. ⛔ **Fixes deliberately
NOT applied** — this phase records only, per the plan above.

#### The headline is the same as audit #3's, and it is the reassuring one

| verdict | n |
|---|---|
| `present` | 47 |
| `changed-but-correct` | 3 (B15, B30, B37) |
| `WEAKENED` | 2 (B21, R1) |
| **`REVERTED`** | **0** |
| **`not-found`** | **0** |

**Nothing audit #4 shipped has been silently undone in ~950 builds.** So — as with #3 — the answer
is *not* "re-run audit #4"; the value is in coverage and in the wire, which is where phases 2–4 go.

⭐ **THREE LEVELS, AND EACH ONE CORRECTED THE LEVEL ABOVE IT.** The first pass called B30 and R3
fixed; the skeptics refuted both; and my own hand-check then **refuted one of the skeptics** (R1).
Recording that chain rather than only its output, because the audit-agent calibration in
working-lessons §2 is built from exactly this.

#### ⛔⛔ FIX 1 — `[B30-REOPEN]` 🔴 the "already serving" bail-out tears down the pipe it just told the user to connect to

**Verified by hand, all six links, not taken from the agent.**

```
.CT:645-653   SERVING branch  ->  showMessage("No injection needed — just launch
                                  UE5DumpUI.exe and click Connect.")  then  return false
.CT:883-885   [ENABLE]        ->  on false, the DEFERRED untick timer fires memrec.Active = false
              CE semantics    ->  that RUNS [DISABLE]. The repo says so itself, twice:
                                  ue5_freeze_helper.lua:977 "Setting `memrec.Active = false` runs
                                  the record's [DISABLE] block synchronously", and
                                  CeInjectScriptGenerator.cs:232 "[ENABLE]'s early bail-outs untick
                                  the record, which makes CE run this block"
.CT:887-891   [DISABLE]       ->  ue5_shutdown()
.CT:811-812   its ONLY guard  ->  pcall(getAddress, "UE5_StopPipeServer")
ProxyVersion/Dinput8/Dxgi/Winmm.def  ->  ALL FOUR export UE5_StopPipeServer
```

⛔ **So in the one case B30 was filed about, the guard PASSES and the shutdown runs.** The guard's
own comment says it is there for *"nothing was ever injected"*; it does not cover **"something is
loaded that we did not inject"**, which is the whole finding. Net effect: the user ticks the CE
inject row while a proxy is serving, is told to go and connect — and ~50 ms later the pipe is torn
down under them (`Frieren.cpp` `UE5_Shutdown`: `Tot::RequestShutdown`, `Mimic::StopThread`, four
worker joins, `Schlacht`/`Grausam` off, `Stark::Shutdown`, `s_pipeServer.Stop()`).

⛔ **It ships in BOTH artifacts** — the checked-in `scripts/UE5CEDumper.CT` and the script the UI
generates (`CeInjectScriptGenerator.cs:159` the same `pre == INIT_READY or pre == INIT_SKIPPED`
branch, `:162` the same message, `:165` the same deferred untick).

⚠⚠ **AND THE DEFERRAL IS OURS.** The `.CT` comment at :874-881 argues the untick must be deferred
because an immediate `memrec.Active = false` is a CE no-op — `[FREEZEUNTICK-2026-08-20]`, now pinned
by `tools/check_ce_untick_placement.py` (gate 17a, shipped **2026-09-08**). That reasoning is right
about CE and **inverted about B30**: the fix unticks *to prevent* a later user-untick from running
`UE5_Shutdown`, but the untick **is** the thing that runs it. Before the deferral the destructive
path needed a user untick; after it, it fires by itself. ⛔ This is not an argument for removing the
deferral — the freeze row needs it — it is that the SERVING branch must not reach an untick at all,
or `ue5_shutdown` must learn the difference between *our* DLL and *a* DLL.

⚠ **What is NOT claimed**: this has not been reproduced on a running game. It is a code-path
argument with every link read at HEAD, and it belongs in phase 3's live arm.

#### ⛔ FIX 2 — `[R3-SEETHRU]` 🟠 See-through's `[DISABLE]` writes the mailbox with NO idle wait

`SeeThroughScriptGenerator.cs` shares one `EmitBlock` between `[ENABLE]` and `[DISABLE]` (:28-29).
`AppendIdleWaitOrBail` sits at **:78, textually inside the `if (enable)` braces** (`{` :70, `else`
:81) — the comment above it is **outdented to the outer level**, which is what makes it read as
unconditional. The `cmd` store at :88 is emitted for **both** blocks. So unticking See-through
writes operands, clears status and stores `cmd` while another mailbox command may still be in
flight — the exact AA10 hazard, and the comment two lines above says so: *"operands land in the same
mailbox, so writing them corrupts the command in flight just as surely"*.

⭐ **Measured structurally, not by eye** — every `AppendIdleWait*` / `AppendContractCheck` call in
all 14 generators, classified by brace depth:

```
SeeThroughScriptGenerator.cs:78   AppendIdleWaitOrBail   GUARDED by: if (enable)   <== the only one
BakedScriptGenerator.cs:258       AppendContractCheck    GUARDED by: if (verifyReturn)   (legitimate)
CoordLibraryScriptGenerator.cs:227/261                   GUARDED by: if (dll)            (legitimate)
   ...the other 22 call sites: unconditional
```

⭐ **The file refutes itself**: its own `AppendContractCheck` at :63 is unconditional, and
`Movement`/`TimeDilation` carry the **byte-identical comment** while passing
`enable ? UntickAndReturn : SilentReturn` to an **unconditional** call — proving the intended shape.
SeeThrough is a copy that lost one level of indentation.

#### 🟡 FIX 3 — `[B33-SPELLING]` two emit sites resolve only the bare `g_invokeMailbox`

B33's rule is *"resolve both spellings everywhere you look the mailbox up"*. At HEAD, 17 of 19 sites
obey. The two that do not are both the "already loaded" pre-check that **B30 added**:

```
ui/UE5DumpUI/Services/CeAutorunScriptGenerator.cs:116   pcall(getAddress, 'g_invokeMailbox')
ui/UE5DumpUI/Services/CeInjectScriptGenerator.cs:153    pcall(getAddress, 'g_invokeMailbox')
```

The `.CT`'s counterpart of the same pre-check uses `ue5_findMailbox()`, which tries both — so the
three routes have drifted apart. ⭐ **And it COUPLES WITH FIX 1**: on a miss, `pre` stays nil, the
SERVING branch is skipped, and the script calls `UE5_AutoStart` on an already-serving DLL instead.
Whichever way the spelling lands, one of the two defects fires. Fix them together.

#### 🟡 FIX 4 — `[B21-DOCROW]` the summary row strikes three holes; one shipped

B21 was three independent import-parser holes. Only **AllowThousands** shipped (and it is properly
pinned — `CoordCsvCodecTests.CoordPrecision_RejectsAnyCommaInANumber`). The quote-state hole is
untouched at HEAD: `CoordCsvCodec.SplitLines` (:352-358) flips `inQuotes` on **any** `"`, while
`SplitCsvLine` (:399) enters quote mode **only at field start**, so one unpaired mid-field quote
still swallows every following record to EOF, with no unterminated-quote diagnostic.

⛔ **The defect to fix here is the DOCUMENT**: `audit-2026-08-04-findings.md:180` strikes all three
as *"FIXED build 2621"*. ⚠ The **dev-log entry is honest** — it describes only the AllowThousands
decision — so the over-claim is in the tracker, and a future reader greps the tracker. Either
re-scope the row to hole 1 or reopen holes 2/3 as work; **do not let a hole-1 green close it.**

#### ⚠ THREE LATENT ENUMERATION HAZARDS — not defects today, recorded so they are not re-derived

- **B10** — the audit's safety precondition (*"node-based map, no erase/clear anywhere in dll/src"*)
  is **no longer true**: audit #5 U5 added `s_walkClassCache.erase(victim)` at `Ubel.cpp:914`. The
  property still holds because eviction went on the **by-value** cache only, and `Ubel.cpp:881-887`
  says exactly that. ⛔ But the argument now rests on a per-cache distinction, and **any future
  eviction on `s_walkClassExCache` turns every `const ClassInfo&` call site into a use-after-free.**
- **B15** — `CeLuaHygieneTests.EveryGeneratedScript()` is a **hand-maintained list of 8** generator
  families; `ls *ScriptGenerator*.cs` has **14**. Six (Freeze, PointerQuery, CoordLibrary, Baked,
  StandaloneTrainer, Invoke) are never fed to it, and `CeMailboxBailoutTests.MailboxScripts()` omits
  Teleport. ⭐ The world is currently clean (`grep -rn "then break end"` → one comment), so this is
  exposure, not a defect. ⚠ **It is also why R3 above survived**: no test feeds a toggle generator's
  `[DISABLE]` block through an idle-wait assertion.
- **B17** — the pose fields are behind one `ClearPoseDisplay()` helper, but the enclosing
  `SetConnected(false)` is still a hand-maintained list of ~14 per-card resets, and it is **still
  growing by hand** (entries tagged `(B26)` and `(L13)` were added after B17).

#### ⚠ AND ONE AGENT CLAIM I REFUTED — R1's `WEAKENED` is WRONG

The skeptic reported the Frieren roster *"no longer covers the world"*, naming `Voll` and
`VersionNeedleScan` as modules that slipped through. Checked by hand, both are wrong:

- `Voll` **is** in the roster — `naming-convention.md:366`, marked 🟢.
- `VersionNeedleScan.h:11-14` says in its own header *"Kept in English and NOT given a Frieren roster
  name: CLAUDE.md's module-naming rule exempts algorithm helpers that live inside an existing
  namespace, the precedent being `GraphPath.h` under `Aura::`. This lives under `Genau::`."* — and
  `:47` confirms `namespace Genau {`.

A sweep of every `dll/src/*.cpp` against the roster returns exactly one absentee, `Lugner_Dinput8`,
which is a proxy flavour of the rostered `Lugner`. **R1 is `present`.** ⚠ The agent's residual point
is fair and is NOT a defect: nothing *gates* roster membership (`check_derived_counts` pins two
numbers in that file, not list membership) — a candidate for phase 4, not a fix.

#### 📐 The verification half — the 25 findings with no register row

| | n | ids |
|---|---|---|
| **no unit test at all** | **12** | B9 B11 B17 B20 B22 B24 B40 B43 B44 B48 R1 R7 |
| test exists but does **not reach the fixed path** | **7** | B15 B23 B27 B32 B33 B37 B46 |
| an acceptance side that **does not exist** | **9** | B20 B21 B24 B40 B44 B46 B48 R1 R7 |
| half: manual-only / log-derivable / neither | 12 / 8 / 5 | |

⛔ **B22 is the sharpest**: no test *could* cover it — neither `Laufen.cpp` nor `Hemmung.cpp` reaches
any test target, the trap CLAUDE.md's `-Target Test` warning documents. Its acceptance is a WALK-log
line plus a UI status string, and it is genuinely log-derivable — it just never had a row.

⭐ **Every one of the 25 now has a concrete two-sided acceptance written by the coverage pass** (the
register's charter requires both sides). ⛔ **Nine of them could not be given a producer-side
observable at all**, and the coverage agents were told to say *"none — and that is itself the
finding"* rather than invent one. Those nine are UI-local fixes whose effect never reaches a log or
the pipe: **that absence is the same family as phase 2's dead reply keys**, and the two should be
adjudicated together rather than separately.

#### ⬜ Carried into the fix pass (NOT done here)

1. `[B30-REOPEN]` 🔴 · 2. `[R3-SEETHRU]` 🟠 · 3. `[B33-SPELLING]` 🟡 (with 1) · 4. `[B21-DOCROW]` 🟡
5. 25 register rows to write · 6. the three latent enumerations, if a gate is cheap (phase 4)

### ✅ PHASE 2 DONE 2026-09-10 — the wire's two comparable axes are CLEAN; the defects are in fields that were never created

29 agents over two workflows (8 adjudicate · 4 no-channel hunt · 17 refute-mandated skeptics), 0
errors. ⛔ **Fixes deliberately NOT applied.**

#### ⭐ The good news first, because it is a real measurement and it answers half the question

| axis | population | result |
|---|---|---|
| **command names** | 99 constants / 99 dispatched / 99 sent | **0 either direction** |
| **A — request params, PER COMMAND** | 101 C# send sites | **0** keys the UI sends that its handler never reads |
| **B — reply keys, PER COMMAND** | **78** (command, key) candidates | **46** read elsewhere · **32** dead-benign · **0 costly** |

⛔ **So "資料送出，另一邊根本不收" does NOT hold for the reply-key axis.** Every key the DLL
publishes is either consumed or harmless. ⭐ And the reason the coarse axes are clean is worth
stating: **a wrong command name fails LOUDLY** (`unknown command`), so it cannot survive — the
silent failures had to be somewhere else, and they were.

⚠ **My extraction's false-positive rate was 59% (46 of 78)** and the misses are instructive: 21 of
the 46 are `teleport_get_pose` keys read in a `ParsePose` helper far from the send site, and 11 are
`get_offsets` keys whose only consumers are the **Python rigs in `tools/verify/`** — which are
first-class pipe clients, not tests. Any future gate on this axis must treat `tools/**` and
`scripts/*.lua` as consumers or it will report the rigs' contract as dead.

#### ⛔ The reverse axis was NOT measured, and the instrument is why — said rather than reported

"Keys the UI reads that the command never publishes" produced **93 sites** with huge key lists. That
is an artifact: the extraction uses a ±line window, and `DumpService.cs` is one ~3,500-line file, so
the window swallows neighbouring methods. **Those 93 are not a population and were not adjudicated.**
The right instrument is a live capture — send each command, keep the actual reply, diff against what
the UI's model consumes — which is **phase 3**, not a regex.

#### 🔎 Axis C — "the DLL knows it and tells no channel". 17 raised, 14 REFUTED, 3 survive

⭐⭐ **The 14 refutations are the more valuable half of this phase**, and two of them cost me
personally: I hand-verified the *code structure* of four findings and confirmed every structural
claim — and was still wrong about all four, because **a discarded return is not a defect until the
consequence survives too.** Written up as working-lessons §1.w4.

**Refuted, with the route that killed each:**

| # | subject | route |
|---|---|---|
| 0 | teleport post-move observation "discarded" | published-elsewhere — it is the same read `teleport_get_pose` returns |
| **3** | **Fly disable `return 0` on a failed collision restore** | **deliberate-and-documented** |
| 2, 14 | live CMC velocity / MovementMode not published | published-elsewhere — both are on the wire already |
| 4, 11 | god-mode observed bit | published-elsewhere / user-can-see-it-anyway |
| 5 | object-null hold "never restored" | published-elsewhere — irreversibility is static and confirmed *before* the act |
| 6 | see-through trace refusals collapsed | published-elsewhere — the game-thread state is its own field |
| **7** | **`Schlacht.cpp:366` `Invoke(...); return true;`** | **deliberate-and-documented — and see below** |
| 8, 15 | cursor input mode / foreground lock | deliberate-and-documented |
| 10, 16 | fly `driftCount` / time-dilation siblings | not-actually-known — the counter is not the fact claimed |
| 13 | god-bits truncation | unreachable |

⛔⛔ **AND #7 IS ON THIS FILE'S OWN "Refuted (8) — do not re-raise" LIST. TWICE.**
`docs/todo.md:1183-1186` and `:1302-1310` both name `Schlacht.cpp:366`, each after ≥4 collapse
routes. **This is §1.w2 happening again — and it extends the lesson: it is not only *mechanical*
scanners that resurface refuted rows. An LLM agent reading the same code reaches the same
plausible-looking conclusion, and it will keep doing so, because the refutation lives in prose and
the code is unchanged by design.** The brief for the no-channel hunt did not tell agents to grep the
refuted list first; the phase-1 brief did, and phase 1 raised no refuted row. **That difference is
the whole lesson.**

⭐ The strongest single refutation, worth keeping: the repo answers *"was the hide applied?"* with
the published **actor addresses** (`Fern.cpp:6062-6069`, consumed by four rigs), not with a dispatch
code — because `Invoke() == 0` means "ProcessEvent dispatched without faulting", **not** that
`bHidden` moved. Gating on the return code would reproduce audit #4's own root cause: the report and
the reality computed by the same code path.

#### ⬜ SURVIVOR 1 — `[POSEATTACH]` 🟠 MED: the spec REQUIRES a flag that was never created

`docs/teleport-spec.md:218-220`, verbatim: *"If the invoke fails (game-thread idle), return the raw
values anyway with `source = raw` **and a warning flag** — better an approximate display than an
error."*

The fallback shipped; **the flag never did.** `Wirbel.cpp:421` sets `*outSource = 0` once and never
revises it; `:425` computes `bool attached` right there; `:443-444` the sole outlet is a `LOG_WARN`
naming the degradation. So two very different reads arrive as the identical `source = "raw"`:

- a healthy unattached pawn — world-space, correct;
- an **attached** pawn (vehicle / mount / moving platform) whose `K2_GetActorLocation` failed —
  **parent-relative numbers presented as world coordinates**.

⛔ **And the UI's own model documents the wrong inverse**: `TeleportModels.cs:24-26` says `"raw"` =
*"direct property read"* and `"invoke"` = *"used for attached/vehicle pawns"* — i.e. the UI reads
`raw` as **meaning** not-attached. The damage is not only on screen: `SaveMarker` stores the current
map name, so `RecallMarker`'s map guard passes, and those parent-relative numbers are later driven
into the pawn as a **world-space destination**, and exported into BugItGo strings and CE trainer
coords.

⚠ MED not HIGH: it needs an attached *possessed* pawn (a minority configuration) **and** an invoke
failure, and `UE5_CallProcessEventEx` has a direct-call fallback that often succeeds anyway.

#### ⬜ SURVIVOR 2 — `[TPREL-ZEROPOSE]` 🟡 LOW: a failed re-read publishes a landing at the origin

`Wirbel.cpp:1788` calls `GetPoseImpl(outNewPose, nullptr, 0, nullptr)` — *"best-effort re-read of the
landing"* — and discards the return; `:1792` returns `TP_OK` regardless. `GetPoseImpl` leaves `out`
untouched on both failure paths (`:419`, `:446-448`), and all three transports zero-init and gate on
`code == 0`, so a failed re-read is published as a landing at exactly **(0,0,0,0,0,0)**,
indistinguishable from a real arrival at the world origin. The panel then prints *"Teleported N uu
horizontally → (0.0, 0.0, 0.0)"* and overwrites the live X/Y/Z the user can copy or save.

⚠ Contract contradicted rather than merely unimplemented: `Wirbel.h:223-225` and
`teleport-spec.md:1026-1027` both promise the re-read landing. ⚠ LOW because the blast radius is the
display, it self-corrects on the next pose refresh, and the trigger needs the pawn or world to
vanish inside one locked call.

#### ⬜ SURVIVOR 3 — `[SOLIDE-REFUSAL]` 🟡 LOW: a re-arm refused everywhere replies `held:0, code:0`

`Solide.cpp:352` recomputes `job.lastRefusal` every 300 ms tick, and `:453` returns it **only when
`newlyAdded`**. Neither `GetState` (`:493-508`, 8 fields) nor `get_forced_fields`
(`Fern.cpp:5958-5975`) carries it. So re-arming an already-armed `class::field` that resolves
instances and is **refused on every one** replies `{held: 0, code: 0}`, and
`PropertySearchViewModel.cs:494-496` prints the positively false *"no live instance of {Class} or any
subclass exists right now … will apply as soon as one spawns."* ⚠ LOW: most refusal triggers are
pre-empted UI-side.

#### ⚠ One narrow residual recorded, NOT filed — and why it is not the defect it looks like

`Dunste.cpp:830`'s `return 0` **does** skip the `modeRestoreFailed → FR_ERR_WRITE` check at `:833`,
so a disable where **both** restores failed reports success. `git blame` makes the shape vivid: the
early return is `8099a4b7` (2026-08-04, **audit #4 B8's own fix**) and the check below it is
`dbed64e4` (**2026-09-09, our slice B fix**) — yesterday's fix appended after an existing early
return, unreachable on that path.

⛔ **It is still not a reportable defect, for two measured reasons.** (1) The collision branch is
**PENDING, not failed**: `StartPendingLocked()` is called and the record is deliberately kept, so
`return 0` means *"fly is off and the restore is in hand"*, which is true — and `Dunste.cpp:604-612`
records that this is the **COMMON** path, because the click that disables Fly is what backgrounds
the game. Escalating would false-alarm on nearly every Noclip disable. (2) The mode fact is **not
off-channel**: `current_mode` is on the same reply (`Fern.cpp:6014`) and `ApplyFlyReadout` renders it
into **`FlyCurrentText`** — a *different* property from the `StatusText` that says "Fly OFF." — so
both strings are on screen and the user sees `MovementMode = 5`.

⚠ What is left is **wording**, not plumbing: "Ready (MovementMode = 5). Toggle Fly ON to take off."
over a pawn still in `MOVE_Flying` is confusing, and that is a UI copy fix at most.

#### ⬜ Carried into the fix pass (NOT done here)

7. `[POSEATTACH]` 🟠 · 8. `[TPREL-ZEROPOSE]` 🟡 · 9. `[SOLIDE-REFUSAL]` 🟡
10. ⚠ **Before ANY of these is fixed, grep the two "Refuted — do not re-raise" lists** — this phase
    re-raised a twice-refuted row and cost a full skeptic pass to put back down.

### ✅ PHASE 3 DONE 2026-09-10 — the live two-sided arm, on SHIPPING. And the UI half got done after all

DumperTest **Shipping** (pid 46524, UE 5.4, **24,497 objects**), DLL 3508 from `dist/`, injected
headlessly; then `dist\UE5DumpUI.exe` driven on top of the same live DLL. Both killed at the end;
`tasklist` verified clear. ⛔ **Fixes deliberately NOT applied.**

⭐ **The UI half was NOT blocked.** `list_granted_applications` showed `UE5DumpUI` and
`DumperTest Shipping` already granted at tier `full`, so the half audit #4 never did — *is the UI
showing what the DLL sent?* — was actually driven, unattended, rather than deferred again.

#### ⛔⛔ THE FINDING — `[BADGEPRIME]` 🟠 MED: connect primes THREE badges, disconnect resets TWELVE

`TeleportViewModel.SetConnected(true)` (`:857-872`) starts exactly three things —
`RefreshMarkersAsync`, `RefreshHeldTimeStateAsync`, `RefreshHeldProtectStateAsync` — which between
them prime **three** badges: God Mode (via `ApplyProtectState`) and the two time lanes.
`SetConnected(false)` (`:874-905`) walks **twelve** badges back to Unknown — ten distinct
`Apply*State(-1)` calls (Debug Camera, Fly, Foreground Lock, God Mode, Gravity Direction, Gravity,
Mouse Cursor, Move Speed, See-through, Super Jump) plus `ApplyLaneState(…, -1)` twice — and clears
POV and the pose besides.

**So NINE of the twelve are reset on disconnect and primed by nothing on connect.** They sit at
`Unknown` until the user presses a button, while the DLL has a definite answer for every one.

⚠ **DERIVED, and it corrects my own first count.** This section first said *"fourteen reset, eleven
unprimed"* — a hand tally that counted the POV and pose clears as badges. Re-derive rather than
quote either number:
`grep -c 'Apply\w*State(\s*-1' TeleportViewModel.cs` inside `SetConnected`, against the badges the
three prime calls actually reach.

⭐ **OBSERVED LIVE, WITH A BUILT-IN CONTROL** — UI and pipe read at the same moment, same process:

| card | the UI showed | the pipe answered |
|---|---|---|
| Keep Foreground | **State: Unknown** | `get_foreground_lock` → `state: 0` |
| Move Speed | **State: Unavailable** | `get_movement_params` → `code: 0, has_cmc: true, cmc_addr: 0x1D8775730D0` |
| Debug Camera | **State: Unknown** | `get_debug_camera_state` → `state: 0` |
| Gravity | **State: Unavailable** | (same family) |
| Super Jump | **State: Unavailable** | (same family) |
| Fly | **State: Unavailable** | `fly_get_state` → `active: false, has_cmc: true, current_mode: 1, mode_resolved: true` |
| **God Mode** | **State: OFF** ✅ | `get_god_mode` → `state: 0` |
| **Time Dilation** | **State: OFF** ✅ | `get_time_state` → `code: 0` |

⚠ **CORRECTED 2026-09-10 while fixing this row.** The table first quoted *"State: Unknown"*
for all six — read off a 0.55-scale screenshot. The badges do **not** share a word:
`Apply*State(-1)` renders **"Unknown"** for Debug Camera, God Mode, Foreground Lock and
Mouse Cursor, and **"Unavailable"** for Move Speed, Gravity, Super Jump, Fly, See-through
and Gravity Direction. Derive it, never read it off a screenshot:
`awk '/private void Apply<X>State\(int state\)/,/};/' TeleportViewModel.cs`.
⛔ **The finding is unaffected** — both words mean *this card was never asked* — but four of
the six labels were wrong, and a wrong quoted string is how a later reader "fails to
reproduce" a real defect. It was caught by a unit test asserting the reset literal, which
is the argument for pinning the literal per card rather than accepting either word.

⭐⭐ **The two that render correctly are EXACTLY the two that are primed.** That is not a coincidence
to be argued about — it is the control that says the instrument (me reading badges off a screenshot)
works, and that the other six are a real gap rather than a misreading.

⛔ **WHY IT IS A DEFECT AND NOT MERELY COSMETIC, IN THE REPO'S OWN WORDS.** `SetConnected`'s comment
at `:860-870` states the model: *"Reflect any dilation the DLL is already holding (prior session / CE
record) — **it survives a UI reconnect as long as the game lives**"*, and for God Mode: *"`want`
survives a UI reconnect, so the badge must reflect it without the user pressing ↻ (**audit #5 AD4 —
nothing queried it on connect**, and AutoTick polls only pose + markers)"*.

**AD4 fixed this shape once, for one card.** The other nine were never done — so after a UI restart
against a live game, a still-active **Fly**, **Move Speed**, **Gravity** or **See-through** hold
reads `Unknown`, and the user has no indication the game is still modified. For Fly that means a
pawn that may still be in `MOVE_Flying` behind a badge that declines to say so.

⭐ **AND THIS IS THE AUDIT-UNDER-REVIEW'S OWN SHAPE, TWICE OVER.** Audit #4 **B9** is
*"Wrong-game warning **never runs on connect**, never clears on disconnect"* — same asymmetry, one
card, fixed. Phase 1 flagged **B17**'s `SetConnected(false)` as a hand-maintained list *"still
growing by hand"* with *"nothing structural forcing a new card to add its own row"*. **This is the
mirror image nobody wrote down: the list that must grow on the OTHER side has three entries and
should have twelve.** Neither audit #4 nor any later pass caught it, and it took a running UI next
to a running DLL to see — precisely the check the maintainer said was missing.

#### ✅ What the live capture measured that no static pass could

`tools/verify/pipe_reply_capture.py` (new) sent **56 of 99** commands and captured the real reply
key set. ⛔ **43 commands are on a documented SKIP list and were never sent** — every one that
writes game memory, arms a hold, moves the pawn, starts a worker or persists a setting, each with
its reason inline. That list is part of the result: *"we measured 56 of 99"* must never be read as
*"we measured the wire"*.

- ⭐ **The static pass had a real blind spot, now sized**: **4 commands publish their ENTIRE reply
  through a helper** — `fly_get_state`, `get_pointers`, `seethrough_get_state`, `walk_instance` —
  so `pipe_command_contract.py` saw **zero** keys for them and **phase 2's axis B never adjudicated
  them**. `get_pointers` alone carries **46** keys.
- **5 commands carry a key no consumer reads**, confirmed against live data:
  `fly_get_state` → `cmc_addr`, `mode_resolved` · `get_movement_params` → `cmc_addr` ·
  `get_diagnostics` → `class_cache`, `approx_bytes` · `get_offsets` → the `ffield_*` / `ustruct_*` /
  `fproperty_*` family · `init` → `build_git/hash/info/time`. All are diagnostics or raw offset
  detail — consistent with phase 2's *dead-benign*, and now measured rather than inferred.
- **The reverse axis converged but did not reach zero: 170 → 134 → 97** hits as the capture got
  richer (top-level keys → nested keys → type-rich instances). The residue is bounded by the 43
  unsent commands and by branches this fixture did not provoke, **so it is a shortlist, not a
  finding list**, and it is left as such.

#### ⛔⛔ AND A CONSEQUENCE OF THE SHIPPING-FIRST RULE, MEASURED — `delegate_pad` CANNOT be seen on Shipping

`tools/verify/d4b_delegate_pad.py` re-run on this Shipping fixture: **PASS**, all four delegate
shapes read correctly — and its first line is the point:

```
elem_size : 16  ->  access-detector pad = 0  (Shipping/Test, or UE <= 5.2 build)
```

The access detector only exists in non-Shipping builds, so the pad is **0**, and `Fern.cpp:1509`
emits the key only `if (fv.delegatePad > 0)`. ⛔ **On a Shipping fixture the field is absent by
design, and no Shipping run can ever verify it.** `[SW7]`'s wire fix and the whole `[D4B-...]`
family therefore **require** the `dev` arm — this is a concrete case where the Shipping-first rule's
own *"Development is a check in the other direction"* caveat is **load-bearing, not optional**, and
it should be cited whenever a delegate-pad row is scoped.

⭐ Separately worth keeping: **D4b/D3b now has a Shipping measurement** (it was closed on
Development on 2026-09-09), and the four shapes — sparse, multicast-inline, scalar, array — all read
correctly with the array's bound element reported at `[1]`, not swallowed by `[0]`.

#### ⚠ §1.w3 FIRED A SECOND TIME, IN A SECOND TOOL, WITHIN HOURS

The capture's first orphan check asked *"does this key appear anywhere in the tree?"* and reported
`mode_resolved` and `cmc_addr` as **consumed**. Their only occurrence in the whole repo was a
**docstring line in `tools/verify/pipe_wire_parity.py`, written earlier the same night, naming them
as examples of unread keys.** The report of the orphan erased the orphan — exactly §1.w3, in a tool
written *after* that lesson was recorded.

⭐ **The fix generalises better than a tag list**: the predicate is now **consumption SHAPE**, not
presence — a literal index, a `.get("k")`, or the PascalCase property — so prose, comments and
docstrings no longer count. That is both the self-reference fix and a stricter question. ⚠ §1.w3's
tag-based guard in `audit_coverage.py` stays, because a coverage report legitimately lists filenames
in prose; the two tools need different guards for the same hazard.

#### ⬜ Carried into the fix pass (NOT done here)

11. `[BADGEPRIME]` 🟠 — prime the eleven unprimed badges on connect, or make the two lists one
    structure so they cannot diverge again (B17's point, from the other side).
12. ⚠ **Phase 2's axis B must be re-run for the 4 helper-delegating commands** — they were never
    adjudicated, and `get_pointers` (46 keys) is the biggest single un-checked reply in the tree.

### ✅ PHASE 4 DONE 2026-09-10 — five gate predicates MEASURED; two ship now, two later, one REFUSED

⛔ **Design + measurement only. No gate file was created and `check_all.py` is untouched** — the
fixes are a separate pass, and a gate belongs with the fix it holds.

The rule this repo paid for twice — gate #17 refuted in round 1, gate 17b refuted as first scoped —
is: **pick a predicate whose LEGITIMATE population is EMPTY, never one whose legitimate population
must be enumerated.** So every candidate below was measured before being proposed, not after.

| # | predicate | violations today | verdict |
|---|---|---|---|
| **A** | every `cmd` the UI sends is dispatched by `Fern` | **0** (99 sent / 99 dispatched) | ✅ **BUILD NOW** |
| **B** | every request param the UI sends is read by some handler | **0** (60 sent / 100 read) | ✅ **BUILD NOW** |
| **C** | no `AppendIdleWait*` is guarded by an `enable` condition | **1** — and it is `[R3-SEETHRU]` | ✅ **BUILD WITH THE FIX** |
| **E** | every badge reset on disconnect is primed on connect | **9** — and they are `[BADGEPRIME]` | 🟡 **BUILD AFTER THE FIX** |
| **D** | every reply key the DLL publishes has a consumer | **many, and legitimately** | ⛔ **REFUSED — do not build** |

#### ✅ A + B — the two that are green today, and worth having precisely because they are

They pin the **loud** axis so it stays loud. A wrong command name currently fails with
`unknown command`; a wrong param currently reads a default. Both are clean at HEAD, so a gate here
never has to be argued with — it simply stops the first regression. ⭐ A is the cheaper and the more
valuable: there is **no shared constant table across the language boundary**, the C# side hand-types
`["cmd"] = "walk_instance"`, and nothing but this would catch a rename that lands on one side only.

⚠ **One design note that must survive into the implementation**: `tools/**/*.py` and `scripts/*.lua`
are **first-class pipe clients**, not tests. Phase 2 measured 11 of 46 false positives coming from
`get_offsets` keys whose only consumers are the Python rigs. A gate that treats the rigs' contract as
dead code will fail on day one.

#### ✅ C — the narrow predicate the R3 defect hands us for free

Not *"every idle wait must be unconditional"* — that has a legitimate population
(`CoordLibraryScriptGenerator.cs:261` guards on `if (dll)`, and `BakedScriptGenerator` on
`if (verifyReturn)`; both are correct). The predicate that qualifies is the **narrow** one:

> an `AppendIdleWait*` call must not sit inside a branch conditioned on `enable`

**Legitimate population: EMPTY**, by construction — a `[DISABLE]` block writes the mailbox exactly
as `[ENABLE]` does, so an idle wait that only one of them gets is always wrong. Measured at HEAD:
**one** violation, `SeeThroughScriptGenerator.cs:78`, which is `[R3-SEETHRU]` itself. ⛔ **Ship it in
the same commit as that fix** — a gate that lands red is worse than no gate.
`tools/verify/ce_emitter_guard_scope.py` (phase 1) already does the brace-depth analysis; the gate
is that tool with an exit code and a negative control.

#### 🟡 E — real, but it must follow `[BADGEPRIME]`, not lead it

> every `Apply*State(-1)` in `SetConnected(false)` has a corresponding prime in `SetConnected(true)`

Nine violations today, and they ARE the finding. ⚠ The better fix may make the gate unnecessary:
if the reset and prime lists become **one structure** (a table of cards, each with its reset and its
refresh), they cannot diverge and there is nothing left to check. **Prefer the structure; keep the
gate only if the structure is rejected.** That is B17's point applied to its own mirror.

#### ⛔ D — REFUSED, with the measurement, so it is not proposed again

*"Every reply key the DLL publishes must have a consumer."* It fails the rule outright:

- phase 2 adjudicated **32 pairs as dead-benign** — build stamps, duplicated values, diagnostics;
- phase 3's live capture found **5 commands** carrying a key no consumer reads, all of the same kind
  (`init`'s build stamps, `get_offsets`' raw `ffield_*`/`ustruct_*` detail, `get_diagnostics`'
  `class_cache`);
- and **11 of phase 2's 46 false positives were keys consumed only by the Python rigs**, which a
  naive gate would also call dead.

So its legitimate population is large, heterogeneous, and would have to be enumerated as an
allowlist — **the exact failure mode that refuted gate #17 and re-scoped 17b**. ⛔ Do not build it.
The right instrument for that question is not a gate but `pipe_reply_capture.py` run occasionally
against a live game, whose output is a shortlist for a human.

#### ⬜ Carried into the fix pass (NOT done here)

13. Build gate **A** (command-name parity) and **B** (request-param parity) — green today.
14. Build gate **C** with the `[R3-SEETHRU]` fix, never before it.
15. Decide `[BADGEPRIME]`'s shape first (one table vs two lists); gate **E** only if two lists survive.

### ⬜ THE FIX LIST — all four phases closed 2026-09-10, nothing repaired yet, by instruction

The maintainer asked for one phase at a time, each recording what needs fixing before the next
starts, with the repairs done together afterwards. All four are closed. **This is the single list
the fix pass works from**; each row links to the phase section that measured it.

⛔ **BEFORE FIXING ANY ROW, GREP THE TWO "Refuted — do not re-raise" LISTS.** Phase 2 re-raised
`Schlacht.cpp:366`, which is on that list **twice**, and it cost a 17-agent skeptic pass to put back
down. Phase 1's brief carried that instruction and re-raised nothing. → working-lessons §1.w4.

| # | row | sev | phase | what it is |
|---|---|---|---|---|
| 1 | `[B30-REOPEN]` | 🔴 | 1 | the CE "already serving" bail-out unticks the record, which **runs `[DISABLE]` → `UE5_Shutdown`** against the serving proxy ~50 ms after telling the user to connect to it. Both the `.CT` and the UI-generated script. |
| 2 | `[R3-SEETHRU]` | 🟠 | 1 | `SeeThroughScriptGenerator.cs:78` — the idle wait sits inside `if (enable)`, so `[DISABLE]` writes the mailbox unguarded. The only enable-guarded one in 14 generators. |
| 3 | `[BADGEPRIME]` | 🟠 | 3 | connect primes 3 badges, disconnect resets 12 — **nine** cards read `Unknown` while the DLL holds the answer. Observed live on Shipping, with the two primed cards as the control. |
| 4 | `[POSEATTACH]` | 🟠 | 2 | `teleport-spec.md:218-220` **requires** a warning flag on the attached-pawn fallback; it was never created, so parent-relative coordinates are shown, saved and later replayed as world-space. |
| 5 | `[B33-SPELLING]` | 🟡 | 1 | two emit sites resolve only the bare `g_invokeMailbox`; **fix with row 1** — they are the same pre-check and either way one of the two defects fires. |
| 6 | `[B21-DOCROW]` | 🟡 | 1 | the tracker strikes three parser holes as fixed; one shipped. Re-scope the row or reopen holes 2/3 — **a document fix, not a code fix.** |
| 7 | `[TPREL-ZEROPOSE]` | 🟡 | 2 | a failed post-move re-read publishes a landing at exactly `(0,0,0,0,0,0)`. |
| 8 | `[SOLIDE-REFUSAL]` | 🟡 | 2 | a re-arm refused on every instance replies `held:0, code:0`, and the UI says "no live instance … exists right now". |
| 9 | register rows | — | 1 | **25** audit #4 findings have none. 12 have no unit test at all; 7 have a test that does not reach the fixed path; 9 have an acceptance side that does not exist. Each now has a two-sided acceptance written for it. |
| 10 | gates **A** + **B** | — | 4 | command-name and request-param parity — **0 violations today**, so they can ship on their own. |
| 11 | gate **C** | — | 4 | the narrow enable-guard predicate — **ship in the same commit as row 2**, never before. |
| 12 | gate **E** | — | 4 | badge prime/reset symmetry — only if row 3's fix keeps two lists instead of one table. |
| 13 | phase 2 re-run | — | 3 | axis B never adjudicated the **4 commands whose whole reply comes from a helper**; `get_pointers` (46 keys) is the largest unchecked reply in the tree. |

⚠ **Three latent enumeration hazards** are recorded in phase 1 and are **not** on this list, because
none is a defect today: `B10` (the enriched class cache's safety now rests on a per-cache
distinction), `B15` (`EveryGeneratedScript()` is a hand list of 8 of 14 generators — and is *why*
row 2 survived), `B17` (the `SetConnected(false)` list, whose mirror is row 3).

#### 📐 What the four phases actually answered

- **"Is there an unaudited blank like June's?"** — Yes, and bigger: **30,579 lines** of audit #4's
  window alive at HEAD, **31 production files / 3,409 lines** named by no later audit document. And
  a larger one nobody had named: **50.8% of surviving production code has never been inside any area
  audit's scope**, the biggest block being 2026-06-01 .. 07-03.
- **"Was the verification unsound?"** — Partly, and the register already recorded it: 22 of 49
  findings never got a row, 6 of 16 closures were **log-reading only**, and three log-reading false
  greens are on file.
- **"Did anyone check the UI↔DLL wire?"** — Nobody had. Now measured: the **comparable axes are
  clean** (99/99 commands, 0 unread params, 0 costly unread reply keys), and the defects are where
  no key comparison could reach — **fields never created** (`[POSEATTACH]`) and **a consumer that
  never asks** (`[BADGEPRIME]`).
- **"Were the fixes silently reverted?"** — No. **52/52 still live, 0 reverted**, across ~950
  builds. As with audit #3, the answer is *don't re-run the audit* — the value was in coverage and
  in the wire.

#### ✅ FIX PASS ROUND 1 — 2026-09-10, builds 3513 → 3527. Rows 1, 2, 3, 5 + gates C and E

| # | row | commit | what shipped |
|---|---|---|---|
| **1** | `[B30-REOPEN]` 🔴 | `22a95b91` | ownership flag `UE5_StartedByThisRecord` — the disable tears down only what this record started |
| **5** | `[B33-SPELLING]` 🟡 | `22a95b91` | both mailbox spellings at the last two holdout emitters |
| **2** | `[R3-SEETHRU]` 🟠 | `0fac36e9` | the idle wait moved out of `if (enable)`; **gate 17c** holds it |
| **3** | `[BADGEPRIME]` 🟠 | `b388e925` | `PrimeHeldBadgesAsync` — 12 badges primed on connect; **gate 17d** holds it |

`check_all.py` runs **20**. All C# tests pass. `dist/` republished AOT-trimmed:
`UE5DumpUI.exe` 54.8 MB `D68849EF` · `UE5Dumper.dll` 2.8 MB `BCC68DC8` · `UE5CEDumper.CT`
44.8 KB `C1239550`, build 3527.

⭐ **THE B30 GUARD ASKED THE WRONG QUESTION, AND THAT IS THE WHOLE FIX.** *"Is a DLL
loaded"* is not *"did we load it"* — all four proxy `.def` files export
`UE5_StopPipeServer`, so in the exact case B30 was filed about the probe SUCCEEDED. The
untick is kept (an un-owned record should not stay ticked) and is now harmless, because a
flag decides the teardown rather than a symbol.

⛔ **AND A HAZARD THE FIX ITSELF COULD HAVE CREATED, pinned in the autorun generator**: the
flag is a CE Lua **global**, shared by every chunk. Had the autorun set it, the inject
record would find the DLL serving, untick itself, and its disable would read a flag set by
somebody else — B30 back through the side door. The autorun deliberately does not set it,
and its own `ue5_shutdown()` keeps the plain probe because it is reached from the CE menu,
i.e. with the user's consent rather than from an untick CE fired for them.

⭐ **R3'S TEST CLOSES THE HOLE THAT LET IT SURVIVE.** Phase 1 measured that no test fed ANY
toggle generator's `[DISABLE]` block through an idle-wait assertion. The new check is a
Theory over the whole shared roster, green for all 11 generators — fixing one generator
and leaving the hole open is how the class recurs.

#### ⚠ Two checker bugs and one test bug, caught by their own controls

Recorded because they are the reason to write controls at all:

1. **Gate 17d walked exactly two levels** and reported EVERY badge unprimed. ⛔ **A checker
   wrong in the RED direction is the dangerous kind — it looks like a finding.** It now
   closes transitively to a fixpoint.
2. **Its pattern required `(`** and so missed the primes passed to `PrimeOneAsync` as
   **method groups**, losing God Mode and the time lanes.
3. **The BADGEPRIME test first asserted badge TEXT** and failed on three cards that were
   fake defaults, not defects — badge text conflates *"the VM never asked"* with *"the fake
   had nothing to say"*. It asserts the call counters now, which separates them.

#### ⚠ CORRECTION to phase 3, made before anyone acted on it

That section quoted **"State: Unknown"** for all six cards, read off a 0.55-scale
screenshot. `Apply*State(-1)` renders **"Unknown"** for Debug Camera, God Mode, Foreground
Lock and Mouse Cursor, and **"Unavailable"** for Move Speed, Gravity, Super Jump, Fly,
See-through and Gravity Direction. The finding is unaffected — both mean *never asked* —
but four labels were wrong, and a wrong quoted string is how a later reader "fails to
reproduce" a real defect. Caught by a unit test pinning the reset literal per card.

⚠ **A counting note, so the gate and this file do not look like they disagree**: gate 17d
reports **11**, because it counts `Apply*State` SYMBOLS and `ApplyLaneState` drives both
time lanes. As CARDS it is twelve.

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

#### ✅ LIVE VERIFICATION 2026-09-10 — DumperTest **Shipping**, DLL + UI + real Cheat Engine

Shipping fixture, UE 5.4, **24,497 objects**, builds 3534 → 3544. Game, CE and UI all killed
afterwards; `tasklist` verified clear. ⭐ **Three of the eight rows now have a genuine
red-before-green on a running game** — not a green test, a measured difference.

| row | verdict | how |
|---|---|---|
| **`[B30-REOPEN]`** 🔴 | ✅ **VERIFIED, before/after** | real CE tick on a serving pipe |
| **`[SOLIDE-REFUSAL]`** 🟡 | ✅ **VERIFIED, before/after** | rebuilt DLLs, pre-fix vs post-fix |
| **`[BADGEPRIME]`** 🟠 | ✅ **VERIFIED** | six badges, against phase 3's own before |
| `[R3-SEETHRU]` 🟠 | 🟡 regression only | the hazard needs a command in flight |
| `[POSEATTACH]` 🟠 | 🟡 control only | needs an attached pawn + a failed invoke |
| `[TPREL-ZEROPOSE]` 🟡 | ⬜ not reachable | needs the pawn/world to vanish mid-call |
| `[B33-SPELLING]` 🟡 | ✅ implied | the serving branch resolved and fired (below) |
| `[B21-DOCROW]` 🟡 | — | a document fix; nothing to run |

#### ⭐⭐ `[B30-REOPEN]` — the pipe dies on the old table and survives on the new one

Setup: DumperTest Shipping running, `UE5Dumper.dll` injected and **serving** (`get_object_count`
= 24,497) — the exact precondition. Cheat Engine attached to the game, the table loaded, and
`Inject DLL + Start Pipe Server` ticked by hand. Both runs produced the identical dialog:

> *UE5CEDumper is already loaded and serving in this process as 'UE5Dumper.dll'.*
> *No injection needed — just launch UE5DumpUI.exe and click Connect.*

Then the record unticks itself, which is what runs `[DISABLE]`:

| table | after dismissing the dialog |
|---|---|
| **PRE-FIX** — `git show 22a95b91^:scripts/UE5CEDumper.CT` (guard absent, `grep -c` = 0) | ⛔ **`\\.\pipe\UE5DumpBfx` GONE** — `PipeError: could not open` |
| **POST-FIX** — the shipped `dist/UE5CEDumper.CT` | ✅ **pipe alive, 24,497 objects** |

⭐ **The game process survived BOTH runs** — only the pipe died, which is precisely the defect:
the user is told to go and connect, and the thing they were told to connect to is destroyed
about 50 ms later. ⭐ And `[B33-SPELLING]` rides along: the serving branch could only fire
because the pre-check resolved the mailbox symbol at all.

#### ⭐ `[SOLIDE-REFUSAL]` — and the first reproducer was WRONG, which the before/after caught

Measured on **rebuilt DLLs**, pre-fix and post-fix, each relaunched and re-injected:

```
PRE-FIX   arm #2 (RE-ARM): {'code': 0,   'held': 0}   <- the defect
POST-FIX  arm #2 (RE-ARM): {'code': -12, 'held': 0}   <- FR_ERR_WEAK_PTR reported
```

⛔ **The obvious reproducer does not reach the fix at all.** Arming a permanently-refused field
twice *looks* like a re-arm and is not one: arm #1 is refused and the job is **erased** (it never
persisted), so arm #2 is another `newlyAdded`. Both pre- and post-fix returned −12 — no
difference. **Only a before/after could have shown that**, and without it this row would have
been "verified" on a path the fix never touches.

The real sequence needs a hold that SUCCEEDS first: force `CharacterMovementComponent::MaxWalkSpeed`
as `numeric` → **held 7** on the live components, so the job persists; then re-arm the same field
as `object_null` → refused, `newlyAdded` false. ⚠ That really writes MaxWalkSpeed on 7 components,
so the arm restores it in a `finally` and asserts nothing is left armed.

#### ⭐ `[BADGEPRIME]` — six cards, against phase 3's own measurement

UI connected to the same live DLL. Every card that read its reset label before now carries a real
state **and a live value**:

| card | phase 3 (before) | now |
|---|---|---|
| Debug Camera | Unknown | **OFF** |
| Keep Foreground | Unknown | **OFF** |
| Move Speed | Unavailable | **Off**, `Current: 600 cm/s` |
| Gravity | Unavailable | **OFF**, `Current: 1.75× GravityScale` |
| Super Jump | Unavailable | **OFF**, `Current: 700 cm/s JumpZVelocity` |
| Fly | Unavailable | **OFF**, `Ready (MovementMode = 1)` |
| *God Mode / Time Dilation* | *OFF (already primed)* | *OFF — the unchanged control* |

⚠ Labels read from a **zoomed** capture this time, not a 0.55-scale one — that is what produced
the wrong "Unknown" quotes in phase 3.

#### ⚠ THREE HARNESS BUGS CAUGHT DURING THIS RUN, each by its own guard

1. ⛔ **The game holds `dist/UE5Dumper.dll`**, so `-Target DLL` FAILS while it is running — and the
   first before/after printed "FAILED", carried on, and measured the **same already-fixed binary
   twice**, reporting it as a pre-fix result. The harness now kills the game first and aborts
   unless BOTH the build summary says SUCCESS **and** the DLL's mtime moved. The stale-build trap
   CLAUDE.md names, walked into and then closed.
2. **`search_properties` names the property `prop_name`, not `name`.** Reading the wrong key found
   no host — and the anti-vacuity guard reported the row **UNDECIDED** instead of passing it, which
   is the only reason it was noticed.
3. **The pipe takes `kind` as a STRING** (`"object_null"`), not the C++ enum's int.

⚠ And a fourth, in my own editing rather than the rigs: a heredoc collapsed `\n` inside a Python
string into a real newline for the **third time tonight**. Prose and code with escapes go through
the Write tool, never a heredoc — that is now a habit to keep, not a lesson to re-learn.

#### ⬜ What is still NOT verified live, and why

- **`[POSEATTACH]`** — needs an **attached possessed pawn** whose `K2_GetActorLocation` invoke
  fails; two coincident conditions this fixture cannot stage. Only the anti-over-shout control ran
  (a healthy pose does **not** claim parent-relative). ⚠ Also note the CE mailbox still cannot
  express the flag at all — a recorded contract gap.
- **`[TPREL-ZEROPOSE]`** — needs the pawn or world to vanish **inside one locked call**.
- **`[R3-SEETHRU]`** — the hazard needs another command in flight at the instant of the untick.
  What was checked is that See-through still reads cleanly after its code moved.

⭐ **FILED 2026-09-10 as `verification-register.md`'s FP1 / FP2 / FP3** — a new `### ⬜` batch
(open batch headings 8 → 9, both pinned copies updated from the tree). Each row names the
producer-side observable as well as the screen, per the register's charter, and each says what
would make it VACUOUS: FP1's pairing (`source: "raw"` **with** `parent_relative: true`, the
combination that used to be indistinguishable), FP2's absence assertion (no `x`/`y`/`z` keys at
all, not a zero in them) and FP3's entry proof (the idle wait must be shown to have SPUN — an
idle mailbox at the moment of the untick makes the run decide nothing).

⭐ **FP3 first**, and not for severity: it is the only one whose staging needs no special game —
a long command on this same fixture and a well-timed untick.

#### ⬜ STILL OPEN from the fix list

⚠ *(as of round 1; rows 4, 6, 7 and 8 were closed in round 2 above)* — **9** the 25
register rows · **10** gates A + B (command-name and request-param parity, both measured
green today) · **13** phase 2's axis B re-run for the 4 helper-delegating commands.

⛔ **None of the three fixes above has been re-verified on a running game.** They are held
by unit tests, two new gates and code reading. `[B30-REOPEN]` in particular is a CE
interaction — its acceptance needs a proxy-served game plus a real Cheat Engine tick, which
is a manual row, and it belongs in the register rather than being assumed from green tests.

## ⬜ The bounded class cache sits BEHIND an unbounded one `[CLASSCACHE-FRONTED-2026-09-09]`

Measured while building `sw6_stride_refusal.py`, on a COLD DumperTest 5.4 process:

```
at start                                entries=0     fields=0
after walk_instance (DumperTestActor)   entries=10    fields=194
after walk_class_batch x600             entries=2048  fields=29251
after walk_class_batch ALL (3,946)      entries=2048  fields=29251   <- IDENTICAL
```

Walking 3,346 further classes changed **neither number**. `walk_class_batch` →
`Aura::WalkClassesBatch` → `Ubel::WalkClassEx`, which consults the separate and deliberately
**UNBOUNDED** `s_walkClassExCache` (`Ubel.cpp:1200`) and only falls through to `WalkClass` on a
MISS (`Ubel.cpp:1211-1214`). Six hundred classes are enough to warm it — each walk pulls in super
chains and struct types — after which **no later class walk reaches `WalkClass` at all**, so the
2048-entry LRU (`Ubel.cpp:879`, audit #5 U5) stops taking inserts and therefore stops evicting.

⚠ **Both caches' own comments are individually correct; what is undocumented is the interaction.**
`Ubel.cpp:881-887` explains why the enriched cache is unbounded (it returns `const ClassInfo&`, so
eviction could dangle a reference) and why the LRU is safe to bound (it returns by value). Neither
says that the unbounded one is *in front of* the bounded one for the highest-volume caller.

**Two consequences worth deciding on, neither of which is a bug today:**

1. **The LRU's memory bound is not the system's memory bound.** U5 capped `s_walkClassCache` at
   2048 entries. `s_walkClassExCache` — described in that same comment as "the more widely
   consulted of the two" — has no cap, so the actual ceiling on cached class metadata is the
   number of classes the title loads, not 2048. On DumperTest that is ~4k classes / ~29k fields;
   on a large title it is larger. Nobody has measured it.
2. **There is no way to invalidate a class's cached layout.** ⭐ **CONFIRMED A SECOND TIME,
   2026-09-09**, by an experiment with nothing to do with delegates: patching `FPROPERTY_OFFSET`
   on `CharacterMovementComponent.MovementMode` was invisible to both `Dunste` and the walker,
   because `FindField` → `WalkClass` had already cached the class
   (`tools/verify/sliceb_fly_fail_probe.py`). The count of arms this blocks is now **four**, not
   two — add `[SLICEB-FLY-2026-09-09]`'s `FR_ERR_WRITE` arm and a live `ByteProperty` refusal
   arm for `[UNREADVAL-2026-09-09]`. Not a defect for the shipping
   product — reflected layout genuinely does not change at runtime — but it makes the two SCALAR
   delegate-refusal arms unreachable by manufacture-and-restore, which is why
   `[SW6-STRIDEREFUSAL-2026-09-09]` closed on the array arms only. A `--force` on `walk_class`, or
   a debug-only `invalidate_class_cache` command, would make that whole family of layout
   experiments reachable.

⛔ **Do not "fix" this by bounding `s_walkClassExCache`** without solving the reference-return
first — that is exactly the dangling-reference hazard `Ubel.cpp:881-887` was written to prevent.

## ✅ Live Walker's CE paths now carry `delegate_pad` `[CEPATHS-UNPADDED-2026-09-09]`

Found while closing `[SW4-CEPAD-2026-09-09]`, which proved the CLIPBOARD path correct. The other
two CE-facing buttons on the same grid row do not agree with it.

**Measured on DumperTest 5.4 Development**, `Multicast_Inline` at offset `0x980`, actor
`0x1ED06BBCAE0`, `delegate_pad=8`:

| path | address used | what it points at |
|---|---|---|
| `Copy CE XML` (`CeOffset`) | `+988` → `0x1ED06BBD468` | `InvocationList::Data` — reads `0x1ED33872F80` ✅ |
| `HEX` button (navigate hex view) | `0x1ED06BBD460` | the access detector — reads **0** ❌ |
| `+CE` button (add record) | — | **nothing happens at all** ❌ |

1. **`HEX` uses the UNPADDED `FieldAddress`.** The UI log line is
   `AOBMaker: navigated hex view to 1ED06BBD460`. The Live Walker's Address column shows the same
   unpadded value, so this is consistent with what the user sees — but on a checked build it
   parks CE's hex view on the 8-byte access detector rather than on the invocation list.
2. ~~**`+CE` silently does nothing on a delegate row.**~~ ⛔ **THIS HALF WAS WRONG AND IS
   RETRACTED.** Re-tested deliberately on 2026-09-09 and the button works:
   `AOBMaker: created memory record 'Multicast_Inline' @ 22DB4A9D468 (type 3)`. The original
   observation was a computer-use click that missed the button, not a defect — the row carries
   an extra `{}` expander that a scalar row does not, and the miss produced exactly what a
   silent failure would. Reading the code first would have caught it: `MapCeField` has explicit
   arms for all three delegate types and `MapFieldToCeRecordType` returns `PointerRecordType`
   for a null, so there is no path that throws or returns early. ⚠ A finding that survives
   only because nobody re-ran it is worth no more than the run that produced it.

### ✅ RESOLVED 2026-09-09 — a payload address, distinct from the field address

`FieldAddress` was left UNPADDED on purpose. It is the address of the FIELD, which is what the
Address column shows and what a reader comparing against an offset table expects; the exporter has
always drawn the same distinction, emitting `<Description>"Multicast_Inline (980)"` (the field
offset) with `<Address>+988</Address>` (the payload). Making `FieldAddress` padded would have made
the grid disagree with every offset table for one CE-facing reason.

Instead `LiveFieldValue.PayloadAddress` = `FieldAddress + DelegatePad`, and both CE-facing
handlers use it. **Verified live** on DumperTest 5.4 Development, where the two differ by 8:

    AOBMaker: navigated hex view to 22DB4A9D468            (was ...460, the access detector)
    AOBMaker: created memory record 'Multicast_Inline' @ 22DB4A9D468 (type 3)

Pinned by three tests in `CeXmlDelegatePadTests`: the pad is added; the two addresses are the same
object when there is no pad (a payload address that drifted on an ordinary `IntProperty` would be
far worse than the defect it fixes); and an unparseable address comes back unchanged rather than
becoming `0x8`.

## ✅ The clipboard failure now leads with the imperative `[CLIPELLIPSIS-2026-09-09]`

Observed during SW2's live run (`[SW2-CLIPDELIVERY-2026-09-09]`), not inferred: with the clipboard
held, the toolbar showed

    ERROR: could not write to the clipboard — the inject ...

and stopped there. The full message is **206 characters** and `so do not paste` — the only part
that tells the user what to *do* — begins at **index 135**.

**Measured, both ends:**

| where | markup | effect |
|---|---|---|
| `MainWindow.axaml:41-46` `StatusText` | `MaxWidth="360"` + `TextTrimming="CharacterEllipsis"` | ellipsed; full text only in `ToolTip.Tip` |
| `MainWindow.axaml:47-53` `ErrorMessage` | same | same |
| `LiveWalkerPanel.axaml:519-522` `ErrorMessage` | `TextWrapping="Wrap"`, no MaxWidth | **full message visible** |
| `InstanceFinderPanel.axaml:69-73` `ErrorMessage` | `TextWrapping="Wrap"`, no MaxWidth | **full message visible** |

So the 7 `SetError` delivery sites are fine — they render in the panels, which wrap. The gap is
the **MainWindow toolbar**, which is where the `StatusText` delivery sites report, including the
`InjectCeBootstrapAsync` site SW2 exercised.

⚠ **Scope it honestly.** The message is not lost — `ToolTip.Tip` carries it, and that is how SW2
read it. Nothing is broken in the delivery logic; the register row closed on it. What this is: the
one sentence the whole `FailureText` wording exists to deliver (`ClipboardDelivery.cs`' own
`<remarks>` says the actionable part is *"the clipboard still holds something else, so pasting now
runs the wrong script"*) is the sentence a user does not see unless they hover.

⛔ **Do NOT "fix" this by removing the MaxWidth.** The comment right above it
(`MainWindow.axaml:38-39`) says why the cap is there: a long status line pushes the rest of the
toolbar off-screen and wraps. That trade-off was made deliberately.

### ✅ RESOLVED 2026-09-09 — front-load the imperative

Chosen because it is the only option that costs nothing. Widening the toolbar re-breaks what the
`MaxWidth` was added for; routing to a wrapping surface would move where SIX delivery sites report
for one presentational reason. Reordering fixes it at EVERY truncation width, including ones
nobody has measured.

    before:  ERROR: could not write to the clipboard — {what} was NOT delivered. The clipboard
             still holds whatever was there before, so do not paste. …
    after :  ERROR: do not paste — the clipboard still holds whatever was there before.
             {what} was NOT delivered; another application may be holding the clipboard open. …

"do not paste" moves from character **135 to character 7**. Nothing is lost — the wrapping panel
surfaces show the same sentences.

⚠ Pinned by a test that asserts the INDEX, not the presence: presence was already true while
the defect was live. `FailureText_PutsTheImperativeWhereTruncationCannotEatIt` requires it inside
the first 40 characters AND ahead of the caller-supplied `{what}`, which varies in length —
otherwise a longer subject pushes the imperative back out of view.

## ✅ TMap geometry's TWIN — both copies now REFUSE `[TMAPGEOM-TWIN-2026-09-09]`

Found 2026-09-09 while building the SW8 rig, by discovering that the rig's *intended* observable
was measuring the wrong function.

`Ubel::GetMapPairLayout` is **not** what the instance walk uses. `WalkInstance` carries its own
**inlined copy** of the same map geometry (`Ubel.cpp:~4730`, and a second at `~4887`) and never
calls it. The four real callers are all recursive collectors in `Aura.cpp` —
`CollectContainersRecursive`, `CollectRefMetaRecursive`, `CollectSchemaLeaves`, `ScanForValue`.
⚠ So `map_stride` on the wire is **not evidence about `GetMapPairLayout`**, and a rig that
asserts on it measures the twin. (`sw8_map_geometry.py` drives `CollectSchemaLeaves` instead,
and its docstring carries this as TRAP 1.)

**The twin still has the shape `[TMAPGEOM-2026-09-09]` removed from the original:**

```cpp
if (keyTypeName == "StructProperty") {
    uintptr_t kStruct = 0;
    if (Macht::ReadSafe(keyProp + DynOff::FSTRUCTPROP_STRUCT, kStruct) && kStruct) {
        fv.mapKeyStructAddr = kStruct;      // faulted read -> stays 0, and we CONTINUE
    }
}
...
int32_t keyAlign = ResolveElementAlignment(keyTypeName, fv.mapKeySize, fv.mapKeyStructAddr);
```

`ResolveElementAlignment(..., 0)` → `GetStructAlignment(0)` → **0 = "unknown"** → the caller falls
back to `ComputeMapValueOffset`'s size guess. `GetStructAlignment`'s own header says what that
costs: *"`TMap<int32, FVector>` put the value at +8 when FVector is 4-aligned and really sits at
+4 — wrong for element 0, and wrong again in the stride."*

⚠ **State this precisely — it is NOT "the walk path is broken".** The fallback is the behaviour
that shipped for years, and refusing there would BLANK a map in the UI rather than show it
slightly wrong, which is a different trade-off from a background collector's. The actual defect
is that **the two twins now disagree on the same input, and nothing says so**: one refuses and
logs, the other guesses silently. Whichever is right, they should not differ by accident.

### ✅ RESOLVED 2026-09-09 — the twins now agree, and they agree on REFUSING

The alternative was to keep guessing in the walk, on the grounds that a slightly-wrong map beats a
blank one in a UI. That reasoning does not survive contact with what a wrong stride actually
renders: not "slightly wrong" values but **confident values read from the middle of the previous
pair**, with nothing on screen to say so. Publishing nothing and naming the reason is the same
call `ReadDelegateArrayElements` already makes for an unrecognised stride, so the codebase now has
one answer to this question instead of two.

**What changed** (`Ubel.cpp`, BOTH copies — FProperty and UProperty):
* a `mapStructAddrsOk` flag, cleared in either faulted-read branch;
* the element read is gated on it, so no pair is ever strided at a guessed alignment;
* the field still publishes its HEADER — name, type, element count — and carries
  `"(TMap - FStructProperty::Struct unread, pairs not read)"` as its value, so the refusal is
  visible rather than looking like an empty map.

**Not live-reachable, by construction**: it fires only when the engine's own
`FStructProperty::Struct` pointer cannot be READ, which needs the page-edge fixture
`[TMAPGEOM-2026-09-09]` built for `GetMapPairLayout`. What WAS checked live is the absence of a
regression — on DumperTest 5.4 the struct-valued maps still report their strides
(`Map_IntToVec3f` 24, `Map_IntToVecLwc` 40) with `map_value_struct_type` resolved, i.e.
`mapStructAddrsOk` stays true on the normal path.

## ✅ TMap geometry — the fixture that looked impossible `[TMAPGEOM-2026-09-09]`

`GetMapPairLayout` dropped both `FStructProperty::Struct` reads. On a faulted read the addr stays
0, `ResolveElementAlignment` is asked to align a struct it cannot see, and whatever it guesses
flows into `pairAlign` → `pairStride` — while the comment two lines below says out loud that *"the
stride must be a multiple of it, or every element after index 0 lands at a wrong address"*. The
sweep filed this site as *"the same question unanswered"* in round 2 and never answered it. It was
repaired 2026-09-09 and recorded as **not live-verifiable**. It is now verified.

### ⛔ WHY THE OBVIOUS FIXTURE PROVES NOTHING — and why the row was nearly closed wrongly

Every other fault fixture here points a pointer at `0x1000`, so the whole target is unreadable.
**That does not reach this arm.** A wholly-unreadable property makes `GetFieldTypeName` return
`"Unknown"`, the `keyTn == "StructProperty"` test fails, and the function returns false *before*
the struct read — the same outcome as the fix, for a different reason. A rig built that way would
have gone green while measuring nothing.

The repair only matters for a **partial** failure: the property readable at `+0x08` (FFieldClass)
and `+0x3C` (ElementSize), unreadable at `+0x78` (Struct). Nothing in a live game hands you that,
and the pipe has no way to unmap a page inside the target.

### ⭐ Manufactured with a page edge, in our own process

`dll_core_test` compiles `Ubel.cpp` and `Macht::ReadSafe` reads **this** process — so the fixture
is built where `VirtualAlloc` is available:

* **two pages RESERVED, one COMMITTED**, and the synthetic `FStructProperty` laid at
  `page + 0x1000 - 0x40`. `+0x08` and `+0x3C` are then the last readable bytes (ElementSize ends
  exactly at the page edge) and `+0x78` lands in the uncommitted page;
* a fake **UE4 name pool** (`Serie::InitUE4`, chunks → chunk → entry → string at `+0x10`) so
  `GetFieldTypeName` answers `"StructProperty"` — without it the walk never enters the branch;
* `ElementSize = 12`, because `ResolveInnerSize` tries ElementSize **first** and returns before
  ever touching `FSTRUCTPROP_STRUCT` — which is what makes the struct read the *only* faulting
  read in the straddled case.

⚠ **The block must stay LAST in `dll_core_test`.** `Serie`'s pool state lives in file-statics no
header exposes, so `InitUE4` cannot be undone; anything appended after it would run against a fake
UE4 name pool.

### ⭐⭐ The control, and the number the mutation produced

The **same fake, wholly inside the committed page**, must lay out and produce a stride. Without
that control a refusal would only prove the fake was broken.

Mutation — disable the refusal and the fixture's two ⭐ checks go red:

```
FAIL  TMAPGEOM ⭐: a faulted FStructProperty::Struct REFUSES the layout
FAIL  TMAPGEOM ⭐: and no stride was published from an unread struct pointer   got: 36
```

**`got: 36`** is the defect stated as a number: a pair stride fabricated from a struct pointer
nobody could read, published as fact. `Ubel.cpp` restored byte-identically and rebuilt green
(62 checks in `dll_core_test`, was 55).

### ⬜ Still open

* **This is an OFFLINE fixture, not a live one**, and deliberately: the partial-read condition
  cannot be produced inside a game from the pipe surface. It drives the real `GetMapPairLayout`
  against real `Macht::ReadSafe`, so it is not a double — but it is not a running UE title either.
* **The refusal is now `return false` for the whole probe loop**, not `continue` to the next
  candidate offset. That is deliberate — `GetFieldTypeName` already agreed this is the right
  property, so the fault is real rather than a wrong guess — but it does mean a build where the
  struct offset itself is misprobed now yields no layout instead of a wrong one. No title has
  shown that.

## ✅ `(stale)` — all FIVE sites, one definition `[STALE-NONE-2026-09-09]`

The seventh defect was repaired in one reader; the ask was "fix the other two". ⭐ **There were
FIVE**, and finding that out first is the point — this sweep's enumerations have already been
wrong twice ("five readers" missed the CE exporter, then missed a sixth DLL site).

```
grep -n '"(stale)' dll/src/Ubel.cpp   ->  ReadDelegateArrayElements
                                          ReadMulticastDelegateArrayElements' preview
                                          the single-field DelegateProperty handler
                                          the sparse-binding elements
                                          the multicast inline element loop
```

A sixth hit is a **weak pointer**, not a delegate — `(stale)` is correct there and it was left
alone.

### The defect

`"(stale)"` is an **affirmative claim**: a target *was* bound and has since been collected. An
untouched `FScriptDelegate` has `Object = {0,0}` and `FunctionName = NAME_None`, and
`Ubel::ReadFName` resolves index 0 to the **string `"None"`** — which is not empty. So every
`!funcName.empty()` test called an untouched slot stale.

### The repair — `Ubel::DescribeScriptDelegate`

One pure, header-inline definition; all five sites call it. ⚠ `hasTarget` is a separate argument
from `targetName` because a resolved object whose name could not be read is **not** stale — it
renders `?::Func`, which is what the call sites did before and what a naive
`targetName.empty()` test would have silently changed.

`IsNamedDelegateBinding` keeps the preview builders' "which bindings are worth listing" test on
the same definition of *named*, instead of a second `!empty()` beside the first.

### Coverage

11 checks in `dll_helpers_test`, and the ⭐ **controls are what make it a narrowing rather than a
deletion**: a real name with no live target must STILL say `(stale)::OnFire`, and an object index
with no name must still say `(stale)`.

Mutation-tested — reverting `named` to the pre-fix `!funcName.empty()` fails exactly **3**:

```
FAIL: stale: an untouched slot is UNBOUND, not stale
FAIL: stale control: an object index with no name is still stale
FAIL: stale: serial set without an index is not called unbound
```

while every bound/stale control stays green. `Ubel.h` restored byte-identically.

### Re-verified live after the refactor — five sites moved, so all four rigs re-ran

| rig | Development | Shipping |
|---|---|---|
| `d4b_delegate_pad` | ✅ pad 8, `Arr_Deleg` `['(unbound)', 'DumperTestActor_0::D4b_OnPingProbe']` | ✅ pad 0, identical |
| `d3_delegate_array_unread` | ✅ three-state, baseline `['(0 bindings)', '(1 binding) [...]']` | — |
| `d5_lazyguid_unread` | ✅ | — |
| `d1_collision_refusal` | ✅ both arms still naming different causes | — |

A refactor that touches five rendering sites is exactly the change where "the tests pass" is not
enough — the live strings are the acceptance, and they are unchanged except where they were
wrong.

### ⬜ Still open

* **The `"None"` string itself is a Serie-level convention**, not a checked constant. If
  `ReadFName` ever returned something else for index 0 — a localisation, a different pool
  reading — the narrowing would silently stop firing and every untouched slot would read stale
  again. No gate pins it.

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

## ✅ UE4 — the UProperty path, finally exercised `[D4B-UE4-2026-09-09]`

`[D4B-DELEGATEPAD]` now gates five DLL readers plus the CE exporter on
`DelegatePadFromElementSize`, and it had only ever met UE 5.4 / 5.7 / 5.8. Two things about UE4
were untested, and CLAUDE.md names UE4 a **priority target**:

* ⭐ **`DynOff::bUseFProperty == false`** (UE < 4.25) — the walker reads `UProperty` objects out
  of GObjects instead of the `FField` chain and reports ElementSize from a different place.
  Nothing had ever asked what that path reports for a delegate, and the derivation **refuses**
  a size it does not recognise, which would blank the field.
* UE4 has **no access detector at all** — `TScriptDelegate` / `TMulticastScriptDelegate` gained
  their `TDelegateAccessHandlerBase` base in UE 5.3, and 4.15, 4.23 and 4.27 were each checked:
  none has it. ⚠ So a UE4 run tests **RECOGNITION, not the pad** — pad 0 in every UE4 build
  configuration. Do not read a pad-0 result here as evidence about the checked-build side.

### The fixture — `tools/ue-sample/ue4-delegate-fixture/`

A portable `ADelegatePadFixture` (`.h`/`.cpp` + `install.py`) rather than a port of DumperTest's
property zoo, which **cannot** be ported: `TOptional` UPROPERTY (`FOptionalProperty` post-dates
5.0), Utf8Str/AnsiStr (5.5) and much else do not exist in UE4. The four shapes the changed
readers actually handle do: a bound `MulticastInlineDelegateProperty`, a bound `DelegateProperty`,
a `TArray<multicast>` with **[1] bound and [0] empty** (the only way an element STRIDE is
observable), and the inherited sparse delegates.

⭐ **The survey needs only the CLASS.** A `UCLASS` in a game module is registered in GObjects at
module load, so `d4b_pad_survey.py` reads its ElementSizes without the actor ever being spawned.
Only the binding-read rig needs an instance.

### Result — UE 4.23 and 4.27, packaged Development, injected

| engine | config | `use_fproperty` | classes | delegate properties | pad | unrecognised |
|---|---|---|---|---|---|---|
| **4.23** `UE423_Flying` | Development | **false** — ⭐ the path under test | 600 | 264 | 0 | 0 |
| **4.23** | **Shipping** | **false** | 600 | **272** | 0 | 0 |
| **4.27** `UE427_3rdPerson` | Development | true | 600 | 235 | 0 | 0 |
| **4.27** | **Shipping** | true | 600 | **235** | 0 | 0 |

All four `validated: true`. Together with the UE5 runs that is **five engine versions across both
property systems** — 4.23 (UProperty), 4.27, 5.4, 5.7, 5.8 (FProperty) — and both build
configurations on 4.23, 4.27, 5.4 and 5.8.

### ⚠ Shipping was added because the first UE4 pass had the population backwards

The 4.23/4.27 runs were **Development only**, which is the wrong way round for this repo: every
real title measured here is a **Shipping** build, and Shipping additionally strips logging and
editor-only reflection. ⭐ On UE4 the PAD cannot differ — no access detector exists before UE 5.3
— so what the Shipping pass actually sanity-checks is the *other* axis: that a stripped build
still reports delegate ElementSizes the derivation recognises, and still carries the fixture at
all. It does, on both:

```
DelegatePadFixture present: True
  Multicast_Inline        ('MulticastInlineDelegateProperty', 16)
  Del_Unicast             ('DelegateProperty', 16)
  Arr_MulticastDelegates  ('ArrayProperty', 16)
```

⚠ **264 → 272 on 4.23 is not a discrepancy to explain away.** The survey walks the FIRST 600
classes `list_classes` offers, and which classes are loaded differs between configurations. It is
a different sample of the same population, not the same sample measured twice.

And the fixture's own rows, identical on both engines:

| property | type | ElementSize | implies |
|---|---|---|---|
| `Multicast_Inline` | `MulticastInlineDelegateProperty` | 16 | pad 0 |
| `Del_Unicast` | `DelegateProperty` | 16 | pad 0 |
| `Arr_MulticastDelegates` | `ArrayProperty` | 16 (TArray header) | — |
| 16 inherited `On*` | `MulticastSparseDelegateProperty` | 1 | `sizeof(FSparseDelegate)` ✓ |

So the derivation recognises everything UE4 reports, on the `UProperty` path, with 264 witnesses.

### ⛔ Three UE4 blockers, all measured, two still open

| engine | state |
|---|---|
| **4.23** | ✅ builds **and packages**, and surveyed. Needed VS2017 **and** write access to `UE_4.23`. ⚠ `--pin-compiler 14.16.27023`, NOT 14.29.30133 — 4.23's UBT maps toolset→VS version and rejects a VS2022 toolset outright. |
| **4.27** | ✅ builds, packages and surveyed, once `UE_4.27` was opened the same way. Its blocker was UAT writing its cook log to `<engine>\Engine\Programs\AutomationTool\Saved\Cook-*.txt`. ⚠ `uebp_LogFolder` does **not** redirect that — tried. |
| **4.15** / **4.18** | ⛔ **NOT a permission problem any more, and NOT a fixture problem — the wall is measured and it has no knob.** Both now compile 30-45 s of real work before dying in a SYSTEM header: `Windows Kits\10\include\10.0.26100.0\ucrt\wchar.h(316): error C3861: '_mm_loadu_si64'`. That breaks every `.cpp` in the module, mine included and mine last. See the ⛔ block below. |
| **4.11** | ⛔ needs VS2015-era support the maintainer ruled out of scope. ⚠ For the record, VS2015's `cl.exe` IS present (`Microsoft Visual Studio 14.0\VC\bin\amd64`), so the obstacle is not the compiler. |

⚠ 4.11 and 4.18 are installed but have **no C++ project**, so there is nothing to host a
`UPROPERTY` fixture. (The other projects under `D:\Unreal Projects` are Blueprint-only; these five
UE4 ones all have a `Source/` module — verified, not assumed.)

### ⛔ 4.15 AND 4.18 CANNOT BUILD HERE, AND THE REASON HAS NO OVERRIDE

Worth writing down in full, because "old engine, probably the compiler" is the wrong diagnosis and
would send the next session installing things.

`UE_4.15` and `UE_4.18` are now writable, `UE418_3rdPerson` was given a C++ module (it was
Blueprint-only, so it could not host a `UPROPERTY` at all), and the fixture installs. Both then
fail identically:

```
Windows Kits\10\include\10.0.26100.0\ucrt\wchar.h(316): error C3861: '_mm_loadu_si64'
```

`wchar.h` is a SYSTEM header pulled in by CoreMinimal, so this breaks every translation unit in
the module — the fixture is the last thing implicated, not the first. The 26100 UCRT uses an
intrinsic that neither VS2015 nor VS2017's final toolset (**14.16.27023**, all VS2017 ships)
defines.

⭐ **AND THERE IS NO SUPPORTED WAY TO POINT THEM AT AN OLDER SDK.** Measured, not assumed:

* Older SDKs **are** installed — 8.1, 10.0.10240, 19041, 22621.
* `--pin-compiler` is inert: 4.15's UBT cannot even parse the modern `BuildConfiguration.xml`
  (`XmlConfigLoader: Reading config XML failed`), and the toolset is not the discriminator anyway.
* Forcing **VS2015** with `-2015` changes the compiler and **not** the SDK — both engines fail
  with the identical line. So the compiler is not what selects it.
* `UEBuildWindows.cs` (4.18) exposes `WindowsPlatform.Compiler`, `StaticAnalyzer`,
  `bStrictConformanceMode` and `ObjSrcMapFile` as `[XmlConfigFile]` — and **no SDK version knob**.
* `VCEnvironment.FindWindowsSDKExtensionLatestVersion` simply enumerates the directories under
  `Windows Kits\10\include\` and keeps the **maximum**. Nothing filters it.

So the only lever is removing or hiding `10.0.26100.0` from that directory — which every UE5 build
on this machine depends on. ⛔ Not a trade worth making for a third data point in a regime 4.23
already covers.

⚠ `UE418_3rdPerson` keeps its new `Source/` module and its `.uproject` change (the maintainer's
call: *"改 .uproject 沒差, 那只是 sample"*). It is correct and will build the day the SDK situation
changes; `UE418_3rdPerson.uproject.pre-cpp.bak` is the exact undo.

### ⛔ AND THE MODULE-PCH RULE HAS TWO OPPOSITE HALVES

Two rules, opposite to each other, and **the engine version is the wrong discriminator** —
4.18 supports both and the template picks one:

* **module-wide PCH** (no `PCHUsage` in `Build.cs` — UE 4.15's template): every `.cpp` must include
  the MODULE header first, or UBT refuses with *"All source files in module X must include the
  same precompiled header first"*.
* **IWYU** (`PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs` — 4.18, 4.23 and 4.27's templates):
  the file's OWN header must be first — *"Expected DelegatePadFixture.h to be first header
  included."*

⚠ Prepending unconditionally fixed 4.15 and **broke 4.18**. `install.py` now reads `Build.cs`,
which is where the answer actually lives, and prepends only in the module-PCH case. The module's
name is per-project, which is why the STORED pair cannot carry the include and the INSTALLED copy
must.

### ⛔ AND A UE4 TRAP THAT READS AS A BROKEN TOOLCHAIN

Both 4.23 and 4.27 ship `Build\InstalledBuild.txt` but **not**
`Binaries\DotNET\AutomationToolLauncher.exe`. `RunUAT.bat` line 12 sets
`UATExecutable=AutomationToolLauncher.exe` and line 24 jumps straight to `:RunPrecompiled` on an
installed build — so it executes a file that is not there. cmd answers **ERRORLEVEL 9009** and
RunUAT prints only `BUILD FAILED`, 0.1 s in, naming nothing. The fallback that would fix it
(line 47, "if the launcher is missing use AutomationTool.exe") lives in the **non-installed**
branch and is therefore unreachable exactly where it is needed. `AutomationTool.exe` is present
and works (`-help`, exit 0); `repackage.py` now calls it directly when the launcher is the only
thing missing.

### ⬜ Open

* **4.15 / 4.18 stay blocked** on the Windows SDK, with no override — see the ⛔ block above. The
  4.18 scaffolding is done and waiting.
* **No UE4 READ test.** The survey covers the class table; `d4b_delegate_pad.py` needs a live
  instance and nothing spawns `ADelegatePadFixture`. `install.py --spawn-from` prints the
  two-line patch rather than applying it — a blind regex edit of someone else's project template
  is the wrong trade.
* **`walk_class` (singular) returned 0 fields for a class `walk_class_batch` walks fine** (76
  fields, same address). Noticed while verifying the fixture landed; not chased, and the survey
  uses the batch form.

## ✅ The reconciliation's actionable half, fixed `[D4B-TAIL-2026-09-09]`

A 9-agent reconciliation of this whole work stream (four readers over four independent sources,
then four adversarial lenses) answered "is everything in scope verified?" with **no** — six items
live-verified out of roughly fifty — and, more usefully, found **defects of this stream's own
shapes surviving inside the blocks the stream had just edited**. Verified by hand before fixing;
these are the ones that were real.

### ⛔ Three in the DLL, all in code `2c1d54ff` had touched hours earlier

| site | what it still did |
|---|---|
| `Ubel.cpp` `DelegateProperty` handler | both `Macht::ReadSafe` returns dropped — **four lines below the `dPad` line that same commit added**. A faulted read left objIdx/serial at 0 and published the affirmative `"(unbound)"`. D3/D5 verbatim. |
| `Ubel.cpp` `ReadMulticastDelegateArrayElements` | `if (innerCount > 4096) innerCount = 0;` → `"(0 bindings)"`. **This is D4's defect spelled again**: the identical `invNum = 0` clamp was deleted from `Aura.cpp` on 2026-09-08 *with a ⛔ comment saying never restore it*, while two copies survived in Ubel. ⚠ D4b's own fingerprint went straight through it — Num read at the wrong offset was **322437056**, over the ceiling, so this line would have swallowed the symptom. |
| `Ubel.cpp` MulticastInline element loop | third copy of the dropped pair, and the one that reaches a LIST: an unread binding fell to the final `else` and rendered `"(unbound)"` for an entry the invocation list says EXISTS. |

Both bare `4096`s are gone — they now use `Aura::kMaxPlausibleInvocationListNum`, the constant
this stream had already moved to `Aura.h` for exactly this reason.

### ⛔⛔ And one OUTSIDE the DLL — the artefact the user actually pastes into Cheat Engine

**`[D4B-DELEGATEPAD]`'s enumeration said "five readers assumed the unpadded layout" and was
DLL-only.** `CeXmlExportService` emitted `Offsets=[0]` at a delegate field's *raw* offset, whose
first 8 bytes on a checked build are the zeroed access detector — so the pasted CE record pointed
at **address 0**. Its comment asserted *"the field's first 8 bytes are the InvocationList::Data
pointer"* unconditionally; that was true only on the Shipping half. `scripts/ue5_dissect.lua`
baked `size = 16` the same way.

The pad now travels on the wire as `delegate_pad` — `LiveFieldValue::delegatePad` (DLL) →
`Fern.cpp` (emitted only when non-zero, so a Shipping wire is unchanged and an older UI never
sees the key) → `DumpService` → `LiveFieldValue.DelegatePad` (C#) → a single `CeOffset(field)`
helper used at all 21 address-emit sites. ⭐ **Sent, not re-derived**: the DLL already computed it
from the engine's own ElementSize, and a second implementation of the rule in C# or Lua is a
second thing to get wrong — which is precisely what the reconciliation caught in
`d4b_pad_survey.py`, a Python re-implementation that therefore verified the copy rather than the
shipped rule.

⚠ **The first attempt at this fix was wrong and the tests are what said so.** The pad was applied
at the exporter's field *projection*, which turned out to serve nested structs only — the
`GenerateInstanceXml` path never passes through it, so the emitted address stayed `+2C0`. Worse,
had both been changed the pad would have been added **twice**, because the projected record
copies `DelegatePad` forward. Red-before-green is the only reason that surfaced.

`ue5_dissect.lua` now prefers the engine's ElementSize for the delegate family — guarded to the
two widths the layout can actually have, so a garbage ElementSize falls back to the baked size
instead of widening a CE row arbitrarily — exactly as its own `EnumProperty` branch already did.

### ⛔ And a rig this stream broke the same day

`d3_delegate_array_unread.py` asserted `all(v == "(0 bindings)")` for its baseline. D4b's fixture
work then **bound element [1]** (to make the stride observable) without touching the rig, so a
passing verification was silently turned into a failing one. **No gate reads these rigs**; a rig
is only run when someone remembers the row. Now asserts the shape the fixture guarantees —
`[0]` empty, `[1]` naming its probe — which also makes it a D3b check rather than only a D3 one.

### Coverage

`CeXmlDelegatePadTests` (4 cases) and a D4b block in `dissect_test.lua` (10 checks). Both
mutation-tested:

* reverting `CeOffset` to `field.Offset` fails **3** — and the **Shipping control stays green**,
  which is the point: the old code was right on the side every real title is built with, so a fix
  that moved the offset unconditionally would have broken every real export;
* removing the Lua delegate branch fails exactly the **2** checked-build checks, controls green.

Sources restored byte-identically after each. 18 gates green — `check_derived_counts` caught the
new C# test file and both docs were corrected **from the tree**, 187 → 188.

### ⬜ Found while fixing, deliberately NOT fixed

* **A scalar `DelegateProperty` produces NO CE entry at all** — the multicast path emits, the
  single-cast one is silently absent from the exported table. Found while writing the tests
  (the unicast assertion could never pass because nothing is emitted); it is a missing feature,
  not a wrong value, and unrelated to the pad.
* **`ue5_dissect.lua` gives `MulticastSparseDelegateProperty` `size = 16`, and an
  `FSparseDelegate` is ONE byte** (`bIsBound`), so that row has always overrun into the following
  fields. Pre-existing and independent of the pad; correcting it changes the rendered width of
  every sparse delegate on every title, which needs CE in front of a human rather than being a
  side effect of a layout fix. Marked ⛔ in the table.
* **None of this is live-verified.** The DLL arms need a faulted read or an implausible Num, and
  the CE half needs a table pasted into Cheat Engine on a *checked* build. Offline only.

## ✅ D4b extended to a SECOND ENGINE VERSION `[D4B-PADSURVEY-2026-09-09]`

`DelegatePadFromElementSize` had only ever met UE 5.4, and it now gates five readers that run on
**every** title. Its refusal arm is the honest failure mode — but on a title whose delegate
ElementSize this repo has never seen, that refusal turns a working read into a blank. So the
question worth asking separately from "does it read correctly" is the narrower one:

> does the derivation recognise the sizes this engine actually reports?

`tools/verify/d4b_pad_survey.py` answers it with **no fixture at all** — it walks whatever
`list_classes` offers via `walk_class_batch` and reads `FProperty::ElementSize` off the class
field tables, so no instance and no bound delegate is needed. ⭐ **It runs on any injected
title**, which is what makes it the reusable screen for a real game.

| engine | configuration | classes | delegate properties | derived pad | unrecognised sizes |
|---|---|---|---|---|---|
| UE 5.4 | Development | — | read test, see `[D4B-DELEGATEPAD]` | **8** (`elem_size` 24) | 0 |
| UE 5.4 | Shipping | — | read test, see `[D4B-DELEGATEPAD]` | **0** (`elem_size` 16) | 0 |
| UE 5.8 | Development | 600 | 33 | **8** | 0 |
| UE 5.8 | Shipping | 600 | 33 | **0** | 0 |

Same 33 properties on both 5.8 flavours (`EmitterCameraLensEffectBase::OnParticleSpawn` and
friends), differing only by the 8 bytes. `MulticastSparseDelegateProperty` reports ElementSize
**1** on every one — `sizeof(FSparseDelegate)`, which is exactly why the sparse walker cannot use
this derivation and needs `LocateInvocationList`'s invariant-based one instead.

⭐ The rig fails on a **split verdict** as well as on an unknown size: the pad is a property of
the BUILD, not of the class, so two answers within one process would mean one of the two base
sizes (16 for the multicast container, `8 + SizeofFName()` for the standalone unicast) is wrong
for that engine. Neither engine produced one.

⚠ **What this does NOT show.** It reads the class field TABLE, so it says nothing about whether
bindings are read correctly — that is `d4b_delegate_pad.py`'s job and it needs DumperTest's
purpose-built bound delegates. The two rigs are complementary and neither subsumes the other.

### ✅ …and on a REAL title — Titan Quest II `[D4B-PADSURVEY-TQ2-2026-09-09]`

The row above listed "no real title has been surveyed" as its residual. Closed the same day.

**Titan Quest II** (Steam appid 1154030), launched with `steam.exe -applaunch`, engine confirmed
really up before anything was believed: **279,587 objects**. A menu is enough — the survey reads
the class field TABLE, so it needs no world, no pawn and no bound delegate.

> `600 classes walked · 46 delegate properties · all pad 0 · 0 unrecognised sizes`
> e.g. `SkeletalMeshComponent::OnConstraintBroken` / `OnPlasticDeformation` / `OnAnimInitialized`

So the readers changed by `[D4B-DELEGATEPAD]` meet nothing on a shipped title that the derivation
cannot name, on a class graph 460× the fixture's.

### ⛔ TWO THINGS THIS RUN CORRECTED OR LEFT OPEN — read before quoting it

* ⚠ **The case-preserving branch is STILL unexercised.** TQ2 was chosen *because*
  `Ubel.cpp:1735`'s comment names it as the "UE 5.7 + CasePreservingName" example, which would
  have driven the unicast base to `8 + 12 = 20`. It does **not**: `get_offsets` on this build
  reports `case_preserving: false`, so the base was 16 and that arm never ran. ⭐ The rig prints
  the flag it used rather than assuming — worth keeping, because a `.get(key, False)` over a key
  that does not exist would have *silently claimed* non-CPN, which is this sweep's own defect
  shape. The key exists and the answer is genuinely false. Either the Ubel comment is stale or it
  is about a different measurement; not chased. `Grimoire.h` already records that
  `bCasePreservingName` has **12 titles measured false** and no title measured true.
* ⛔ **EVERY deployed proxy on this machine was STALE — 10 titles**, and TQ2's was the reason
  `inject.py` refused: a proxy auto-loads at game start and OWNS THE PIPE, so an inject of the
  current DLL is a no-op and everything measured is the old binary. The injector's stale-module
  guard is what caught it.
  ✅ **ALL TEN REFRESHED 2026-09-09** to dist 3462 — EVERSPACE 2 / Avowed / DQ7R / EVERSPACE /
  Lushfoil / Manor Lords / OCTOPATH / Elliot / The Artisan of Glimmith / Titan Quest II. The report
  now reads `10 deployed proxy(ies), 0 stale`, and every replaced copy is in `out/proxy-backups/`
  with its size and SHA. A future row can boot any of the ten and be measuring current code —
  **but check anyway**: the next `dist` build makes all ten stale again by definition.
  ⚠ **Staleness is a SHA comparison, not a size one.** TQ2 was refreshed at 08:52 and read
  `*** STALE ***` again 25 minutes later at the *same* 2,921,472 bytes — the `-Mode Publish` in
  between rebuilt the proxies with a new build number embedded. A size check would have called it
  current, which is why `report()` hashes.

## ✅ D1 — the refused-restore call site, live `[D1-COLLREFUSE-2026-09-09]`

The last of the sweep's DLL fixes that no test could reach. `todo.md` recorded the blocker as
*"every D1 call-site path needs a running game with the PE hook down"* — the pure core
(`ShouldCommitCollision`) was pinned, the call sites were **reviewed, not executed**.

### ⛔ Why the obvious rig would have proved nothing

The restore site short-circuits:

```cpp
const bool responsive = Stark::IsGameThreadResponsive();
const bool restored   = responsive && ShouldCommitCollision(InvokeSetCollision(pawn, true));
```

Freeze the game and `responsive` goes false, so `InvokeSetCollision` is **never called** and the
record survives for B8's 2026-07 reason rather than D1's 2026-09 one. A rig that froze the game,
saw the record kept and called it a pass would have re-measured the wrong repair.

### ⭐ The window that separates them — two different clocks

`Stark::kStallThresholdMs` is **500** (time since the hook last fired); `kMinInvokeTimeoutMs` is
**100** (how long one dispatch waits). Freeze the UE game thread and issue the restore *inside*
that gap and both conditions hold at once: the thread still counts as responsive because it
fired a few ms ago, while the dispatch times out and returns non-zero. That is D1's condition
exactly — **responsive, and refused anyway**. `set_invoke_timeout {timeout_ms: 100}` and
`suspend.py suspend-tid` are the whole apparatus; no purpose-built DLL was needed, unlike the
route PEHOOK 3b documents.

### The result — `tools/verify/d1_collision_refusal.py`, DumperTest dev (UE 5.4)

| arm | frozen | cause the DLL named | record | restore |
|---|---|---|---|---|
| **1 — D1** (issue immediately) | 265 ms | `(the dispatcher REFUSED the restore)` | kept | landed on its own |
| **2 — control** (issue after 1 s frozen) | 1161 ms | `(game thread unresponsive)` | kept | landed on its own |

⚠ **Superseded for arm 1 by B31** (`[W3-DUNSTE-QUEUED]`, 2026-09-12). A -5 timeout is now QUEUED and commits, so
arm 1 reads "QUEUED" with no kept record. The rig asserts that since review 5, and the refused arm has no live
trigger left. Arm 2 is unchanged.

⭐ **The rig runs both and requires them to DIFFER.** With only arm 1 it would pass just as
happily while matching any "keeping the record" line, on either path — the discriminator would
itself be unmeasured. And the ⭐⭐ acceptance is behavioural, not textual: after the thread
resumes, `PendingRestoreLoop` re-enables collision **with no further command**. That can only
happen because the record survived; pre-fix there was nothing left to poll for and the pawn
stayed ghosted for the session.

### ⚠ The control caught a defect in the control

The first draft slept *after* issuing the restore. `IsGameThreadResponsive()` is evaluated the
moment the command is handled, so the extra second changed nothing being measured: arm 2 froze
for **1459 ms and still reported the dispatcher refusal**. A control that cannot fail is not a
control — and this one only revealed itself because arm 1 and arm 2 were required to disagree.

### ⛔ AND A HOUSE-RULE VIOLATION THIS ROW WALKED INTO — two games at once

The first three attempts measured nothing because **`taskkill /IM DumperTest.exe` matches the
Development image only**. Shipping is `DumperTest-Win64-Shipping.exe` and survived every "kill",
so a freshly launched Development fixture could not create `\\.\pipe\UE5DumpBfx` — and every
command went to the *Shipping* process still holding it. The tells: `offsets-0.log` at 122 bytes
for a session that should write thousands of lines, and `find_instances` returning the **exact
addresses** seen in the earlier Shipping run.

`launch_dumpertest.py` now **refuses to launch** when any fixture image is alive (`--allow-second`
to override), listing what it found. The image names are enumerated in `FIXTURE_IMAGES` there,
because the exe name is not derivable from the flavour: only Development is plain
`DumperTest.exe`.

### ⬜ Still open

* **D2 is the one sweep fix still unverified live.** It needs a scan worker to actually THROW,
  which nothing in the pipe surface can induce — no fault-injection hook exists. Its pure core is
  pinned by tests and its deliberate residual (a worker fault reports through `deadline_hit`) is
  recorded above. A live row would need either a fault-injection build or a real OOM.
* The `arm()` helper leaves Fly enabled between arms only briefly, but it does **not** assert the
  pawn is physically colliding again — only that the DLL says it re-enabled it. Reading the
  pawn's collision flag back would close the last gap between "the setter was invoked" and "the
  collision changed", which is the very distinction D1 is about.


## ✅ D4b + D3b — the access-detector pad `[D4B-DELEGATEPAD-2026-09-09]`

Both rows filed on 2026-09-08 are **one defect**, and it is not a UE-version defect — it is a
**build-configuration** one, which is why nothing red had ever appeared.

UE 5.3 gave `TScriptDelegate` and `TMulticastScriptDelegate` a base class,
`TDelegateAccessHandlerBase<ThreadSafetyMode>` (`Delegates/DelegateAccessHandler.h`). With
`DO_CHECK` on — Debug, Development, DebugGame — that base holds one
`FMRSWRecursiveAccessDetector`, whose only data member is a `std::atomic<uint64>`, so **every
delegate payload starts 8 bytes late**. With `DO_CHECK` off (Test/Shipping) the base is the
empty `FNotThreadSafeNotCheckedDelegateMode` specialization and EBO applies. UE 4.27 has no
base class at all, so the constants checked against the DropIn 4.27.2 PDB were never wrong —
just narrow.

| | base | `InvocationList` | `sizeof(FMulticastScriptDelegate)` |
|---|---|---|---|
| Shipping / Test, and every UE ≤ 5.2 | 0 (EBO) | `+0x00` | 16 |
| Development / Debug / DebugGame, 5.3+ | 8 | **`+0x08`** | **24** |

### How it read, and the fingerprint to recognise it by

Raw bytes at the bound `OnActorHit`'s `FMulticastScriptDelegate`, DumperTest Development:

```
+0x00  0000000000000000   std::atomic<uint64> State   (the access detector)
+0x08  405CBFF5D5010000   Data = 0x1D5F5BF5C40
+0x10  01000000           Num  = 1        <- the subscriber, invisible at the old +0x08
+0x14  04000000           Max  = 4
```

At the old offsets that is `Data = 0`, `Num = 0xF5BF5C40` — **Num is the LOW HALF OF DATA**.
⭐ The DLL's own `WARN` had been printing it for a day (`implausible InvocationList
Num=322437056`); decoding `322437056` to `0x1337FFC0` and noticing it was half a heap pointer
is what cracked it. **Read the log before re-deriving the layout from source.**

### ⚠ Two types, one spelling — the trap that made this hard to reason about

A multicast's invocation-list **elements** are `TScriptDelegate<FNotThreadSafeNotCheckedDelegateMode>`,
whose base specialization is empty in *every* configuration, so they are **never** padded and
`8 + SizeofFName()` stays right. A **standalone** `FScriptDelegate` — what a `DelegateProperty`
stores — is `TScriptDelegate<FNotThreadSafeDelegateMode>` and **is** padded. Our comments call
both "FScriptDelegate". `Grimoire.h` now carries this as a ⚠; `Aura.cpp`'s
`ClassReferenceMeta` comment had it exactly backwards ("element layout matches the multicast
bindings list") and is corrected.

### What changed

`DynOff::kDelegateDetectorPad` + `DynOff::DelegatePadFromElementSize(elementSize, baseSize)` in
`Grimoire.h`. **Nothing is version-gated or configuration-gated.** Two derivations, each
authoritative where it is used:

* **Where an ElementSize is in hand** (3 sites in `Ubel.cpp`) — UE computes it as
  `sizeof(TCppType)` (`UnrealType.h`, `TProperty::SetElementSize`), so it already answers "how
  big is this in THIS build". `FDelegateProperty` is `TProperty<FScriptDelegate, FProperty>`;
  `FMulticastInlineDelegateProperty` is `TProperty_MulticastDelegate<FMulticastScriptDelegate>`.
  An unrecognised size is **refused**, not rounded to the nearer candidate.
* **Where there is none** (2 sparse sites in `Aura.cpp` — a `MulticastSparseDelegateProperty`'s
  ElementSize is `sizeof(FSparseDelegate) == 1` and says nothing) — `LocateInvocationList`
  derives it from the object's own invariants. ⭐ **This is a derivation, not a probe-and-hope**:
  reaching it means the delegate's FName was *found* in `FSparseDelegateStorage`, and UE erases
  that entry the instant the delegate empties (every remover in `SparseDelegate.cpp` calls
  `DelegateMap->Remove(DelegateName)` as soon as `IsBound()` reads false), so `Num >= 1` is
  **required**. Both misreadings fail on the first test: on a checked build the pad-0 candidate
  reads the zeroed detector as `Data` and dies on the null; on Shipping the pad-8 candidate
  reads `{Num,Max}` packed as a pointer and dies at the element-0 FName read.

Six call sites: `Ubel.cpp` ×3 (`MulticastInline`/`MulticastDelegate`, `DelegateProperty`,
`ReadMulticastDelegateArrayElements`), `Aura.cpp` ×3 (`WalkSparseDelegateBindings`,
`FindReferencesToUObject`'s sparse pass, `ClassReferenceMeta`'s `TArray<FScriptDelegate>`).

Two more repairs made in passing, both the sweep's own defect shape:

* `ReadMulticastDelegateArrayElements` took `int32_t /*elemSize*/` — an **ignored parameter**,
  overridden by a local `constexpr int32_t elemSize = 16`, while both callers were already
  passing the engine's authoritative answer.
* the `MulticastInlineDelegateProperty` handler dropped **both** `Macht::ReadSafe` returns, so a
  faulted read published the affirmative `(0 bindings)` — D3/D5 verbatim, in a reader the
  2026-09-08 sweep walked past.

`WalkSparseDelegateBindings` also stopped returning **silently** when the `TSharedPtr` yields
nothing; that state and a real layout failure shared one unlabelled bucket.

### ⭐ Verified on BOTH build configurations — `tools/verify/d4b_delegate_pad.py`

One configuration proves nothing about a configuration-dependent layout, so the same rig ran
against the same fixture source built twice:

| | `array_elem_size` → pad | `OnActorHit` (sparse) | `Multicast_Inline` | `Del_Unicast` | `Arr_MulticastDelegates` |
|---|---|---|---|---|---|
| **Development** | 24 → **8** | `(1 sparse binding) [DumperTestActor_0::D4_OnActorHitProbe]` | `(1 binding)` | resolved | `['(0 bindings)', '(1 binding)']` |
| **Shipping** | 16 → **0** | identical | identical | identical | identical |

Development is the repair; **Shipping is the negative control** — the side every real title is
built with, and the one that must not regress. Before the fix Development read
`(sparse, bound — invocation list unreadable)`.

### ⛔ The fixture could not falsify D3b until today, and that was the point of the gap

`Arr_MulticastDelegates` had **two empty elements**, so a 16-byte stride and a 24-byte stride
read the same zeros and printed the same `(0 bindings)`. `BeginPlay` now binds element **[1]**
and leaves **[0]** empty: a reader stuck on 16 reads [1] from inside [0] and reports both empty.
Also added, because neither reader had *any* host here: `Multicast_Inline` (the only non-array
`MulticastInlineDelegateProperty`) and `Del_Unicast` (the only `DelegateProperty` — the padded
standalone type). All three bound; an unbound one reads the same at either offset.

Unit tests: `Test_Delegate_AccessDetectorPad` and `Test_Aura_IsBoundInvocationListHeader` in
`dll_helpers_test`. Mutation-tested — reverting the refusal to a silent pad-0 and dropping the
`Num >= 1` requirement produced **exactly** the 5 predicted failures, no more and no fewer.

### ⛔ AND A TOOLING DEFECT FOUND ON THE WAY — `repackage.py --sync-mirror` built stale reflection

`--sync-mirror` used `shutil.copy2`, which **preserves the mirror file's mtime**. UHT decides
whether to re-run by comparing each header against `Intermediate/Build/.../UHT/Timestamp`, and
any earlier build in the same session has already pushed that forward. A file edited at 08:03,
synced after a build that ran at 08:09, is *older* than the Timestamp: UHT skips, regenerates
nothing, and UBT compiles against the **previous** reflection data.

⚠ It reports `BUILD SUCCESSFUL`. Two new `UPROPERTY`s and a `UFUNCTION` were packaged away, the
game booted normally, and the fields were simply **absent** from `walk_instance`; the only tell
was `DumperTestActor.generated.h` still carrying the previous day's mtime. Fixed by stamping
`os.utime(dst, None)` after each copy. ⚠ Also note `--sync-mirror` is **not** the default and
its "packaging would build the REAL project's source" warning is easy to lose in a tail-only
read of the log — which is how this was hit at all.

### ⬜ Still open, deliberately

* **`Arr_MulticastDelegates` element [0]'s own detector bytes are never shown.** The hex now
  covers the TArray header the reader interpreted, not the 8 bytes in front of it. Fine for
  reading, but a future "why is this element odd" investigation will want the whole 24.
* **DebugGame is STALE** — only Development and Shipping were repackaged.
  `capture_package_identity.py` exits 1 and names it, which is the gate doing its job. DebugGame
  is a checked build, so it would exercise the same pad 8 as Development.
* **No non-DumperTest title has been re-measured.** Every one is Shipping (pad 0) and the
  Shipping column above is that path, but the claim "no regression on real titles" rests on the
  fixture, not on a re-run of a real game.
  ⭐ Now one command away — see `[D4B-PADSURVEY-2026-09-09]`.
* ✅ **`ConcurrentRescores_SettleOnTheNewestMode_NotTheLastToFinish` was load-flaky — INVESTIGATED
  and FIXED 2026-09-12** (`[TESTFLAKE-2026-09-12]`). First seen 2026-09-09 in a `-Target Test` run
  sharing the machine with `check_all.py`, then measured again 2026-09-12 at 1 failure in a 5204-test
  run and 1 of 3 class-isolated runs.
  - **The test raced the window it names, and always had.** `3ad4f524` parked its two siblings with
    `GatedEntryList` and said it had parked this one too; it had not. Firing both toggles straight
    after `ExecuteAsync` left FOUR landing zones — only one of which is the scenario the name
    describes — so `PendingRescore` could be null, the two `if (… != null)` drains could skip
    silently, and the single-slot `PendingToggleRescore` could orphan the first toggle's re-score,
    leaving it rebuilding `Results` while the assertion enumerated it.
  - **Not a product regression:** the OLD test passed 15/15 idle and 25/25 under deliberate CPU
    contention; what varied was which zone it landed in, not the VM's end state.
  - **Fixed by enforcing the interleaving**, not by retrying: a multi-pass `PhasedEntryList` parks
    the load's scoring, the load reconciliation's re-score and the untoggle's re-score, releases the
    NEWER request first and the OLDER one's scoring LAST, and pins both re-scores in flight at once
    (`Assert.False(rLoad.IsCompleted)`), with `Assert.NotNull` replacing both silent skips and the
    repo's bounded wait replacing the bare `await load`.
  - **Shown able to fail:** deleting `RescoreAsync`'s generation check makes it red in 50 ms with the
    original `Assert.DoesNotContain() Failure: Filter matched in collection` — deterministically now,
    where the pre-2026-09-12 version needed luck.
  - ⬜ **Lead, recorded rather than claimed:** `RescoreAsync`'s generation check (`:615`) is still not
    atomic with its publish (`:621-623` + `ApplyFilter`), and `ApplyFilter` rebuilds the shared
    `Results` collection unsynchronised. Avalonia's dispatcher serialises both in the shipped app, and
    no test seam can suspend a run between its guard and its publish — so this window is **not
    reproduced** and was NOT fixed here. It is a real check-then-act nonetheless.


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

- **✅ Multi-pipe Phase 1 — §9.6 item 5 REOPENED, REPRODUCED, FIXED and RE-VERIFIED 2026-09-07;
  the watch item was already closed** — `[MULTIPIPE-CANCEL-2026-09-07]`
  ⭐ **Read the body for the trail, not just this line.** Opened the day it was closed-by-mistake:
  the evidence on file described the *opposite* experiment. Measuring it reproduced a real defect
  (a foreign client's death truncated another connection's scan and answered `ok:true`), which is
  now fixed for pipe handlers (`bea9009c`), for Aura's parallel workers (`257878a3`), flagged when
  it does happen (`c4e270e9`), and **verified end-to-end on a real game with a true before/after**
  (`0ee59c97`). The two remaining sub-questions were *answered*, not deferred: `RunScan` must NOT
  bind to its requester (a scan is a shared product), and the CE partial fix is unsafe. What is
  genuinely still owed is listed below and is small.
  <!-- superseded header: "⛔ §9.6 item 5 REOPENED with a REPRODUCED DEFECT" -->
  Effort: **S** · Risk: low. The two-connection lane split shipped + in-game verified for §9.6 items
  1–5 (dev-log 2026-06-28).
  > ~~**✅ The lane-drop edge is now verified (Elliot 2026-07-23).** Closing the game mid-snapshot
  > dropped the bulk lane and the router did exactly what §9.7 specifies:
  > `Pipe lane dropped — tearing down both lanes for a clean reconnect` → `Pipe disconnected`, with the
  > in-flight snapshot faulting into H1's delete path rather than half-finishing. No wedge, no orphan.~~
  >
  > ### ⛔ THAT EVIDENCE DOES NOT MATCH THE ITEM — reopened 2026-09-07 `[MULTIPIPE-CANCEL-2026-09-07]`
  >
  > §9.6 item 5 asks for *"disconnect only ONE lane — **the other keeps working**; a bulk scan isn't
  > wrongly cancelled by an interactive disconnect (the §9.3.5 caveat)"*. The Elliot run closed the
  > whole **game** mid-snapshot, so **both** lanes died together and the router tore both down —
  > the opposite scenario. A whole-process death cannot show one lane surviving the other's drop,
  > and it never touched the cancellation clause at all.
  >
  > **The clause is not just untested, it is violated.** §9.3's mitigation never shipped —
  > `inFlightHeavy` appears nowhere in the tree. `Fern::MonitorLoop` (`Fern.cpp:861`) calls
  > `Tot::RequestPerCommand()` **unconditionally** for any broken in-flight connection, and
  > `Tot::g_perCommand` (`Tot.h:45`) is one process-wide atomic OR-ed into every `Tot::Requested()`.
  > The comment defends it with a **timing** argument — *"a fast light command finishes before a
  > 200ms peek catches it in-flight"* — that had never been measured.
  >
  > **Measured** by `tools/verify/multipipe_cancel_isolation.py`, DumperTest Development at 1 FPS,
  > build 3405, reproduced twice:
  >
  > | | |
  > |---|---|
  > | baseline `list_enums` | 720,793 bytes (5 samples, spread 0) |
  > | victim replies after a FOREIGN client's death | 5,264, of which **5,157 short** |
  > | truncated sizes | **77 / 78 / 79 bytes** |
  > | blast radius | 0.14 s → 0.71 s, then recovers |
  >
  > ⛔ **The severity is not the truncation — it is that all 5,157 truncated replies said
  > `ok: true`.** A caller gets 77 bytes where 720 KB was due, with a success status, and cannot
  > tell it from a complete answer. Bounded (~0.6 s, audit #5's `ReevaluatePerCommandCancel` clears
  > the latch) but silent. Reachable in normal use: `kMaxPipeInstances = 3` and the UI takes 2, so
  > any `tools/verify/*` rig dying mid-command hits the UI's scans.
  >
  > ⚠ **Why it stayed untested for months, and the trap in reproducing it.** No ordinary command on
  > this fixture occupies its connection for >200 ms (`list_enums` 0.09 s, `list_all_functions`
  > 0.15 s; `trigger_scan`/`rescan` are async), so the monitor essentially never catches one in
  > flight. The lever is `-DumperTestMaxFPS` + a game-thread `invoke_function`. ⭐ **2 FPS is not
  > enough even though the arithmetic says it is** — the invoke does take ~0.50 s there, over the
  > 200 ms poll, yet the run logged **zero** `client gone mid-command` lines because the kill was
  > seen by the doomed connection's own read/write path first and logged as an ordinary
  > `Client disconnected`. The window must span *several* polls. 1 FPS reproduces every time.
  > The rig therefore treats `observed == 0` as **INCONCLUSIVE**, never a pass — at 2 FPS it
  > produced a clean, plausible, entirely meaningless green.
  >
  > **Owed:** either make the cancel per-connection (what §9.3 actually specified), or — at minimum
  > — stop a cancelled scan from answering `ok: true`. Also `docs/multipipe-eval.md:247-248` still
  > lists this item as remaining, so that doc was right and todo.md was wrong.
  ~~Still open: (6) **watch-event delivery** to the interactive lane (System-tab / address watch still
  pushes correctly while the bulk lane is busy). Verify opportunistically.~~
  > ### ✅ (6) CLOSED 2026-09-06 `[MULTIPIPE-WATCH-2026-09-06]` — delivery is completely unaffected by a saturated bulk lane
  >
  > "Verify opportunistically" had meant *not at all* since 2026-06-28, so it was done deliberately:
  > `tools/verify/multipipe_watch.py`, DumperTest dev, build 3401. **No UI** — the UI's two lanes
  > are just two connections, and the DLL side is per-connection by construction
  > (`Fern::StartWatch`, Fern.cpp:6444, gives each watch its own thread writing
  > `WriteLine(*ptr->owner, …)`), so two raw pipe clients test the actual mechanism and let both
  > sides be counted instead of read off a panel.
  >
  > | watch | idle window (20 s) | busy window (20 s) |
  > |---|---|---|
  > | `TickCount` (the mover, 1 Hz) | **50 pushes** | **50 pushes** |
  > | `FrozenInt` (the control, never written) | 50 pushes | 50 pushes |
  >
  > **Delivery ratio busy/idle = 1.00.** The bulk lane completed **286** `list_all_functions`
  > calls inside that 20 s window, so it was genuinely saturated, not nominally busy.
  >
  > Controls both held: `TickCount` rose monotonically across the busy window (853 → 873, 50
  > samples, duplicates present because the watch pushes faster than 1 Hz), and `FrozenInt`
  > reported exactly **one** distinct value, `424242` — so the watch was reading the address we
  > think it was, and the mover really moved.
  >
  > ⚠ **A suspected cadence defect was measured and DISMISSED.** The counts above are ~2.5/sec
  > against a requested `interval_ms=250`, which looked wrong. Polling at 0.05 s instead of 0.2 s
  > returns **48 events in 12.0 s = 4.00/sec** with DLL-stamped gaps of **median 254 ms** (min 252,
  > max 301). The cadence is correct; the low number was the measuring instrument. The ratio above
  > is unaffected because the poll rate was identical in both windows.
  >
  > ⚠ **The rig's first version HUNG** on a raw blocking `read()` of the same handle it sends
  > requests on, before the busy window ever began — producing a 0-byte output that is
  > indistinguishable from "no events are being delivered", i.e. from the defect it exists to
  > detect. It now polls with a bounded request instead, and the docstring forbids reverting that.
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

- **✅ DONE 2026-09-07 for pipe handlers (`bea9009c`) — narrowing the UNBOUND population is what
  remains** — `[MULTIPIPE-CANCEL-2026-09-07]`
  The measured defect (`6d674989`) is fixed: 5,157-of-5,264 truncated replies → **0 of ~113 across
  three consecutive runs**, each with `client gone mid-command = 1` so the monitor genuinely
  observed the foreign death. `Connection` owns a `cancel` flag, `HandleConnection` binds it to its
  serving thread (`Tot::ConnectionCancelScope`), `MonitorLoop` sets it on the connection that broke.
  ⛔ **The obvious design was WRONG and is documented as rejected in `Tot.h` — do not re-propose it.**
  `return (t_connCancel && t_connCancel->load()) || g_shutdown` makes *unbound* identical to
  *cancel-immune*, inverting the default from **fail-safe to fail-silent**: every thread that does
  not bind silently stops being cancellable, and `t_cancelImmune` goes semantically dead (M4 and B4
  erased without a line of them deleted). So the unbound case deliberately falls back to the global
  flag, and `MonitorLoop` still trips it.
  **Still owed, in rough priority order:**
  1. **Narrow the unbound population — HALF DONE (`257878a3`).**
     ✅ **Aura's parallel workers and its `cancelWatcher` now inherit the caller's context**
     (`Tot::CaptureCancelContext` / `CancelContextScope`). That was the half that mattered most:
     `ScanThreadCount` (`Aura.cpp:129`) picks the parallel path at ≥8192 objects — **every real
     game** — for all seven heavy scans, so until this landed the measured defect was still live
     for exactly the commands worth cancelling, and the `cancelWatcher` was flipping `deadlineHit`
     on *any* client's disconnect.
     ⭐ **Why a raw pointer is safe there and not elsewhere** — the distinction the design review
     did not draw. `ParallelIndexRanges` **joins** its pool (`Aura.cpp:190`) and
     `ParallelGObjectsScan` joins the watcher (`Aura.cpp:258`), so the caller's binding strictly
     outlives both.
     ✅ **VERIFIED END-TO-END ON A REAL GAME 2026-09-07 (`0ee59c97`)** — Octopath Traveler, UE 4.18,
     406,060 objects, victim `begin_value_scan` (→ `ScanForValue` → `ParallelGObjectsScan`). The
     game already carried a **build-3380** proxy from an older session, so this is a true
     before/after on one host, not fixture-vs-game:
     **before 10 of 125 replies truncated to 238 bytes; after 0 of 115, three consecutive runs**,
     with `client gone mid-command` logged exactly once in *both* directions — so the AFTER is a
     real negative, not a missed trigger.
     ⛔ **Still owed, and here the review's use-after-free warning DOES apply:**
     `Fern::RunScan`/`RunRescan` (`Fern.cpp:5276`/`5114`), Frieren's `UE5_AutoStart`, and the CE
     remote thread. Those outlive the command that started them, so binding them to
     `&conn->cancel` would dangle exactly in the disconnect case the binding exists for. They need
     an **owning** handle — a `shared_ptr` to a small cancel object that the `Connection` also
     holds — not a borrowed pointer.
     ⭐ **THE OWNERSHIP QUESTION IS ANSWERED (2026-09-07), AND IT RULES OUT THE OBVIOUS MECHANISM.**
     A scan is a **shared, process-wide product**, not the requester's work: `trigger_scan` is
     gated by `m_scan.running` (`Fern.cpp:5273`), so a second client gets *"Scan already in
     progress"* and then polls `scan_status` — B is genuinely waiting on A's scan. Its results land
     in the global `EnginePointers` and the hint cache that every later client reads. So binding
     `RunScan` to its requester would be **wrong regardless of lifetime**: A leaving would abort a
     scan B is still waiting for. ⛔ Do not give it an owning token to "fix" this — the token is a
     mechanism for a question whose answer is no.
     The right predicate is *"does any client still need this work"*, which is exactly
     `Tot::PerCommandStillOwed` — the thing the review said is **not** subsumed by per-connection
     flags. And it is already wired: `ReevaluatePerCommandCancel` (`Fern.cpp:788`, called at `:970`
     and `:1213`) clears the latch once no raiser is live, so once A's connection is erased, B's
     dependence is respected.
     **What actually remains is a narrow window**, not a missing mechanism: between A's pipe
     breaking and A's connection being erased, `g_perCommand` is set, so a running `RunScan` can
     abort even though B is still connected.
     ⛔ **Judged NOT worth fixing yet, deliberately.** Harm is low — MA1 means a cancelled scan does
     not latch, so the next `trigger_scan` simply re-scans; the cost is one wasted scan. Risk is
     high — the candidate fix ("abort only once the registry has gone empty *after* being
     non-empty") has to stay correct for the CE-only case, where the registry is *always* empty and
     `UE5_AutoStart` scans before the pipe server exists. Get that predicate wrong in one direction
     and the DLL never initialises; wrong in the other and `Fern::Stop` joins a scan that never
     aborts — the "game won't close" freeze `Tot.h` was written for. A one-wasted-scan bug does not
     justify that exposure. Revisit if the window is ever observed to bite.
  2. ~~**`ScanReport::cancelled` is still a process-global non-atomic file static.**~~
     **✅ CHECKED 2026-09-07 — the finding does not apply, and the check found an older bug
     instead (`55be6f9b`).** The review raised this as fatal, but it was written against design v1
     (where every non-connection thread became immune). In the shipped version `cancelled` is
     consumed **locally** — the no-latch branch (`Genau.cpp:2594`) reads it in the same function,
     immediately after the scan that wrote it — so it is not a persisted cross-connection verdict.
     The other three reports are written only from `Genau::FindAll` on unbound scan threads.
     ⭐ What the check *did* find: `FindSparseDelegateStorage` is the one scan reachable from **pipe
     command** threads (`Aura.cpp:3742`, `Aura.cpp:6284`), so two clients could enter its slow path
     before the one-shot latch and both write the same file-static `ScanReport` — which owns a
     `std::vector`. A plain data race, older than the cancel work. Now serialised with a mutex plus
     a latch re-check. ⛔ Not `std::call_once`: MA1 deliberately does not latch a cancelled scan, and
     `call_once` would burn the one-shot on it.
  3. ~~**The CE case is arguably a fix, not a regression.**~~ **⛔ DECIDED 2026-09-07: leave it, and
     do NOT apply the obvious partial fix.** The observation stands — a pipe client's disconnect
     does cancel a CE user's export call (`Aura.cpp:1410` via `UE5_FindObject`/`UE5_FindClass`),
     which is the same class of bug B4 fixed for the Mimic poller and never for the direct exports.
     The tempting fix is `Tot::MarkCancelImmune()` at the top of the CE-facing lookup exports.
     ⛔ **That is unsafe, and specifically so.** `t_cancelImmune` is set-once with no restore, and
     CE runs a whole Lua block on ONE `CreateRemoteThread` thread — `scripts/ue5_dissect.lua` calls
     several exports per block (`callDLL("UE5_FindObject", …)` at :567/:595, `UE5_FindClass` at
     :605). So a block that calls a marked lookup export and *then* `UE5_Init` would run that
     initialisation **cancel-immune**, making the DLL's longest scan unabortable — resurrecting
     failure mode #1 from `Tot.h`'s own header ("game won't close": `Fern::Stop` joins a thread
     that is mid-scan). A partial marking also leaves an inconsistent mental model, which is worse
     than the current uniform one.
     ⭐ The CE path needs the same proper cancellation-context that item 1 needs, not a scattering
     of immunity calls. Fold it into item 1 rather than doing it separately.
  ⛔ `Tot::PerCommandStillOwed` / `m_cancelOwners` / `ReevaluatePerCommandCancel` are **NOT**
  subsumed by per-connection flags and must not be deleted — that predicate is the only expression
  in the tree of *"does any client still need this work"*.
  ⚠ Also unresolved: whether a cancelled reply should flip `ok` to false. More correct, but the UI
  and every `tools/verify` rig branch on `ok`, so it risks trading silent truncation for a spurious
  failure. `truncated: true` (`c4e270e9`) is the additive half.
  ⭐ **The rig reproduces on demand**: `tools/verify/multipipe_cancel_isolation.py`, host at
  `-DumperTestMaxFPS=1`. ⚠ 2 FPS silently measures nothing and 1 FPS is flaky run-to-run — a run
  with `observed == 0` is INCONCLUSIVE, never a pass; re-run until the monitor fires.
  *Parent: multipipe-eval §9.3/§9.6 item 5.*

- **ℹ️ MEASURED AND DELIBERATELY NOT FIXED — `peHash` degenerates when `TimeDateStamp` is 0** —
  `Genau.cpp:54` builds the per-game key as `TimeDateStamp` + `SizeOfImage`. A deterministic /
  reproducible link stamps `TimeDateStamp = 0`, and the key then collapses to `SizeOfImage` alone —
  which is page-granular, so two builds of the same project that differ by under a page would
  **collide and silently reuse each other's cache entry** (cached UE version, GObjects/GNames
  pattern hints). Real instance seen: DumperTest58 Development links with `TimeDateStamp=0`
  (`peHash=000000001424C000`, and its stale sibling `0000000014252000` — only 0x6000 apart).
  ⛔ **Not worth fixing.** Swept every UE game exe on this machine: **1 of 40** has
  `TimeDateStamp == 0`, and it is our own 5.8 fixture. Changing `peHash` would invalidate every
  cached hint and orphan every `Snapshots\` / `Bookmarks\` / `TeleportCoords\` folder keyed by it —
  a real migration cost to fix a 2%-of-one-machine, same-image-size-required hazard. Recorded so
  the next person who notices the collapse does not pay for the change either. Re-measure if a
  future UE default makes deterministic linking common.

- **✅ Tier 2 ceilings DONE 2026-09-07 (`2b9ffac9`); Tier 3 still deferred** —
  The row asked to split `0x100000` into "container-element-COUNT vs PropertiesSize-BYTES". It
  conflated **more than two**, and the axis that matters is not the one proposed. 21 sites
  classified by tracing each value's producing read and consuming arithmetic, then checked
  adversarially: `SANITY_MAX_STRUCT_BYTES` (3) · `SANITY_MAX_CONTAINER_NUM` (4, the +0x08
  field) · `SANITY_MAX_CONTAINER_CAPACITY` (9, the +0x0C field) · `SANITY_MAX_SPARSE_BITS` (1)
  · `SANITY_MAX_UOBJECTS` (4, the `0x800000` family). All in `Grimoire.h` with their reasoning.
  ⭐ **The axis came out of a disagreement.** Two independent traces of the same `+0x08` int32
  reached opposite verdicts — "live COUNT" (UE's `GetMaxIndex()` is literally
  `{ return Data.Num(); }`) versus "slot EXTENT including freed" (this codebase spells the live
  count `MaxIndex - NumFreeIndices` six times, and `ReadTMapHeader` never subtracts it). Both
  hold, so count-vs-index is **not a distinction this code sustains** and gets one name; the
  real split is +0x08 vs +0x0C, where `Max >= Num` always.
  ⚠ Behaviour identical and **checked**, not assumed: every constant equals the literal it
  replaced, and the diff removes exactly 21 lines, all 21 containing that literal.
  **Tier 3 (single-use knobs) remains deferred** — unchanged from below, and still low priority.
  *Superseded row kept below for the Tier-1 history and the Tier-3 list.*

- ~~**Magic-number centralization — Tier 2 remainder + Tier 3 (deferred; low priority)**~~ —
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
  (2) **"Can't find data" / GAS-capture** — the recent snapshot fixes (nested-`StructProperty` GAS capture `Aura::CaptureDirectStructFields`, build 1648; rounded-float matching) flowed into Snapshot/SPC/Group. Pivot reads the **same captured corpus**, so the GAS `Health.BaseValue`-style fields *should* now appear in Pivot automatically — ~~**but this is UNVERIFIED**~~ ✅ **VERIFIED 2026-09-06 `[PIVOTGAS-2026-09-06]`** — see below. Verify in-game that a GAS attribute captured post-1648 actually shows up as a pivotable field/key in Class Pivot; if Pivot has its own field-selection or numeric-only filter that drops nested-struct leaves, fix it. *Parent: rounding-mode switch build 1672; snapshot GAS-capture build 1648 (project-snapshot-nested-struct-gas).*

  > ### ✅ CLOSED 2026-09-06 `[PIVOTGAS-2026-09-06]` — nested-struct leaves reach Pivot, and they are USABLE as keys
  >
  > DumperTest dev, **build 3401**, AOT `dist\UE5DumpUI.exe` 54.7 MB, connected `UE504 (25,215
  > objects)`. Snapshot scope `NumericNoByte`, 652 objects / 12,327 fields; Class Pivot →
  > `DumperTestActor` (2 instances).
  >
  > **The Key field list contains `Health.BaseValue` AND `Health.CurrentValue`**, alongside the
  > flat fields (`F64_Ticking`, `FixedArr`, `FrozenInt`, `I16/I32/I64`, …). So nothing in Pivot's
  > field selection drops nested-struct leaves — no fix needed.
  >
  > ⭐ **Listed is not the same as usable, so the key was exercised, and it DISCRIMINATES:**
  >
  > | key | result |
  > |---|---|
  > | `RayTracingGroupId` (default) | **1 group** from 2 instances |
  > | `Health.CurrentValue` | **2 groups** from 2 instances, keys **100** and **99** |
  >
  > The nested leaf partitions the instances where the flat default does not, so it is genuinely
  > applied rather than merely offered. 100/99 is the CDO against the live actor mid-tick, which is
  > the expected shape for a field that falls 1/sec.
  >
  > ℹ️ Two more nested leaves corroborate that this is general, not special-cased for GAS:
  > `PrimaryActorTick.TickInterval` is a key, and `AttachmentReplication.LocationOffset.X/.Y/.Z` +
  > `.RelativeScale3D.*` + `.RotationOffset.*` all appear as pivotable fields.
  >
  > ⚠ **UI note, not filed as a defect because one witness is not enough:** selecting the class row
  > in the picker needed a **double-click**; single clicks left `Run Pivot` disabled. It selected on
  > a single click earlier in the same session, right after the list was first populated — so the
  > difference may be freshly-populated vs re-populated (the snapshot was changed in between).
  > Worth a second look if anyone sees it again; `[project-class-pivot-field-load-freeze]` is the
  > related trail.

- **Flatten GAS attributes — optional extensions (deferred by user, build 1698)** —
  Effort: **S** · Risk: low. The "Flatten GAS attributes" Options toggle (build 1698) collapses a
  `GameplayAttributeData` StructProperty one level in **Copy CE XML / Copy CE Field** only. Two
  follow-ups the user explicitly scoped out of that change:
  (1) ~~**Export CSX** — apply the same flatten to the CE Structure Dissect (`.csx`) export
  (`CsxExportService.EmitElement`).~~ ⛔ **WON'T DO — maintainer's decision 2026-09-07.** CSX simply
  does not have this feature, does not need it, and **must not even be passed the flag**. The 2026-09-07
  todo audit reported this as a gap ("the Options toggle is a strict no-op for Export CSX"), which
  read the *absence* of a feature as a *missing* feature. It is not: `.csx` is CE's Structure Dissect
  format, a different emitter with a different job, and CSX already flattens every StructProperty by
  its own rules.
  ⚠ Nothing misleads the user here either — `str.Tip.LiveWalker.FlattenGas` already scopes itself in
  its first sentence, "one level in **Copy CE XML / Copy CE Field**", and never mentions CSX. So there
  is no plumbing to add and no wording to fix. Do not re-open this by grepping for
  `flattenGasAttributes` and noticing CsxExportService has no parameter — that absence is the design.
  (2) **Other single-field / wrapper structs** — keep flatten GAS-only "for now"; a general
  "flatten any single-/two-scalar-field struct one level" option would need a careful detection rule
  to avoid surprising collapses ("various cases"). *Parent: Flatten GAS attributes build 1698
  (project-gas-attr-flatten-ce-export).*

- **✅ CLOSED 2026-09-07 — BOTH horns are dead, and the second one had leaked into shipping source** —
  ⛔ Horn 1, the engineering (thin-shim + renamed `dxgi_orig.dll` + 2-file deploy): **never built and
  no longer needed.** The defect was closed a different way — builds 3363 + 3365 (the AppCompat
  pre-CRT crash, then the SRWLOCK self-deadlock from our own re-entrant `LoadLibraryW`) — and
  verified in-game on the exact witness title: Octopath, 2026-08-27 build 3366,
  `dxgi proxy: lazily forwarded 20/20 exports`, pipe server up, **406,060 objects** (a count
  independently re-measured on that title 2026-09-07). `thin-shim` / `dxgi_orig` exist nowhere in the
  tree. The Proxy Deploy 2-file deploy/undeploy/redundancy work was contingent on this path, so it
  dies with it.
  ⛔ Horn 2, *"or leave dxgi as late-load games only"*: **that restriction was still in force in
  SHIPPING C# SOURCE**, stated as present-tense fact, three days out of date and never revisited —
  `ProxyImportAnalyzer.cs` ("it instant-exits under the dxgi proxy", last touched 2026-08-23) and
  `ProxyDeployViewModel.cs` ("Octopath Traveler instant-exits with the dxgi proxy … Pick dxgi only
  for EXEs importing neither version nor dinput8", last touched 2026-08-24). Both now corrected: the
  restriction is lifted, Octopath is a dxgi **witness** rather than a counter-example, and dxgi
  remaining a non-default is recorded as a timing *preference* (version.dll activates at ordinary
  runtime) rather than a capability limit.
  ⚠ The pre-CRT WARN still appears under dxgi and is **expected** — it is the shim engine's
  fingerprint, not a failure. Say so wherever it is reported.
  *Superseded row kept below for the diagnosis trail.*

- ~~**dxgi proxy early-load fragility — harden (thin-shim + renamed real-dxgi copy), or leave dxgi as "late-load games only"**~~ —
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

- **✅ DONE 2026-09-07 (`11748702`) — the "RESOLVED" verdict was right for containers and WRONG for
  static C-arrays** — Guess? was inventing a phantom row over every element but the first of a
  `UPROPERTY Type Foo[N]`, because the gap pass built occupancy from the RENDERED field size while a
  static array renders as ONE element (`WalkInstance` never expands `ArrayDim`). Measured on the
  fixture's `int32 FixedArr[8]`: **7 fake rows before, 0 after**, with 34 legitimate guessed rows
  still emitted elsewhere and none overlapping any of the 152 reflected fields.
  ⚠ **Why the original verdict survived review**: its evidence was a TArray + TMap, and for a
  *dynamic* container `ElementSize` IS the whole inline footprint, so the defect is structurally
  invisible there. Correct for the case examined, wrong for the case not examined.
  ⛔ **And a comment asserted it was fine** — when `ArrayDim` was added, the site gained "so its
  output is unchanged by the new ArrayDim field". It was not unchanged, it was wrong, and that
  sentence is why nobody looked again. The fix was a *deletion*: `Ubel::ComputeClassHoles` already
  had the right formula for the Native-C path, so the duplicate local loop is gone.
  ✅ The bullet's one named deliverable is also done: `docs/tips.md` now has a **"Guess?"** section
  covering both shapes (container internals and static arrays) plus what a guessed row is actually
  worth. Guess? had no user-facing documentation at all beyond one tooltip.
  *Superseded row kept below for the reasoning trail.*

- ~~**Guess? "missing" mid-object data — RESOLVED (working as designed; diagnostic kept).**~~ The
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

- **✅ Native-C Value Scan — P0–P3 SHIPPED and FULLY VERIFIED (SPC arm closed 2026-09-07, `2b8aad04`)** —
  The 2026-09-06 run closed the Class Pivot half and recorded two honest limits; **both are now
  resolved**, and neither needed a game — that run's capture is still on disk
  (`snapshots.6A9C1C8410F23000.db`).
  (1) **SPC Query arm.** Replaying the product's Strict join key over the real pair: **all 8,556**
  `<raw@0x..>` rows join, **none** with a vacuous `prop_offset`, **8** changed across the 77 s gap.
  (2) **"DumperTestActor's own raw rows did not appear in the changed list — not chased."** They do
  — four of them, including `<raw@0x918>` **4684 → 5829**, and 5829 is the exact pivot group key
  that same run recorded. The absence was an artifact of the *Compare snapshots* view, not the data.
  ⚠ Pinned by two tests rather than left as a one-off replay; the second one exists because a
  `<raw@0xNN>` name already encodes its offset, so a broken offset term would be **invisible on
  exactly the rows P3 is about**.
  *Superseded row kept below for the trail.*

- ~~**Native-C Value Scan — P0–P3 ALL SHIPPED on dev; only in-game verify of P3 remains**~~ —
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
  existing `fields` schema, no migration). ~~**REMAINING: in-game verify P3** — BLOCKED on the
  snapshot-perf item below~~ ✅ **P3 VERIFIED 2026-09-06 `[NATIVEC-P3-2026-09-06]`** — the block
  dissolved rather than being cleared: the row said *"Verify on a smaller / faster game"*, and
  **DumperTest is that game**. The FF7-Rebirth perf item was never a prerequisite.

  > ### ✅ P3 CLOSED 2026-09-06 `[NATIVEC-P3-2026-09-06]` — both halves, on DumperTest dev / build 3401
  >
  > AOT `dist\UE5DumpUI.exe` 54.7 MB, `UE504 (25,215 objects)`. Scope `NumericNoByte` with
  > **Native-C (raw) ticked**. ⭐ The toggle's effect is visible before anything else:
  > **652 objects / 12,327 fields → 1,066 objects / 20,883 fields**.
  >
  > A capture pair 77 s apart (20:51:15 → 20:52:32), then *Compare snapshots (diff)*:
  >
  > | field | old | new | |
  > |---|---|---|---|
  > | **`<raw@0x180>`** (BP_ThirdPersonCharacter) | 312.3358 | **388.67** | ⭐ a raw hole, tracked across the pair |
  > | **`<raw@0x4C>`** (CR_Mannequin_BasicFoo) | 312.277 | 388.627 | |
  > | `TickCount` | 312 | 388 | +76 over 77 s — the 1 Hz control |
  > | `F64_Ticking` | 20078.125 | 20097.125 | +19.0 vs 0.25/s × 77 = 19.25 |
  > | `Health.CurrentValue` | 85 | 9 | falls 1/s and wraps, as documented |
  >
  > The reflected controls all agree with the 77 s gap, which is what makes the raw deltas
  > readable rather than merely present.
  >
  > **Class Pivot decodes it, and NOT as hex.** On the Native-C snapshot, `<raw@0x7F8>`,
  > `<raw@0x918>`, `<raw@0x91C>`, `<raw@0x920>` are all selectable pivot **keys**, and pivoting
  > `DumperTestActor` on `<raw@0x918>` gives **`2 groups from 2 instances`** with group keys
  > **`5829`** and `(missing)` — a decimal integer. The grid also renders raw rows with their
  > guessed type resolved (`<raw@0x17E>` → `Int16Property`, `<raw@0x254>` → `IntProperty`).
  >
  > ⚠ **Scope precision, because the row's own note is easy to over-read:** `AppendRawHoleFields`
  > returns immediately unless the numeric scope is a **meta** type (`Aura.cpp:9481`,
  > `Radar::MultiNumericMembers` → only `NumericNoByte` and `NumericAll` expand). The **default
  > `NumericNoByte` already qualifies**; what would capture nothing is narrowing the scope to a
  > single concrete type such as `Int32`.
  >
  > ⚠ **Two honest limits.** (1) The diff used was the Snapshot panel's *Compare snapshots*, not
  > the SPC Query tab — same corpus and same raw rows, but if the row means SPC Query specifically
  > that arm is still owed. (2) DumperTestActor's own raw rows did **not** appear in the *changed*
  > list even though its raw fields are captured and pivotable; the changed raw rows came from the
  > two Blueprint actors. Not chased — it is consistent with those particular holes being static,
  > but it was not proven, and it is the obvious next question.
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

- **✅ DONE 2026-09-07 (`5d1a6bfe`) — it was not "benign", and neither proposed fix was right** —
  The write at `Ubel.cpp` WalkInstance was a **third** writer of the `sizeof(FProperty)` family,
  assigning `FSTRUCTPROP_STRUCT` directly and so **splitting the family** — the exact failure
  audit #5 G12 introduced `ApplyPropertyFamily` to prevent, still reachable by the one writer G12
  did not count (its own note says "Both writers now go through here"). Silent and half-right:
  struct reads stay correct while TArray element descriptors and every enum name read 8 bytes off.
  ⛔ **Both fixes this row proposed were wrong.** "Drop the redundant write" — it is not redundant,
  it probes `{0,±4,±8,±0x10}` where `CorrectSubclassOffsets` probes `{0,±4,±8,±0xC}`, so it is the
  only path that can land a ±0x10 layout. "Make the offsets `std::atomic<int>`" — fixes the
  technical race and leaves the actual bug, since a lone atomic store still splits the family.
  Routing it through the helper under the existing `s_calibrationMutex` fixes both.
  ⭐ Now gated: `tools/check_property_family.py` (16th gate), proven to fail on the reintroduced
  defect. A prose invariant plus a hand-counted writer list is what failed here twice.
  *Superseded row kept below for its reasoning trail.*

- ~~**DynOff calibrated offsets are non-atomic — tighten the second writer (low-risk hardening)**~~ —
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

- **✅ CLOSED 2026-09-07 (`0a7ac3fb`) — the code half shipped in build 2358; the IN-GAME re-check it
  owed is now done** — `[MHPOISON-STARVE-2026-09-07]`
  The row's own acceptance test ran for the first time, against a deliberately starved trampoline
  window (`-DumperTestStarveVM`). It had never been exercised: the only prior attempt is recorded in
  this bullet as "Not reproduced on the build-2361 run" — the hook installed first try, so nothing
  ran.
  ```
  13:52:05.862  MH_CreateHook failed: MH_ERROR_MEMORY_ALLOC
                first-time init complete — offset=616, hook_active=0
                hook install failed (attempt 1/8) … — will retry
                game-thread hook NOT active … Logged once per transition
  13:52:10.893  attempt 2/8      ← 5.03 s apart: the configured cooldown
  …             attempts 3/8 … 6/8
  13:52:36.118  hook RECOVERED on attempt 7 — dispatch available again
  13:52:36.119  hook is ACTIVE again — 61 invoke(s) took the fallback
  ```
  **(a)** six retries, not one → the permanent latch is gone. **(b)** ONE fallback WARN for 61
  fallback invokes → per-invoke logging gone; **nine** WARN lines across two entire runs, against a
  historical **552 unsafe-call lines in 19 seconds**. **(c)** See-through **refused** while the hook
  was down — `seethrough_set` returned `active:false` with `hook_active:false` in the same reply, so
  the UI can say why, instead of hammering the unsafe path. **(d)** recovery observed, which a latch
  could never reach.
  ⛔ **The fixture that made this possible was itself broken and looked fine** — `-DumperTestStarveVM`
  reserved 1 MB at a time stepping 1 MB, and `VirtualAlloc(MEM_RESERVE)` fails for the *whole*
  request on any overlap, so 272 of 4,096 windows failed and left ~960 KB free apiece. It reported
  "reserved 3824 block(s)" (93%, reads as working) while MinHook installed on the first attempt.
  Now it walks the window with `VirtualQuery` and reserves each free run exactly: **2 blocks instead
  of 3,824**, and the count going *down* is the fix.
  *Superseded row kept below for the diagnosis trail.*

- ~~**NEW (Elliot 2026-07-23) — a transient `MH_CreateHook` failure permanently poisons the session
  into the "unsafe direct call" path**~~ — Effort: **S-M** · Risk: **med** (touches the hook path, the
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
  ⚠ *Amended 2026-09-10 by wave A4 (`[TRACKB-A4-2026-09-10]`, refuted A4-5-2).*
  - *That fix is INSUFFICIENT. A top-of-loop flag cannot stop slot 0's own clear in the same
    iteration. The DLL drains the mailbox on its own thread while CE sits in the modal "busy"
    dialog, so after OK the `readInteger(cmd) == 0` re-read at `:266` can pass and clear slot 0
    anyway. The user then reads "markers not cleared" over cleared markers.*
  - *The misreport is in the benign direction, so this stays LOW / UX.*
  - *Safe repair: `if hadError then break end` immediately after BOTH the idle wait and the mailbox
    wait, which leaves the `for` loop with the deferred untick still reachable.*
  - *Never change onBusy to `return`: that strands the momentary record ticked.*

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
