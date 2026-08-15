# Working lessons — the notes that used to live only in agent memory

> **Why this file exists.** Development happens on **two PCs**, and the assistant's memory files live
> under `%USERPROFILE%\.claude\` — they are **not** in git and do **not** travel with a `git pull`.
> Every lesson below was paid for with a debugging session, and until now the other machine had no
> way to know it. This doc is the shared copy.
>
> **Sync rule (changed 2026-08-15): this file is now the SOLE copy — it is no longer mirrored.**
> The assistant's memory folder used to carry a near-identical duplicate of every section below, and
> the "edit both" tax was paid unevenly: the copies drifted, and the folder does not travel with git
> anyway. Those 15 memory files were deleted; `MEMORY.md` now carries a pointer to this file plus the
> section map. **Write new working-lessons here, not into memory.** Memory keeps only what is
> genuinely machine-local (paths, in-flight project state, session preferences).
>
> Every claim here was true at the build named beside it, and code moves — re-verify against the code
> before acting on a `file:line`.
>
> **What belongs here vs elsewhere:** this file is *how to work* — verification method, traps in our
> own stack, and decisions settled in conversation that leave no trace in the code.
> [lessons-learned.md](lessons-learned.md) is *what the games do* (cross-game UE debugging).
> [dev-log.md](dev-log.md) is *what shipped when*. Do not duplicate those here.

-----

## 1. Verification method

These are the rules that keep a green result from meaning nothing. Every one of them caught a defect
that clean builds, green tests and plausible-looking numbers had all missed.

### 1.1 A verification must first be shown capable of SEEING the change

A clean result from a harness that structurally cannot observe the code path reads as "no regression"
when it means "not measured". Concrete case: `tools/ghidra/scan_patterns.java` skips every `Symbol*` /
`CallFollow` signature and says so out loud, so `sweep.sh` provably cannot verify changes to
`Genau::ScanFunctionBodyForRipRef`.

**Ask "what would this run look like if the change were broken?" before quoting it as evidence.**

The same trap in its most common local form: **`build.ps1 -Target Test` does not compile any `.cpp`.**
It builds two header-only test executables, so a syntax error in `Fern.cpp` passes it clean. A green
`-Target Test` after editing a `.cpp` measures nothing about that file.

### 1.2 Prove the assertion FAILS when it should — negative controls

When `extract_patterns.py --check` was added (build 2530) the work did not stop at "it passes":
`Himmel.h` was mutated three ways (count wrong, intro total wrong, header reworded so the regex
misses) and exit 1 was confirmed on all three. **The third matters most — a check that silently
degrades to a no-op when its input is reworded is worse than no check.**

**Run the control even when it is "obviously" going to pass.** On 2026-08-12 the B13/B41
leftover-proxy test predicted "no row appears when the volume has no Recycle Bin", and the first run
produced exactly that. It looked like a clean confirmation. The control — flip the bin back ON,
change nothing else, expect the row to appear — **also returned 0 rows**, which is what revealed the
first run had measured *nothing* (see §2.1). The prediction was right and the measurement was
worthless, and only the control could tell those apart.

**Why:** a test whose PASS criterion is *absence* ("no row appears", "no warning fires", "nothing is
logged") is satisfied by every broken rig in existence. Absence is the cheapest thing in the universe
to produce by accident.

**How to apply:** whenever the pass criterion is that something does NOT happen, pair it with a run
where the same machinery MUST produce the thing, differing in one variable — ideally one you can flip
without restarting anything. Corollary that also cost a session: **an "examined N items" counter is
the cheap way to tell "looked and found nothing" from "never looked"** (22 → 23 was the number that
proved the folder had finally entered the candidate list). Ask for that number before trusting a null
result.

### 1.3 Green tests do not cover the SEAM

AOBMaker's `PreferNearOriginalCandidates` was a dead no-op (an RVA compared to a VA) under three green
tests, because they called the scorer directly with both address arguments *in the same units*.
Nothing exercised where the engine supplies them. We have the same shape: `SymbolExportServiceTests`
hand-builds `SymbolEntry` with a literal address, so it proves the generator reads the field and never
that the value was computed right.

**Corollary: when the outcome is already saturated, assert the mechanism, not the outcome.** A ranking
assertion on an item already ranked #1 passes before and after and proves nothing.

**Corollary: before writing the test, ask whether the CALL SITE can fail it.** Audit #5 Y15 (build
2904) plumbed an engine-reported width into a mapping. The mapping was easy to cover. The two places
that *used* it were not reachable from a test at all — `FreezeValueDialog`'s helper-type choice sat
in an Avalonia constructor, and `PropertySearchViewModel`'s equivalent inside a command needing the
AOBMaker bridge and a modal dialog — so the width could have been dropped at either with **zero**
failures, and a negative control aimed there would have reported "0 red" as if the code were fine.
Two `internal static` seams later, those controls red 3 and 3. A control that cannot fail is
indistinguishable from a passing one; **an untestable call site turns §1.2 off silently.** When a
value has to survive several hops, assert the two ENDS against each other (there: the type the
dialog validates against == the type the generated script writes with) — that single assertion fails
no matter which hop drops it.

### 1.4 Measure with two independent detectors, or you are measuring the detector

AOBMaker's vtable-slot numbers swung up to **14×** across three detection variants on the same
binaries; the middle variant moved every number in the expected direction and would have shipped as a
success. Related: never quote an accuracy figure without its conditioning variable (their "UE is 18
points harder than non-UE" was a *function-size* artifact and was retracted).

### 1.5 Only a machine-checked number survives

On 2026-08-01 the AOB count existed in five places: `Himmel.h`'s header (CI-adjacent, **correct**) and
four hand-copied prose sites — CLAUDE.md, roadmap.md, Features.md, dll-spec.md, architecture.md —
**each wrong, each differently** (128 / 128 / 150 / "2 symbol exports"). Fixed in build 2530 plus a
`--check` in `ci.yml`. **Derived prose needs a regeneration command sitting next to it, not good
intentions.** This is why CI now runs `check_derived_counts.py`.

### 1.6 A number recorded without its conditions is not a measurement

This hit three times in one day (2026-08-01), and only the first was obvious:

- a corpus **path** written into docs as if it were the repo's, when it was one machine's;
- a **sweep duration** (4m38s) that turned out to be a different computer than the 872 s
  re-measurement — 3.1× apart, **both correct**, and a round was wasted wrongly calling them "in
  dispute". Nobody catches this one, because a duration does not *look* machine-specific the way `E:\`
  does;
- a **verdict**: `CLEAR` in the AOB specificity index means "this window is absent from the index",
  i.e. *never observed* — not "measured to be rare". So a HIGHER clear count means a WORSE-covered
  index. (Reductio: an x86-only index certifies 109 x64 patterns "CLEAR".)

Before recording any number, ask what would have to be true for it to be reproduced, and write that
next to it: machine, corpus version, flags, what the metric is conditioned on.

**An ABSENCE has conditions too, and that is the version that gets past you.** On 2026-08-01 `X:` was
checked from the laptop, found missing, and "the corpus is single-copy / `X:\Ghidra_Projs_Backup`
never produced a file" was published into three documents. The backup is on the OTHER machine. A
missing drive letter *feels* like a fact about the world in a way that `4m38s` does not, which is
exactly why it slipped. **Never assert that something does not exist without naming the host you
looked on.** ← this rule is the same two-machine problem that makes this whole file necessary.

### 1.7 The second machine turns an anecdote into a fact — and it cuts both ways

"This is probably machine-specific" is also a hypothesis, and re-measuring settles it in one run. CE's
`sleep(1) = 15.47 ms` was re-probed on a 9955HX3D laptop against the 9950X3D desktop and matched to
**three decimals** — refuting the per-PC-performance explanation and upgrading "our timeout might be
long here" to "**every user** had a ~155 s timeout" (§3.2).

### 1.8 A reported defect is a hypothesis until you reproduce it — including one from a subagent

A review agent reported that `reimport_verify.py` would fail OPEN on a truncated baseline
(`a.get(k) == b.get(k)` compares `None` to `None` and passes). It reads as obviously right.
Reconstructing the pre-guard code and running it showed the empty baseline **already failed** on the
input and symbol legs, because the rebuild side has real values to compare against. The guard was
still worth adding — for the error message — but crediting it with fixing a fail-open would have put a
false claim in the commit log. **Fixes get the same burden of proof as findings.**

### 1.9 A test that pins the ABSENCE of a string will match the fix's own documentation

A new `DoesNotContain("process alive?")` assertion failed on first run — against the *comment*
explaining why that string had been removed. Scan code lines only. Same family: an ordering assertion
matching the substring `"write"` hit the word inside the scripts' header comments and failed on two
generators whose ordering was correct — a cheap proxy standing in for the predicate that mattered (a
write *call*), committed inside a test written to catch exactly that class of error.

### 1.10 Two more that keep recurring

- **Abstain rather than emit a wrong-unit value.** Feeding a wrong-unit value is silent failure;
  declining to score is honest.
- **A correctness fix may legitimately make the headline number worse.** Removing confidently-wrong
  answers can lower agreement while improving the code.
- **Predict the magnitude, then measure it, and correct the prediction in writing.** Two own-goals
  kept because the shape recurs: a `cores/4 ÷ SWEEP_XMX` concurrency formula that yielded a value
  *forbidding the shipped default* (budgeting on `-Xmx`, a reservation ceiling, not a working set);
  and a prediction that a bug would "collapse concurrency to 1" when it measured at **+42%**.

### 1.11 The recurring-defect sweep is not "grep the symbol" — it is "grep the argument nobody used"

Audit #5's most expensive family was `EnumProperty` being written as 4 bytes when UE's dominant
`enum class E : uint8` is one. It cost **four findings across seven sites in four subsystems**: W6
(CE XML export), Y2 (FIRE param buffer), Y15 (freeze/force), Y16 (interactive CE invoke form, baked
AA script, and the return decode). Each was found only when someone happened to be standing in that
file.

Two properties made it recur, and both generalise past enums:

- **At all seven sites the correct width was already in scope and simply not passed.** Not one was a
  case of "we could not know" — `p.Size`, `v.Size`, `PropertySearchMatch.PropSize` and a `size`
  parameter were right there. So the productive grep is not the type name; it is *a method that
  accepts a size and never reads it*, or a mapping keyed on a type name when a size is available at
  every call site.
- **Three of the seven carried a code comment describing the gap before anyone reported it** —
  `"out of v1 scope"` (Y15), `"writes by type, not size"` (Y16), and for W6 a correctly-written
  `CeWidthForSize` helper that the defective path simply did not call. A comment admitting a
  limitation is an unfiled bug; treat it as a finding, not as documentation.

Corollary for the read side: Y16's third site *reads* four bytes for a one-byte enum return. Width
bugs are not only write bugs, and the read half tends to be filed later because it corrupts nothing
— it just reports a number that is wrong.

-----

## 2. Audit agents — raw finder output is about half wrong

**Never present un-refuted audit finder output as findings.** Measured base rate over **seven**
completed segments of audit #5: **71 of 136 raw claims (~52%) refuted.** Per-segment: D1 13/27 (48%),
D2 19/26 (73%), D3 8/18 (44%), D4a 5/9 (56%), D4b 9/18 (50%), D5 11/19 (58%), **U1 6/18 (33%)**.

**The rate is a RANGE (33–73%), not a constant**, and U1 shows what moves it: it is the first segment
whose skeptics could refute using **real test coverage** (~3,567 C# tests compile those files), and it
produced both the lowest kill rate *and* the best-argued kills. Quote the range to finders, not "about
half".

> ⚠ **CORRECTED 2026-08-14 — "every claimed HIGH dies" held for ten and is now FALSE.** Eleven HIGHs
> have been claimed; ten died and **U1/V1 survived** a mandated skeptic, a second lens, and a hand
> re-derivation (a TMap element row is inline-editable while its `FieldAddress` points at the TPair
> base — the KEY — so an edit writes over the map key in a live game process). The old heuristic
> justifies **scepticism, not dismissal**: do not let it talk you out of a HIGH that survives
> refutation.

**The error has a direction, so expect it:** finders report criticisms that are *structurally true* of
code whose oddities are **load-bearing for one specific game, or neutralised by a later phase** — and
they over-rate severity. HIGH from a finder is close to worthless *before* refutation.

In segment D4b, **five of the nine refutations were won by the skeptic finding a COMMENT that names
the very defect the code already prevents** — i.e. the finder had rediscovered the original bug, not a
live one. Put "read the surrounding comments and the callers first" in every finder prompt.

**How to apply:**

- **The audit Workflow's dead-skeptic fallback carries a finding through as
  `verdict: 'UNVERIFIED (skeptic died)'` and it lands in the `confirmed` array.** Check `verdict` and
  the run's `<failures>` block before believing the array's name. Segment D2 "returned" 26 items with
  zero refutation after a session-limit abort.
- **An empty result is what a total wipeout looks like.** D4b's first launch lost all five finders to
  `API Error: 529` at 0 tokens each and the workflow still returned
  `{"confirmed": [], "refuted": [], "note": "no findings"}` — read literally, "this code is clean".
  Never record a segment without reading the failure block.
- Label unverified output UNVERIFIED in the tracker, do not file it in `todo.md`, and do not quote its
  severities. Resume with `Workflow({scriptPath, resumeFromRunId})` — completed finders replay from
  cache, so only the killed agents re-run.
- **Cross-lens convergence (3 lenses, same defect) is a positive signal, NOT verification** — lenses
  can share one wrong assumption.
- **Skeptics disagree with each other, and the majority is not automatically right.** In D2 the same
  `DetectBlockOffsetBits` claim was refuted by one skeptic and confirmed by two; checking the code by
  hand settled it in ~2 minutes and the *refutation* was the wrong one. When duplicate claims get
  split verdicts, decide it yourself — do not report both, and do not file the losing side as
  do-not-re-raise (that silently suppresses a real defect).
- **Calibrating finders is cheaper than refuting them.** Putting the measured refutation rate into the
  finder prompt (from D3 onward) cut raw claims 26 → 18 → 9 while confirmed yield held.
- **Verify against the artifact, not the source, when the artifact is what ships.** D4b's PX1 was
  restored from LOW to MEDIUM by reading the shipped DLL's export table rather than the `.def` file.
- **Hand-verify the segment's headline finding yourself — the pipeline's confidence is not evidence.**
  U1's HIGH had already passed a skeptic *and* a second lens, which is precisely the state in which
  ten earlier HIGHs were still wrong. Re-deriving it took ~5 tool calls, confirmed it, and **found the
  finder had understated a sibling MEDIUM by 3×** (it reported one stale copy of a duplicated formula;
  `grep` found three). One hand-check per segment on the top item is the cheapest quality step in the
  method.
- **Pre-refute a mechanically-decidable claim category with a script, before the agents report.** In
  U1, ~40 lines compared every expression-bodied computed property in the three files against every
  `OnPropertyChanged(nameof(…))` / `[NotifyPropertyChangedFor]` (PointerPanel: 57 computed / 58 raised
  / 0 orphaned). Because the result was an *absence*, §1.2's negative control was mandatory — deleting
  one known-historically-missing raise from a scratch copy made the detector report it. That converts
  a lens's opinion into a measured zero that cannot be re-raised.
- **Once a segment has test coverage, point finders at SEAMS, not helpers.** U1's HIGH lives exactly
  there: `IsEditableType` is unit-tested in isolation while nothing covers the caller that hands it a
  wrong address. "A test asserts the opposite" becomes an available refutation at the same time, so
  say so in the skeptic prompt.

### 2.1 What audit #5 measured across all 12 segments (2026-08-13 → 2026-08-15)

Recorded when scanning completed. These are measurements, not opinions — do not re-derive them.
The findings themselves live in
[audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md) §3c.

- **The comment sweep is the best single technique: 6 for 6.** Grep for a comment that admits a
  limitation or asserts an impossibility, then check whether it is still true. It produced the lead
  finding in T1a, T1b **and** T1e. Nothing else in the method has a hit rate anywhere near it.
- **Fix by FAMILY, not by ID — and grep for siblings *at fix time*, not at scan time.** Two families
  recurred across unrelated subsystems: *the width family* (an out-of-range value masked to the field
  width and then reported as written — six occurrences over four subsystems, and **at every site the
  correct width was already in scope and simply not enforced**) and *root cause #4* (a fix applied at
  only some of its sites — seven occurrences). The rule "grep for siblings before closing a fix" had
  been written down since the fourth occurrence and three more appeared anyway, because it was being
  read at scan time and not applied at fix time. The audit's own register generator repeated the
  pattern: §4 documented a marker tolerance that §3c's regex then failed to apply, dropping 9 rows.
- **Before writing a test, ask whether the CALL SITE can fail it.** AB1's guard was unit-tested and
  still shipped the crash — its single call site sat *inside* the thread the guard existed to
  prevent. A helper tested in isolation proves nothing about the seam that calls it (§1.3, and U1's
  HIGH is the same shape).
- **Cost scales with CLAIMS FOUND, not with lines read.** Segment T1 covered 8.5× S1's lines for
  2.2× its tokens. The lever is claim volume: **merge claims by location inside the script**, and
  batch 10 per refute agent / 8 per second-lens agent to hold a phase to 11–13 agents.
- **Tightening the skeptic's rubric does NOT raise its kill rate.** S1 had the strictest rubric
  written and killed the least (14%). What *does* work is calibrating the finders up front (§2).
- **Keep the second lens even when its kill count is zero** — twice (T1c/AE1, T1e/AF1) it caught the
  *skeptic* being wrong, which is a failure mode nothing else in the pipeline detects.
- **Validate that every filename in a "covered" list resolves to a real file.** T1's first sizing
  budgeted a phase around `PointerViewModel.cs`, **a file that does not exist**; the real file was
  already covered by U1. Six planned phases became five once the names were checked.
- **A tier that skipped the second lens is not a finding tier.** Audit #5's LOW/INFO kill rates ran
  10–35% against the method's own measured 33–73% band — i.e. those tiers were scored leniently, not
  found cleaner. Re-derive any LOW before fixing it; several are pattern-sweep leads, not findings.

-----

## 3. Traps in our own stack

### 3.1 We cannot read our own live log

**`File.ReadLines` / `File.ReadAllLines` / `File.ReadAllText` cannot read a file that anything —
including our own logger — currently holds open for writing.** They open with `FileShare.Read`, which
declares "other handles may only read"; that conflicts with the writer's existing write access, so the
open fails with `IOException` even though we only want to read.

**Why it matters here:** the UI keeps `Logs\<proc>\<cat>-0.log` open for the whole run, so any feature
that mines our own logs sees **every archived log and never the current one**. Measured 2026-08-12:
the leftover-proxy scan's `CandidatesFromLogs` silently contributed zero candidates from the live
`view-0.log`, so a proxy deployed in the current session was invisible to "Find leftovers" until an
app restart rotated the log.

**How to apply** — use `ProxyDeployService.ReadLinesShared`, or the same shape:

```csharp
new FileStream(path, FileMode.Open, FileAccess.Read,
               FileShare.ReadWrite | FileShare.Delete)
