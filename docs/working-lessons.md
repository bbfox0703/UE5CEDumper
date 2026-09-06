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

**And the conditions must reach the doc that owns the PROCEDURE, not only the one that owns the
number.** 2026-08-17 recorded A5's PASS *with* its conditions — *"a re-search ~38 s later previewed
317"* — into `todo.md`'s evidence block, and that block was right. But the **step**, in `todo.md`
*and* in the 繁中 mirror, still said to watch the Preview column, and a Property Search preview is a
per-search snapshot with no timer and no binding. The wrong procedure therefore outlived a PASS: on
2026-08-19 the maintainer followed it, saw nothing move, re-ran the same query three times in four
seconds, and reported a defect that does not exist. **A PASS obtained by a procedure the checklist
does not prescribe means the CHECKLIST is the defect — correct the steps, in every copy that carries
them, in the same commit that records the PASS.** Recording the conditions and leaving the
instructions alone reads as done and is not.

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

### 1.10a A probe that holds the deciding variable fixed will confidently answer the wrong question

**Three times in one sitting (2026-08-17), and each one nearly became a filed finding.** The shape is
identical every time: build a probe, get a clean negative, and conclude something general — while the
one variable that actually decided the outcome was never varied.

| the probe | its clean negative | what it actually held fixed |
|---|---|---|
| copied an indexed `Ollama.lnk` under a new name to test whether the app index takes new entries | copy never appeared | **the target** — AppsFolder dedupes on target path, so a duplicate is invisible no matter what |
| three fresh `.lnk` files, varying drive / target / file completeness | none appeared ⇒ *"the index takes no new `.lnk` at all"* | **the directory** — all three were per-user; the all-users folder works, which is the entire answer |
| a title missing from the injection picker, with `OpenProcess` succeeding at every access right | looked like a real `IsUe` misclassification | **the scroll position** — the list held 7 and showed 6; the title was one scroll below the fold |

**The rule that caught all three, and it is cheap:** before believing a negative, *enumerate what
differs between a case that works and the case that does not*, and make sure the probe varies **that**.
A working example was sitting in plain sight each time — `Ollama.lnk` in the indexed list, and the
same picker having listed `DSClient-Win64-Shipping.exe` minutes earlier.

**A second, blunter tell:** if a probe produces a *general law* ("X never happens") from a sample that
never varied the obvious suspect, the law is almost certainly a property of the sample. Two of the
three above were phrased as laws, and both were wrong.

⚠ **And the same day, the same shape at a smaller scale:** a Recycle-Bin check filtered on "modified
in the last 30 minutes" and reported the recycled file absent — because `shutil.copy2` had preserved
the *source's* mtime. Match deleted files on **size**, never on time.

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

### 1.x A log-window that is coarser than the events it separates reports a CONFIDENT WRONG ANSWER

Four separate rigs in the 2026-08-20 batch got this wrong in four different ways, and each time
the failure mode was the same: **the rig printed a verdict, not an error.**

| how the window was taken | what broke |
|---|---|
| line COUNT across several `*-0.log` files, sliced `[before:]` | more than one file grows, so new lines land in the MIDDLE of the concatenated list, not at the end. Reported **0** `enqueued invoke` while the log plainly held one. |
| `strftime("%Y-%m-%d %H:%M:%S")` timestamp watermark | **one-second** resolution. Three mailbox cells running milliseconds apart: cell A's `static-native fast path` line fell inside cell B's window, and the rig announced `FAIL: the poisoned flags took the FAST PATH` — on a run whose own `result=-5` proved the call had been **queued**. |
| counter read OUTSIDE the timed window | `get_diagnostics` round trips take milliseconds during which the game keeps firing `ProcessEvent`; timing only the invoke while differencing across two round trips manufactured a 6.6-fire "excess" and a false FAIL. |
| BYTE offset into a log recorded before launching the process | **every process start ROTATES `<cat>-0.log`**, so the offset (tens of KB) slid past an entirely fresh file. The rig printed an empty section while the log plainly held the line it was looking for. |

**The rule: measure with something at least as fine-grained as the thing you are separating.** For
append-only logs inside one run the reliable primitive is a **before/after COUNT of matching lines**
— exact at any timing, immune to file count, and needing no clock at all:

```python
class Watch:                      # tools/verify/l4_mb1_stale_flags.py
    def __init__(self):  self.base = {n: len(all_matching(n)) for n in self.NEEDLES}
    def delta(self, n):  return all_matching(n)[self.base[n]:]
```

⚠ **A timestamp watermark is still right when the events are seconds apart and the log is shared
with other processes** — it is not banned, it is just the wrong tool below its own resolution.

⭐ **The tell that saved the third case: two signals in the same output disagreed.** `result=-5`
(queued) and "fast-path lines: 1" (called directly) cannot both be true. When a rig's own numbers
contradict each other, suspect the rig before writing up the defect.

### 1.y "The first live instance of class X" is not what `find_instances` returns

`find_instances(class_name="Actor")` without `exact_match` is a **name SUBSTRING** match. On
DumperTest it returns `ActorSequence`, `ActorElementAssetDataInterface`, `ActorPartitionSubsystem`
and ~200 more, so `[i for i in r if not name.startswith("Default__")][0]` handed back a
`UActorSequence` — and the rig then invoked `Actor.UserConstructionScript` against it.

Filter on the reported **`class`** field, not on the query:

```python
ins = [i for i in r["instances"] if i.get("class") == cls
       and not str(i.get("name","")).startswith("Default__")]
```

Two reasons this matters beyond tidiness:
* **it is a live-fire hazard, not just a wrong sample.** A wrong-type invoke that TIMES OUT stays
  **queued**, and a queued request can drain later — running an `AActor` method against a non-actor
  at an arbitrary moment. (It did drain here, and nothing crashed. That is luck, not a result.)
* the same substring behaviour already produced a wrong baseline once, when `Actor`'s "58 live
  instances" was compared against a `find_instances` count that had swept in `ActorElement*`
  non-actors.

### 1.w Before filing a defect, GREP THE DOCS FOR THE SUBJECT — the answer is often already written

2026-08-22, running G11 step 2: Avowed's UI badge said `UE504` while the DLL log said `503`. That
looked like the repo's signature defect shape — report and reality computed by different code paths
— so it was filed as a confirmed defect with a reproduction.

**It was a false alarm, and `docs/todo.md` already said so**, under a heading that could not have
been more explicit: *"THREE DIFFERENT 'UE VERSION' QUANTITIES, and confusing them manufactures a G11
false alarm"* — naming Avowed, naming the `CMC::GravityDirection` raise, concluding *"503 and 504 are
both right for different questions"*. That note also records that the same confusion had already
cost **two** contradictory readings before it. The filing made it three.

* **The cost is asymmetric.** A false defect is worse than a missed one here, because the prescribed
  fix ("make Avowed report 503") would have deleted a deliberate, correct structural correction —
  the same failure mode as §2.4.
* **The cheap guard is one command.** `grep -n <subject> docs/todo.md` before writing the filing. The
  answer was one grep away, in the same file the filing was being written into.
* ⚠ **A live reproduction is not evidence that the behaviour is wrong.** Reproducing `UE504` on
  demand felt conclusive and proved only that the feature works every time. Ask "what would this
  look like if it were CORRECT?" before "how do I reproduce it?".
* ⭐ **The re-derivation still paid, but only because it was pushed to the mechanism.** Chasing it to
  the actual writer showed the older note's own explanation ("the cache's 504 is from an older run")
  was wrong — the value is written fresh every scan by the C# mirror. Stopping at "already
  documented, never mind" would have preserved that error.

### 1.z "No pre-fix baseline exists" is sometimes DISSOLVABLE — and the oracle must be computed FIRST

Three lessons from closing `AC15` (2026-08-22), which two earlier sessions had left half-open with
the identical honest limit: *"the same games as before" cannot be checked — no baseline exists on
this machine.*

**a) Ask whether the change could possibly matter before hunting for a baseline.** The whole diff
was `try { var info = GetVersionInfo(p); return null; } catch { return null; }` → `=> null`. **Both
forms return null on every path.** A behavioural baseline was never needed: the removed call could
not influence the result, and that is a *proof*, strictly stronger than any two observations. When
a row demands a before/after and the "before" is gone, read the diff — a fix that deletes work
whose result was discarded is provably behaviour-preserving, and the row collapses to a smoke test.
⚠ The reverse also holds: if the diff does not carry that property, no amount of re-running the new
code is evidence about the old, and the row should say so rather than tick.

**b) An oracle computed AFTER you read the answer is worthless.** Write the independent
implementation, run it, and **write its number down before opening the UI**. An oracle produced
afterwards gets debugged until it agrees — every discrepancy reads as a bug in the oracle, because
you already believe the answer. Order is the whole control, and it costs nothing to get right.

**c) A count agreeing is weak evidence; the NAME is what pinned it.** The drive scan and the oracle
both said 22 — but the sharp agreement was that **ten rows are named `Unreal Projects`** rather than
`DumperTest` / `StackOBot` / …, because prune-on-match fired at `D:\Unreal Projects` itself and the
per-game detector then walked its children. Pruning one level deeper yields **the same count of 22**
with ten different names. So when comparing against an oracle, diff the *derived* fields that
encode the algorithm's decisions, not the tally — the tally is the one number a wrong
implementation is most likely to get right by accident.

⭐ **d) Corollary that closed the row's last claim: check what the column BINDS before treating it
as evidence.** A 2026-08-20 note read "the Version column is empty" as proof `UeVersion` was null.
That column binds `InstalledVersion`; `UeVersion` is bound by nothing at all, so the observation was
about a different property entirely. The conclusion happened to be true. One `grep` over the
`.axaml` is cheaper than an argument about what a blank cell means.

-----

-----

### 1.aa When no host has the case, MANUFACTURE it — and let the write be the negative control

