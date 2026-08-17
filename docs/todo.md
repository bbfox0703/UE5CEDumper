# Todo

Open work only. **Read this when deciding what to do next.**

> ## ▶ If the ask is "carry on fixing bugs", do NOT start here
>
> The bug queue is **not** in this file. It lives in
> [audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md) →
> **§3b "▶ THE NEXT FIX SESSION STARTS HERE"**, which carries an ordered, already-vetted list of
> the next six fix groups (① – ⑥) with file:line and the reason each group is one job. Start at ①;
> no re-derivation is needed to begin.
>
> **What IS in this file, and is not in that one:**
> - `## Pending live-game verification` — **31 batches** needing a running game. **Offer these
>   whenever the maintainer has a game up.** The five newest are 2026-08-17's and NONE has been seen
>   on a real target; two of those need less than a full session:
>   **AA4–AA7 step 2 needs no DLL at all** (enable the dissect auto-callback with the DLL absent and
>   confirm CE still dissects an ordinary address), and **all six AE4–AE7 steps need no game** —
>   just the Proxy Deploy panel.
> - Everything below that is ordinary feature/infra work, unrelated to the audit.
>
> State as of 2026-08-17: **196 audit findings open of 277 · 0 HIGH · 37 MED**. Nothing is
> blocked on a maintainer decision any more (A6 was the last, and it is decided and shipped).
> Re-derive the count with `py tools/check_audit_register.py --list` — never hand-tally.

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
  Effort: **M-L** · Risk: med (loader-time code + deploy flow). **Deferred by owner (2026-06-19); Octopath uses version.dll for now, and the UI default is back to version.dll.** The dxgi proxy instant-exits on games that call dxgi **extremely early — under the loader lock, before our CRT is initialised** (Octopath Traveler: debugger-confirmed across 3 distinct crash dumps — execute-0 / `__tzset` uninit CRT lock / `RtlAllocateHeap` null heap; see dev-log 2026-06-19). Two genuine early-load fixes shipped + kept (`Sein::GetTimestamp`→Win32 `GetLocalTime`; dxgi lazy self-resolving thunks), but they do NOT make Octopath's dxgi work — the **root blocker** is that `LoadLibraryW(real same-named System32\dxgi.dll)` returns NULL under the early loader lock. **version.dll dodges it all by being called at normal runtime, not under early loader lock.** Robust fix = **thin-shim split (like RE-UE4SS):** `dxgi.dll` becomes a tiny CRT-free forwarder that (a) loads the real dxgi via a **renamed copy** (`dxgi_orig.dll`) to dodge the same-base-name-under-lock failure, and (b) `LoadLibrary("UE5Dumper.dll")` to run the heavy dumper as a **separate, normally-named, late-loaded** DLL. Deploy becomes **2 files** (`dxgi.dll` + `UE5Dumper.dll`) → the Proxy Deploy panel's deploy/undeploy/redundancy/Update-All must copy/remove both. NOTE: `/MD` (dynamic VCRuntime/UCRT) alone is only a **partial** fix — it removes the CRT-init crashes (Octopath already loads the shared UCRT early) but NOT the loader-lock same-name `LoadLibrary` blocker (that resurfaces as execute-0). version.dll/dinput8.dll don't need any of this (they load late). *Parent: dxgi proxy build 1172; early-load diagnosis + 2 fixes build 1351 (dev-log 2026-06-19).*

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
already a multiple of 8, so nothing discriminates. It also cannot reach M2/A2: the containers hold
**3 entries each** and nothing is ever removed, while A2 needs **>128** entries (the `TBitArray` heap
spill) and M2 needs a removal.

`FDumperTestStat` does not help either: it carries an `FText` (a `TSharedRef`, so 8-aligned), which
is exactly the case the size guess gets *right*.

**Add these five properties** (the arithmetic each one is chosen to expose):

- `TMap<int64,int32> Map_I64ToI32;` — pairAlign 8, unpadded 12 → **old 20 vs new 24**. The core M1
  witness. `int64` key rather than `UObject*` so there is no lifetime/GC variable in the test.
- `TMap<FString,int32> Map_StrToInt;` — unpadded 20 → **old 28 vs new 32**. A second M1 witness with
  different arithmetic, so one wrong assumption cannot pass both.