```

`ReadWrite` tolerates the live writer; `Delete` additionally survives the file being rotated out
mid-read. The same trap applies to Steam's `appmanifest_*.acf` while Steam is writing it.

**The tell:** a log/config sweep that works after a restart but not before. The failure is *silent by
construction* — this idiom is always wrapped in `catch { }` so one bad file does not abort the sweep,
which is correct, and which is exactly why it hides this.

### 3.2 Avalonia DataGrid — four mandatory rules

Every new `DataGrid` must follow all four; each was learned by shipping the bug first. The project
sets `AvaloniaUseCompiledBindingsByDefault=true`, and compiled bindings change DataGrid behaviour in
ways the Avalonia docs do not lead with. Three of the four are invisible at compile time.

1. **`SortMemberPath` on every sortable column — mandatory.** Avalonia's DataGrid does NOT derive the
   sort path from a compiled binding, so without it **nothing sorts** (found build 933-934). Use the
   **numeric backing property** for hex offset / size / score columns so they sort numerically, and set
   `CanUserSort="False"` on action columns. Deliberate exception: `SpcPanel`'s `SnapshotPicks` sets
   `CanUserSortColumns="False"` — it is chronological on purpose.
2. **Any star column defeats horizontal overflow.** A `DataGrid` with **any** star-sized column fits
   its total width to the viewport, so no horizontal scrollbar can ever appear. The complaint "can't
   drag a column past the window edge" is *always* a star column. `HorizontalScrollBarVisibility`
   already defaults to `Auto`, so a missing attribute is never the cause. Fix: fixed numeric `Width` +
   `MinWidth`, no star anywhere. **Accepted trade-off:** fixed columns leave empty space at the right
   edge of a wide window. **Do NOT "fix"** the intentionally non-scrolling ones
   (`HorizontalScrollBarVisibility="Disabled"`): ConsolePanel's ScrollViewer and ClassPivot's class
   list. `MainWindow.axaml`'s `<ColumnDefinition Width="*"/>` is the tab host — correct, leave it.
3. **Never bind `ItemsSource` to a non-generic `DataGridCollectionView`** under compiled bindings — the
   column bindings lose row-type inference (AVLN2000). For client-side filtering, rebuild a **typed**
   `ObservableCollection` (the pattern Value Search uses: `FilterText` → `ApplyFilter`).
4. **`DataGridCheckBoxColumn` needs select-then-click (2 clicks).** Use a `DataGridTemplateColumn` with
   a TwoWay-bound `CheckBox` for single-click toggle.

### 3.3 Avalonia — animating a bare Transform throws

`Animation.RunAsync(someTransform)` throws `InvalidCastException` inside `TransformAnimator.Apply`. The
built-in `TransformAnimator` only animates a **Visual's** `RenderTransform` — it casts the target to
`Visual` — so animating `TranslateTransform.XProperty` / `RotateTransform.Angle` on a bare `Transform`
routes through it and dies regardless of what you pass.

It is AOT-relevant and fails *silently* if any `catch {}` sits in the path; on the Live Walker
landing-logo shine (build ~1851) a swallowed exception hid it completely, and temporary file-logging of
the exception is what found it. **Assume any "the animation just doesn't run, no error" report in this
app is this bug until disproved.**

**How to apply:** drive the transform by hand. A ~60 fps `DispatcherTimer` writing `translate.X` (easing
by hand) re-renders cleanly via `Transform.Changed` → `TransformGroup` → `RenderTransform`, is AOT-safe,
and avoids `TransformAnimator` entirely. Reference: `LiveWalkerPanel.axaml.cs`.

Unrelated second lesson from the same work: **a soft edge-fade on a bitmap is cheaper baked into the
PNG's alpha than done with `OpacityMask`** — a radial mask could not dissolve the top/bottom edges
without eating artwork that reaches them.

### 3.4 SQLite — the async that isn't

Three rules for the snapshot / SPC / Class Pivot data layer, all learned from freezes the user hit.
These queries scan millions of rows (~1.7M is routine), so each can hang the UI or refuse to cancel,
and the failure looks like a deadlock rather than a slow query.

1. **`Microsoft.Data.Sqlite`'s `*Async` methods run synchronously, AND `ReadAsync(ct)` ignores the
   token.** The only way to cancel a multi-million-row scan is an explicit
   `ct.ThrowIfCancellationRequested()` *inside the read loop*, plus an early bail before opening the
   connection. Every heavy SQLite call must be `Task.Run`'d off the UI thread — including `DELETE`.
2. **`HashSet` is not safe for concurrent read+write.** A denylist passed by reference into a
   `Task.Run` query must never be mutated in place on the UI thread — build a fresh set and reassign.
3. **Immutable data means cache/precompute with no dirty-flag.** Snapshots are write-once, so
   per-snapshot derived data (the Class Pivot class-index) is computed once and persisted; correctness
   needs no invalidation. This turned a ~10 s `COUNT(DISTINCT …) GROUP BY` into a lookup (build 923).

**Diagnostic lesson from the same area: a low-CPU + heavy-I/O freeze needs ProcMon ground truth.**
`LockFile`/`Unlock` returning SUCCESS repeatedly at one offset is a **re-open loop**, not lock
contention — that distinction is what finally solved the Class Pivot freeze after a wrong first
diagnosis.

Related: transient UObjects (`//Engine/Transient/*`) have unstable, colliding normalised paths, so
Strict/Identity joins collapse them — use In-session (`gobjects_index`) for same-session queries, or a
field key (ItemID) for Pivot.