`V6/U8` step 2 wanted a `NameProperty` whose value carries a numeric suffix (`Slot_1`), to check
that Live Walker and Value Search render the same 8 bytes of an `FName`. DumperTest has none —
measured three ways rather than assumed (a game-wide 375-property sweep, the test actor's own
`Name_*` fields, `Map_NameToInt`'s three keys: all `Number=0`). The reflex is to go install or boot
a game that has one, which costs a whole session and leaves the row open until then.

**Cheaper and strictly stronger: write the case into memory with CE, observe, write it back.**

```
as found  1D 04 00 00 00 00 00 00  ->  GameNetDriver
written   1D 04 00 00 02 00 00 00  ->  GameNetDriver_1
restored  1D 04 00 00 00 00 00 00
```

⭐ **The write is not a workaround for the missing fixture — it is a better experiment than the
fixture would have been.** A found `Slot_1` shows only that the panels agree. A *toggled* one shows
they agree **because they read that field**: scanning the bare name went from **267 hits to 266**
and the row vanished the instant the `Number` moved, which proves the matcher is not comparing
`ComparisonIndex` alone. Two panels can agree while both ignore the same half of a value; only the
transition rules that out. This is §1.2's negative control, obtained for free by owning the input.

**When it applies:** the quantity is (i) reachable and writable from outside, (ii) inert enough that
a wrong value cannot corrupt state you care about — a scratch field in our own test fixture is
ideal — and (iii) restorable, with the restore **verified by re-reading**, not assumed from the
write returning.

⛔ **When it does not:** anything the game recomputes, anything persisted to disk, and anything whose
wrong value could crash a session you still need. And do not manufacture the case in a title you did
not launch yourself for this purpose.

⚠ **Keep the third reader.** CE's raw `readBytes` is what makes this more than self-agreement: Live
Walker and Value Search are two consumers of the same DLL, so they can share a decoding bug. The
tool doing the write is conveniently also outside that path — use it to read as well as to write.

### 1.ab A HEADING IS NOT EVIDENCE — in this register the closure is recorded somewhere else

⚠⚠ **`docs/todo.md` records a closure in a DIFFERENT PLACE from the row it closes**, usually under
its own `✅ … [SOMETAG-2026-08-NN]` heading thousands of lines away. The original `⬜`/`🟡` heading
is **not** updated as a matter of course. So reading a heading — or a summary built from headings,
or a handover paragraph quoting one — tells you nothing about whether the work is outstanding.

**Measured, in a single session (2026-08-24): five times.** The worst was audit **L10 step 2**. Its
section heading read, in these words, *"Step 2 is still open, on a NEW blocker"*, and the block
ended with *"▶ Next session starts here"*. Both were **false the following day**: the step is
closed three times over elsewhere in the same file — `[AF16-PROPSSORT-2026-08-22]` (Props half),
`[AF16-XREF-2026-08-23]` (Xref half, the blocker cleared by `tools/verify/af16_xref_fixture.py`
finding a fixture *by construction*), and `[AF16-BYCONSTRUCTION-2026-08-24]` (the numeric-vs-string
residual, closed as **unreachable**). A seven-agent workflow was spun up to solve a problem that
had already been solved twice.

**The rule, and it is cheap:**

1. **Before planning ANY row, grep the whole file for its finding ids** — `AF16`, `AE4`, `[TAG-…]`
   — and read every hit, not just the one under the heading you started from. A `✅ CLOSED` block
   naming the id anywhere in the file closes it.
2. **The per-step TABLE inside a section is the ground truth, not the heading.** Headings like
   *"4-of-6"* and *"STEPS 1-4, 7, 8, 9 DONE"* go stale the moment one more step lands.
3. ⭐ **When you close a step, edit the HEADING in the same commit.** This is the whole fix. Closing
   AE4 step 2 while leaving the heading saying *"only step 2 is still PARTIAL"* is how the next
   session gets misdirected — and that heading survived a maintainer pass, an automated sweep, and
   two of my own reads of the same section.
4. **A stale heading is worth a commit on its own.** Mark it superseded with pointers to the real
   closures; keep the body only when it still carries something (L10's five dead ends are worth
   keeping *because each is now explained*, and the explanations are what stop a sixth attempt).

ℹ️ The same trap wears a second face: `docs/pending-verification_zh-TW.md` is the operational
mirror and is deliberately **much** shorter. An item absent from it is weak evidence the work is
done — worth checking, never sufficient on its own.

### 1.12 ⭐ THE DOMINANT DEFECT SHAPE HERE: the report and the reported thing are computed by different code paths

*Four independent instances in one 2026-09-05/06 verification session — a logging change, an
acceptance criterion, a test target, and a struct field. None was a coding mistake in the ordinary
sense; every one was **a claim that had drifted away from the thing it claimed about**, while every
gate stayed green. This is not a new observation — audit #4 filed it as root cause 4a — but four in
one session is the evidence that it is **the** shape to hunt, not one of many.*

| # | the claim | what was actually true |
|---|---|---|
| 1 | `70d28548`: "build: zero warnings on a CLEAN publish" | `_wfopen`→`_wfopen_s` also made every live log **unreadable to every process** (`fopen_s` opens exclusively), deleting the DLL-side half of every acceptance test in the repo |
| 2 | A2's row: "`VALIDATION FAILED` must be ABSENT" | on the pattern path a zero-fire hook logs a **different** line, so the absence is satisfied **by construction** — a wrong slot passes |
| 3 | todo + register: "A7's live half CLOSED" | the test block drives `Aura::ForEach`; A7 fixed `FindByAddress`, which hand-rolls its own loop. `grep FindByAddress dll/tests/ tools/verify/` = **0 hits** |
| 4 | `Ubel.h:435`: "`softArrayFNameSize` — sizeof(FName): 8 or **12**" | both writers set **`0x10`**, and six sibling sites did the same |

**What they share, and it is the thing to look for:** in each case the *checker* and the *checked*
were separated by a step nobody re-derived — a share mode not visible in the diff, a log line whose
branch was never read, a label that says "A7" over a call to something else, a comment that stopped
being regenerated from the code. **Green is not evidence. Green plus a demonstrated ability to go
red is evidence** (§1.2).

**The four questions that found all of them**, in the order they are cheapest to ask:

1. **"What would this check say if the thing under test were deleted?"** If the answer is "the same",
   the check is decorative. Deleting A7's poll turned two new assertions red with
   `...reports no index rather than a stale one   got: 8000` — *that* is what closed the row, not
   the assertions passing.
2. **"Does the label name the thing, or a neighbour of it?"** `Aura::ForEach` and
   `Aura::FindByAddress` are siblings in one file and the fix's own comment names ForEach as a
   *model to copy*. A block titled "A7" over the model instead of the subject reads correct at a
   glance and forever.
3. **"Is the absence I am asserting reachable at all?"** An `ABSENT:` criterion needs a paired
   positive that proves the code path ran (§1.9 says this for strings; it generalises to branches).
   A2's real evidence turned out to be a line already in the log that nobody had cited —
   `validation OK — hook fired 660 times in 1500ms`.
4. **"Does the doc regenerate, or was it typed once?"** `Ubel.h`'s field comment and its two writers
   could not both be right, and nothing compared them. Where the answer is "typed once", either
   derive it (`check_derived_counts`) or make the wrong answer unrepresentable — which is what
   `DynOff::SizeofFName()` / `FNameSlotIn8Aligned()` do: the rule had been written out in full in
   `Grimoire.h`, *including a warning that it was the most-copied wrong sentence in the tree*, and
   was still copied wrongly into eight sites, because **both answers are spelled
   `bCasePreservingName ? … : 0x08`** and the expression cannot say which question it answers.

⛔ **A documented rule is not a defence.** All four had documentation on their side; #4 had the rule
stated correctly *four lines above* one of the sites that got it wrong. Prefer, in this order:
**make the wrong answer unrepresentable** (a named function, a type) → **derive it and gate the
derivation** → **write it down**. Only the third is what most of these had.

⚠ **And the same shape lives in verification rigs, which is worse, because a rig is trusted.** Three
of mine manufactured false verdicts in the same session: one expected soft and lazy to share a
tagged envelope (they do not — `0x10` vs `0x0C`) and scored a *correct* DLL as FAIL on OCTOPATH; one
demanded a `DetectVersion:` line that a cached verdict legitimately suppresses; one **raced a
1500 ms validator** and reported "no validation OK line" about a line that appeared 1.6 s later.
A rig that encodes the pre-fix model, or that reads a log before the code has finished writing it,
converts a working fix into a bug report. Rigs need §1.2 applied to themselves.

### 1.13 ⭐ A CACHE STAMPED WITH ITS PRODUCER'S VERSION CANNOT SEE A VALUE A DIFFERENT WRITER PUT THERE

*Sibling of §1.12 and worth its own entry, because the diagnostic is different. §1.12 is "the
report and the reported thing are computed by different code paths". This one is: **the invalidator
and the writer are different code paths** — so the guard is watching a door the bad value did not
come through.*

The pattern is a cache whose freshness is decided by a stamp naming the version of *the code that
produces the value*: `Genau::kVersionDetectLogicRev` here, but any `schemaVersion` / `cacheEpoch` /
`algoRev` is the same thing. It answers exactly one question — **"has the producer changed since
this was written?"** It cannot answer, and is not built to answer, **"did something other than the
producer write this?"**

Once a foreign value carries the current stamp it is not *hard* to find. It is **structurally
unreachable**: the reuse branch skips the producer entirely, so the only thing that could ever
disagree with the value never runs again. Not "until someone notices" — *ever*.

| # | the cache | what got in that the producer never produces |
|---|---|---|
| 1 | `ueVersion` + `versionDetectRev` | Avowed cached **504**; `DetectVersionDetailed` yields **503** for that PE. The 504 was the runtime `CMC::GravityDirection` raise, from a write path that no longer exists |
| 2 | `ueVersionUserOverride` | an override left on OCTOPATH after an A/B. ⛔ **It is checked BEFORE the rev-stamped cache and a rev bump does not clear it** — so even rev 6, the fix for #1, does not reach #2 |
| 3 | `DynOff::SOFTPTR_PATH` / `LAZYPTR_GUID` latches | latched under one UE version and still authoritative after `set_ue_version_override` changed the version they were derived from (fixed by clearing them in `Fern.cpp`) |

⭐ **THE ONE THAT MATTERS: the dangerous value is the CORRECT one.** Avowed's cached 504 was *right*
— the runtime ladder raises 503 to 504 anyway, so the badge a user saw was correct either way.
OCTOPATH's stale override was `418`, which is *also* the right answer for that title. Both were
invisible **because** they were right. A wrong value gets reported by somebody; a right value
written by the wrong path is permanent, and it silently disables re-derivation for that key
forever. So **do not look for wrong outputs. Look for values whose PROVENANCE is not the producer.**

**How to actually find one** (cheap → expensive):

1. **Run the producer and diff it against the cache.** This is the whole detector, and it needs no
   theory about what could have written the value. Avowed took one forced re-detect: seed the rev
   down, relaunch, read what detection says now. A disagreement is a poisoned entry by definition —
   the cache's own justification is *"the same binary always yields the same answer"*.
2. **Ask who else can write the field.** Grep every assignment to the cached name, not just the one
   in the producer. In #1 the second writer (`UE5_Init`'s ladder) is in a different file, runs in a
   different phase, and is correct — it just must never persist.
3. **Ask what would recompute a wrong value here.** If the honest answer is "the stamp", and the
   stamp is already current, the answer is **nothing**, and you have found the bug even without an
   instance of it.
4. **Check every LAYER of the lookup, not just the stamped one.** #2 is the trap: the override sits
   *above* the stamped cache, so the mechanism that fixes #1 cannot see it. A bump proves nothing
   about a tier it does not gate.

**Prevention, strongest first:**

* **Keep the second writer out of the store.** The 503/504/507/508 raise ladder is *correct* as a
  runtime-only correction and must not write back; that property is what made the rev-6 bump safe
  (re-deriving cannot lose a raise). Verify it rather than assuming it — that check was A4's, and
  it is what let rev 6 be argued for at all.
* **Record provenance in the record.** A `source: "detect" | "raise" | "user"` field makes a
  poisoned entry *self-identifying* and turns step 1 above from a live re-run into a grep. Cheap,
  and none of our three caches has it.
* **Write down what the stamp does NOT cover.** `Genau.h`'s rule said "bump when the logic changes"
  and was silent on the data, so three separate documents correctly refused to bump for a runtime
  ladder while a poisoned entry sat unreachable. The rule now carries a second clause: *also bump
  when a cached VALUE is found that the current detection cannot produce.*

⚠ **A bump is a blunt instrument and its cost is not the number you remember.** Rev 4 recorded
~0.35 s per cached game; the measured cost of rev 6 on DumperTest was **2 ms**, because a Tier-1
PE-VERSIONINFO hit never reaches the memory string scan that 0.35 s describes. Both numbers are
real and neither generalises — a version-stripped title still pays the scan. Quote the conditions
or do not quote the number (§1.6).

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

### 2.0b A subagent reads the WORKING TREE — do not run one while an experiment is applied

The crash-hunt workflow for the `0xC0000409` fast-fail got the mechanism exactly right and then
built a corroboration out of thin air: *"`dll/CMakeLists.txt:545` builds this target with
`/fsanitize=address` — I confirmed it from the binary's import table … yet CI printed no ASan
report."* Both halves were true **of my machine at that moment** and false of CI, because I had a
temporary `/fsanitize=address` edit applied while the agent was running. It then reasoned about why
CI's ASAN stayed silent for a binary CI never built.

**How to apply:** a subagent sees the tree as it is *now*, not as it is committed. Before launching
one, either revert experimental edits or say explicitly in the prompt which files are dirty and that
`git diff origin/main...HEAD` is the authority. Same rule for a temporary edit made *while* an agent
is already running — that is a race against your own reviewer.

It also predicted `ASAN_OPTIONS=detect_stack_use_after_return=1` would name the bug. It does not:
MSVC parses the option and does not implement it (§3.7b). **An agent's "how to confirm" is a
hypothesis too.**

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

### 2.2 Fixing a finding — four things the HIGH tier taught (audit #5, builds 2913–2932)

All eleven HIGHs are fixed. These are what generalised out of doing them.

- **Ask what the FEATURE promises before adopting the register's proposed fix shape.** AA2's entry
  said the fix "needs an identity witness (`InternalIndex`, `SerialNumber`)". Wrong question: the
  freeze is class-wide *by design*, so a slot recycled by the same class is a target, not a hazard —
  an identity check would have refused a correct write. The right predicate (class membership) was
  also far cheaper: two unused mailbox output fields instead of widening every entry. **A finding is
  evidence a defect exists; it is not authority on the repair.**
- **Make a wired-through field `required`, never optional.** AA1's mask had to cross five tiers, and
  `required` on the params record immediately caught a sixth the plan had missed (`ScoredPropertyRow`
  forwarded `PropSize` but not the mask — the same dropped-field shape, one line further down the
  same file). Optional would have silently kept the bug on that path.
- **Negative-control ONE change at a time.** Breaking all three freeze fixes at once produced output
  that could not be read — one break made an unrelated case fail for its own reason. Separately:
  AA1→4 failures, AA2→11, AA3→6, all detected. The same run exposed the harness aborting on its first
  failure and hiding every later case.
- **A predicate that guards X belongs WITH X.** Audit #5's Y7 wrote a struct-layout guard as a
  private helper inside a View; its two other consumers were in a Service, which cannot depend on a
  View, so the guard could not spread even in principle and the same dialog refused a bad layout for
  its inputs while accepting it for its results (AC2). Before writing a guard, ask which callers must
  reach it — if any of them is in a lower layer, the guard is already in the wrong place.
- **When you find a fix under-applied, expect its siblings to have TESTS defending them.** Three of
  the four sites closed in the AC2/AE10 batch had a green test asserting the defect, and one carried
  a written justification for it. That is *how* the sibling survived a fix: the code was wrong and
  the suite said it was fine. Read such a test as evidence about the **belief** at the time, not
  about the code — then check the belief's premise. (AE10's premise was that
  `IsGWorldAvailable` means "GWorld is resolved"; its own definition is "the AOB scan produced a
  slot address", which is not the same claim.)
- **When no test target compiles the file, measure the behaviour instead of arguing about it.**
  Nothing compiles `Frieren.cpp`, so AB2's async property was measured: 2.3 ms to return with the fix
  versus 3486 ms with the spawn reverted (`tools/probe_autostart_async.py`). Two reusable pieces —
  a Lua interpreter can execute the CE helpers against stubbed globals
  (`scripts/tests/freeze_helper_test.lua`), and Python `ctypes` can load the shipped DLL, time an
  export and read a data export. **Do not write that probe in PowerShell**: AMSI blocks a
  `LoadLibrary`/`GetProcAddress` P/Invoke script as malicious content.

### 2.3 Fixing the MED tier — what fourteen in one session taught (audit #5, builds 3016-3031)

The HIGH-tier lessons in §2.2 all held. These are the ones that only showed up at MED volume.

- **The fix-time sibling grep is not an optional polish step — it found MORE defects than the
  findings did.** Eight of the fourteen grew at least one extra site: V7 named one panel and six were
  unbound, AE8 named one probe site and four had the shape, and G1/U8/AE9/AF6 each gained a second.
  In no case did the finding's text hint at the sibling. Budget for it: the grep costs a minute and
  changed the size of two thirds of these commits.
- **A finding can be the SMALL half of its own defect.** U8 was "three open-coded FName decoders
  drop `Number`". Fixing them made the grep obvious, and the grep found ~19 further sites reading an
  *instance's* name the same way — a bigger, user-visible surface (the Instance Finder shows every
  instance of a class under one name, and its name filter matches against that same truncated
  string). When a fix is "route N call sites through one helper", ask what ELSE calls the thing the
  helper wraps.
- **Order two findings that touch the same fact.** G1 made `bOffsetsValidated` honest; X3 gave that
  verdict its first UI client. Doing X3 first would have shipped a banner driven by a flag that was
  itself lying — a green test and a visible feature, both meaningless.
- **Check your own hypothesis against the vendored engine before treating it as a finding.** While
  fixing U8 I was confident `ReadFName`'s `+4` Number read was wrong on case-preserving-FName games,
  because the tree derives FName *size* from that flag in eight places. `NameTypes.h:1258-1267` says
  ComparisonIndex, **Number**, then the case-preserving DisplayIndex — the 0x10 FName is wider at the
  TAIL. §2's "raw finder output is ~52% wrong" applies to your own leads too.
- **Refuse rather than silently substitute, when the honest fix is out of scope.** `force_field`
  carries a `double` end to end (`Solide::AddForce(..., double)`), so a wide `Int64Property` cannot
  be held exactly. Widening the wire is a DLL + protocol change; refusing with the substitute named
  is one function. The bad option is the one that was there — write a different number and say
  nothing.
- **Two states cannot express three outcomes, and the missing one is always the interesting one.**
  `double?` for a value prompt collapsed *cancel* and *rejected* into `null`, so a refused value was
  reported to the user as "you pressed Cancel". Whenever a nullable return doubles as an error
  channel, check what happens to the third case.
- **"Final cleanup" in a teardown comment is a claim, and it is usually wrong in a UI.** Live
  Walker's `OnDetached` said "final cleanup when the panel leaves the visual tree" and tore down six
  VM callbacks with no re-subscribe — but a TabControl detaches and re-attaches on every tab switch,
  and re-attach does NOT raise `DataContextChanged` (the VM is the same object). One round trip
  silently killed every scroll-to and the bookmark restore, with no error. The repair is to make
  subscribe/unsubscribe **idempotent** and call subscribe from both `DataContextChanged` and
  `AttachedToVisualTree` — idempotence is what makes two call sites safe, not discipline about which
  one runs.
- **Not every fix should get a unit test, and saying which is part of the work.** AF4's defect is in
  Avalonia's visual-tree lifecycle; a test calling the private handlers directly would assert *my
  model of when Avalonia raises them*, not the behaviour — a test that passes whether or not the
  product works. It went into the live-verification register instead, with the exact click sequence.
  Prefer an honest "verify this by hand, here's how" over a test that only pins the author's belief.
- **A capability gap is not a corruption, and they get different treatment.** A6 is confirmed —
  Force resolves an empty pool on inherited rows — but its status line already SAYS *"0 live
  instances of Actor matched"*. The fix needs a product decision that would change what a shipped,
  in-game-verified feature writes to. Correct the false comment (cheap, prevents the next
  re-derivation); park the behaviour and ask.

**Three C# numeric traps, all hit inside one fix** (AF6, deciding whether a `long` survives a
`double`). Each looks obviously right and each is wrong:

| expression | why it lies |
|---|---|
| `(long)asDouble != i64` | an out-of-range `double`→`long` **saturates** in .NET Core, so `long.MaxValue` reports itself unchanged — it fails on the exact input the check exists for |
| `asDouble.ToString("F0") != i64.ToString()` | a formatting question, and it answers differently at the ends of the range |
| `(decimal)asDouble != i64` | `double`→`decimal` **rounds to 15 significant digits**, so it rejects 2^53, which a `double` holds exactly |

Range-guard first (`>= -2^63 && < 2^63`), *then* cast back — exact, and it accepts 2^53 and -2^63
while rejecting 2^53+1 and `long.MaxValue`. **My first two test oracles had the same bugs as the
code and disagreed with a correct implementation.** When every concise way to compute the oracle
shares the code's failure mode, write the expectations out explicitly with the rule stated beside
them.

- **A test fixture can silently alias and report coverage it does not have.** AF1's loop wrote three
  different headers to ONE address in `NeuFakeMem`, whose `Put` **appends** and whose `Read` returns
  the FIRST region covering an address — so all three cases re-read the first value. It passed. The
  tell came from the negative control: 6 failures (3 × 2 assertions) when only 2 of the 3 inputs can
  actually slip through. **A negative control checks the test, not only the code** — an unexpected
  failure COUNT is a finding.
- **Assert the direction that could disable the thing.** Moving a probe (AE8) is an easy way to
  delete it. "Not called on the rejected path" passes for a probe that no longer exists, so the
  accepted path needs an assertion too.
- **Existing tests that break are doing their job — read them before editing.**
  `InitAsync_ParsesResponse` asserted the connect makes exactly 2 round-trips and X3 made it 3. The
  right move was to update the number and say why in a comment, keeping it exact so an accidental
  fourth is still caught — not to relax it to `>= 2`.

### 2.4 Re-derive the PREMISE, not just the location (audit #5 queue ②, build 3037)

Queue ② was four MED rows the audit's §3b called "already-vetted". Of its four premises, **two were
wrong**, and one was wrong in the direction that makes a fix *destructive*.

**AA4 asserted, as "CE source-verified", that `getAddress` raises on a missing symbol** — and
therefore that a `if fn == nil or fn == 0 then error(...) end` guard was dead code to be removed.
It is not. The Lua *wrapper* (`LuaHandler.pas:4374-4391`) genuinely does contain `lua_pushstring` +
`lua_error`, so reading only the wrapper makes the claim look proven. The resolver underneath it
decides, and it does not throw by default: `getAddressFromNameL` gates the raise on
`ExceptionOnLuaLookup`, which `TSymhandler.create` sets **FALSE**. Acting on the finding would have
deleted the only thing that turns "the DLL was never injected" into a message naming the export.