- A deliberately **4-aligned POD** struct (`USTRUCT FDumperTestVec3f { float X, Y, Z; }` — no FText,
  no pointer, no double) + `TMap<int32,FDumperTestVec3f> Map_IntToVec3f;` — **M3**: the size guess
  says "≥8 ⇒ align 8" and puts the value at +8 where it really sits at **+4**, so *even element 0* is
  wrong. This is the only shape that exercises the `UScriptStruct::MinAlignment` read. It doubles as
  **A4**'s target (a scalar leaf inside a map's struct side).
- `TSet<int32> Set_Big;` populated with **200** entries, then `Remove()` of a **low** index (< 128)
  at BeginPlay — **A2** (post-spill stale inline bits: the freed low slot must not appear) and **M2**
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

### ✅ VERIFIED 2026-08-17 `[AA38-PYTHON-2026-08-17]` — AA38: a GWorld must not be reported on a process with no object pool (build 3245)

**Steps 1, 2, 3 and 5 PASS. Step 4 NOT TESTED** (no modular-build title was scanned).

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

### ⬜ NEW 2026-08-17 — AD4: the God Mode badge must now name WHY, not just on/off (build 3203)

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

### ⬜ NEW 2026-08-17 — AC1: Force Overwrite must no longer be able to destroy a foreign DLL (build 3191)

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
> **Steps 1/2/3/4/6/7 still need the foreign DLL.** Prefer §4.1's **synthetic folder** over Light
> Maze; the same synthetic folder also unblocks **AE4 step 4**, which has no leftover to delete
> today. ⚠ Note for whoever stages it: copying a system DLL to `dxgi.dll` inside a game folder is
> the textbook DLL-hijack shape, so on this machine (Bitdefender ATD, working-lessons §3.8) prefer a
> benign third-party DLL that merely lacks our `ProductName`, and do it while someone can answer an
> AV prompt.

### 🔲 U3 + U17 — struct previews: dropped members, then wrong widths (builds 3169, 3171)

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

### ⬜ NEW 2026-08-17 — SkiaSharp/HarfBuzzSharp ABI alignment: the UI must stop crashing

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
2. **G10 — the count no longer lies.** In `scan-0.log`, a `Hint MISS` line now reports the real match
   count (`(%zu matches, none validated; …)`). It must never say `1 match` for a pattern the cold run
   logged with hundreds — that mismatch is what hid this defect for months.
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
4. **MA1 — the cancel actually fires.** On a cold, hint-less title, untick the CE script ~2 s into
   the scan. `scan-0.log` must show `AOB scan CANCELLED after N/M batches` within ~1 s, and
   `FindAll: scan was CANCELLED — NOT writing the hint cache`.
5. **⚠ MA1 — the guards, each checked SEPARATELY** (a control that passes is how a bug in a fix gets
   found): after that cancelled run, (a) **diff `UE5CEDumper.{Machine}.json`** — it must be
   *unchanged* for that PE hash; (b) re-enable in the **same** process and confirm a full re-scan
   runs rather than short-circuiting (the `UE5_Init` latch guard); (c) drill into a
   `MulticastSparseDelegateProperty` and confirm `FindSparseDelegateStorage: Scanning` appears a
   **second** time rather than a latched 0 (the sparse latch guard).
6. **⚠ REGRESSION — a healthy scan still completes and still saves.** Connect the UI, disconnect it
   mid-command, reconnect, and confirm a fresh scan resolves normally, writes the hint cache, and
   shows **no** `CANCELLED` line. This is what keeps `bScanCancelled` from being widened to
   `Tot::Requested()`, which would refuse the latch on a scan that finished fine.

**Not covered:** `Macht` still carries no poll (deliberate — see the comment above its AOB
declarations), and **MA2**, the `ScanRegionBatch` per-pattern underflow, is unreachable until
`AOBScanBatch` is given a `moduleBase`.