### 3.5 Verify `dist/` before asking anyone to test

The user runs the DLL via Proxy mode — copying `dist/UE5Dumper.dll` (or `version.dll`) into the game's
`Binaries\Win64\`. If `dist/` is stale they re-test the OLD binary and the bug looks unfixed. This
happened on build 588 → 589: `-Target Test` was run (which only rebuilds the test exes) and the user
was asked to test; their game DLL was still build 586.

**How to apply:** after DLL-side changes run `build.ps1` with no `-Target` (or `-Target DLL`), then
check `dist/build_number.txt` shows the expected number AND `dist/UE5Dumper.dll` has a recent mtime.
Quote the fresh build number in the test instructions.

**The UI half of the same rule (CLAUDE.md's Build & Deploy section):** hand over the **AOT-trimmed**
build (`-Mode Publish`, ~54 MB), never the plain self-contained one (~107 MB). Reflection-shaped code
compiles and runs fine untrimmed and fails **only** after trimming — and a stale/oversized
`dist/UE5DumpUI.exe` is how that reaches the maintainer.

-----

## 4. UE and CE facts that cost a session each

### 4.1 `FProperty` layout is +4, not +8

```cpp
class FProperty : public FField {
    int32 ArrayDim;        // +0x30
    int32 ElementSize;     // +0x34  <- propElemSizeOff
    EPropertyFlags Flags;  // +0x38  <- propElemSizeOff + 4
    uint16 RepIndex;       // +0x40
    int32 Offset_Internal; // +0x44
};
```

`ArrayDim` is BEFORE `ElementSize`, not between `ElementSize` and `Flags`. The pre-build-642 formula
`FPROPERTY_FLAGS = propElemSizeOff + 8` was based on the wrong order and read into the high 32 bits of
the 64-bit `Flags`.

**Why it stayed silent for 600+ builds:** the parm-classification bits (`CPF_Parm=0x80`,
`CPF_OutParm=0x100`, `CPF_ReturnParm=0x400`) all live in the **low** 32 bits, so with the wrong offset
every UFunction parameter classified as `IsReturn=false / IsOut=false`. Nothing cared until build 637's
Verify Return Value mode tried to find the return slot.

**How to apply:** use `DynOff::FPROPERTY_FLAGS` (correctly `ElementSize + 4` at runtime). Never
hardcode `+0x3C` or any "+8 from ElementSize" form.

### 4.2 CE Lua quirks baked into our code as defensive patterns

**`getAddress` vs `getAddressSafe`.** `getAddress(name)` either throws or silently returns garbage when
the symbol can't be resolved (CE-version dependent); `getAddressSafe(name)` consistently returns nil/0.
CE's resolver may only register the **module-prefixed** form on some setups, so bare-name lookups can
silently succeed-but-return-wrong-address. The robust pattern (mirrored in `ue5_invoke_helper.lua`'s
`findMailbox`):

```lua
local a = getAddressSafe('g_invokeMailbox')
if not a or a == 0 then a = getAddressSafe('UE5Dumper.g_invokeMailbox') end
return a or 0
```

**`tableFile.Stream.write` does not write.** It returns no error but doesn't update the TableFile's
stored content — `Stream.Size` keeps reading 0. Use CE's own pattern instead:

```lua
local ss = createStringStream(content)
f.Stream.copyFrom(ss, 0)
ss.destroy()
```

**`executeCodeEx(callmethod, timeout, address, params...)` — the address is argument 3.** Every emitter
here once passed `(0, fn)`, putting the address in the timeout slot; the call then returns `nil`
**without raising**, so a `pcall`-status check reported success and the CE window auto-closed announcing
a clean shutdown that never happened — `UE5_Shutdown` had never once run in the field. Also:

- **`nil` timeout means `INFINITE`**, not "use a default".
- **The wait is `WaitForSingleObject` on the CALLING thread with no message pump**, so from an AA
  `{$lua}` block the timeout is a ceiling on GUI-freeze time, and a Lua-side `processMessagesPaintOnly`
  structurally cannot reach it. (The `sleep()` mailbox loop is the opposite — it pumps itself.)
- **Failure returns `nil` PLUS a reason string** — six distinct ones, four of which occur with a
  perfectly healthy process. Never guess the message; capture `local ret, why = ...`.
- **A timeout does not reclaim.** `dontfree := true` on the `WAIT_TIMEOUT` branch permanently leaks the
  stub, the result address and every string allocation **in the target process**, so "just raise the
  timeout" is not free.
- Wrapping gotcha: `pcall`'s second return is the Lua error on a raise and the callee's RAX on a clean
  run, so one `if not okCall or ret == nil` cannot tell them apart. Two branches.

Full model: [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §13.

**`sleep(n)` is quantised to the ~64 Hz kernel tick.** `sleep(1)` measures **15.47 ms**, and
`sleep(1)`…`sleep(10)` all cost the same; `sleep(16)` jumps to ~30 ms. A wait loop counting iterations
against a `10000` ms constant was therefore bailing at **~155 s** of frozen Lua Engine. It is **not**
machine-dependent (§1.7). Use a real deadline via `getTickCount()`.

**`getSettings()` works, but CE cannot read its own REG_MULTI_SZ.** The API, subkey selection and
`Value[]` all work; the **type** is unreadable — `Value["Recent Files"]` returns a zero-length string
and `getBinaryValue` returns `nil`, while a REG_SZ under `Plugins64` reads fine. The working route is
shelling out to `reg.exe`, which renders MULTI_SZ separators as the literal two characters `\0`. Three
rounds of being wrong here were each corrected by a **probe**, not by re-reading `celua.txt` — which had
advertised both a non-existent capability and a working one it had first denied.

### 4.3 Do not use KismetMathLibrary as a verification target

KismetMathLibrary helpers (`Exp`, `Multiply_DoubleDouble`, `Add_IntInt`, …) **silently no-op** when
invoked via ProcessEvent from a reflection-driven dumper on UE 5.5+ cooked Shipping. Likely UE's
BlueprintFastCall optimisation: the BP VM bypasses ProcessEvent entirely for these helpers, so the
cooker leaves the reflection metadata intact (parmsSize, numParms, parm offsets, flags all correct)
while the `execXxx` thunk returns without writing `Z_Param__Result`.

Verified failing pattern on Everspace 2 (UE 5.5): A=3, B=4 written correctly, dispatch returns
`result=0`, ReturnValue stays 0, inputs preserved and the return slot untouched.

**How to apply:** redirect verification to **game-specific instance methods** (PlayerController /
Character / Inventory subclass functions), with the user in **active gameplay** (not an idle main menu)
so the game thread pumps ProcessEvent, and prefer simple scalar returns.

> ⚠ This one carries a confound worth remembering: it was diagnosed while the ProcessEvent hook was
> installed in the **wrong vtable slot** (a hardcoded UE-version table whose only "validation" was that
> the slot pointed at readable code — which every UObject virtual does). That was fixed in build 648 by
> pattern-scanning the function body plus a post-install fire-counter watchdog. **The stub hypothesis
> was never re-verified against the corrected hook.** The generalisable half is the reason it slept for
> 600+ builds: `-5` timeouts were attributed to "idle game / game thread not pumping" — *a
> plausible-sounding explanation that was never falsified.*

-----

## 5. Triage recipes

### 5.1 "Value Search / Group Scan can't find field X"

Reported **five** times between 2026-08-05 and 2026-08-10 with two completely different root causes,
**neither of them in the scanner** — and the first sessions both went hunting in scan code. The symptom
is identical (the user sees a field in Live Walker, no scan returns it) but one cause is "the object was
never enumerated" and the other is "it matched and the row could not show it". Neither logs an error;
both read as a healthy scan.

**Check these two, in this order:**

1. **Was the object even enumerated?** `find_by_address` on the live object settles it in one call.
   `index: -1, match_kind: "backward"` for the instance while its CDO resolves
   `index: <N>, match_kind: "exact"` means every object any tool can see is a `Default__*` — the scan is
   fine and the array descriptor is wrong.
   **The tell is a counter that does not move**: `get_object_count` returning the *same* number 78
   minutes and one map later is not a count of anything current — a live `FUObjectArray` never holds
   still. The 2026-08-10 case was `ObjLastNonGCIndex` (+0x04, the frozen startup high-water mark) being
   read as `NumElements` (+0x24, 317,810 and climbing), enumerating **11.7%** of the pool and calling it
   a full scan.
   **The general shape, worth more than the specific offset:** *a validator that rejects the right
   answer does not fail loudly — it falls through to a wrong answer that looks healthy.* The relaxed
   tier logged `Valid`, and the wrong count was copied into `test-games.md` as a normal result.
   Corollary: which preset row has to be correct is **not a fixed property of a title** — the same
   binary at the same module base resolved a different pattern and anchor between two runs.
2. **Did it match, but the row could only display one pairing?** A group-scan row shows ONE assignment,
   not the whole match: a slot keeps every field that satisfied it (up to `per_slot_cap`, default 256).
   **Four of the five reports were this.** Confirm from the session's own `ui-pipe-0.log` (it lists the
   kept offsets per slot), then use the **All fields** button (`query_group_slot_leaves`) and the `(+N)`
   annotation. The group filter is **space = AND**, so `tickcount frozenint` forces that exact pairing.
   Do **not** "fix" this by re-ranking which witness wins — that is zero-sum; promote either pairing and
   the other reads as missing. Two rules did survive as tie-breaks: prefer a same-struct sibling, and
   **non-zero beats zero** ("a 0 has little real meaning in a game", maintainer, 2026-08-05).

The witness rule lives in `Radar::PickGroupWitnessAssignment`, deliberately beside the filter it must
agree with, because while it sat in `Fern.cpp`'s JSON encoder **no test target compiled it** and it kept
drifting. **Check that a rule you are about to move is somewhere a test can reach.**

-----

## 6. Settled — do not re-propose

Decisions the user already made, or approaches already tried and rejected. **Most were settled in
conversation, not in code, so the repo carries no trace of them** — which is exactly why a fresh session
on either machine re-derives the same "good idea" and gets corrected. Scan this before proposing
architecture or UX changes in these areas.

- **Per-tab denylists, NOT one shared list.** Diff / SPC / Pivot each keep an independent list (one
  per-game JSON, `DenylistScope`). A shared single denylist was built and then **reverted on request**.
- **`app.manifest` was never the window-icon fix.** The manifest already existed and was DPI-aware; the
  real cause was `.ico` decoding flakily under AOT/Skia. Fixed with a **PNG** for `Window.Icon`
  (`ApplicationIcon`, the exe's file icon, stays `.ico`).
- **Do not `NoWarn` the X11 ILC warnings.** The non-Windows Avalonia backends were removed instead. The
  sibling project `D:\Github\CrimsonAtomtic` takes the NoWarn approach — that is not our choice.
- **DB normalisation** — tried and reverted at build 882.
- **UFunction `MetaDataMap`** — editor-only; cooked UE Shipping strips it, and there is no `DisplayName`
  / `Category` at runtime. Not recoverable; do not plan features on it.
- **Substring keyword matching** — rejected. Keyword boxes are whitespace-split **term-level AND** via
  `ObjectTreeFilter.MatchesAllTerms` (see the CLAUDE.md rule).
- **GPL-3.0** — rejected. The project is MIT.
- **Hierarchical Copy CE XML direct-push to CE** — DEFERRED, not refused: it needs an unbuilt bulk-tree
  client plus a `CeXmlExportService` Emit-layer refactor (there is no tree model today). Per-row `+CE`
  (PR #251) and flat `+CE Fields` (PR #252) **did** ship.
- **Filter-and-pick UI = TextBox + ListBox.** Do not re-propose `AutoCompleteBox` (`SelectedItem`
  oscillates) or `ComboBox` (dropdown drops clicks on rebuild).
- **Multi-pipe IPC**: Phase 0 (scan thread-priority guard) and Phase 1 (single-handle worker) were both
  **REVERTED** — Phase 1 deadlocked on the sync pipe, Phase 0 starved scans 20×. The shipped answer is
  Path A, two connections each with its own handle+thread. See [multipipe-eval.md](multipipe-eval.md),
  whose §10 also **measured and refuted** the original head-of-line-blocking premise.
- **Never refuse a proxy flavour because the .exe does not import it.** Measured: **11 of 21** Steam UE
  games run a working `version.dll` proxy with **no** static import — it arrives via a runtime
  `LoadLibrary`, and the search order reaches the .exe directory first. An import proves a proxy *will*
  load; its absence proves nothing. Now advisory (`ProxyImportAnalyzer.DescribeLoadRisk`). Also **do not
  escalate to dxgi** on "version not imported": 21/21 import dxgi, and Octopath instant-exits under it.
  The diagnostic for a proxy that genuinely cannot load is **"no log folder appeared"**.
- **Do not version CE scripts on the BUILD number** — that condemns every saved `.CT` on every release.
  The axis is the **contract** (`MAILBOX_CONTRACT` / `..._MIN`, a *range*). A forgotten bump is worse
  than no versioning, hence the `check_mailbox_contract.py` CI gate.
- **gz/zip for log compression** — measured and rejected. `compact /c /exe:LZX` does 12.8:1 in 2.8 s in
  place and leaves filenames, `rg`/grep, "Open Log Folder" and the 21-day purge untouched; GZip is only
  1.6% smaller and costs all of that.
- **Bookmark expiry sweep** — **rejected**, not pending. `BookmarkStore` passes `maxAgeDays: 0`
  deliberately: a few KB of hand-placed navigation nobody can regenerate ≠ a regenerable multi-GB
  snapshot DB. Do not "finish" it later.
- **Auto re-scan after a leftover-proxy delete** — rejected. It would re-find every FAILED row with a
  **blank** status, and that status is the only actionable output a failed delete produces.
- **`docs/evaluations/` subfolder** — rejected. After fixing stale status headers the set of "record of
  something deliberately not built" is **n = 1**, and moving ~40 files would regenerate two CI-compared
  golden artifacts that embed doc paths.
- **`{$CCODE}` / `{$C}` adoption** — evaluated 2026-08-07, **do not adopt**. The repo emits zero
  injection hook sites, and our injected DLL pays no SafeCall stub, so it is *faster* than CCODE. See
  [ce-ccode-eval.md](ce-ccode-eval.md) for the two conditions that would reopen it.
- **Splitting a keyword box's concatenated haystack into per-field matches is not automatic.**
  Prescribed for `DumpExplorerViewModel`, measured, and **rejected on the number**: four fields +
  `OrdinalIgnoreCase` is **2× slower** (55.1 ms vs 25.8 ms, 500K entries) for identical hits. The real
  defect was splitting on `' '` alone — fixed with `SplitTerms` / `MatchesAllTerms`.
- **Do not reorder GObjects preset rows B and E.** Putting E first would let its +0x20/+0x24 reads steal
  a real Back4Blood layout — trading one silent misread for another. The fix is the chunk-count
  discriminator plus a two-pass relaxed table, both strictly widening.

Evaluations that concluded "do not build" live in the repo rather than here — see CLAUDE.md's docs table
for `text-translation-eval.md`, `teleport-coord-library-spec.md`, `native-c-value-scan-spec.md`,
`multipipe-eval.md`, and `Nibble-Mask-Evaluation.md` in the AOBMaker repo.

-----

## 7. Operational notes for two-machine development

- **`build_number.txt` auto-increments on every `build.ps1` run** (MSBuild only reads it), so doc and
  commit build references drift. Cite the build as of commit time.
- **`dist/` is gitignored**, so a freshly synced repo can still hold a days-old runnable build. Check
  `dist/UE5DumpUI.exe`'s size and mtime, not just `git status` — ~54 MB is the AOT-trimmed build,
  ~107 MB is the non-trimmed one that must never be handed over.
- **The Ghidra corpus is machine-local and derived.** `$GHIDRA_PROJS` = `D:\Tools\GHIDRA_Projs` on this
  machine, but the real corpus is the archive at `D:\UE_Analyze_data`; run
  `py tools/ghidra/corpus_relocate.py` / `preflight.py` before trusting any path. Never host it on USB
  (see the 2026-08-01 drive-drop incident).
- **Plan ONE stage per 5-hour quota window, not two.** Measured 2026-08-14: one window ran an audit
  segment takeover + a full new segment + six fixes with their builds and in-game verification, and
  reached **80%**. A stage is a scan *or* a fix batch, not both. If budget is left over, spend it on
  **verification** — it is cheap, it compounds, and it ends at a clean stopping point; starting the
  next stage does not, and a segment cut off mid-flight costs more to resume than it saved.
- **Long unattended work can run as a scheduled task in its own session with its own quota.** Audit #5's
  segment D4b ran that way for ~49 minutes and survived a Claude Desktop re-login; the prompt file under
  `~/.claude/scheduled-tasks/<name>/SKILL.md` is the template. Two constraints: the task must **commit
  its own work** (nothing else persists), and if another session is open on the same clone it must stay
  **hands-off** — one working tree, two sessions.

### 7.1 Where a lesson belongs — this file vs. the assistant's memory

**Rule, settled 2026-08-15 and binding on both machines: a working lesson goes in THIS FILE, and the
memory folder does not keep a copy.**

The memory folder lives at `%USERPROFILE%\.claude\projects\<project>\memory\` and is **not in git**,
so it exists on one machine at a time. Between 2026-08-14 and 2026-08-15 every section of this file
also existed there as a `feedback_*.md` twin, on an "edit both" rule. That rule failed in the ordinary
way: the copies drifted, several twins were staler than this file, and the machine that needed them
most — the other one — never had them at all. **Fifteen duplicate memory files (~48 KB) were deleted
on 2026-08-15**; `MEMORY.md` now carries a pointer here plus the section map above.

**How to route a new fact:**

| Fact | Goes to |
|---|---|
| A verification method, a trap in our stack, a UE/CE fact, a settled decision | **This file** (§1–§6) |
| What shipped, when, and why | `dev-log.md` (append-only) |
| Open work, effort/risk, pending live verification | `todo.md` |
| What a *game* does differently | `lessons-learned.md` |
| A machine-local path (`$GHIDRA_PROJS`, corpus location, sibling repo checkouts) | memory |
| In-flight project state that has no home in the repo yet | memory |
| Which doc to read next, and where the current work is | `MEMORY.md`, as a **pointer**, not a copy |

**Two corollaries, both learned by paying for them:**

1. **Never let memory restate content that a repo doc owns.** If you catch yourself writing a fact
   into memory that a doc already states, write it in the doc and point at it. Duplicated prose is not
   redundancy — it is a second thing that can be wrong, and the reader cannot tell which copy is
   current. The audit register hit the same shape from the other direction (§2.1, root cause #4).
2. **`MEMORY.md` is the only file loaded into every session**; topic files load on recall. So the cost
   of a long `MEMORY.md` is paid on every single turn of every session, while the cost of a topic file
   is paid only when it is relevant. Keep `MEMORY.md` to pointers and machine-local facts. **Adding a
   second index file does not help** — it either also loads (no saving) or is never read (dead
   weight). The lever is deduplication against git-carried docs, not more files.