The rules this pays for:

- **A finding's LOCATION is usually right; its MECHANISM often is not.** All four rows pointed at
  real code. Two described what that code does incorrectly. Re-deriving cost ~20 minutes against a
  fix that would have shipped a regression.
- **Follow the call chain to the thing that DECIDES.** A wrapper that contains an error path is not
  proof the path is reachable. Stop at the function that owns the condition, not the first one that
  mentions the outcome.
- **A "kill rate" is not uniform across a finding's parts.** AA4's premise was refuted and its
  *consequence* ("this breaks CE's dissect for unrelated addresses") was confirmed — and the
  consequence was the part worth fixing. Judge the halves separately; a refuted premise does not
  close the row.
- **The measured impact was WORSE than filed, in the same batch that refuted two premises.** AA6
  said "a duplicate of the previous field". Running it showed a total DLL failure builds a
  45-element structure of empty rows, registers it with CE and logs "Struct created". Under-statement
  and over-statement live side by side; neither is the default.
- **A counted claim in a finding is a claim.** "14 call sites" was 19. Cheap to check, and a fixer
  working to the wrong number stops short.

**The general form:** treat a finding as *evidence that something is wrong here*, never as an
account of what. That is the same lesson §2.2 drew from the HIGH tier ("a finding is evidence a
defect exists, not authority on the repair") — this is its earlier half: it is not authority on the
diagnosis either.

### 2.5 A rig that RUNS the thing beats any number of assertions about its text

The C# suite could only assert on `ue5_dissect.lua`'s **source text**, so every one of its
assertions passed over a script that reported a total DLL failure as a successfully built structure.
A 40-check Lua rig stubbing CE's globals found 13 real failures in the unfixed file on its first run.

- **`lua` is installed on this machine** (`%LOCALAPPDATA%\Programs\Lua\bin\lua`, 5.4.6) and
  `luac -p` syntax-checks any script. Both rigs live in `scripts/tests/` and are documented in
  `scripts/README.md`. They are deliberately **not** in CI — a test step that silently skips when
  its tool is missing is the AD1/AD2 defect.
- **Write the rig BEFORE the fix and run it against the unfixed file.** The failure list is the
  finding, restated as behaviour. It is also the only honest way to claim a fix works.
- **Two load-order traps, both measured:** `ue5_dissect.lua` *returns* its table and defines no
  globals while `ue5_freeze_helper.lua` does the opposite; and CE's `vt*` constants must exist
  **before** the chunk runs, or mapped types silently get `Vartype = nil` while `EnumProperty`, the
  unknown-type fallback and the header rows still resolve — a partly-correct result is harder to
  diagnose than a uniformly broken one.
- **Keep the check COUNT independent of how far the code got.** A per-element assertion loop shrinks
  its own coverage as the fix improves things (48 checks → 39 on the first green run). Count into one
  assertion instead. Same family as AF1's aliasing fixture in §2.3.


### 2.5a The generated artifact must be shown to **PARSE**, and the tool that consumes it may not say so

§2.5's rule, paid for a second time and with a sharper edge: on 2026-08-22 the baked invoke script
had an **unescaped apostrophe** in a single-quoted Lua literal (`read it in CE's memory viewer`),
which closed the string early and made the whole `[ENABLE]` block a syntax error.

Three things about it are worth carrying forward.

1. **The existing test generated the broken artifact and passed on it.** Y13's
   `..._OnlyClaimsTheDumpWhenItReallyHoldsIt` produces this exact script and asserts substrings —
   all of which are present. Across 4,648 tests, **nothing asked whether the emitted Lua compiles.**
   Substring assertions cannot see a syntax error; only a parser can.
2. **Cheat Engine reports nothing.** Ticking the record leaves `Active` at `false` with no dialog,
   no output and no log line. From the outside it is indistinguishable from a checkbox that will not
   stay ticked. The way in was `autoAssemble(record.Script:match('%[ENABLE%](.-)%[DISABLE%]'))` from
   the Lua Engine, which *does* return the error — **when a record silently refuses to enable, run
   its own `[ENABLE]` text through `autoAssemble` and read the message.**
3. ⭐ **A grep for the offending character structurally could not find it.** The apostrophe was in a
   variable defined ten lines above the interpolation, so the first sweep — grep the emitting lines
   for `'s ` — came back **empty and looked like a clean bill of health**. The sweep that meant
   something *ran* 19 generators (17 simple + teleport's 13 actions + freeze) through a scanner,
   with apostrophes deliberately fed in via a class name, a property name and a Windows account
   named `O'Brien`. Same family as §2.3's sibling grep: a filtered grep measures what survived the
   filter.

The scanner itself has one non-obvious requirement: **it cannot be a quote count.** The generators
legitimately emit `-- ... when CE's resolver ...` inside comments, so it has to track `--` line
comments and `--[[ ]]` long brackets. It was written against the real broken artifact first and
shown to fire (1 line) and clear (0 after escaping that one apostrophe) before being ported into
`CeLuaQuotingTests`; both directions are pinned as tests.

### 2.5b A verification fixture can destroy the thing being measured — displace, do not relocate

Running MB3's teleport round trip, the displacement step used `TP to coordinates` with the UI's
default **0 / 0 / 0**. In `ThirdPersonMap` the origin is under the floor: the pawn fell, crossed
KillZ and was **destroyed**. The next record, `Recall marker 1`, then returned `code -3`
(`TP_ERR_NO_PAWN`) — a completely honest error that reads exactly like "Save silently failed".

- **DumperTest does not respawn the pawn**, focused or not. The only way back is relaunch,
  re-inject, and re-attach CE to the new PID.
- When the point of a step is a **round trip**, displace with something relative and bounded
  (`TP facing direction`, 100 uu) rather than an absolute coordinate. The measurement wants the pose
  to change and come back, and it does not care where.
- ⭐ The general shape: before reading an error as a defect, ask whether the **previous step in your
  own script** put the system into the state the error is truthfully reporting.


### 2.5c "54.7 MB" is not a verification — hash `dist/` against what was just built

The memory index's rule of thumb ("54.7 MB = AOT and shippable, 106.8 MB = non-trimmed") answers
*which kind* of build is sitting in `dist/`. It cannot answer *which build*, and on 2026-08-22 that
gap shipped a stale binary past a green run: `build.ps1 -Mode Publish` printed
`[OK] UE5DumpUI.exe (54.7 MB)` and exited **0** while the copy into `dist/` had silently failed
(a running `UE5DumpUI.exe` held `av_libglesv2.dll`). The size was right. The file was old.

- **`Copy-Item` is non-terminating.** A `ForEach-Object { Copy-Item ... }` pipeline reports nothing
  and leaves `$LASTEXITCODE` alone. Same trap as the `-ErrorAction SilentlyContinue` note in the
  PowerShell tool docs, but here nobody had even asked for silence.
- **The holder is usually one of ours**: a running UI, or an **injected game** holding
  `dist/UE5Dumper.dll` (that one bit `-Target DLL` twice in the same session). Kill both before any
  publish.
- **Native AOT is not byte-reproducible.** Four publishes of identical source gave four different
  SHA256s. So a hash proves *this copy landed*; it can never prove *two builds are the same build*.
  Do not try to use it that way.
- The fix in `build.ps1` is the shape to copy elsewhere: verify the **artifact**, not the operation,
  and keep the good output on disk when the verification fails.



### 2.5d A computer-use coordinate is a MEASUREMENT, and it expires when the layout reflows

Driving the Live Walker on 2026-08-22, two of three button coordinates captured earlier in the same
session were wrong by the time they were used — silently, because a click on empty chrome does
nothing and looks exactly like a click on a control that did nothing.

- **The toolbar reflowed mid-session.** `Find Refs` and `Related` appear once an object is loaded,
  pushing everything right of them left. The ▼ match-stepper moved from x≈547 to x≈521, so two
  "press ▼" actions actually clicked the **"2 matches" label**.
- The damage is not the wasted click, it is the **conclusion**: "the stepper stopped working after
  auto-refresh" was one sentence away from being filed as a defect, on an instrument that had never
  been shown to fire in that state.
- **Rule**: before a click that an assertion depends on, re-read the control's position from a fresh
  screenshot, and prove the click LANDED (a state change you can see) before reading anything into
  what follows. This is §1's "a check must be shown able to fail" applied to the actuator rather
  than the detector.
- The same session also had a toggle whose ON/OFF bookkeeping drifted, so a "control with the
  feature OFF" ran with it **ON**. ⭐ For any toggle, read its state back from the screen — the
  countdown, the highlight, the label — rather than tracking it in your head across a long run.


### 2.6 Verify the DLL through the PIPE, not the UI — and check `build_number` first

Learned 2026-08-16 closing the AB4 batch, which had been the top-ranked unverified item.

**The UI is a client, not the subject.** Every DLL-side batch on the verification register can be
driven by a ~50-line Python client speaking the pipe protocol directly: open the `UE5DumpBfx` named
pipe as `'r+b', buffering=0`, write `{"id":N,"cmd":...}` + `\n`, read newline-delimited replies and
match on `id` (async events interleave, so a blind readline will hand you the wrong message). AB4's
seven steps went from "needs a UI session and a human" to a few minutes of scripted scans. It is also
**stronger evidence** for a DLL-side claim, because it removes Avalonia as a variable — but say so
explicitly in the register, since it correspondingly proves nothing about the panel's own bindings.

**Three traps, each of which produces a confident wrong answer:**

- **A game with a deployed proxy IGNORES a fresh injection.** `injectDLL` returns `true`, and then
  the log says `DllMain AutoStart: pipe already exists (another UE5Dumper instance running) — skip`.
  The OLD proxy keeps serving the pipe. On 2026-08-16 that meant a build-**3122** proxy answering
  while the freshly built 3156 DLL sat loaded and inert in the same process — i.e. the batch would
  have "verified" a fix the running code did not contain. **Read `get_pointers.build_number` and
  compare it against what you just built, before believing any result.** To test a new build, replace
  the proxy in the game's `Binaries\Win64` and restart the game.
- **Proxy mode does not scan on load, and `init` does not make it.** A proxy is loaded long before
  the engine exists, so it deliberately starts the pipe server only
  (`DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)`). `init` returns
  `ok:true` in ~0 ms and scans nothing. The client must send **`trigger_scan`** and then poll until
  `gobjects_method == "aob"`. Skip it and every pointer reads `not_found`, which looks exactly like a
  broken AOB table on a game that in fact resolves fine.
- **A result cap silently turns an absence claim into a sample.** `max_results` truncation sets
  `deadline_hit: true`. "I paged 40,000 rows and saw no 1-byte fields" is not the same claim as
  "there are none". Re-run with a cap high enough to finish, confirm `deadline_hit:false`, and only
  then assert over the population. AB4's control step is decisive *only* because it completed —
  367,401 rows over the full 84,387-object pool, zero 1-byte rows, against 81,547 for the opposite
  predicate on the same data type. Same pool, same value, opposite predicate: that pairing is the
  evidence, not either number alone (§1.6).

**And the reason to do this work at all:** running AA4–AA7 step 1 for the first time surfaced
**AU1** — find-object-by-path had never worked, on any of three APIs that advertised it, because
`Ubel::GetFullName` emits `//Script/Engine/Actor` while every caller, doc and `.CT` writes
`/Script/Engine.Actor`. No finder had raised it across five audits, and one of the three was a stub
returning 0 that `dll-spec.md` documented as working. **Executing a shipped path finds defects that
reading it does not.**

### 2.7 Six MED fixes in one session — what the offline half taught (builds 3189-3195)

Learned 2026-08-17 closing Z1, Z3, AC1, AD5, AD6 and A1 with **no game available** and no ability to
grant new permissions. Every one was pinned by `build.ps1 -Target Test` alone. Five lessons, none of
which is about the individual bugs.

**a. A cap changes what "precision" means, and the answer is not the intuitive one.** Z3's fetch
sends each seed as its own query with its own 200-row envelope, filled in walk order. So a seed
matching 3,050 names is *fine* when 98% of them are the word you meant (`Item`, `Velocity`) — the
200 rows you get back are the right rows — and a seed matching 10,180 is *fatal* when 6% are
(`MP`, which is mostly `Component`/`Compression`/`Template`). **Volume is not the test; precision
above the cap is.** The filed fix ("cost is near zero, just add the keywords") measured CPU, which
really was free, and missed that the scarce resource was the envelope. Below the cap precision does
not matter at all, because everything fits.

**b. That calibration is FREE and needs no game.** Extracting ASCII identifiers from three shipped
game EXEs in `D:\UE_Analyze_data` gave **578,809 distinct names** in about a minute of Python — UE
registers reflected property names as literal strings, so it is a sound over-approximation of the
pool the DLL walks. That corpus turned "seems reasonable" into a per-seed number and changed the
shipped list. Same move as G8/G9/G11 modelling tier rules over the PE corpus: **when a rule is about
names or bytes, model it offline before writing it.**

**c. A test that calls the method under test directly can pass straight through the defect.** Z1's
existing coverage set the property and then `await`ed `RescoreAsync()` itself — which re-scores
unconditionally, so it was green with the bug fully present (§1.3, again). The fix is to await *what
the VM decided to do*, not what the test decided: an `internal Task? PendingRescore` the production
path assigns, `null` when it chose not to act. That distinction — "did the VM reconcile?" vs "did I
reconcile it?" — is the whole assertion.

**d. In an async VM test, a bounded wait is not a nicety.** Both Z1 regressions would *hang* the
suite under the pre-fix code rather than fail it, because the pre-fix code awaits something the test
never releases. `var done = await Task.WhenAny(work, Task.Delay(10s, TestContext.Current.CancellationToken)); Assert.Same(work, done);`
turns that into a failure. **A hang is not a test result** — and a negative control that hangs cannot
be distinguished from a hung machine.

**e. Before calling an existing guard blind, run your negative control past it.** AD6's write-up was
going to say `check_mailbox_contract.py` could not see a moved mailbox field. Measured: it *does*
fail on `className[256]`→`[255]`, via its surface hash. What it cannot do is compute an offset — so
it demands a *decision*, and a developer who bumps the contract version sails through with every
offset moved. The honest claim is "complementary", not "blind", and the difference took one command
to establish. The generalisation: **"the existing check misses this" is a claim to test, exactly
like the defect itself.**

**f. Register hygiene — the severity cell must stay a plain severity.** Writing AD6's re-tier as
`| **AD6** ✅ | MED→**LOW** |` silently dropped the row out of `check_audit_register.py`'s total
(290 → 289) while still reporting OK-looking numbers. The convention `AB7` already follows is a plain
`LOW` in the cell with *(MED→LOW on re-derivation, build N)* in the body. Watch the **total**, not
just the open count, when pasting the tool's headline.

-----

### 2.8 Most "needs Cheat Engine" rows do not — pull the emitted script out and run it under `lua`

The register has a large block of rows filed as CE-only. Two of them were closed on 2026-08-20
**without opening Cheat Engine at all**, and the same route should be tried before booking a CE
session for any of the rest.

**The route.** With **AOBMaker offline**, the UI's CE buttons fall back to copying a CE memory-record
XML to the clipboard. That XML contains the *real emitted* `[ENABLE]`/`[DISABLE]` `{$lua}` blocks —
the exact text the shipping build produces today, not a generator fixture. So:

1. drive the UI to the button (e.g. Teleport → Global Pointers → **Get GameEngine**);
2. read the clipboard, `html.unescape` it, pull `<AssemblerScript>`;
3. split the two `{$lua}` blocks and `load()` them over a table of **stubbed CE globals**
   (`getAddressSafe` / `registerSymbol` / `unregisterSymbol` / `readInteger` / `readQword` /
   `allocateMemory` / `sleep` / `getTickCount` / a `memrec` stand-in …);
4. assert on what the script *did*, not on what it says.

**Why it is stronger than it sounds.** The generator already has C# tests, but those assert on the
emitted *text*. Running it catches control-flow defects that read fine — `[SLOTSYM]`'s bug was
exactly that: on the slot path both arms of a guard were skipped and a trailing unconditional `dbg`
claimed success anyway. **Stubbing lets you neuter one primitive and prove the honesty branch is
reachable**: make `unregisterSymbol` a no-op and the script must say *"could NOT be unregistered"*
rather than *"unregistered"*. No amount of source-reading establishes that.

**Two practical notes.** `scripts/tests/*.lua` is the home and the convention (they are manual tools,
deliberately not in CI, because a standalone `lua` is not a declared dependency and a step that
skips quietly is worse than one run on purpose). And a pure helper needs no UI at all — `[STALEDLL]`
(b)'s size readout was closed by lifting `ue5_dllFileSize`/`ue5_dllSizeText` straight out of
`dist/UE5CEDumper.CT` and running them against the two real DLLs.

⛔ **Know what this does NOT cover, and say so on the row.** The stubs are not CE. Anything whose
question *is* CE's own behaviour stays CE-only — `[FREEZESTUCK]` step 3 asks whether CE's real
`TMemoryRecord.Active = false` behaves like the stand-in, and its row already says no offline test
can reach it. Closing the offline half is progress; claiming the row is done is not.

-----


### 2.8 A patch script's ENCODING is part of the patch — `utf-8-sig` writes a BOM you did not ask for

Every patch script in the 2026-08-21 session read **and wrote** with `encoding="utf-8-sig"`.
Reading with it strips a BOM; **writing with it ADDS one.** Eleven files silently gained a BOM they
never had, against a repo that is 427-of-446 BOM-less. Nothing failed — not the build, not 4,600
tests, not five CI gates — and it was found only by diffing the first three bytes of every file the
session had touched against the pre-session baseline.

Two rules, both cheap:
* **Round-trip bytes, not text**, whenever the edit does not need decoding: `p.read_bytes()` /
  `p.write_bytes()`. It cannot change an encoding, a BOM, or a line ending.
* When you must use text, **read with `utf-8-sig` and write with `utf-8`** — never the same codec
  for both.

⚠ The same session hit the sibling trap **four times**: a bash heredoc mangles backslash escapes, so
`b"...
..."` in a Python source becomes a real newline and the anchor silently stops matching.
`working-lessons` already said "prefer the Write tool"; the correction is that it is not a
preference. **Anything containing a backslash goes in a file written by the Write tool.**

⭐ And the reason both matter more than they look: each produced a change that **no test could
see**. A BOM does not break C#; a mangled anchor produces `occurrences: 0` and an assert, which is
loud — but its cousin, an anchor that matches something slightly different, would not be.

### 2.9 A new test that passes proves nothing until you have seen it fail — and the first two rules you write will probably be wrong

Worked example, same session, closing the L11 #7 offline half. The task was "the untick theories
never inspect a script containing a contract check". Three iterations, each caught by a control
rather than by reading:

1. Added the missing fixture to the list the theories iterate. Suite green. **The negative control
   (strip the untick out of `CeLuaHygiene.AppendBail`) reddened 13 rows and left the new fixture
   green** — because the theories fed by that list check for the *absence* of an immediate untick
   and for the deferred line's *exact text*, and neither can fail when an untick is simply MISSING.
   The rule that catches a missing untick is fed by a different list.
2. Wrote a focused test with the rule *"every `showMessage` must be followed by an untick"*, copied
   from the toggle theory. **It failed on correct code**: a momentary script's final failure message
   does not untick inline because it does not RETURN — control falls through to an unconditional
   deferred timer. Demanding an inline untick there is precisely the change that breaks the
   momentary shape.
3. Rewrote it as *"every bare `return` must have unticked"*. **Also failed on correct code**: the
   generator defines a local `_dumpHex` helper whose early `return` exits the FUNCTION, not the
   block. The toggle theory had never met that because its scripts define no helpers.

The rule that is actually true of both shapes: **a branch that REPORTS a failure and then LEAVES
must already have unticked** — a `return` preceded closely by a `showMessage`. That version passes,
and reddens under the same negative control.

Lesson: a test written by copying a sibling theory's predicate inherits that sibling's *unstated
scope*. Run the control first — a green new test is the least informative outcome available.



### 2.11 Arming a negative control is an EDIT — restore the bytes, never the substitution

*2026-08-22, during `[PARAMSSORT-2026-08-22]`. This did real damage to the tree and every gate
stayed green.*

The procedure that has been working all session is: break the thing, watch the check fail, put it
back. **The putting-back is where the danger is.** I armed a control by replacing
`>Hold this value<` with `>Apply<` in `en.axaml`, saw the assertion fire, and restored by replacing
`>Apply<` with `>Hold this value<`.

`en.axaml` already contained **five other** `>Apply<` strings — the Teleport panel's Move Speed,
Time Dilation, Gravity, Gravity-Direction and Coordinate-Library buttons. All five became
"Hold this value".

⛔ **Nothing caught it.** 4,640 tests passed. `check_axaml_strings.py` passed — it verifies that
every referenced key *exists*, which is untouched by rewriting a key's *value*. The four other CI
gates passed. Only `git diff` knew, and only because I went looking after a `grep -c` returned 6
where I expected 1.

**The rule.** A control is armed by mutating a file you did not intend to change, so restore it the
way you would restore any accident:

- `git checkout -- <file>` when the file has **no** intended edits in the working tree. Byte-exact,
  no reasoning required. This is almost always the right answer.
- Otherwise snapshot the exact bytes first (`b = p.read_bytes()`) and write them back
  (`p.write_bytes(b)`).
- **Never** reverse the substitution. `A→B` then `B→A` is only sound when `B` did not already occur,
  and you rarely know that. If you must, scope the replacement to a count (`replace(a, b, 1)`) or to
  the one line — and then still verify with `git diff`, not with a `grep -c` of what you expected.

⭐ **The generalisation, which is the part worth carrying:** `grep -c` told me "6" and I noticed only
because 6 ≠ 1. Had the file contained one stray `>Apply<` instead of five, the count would have read
2 and looked close enough to right. **`git diff --stat` after restoring a control costs nothing and
does not depend on guessing the expected number** — which is the same failure this file's §1 warns
about in the other direction: *a number recorded without its conditions is not a measurement*, and a
count checked against an expectation you formed before the edit is not a verification.

▶ Also noticed and left alone: **this document has two sections numbered 2.8.** Renumbering would
break inbound references, so it is flagged here rather than silently changed.



### 2.12 A sequence of structural edits compounds — anchor each one on what it MATCHES, not on what you meant

*2026-08-22, same session as §2.11, same afternoon, different mechanism. Both were mine.*

Three edits to `docs/pending-verification_zh-TW.md`: delete section AC17, delete section AF21,
rewrite section V8. Each was written as *"find the heading, walk BACK to the `-----` above it, cut to
the next heading"*. Individually correct. In sequence, wrong.

Deleting AC17 took the separator above it — which was the separator **below its neighbour Y12**. Now
Y12 and AF21 were adjacent with nothing between them. The AF21 edit then walked back looking for a
`-----`, found the one above **Y12**, and deleted Y12 as well.

⛔ **The bug is `rindex` on a shared delimiter.** A separator does not belong to the section after
it; it belongs to the *gap*, and the previous edit may already have consumed it. **Delete forward
only** — from the heading to the next heading — which takes the section together with the separator
that trails it and cannot reach a neighbour:

```python
m = re.search(r"^(?:### |## )", text[i + 4:], re.M)   # next heading, H3 or H2
end = i + 4 + m.start()
```

⭐ **What caught it was the DERIVED COUNT, not review.** The file states its own invariant —
`grep -c '^### '` minus the three subsections must equal the table's total — and after the second
delete it read 47 against a table saying 48. One heading unaccounted for. Nothing in the prose
looked wrong; the deleted section was 22 lines in the middle of a 60 KB file.

▶ **So: give a structural document an arithmetic invariant, and re-derive it after every edit.**
This one already had one, written down by someone who had been bitten before, and it paid for itself
within the hour. `git diff | grep '^-### '` is the confirming check — it names exactly which
headings a change removed, which is the question you actually care about.

▶ And when incremental edits have already compounded, **reset and redo them in one pass** rather
than patching the patch. `git checkout -- <file>` was safe here only because everything earlier in
the day was already committed — which is the other half of why the commit-early habit is worth
keeping.



### 2.13 A DEFERRAL REASON AGES WORSE THAN THE FINDING IT DEFERS

*Measured over 2026-08-21/22. Six rows closed; **five had sat on a stated blocker that was simply
false.*** The finding itself was fine each time — what had rotted was the sentence explaining why it
could not be checked yet.

| the deferral said | what was actually true |
|---|---|
| "only a real mount point can verify it" (`AC17`/`VOLUMEROOT`) | a cross-volume `mklink /J` junction separates the volumes just as well, and needs no elevation |
| "the deps listing shows a breakage" (`PROXYDEPS`) | it shows **empty translation units** — six 527-byte objects against a smallest-real 10,985 |
| "cannot be visually verified in an unattended session" | computer-use drove all five steps, including hand-corrupting a settings file to a value no control can produce |
| "needs a real scaling change, the one row a script cannot do" (`AF21`) | the desktop is *permanently* at 225%; and the row's own gesture (hang it off the **right** edge) provably **cannot** expose the defect, so following it yields a confident false PASS |
| "needs a game with a `UDataTable` over 64 rows" (`V8`) | five existing tests already pin every step's substance; only "is it rendered" is left |
| "the dxgi resolver's SRWLOCK across `LoadLibraryW` — audit #4 B43 removed it from winmm only; **deliberately out of scope**" (2026-08-26, written into the audit doc's own §8.6 the same day) | **It was the live defect, not a latent one.** The very next build turned the crash into a HANG, and the dump named that exact lock: the main thread in `ZwWaitForAlertByThreadId` on an SRWLOCK at `dxgi.dll+0x2ACC90`, with the `AcGenral`/`apphelp` frames on the stack **twice**. Our own `LoadLibraryW` re-enters us on the same thread (the loader raises `SE_DllLoaded`, the shim resolves the proxied name back to US), and SRWLOCK is non-recursive. B43 had already written the reason down for the twin — the deferral's mistake was rating a *sibling's* proven finding as theoretical here. **Cost: a whole extra round trip through a live game.** |
| "needs CE installed under `%ProgramFiles%` with the app non-elevated — no unattended session can stage it" (`X12`, 2026-08-24) | **wrong in BOTH directions.** `TryFindCheatEngineDirAsync` resolves CE's folder from the **running `cheatengine*` process's own path**, so any CE we start decides the target — a 97 MB copy we own is as real as the installed one. *And* the installed `C:\Program Files\Cheat Engine\autorun` is **writable non-elevated on this host** (measured with a probe file), so the prescribed setup would not have reproduced the denial anyway |

⭐ **Why it rots in exactly this direction.** A deferral is written at the moment of *least*
knowledge about the thing — right after diagnosing the defect, before anyone has tried. It then
travels attached to the row as though it were a measured fact, and every later reader treats it as
one, because it is sitting next to a diagnosis that IS well-evidenced.

▶ **So: re-derive a deferral's premise before accepting it, exactly as §2.4 says to re-derive a
finding's premise before fixing it.** Ask what specifically would have to be true, and whether it
has ever been checked. Cheap: all five above collapsed in minutes.

⚠ **And the sharpest version — a blocker can be worse than wrong, it can be actively misleading.**
`AF21`'s row named a gesture that exercises the *permissive* side of the guard, so a careful tester
following it exactly gets a pass and learns nothing. When a row prescribes a specific manipulation,
check that the manipulation lands in the band where the two builds actually differ.

⭐ **Two capability beliefs retired the same day (2026-08-24), both of which had been silently
shrinking what counted as automatable:**

* **Avalonia's top-level menu items ARE clickable by computer use.** A carried note said the header
  opens but the item click runs nothing, which would have made every `Tools ▸ …` row human-only.
  Measured on `Tools ▸ Install CE autorun Helper`: it fired on the first attempt, twice, and drove
  a `SaveFileDialog`. ▶ **Re-test a carried UI-capability claim before letting it reclassify a row.**
* **`wmic` is GONE on this Windows build (26200)** — `subprocess` raises `WinError 2`, which reads
  like a missing script rather than a missing OS component. Enumerate processes with a Toolhelp
  snapshot + `QueryFullProcessImageNameW` (see `tools/verify/x12_ce_autorun_denied.py`), which is
  also what the app's own `GetRunningProcesses` does, so the rig and the code agree by construction.

⚠ **Staging a "not writable" target does NOT require an ACL edit** — and reaching for `icacls` on a
real install is both a security-settings change and unnecessary. The write under test is a
`File.WriteAllTextAsync` onto a fixed file name, so a **read-only file** raises the very
`UnauthorizedAccessException` a permission denial raises. One attribute, instantly reversible.
⛔ But check WHICH mechanism the code actually reaches: for `AE20` the sibling trick — a **share
lock** — was silently defused, because that code re-plans from disk first and a file it cannot open
simply leaves the plan.



### 2.14 A verification row's stated PASS is a HYPOTHESIS about where the defect lives — check it against the fix

*Three rows in one afternoon, 2026-08-22, all three the same shape.* Each row was written by
whoever fixed the defect, at the moment they were most sure what it was. Each names a check that is
either satisfiable without exercising the fix, or points at the wrong half of it.

| row | what it told you to assert | where the defect actually lives |
|---|---|---|
| `B19` locked log | "the locked file is still there" | **ORDER.** That assertion is true under the fix, under the defect, *and* when the sweep never ran. The fix was one shared `error_code` ending the loop, so the witness is the aged file *after* the lock in enumeration order. |
| Dump Explorer cross-game gate | walk two games and watch the refusal | The gate was **already** pinned by five headless tests. The only unpinned thing was the **log line** the row names in passing — and the status text it does pin is transient, overwritten by the next action. |
| `AF7` `budget_hit` | "the reply has `budget_hit`" | **A tautology.** The DLL writes the key unconditionally, so it is present on bytecode-path replies where the flag can never be true. Meaningful only when `method == "disasm"`. |

⭐ **The pattern: a row records what the author was LOOKING AT when they fixed it, not what
distinguishes fixed from broken.** Those coincide only by luck. So before running a row, do the
thing you would do before fixing a finding (§2.4) — **read the fix, and ask what state would make
the stated check pass on the BROKEN build.** If you can name one, the row is wrong and the check
you actually want is somewhere adjacent.

▶ **Three practical tells**, all cheap:
1. **Would the assertion also hold if the feature never ran?** ("the locked file survived", "no
   error appeared") → you need a positive arm that proves it ran.
2. **Is the asserted value written unconditionally?** grep the emitter. If it is, the row is
   asserting the schema, not the behaviour.
3. **Does the row name a diagnostic, a log line, a status string in passing?** That aside is often
   the only part not already covered — the mechanism usually has tests and the *reporting* usually
   does not.

⚠ **And the correction is cheap while the mis-run is not**: all three rows had a better check
available for the same effort or less, and two of them would have produced a confident PASS that
measured nothing.


### 2.10 An absence proves nothing until the CHANNEL is shown to carry the thing

A11 step 6's PASS was recorded 2026-08-20 as *"PASS, and non-vacuously"*, with this reasoning: the
refine was shown to have run over 11 real candidates, so the absent `Refine re-anchor:` line must be
a decision rather than an empty pass. The reasoning is sound and it is **irrelevant**. The rig was
grepping `scan-0.log`; `Refine re-anchor:` is emitted from `Aura.cpp`, whose `#define LOG_CAT "OARR"`
Sein routes to **`offsets-0.log`**. The line could not have appeared in the file being read no matter
what the code did — and the row's own step text named the same wrong file, so the rig inherited it.

⭐ **The write-up controlled for the STIMULUS and not for the DETECTOR.** A grep of the wrong file
returns zero for both "the code did not do it" and "I am reading the wrong channel", and no amount
of evidence about the stimulus separates those.

The fix is one assertion, and it belongs in every absence-shaped check: **before treating an absence
as evidence, prove the channel carries that category's traffic at all.** The rig now aborts unless
`offsets-0.log` contains an `[OARR]` line.

⚠ Concretely, for this repo: `docs/log-verification-checklist.md` already says grep by FORMAT STRING
rather than line number. The missing half is that a format string tells you nothing about WHICH FILE
it lands in. Four categories (`SEETHRU` / `Grausam` / `SENSE` / `PROXY`) fall through to
`init-0.log` rather than to a file named after them.

⭐⭐ **AND THE OBVIOUS FIX FOR THIS IS ITSELF A TRAP — it caught me the same day.** Having found that
A11's marker is `[OARR]` because `Aura.cpp` declares `#define LOG_CAT "OARR"`, I applied the same
reasoning to A12's marker in the SAME FILE and moved a working rig to the wrong log. The two lines
sit a few hundred lines apart and go to different files:

| marker | call | tag | file |
|---|---|---|---|
| `Radar: Refine re-anchor:` | `LOG_INFO(...)` — takes the file's `LOG_CAT` | `[OARR]` | `offsets-*.log` |
| `RefineGroup re-anchor:` | `Sein::Info("SCAN:grp", ...)` — **explicit** | `[SCAN:grp]` | `scan-0.log` |

**`#define LOG_CAT` is a DEFAULT, not the answer. Read the CALL.** `Aura.cpp` alone has 93 `LOG_*`
calls and 22 explicit `Sein::` calls. `Sein.cpp`'s table resolves a category to a file; it cannot
tell you which category a given line passes — only the call site can.

▶ The cheap way to settle it without reading any of this: run the thing once and
`grep -l "<marker>" *.log`. One command, no inference. That is what finally decided it, after two
rounds of reasoning from the source produced two different wrong answers.

### 2.15 A SENTINEL WITH NO CONSUMER-SIDE GUARD IS A LATENT BUG WAITING FOR A SECOND PRODUCER

`[GWORLDACTORCHAIN-2026-08-26]`, build 3359. Audit #5 F8/F9 established that a level actor is
reached through its `Outer`, not through any field, so the hop has **no offset**. The codebase
encoded that as `BreadcrumbItem.FieldOffset = -1`, stamped by `PathStepToBreadcrumbs`, and one
consumer — the AA-script `gworldWalkable` gate — tested `FieldOffset >= 0` and refused. That looked
finished. It was two-thirds of a mechanism, and both missing thirds shipped a wrong address to a
user:

- **No second producer had it.** `PopulateFromWorld` builds the *same hop* for the "Start from
  GWorld" list and published `Offset = 0` — a positive claim that the actor is at `[UWorld + 0]`,
  which is the world's **vtable pointer**. The correct copy was the OLDER one, so a "did I update
  every site I touched?" review at fix time would have passed: the site that needed changing was
  not one the fix touched. The question that finds this is **"who else builds this kind of
  hop?"**, not "what did I just edit?" — §2.3's sibling grep, aimed at the CONCEPT rather than at
  the diff.
- **No emitter validated it.** `ProjectBreadcrumb` formatted the offset with `$"+{off:X}"`, so a
  `-1` that *did* reach it printed **`+FFFFFFFF`**. That path was live the whole time via
  Locate-in-GWorld and had simply never been exercised. A sentinel the emitter cannot survive is
  not a sentinel; it is a second bug parked next to the first.

**The repair shape, and why the throw is not defensive noise.** The producer now stamps the
sentinel, a shared `AnchorAtLastUnchainableHop` strips such hops before emission, *and*
`ProjectBreadcrumb` **throws** if one still arrives. The throw is what makes the invariant real:
with the strip removed, the export fails loudly with the hop named, instead of copying a table that
resolves into the executable image. ⭐ It is also **testable and demonstrably reachable** — removing
the strip turned it red in the production path, not just in a unit test — so it is an invariant,
not the `elseif false` kind of dead branch §1.9 warns about.

⚠ **Do not "simplify" this to one representation.** `LiveFieldValue.Offset` deliberately stays `0`
on those rows (bookmarks and the same-layout row-reuse path key on name+offset) while a separate
`HasNoParentOffset` carries the fact. Forcing `Offset = -1` would have been the tidier-looking
change and would have silently repointed two unrelated features.

⚠ **The user's first symptom was the cosmetic half, and it was still the right thread to pull.**
The report opened with *"lots of fields with offset `0x0` appeared after the fix"* — true, connected,
and not the bug. The F9 fix is what first *populated* that list; every row it added carried the
fabricated zero. A cosmetic complaint that arrived **with** a correctness complaint usually shares
a cause with it.

### 2.17 "A fix that landed in one of its two copies" — three instances in one day

2026-08-26, builds 3359-3362, three unrelated modules:

| | the fix that WAS applied | the copy that was NOT |
|---|---|---|
| `[GWORLDACTORCHAIN]` | `PathStepToBreadcrumbs` stamps the no-offset sentinel | `PopulateFromWorld` builds the same hop and does not |
| `[PERFDENOM]` | `busyMs` excludes the probe's own call, with a comment saying why | `dispatches`, **two lines below it**, still reads the global counter |
| `[SANEPROPS]` | the gap-fill site treats the bound as a WORK cap | the instance / cache sites treat the same constant as a PLAUSIBILITY test |

⭐ **The grep that finds these is aimed at the CONCEPT, not at the diff.** §2.3's sibling grep is
"what else calls the thing I just changed?" — that is necessary and it would have missed all three,
because in each case the un-fixed copy is **older than the fix** and the fix never touched it. The
question that works is *"who else builds this kind of hop / answers this question / uses this
number?"*, asked before the commit and again when the finding is written up.

⚠ **The tell is a comment that explains why one site is careful.** All three had one. A comment
justifying a subtlety at site A is evidence that site B, which does the same thing without the
comment, has not been thought about. When you write that comment, grep for the shape it describes.

⚠ **And the un-fixed copy's own justification may cite the fixed one.** `Ubel.cpp`'s WalkClassEx
gate says the trade is bounded *"because WalkInstance already hard-fails on this exact predicate"* —
true, and it is exactly why P3R's `USaveGame` classes were invisible everywhere at once. A
justification that leans on a sibling's behaviour breaks silently when the sibling's premise is
wrong for both of them.

### 2.15a The LOG TREE is a corpus — grep it before concluding a scenario needs a game you do not have

`[GWORLDACTORCHAIN-2026-08-26]`. Verification row 5 needed an `ok_via_level` (streaming /
World-Partition) recovery path. Three independent finder agents concluded "this needs a different
title" and stopped. It had **already been reproduced on this machine five days earlier**, and the
line was sitting in `%LOCALAPPDATA%\UE5CEDumper\Logs\TQ2-Win64-Shipping\ui-view-20260823-091415.log`:

```
LocateInGWorld: reach mode, 2 hop(s) | BC=GWorld(P,0x0,68E0) > (world level)(S,0xFFFFFFFF,A960)
                                                             > (level actor)(S,0xFFFFFFFF,FA60)
```

⭐ **Every log folder under `Logs\` is a record of a scenario that has occurred on real software
here.** Before writing "needs a title we do not have" / "cannot be reproduced on demand", grep the
whole tree for the *symptom string*, not the game name. The 21-day retention sweep means the corpus
is a rolling few weeks deep — which is usually plenty, and is also a reason to look **now** rather
than deferring the row.

⚠ **Two second-order lessons from the same find.**

- **A finder agent asked "which game has feature X?" will answer from world knowledge.** It has to
  be pointed at the evidence: *"grep the log tree for this exact string"* is a different instruction
  from *"which title would exercise this?"*, and only the first one found it. Put the symptom
  string in the prompt.
- **The absence of a symptom is not evidence when the code path never ran.** Three lenses noted
  `+FFFFFFFF` appears 0 times in the new session and correctly refused to call that a pass —
  `find_path_from_gworld` was never sent, so the generator was never reached. That is §2.10 (an
  absence proves nothing until the CHANNEL is shown to carry the thing) applied to a whole feature
  rather than to one log line.

⚠ **And the row it closed had never been executed at all** — not before the fix, not after. The
`ok_via_level` spine had been *produced* on 2026-08-23, but nobody had pressed Copy CE XML on it, so
the `+FFFFFFFF` it would have emitted was latent for as long as the marker had existed. When a
defect is "the producer marks it and no consumer handles it" (§2.15), the consumer half is
**usually unexercised by construction** — that is why it survived. Budget a live run for it
specifically; a green suite says nothing about a path no user has walked.

### 2.16 An export row rarely needs Cheat Engine, and rarely needs the game that was reported

Two independent shortcuts, both used to close `[GWORLDACTORCHAIN-2026-08-26]` rows 1–4 in about ten
minutes. Sibling of §2.8 (pull the emitted Lua out and run it under `lua`) — same instinct applied
to the OTHER two costs of a live row.

- **`clipboardRead` is a granted computer-use flag, so an emitted table can be READ AS BYTES.**
  Drive the UI, press Copy CE XML, call `read_clipboard`, and count. That turned "does CE resolve
  this correctly?" — a screenshot-and-squint job — into *of 382 `<Address>` entries, exactly one is
  absolute and it is the actor's own; the world address appears 0 times*. CE was never launched.
  ⭐ The measurement is also **stronger** than the CE one: a screenshot shows the rows that fit.
- **Substitute the fixture for the reported game when the defect is provably engine-independent.**
  The report came from P3R (UE 4.27, not granted, a purchased title); the defect lived in a
  ViewModel that consumes the DLL's `walk_world` reply and never reads the engine version. The
  already-granted **DumperTest** (UE 5.4) exercises the identical code. No new grant, no launch of
  someone's game. ⚠ **State the reason in the evidence** — "ran it on the fixture instead" is only
  legitimate when you can name the line that makes the two hosts equivalent, and the row must still
  say which sub-checks the substitute could NOT reach (row 5 here needs World-Partition streaming,
  which DumperTest has none of, so it stayed open).

⚠ **What the substitution does NOT excuse**: check the host actually booted. `24,479 objects` is
what made the run meaningful — §3.w's dead-engine trap reports coherent zeros through injection,
pipe and scan alike.

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

### 3.6 A heap-corruption dump names nothing — re-run it under page heap

`0xC0000374` (STATUS_HEAP_CORRUPTION) is raised by the NT heap manager at the **next heap
operation**, not at the write that broke it. So the faulting stack is the **detector, not the
culprit**, and reading it harder does not help. Ours (build 3122, UI CTD after a Copy CE XML) showed
ntdll's heap-error path on the UI thread with Skia/DWrite/user32 frames below — enough to say "native
code on the render thread", which is motive and opportunity, never the act.

**What actually names it:** full page heap, which puts a guard page after every allocation so the
overrun faults **immediately, in the guilty module**.

```
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\UE5DumpUI.exe" /v GlobalFlag /t REG_DWORD /d 0x02000000 /f
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\UE5DumpUI.exe" /v PageHeapFlags /t REG_DWORD /d 0x3 /f
```

The next crash came back as `0xC0000005` at `libSkiaSharp.dll+0x102B8D`, WER event **`AutoVerifierV2`**
(not `APPCRASH`) with `verifier.dll` on the stack. Delete the key to disable; leave it on and
everything is slow and memory-hungry, which is the tool, not the build. **`gflags` and Application
Verifier are NOT installed on this machine** — the registry route needs no tools, and it is a system
setting, so the maintainer runs it.

**Ruling our own code out is structural, not a search.** The UI project has no `AllowUnsafeBlocks`,
no `Marshal.AllocHGlobal` / `Marshal.Copy`, and its only `stackalloc`s are bounded `Span`s — and a
stack overrun is `0xC00000FD`, a different code. That took one grep and eliminated ~277 files.

**CHECK THE PACKAGE FOR A PDB BEFORE DECLARING A NATIVE CRASH UNSYMBOLIZABLE.** This was written up
twice saying "there is no PDB for `libSkiaSharp`, so the faulting function is unknown" — an
assumption, never a `dir`. `SkiaSharp.NativeAssets.Win32` **ships `libSkiaSharp.pdb`** next to the
DLL in the NuGet cache (every version), and so does `HarfBuzzSharp.NativeAssets.Win32`. Look:

```
%USERPROFILE%\.nuget\packages\skiasharp.nativeassets.win32\<ver>\runtimes\win-x64\native\
```

Then symbolize the RVA — and use the **x64** binary, not the one a recursive search finds first:

```
"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\Llvm\x64\bin\llvm-symbolizer.exe" --obj=<dll> --demangle --functions=linkage --relative-address
```

(`VC\Tools\Llvm\` holds `ARM64\` *and* `x64\`; the ARM64 copy sorts first and will not run on an x64
host.) Feed it `CODE 0x<rva>` on stdin. It prints the whole INLINE chain, which is the useful part —
ours resolved a bare offset into `TArray<SkPathVerb>::size` → `SkSpan` → `SkPathBuilder::verbs` →
`SkPathBuilder::computeFiniteBounds`, i.e. path geometry, which exonerated HarfBuzz in one command.
**Confirm binary identity first** (byte size of the shipped DLL vs the package payload) or you have
symbolized a different build.

**Reading the dump without symbols:** `py -m pip install minidump`, then walk the faulting thread's
stack region and attribute every 8-byte-aligned qword to a loaded module's `[base, base+size)`. It
over-reports (stale frames, spilled pointers) but **cannot miss the guilty module**, which is the
only question at that stage. Two gotchas in that library: it logs a PEB parse failure that is
harmless, and `ExceptionRecord.ExceptionCode` is a **str-valued** Enum with no entry for
`0xC0000374`, so it degrades to `EXCEPTION_UNKNOWN` and `int()`/`"%X"` both raise on it — read the
real code from the Event Log instead.

**Cross-check the module base two ways** before trusting an offset: `0x7FFEE53C2B8D − 0x102B8D` and
an unrelated stack frame `0x7FFEE5453602 − 0x193602` both gave `0x7FFEE52C0000`.

**And read the AV subtype from the dump, not from WER.** `ExceptionInformation[0]` is `0` = read,
`1` = write, `8` = DEP. WER's `P9` is not that field; taking it for one turned a read into a write in
the first write-up.

### 3.6a Reading a WER crash on THIS machine — six traps, two of which cost a wrong culprit

From the 2026-08-26 dxgi/AppCompat investigation
([audit-2026-08-26-dxgi-appcompat-crash.md](audit-2026-08-26-dxgi-appcompat-crash.md)). §3.6 above is
about *making* a dump name the culprit; this is about *reading* one that already exists.

**⚠ `UploadTime` is not `EventTime`, and the folder mtime is neither.** The WER event surfaced in the
Application log dated **2026-08-26 20:52**, the `ReportQueue` folder was stamped the same, and the
crash was actually on **2026-08-23 09:22**. `EventTime=134319217554888574` is a FILETIME —
`datetime(1601,1,1) + timedelta(microseconds=v//10)`. Getting this wrong first dated the crash to
"today", and then — because ReShade was being installed at the real timestamp — attributed it to
**ReShade instead of to us**. Decode `EventTime` before you decide whose crash it is.

**⭐ `…tmp.appcompat.txt` in the WER cab is what PROVES which DLL was loaded.** It lists every
`MATCHING_FILE` with `SIZE`, `LINK_DATE`, `PRODUCT_NAME`, `ORIGINAL_FILENAME` and `EXPORT_NAME`. Ours
read `dxgi.dll SIZE=2891264 PRODUCT_NAME="UE5CEDumper" BIN_FILE_VERSION="1.0.0.3315"` alongside
`dxgi0.dll … PRODUCT_NAME="ReShade"` — settling both "was it ours" and "which build" in one file, and
overturning the misattribution above. Look here before reasoning from the module list.

**⚠ Resolve dump RVAs against the DUMPED build, never the current one.** `dist/proxy/dxgi.dll` had
`SizeOfImage 0x2CB000` against the dump's `0x2CA000`; the crash return address `0x1B43E4` landed
**mid-instruction** there and the "identifying" disassembly was meaningless. `out/proxy-backups/`
keeps timestamped copies of every deployed proxy — `Avowed.dxgi.dll.20260823-212124.bak` was a
byte-exact match, and against *it* the same RVAs resolved to the exact export thunk and to the
instruction after its `call ResolveAll`. **Check `SizeOfImage` before trusting any RVA mapping.**

**You can name the immediate caller with no symbols at all.** Disassemble the faulting function's
prologue, add up its frame (`RtlAllocateHeap` = 7 pushes + `sub rsp,0x140` = `0x178`), and read the
qword at `rsp+frame`. That gave `dxgi.dll+0x1B43E4` — our module — in one step, with no PDB.
Registers survive too: the same prologue showed `mov rdi,rcx` / `mov r13,r8` / `mov ebx,edx`, so the
context's `Rdi=0 Rbx=0 R13=0x20` read straight off as `HeapAlloc(NULL, 0, 32)`.

**⚠ ntdll's export table is far too sparse to symbolize by nearest-export.** `ntdll+0x17AD3C`
resolves to `RtlNtdllName+0x6F9C`, which is a **`.rdata` data pointer**, not a function. Use the
`.pdata` (exception directory) function table to tell code from data and to get real function
bounds — a hit *inside* `[begin,end)` is a frame, one outside is spilled data. Same trick found
`apphelp!SE_DllLoaded` (a real export, RVA `0x1F790`) live on the stack, which is what tied the crash
to the shim engine.

**⚠ Minidump module order is stable in the head and ±1 later.** Across all three Octopath dumps
`apphelp[4] / AcGenral[5] / shell32[20] / version.dll[21]` were identical, but `winmm` was `[31]`
or `[32]` and `dxgi` `[37]` or `[38]`. Order is usable for "who was loaded during shim init"; it is
**not** usable for a fine-grained argument resting on one index. (A scratch script that had been
overwritten by a subagent printed a *different* order and nearly retracted a correct inference — see
the `dis.py` note below, and §2.0b.)

**⚠ `wevtutil` from git-bash needs MSYS path conversion off.** `wevtutil qe Application /c:5` fails
with `參數過多` / "too many arguments" because MSYS rewrites `/c:5` into a path. Prefix
`MSYS2_ARG_CONV_EXCL='*' MSYS_NO_PATHCONV=1`. Output is CP950-mojibake in this shell but the ASCII
fields (`P1`..`P10`, paths, hex codes) are intact — decode `Report.wer` as **UTF-16LE** for the
readable version, and `sys.stdout.reconfigure(encoding='utf-8', errors='replace')` or python raises
on the BOM. PowerShell is not an option here (§3.8).

**⚠ Never name a scratch script after a stdlib module.** `scratchpad/dis.py` shadowed `dis`, which
`capstone` imports via `inspect`, producing
`partially initialized module 'capstone' has no attribute 'Cs'` — a circular-import error that reads
like a broken capstone install. Same class as `code.py`, `types.py`, `select.py`.

### 3.7b A DETACHED thread must not capture a stack local by reference — and ASAN will not tell you

`dll_helpers_test` died on CI with **`0xC0000409` (STATUS_STACK_BUFFER_OVERRUN), no output at all**,
and passed locally every way it was run: Release, Debug, `-Clean`, and under ASAN. Two of three CI
runs on the same tree passed. The cause was found by **reading**, not by any tool:

```cpp
std::atomic<int> ran{0};                 // this function's stack frame
{
    Routine::SafeThread t;
    t = std::thread([&] { for (int i = 0; i < 50; ++i) { ran.fetch_add(1); sleep_for(1ms); } });
}   // ~SafeThread DETACHES (Routine.h:82) -- that is the behaviour under test
```

`sleep_for(1ms)` is quantised to Windows' ~15.6 ms tick (§4.2 has the same fact for CE's `sleep`), so
the worker lives **~780 ms** while the function returns in **~25 ms**. For the remaining ~750 ms it
does `lock xadd` into a **reclaimed stack frame** — through the frames of every later test and then
the CRT exit path. Land one of those on a `/GS` cookie and the process fast-fails. The status name
is exactly right for once, and **the intermittency is structural**: which byte gets hit depends on
stack layout and timing, so unrelated edits (27 new assertions) can surface a latent defect.

**Three traps, all of which cost time here:**

1. **A clean ASAN run is NOT evidence against a stack use-after-return.** MSVC's ASAN *parses*
   `detect_stack_use_after_return=1` — `verbosity=1` proves options are read — but **does not
   implement it** (a clang-only feature). The buggy code and the fixed code produce identical,
   silent ASAN runs. Confirm a sanitizer actually covers your bug class before trusting its silence.
2. **A test harness that buffers stdout tells you nothing at the only moment it matters.** The CI log
   held not one line, not even the banner, because stdout to a pipe is fully buffered and the buffer
   dies with the process. `setvbuf(stdout, nullptr, _IONBF, 0)` is now main's first statement, plus a
   `DLL_TEST_TRACE` per-test trace that `build.ps1` enables under CI.
3. **One green run after a red one is not a fix**, especially when the change between them altered
   timing. Re-run CI on the *same* commit to measure flakiness before believing it.

**The fix is `shared_ptr` captured BY VALUE**, not `static`: the worker owns a share, so no lifetime
dependency is left to get wrong, and the test stays re-entrant.

### 3.7 NuGet cannot express "and not a different major"

`Avalonia.Skia 12.1.1` depends on `SkiaSharp >= 3.119.4` — an **open-ended minimum**, which is the
NuGet default. A `chore(deps)` bump to `SkiaSharp 4.151.1` therefore *satisfies* the constraint:
**no NU1608, no NU1605**, and `TreatWarningsAsErrors=true` has nothing to fail on. The build is
green, the app starts, the managed API is close enough — and the native side reads off the end of a
buffer sometime later, somewhere else. `HarfBuzzSharp` was **six** majors ahead by the same route.

**How to apply:** when a package is pinned ABOVE what its consumer was built against, that is a
decision and it needs a comment saying why — the `SQLitePCLRaw` pin in the same csproj has a full
paragraph; these two had nothing, which is how three consecutive bumps walked past them. Read the
truth out of the resolved graph, never from the version you typed:

```
py -c "import json;d=json.load(open('ui/UE5DumpUI/obj/project.assets.json'));t=list(d['targets'].values())[0];print(t['Avalonia.Skia/12.1.1']['dependencies'])"
```

Put the version in **one** MSBuild property feeding every reference. Seven scattered references are
how a bump gets applied to some and not the others — the variant that hides longest.

### 3.8 The AV quarantines *collateral*, and it takes uncommitted work with it

2026-08-17. A newly written `scripts/startup-shortcut.ps1` was run once. Bitdefender's **Advanced
Threat Defense** — the behavioural layer, not a signature scan — flagged the process chain
`claude.exe → pwsh.exe → powershell.exe` writing a `.lnk` into the Startup folder. That is a textbook
persistence shape and the AV was not wrong to look.

**What it actually did is the lesson.** It did not block one action. It removed **six files**, most of
which had nothing to do with the Startup folder:

| Removed | Recoverable? |
|---|---|
| `scripts/startup-shortcut.ps1` | ❌ **untracked — gone for good** |
| `build.ps1` | ✅ committed, but the working-tree edit was lost |
| `scripts/gen_proxy_forwarders.py` | ✅ committed minutes earlier |
| `tools/check_proxy_exports.py` | ✅ committed minutes earlier |
| `dist/UE5DumpUI.exe`, `dist/startup-shortcut.ps1` | ➖ rebuildable |

Four rules came out of it:

1. **Commit before you execute anything new.** The only unrecoverable loss was the one file that had
   not been committed yet. `git checkout` restored the other three verbatim. This costs nothing and
   is the whole defence.
2. **It then BLOCKS THE PATHS, and `git checkout` fails with `Permission denied`.** The block is
   *path-specific*: new paths in the same folders stayed writable, which is why the Python twin could
   be written and committed while the `.ps1` could not. **It survives until a reboot** — do not
   burn a session retrying.
3. **Windows Defender's logs are empty and prove nothing.** `Get-MpThreatDetection` and the
   `Windows Defender/Operational` log both returned zero events. Defender is *passive* here
   (`SecurityCenter2 productState 393472`); the real product is Bitdefender, whose events are not in
   any Windows event log. Check `root\SecurityCenter2 AntiVirusProduct` before concluding "no
   detection" — an absence in the wrong log is not evidence.
4. **PowerShell is inspected far more aggressively than Python on this machine.** Maintainer's
   standing rule: **anything automated that creates or deletes must go through the Python tool**; the
   `.ps1` is for the maintainer to invoke by hand. Same family as the older AMSI finding (a
   `LoadLibrary`/`GetProcAddress` P/Invoke probe is refused as "malicious content").

**Do not respond by making the script look like something else.** The behaviour genuinely *is*
persistence; that is what the tool does. The honest fixes are a folder exclusion, a second
implementation in a less-inspected host, and not running the thing automatically.

### 3.8 This machine has TWO Visual Studios, and picking the wrong one fails at LINK, in a file you never touched

`vswhere -all` returns **two** installations, and only the second has the toolset `build/` was
compiled with:

| install | MSVC toolsets |
|---|---|
| `C:\Program Files\Microsoft Visual Studio\2022\Community` | `14.44.35207` only |
| `C:\Program Files\Microsoft Visual Studio\18\Community` | `14.38.33130`, `14.44.35207`, **`14.51.36231`** |

`build.ps1` calls `Enter-VsDevShell` on the **newest** install, so every object already sitting in
`build/` was produced by **14.51**. Point any other builder at `2022\Community\…\vcvars64.bat` and
the compile stage happily succeeds — then the LINK dies:

```
Radar.cpp.obj : error LNK2019: unresolved external symbol __std_rotate
Radar.cpp.obj : error LNK2019: unresolved external symbol __std_find_last_not_ch_pos_1
dll\UE5Dumper.dll : fatal error LNK1120: 2 unresolved externals
```

Those are 14.51 vectorized-algorithm helpers, and **no `.lib` under `14.44.35207\lib\x64` defines
either** — checked with `dumpbin /LINKERMEMBER:1` over every lib in that directory, not assumed.

**Why it misleads so effectively.** The named file is one you did not edit (`Radar.cpp` is not even
a `Macht.h` dependent), the symbols are STL internals, and it surfaces immediately after whatever
source change you *did* make — so it reads as "my edit broke the build" or "the STL install is
corrupt". It is neither: it is **objects from one toolset being linked against another's libs**.

**The one-line diagnostic**: revert your change and rebuild pristine. If the failure survives, it
was never yours. (That is what settled it here.)

⇒ **Never hardcode a `vcvars64.bat` path.** Resolve with
`vswhere -latest -prerelease -products * -property installationPath`, which is what
`tools/verify/build_dll.py` does, and fail loudly rather than falling back to a guess. Related but
*separate*: CLAUDE.md's `msvc_deps_prefix` warning is about **configure**, not build — that one is
about the console code page, this one is about which VS you entered. Getting the code page right
does not save you from the wrong toolset.

### 3.8c `build.cmd` runs Windows PowerShell **5.1**, and exactly ONE command in `build.ps1` is fragile there

Two facts that only bite together.

**1. `build.cmd` always spawns 5.1, whatever shell you type it in.** Its last line is
`powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 …`, and on Windows `powershell.exe`
is *always* Windows PowerShell 5.1 — PowerShell 7 is a **separate executable**, `pwsh.exe`. Launching
`build.cmd` from pwsh 7 just makes pwsh the parent process. The transcript proves it out of the
script's own mouth: `主應用程式: powershell -NoProfile …` / `PSVersion: 5.1.…` / `PSEdition: Desktop`.

**2. Under 5.1, `Get-FileHash` is a script FUNCTION, not a cmdlet.** Measured over every command
`build.ps1` calls — 26 of them — exactly one is not a compiled cmdlet:

```
Name          Type      Source
Get-FileHash  Function  Microsoft.PowerShell.Utility
```

It lives in `Microsoft.PowerShell.Utility.psm1`, so resolving it forces PowerShell to **auto-load
that module from disk at the moment of first call**. `Write-Host`, `Copy-Item`, `Get-ChildItem` and
the other 24 are binary cmdlets already in the initial session state and never touch the module file.
⇒ anything that blocks that one on-disk load — an AV real-time scan (**this machine runs
Bitdefender**, §3.8), AMSI, a transient lock — raises `CommandNotFoundException` for `Get-FileHash`
**and for nothing else in the script**. Which is precisely what happened on 2026-08-24: four
consecutive `build.cmd publish` runs died at *"無法辨識 'Get-FileHash' 詞彙"*, immediately after the
AOT publish had just written a 54 MB native binary — peak AV activity. It did **not** reproduce
under an otherwise identical run minutes later, so it is intermittent, not a logic bug.

⚠ **The version-dependence nearly hid it.** Under PowerShell 7 `Get-FileHash` *is* a compiled cmdlet,
so probing from `pwsh` reports **zero** fragile commands and reads as an all-clear. Classify commands
in the host that will actually run them.

**Fix: remove the dependency, do not switch hosts.** `build.ps1` now computes SHA-256 through
`System.Security.Cryptography` (always-loaded CLR, no module) via `Get-Sha256Hex`, verified
case-sensitively identical to `Get-FileHash` on three files including the 54 MB exe.
⛔ **Do NOT "fix" this by pointing `build.cmd` at `pwsh`** — `Microsoft.VisualStudio.DevShell.dll` is
a .NET Framework assembly, and more importantly the `[Console]::OutputEncoding` pin at the top of
`build.ps1` is load-bearing for `msvc_deps_prefix` (CLAUDE.md): change the host and a `.h` edit can
silently stop triggering a rebuild. That trades a located, one-line problem for an unlocated one.

⚠⚠ **CI runs the SAME `build.ps1` under a DIFFERENT PowerShell, so it structurally could not have
caught this.** `.github/workflows/release.yml` invokes `./build.ps1 -Mode Publish` under
`shell: pwsh` (every step in that file is `pwsh`), i.e. **PowerShell 7** — where `Get-FileHash` is a
compiled cmdlet and the failure mode does not exist. Locally `build.cmd` gives it **5.1**. Keep that
divergence in mind for any *other* `build.ps1` behaviour too: green CI says nothing about the host
the maintainer actually builds in. (The `Get-FileHash` call in that workflow at `release.yml:79` was
checked and left alone for the same reason — pwsh, and no AV on the runner.)

### 3.8d An incremental build HIDES compiler warnings — a "new" warning after a sync usually is not new

`.\build.cmd clean` and a from-scratch tree surfaced `Frieren.cpp(1009): warning C4190` that plain
`build.cmd publish` never showed. Nothing had changed: read the Ninja step counts — `[1/11]` (no
`Frieren.cpp` in the list, no warning), `[1/34]` (`Frieren.cpp` compiled, warning), `[1/83]` clean
(compiled, warning). **A warning is emitted by a COMPILE, and Ninja only compiles what changed**, so
a warning in an untouched file is invisible until something forces its TU to rebuild — which a repo
sync, a header edit, or `clean` will do at an arbitrary later date. ⇒ *"this appeared after I synced"*
is not evidence the sync caused it. Before hunting a cause, check whether that TU was compiled at all
in the run that was quiet.

The C4190 itself is worth knowing as a shape: `Frieren.cpp` wraps ~2,200 lines in one
`extern "C" {` for the `UE5_*` exports, so a `static inline` helper declared inside it inherits **C
language linkage** and returning `std::vector<FunctionInfo>` trips C4190. ⭐ **The diagnosis was the
asymmetry**: `Mimic.cpp:529` holds a byte-identical twin that does *not* warn, because Mimic uses
per-declaration `extern "C"` and never opens a block. Fixed with `extern "C++" { … }` around the two
adapters; negative-controlled in both directions.

### 3.9 Two injected hosts at once: the second one silently never scans

"One game at a time" is written down as a **resource** rule. It is also a **correctness** rule, and
that half is not obvious until it costs you a measurement.

The DLL's auto-start refuses to run when the pipe is already owned:

```
[WARN] [INIT] DllMain AutoStart: pipe already exists (another UE5Dumper instance running)
              — skipping auto-start
```

So the second host **loads the DLL, reports a successful injection, creates its log folder, writes a
`Logger started` line — and then does nothing at all.** Its `scan-0.log` was **122 bytes**. Every
downstream check on that host then measures an absence that the injection itself caused: no scan, no
pointers, no `HintCache: Saved results`, no sweep. A rig looking for any of those reports a clean,
confident FAIL of a working fix.

**The tell is in `init-0.log`, not `scan-0.log`** — the skip is an INIT-category line, and the scan
log looks merely *empty* rather than *skipped*.

⇒ **Kill the previous host and confirm it is gone before injecting the next**, and when a scan-shaped
check comes back empty, read `init-0.log` before believing it. `initState` also says so directly:
`INIT_SKIPPED = 4` exists for exactly this case (`Mimic.h`), so a rig can assert
`initState == INIT_READY` up front rather than discovering it afterwards.

### 3.w A game PROCESS that exists is not a game that BOOTED — and the failure looks like a result

Satisfactory was launched for G3 steps 3+4 by running its shipping exe directly:

```
D:\SteamLibrary\...\Satisfactory\Engine\Binaries\Win64\FactoryGameSteam-Win64-Shipping.exe
```

`tasklist` showed the process. Injection succeeded. The pipe answered. The DLL scanned. Every
outward sign said "running game" — and the whole 20-minute run was measuring **an engine that had
never initialised**, because the exe had put up a modal dialog behind everything:

> Failed to open descriptor file ../../../FactoryGameSteam/FactoryGameSteam.uproject

UE resolves the `.uproject` **relative to the exe**, and for this title the exe lives in
`Engine\Binaries\Win64\`, so `../../../FactoryGameSteam/` does not exist in the install layout.
**Satisfactory must be started through Steam**, which supplies the right working directory. (Compare
Elliot and DQ7R, whose exes sit under `<Game>\Binaries\Win64\` and *do* start directly — so "start
the shipping exe directly" is a per-title fact, not a general one.)

⚠ **WHY IT WAS CONVINCING, which is the part worth remembering.** The readings were not garbage;
they were *coherent*:

| observation | why it looked genuine |
|---|---|
| `GNames` and `GWorld` **resolved** (real addresses) | those come from symbol exports that work as soon as the DLLs are mapped — no engine init needed |
| `GObjects=0x0`, `GEngine=0x0` | reads exactly like the "unresolved globals" title the step was hunting for |
| `TrySymbolExport: Found '?GUObjectArray@@3VFUObjectArray@@A'` then `ValidateGObjects: Failed … Num@+04=-1` | the symbol resolved; only the *counts* were empty |
| `ExtraScanGObjects: No valid FUObjectArray found (763 candidates tested)` | a specific, quantitative-sounding negative |

**The contradiction that should have stopped it earlier was already in our own docs**:
`test-games.md` records this exact title/engine resolving **all three globals via symbol export with
217,602 objects**. A host that "regressed" to zero should have been suspected before it was believed.

**Tells, in order of cheapness:**
1. an **empty** array behind a **resolved** symbol (`Num = 0 / -1`) is "not initialised", not "wrong
   address" — a wrong address gives garbage counts, not zeros;
2. `object_count == 0` while GNames works at all;
3. our own `test-games.md` row for that title disagreeing;
4. and simply **looking at the window**.

⇒ Any conclusion from such a run is void. In this case it would have entered the register as
*"Satisfactory has unresolved globals"* — a fact about a game that was not running.

✅ **Confirmed by re-running it properly** (`steam.exe -applaunch 526870`, wait for a menu, then
inject): all four globals resolve, **137,425 objects**, and `gobjects` comes back as
**`0x7FFCC7CE3620`** — *the exact address the failed run had already found and rejected as empty*.
Holding the address constant across the two runs isolates "array not yet populated" from "wrong
address" perfectly, and confirms the symbol path was never broken.
⚠ The **pre-existing** `FactoryGameSteam … GObjects=0x0, Objects=0` line in that title's older log,
which is what made it the chosen host in the first place, is very plausibly the same artefact. It
should not be cited as evidence without a Steam-launched re-run.

-----

### 3.x `proxy_refresh.py report` cries wolf after ANY local rebuild — do not act on it blindly

It compares **SHA-256**, and our build is not byte-reproducible: rebuilding *identical* source
(clean tree, same `build_number.txt`) produces different bytes — PE timestamp, checksum, embedded
build date. So the moment you run `build.ps1` for any reason, every deployed proxy on the machine
flips to `*** STALE ***`:

```
dist/proxy: version.dll=2,882,560 ...
  DQ7R      version.dll   2,882,560  *** STALE ***      <- SAME SIZE. Not stale.
  ... 9 deployed proxy(ies), 9 stale
```

**The tell is that the sizes match exactly.** A genuinely stale proxy is a *different build* and
essentially always differs in size too (the 2026-08-19 sweep found six at 2,860,544 / 2,867,712 /
2,855,936 against a 2,88x,xxx dist).

⛔ **Do not `refresh` on that signal.** It overwrites the genuine artifacts in nine game folders
with local rebuilds for no functional gain, and burns the detector: once everything has been
refreshed from a local build, a *real* staleness later has nothing to be compared against.

ℹ️ It does **not** block a run: `PipeClient.assert_build()` compares only the **build NUMBER**
(`get_pointers.build_number` ends with `dist/build_number.txt`), so a deployed 3263 proxy still
satisfies it against a locally rebuilt 3263 dist. The two checks disagree by design — one asks
"same build?", the other "same bytes?".

A size-or-build-number comparison, or the embedded `1.0.0.NNNN` string, would be the honest
predicate here; SHA-256 answers a question nobody asked.

-----

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

> ⚠ **First, read CE's address-list checkbox correctly, because getting it backwards inverts every
> verification result you draw from a screenshot** (maintainer, 2026-08-18):
>
> | what you see on the record | what it means |
> |---|---|
> | **a big red ✗ drawn over the checkbox** | the script is **ACTIVE** |
> | an empty checkbox | not active |
>
> The ✗ is CE's *enabled* glyph for `<script>` rows — **it is not an error marker**, and CE has no
> per-record error marker at all. Two consequences, both of which have already cost time here:
> **(a)** a script that bailed out correctly and untick'd itself looks *identical* to one that was
> never ticked, and **(b)** a script that ticked, armed, and then silently stopped writing still shows
> the red ✗ — which is exactly how `[FREEZESTUCK-2026-08-18]` hid (`todo.md`). The checkbox reports
> what CE was *asked* to do, never what our Lua *achieved*; for that, open **CE → Lua Engine**, since
> the hygiene rules deliberately keep those messages out of dialogs.

> **Where to check whether a CE Lua call exists — and there are two copies, which answer different
> questions.** CLAUDE.md forbids inventing CE Lua calls, so grep one of these first:
>
> - **The installed binary's docs** — `celua.txt` in the CE install directory
>   (`C:\Program Files\Cheat Engine\celua.txt`, ~238 KB, 7.7 on this machine). Use this for *"does
>   this work in the CE the user is actually running?"*
> - **The CE source clone** — `D:\Github\cheat-engine\Cheat Engine\bin\celua.txt` (a real git clone;
>   `git tag` includes `7.5`). Check out an older tag to read that release's `celua.txt`. Use this for
>   *"since which version has this existed?"*
>
> **The public source lags the release**, so the two genuinely disagree — [CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md)
> §4 is the worked example: a defect still present in the 7.5 source and measured **fixed** in the 7.7
> binary. Reading only the source would have had us document a bug the shipping CE does not have.
>
> **And a probe beats both.** `celua.txt` has advertised a capability that does not exist *and* denied
> one that works (see the `getSettings()` entry below, where three rounds of being wrong were each
> settled by probing, not by re-reading).
>
> **For CE's *plugin* API, read the Pascal, not the header.** `ce_InjectDLL` is the worked example
> (`pluginexports.pas:622-640` + `CEFuncProc.pas:1050-1051`, `1391-1396`): it returns `false` only if
> an exception *escapes*, and CE catches one of the three it can raise. So it returns **true** on
> "Failed injecting the DLL" (caught internally, falls back to `forceLoadModule`) and **false** on the
> >10 s timeout (a plain `Exception`) and on "Failed executing the function of the dll"
> (`EInjectDLLFunctionFailure` — a **sibling** of `EInjectError`, not a subclass, so
> `on e:EInjectError` misses it). The BOOL is therefore *inverted for the common cases*, and a UI that
> trusted it told users to check that the target is 64-bit while the DLL was loaded and running
> (audit #5 AB2). **Where CE hands back an ambiguous status, prefer something you can observe** — the
> plugin now re-walks the target's module list instead.
>
> **CE's Lua has no `bAnd` / `bOr` / `bNot`.** Single-bit set/clear is done with pure arithmetic
> (`math.floor(b / mask) % 2` to test, `b + mask` / `b - mask` to set/clear), which is also version-
> proof. Two places in this repo do it that way — `StandaloneTrainerScriptGenerator`'s `UE5T_setbit`
> and `ue5_freeze_helper.lua`'s `writeBool` — and a third tier (`Solitar::ApplyBoolBit`) and a fourth
> (`FieldValueConverter.ApplyBoolMask`) implement the same rule in C++ and C#. If you change the rule,
> change all four.

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

### 4.3 `FUObjectItem::SerialNumber` is NOT an identity witness — witness the INPUT BYTES instead

**Two audit registers in a row prescribed an `(InternalIndex, SerialNumber)` witness** for caches
keyed by a recycled address (audit #5's cluster ③, and AA2 before it), each calling it *"the same
pair UE itself uses to detect a recycled slot"*. **It is not, for a passive observer, and shipping
it would have produced a validator that is silent in exactly the case it exists to catch.**

`FUObjectArray::FreeUObjectIndex` sets `SerialNumber = 0`, and `AllocateSerialNumber` assigns only
inside `if (!SerialNumber)` — essentially only from `FWeakObjectPtr::operator=`. So **most objects
carry serial 0 for their entire life**, and the free list is LIFO. A stored witness of `(i, 0)`
therefore matches the recycled slot's `(i, 0)` and reports "fresh". UE gets away with the pair
because `FWeakObjectPtr` *forces* the serial to be non-zero at creation; a cache that merely
*observes* objects never does. In this repo it would also have rested on `Aura::GetSerialNumber`,
whose own comment marks the packed `FUObjectItem` layout `*** UNVERIFIED ***`.

**How to apply — the generalisable rule, and it is the better fix anyway:**

> **Witness the INPUT BYTES of a memoized decode, not the identity of the object that held them.**

A memo whose value is a pure function of a few bytes can be validated by re-reading exactly those
bytes. It is *total* rather than heuristic (equal inputs ⇒ the cached output is still correct, no
matter who owns the address now), and it is usually **cheaper than the thing it guards** — `GetName`
now compares two `int32`s where the audit's version would have walked GObjects. Shipped as
`Ubel::NameWitness` (build 3065).

Two riders that generalise with it:

- **Cover every byte the decode consumes.** `Serie::GetString(comparisonIndex, number)` takes two, so
  the witness holds two. Dropping `Number` is audit #5's U8 — a defect that already shipped once and
  rendered `Slot_1`/`Slot_2`/`Slot_3` all as `Slot`.
- **Check whether the memo hands out a VALUE or a REFERENCE before designing any invalidation.** The
  same register grouped three caches as "one fix pattern, not five". They split by return type, not
  by key type: `GetName` returns a copy, so it can validate-and-replace; `WalkClass`/`WalkClassEx`
  hand out `const ClassInfo&` into their maps to 25 call sites, several of which re-enter while
  iterating the referenced `Fields` **on one thread**, so any erase/clear/assign there is a
  use-after-free with **no race required**. Those got an insert-time gate instead, and bounding their
  growth is now blocked behind a return-type refactor.

### 4.3b A negative control that reds MORE than predicted has found a COUPLING

Audit #5 AE3 (build 3068) shipped two changes to one guard: release the dedupe key on every failed
load, and drop the `&& HasClass` conjunct that had been keeping cold-start failures retryable.
Reverting the release alone was predicted to red one test. It red **two** — the extra one being the
test asserting that a *cold* failure had always been retryable.

That was not a broken control and not a broken test. The two halves are **coupled**: dropping the
conjunct is only safe *because* the release now covers every failure, so removing the release while
keeping the drop produces a state strictly worse than either the before or the after — a state that
never shipped and never could.

**How to apply.** §1.2 says run the control; this is what to do when its result surprises you. An
unexpected red is a claim about the *shape of the fix*, not about the test:

- **More tests red than predicted** ⇒ the reverted piece is load-bearing for something else you
  changed. Find the partner and record the dependency, or a later "simplification" will delete one
  half and the suite will still look fine on the other.
- **Fewer red than predicted** ⇒ the assertion cannot see the change (§1.3's seam problem), or the
  finding's premise was wrong.
- Either way the honest baseline is **reverting the change as a WHOLE** and recording that count.
  For AE3 that was exactly 3 red with 2 deliberately green, and the two green ones are what proved
  the finding's "with no way to retry" was too broad.

Corollary already paid for twice: **when a control's patch target is not found, that is a broken
control, not a passing one.** In the same session a control silently no-op'd because the search
string used a hyphen where the source had an em-dash. A harness that reports "0 red" for a revert it
never applied is indistinguishable from a fix that works.

### 4.3c A refutation recorded in a "do-not-re-raise" list is where a wrong call does the most damage

Audit #5's §3 listed the claim *"`Macht.cpp`'s AOB scan family never polls `Tot::Requested()`"* as
**refuted**, with the parenthetical *"here the guards exist"*. Re-derived while fixing G2 (build
3086): `grep -c "Tot::" dll/src/Macht.cpp` = **0**. The `__try`/`__except` blocks in `Macht.h` are
SEH **read** guards — they stop a fault, they do not stop a loop. The entry had been suppressing a
finding *larger* than the one it was contrasted against, for two weeks, in the one place nobody
re-checks by design.

The refutation was plausible because the same claim shape had been correctly refuted elsewhere in the
same pass, and because "guards" is ambiguous between the two kinds.

**How to apply:**

- **Re-derive a refutation the same way you re-derive a finding**, and with the same suspicion. A
  do-not-re-raise row is a *negative* claim, and §1.2's rule applies to it too: ask what evidence
  would show it wrong, then go and look. Here that was one `grep -c`.
- **A refutation that names a mechanism must name the RIGHT one.** "The guards exist" was true of
  some guards. Write which symbol, at which line, doing which job — a refutation with no `file:line`
  is an opinion.
- **Re-check any refutation that was decided by analogy** ("same shape as the one we just killed").
  Analogy is how the correct verdict next door leaks into the wrong one here.
- When you overturn one, **strike the row rather than deleting it**, re-file under a new ID, and say
  in both places why the original was wrong. The next reader needs to know the row was examined, not
  merely that it vanished.

### 4.3d A fast path must never use a WEAKER test than the slow path it shortcuts

Audit #5 G10 (build 3091), and it is the sharpest instance of this repo's dominant defect family.
`Genau::ScanForTarget` cached a winning AOB pattern, then on the next launch re-checked it with
`Macht::AOBScan` — **first match only** — where the scan that produced the hint had used
`AOBScanBatch` and walked **every** match until one validated. A pattern whose first match failed
validation was therefore declared a MISS and *erased from the pattern set*, hiding it from the one
path that would have succeeded. The false "not found" was then persisted over the good hint, so it
oscillated: fail → hint destroyed → cold scan succeeds → hint saved → fail again.

It survived because two things agreed with each other and neither agreed with reality: the fast path
reported `hitCount = matchAddr ? 1 : 0`, so a 166-match pattern logged `hits=1`, and the log
therefore *corroborated* the wrong answer.

**How to apply:**

- **A cache/fast path is a claim that two computations agree.** Write the check so they cannot
  diverge: same helper, same cap, same tie-break. Here the repair was literally to call the same
  function the slow path calls and reuse its `kMaxValidate…` rule.
- **Never let a fast-path failure DELETE work the slow path would have done.** Falling back is
  cheap; excluding a candidate is not. Erase only on the strongest possible evidence — here, zero
  matches, not "matched but did not validate".
- **A confirming log line is not confirmation if the log is computed by the shortcut.** This is
  audit #4's root cause #1 (the report and the reality computed by different code paths) wearing a
  new hat, and it is why the count is now the real one.
- **Look for this shape wherever a "hint", "cache", "quick check", or "fast path" exists.** The
  question is not "is it faster" but *"can it return a different verdict than the thing it replaces
  — and if so, in which direction does it fail?"*

The instance that made it visible was a pair of log files five minutes apart on the maintainer's own
machine, which is worth its own note: **the regression corpus you already have on disk is evidence.**
Before estimating whether a defect is real, grep the logs.

### 4.3e "Widen the window" is not monotone when the search is `strstr` over a copy

Audit #5 G8 (build 3105). A context test copied 8 bytes into a `char ctx[17]` and ran `strstr`,
while its comment and its buffer both said 16. The obvious repair — copy 16 — would have shipped
**two** regressions, and a five-line probe found both before any production code was written:

- `strstr` stops at the **first NUL in the copy**. A neighbouring string's terminator inside the
  *wider* window truncates the search, so the wider window **loses** matches the narrower one found.
  Widening is therefore *not* a superset. (Measured: 12.2% of 14,823 `[Rr]elease` occurrences across
  a 33-binary corpus have a NUL in the preceding 8 bytes.)
- Widening the guard to match (`off >= 16`) silently drops every offset in 8..15 — which included
  the single most common real shape, whose needle sits at offset 8.

**How to apply:**

- **Before widening any window, ask what TERMINATES the search.** `strstr`/`strlen`/`strcmp` over
  raw image bytes are NUL-sensitive; raw memory is not NUL-free. A window search over binary data
  should be an explicit byte loop with a clamped length, never a C-string call on a copy.
- **A widened predicate is only a superset if nothing in it is length- or terminator-sensitive.**
  State that as a claim and check it; "more bytes can only match more" is an intuition, not a proof.
- **Widening a LOOSE predicate needs a compensating gate.** G8's window sat on the only tier with no
  anchor requirement, so widening it alone manufactured a confident version out of a release-notes
  heading. The fix added the gate its sibling tier already had — the "the correct predicate already
  exists next door" pattern from §2.2, again.
- **Probe the layouts before editing.** Four hand-built strings run through a five-line model
  answered this completely, cost a minute, and were more decisive than the reasoning they replaced.

Bonus, and it is the fourth instance this week: **the fix's own negative control passed first time
because the control was broken** — a `break` exited the pattern loop rather than the offset loop and
retired nothing. See §4.3b; when a control passes, suspect the control.

### 4.3f A predicate that has NEVER fired is a defect, not a rarely-used feature

Audit #5 G11 (build 3112). Genau's Tier-2 version detector had never matched anything on any binary
this project owns — 0 hits across 170 PE images. Nobody noticed, because "no Tier 2 hit" is
indistinguishable from "this image has no Tier 2 evidence", and Tier 3 quietly absorbed the traffic
with a `lowConfidence` badge nobody chased.

The cause was one character. The needle table's trailing `.` is a *Tier 3* device (it forces a
three-component `X.Y.Z`); Tier 2 inherited it and therefore demanded `Release-5.4.2`, while UE's own
tag is two-component, `++UE4+Release-4.27`. **The identical bug had already been found and fixed for
Tier 1** and written into the version-rev changelog — Tier 2 simply never received the same repair.

**How to apply:**

- **Count the hits of every branch you rely on, at least once.** A predicate with zero observed
  firings is either dead code or a defect; "it is for rare cases" is a hypothesis, and the corpus can
  test it in minutes. G8/G9 were both tuning this predicate's *parameters* while it could not fire at
  all — real work spent on a branch with no reachable input.
- **When a fix is recorded for one tier/path, grep the siblings THEN.** This is §2.3's fix-time
  sibling grep, and here the evidence was sitting in the file's own changelog: the rev-2 note says
  "Tier 1 no longer requires the trailing '.'", which names the exact shape of the bug still present
  next door.
- **Validate a newly-live predicate against an INDEPENDENT one, not against itself.** Tier 2 now
  fires on 6 images and agrees with Tier 1's version on all six. That agreement — two detectors,
  same answer — is worth far more than any number of hand-built unit cases, and it is §1.4 applied
  to a fix rather than to a measurement.
- **A fix that turns a dead branch live must state what it does NOT change.** Here: Tier 1 answers
  first on all six, so no shipped verdict moved. Saying that plainly is the difference between an
  honest report and an oversold one.

### 4.3g Values that are five names for ONE measurement need one writer, not four

Audit #5 G12 (build 3119). `FSTRUCTPROP_STRUCT` / `FARRAYPROP_INNER` / `FBOOLPROP_FIELDSIZE` /
`FBYTEPROP_ENUM` all name the same slot (`sizeof(FProperty)`), and `FENUMPROP_ENUM` is that slot + 8.
They had **four independent writers**, and one of them set only **two** of the five. Three exit paths
then shipped the split for a whole session: struct reads correct, TArray element descriptors and
every enum-name read 8 bytes off.

Nobody caught it because the split is *plausible* — each writer looked locally coherent, and the two
values it did set were the two that most code exercises.

**How to apply:**

- **If N constants are derived from one measurement, express the derivation ONCE** and make that the
  only way to publish them. A helper that returns the whole family beats N assignments however
  carefully those assignments are commented — this is the "make the helper impossible to bypass"
  half of §2.3's cluster ④, applied to data rather than to a predicate.
- **Count the writers before trusting any one of them.** The finding named one site; the fix-time
  grep found four. Two of the extra three were already coherent — which is exactly why they were
  never suspected, and exactly how they drifted from the fourth.
- **A partial write is worse than a wrong one.** A uniformly wrong family fails loudly (nothing
  resolves); a split family resolves the common paths and quietly corrupts the rest, which is the
  "names resolve, values are garbage" shape that costs the most debugging time.
- **Assert the bad SHAPE, not just the good value.** The unit test pins the invariant across every
  plausible input *and* asserts the historical split as a shape that must be unreachable. The second
  form survives a future refactor that changes the numbers.
- **Leave the deliberate exception, and comment it at both ends.** One site genuinely must diverge
  (UE5.7 puts `EArrayPropertyFlags` before `Inner`, so `FARRAYPROP_INNER` is re-probed separately).
  An unexplained exception is indistinguishable from the bug.

### 4.4 Do not use KismetMathLibrary as a verification target

> ⚠ **NARROWED 2026-08-17 — it is not a version band.** **Lushfoil Photography Sim is UE 5.6 cooked
> Shipping and `Add_IntInt(3,4)` returned `7`** (`✓ PE hook verified`), so "UE 5.5+ cooked Shipping"
> over-states it. Read the rule as **title-specific** — most plausibly whether that title's cook
> applied BlueprintFastCall to that helper. The practical consequence is unchanged: *do not build a
> verification on a KismetMathLibrary return*, because it may or may not be dispatched and you cannot
> tell which from the outside. But equally, **do not read a KismetMathLibrary failure as proof the
> hook is fine** — on DumperTest the same signature turned out to be a genuinely mis-detected vtable
> slot, and only the DLL's own "fired 0 times in 1500 ms" validator separated the two cases.

KismetMathLibrary helpers (`Exp`, `Multiply_DoubleDouble`, `Add_IntInt`, …) **silently no-op** when
invoked via ProcessEvent from a reflection-driven dumper on UE 5.5+ cooked Shipping. Likely UE's
BlueprintFastCall optimisation: the BP VM bypasses ProcessEvent entirely for these helpers, so the
cooker leaves the reflection metadata intact (parmsSize, numParms, parm offsets, flags all correct)
while the `execXxx` thunk returns without writing `Z_Param__Result`.

Verified failing pattern on Everspace 2 (UE 5.5): A=3, B=4 written correctly, dispatch returns
`result=0`, ReturnValue stays 0, inputs preserved and the return slot untouched.
⛔ **REFUTED 2026-08-20 — see the resolved-confound block below. On a correctly detected hook the
same call on the same title returns 7.**

**How to apply:** redirect verification to **game-specific instance methods** (PlayerController /
Character / Inventory subclass functions), with the user in **active gameplay** (not an idle main menu)
so the game thread pumps ProcessEvent, and prefer simple scalar returns.

> ⚠ This one carries a confound worth remembering: it was diagnosed while the ProcessEvent hook was
> installed in the **wrong vtable slot** (a hardcoded UE-version table whose only "validation" was that
> the slot pointed at readable code — which every UObject virtual does). That was fixed in build 648 by
> pattern-scanning the function body plus a post-install fire-counter watchdog. The generalisable half
> is the reason it slept for 600+ builds: `-5` timeouts were attributed to "idle game / game thread not
> pumping" — *a plausible-sounding explanation that was never falsified.*

> ## ⭐ RESOLVED 2026-08-20 — the confound above WAS the whole story, and Everspace 2 no longer no-ops
>
> The paragraph above used to end *"the stub hypothesis was never re-verified against the corrected
> hook."* It has now been re-verified, **on the very title it was diagnosed on**, and it does not
> reproduce. Everspace 2, headless, dist 3263:
>
> ```
> DetectProcessEvent (pattern): match at vtable+0x278 -> 0x7FF60152D940
> ProcessEvent: offset resolved to vtable+0x278 via the pattern scan (detection run 0/8)
> GameThreadDispatch: hook installed at 0x7FF60152D940, validator armed (1500ms)
> VALIDATION FAILED lines: 0        hook_active=True   fire_count=160
> Add_IntInt(3,4) -> result_hex 03000000 04000000 07000000   ==>  ReturnValue 7
> ```
>
> The return slot that "stayed 0" now holds **7**. So the Everspace 2 evidence for a
> BlueprintFastCall stub was an artefact of the **wrong vtable slot**, exactly as the confound
> warned — and with slots now pattern-detected per title (`0x260` Lushfoil 5.6, `0x268` DumperTest
> 5.4, **`0x278` Everspace 2**) the failing pattern this section was built on has **no surviving
> instance on this machine**.
>
> ⚠ **What does NOT follow.** This does not prove BlueprintFastCall never elides a helper — only that
> the one title we cited for it does not. The practical advice is unchanged and still worth keeping:
> **do not build a verification on a KismetMathLibrary return**, because you cannot tell dispatch
> from elision from the outside. What changes is the inverse reading — a KismetMathLibrary failure
> should now be treated as **evidence of a bad slot first**, since that is what it turned out to be
> every time we have actually chased it.

-----

## 5. Triage recipes

### 5.1 "Value Search / Group Scan can't find field X"

Reported **five** times between 2026-08-05 and 2026-08-10 with two completely different root causes,
**neither of them in the scanner** — and the first sessions both went hunting in scan code. The symptom
is identical (the user sees a field in Live Walker, no scan returns it) but one cause is "the object was
never enumerated" and the other is "it matched and the row could not show it". Neither logs an error;
both read as a healthy scan.

> ⚠ **UPDATED 2026-08-17 (build 3133): there is now a THIRD cause and it IS in the scanner.** The
> "neither of them in the scanner" line above was true when written and is no longer a safe prior —
> audit #5 **AB4** was a real scanner defect with this exact symptom. Check cause 3 below **when the
> scan type is `Smaller` or `Bigger`**, because it is free to rule out and it is width-shaped: the
> missing rows are all of one WIDTH (every `ByteProperty`, or every `UInt32Property`), not one class
> or one object. Causes 1 and 2 lose rows by object; this one loses them by type.

**Check these three, in this order:**

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

3. **Is the scan type ORDERED, and are the missing rows all one WIDTH?** (audit #5 AB4, fixed 3133 —
   listed because the *shape* recurs, not because this instance is still live.) `BuildNumericTargets`
   asked "does the target fit this width", which is right for `Exact` and wrong for `Smaller`/`Bigger`:
   every `Int16` field is smaller than 70000, but 70000 has no int16 encoding, so no `Int16` entry was
   emitted and every 2-byte field was skipped. **The tell is that the loss is by TYPE, not by object**
   — all byte fields gone, or all unsigned fields gone, while the same scan finds 32-bit fields on the
   same objects. Two live gaps of the same shape remain: **`Between`** still drops widths its upper
   bound cannot encode (its two bounds are built independently — see todo.md), and a **hex** input
   (`0x1F4`) still emits no Float/Double entries.
   The lesson underneath: **a range gate that is correct for equality is usually wrong for ordering,
   and it is invisible because it is right half the time** — pruning `Bigger 70000` off Int16 is a
   genuine optimisation produced by the very same line, so the code reads as working.

The witness rule lives in `Radar::PickGroupWitnessAssignment`, deliberately beside the filter it must
agree with, because while it sat in `Fern.cpp`'s JSON encoder **no test target compiled it** and it kept
drifting. **Check that a rule you are about to move is somewhere a test can reach.** AB4 was split the
same way and for the same reason: the verdict logic went into `Radar.cpp` (compiled by
`dll_helpers_test`) so `Aura.cpp` — compiled by nothing — was left a mechanical substitution.

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
- **A single hand-maintained bugs control table** (merging the audit docs / todo / dev-log into one
  tracker) — evaluated 2026-08-17, **rejected**. The audit #5 register (§3c) already IS the single
  status owner, CI-gated by `check_audit_register.py`, and the other docs' roles (evidence dossiers /
  append-only history that doubles as the gate's claim source / forward work + the verification
  register) are load-bearing for the gates. The deciding evidence: the six-row re-derivation dossier
  drifted three ways within a day of its own fixes — every extra hand-maintained copy of status is a
  copy that lies. A unified view must be DERIVED, never stored — `check_audit_register.py --list`
  prints the open HIGH/MED tier with segments. The spent dossier is in `docs/archive/`; full
  rationale in the 2026-08-17 dev-log entry.

- **Three Avalonia fix designs killed BY MEASUREMENT — do not re-propose any of them.** Each was tried
  against the real UI and each failed to do the thing it was proposed for: **(a)** restoring a scroll
  anchor with `ScrollIntoView`; **(b)** collapsing a set of collection edits into a single `Reset`;
  **(c)** a `Dispatcher`-posted repaint to make a cleared `NumericUpDown` redraw. ⚠ They are grouped
  here because they share a failure mode — all three are the *obvious* fix for their symptom, so a
  fresh session re-invents them. Moved here from the memory index 2026-08-22; that index does not
  travel with git, and this was the only fact in it the repo did not already own.

Evaluations that concluded "do not build" live in the repo rather than here — see CLAUDE.md's docs table
for `text-translation-eval.md`, `teleport-coord-library-spec.md`, `native-c-value-scan-spec.md`,
`multipipe-eval.md`, and `Nibble-Mask-Evaluation.md` in the AOBMaker repo.

-----

## 7. Operational notes for two-machine development

- **`build_number.txt` auto-increments on every `build.ps1` run** (MSBuild only reads it), so doc and
  commit build references drift. Cite the build as of commit time. The bump is unconditional at
  `build.ps1:446`, **before** any `-Mode` / `-Target` branch; `-NoBumpBuildNumber` suppresses it.
  ⚠ **"only `-Mode Publish` bumps it" is a persistent and wrong belief** — it was in the project
  memory index for months and cost the 2026-08-19 closing session a build-number collision. A
  `-Target DLL` run bumps it exactly as hard as a publish does.
- **`dist/` is gitignored**, so a freshly synced repo can still hold a days-old runnable build. Check
  `dist/UE5DumpUI.exe`'s size and mtime, not just `git status` — ~54 MB is the AOT-trimmed build,
  ~107 MB is the non-trimmed one that must never be handed over.
  ⚠ **A plain `build.ps1` OVERWRITES `dist/`** — the copy step is unconditional, not publish-gated.
  So "there is a build in `dist/`" never implies "there is a *shippable* build in `dist/`", and a
  non-trimmed exe can be sitting there under a number a real release already used. On 2026-08-19
  the number `3262` named three different binaries this way (the other PC's AOT build, a
  106.8 MB non-trimmed local one, and the pending publish). **Before handing over or comparing
  builds, check the SIZE, not just `dist/build_number.txt`.**
  ⚠⚠ **And `-Target Test` publishes the UI too — it is NOT read-only.** Measured 2026-08-20 with a
  before/after hash on `dist/UE5DumpUI.exe`: **54.7 MB `3ebf02e7` → 106.8 MB `fa1e3f19`**, twice,
  reproducibly. The run announces it (`>> Publishing UE5DumpUI (Release, self-contained
  single-file)... [OK] UE5DumpUI.exe (106.8 MB)`) but nobody reads a green test run's middle.
  This is the nastiest instance of the rule above, because of **which** command does it: `-Target
  Test` is the option you reach for precisely when you want to change nothing — CLAUDE.md described
  it as building "only the two test executables", which is true of the C++ side and badly
  misleading about the rest. So the safest-looking command in the file silently destroys the only
  artifact the hand-over rule protects, and leaves a *runnable* exe behind, at the right build
  number, that merely happens to be the wrong one. **After any `-Target Test`, re-run
  `-Mode Publish -NoBumpBuildNumber` and check the size before handing `dist/` over.**
  Found by accident: a `-Target Test` run used only to confirm `build.ps1` still parsed after an
  edit, whose *summary listing* showed `UE5DumpUI.exe (106.8 MB)` where 54.7 MB was expected. The
  summary listing is worth reading for that reason alone.
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

### 7.2 CLAUDE.md's Documentation Index is a POINTER table, and it has drifted twice

Same argument as §7.1, applied to the other always-loaded file. `CLAUDE.md` loads on **every turn**,
so a paragraph there costs more than a paragraph anywhere else in the repo.

* **2026-08-27.** The index had reached **40 KB across 56 rows** — a mean of 716 bytes, i.e. a
  paragraph per entry. A row-by-row audit against every target doc found the prose was
  **near-entirely duplicated**, plus **six claims that had gone stale or were refuted by the very
  doc they pointed at**. Trimmed to ~11 KB; whole file 77.5 KB → 39 KB.
* **2026-09-06.** It had grown back to ~16 KB and the file was 127 bytes under its own 40,960 cap.
  A second pass against the same standard cut the whole file to ~33 KB — and again found stale
  claims *inside the summaries*: `todo.md` billed at 201 KB (really 188), Avalonia at 12.1.0
  (really 12.1.1), and an App-data rule naming two per-game families when `Constants.cs` says
  `TeleportCoords\` is **the third**.

⭐ **The recurring finding is not "it got long" — it is that a SUMMARY GOES STALE AND A POINTER
CANNOT.** A row saying *what a doc contains* has to be re-verified whenever that doc changes, and
nobody does. A row saying *when you would open it* survives. That is why the rule is phrased as a
question ("when would I open this?") and capped in bytes.