### ⬜ NEW 2026-08-17 — G2: the version sweep is ~29 s faster, and must still be RIGHT

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

   Scaling DQ7R's needle-only cost onto Elliot's image brackets the needle at **~1.7–1.9 s of the
   2.400 s** (1.9 s if the two flavours are one interleaved pass, 1.7 s if they are two separate
   passes — the bracket exists because that detail was not established, and the conclusion is the same
   either way). So `CountPreUE4Markers` accounts for only ~0.5 s, and **the lead's hypothesis is
   refuted: the window is dominated by the version needle, not by the ungated marker sweep.**

   **What that leaves is the lead's own alternative, and it holds.** DQ7R's 0.316 s over a ~99 MiB
   image is essentially the dev-log's 0.35 s figure, so that number was measured on a ~100 MB-class
   image and is not a per-title guarantee. Elliot is 4.7× the bytes and cost 4.7× the time — linear,
   i.e. exactly what a rewritten single-pass sweep should look like. **Step 2 is therefore a PASS
   against the mechanism and a correction to the batch's own prediction:** "sub-second" is true at
   ~100 MB and cannot be true at 460 MB. Do not file "G2 is slower than claimed".

   ⚠ **Conditions, because a rate without them is not a measurement:** one title per column, single
   run each, warm page cache, different builds (3122 vs 3262), and the Elliot row is quoted from
   `[ELLIOT-2026-08-16]` rather than re-measured. The two remaining Tier-1 titles (DQ I&II 87 MB,
   OCTOPATH 43 MB) would turn the one-point rate into a three-point slope and are the cheap way to
   harden this — each is a launch plus a ~2 s scan.
3. **⚠ REGRESSION — a Tier 1 game still detects from Tier 1.** Any ordinary UE5 title: confirm
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

**Not covered by this batch:** version detection is still uncancellable (by design — see the block
comment in `DetectVersionDetailed`), and **MA1** — `Macht.cpp`'s AOB family has zero cancellation, so
once a scan enters `AOBScanAllModules` every poll added here is unreachable.

### ⬜ NEW 2026-08-17 — AE2 / AE3: the Class/Struct panel under fast selection

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

### ⬜ NEW 2026-08-17 — U4 / U16 / U6 / F3: the three never-erased caches in `Ubel`

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

**Still open after this batch, deliberately** — do not read a pass here as closing them:
**U5** (nothing is freed; eviction is illegal while `WalkClassEx` returns a reference),
class-to-class recycling (a recycled address whose new occupant has a *sane* `PropertiesSize`),
**A10** (`Aura`'s two reference-returning caches), and names baked into `ClassInfo::Name` /
`FullPath` / `SuperName`, which are never witnessed.

### ⬜ NEW 2026-08-17 — AA14–AA20: the CE Lua invoke path in a real game

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

### ⬜ NEW 2026-08-17 — AE4–AE7: the Proxy Deploy panel, two buttons at once

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
> | 4 | ⬜ **NOT TESTED** | There is no leftover to delete — `No leftover proxy DLLs found (30 folder(s) examined)`. Needs a **synthetic** leftover folder (a `…\Binaries\Win64\` holding only our proxy). Nothing about the gate can be claimed until one exists |
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

### 🟡 4-of-5 CLOSED 2026-08-16 — AA4–AA7: ue5_dissect.lua in a real Cheat Engine

*Needs CE + a game, and **step 2 needs no DLL at all** — it is the fastest check here. See dev-log
build 3037. The Lua rig (`lua scripts/tests/dissect_test.lua`, 40 checks) covers the logic against
stubs; what it cannot cover is CE's real dissect machinery.*

> **Steps 1, 2, 3, 5 ✅ PASS. Step 4 not staged (see it below).** Two sessions, deliberately split:
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
4. **A mid-walk failure leaves nothing behind.** Harder to stage: build a structure, then close the
   game and re-run `createFromClass` on the same class. It must fail cleanly, and the CE structure
   list must not gain a half-built or empty entry.

### ⬜ NEW 2026-08-17 — A6: Force now holds the class AND its subclasses

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

### ⬜ NEW 2026-08-17 — AB3/AB5: the vector scan on a UE5 (LWC) game

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

### ⬜ NEW 2026-08-16 — the fourteen-MED batch, all UI-visible (builds 3016-3031)

None of this session's twelve fixes has been seen on a running game. They are cheap to check because
each has a *visible* pass/fail, and four of them only ever show up when something ELSE goes wrong.

**Free from any ordinary session (just look):**

1. **A5 — Preview shows a LIVE value.** Property Search a field you can change in-game (Health).
   The Preview column must track the real value, not the Blueprint default. A row whose class has no
   live instance must read `… (CDO default)` — the marker is the fix's honesty half, so confirm both.
2. **V6 — the search highlight survives a Refresh.** Live Walker → type a field-search keyword →
   press Refresh (and leave auto-refresh on for a few ticks). Highlights must stay, the ↑/↓ stepper
   must still land on highlighted rows, and **the grid must not jump to the top** — that last one is
   what the fix deliberately avoided by not re-using `ApplySearch`.
3. **AE9 — New Scan resets the Sort picker.** Value Search → First Scan → sort by Value → New Scan.
   The picker must read *"Scan order"*, and picking *"Value"* again must actually re-sort.
4. **U8 — `FName::Number` is back.** Live Walker a `NameProperty` whose value has a numeric suffix
   (`Slot_1`, `Slot_2`). Panel and Value Search must agree on the same 8 bytes. ⚠ Object/instance
   NAMES are a separate, unfixed lead — do not read a truncated instance name as a failure here.

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

### ⬜ NEW 2026-08-15 — install the plugin into a REAL Cheat Engine (audit #5 AB1, build 2913)

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

### ⬜ NEW 2026-08-15 — freeze a PACKED bitfield bool and check its 7 siblings survive (audit #5 AA1, build 2922)

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

### ⬜ NEW 2026-08-15 — freeze a 1-byte enum and check its neighbours survive (audit #5 Y15, build 2904)

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

### ⬜ NEW 2026-08-15 — freeze a byte-wide property and try to overflow it (audit #5 Y9, build 2895)

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

### ⬜ NEW 2026-08-15 — AA(B) / FIRE on a class past the 5,000-row cap (audit #5 X2, build 2888)

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

### ⬜ NEW 2026-08-15 — run a generated CE invoke against a live game (audit #5 Y1, build 2862)

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

### 🟡 PARTIAL 2026-08-17 `[W23-PIPE-2026-08-17]` — SDK header layout: inherited-property boundary + packed bitfields (audit #5 W2/W3, build 2842)

**The headless half — the boundary value itself — PASSES all three checks. The UI half is NOT run**
(see below). DumperTest Development, build **1.0.0.3262**, via `tools/verify/pipe_client.py`.

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

⚠ **The UI half is NOT verified and this must not be read as covering it.** Exporting the SDK header
and checking the struct opens at the super's size, declares none of the base's properties, and emits
`uint8_t bX : 1` runs whose byte count matches the gap — all of that needs UE5DumpUI, which
**cannot currently be granted to computer-use** (it is a loose exe with no installer registration;
see `docs/auto-verification-session-plan.md` §1). The emitters are unit-covered; what stays unproven
is the emitters running against this live boundary value.

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

### 🔴 NEW 2026-08-14 — TMap element geometry: pair padding + struct alignment + free-slot count (audit #5 M1/M2/M3)

Shipped as the first fix batch of [audit #5](audit-2026-08-13-early-code-findings.md) cluster ①.

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
> | **M1** | ✅ | `Map_I64ToI32` all three elements correct (`600000000001..3` → `6001..3`). A stride of 20 makes elements 1–2 read from the previous element's tail; they are exact, so the stride is 24. |
> | **M1** (2nd witness) | ✅ | `Map_StrToInt` `map_value_offset=16`, values `6101/6102/6103`. Different arithmetic from M1's first witness, so one wrong assumption cannot satisfy both. |
> | **M3** | ✅ | `Map_IntToVec3f` reports **`map_value_offset: 4`**. The old size guess yields **8**. This is `Ubel::GetStructAlignment` reading `MinAlignment=4` off a live `UScriptStruct`. Raw hex `00C8C145 00D0C145 00D8C145` decodes to 6201.0/6202.0/6203.0 — all three floats at the right offsets. |
> | **M2** | ✅ | `Set_Big` `set_count=199` (200 added, 1 removed). Before the fix `NumFreeIndices` always read 0, so this reported **200**. |
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
> **Incidental — D1/U3 CONFIRMED LIVE as still broken (not yet fixed).** `Map_IntToVec3f` renders as
> `f:[6203.0000]`: one float, the **last** one. The raw hex holds all three correct values, so the
> loss is in `InterpretValue`'s 8-byte "vtable preamble" skip — 12-byte struct − 8 = one float. U3
> moves from inferred to observed.

The remaining unchecked boxes below are superseded by the table above except where noted; U2 and the
`TSet`/`UDataTable` no-regression check still stand.

- ⬜ **A `TMap<K,V>` whose pair needs trailing padding reads correctly (M1).** Live Walker → expand
  any `TMap<UObject*, float>` / `TMap<FString, int32>` / `TMap<AActor*, uint8>`. **Before the fix
  every element past index 0 was wrong** (stride 20 vs the engine's 24). Confirm element 1..N show
  plausible keys and values, and that no key repeats in a way that looks like a shifted window.
- ⬜ **A struct-valued `TMap` reads correctly at element 0 (M3).** Expand a
  `TMap<int32, FVector>`-shaped field (or any `TMap<K, FStruct>` whose struct is 4-aligned).
  **Before the fix even element 0 was wrong** — the value was read at +8 where it really sits at +4.
  This is the check that actually exercises the `MinAlignment` read.
- ⬜ **Element count matches the rows rendered (M2).** Find a `TMap`/`TSet` that has had entries
  removed during play (an inventory after dropping an item). The header count and the number of rows
  must now agree — previously `NumFreeIndices` always read 0, so the count was inflated.
- ⬜ **No regression on `TSet<T>` or `UDataTable`.** `TSet` geometry is unchanged by design
  (`elemAlign` defaults to 4). Expand a `TSet<FName>` / `TSet<UObject*>` and open any DataTable to
  confirm rows still resolve.
- ⬜ **A container that outgrew 128 slots still lists the right elements (A2).** Find a `TMap`/`TSet`
  with **more than 128** entries, then remove one in-game. Before the fix, indices 0..127 were judged
  from the **frozen inline bit words** the TBitArray left behind when it spilled to the heap, so a
  freed low slot still read as allocated and the walker showed a dead element. Also worth a
  Find Refs / Value Search pass on such an object — the same stale bits admitted phantom hits there.
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
  | `Map_I64ToI32` | 8 / 4 | 8 | **24** | 20 — the core M1 witness |
  | `Map_StrToInt` | 16 / 4 | 16 | **32** | 28 — second witness, different arithmetic |
  | `Map_IntToVec3f` | 4 / 12 | **4** | 24 | value at +8; the only one wrong at element 0 (M3) |
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

- ⬜ **Dissect still builds a structure.** Run `ue5_dissect.lua` against any class with a decent
  field count and confirm the CE structure comes out the same as before. The happy path should be
  untouched — `executeCodeEx` returns the RAX either way — but this is the one shipped script whose
  call helper changed, and a class walk runs it **once per field**, so a mistake here is not subtle.
- ⬜ **No stray warnings on a healthy run.** A clean dissect must print **zero**
  `[UE5Dissect WARN] <name> failed: …` lines. `warn()` is ungated, so any appearing means a call is
  failing that previously failed *silently* — new information, not a new bug.
- ⬜ **`.CT` disable still tears down.** Untick the inject record and confirm `UE5_Shutdown` runs
  (`ue5_callDLL` is the changed path). A regression here reports a clean teardown that never
  happened — the audit #4 B1 symptom.

**Needs deliberate action:**

- ⬜ **Is 5000 ms enough for the slowest real call?** The candidate is `UE5_FindObject`, which scans
  GObjects — dissect a class on a **large-pool** title (Elliot / DragonSword, 250 K+ objects) and
  confirm no `Execution timeout`. If it does time out, the fix is **not** simply a bigger number:
  every timeout permanently leaks the stub + result + string allocations in the *target* process
  (§13.4), so a value that fires regularly has its own cost. Reconsider the call, not the constant.
- ⬜ **Negative control — does `why` actually surface?** The reason capture is the whole point of
  the change and a healthy session never exercises it. Cheapest induction: attach CE, suspend the
  game process, then trigger one dissect call. Expect
  `[UE5Dissect WARN] <name> failed: Execution timeout` — **not** a bare `nil`, and not the old
  guessed wording. Before build 2792 this froze CE permanently instead, so this check doubles as the
  proof that the infinite-timeout fix took.

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
> ⚠ **Still not directly observed: the Object Tree header ratio.** It is closed above by *inference*
> from D2's group scan, and the on-screen string cannot be substituted by the `ui-view` log line —
> `ObjectTreeViewModel` computes the header from a different denominator than the log. Left as-is
> rather than re-opened, but it has never been read off the screen.


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
> ⬜ **What is still unverified: the `graceful=true` path**, i.e. a CE Disable / `UE5_StopPipeServer`.
> It is unchanged by construction (the fix is an early return in front of it) but it was not exercised
> — the headless route cannot drive CE. **Next CE session, one grep:** untick the record with the UI
> connected → `grep "Stop entry" pipe-0.log`. **PASS** = the line does **not** say `process exit`, the
> drain reports `satisfied`, and `Stopped` follows. **FAIL** = `process exit` on a CE Disable, which
> would mean the destructor is racing the explicit call.
>
> ⬜ does **not** mean "probably fine". It means nobody has looked. Most of the fourteen were
> simply not exercised (no wrapper installed, no UI killed mid-command, no Extra Scan).

#### ① Log-derivable

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

- ⬜ **CE mailbox survives a dead UI client** (build 2592, B4). The evidence line is **cold** — once
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

#### ② Manual-only

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

- ⬜ **The pre-4.11 refusal no longer fires on one PE field** (build 2621, B25). Provoke it with the
  UE-version override, or with any game whose PE ProductVersion reports a 4.0–4.10 major/minor.
  Grep `scan-0.log` for `below the … floor — NOT accepting that on its own`. **PASS** = that line
  appears and the scan **runs anyway** (tier 3 → low confidence → the gate does not arm). **FAIL** =
  `SKIPPING the scan` on a game that works. Also confirm the *other* direction still works: a
  genuinely pre-UE4 (UE3) binary must still be refused, via the marker path — grep
  `PRE-UE4 engine POSITIVELY identified`.

- ⬜ **Duplicate GameEngine records no longer break each other** (build 2621, B26). Teleport →
  Global Pointers → *Get GameEngine*, then click it again. **PASS** = the second click says it was
  *already pushed this session* and copies XML instead of adding a record. Then paste that XML to
  deliberately create a second record, tick BOTH, and untick the OLDER one. **PASS** = the newer
  record's `UE_GameEngine` still resolves and its chain still reads (set `UE5_DEBUG=1` to see
  *"another record owns UE_GameEngine now — leaving it alone"*). **FAIL** = the newer record's
  addresses go to `??`.

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

- ⬜ **Provoke the concurrent `UE5_Init`** (build 2592, B5) — the active half of the passive check in
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

- ⬜ **`.CT` DLL discovery — the `reg.exe` recent-files fallback** (build 2576). The breadcrumb half
  is **✅ verified** (run `UE5DumpUI.exe` once, open the `.CT` from CE's recent-files menu, tick
  `init` → the DLL resolves). The registry half has NOT been exercised: it only runs when every
  cheap slot misses. **To test:** delete `%LOCALAPPDATA%\UE5CEDumper\dll-path.txt`, open the `.CT`
  from recent files, tick `init`. PASS = a brief console flash, the DLL resolves, the slot report
  (set `UE5_DEBUG=1`) credits *"folder of the most recent UE5CEDumper.CT in CE's recent-files
  list"*, **and `dll-path.txt` is recreated** so a second tick does not flash again. FAIL = still
  not found, or it flashes every time (the self-heal write did not happen).
  *Why it can't be tested here: it is CE Lua, and `CtDllDiscoveryTests` can only pin structure.*

- **Flaky: `SnapshotViewModelTests.GroupMatch_MissingValue_ShowsErrorNoCandidates`** — failed ONCE
  in a full parallel run on 2026-07-23 (build 2318), then passed 25/25 three times in isolation and
  green on an immediate full re-run. Unrelated to the winmm/proxy work that was in flight. This test
  class has prior form for snapshot-DB concurrency flakes (see `feedback-ci-only-test-flakes`, and
  PR #451's concurrent-first-open fix), so the likeliest cause is another store-level race under
  parallel load rather than the assertion itself. **Not chased** — one observation is not a
  reproduction. If it recurs, capture whether `GroupCandidates` was non-empty or `GroupStatusText`
  empty, since those point at different halves. Effort **S** once reproducible.


Shipped + unit-tests-pass but unproven on real games:

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
  Passive — needs no special in-game action, just one injection each side. ⬜ unverified.

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
