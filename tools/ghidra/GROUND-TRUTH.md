# Ground truth for the AOB regression sweep

Every address below was resolved from a real PDB symbol (or, where noted, from a
2-instruction accessor or from disassembly), so a signature that resolves to one of these is
*provably* correct rather than plausibly correct.

**The sweep is scripted — do not hand-run `analyzeHeadless` per project:**

```bash
bash tools/ghidra/sweep.sh                      # everything; 4m38s desktop / 12-15min laptop @ JOBS=3
```

**The full sweep is CHEAP relative to what it buys — always run it, never a filtered subset "to
save time".** It does not scale the way the row count suggests, and never did: `-noanalysis
-readOnly` has been in the runner since the first scripted sweep (`f70fa66`), so `scan_patterns`
only ever reads raw bytes.

### ⏱ A SWEEP TIME WITHOUT ITS MACHINE IS NOT A MEASUREMENT

Both rows below are correct. They differ by **3.1×** because they are different computers — which
is not a footnote, it is the whole point: the same mistake as writing one machine's disk path into
the repo. **Never quote a sweep duration without saying which machine produced it.**

| machine | CPU | corpus on | rows / programs | `SWEEP_JOBS=3` | date |
|---|---|---|---|---|---|
| desktop | Ryzen 9 **9950X3D**, 64 GB | — | 57 / 70 | **4m38s** (278 s) | 2026-07-29 |
| laptop (MSI Raider A18) | Ryzen 9 **9955HX3D** 16C/32T, base 2.5 GHz, 61.6 GB | internal NVMe (CT2000T500SSD8) | 57 / 74 | **12–15 min** (715–872 s) | 2026-08-01 |

**The laptop row is a RANGE on purpose.** Three runs, same machine, same shipped defaults
(`SWEEP_JOBS=3`, `SWEEP_XMX=14G`), all 57/57 `exit=0` / 74 scan TSVs / 0 failures:

| run | seconds | condition |
|---|---|---|
| independently instrumented | 850.5 | — |
| first full run | **872** | immediately post-reboot, cold cache |
| verification run | **715** | warm cache (this machine runs PrimoCache, a write-back block cache) |

**22% spread on one machine**, entirely from cache state. So even after pinning the machine, a
single number still over-claims — quote the range, and say whether the cache was warm. The 54-row
`4m34s` comparison belongs to the desktop row; do not read it against the laptop.

Practical consequence: **on the laptop this is a ~15-minute job, not a 5-minute one.** That does not
change the advice below (3→8 jobs saves ~12.6%, and the downside is the corpus), but it does change
how you plan a session — budget a quarter of an hour and do something else, rather than waiting.

⚠ The `~30 min` / `~40 min` / `~50 min` figures this file and `tools/README.md` carried for months
were **never measured** — they were inherited from the pre-script era when each project was
hand-run *with* Auto Analyze. One of them was even "updated" by scaling the wrong number up with
the row count. They are now replaced with the timing above. The only reason to use a tag filter is
to isolate one row while debugging it; the correctness argument for the full run (a filtered run
leaves `REPORT.md` describing a corpus that no longer exists) is unopposed by any real cost.

```sh
bash tools/ghidra/sweep.sh UE4.27 UE5.7         # only tags matching these substrings
py tools/ghidra/aggregate_sweep.py out/sweep    # -> out/sweep/REPORT.md
```

`sweep.sh` holds the truth table below in executable form — it is the source of truth; this file
is the explanation. Env knobs: `GHIDRA_HOME`, `GHIDRA_PROJS`, `SWEEP_OUT`, `SWEEP_XMX`,
`SWEEP_JOBS`.

> **`GHIDRA_PROJS` is machine-specific — set it before running anything here.** The corpus root
> does not follow a clone. `sweep.sh:19` falls back to `D:/Tools/GHIDRA_Projs`, which is also where
> it currently lives (internal NVMe, 63 `.rep` / 182.3 GB). Every `$GHIDRA_PROJS` in the commands
> below means *yours*. Run `py tools/ghidra/preflight.py` first — it reports which projects it
> actually found, instead of failing on a path written on someone else's disk. Recovery procedure
> and the full keep/drop analysis: [docs/corpus-preservation.md](../../docs/corpus-preservation.md).

> ### ⚠ `SWEEP_JOBS`: 3 is the default and raising it is not free — MEASURED 2026-08-01
>
> A run at `SWEEP_JOBS=16 SWEEP_XMX=2G`, with the corpus then on an **external USB SSD**, knocked
> the drive off the bus mid-write: `disk` Event 51 ×12, NTFS unable to flush its transaction log,
> **delayed-write failure on `$Mft` ("data has been lost")**, one Ghidra project `.db` caught
> mid-write, and the volume re-enumerated (`HarddiskVolume9` → `12`). The Ghidra JVMs then wedged
> in unkillable kernel I/O waits; only a reboot cleared it.
>
> **RAM was never the limit** — 16 × 2 GB = 32 GB on a 61.6 GB machine. The **storage transport**
> was. So "use half the cores" is the wrong formula: the binding constraint is what the corpus
> sits on.
>
> Weigh it against the measured cost, using the row for the machine you are actually on (see the
> timing table above — 4m38s desktop, 12-15 min laptop). On the laptop, 3→8 jobs saves **108.8 s
> (12.6%)**: real, but set against corrupting the artifact of record, and §"Never drop" lists
> projects whose `.rep` is the last copy in existence.
>
> Guidance: **USB / removable → 2–3, or don't run it there at all. Internal NVMe → the shipped
> default of 3 is safe; 8 was measured to work and to save ~12%.** `SWEEP_JOBS` is deliberately
> left as an un-clamped env var so a deliberate benchmark stays possible; this note is the warning,
> not a guard rail.
>
> ⚠ **One formula written here on 2026-08-01 is withdrawn.**
>
> 1. **A `cores/4` ÷ `SWEEP_XMX` formula was briefly written here and is withdrawn** — it is
>    self-inconsistent with the shipped default: 35.84 GB free ÷ 14 GB = 2, which forbids
>    `sweep.sh:442`'s `JOBS=3`. `-Xmx` is a *reservation ceiling*, not a working set, so budgeting
>    on it is wrong; but it is not free either — measured per-job commit was 2.15 GB at 8 jobs/14G
>    vs 1.07 GB at 16 jobs/2G, and **system commit peaked at 60.64 GB of a 65.51 GB limit (92.6%)
>    at 8 jobs/14G**. Lowering `SWEEP_XMX` is the real lever; raising `SWEEP_JOBS` without doing so
>    walks into the commit limit.
>
> ✅ **`one_project()` used to always exit 0 — FIXED 2026-08-01.** Its last statement was
> `echo "   done $TAG (exit=$?)"`; `$?` is expanded *before* `echo` runs, so the printed text was
> always correct, but the function then returned `echo`'s status. Every backgrounded job "succeeded"
> as far as `wait` and the script's own exit code were concerned. It now captures `rc`, returns it,
> and appends failures to `$SWEEP_OUT/_failures.tsv` (tag + code + log path — a file, because each
> job is its own subshell and a counter could never reach the parent). The script prints the failing
> tags and **exits 1** if any project failed.
>
> The reap loop had to change with it. `wait -n 2>/dev/null || wait` was only safe while every job
> returned 0: once a real status propagates, a FAILED job makes `wait -n` non-zero, the `||` fires,
> and a bare `wait` drains every remaining job while `running` drops by just one. Measured on a
> 6-job / 1-failure harness: **2869 ms with the version probe vs 4069 ms with the old idiom (+42%)**,
> and it gets worse with more failures. `have_wait_n` is now decided from `BASH_VERSINFO`, which no
> job's exit code can fool.
>
> Why it matters beyond tidiness: a project that dies produces **no scan TSV**, and a missing TSV
> becomes a missing ROW in `REPORT.md` — which reads as *"that pattern was never tested"* rather
> than *"that project did not run"*. Silence looked like coverage.

## Read this first if you are going to CHANGE a pattern

This file is the **operations** manual — how to re-run the sweep and how to add a game. The
**authoring and pruning rules** live in the header + band-discipline block of
[`dll/src/Himmel.h`](../../dll/src/Himmel.h), because they belong next to the patterns they
govern. Read that block too before adding, moving or deleting anything. The short version:

1. **Band = specificity.** Pick the priority band from the pattern's LITERAL (non-wildcard) byte
   count, not from how new it is or who contributed it. Under ~8 literal bytes ⇒ band 800+.
   Counter-example kept deliberately: `GOBJ_ES53_1` has 16 literal bytes and still takes 21–475
   matches per monolithic title, because its *shape* is the generic MSVC static-init thunk. Judge
   by specificity **and** semantics.
2. **Wildcard FRAME displacements, keep STRUCT displacements.** `lea rdx,[rsp+????????]` is fine;
   `lea rdx,[rsp+00000318]` is not — a frame offset encodes one compilation's stack layout.
   But `cmp [rcx+0x2C0],rax` (UWorld member) or `cmp eax,[rdi+0x34]` (TSet Max) pin UE's real
   layout and are the evidence that makes a pattern trustworthy. Two measured qualifiers: only
   wildcard if the pattern has enough other literal context (doing it to the 7-literal-byte
   `GWLD_G42_4` produced 38 hits / 37 decoys on UE 4.27), and small shadow-space constants
   (`sub rsp,0x28`, `[rsp+0x20]`, ≤ 0x40) are idiomatic prologue, not frame layout.
3. **Exact registers beat nibbled ones** on the sparse-delegate target — confirmed three times
   independently. Over-wildcarding does not generalise a pattern; it either adds decoys or stops
   it matching entirely.
4. **Convergent hits = a real global; divergent hits = a generic idiom.** This single test has
   rejected more candidates than any other: a rejected GEngine shape gave 76–93 *different*
   targets per binary, and the TSet hash-bucket probe gave 39–43. A good pattern's extra hits all
   land on ONE address.
   **But convergence only holds WITHIN one pattern.** `GENG_X4` is convergent and *wrong* on
   DQ7R: 50 of its 55 hits agree on `145D76D78`, which is a game-side manager singleton, while
   `145FF4B28` is the real `&GEngine`. Across patterns the discriminator is whether the
   semantically-specific shapes agree — on DQ7R `GENG_X1/X2/X3/DI427_1` all said `145FF4B28`
   and only `X4` disagreed. **Rank candidates by DISTINCT PATTERNS AGREEING, never by raw hit
   count.** `consensus_*.txt` already does this; a by-hand tally of a runtime log does not, and
   that is exactly how the wrong DQ7R value was believed at "41 hits" confidence.
5. **Never prune on "no proof" — only on counter-proof.** `GWLD_V7` sat at 0-correct across the
   whole corpus, looked like dead weight, and went UNIQUE-OK the moment Meltopia gained symbols.
   `GOBJ_RE1` had zero hits across 31 programs and was correct the moment FF7 Rebirth was added.
   Contrast `GWLD_V2/V4/V5/V6`, removed only after being re-tested against three *new* oracles
   and still scoring 0 across 12 oracle groups.
6. **Invariants the build enforces.** Every table is sorted by priority with unique priorities
   (`static_assert` in Himmel.h — verified to actually fire), and `extract_patterns.py` reports
   any `AOB_*` constant declared but referenced by no `PATTERNS[]` array. Two dead constants have
   already been found that way.
7. **`instrOffset` is the silent killer.** If you add leading context to a pattern, move
   `instrOffset` by the same amount. Getting it wrong keeps the hit count healthy and makes the
   resolved address garbage — it drops to 0 correct while still looking like it works.

### Still open (as of build 2474)

- ~~**UE 4.23** is the only unverified sparse-delegate version~~ — **CLOSED 2026-07-28.** The
  "do not reopen this unless a 4.23 binary falls into your lap" escape clause fired: the
  maintainer built the 4.23.1 "Flying" template himself against Epic's INSTALLED Launcher engine.
  `SparseDelegates` is PDB-confirmed at 4.23 with a **raw `UObjectBase const*` key**, character-
  identical to 4.24 — so the key shape now holds at *every* version the feature has ever had, by
  measurement rather than interpolation. `SPARSE_DI427_1` resolves it live. The structural
  mitigation still stands and should stay: **`Aura` probes the live key shape instead of gating on
  a version number**, which is what covers licensee forks that no sample can.
- ✅ **FIXED 2026-09-26 (build 3560) by `GNAM_LLM54_1` (695) + `GNAM_IWB_1` (725)
  `[GNAMES-NONSHIP-LLM54]`.** The cause, from UE source and the PDBs: UE 5.4 added
  `LLM(FLowLevelMemTracker::Get().FinishInitialise());` to `GetNamePool()`'s one-time init. LLM is
  compiled out of Shipping, so only non-Shipping codegen changed: two calls now follow the FNamePool
  ctor, the pool sits in a callee-saved register, and the init is no longer inlined into the FName
  callers. LLM54_1 anchors on that init (every hit truth on 5.4 / 5.6 / 5.7.4 / 5.8.3 Dev + DbgGame,
  0 hits on every Shipping build and before 5.4, 0 decoys over the 65-program archive corpus); IWB_1
  on `FName::IsWithinBounds` (Pool+8, adjustment -8), byte-identical 4.23 -> 5.8.3. Live red -> green
  on DumperTest 5.4 Dev and DumperTest58 5.8.3 Dev (V1 -> LLM54_1, same address; GNames step 7.6 ->
  5.0 s and 8.4 -> 6.5 s), Shipping 5.4 / 5.8.3 unchanged. The priority is deliberately low (the
  maintainer's rule: few games ship non-Shipping), so 15-16 Pass-2 scans remain before it; at 101 the
  walk simulation gives 0. Evidence: `out/gnames_mine` (workflow `wf_8c5dd600-b4f`), `out/llm54_live`.
  The original record, as it stood:
- **GNames all but collapses on a non-Shipping UE5 build.** It is **config**, not a version
  regression: every Shipping build tested resolves normally, so **no shipped game is affected**.
  **The boundary is 5.3 → 5.4** — BISECTED 2026-07-29 with stock ThirdPerson builds either side:

  | version | non-Shipping GNames | lands on | wasted |
  |---|---|---|---|
  | 5.3 Dev + DebugGame | **15/15 patterns correct** | `GNAM_ES53_1` | **0** |
  | **5.4.4 Dev + DebugGame** | **1/6** | `GNAM_V1` | **2,240** |
  | 5.7.4 DebugGame | 1/8 | `GNAM_V1` | 2,199 |

  5.4 is already the full collapse, indistinguishable from 5.7.4/5.8.x, and both 5.4 configs report
  the identical 2,240 (UE builds DebugGame's engine modules optimized like Development). Earlier
  wordings of this line said "between 4.27 and 5.7.4" and then "somewhere in 5.4–5.6"; both are
  superseded. **If a fix pattern is ever mined, mine it against 5.3-vs-5.4** — the smallest interval
  containing the change, with a clean control on one side.

  ⚠ **THIS ENTRY WAS WRONG TWICE BEFORE THE SWEEP SETTLED IT — the sequence is the lesson.**
  First written as "a 5.8 thing" (5.7.4 disproved it). Then as "**all 37 patterns miss, n=0**" —
  also wrong: the n=0 came from a byte-replay harness printing only the **top 4 candidates by
  voter count**, and the true VA had exactly ONE voter, ranked off the bottom. Then as
  "unreachable" — wrong a third time, because the full sweep replays the *validator walk* and
  shows it **RESOLVES CORRECTLY**. Final, sweep-verified position:

  | oracle | GNames verdict |
  |---|---|
  | UE5.7.4-StackOBotDbgGame | ⚠️ `GNAM_V1` after **2,199** wasted validations |
  | UE5.4-ThirdPersonDev / DbgGame | ⚠️ `GNAM_V1` after **2,240** (each) |
  | UE5.8-StackOBotDbgGame | ⚠️ `GNAM_V1` after **2,369** |
  | UE5.8.1-StackOBotDev | ⚠️ `GNAM_V1` after **2,372** |
  | UE5.8-TitanDbgGame | ⚠️ `GNAM_V1` after **2,424** |

  crossing `GNAM_CT3`, `G42_1`, `CT4`, `V5`, `V2` each time. **These are the six most expensive
  fall-throughs in the entire corpus** — the next worst is 475 — so the cost is real even though
  the answer is right. `scan_patterns.java` on the real import shows why:

  | pri | pattern | hits | ok | decoy | verdict |
  |---|---|---|---|---|---|
  | 700 | `GNAM_CT3` | 1 | 0 | 1 | DECOY-ONLY **← the walk lands here** |
  | 710 / 720 | `GNAM_G42_1` / `GNAM_CT4` | 1 | 0 | 1 | DECOY-ONLY |
  | 850 / 860 | `GNAM_V5` / `GNAM_V2` | 1161 / 1199 | 0 | all | DECOY-ONLY |
  | **870** | **`GNAM_V1`** | **198** | **1** | 197 | **OK-BEHIND** |
  | 880 / 890 | `GNAM_V3` / `GNAM_V4` | 768 / 1040 | 0 | all | DECOY-ONLY |

  So the honest statement is: **exactly one pattern reaches truth — `GNAM_V1`, the 4-literal-byte
  `lea rsi,[rip]; jmp` at priority 870 — behind 197 of its own decoys, and only after
  `ValidateGNames` has rejected ~2,300 others.** Not "unreachable"; *one degenerate pattern deep
  in the last-resort band, reached expensively.* It is an independent vindication of not pruning
  `GNAM_V1` (kept in build 2405 as "correct yet redundant" — here it is neither redundant nor
  spare capacity).

  ⚠ **`GNAM_V1` does NOT appear in REPORT.md §5 "Load-bearing"**, because that table counts only
  ✅ SELECTED-and-correct landers and these three are ⚠️ fall-throughs. So §5 alone would let you
  prune a pattern that is the sole correct answer on three oracles. **Read §1 and the fall-through
  list too before pruning anything.**

  **Two harness lessons, worth more than the finding itself:** never read "not in the top N" as
  "absent"; and a byte-replay says what *hits*, only the sweep says what the *validator walk
  lands on*. Three successive wrong framings here all came from substituting the former for the
  latter.

  Nine builds:

  | engine | project / config | GNames | SparseDelegates |
  |---|---|---|---|
  | 4.23.1 | Flying, DebugGame | ✅ n=16 | ✅ n=4 |
  | 4.27.2 | Flying, Development | ✅ n=16 | ✅ n=4 |
  | 4.27.2 | Flying, DebugGame | ✅ n=16 | ✅ n=4 |
  | **5.3** | **ThirdPerson53, Development** | ✅ **n=15** | ✅ n=3 |
  | **5.7.4** | **StackOBot, DebugGame** | ⚠ **`GNAM_V1` only** | ⚠ **n=1 — `MEL55_1` ALONE** |
  | **5.8.0** | **StackOBot, DebugGame** | ⚠ **`GNAM_V1` only, OK-BEHIND ×197** | ⚠ n=2 (`X1`+`X2`) |
  | **5.8.0** | **Titan, DebugGame** | ⚠ **`GNAM_V1` only** | ⚠ n=2 (`X1`+`X2`) |
  | **5.8.1** | **StackOBot, Development** | ⚠ **`GNAM_V1` only** | ⚠ n=2 (`X1`+`X2`) |
  | 5.7.4 / 5.8.0 / 5.8.1 | Shipping | ✅ n=11 | ✅ n=3 |

  The 5.8.0-DebugGame row is confirmed by `scan_patterns.java` on the real import; the other three
  come from the byte replay and inherit its top-N blind spot, so read them as "the 16-strong
  Shipping voter set collapses to essentially nothing", not as an exact count.

  **Not project-specific either.** Titan and StackOBot are unrelated projects built at the same
  engine version and config, and their coverage is identical *down to the individual pattern IDs*
  — GObjects `{ES53_1, GH_4, PS3, SAT426_1}`, GWorld n=13, GEngine `{DI427_1, X1, X3, X4}`, and
  the same `{CT3, CT4, G42_1}` GNames decoy cluster. That was the last alternative explanation;
  it is the build configuration and nothing else.

  **BOUNDARY HALVED 2026-07-29 — it is between 5.3 and 5.7.4.** A self-built stock 5.3
  ThirdPerson (three configs) was added precisely to bisect this, and its Development row comes
  back **healthy**: GNames n=15, sparse n=3 (`ES2_1`+`X1`+`X2`, i.e. `ES2_1` still reaches it,
  unlike 5.7.4+). So the collapse begins somewhere in **5.4 / 5.5 / 5.6**, which remains untested.
  Do not write it up as "a 5.7+ thing" without measuring one of those three.

  Not a scan artifact: the same harness reproduces the 5.8.0-Shipping consensus (`149eba940`,
  n=11) exactly, `GNAM_V7` (CallFollow) takes 0 hits, and the Development EXE's 227 exports
  contain no `FName` symbol, so `GNAM_EXP_*` has nothing to work with either.
  **Root cause is a hardcoded destination register — the `GOBJ_V1`-on-DropIn failure mode, one
  target over.** There are 46 rip-relative xrefs to `NamePoolData`; the dominant shape is the
  twin-LEA lazy init the family already targets:
  ```
  74 09              jz  +9
  48 8d 1d <d32>     lea rbx,[NamePoolData]     <- initialized path   (also 4c 8d 3d = r15)
  eb 2f              jmp +0x2f
  48 8d 0d <d32>     lea rcx,[NamePoolData]     <- lazy-init path
  e8  <rel32>        call FNamePool::FNamePool
  ```
  Every GNames pattern pins that FIRST lea to `48 8d 05` (rax) / `4c 8d 05` (r8) / `48 8d 15`
  (rdx) / `48 8d 35` (rsi) / `48 8d 2d` (rbp). **None admits rbx or r15.** A nibble-masked
  `4? 8d ?? <d32> eb ?? 48 8d 0d <d32> e8` covers it — but `48 8d ??` is only 2 literal bytes at
  the head, so it must clear the full 65-program gauntlet before it goes anywhere near the table
  (rule 1 + the `GWLD_G42_4` counter-example). **NOT mined — deliberately.**
  Priority is genuinely low: nobody attaches the dumper to a Development build of a template
  project. The value is that it names a real blind spot in the GNames family, and it is the
  reason the `UE5.8.1-StackOBotDev` row exists.
- **The "redundant" sparse patterns earned their keep — and the sweep now says so formally.**
  `SPARSE_MEL55_1` and `SPARSE_X1` are listed in REPORT.md §5 **Load-bearing** as of 2026-07-29:
  `MEL55_1` is the selected-and-correct pattern on `UE5.7.4-StackOBotDbgGame` and **nothing else
  reaches sparse there at all**; `X1` holds both 5.8 DebugGame rows. All three (`X1`/`X2`/`MEL55_1`)
  were added as pure redundancy against Shipping binaries that already resolved — rule 5 was the
  only argument for keeping them, and they looked like dead weight at the time. **Had any one been
  pruned, a whole build configuration would silently have lost sparse-delegate support.** This is
  the strongest evidence in the file for that rule.
- **FF7 Rebirth's SparseDelegates** exists (proved from `.rdata`: `SparseDelegateFunction`,
  `MulticastSparseDelegateProperty`, the `SparseDelegateReport` console command) but no pattern
  finds it. Lead for whoever picks it up: find the `SparseDelegateReport` string xref and follow
  the `FAutoConsoleCommand` handler.
- ~~**Avowed (5.3)** sparse: zero hits.~~ **CLOSED 2026-07-27** — `SparseDelegates = 0x14B5BD9A8`,
  found structurally (the `SparseDelegateReport` string does not exist in this binary), structure
  is stock UE 5.3, `SPARSE_AV53_1` added at priority 170.
- ~~**Sparse `n=1`** on five binaries~~ **MOSTLY CLOSED 2026-07-27** by `SPARSE_X1`/`X2`, mined on
  Grimhook: Everspace 2 5.5/5.5b 1→2, Satisfactory 5.2/5.6 CoreUObject 1→3, CrashReportClient
  1→3, Grimhook 1→3. **Avowed 5.3 is now the only `n=1` left** — `SPARSE_AV53_1` is its sole
  reach, so a patch that moves that site takes sparse support on Avowed with it.
- **`SPARSE_PAL51_1` is Palworld-specific, not "UE 5.1".** It takes **0 hits** on Grimhook, a real
  symbolised 5.1. Per rule 5 a MISS is not counter-proof, so it stays — but do not read its
  `PAL51` provenance as version coverage. The 5.1 sparse coverage that actually works is
  `SPARSE_ES2_1` (+ now `X1`/`X2`).
- ~~**4.11 / 4.13 GWorld reaches truth only via `GWLD_G42_1` at priority 880**~~ **CLOSED
  2026-07-27 by `GWLD_FD_1` (priority 265).** `UWorld::FinishDestroy`'s read-then-conditional-
  write-back of the same global — PDB-confirmed on HeliumRain 4.20, DropIn 4.24 and DropIn 4.27,
  22 bytes / 12 literal. Result: **21 hits, 16 UNIQUE-OK, ZERO decoys on all 46 programs**, never
  more than 1 hit on any binary, and it appears in neither the hotspot nor the dead-weight table.
  It became the lander on four binaries, three of which improved and none of which regressed:
  4.11 Nekopara (`G42_1`, 5 wasted → 0), 4.13 Fantasynth (`G42_1`, 6 → 0), 4.26 Satisfactory
  Engine (`SF_2`, 2 → 0), 5.2 Satisfactory Engine (lateral from `SF_2`, 0 → 0). **The GWorld
  fall-through list is now empty.** `GWLD_SF_2` is no longer the lander anywhere but still reaches
  truth on both Satisfactory DLLs, so it is redundancy and stays (rule 5).
  > **READ THIS BEFORE CONCLUDING THE PATTERN WAS UNNECESSARY.** The *baseline* sweep showed 4.11
  > and 4.13 already ✅/⚠️ correct — this harness has the truth and walks past a decoy, whereas the
  > live `ValidateGWorldBasic` is deliberately loose and ACCEPTS the first one handed to it. Both
  > titles were landing on a wrong GWorld in-game (Nekopara via `SAT52_1`→`1423C9940`, Fantasynth
  > via `SF_2`→`14288E648`) and were rescued only by instance-scan recovery. **The sweep cannot
  > show that class of bug at all** — it can only show that the fix pre-empts the decoy patterns
  > in priority order, which at 265 (ahead of `SF_2` 300 and `SAT52_1` 365) it does. Section 1's
  > model note says the same thing in general terms; this is the worked instance of it.
- ~~**`GWLD_TQ_1` sits at priority 210**~~ **DONE 2026-07-27 — promoted 210 → 101.** Measured
  first: it wins on **6 of 16** oracles (no other GWorld pattern wins more than 2) and has **zero
  decoys anywhere** (10 UNIQUE-OK, 6 NO-TRUTH, 23 MISS). The saving is a whole `.text` pass, not
  a few validations — patterns scan in **batches of 8**, so order *within* a batch is free but
  crossing a batch boundary costs a full extra AVX2 sweep, and at 210 it sat in batch 2. What it
  displaces out of batch 1 (`GWLD_ES2_3`) wins on nothing, so the swap is free.

### Settled facts — do not re-chase these

Each cost at least one headless run to establish. Recorded so nobody spends another.

- **`out/sweep/patterns.tsv` goes stale.** Always re-run `extract_patterns.py` before scanning;
  a cached tsv from a previous sweep put three of five analysts a commit behind on a priority.
- **THE SWEEP DOES NOT NEED AUTO ANALYZE. Never wait for it, and never delete a project over it.**
  `scan_patterns.java` touches only `currentProgram.getMemory()`, `mem.getBytes()` and
  `getImageBase()` — no `FunctionManager`, no `Listing`, no `SymbolTable`. It scans raw bytes and
  computes RIP targets arithmetically, which is why `sweep.sh` already passes `-noanalysis
  -readOnly`. A *huge* non-Shipping build (a 300 MB Development EXE with a `.uedbg` section) can
  take hours to auto-analyze and it buys the sweep **nothing**. Import with analysis off:
  ```bash
  analyzeHeadless "$GHIDRA_PROJS" <ProjectName> -import "<path>\<file>.exe" -noanalysis
  ```
  One at a time — a Ghidra project takes an exclusive lock, so concurrent imports into the same
  project fail with `LockException` and only the first gets in.
  **MEASURED, and it is the corpus's biggest disk lever:** the same 49 MB binary imports to
  **169 MB in 46 s** with `-noanalysis` versus **1,369 MB** fully analysed — **8.1×, i.e. 88% of a
  `.rep` is analysis output the sweep never reads** — and `scan_patterns.java` returns the
  *identical five verdicts* from the raw import. Full write-up + caveats in
  [corpus-preservation.md](../../docs/corpus-preservation.md) §"re-import without analysis".
  Analysis is only worth its time when you are going to *read* the program — the ten scripts that
  need it (`dump_func`, `decompile_functions`, `find_callers`, `dump_xrefs2`,
  `dump_global_xref_aob`, `find_gobjects`, `dump_vtables`, `dump_types`, `pe_probe`, `probe`) are
  all pattern-mining tools. Analyse into a throwaway project and delete it afterwards. The five
  that need nothing are `scan_patterns`, `find_syms3`, `dump_dataat`, `scan_strings`, `verify_aob`
  — though `find_syms3` does need the PDB *applied*, which `-noanalysis` skips.
  For ground truth on a binary that ships a PDB you no longer need Ghidra at all — see step 0 of
  the derivation recipe.
  *(This was learned the expensive way: a 5.8 DebugGame project was deleted because auto-analyze
  would not finish, when the import never needed it.)*
- **A real game patch moves every global and breaks NOTHING.** Measured 2026-07-29 on Palworld
  across a live Steam update (2026-07-15 → 07-29, md5 `fb10d568…` → `a2dadf69…`, +11,776 bytes):
  every target shifted — GObjects/GNames/GWorld/GEngine by exactly **+0x3300**, SparseDelegates by
  **+0x3180** (two `.data` growth groups) — while the **voter sets came back character-identical**
  (GObjects n=6 + n=6 on the `+0x10` alias, GNames n=12, GWorld n=14, Sparse n=4, GEngine n=4),
  `SPARSE_PAL51_1` included. This is the second answer to "does a pattern survive a game update?"
  after the ES2 5.5 cross-build pair, and the stronger one, because it is a real patch to a
  shipped title rather than two builds of the same version. **Do not re-measure this per patch.**
  Corollary worth knowing: the archived binary is the corpus build (its SparseDelegates consensus
  is `148fb66b0`, the exact address hardcoded in `Himmel.h`'s `SPARSE_PAL51_1` note), so the
  `.rep` and the manifest stay valid — but `preflight.py --verify-hash` now reports Palworld as
  `id=MISMATCH` against the **live** Steam install, correctly. Re-point it at the backup by
  re-running `build_corpus_manifest.py`; do not hand-edit the generated manifest.
- **The pre-4.11 support floor is MEASURED, and it has two independent causes.** UE 4.10.4 joined
  the corpus 2026-07-29 (`UE410_Game_Shipping` / `..._Development`, both full-PDB), and **GObjects
  scores 0 on both — correctly. Leave it ❌.**
  1. *It cannot be found.* At 4.10 the array is a **function-local static behind a magic-static
     guard** inside `GetUObjectArray()`. Consumers reach it with a `call`; the address is never
     materialised inline, and all 52 `GOBJ_*` patterns are `lea reg,[rip+GUObjectArray]`-shaped.
     4.11 promoted it to a plain `GUObjectArray` global, which is why Nekopara (4.11) resolves.
     Measured: 74 GObjects candidates on Shipping / 105 on Development, and the true VA and its
     `+0x10` alias are in **neither list at any rank** — not merely outside the top N.
  2. *It could not be read even if handed over.* Per `4.10.4-release` source, `TUObjectArray` is
     `TStaticIndirectArrayThreadSafeRead<UObjectBase, 8M, 16384>` and **`FUObjectItem` does not
     exist** — elements are bare `UObjectBase*`. Neither the Flat-Base nor the Chunked preset
     models that, and stride auto-detection has nothing to detect.
  So do not "fix" 4.10 by mining a `GetUObjectArray`-shaped pattern: finding the address buys
  nothing without a third array preset. **GNames/GWorld/GEngine all resolve normally**, so the rows
  still earn their scan as the corpus's oldest coverage for those three.
  Truth for GObjects came from disassembling `GetUObjectArray` (@`14023c2e0` Shipping /
  `14067d730` Dev): the guarded init does `lea rbx,[rip+X]`, passes rbx as `this` to
  `??0FUObjectArray@@QEAA@XZ`, returns rbx. Independently confirmed by
  `GetObjectArrayForDebugVisualizers`, which is literally `GetUObjectArray(); add rax,0x10` — that
  **measures** `ObjObjects@+0x10` at this version instead of inheriting it.
- **DropIn's 32-byte `FUObjectItem` is a CONFIG artifact, not a 4.27 trait.** Proven by two
  independent symbolised 4.27 binaries (Breeders, Maelstrom) carrying the stock 24-byte item.
- **A small `.msvcjmc` section does NOT mean a Development build.** Breeders and Maelstrom have
  one at 8 bytes VirtualSize, LightMaze 512 — one stray `/JMC` translation unit, not engine-wide
  instrumentation. All three still ship the stock 24-byte chunked item, and it cost zero patterns.
- **`tdb` @ `0xFF00000000` is a Ghidra loader artifact, not a PE section.** Four analysts have now
  investigated it independently; one dumped the on-disk section table to disprove it.
- **The default PDB loader is fine.** MSDIA remains a *Meltopia-only* workaround, not a habit.
- **One-second test for the UE 5.8 reflection layout, from the PDB alone:** grep for
  `??1FFieldClass@@UEAA@XZ`. The `U` is MSVC's mangling for a **virtual** member — 5.8 made
  `~FFieldClass()` virtual, which puts a vfptr at `FFieldClass+0x00` and moves `FName Name` to
  `+0x08`. Pre-5.8 exports `??1FFieldClass@@QEAA@XZ` (`Q` = non-virtual, Name at `+0x00`). A
  `??_7FFieldClass@@6B@` vftable symbol is the same signal. Confirmed on the StackOBot 5.7.4/5.8
  pair, which differ in nothing else.
  **Offline only — do NOT turn this into a runtime probe.** Monolithic shipping games export
  nothing, so `GetProcAddress` cannot see it (a *modular* build would, since the dtor is
  `COREUOBJECT_API`, but that is the minority case). More importantly it would key on the wrong
  thing: with `UE_WITH_CONSTINIT_UOBJECT` enabled `FFieldClass` becomes abstract with per-type
  `TFieldClass<T>` vftables — **Name is still `+0x08`**, but any vtable-shape test breaks. The DLL
  instead probes the *value* (`DynOff::PickFFieldClassNameOffset`, two safe reads once per
  process, accepts whichever offset yields a `*Property` name), which is version-, fork- and
  constinit-agnostic.
- **These two StackOBot Shipping PDBs carry public symbols + line info but only PARTIAL merged
  type info.** `FFieldClass`, `UObjectBase` and `FUObjectItem` have **no TPI record at all**;
  `FField`/`FProperty`/`UStruct` are forward-references only. Layout recovery here is public
  symbols + capstone, never `LF_FIELDLIST`. Do not burn another session discovering this.
- **NO version has a usable `NamePoolData` symbol, and the boundary is not where it looks.**
  Pre-4.23 has no `FNamePool` at all, so go straight to `FName::GetNames`. But **4.23 itself has
  NEITHER** — zero occurrences of `NamePoolData` *and* zero of `?GetNames@FName@@` in all three
  config PDBs — so both halves of this rule fail at exactly the transition version. At 4.23+ the
  route that works is `FNameDebugVisualizer::GetBlocks` minus 0x10.
- **Pre-4.23 also has no sparse delegates.** It also has no sparse delegates: 10/10 `SPARSE_*` MISS on a 4.20/4.21 binary
  is the CORRECT answer, and raw ASCII+UTF-16 data-section scans for `SparseDelegate` have already
  been run on both and returned zero.
- **The CallFollow body scan WAS UNBOUNDED, and one live success was accidental.** Until build
  2500 `ScanFunctionBodyForRipRef` scanned a flat 256 bytes with no extent check, so on a short
  callee it read into the next function. Measured: **17 of 19 callees in the corpus overrun**. On
  UE 4.23 `GNAM_V7` followed a 124-byte `FName` ctor and resolved GNames from a ref at **+0x94** —
  132 bytes past the end, inside a DIFFERENT `FName` constructor's lazy-init prologue. Now bounded
  via `Macht::GetFunctionExtent` (`RtlLookupFunctionEntry`, clamped with `end - funcAddr` because
  the target is often mid-entry; a NULL return means leaf *or* stripped xdata — indistinguishable —
  and keeps the old 256). **LIVE-VERIFIED on STVoyager 5.6**, which was the one case that could not
  be settled offline: `GNAM_V7 hits=1 (not validated)` — the out-of-bounds ref is now unreachable —
  and the scan fell through to `GNAM_V8`, which resolves correctly. The `FuncBodyScan` log line now
  states `bounded by .pdata` or `UNBOUNDED (leaf/no-xdata)`, so a future overrun is visible.
- **`GNAM_V7` (CallFollow) and the three `GNAM_EXP_*` (SymbolCallFollow) are UNMEASURABLE by this
  harness**, not worryingly unknown: `scan_patterns.java` scans byte patterns only, and the `EXP`
  ones cannot fire on a monolithic EXE because nothing is exported.

### The strongest truth source is not a binary — it is `vendor/UnrealEngine`

`vendor/UnrealEngine` is a **full-refspec `blob:none` partial clone**, so every release tag back to
4.0 is reachable and blobs fetch on demand. For any *structure* question — field order, when a
type was introduced, what a member is actually called — read Epic's source instead of inferring
it from disassembly:

```bash
cd vendor/UnrealEngine
git show 4.11.0-release:Engine/Source/Runtime/CoreUObject/Public/UObject/UObjectArray.h
```

This settled in minutes what several headless runs could only bracket:

| tag | `TUObjectArray` | `FUObjectItem` |
|---|---|---|
| 4.10.2 | `TStaticIndirectArrayThreadSafeRead` | **does not exist** |
| 4.11.0 / 4.12.5 | `FFixedUObjectArray` | 16 B — `Object` / **`ClusterAndFlags`** / `SerialNumber` |
| 4.13.0 … 4.19.2 | `FFixedUObjectArray` | 24 B — `Object` / `Flags` / `ClusterIndex` / `SerialNumber` |
| 4.20.3 + | `FChunkedFixedUObjectArray` | 24 B |

Two traps in that file specifically: there is a **commented-out `//typedef TStaticIndirect…
TUObjectArray;` immediately above the live one** in every version (grep must exclude `^\s*//`),
and `ObjObjects` is declared as `TUObjectArray ObjObjects;`, so the typedef is the only thing that
tells you flat from chunked.

**What source CANNOT tell you**, and why the binaries still matter: which address a pattern
resolves to, what the compiler actually emitted, whether a licensee forked the layout, and
anything about noise. Use source for "what is the structure", binaries for "where is it and can
we find it".

### Recipe — pre-4.23 GNames on a binary with NO symbols

`TNameEntryArray` is `128*8+8 = 0x408` bytes, so scan `.text` for `mov ecx,0x408` and take the
`mov [rip+disp],rax` within ~64 bytes. On Freud Gate's 31 MB `.text` that gave 12 candidates and
exactly one with a rip store — `FName::GetNames`. **Live lead for the three 4.18 rows that leave
GNames unset on purpose** (DQ XI S, Octopath, FF7 Remake), with the caveat that 4.18's chunk-table
size may not be `0x408` — confirm it from the memset / field offsets rather than hardcoding.
Note also that a pre-4.23 binary carries TWO different chunk sizes: Freud Gate's `FUObjectArray`
is 65536 elems/chunk (`sar 0x10`) while its `TNameEntryArray` is 16384 (`sar 0xE`). Do not conflate.

### Thinnest coverage in the table

**Pre-4.23 GNames rested on ONE SHAPE MATCHED TWICE — worse than "two patterns".** Measured
2026-07-28: `GNAM_G42_1`'s byte string is a **strict superset window of `GNAM_CT3` offset by +4**
(`CT3[4:]` equals `G42_1` except that `G42_1` wildcards the two bytes `CT3` pins as literal
`00 00`), so every `CT3` match at A implies a `G42_1` match at A+4 — verified token-by-token and
empirically on all 36 programs. They credited a redundancy that never existed.
**Mitigated 2026-07-28 by `GNAM_XX_1` (717)**, the success-path write-back + epilogue of the same
function: 8/8 pre-4.23 oracles with the correct hit at index 0, the only pattern in the file that
covers all of them, zero spurious-correct on 36 programs. It is a different SITE, not a different
FUNCTION — an xref census found the only other referencing functions are inline expansions of
`GetNames` carrying the identical shape, and a caller-side anchor was mined and REFUSED (the
`and edx,0x3FFF` idiom sits at distance {11,12,27,30} on 4.15.3 but {14,19,20} on 4.22, so no
fixed-offset AOB spans the band). That is the most independence the engine offers.
The original note follows, still accurate about the failure mode: Both are OK-BEHIND, both in batch
3, confirmed identical on HeliumRain *and* Freud Gate. A compiler change to that prologue takes
GNames on every pre-4.23 title at once. This is the sparse-`n=1` situation again, on a different
target; if a third pre-4.23 sample ever arrives, mining a structurally different anchor there is
the highest-value thing to do with it.

**Pre-4.20 GWorld used to be thinner still and no longer is.** On 4.11 and 4.13 the only pattern
that reached truth was `GWLD_G42_1` — 7 literal bytes, priority 880, the last-resort read form —
i.e. one degenerate shape standing between those titles and no GWorld at all. `GWLD_FD_1` (265,
`UWorld::FinishDestroy`) makes it two, from a structurally different site. Do not re-mine this.

**Why this file exists:** re-deriving these costs a headless run per binary, and getting one
wrong silently corrupts every verdict downstream. It has already happened once — a placeholder
truth value made two good GEngine patterns look like they produced five decoys, and they were
demoted on that basis. `scan_patterns.java` now prints `NO-TRUTH` instead of `DECOY-ONLY` when
it has no plausible truth, but the real fix is to keep the values written down.

All addresses are **image-based VAs** as Ghidra shows them (preferred base, not runtime).

## The sweep

| Tag | Project (`$GHIDRA_PROJS\*.rep`) | UE | Symbols | Notes |
|---|---|---|---|---|
| UE4.18-FF7R | `FF7R` | 4.18+ | ❌ none | GObjects+GEngine truth **derived by disassembly** — see below |
| UE4.11-Nekopara | `NEKOPALIVE_UE411` | 4.11.0-pre7 | ❌ none | **oldest in the corpus.** FLAT array at the BASE anchor, `FUObjectItem` = **16 B** |
| UE4.13-Fantasynth | `Fantasynth_UE413` | 4.13 | ❌ none | FLAT array at the BASE anchor, 24-B item. VR title, cannot be run |
| UE4.18-DQXIS | `DQ_XI_S` | 4.18 | ❌ none | truth by disassembly; ASLR base recovered; **GNames absent on purpose** |
| UE4.27-DQ7R | `DQ7R` | 4.27 | ❌ none | truth by disassembly; **GEngine ≠ the live consensus** — see below |
| UE5.4-Elliot | `Elliot` | 5.4 | ❌ none | truth by disassembly; the corpus's **only UE 5.4** sample |
| UE4.15-Flying | `UE415_Flyinh-Win64-Shipping` | 4.15.3 | ✅ full PDB | **built by us** (Shipping, monolithic). **The OLDEST symbolised binary in the corpus** and the only oracle below 4.20 — it turns the 4.13–4.19 FLAT `FFixedUObjectArray` / 24-byte `FUObjectItem` band from source interpolation into measurement. Project name misspells "Flying" as **"Flyinh"** |
| UE4.20-Everspace | `ES1-420` | 4.20 | ✅ full PDB | oldest sample; supersedes the symbol-less `ES1.rep` |
| UE4.20-HeliumRain | `HeliumRain` | 4.20.3 | ✅ full PDB | **second symbolised 4.20** — pre-4.23 GNames no longer rests on Everspace alone |
| UE4.21-FreudGate | `Freud_Gate_UE421` | 4.21 | ❌ none | truth by disassembly; **closes the 4.21 hole** |
| UE4.27-Breeders | `Breeders_of_the_Nephelym` | 4.27 | ✅ full PDB | |
| UE4.27-Maelstrom | `Maelstrom` | 4.27.2 | ✅ full PDB | |
| UE5.0-LightMaze | `Light_Maze` | 5.0.3 | ❌ none | truth by disassembly; **closes the 5.0 hole** (4.27 used to jump to 5.1) |
| UE4.22-Satisfactory | `Satisfactory_UE422` | 4.22 | ✅ full PDB | **monolithic EXE with symbols** — the only pre-4.25 one |
| UE4.23-Flying | `UE423_Flying-Win64_Shipping` | 4.23.1 | ✅ full PDB | **built by us** (Shipping, monolithic; note the UNDERSCORE in the project name). The only 4.23, and the FIRST version of BOTH FNamePool and sparse delegates. **Live-verified** — the packaged copy was run and all five resolved |
| UE4.23-FlyingDbgGame | `UE423_Flying-Win64-DebugGame` | 4.23.1 | ✅ full PDB | **built by us**, DebugGame. The **lower bracket** on the non-Shipping GNames gap: everything resolves here (GNames n=16), so that gap is not a property of non-Shipping builds in general |
| UE5.7.4-StackOBot | `StackOBot_Shipping_UE574` | 5.7.4 | ✅ full PDB | **built by us**, Shipping. The 5.7.4/5.8 pair is a controlled A/B — same game, same config, adjacent engines |
| UE5.8-StackOBot | `StackOBot_Shipping_UE58` | 5.8.0 | ✅ full PDB | **built by us**, Shipping. GObjects takes a SINGLE truth value (5.8 moved `ObjObjects` to +0x00). The oracle for the `virtual ~FFieldClass()` reflection break |
| UE5.8.1-StackOBot | `StackOBot_Shipping_UE581` | 5.8.1 | ✅ full PDB | **built by us**, Shipping. With 5.8.0 it forms a **patch-level** A/B — the finest-grained pair in the corpus. Identical pattern coverage on all five targets, so 5.8.0→5.8.1 moved nothing we depend on |
| UE5.8.1-StackOBotDev | `StackOBot_Development_UE581` | 5.8.1 | ✅ full PDB | **built by us**, Development — the corpus's **first non-Shipping UE5 oracle**. Exists to regress-test two config-only gaps: **GNames reaches nothing here** (all 37 patterns miss — see above) and `SPARSE_ES2_1` misses, leaving `X1`/`X2` alone. Expect ❌ GNames in the matrix; that is the point |
| UE4.27-Hogwarts | `Hogwarts_Legacy` | 4.27 | ❌ none | noise probe — **⚠ DENUVO-PACKED, no `.text` at all** (`.udata` 105 MB + `.xpdata` 274 MB). Its hits are against encrypted data and it dilutes the §6 hits/MB denominator; see the sweep.sh comment |
| UE4.24-DropIn | `DropIn_UE424` | 4.24.3 | ✅ full PDB | **closed the last checkable sparse-delegate gap** — see below |
| UE4.25-Everspace2 | `ES2-UE425` | 4.25.2 | ✅ full PDB | the FField/FProperty transition band |
| UE4.26-Satisfactory | `Satisfactory_UE426` | 4.26.2 | ✅ full PDB | modular, 4 DLLs — supersedes the unusable `Satfi426` |
| UE4.27-DropIn | `DropIn` | 4.27.2 | ✅ full PDB | Development build (32-byte `FUObjectItem`) |
| UE4.27-FlyingDev | `UE427_Flying_Development` | 4.27.2 | ✅ full PDB | **built by us**, Development. **Takes over DropIn's sole-oracle role for `GOBJ_DI427_1/2/3`** — reproduces its 32-byte `FUObjectItem` codegen exactly. Note the project name uses an UNDERSCORE and no `-Win64` |
| UE4.27-FlyingDbgGame | `UE427_Flying-Win64-DebugGame` | 4.27.2 | ✅ full PDB | **built by us**, DebugGame. The control, not coverage: UE builds DebugGame's *engine* modules optimized like Development, so its `DI427` hit counts are **identical** (832/1415/246). Safe to comment out of `sweep.sh` |
| UE4.27-FlyingShipping | `UE427_Flying-Win64-Shipping` | 4.27.2 | ✅ full PDB | **built by us**, Shipping. The NEGATIVE control that proves `DI427` is config-gated: all three score **0** here on the same source. Also the first 4.27 Shipping oracle from a known Epic-stock engine |
| UE4.27-Artisan | `The_Artisan_of_Glimmith` | 4.27 | ❌ none | monolithic noise probe |
| UE4.18-Octopath | `Octopath` | 4.18 | ❌ none | monolithic noise probe; version recovered from the `++UE4+Release-4.18` build tag + a fingerprint identical to DQ XI S |
| UE4.x-FF7Rebirth | `FF7Re` | 4.26 fork | ❌ none | the only binary that exercises `GOBJ_RE1` / `GNAM_V7` |
| UE5.1-Grimhook | `Grimhook` | 5.1.1 | ✅ full PDB | **the first symbolised 5.1** — see below |
| UE5.1-Palworld | `Palworld` | 5.1 | ❌ none | monolithic noise probe; its consensus is now corroborated by Grimhook |
| UE5.2-Satisfactory | `SF521_pdb` | 5.2.1 | ✅ full PDB | **created by us** — see the UE5.2 note below |
| UE5.2-SatGameDLL | `Satisfactory_UE521` | 5.2.1 | ⚠ game DLL only | noise probe; project mis-imported, see below |
| UE5.3-Avowed | `Avowed` | 5.3 | ❌ none | negative control (packed 20-byte `FUObjectItem`) |
| UE5.5-Everspace2 | `ES2-0517` | 5.5 | ✅ full PDB | `0517` is a DATE, not a version |
| UE5.5-Everspace2b | `ES2_UE55` | 5.5 | ✅ full PDB | 2025-06-17 build — the same-game cross-build pair with `ES2-0517` |
| UE5.5-Meltopia | `Meltopia_V2` | 5.5 | ✅ full PDB¹ | second symbolised MONOLITHIC 5.5; supersedes `Meltopia.rep` |
| UE5.5-ManorLords | `Manor Lords` | 5.5 | ❌ none | monolithic noise probe |
| UE5.6-Satisfactory | `Satisfactory_v1.2.3.1` | 5.6.1 | ✅ full PDB | modular; **also holds a symbolised CrashReportClient.exe** |
| UE5.6-TQ2 | `TQ2` | 5.6 | ❌ none | monolithic noise probe |
| UE5.7-Solarpunk | `Solarpunk` | 5.7 | ✅ full PDB | |
| UEx-DQ12HD2D | `DQ_I_II_HD2D` | ? | ❌ none | monolithic noise probe |

¹ Meltopia's first import silently failed to apply its 347 MB PDB. The retry succeeded by
selecting the **MSDIA** PDB loader — **PDB-Universal fails on this file**. If a game ships a PDB
and the probe still reports zero UE globals, try MSDIA before concluding the PDB is unusable.
The symbol-less `Meltopia.rep` is superseded and can be deleted.

**Meltopia is also the cleanest validation of the consensus method.** While it had no symbols,
the sweep's consensus table predicted GEngine `149F002F8`, GWorld `149F03D10`, GObjects
`149D87430` and GNames `149CA3C80`. Once the PDB applied, the symbols gave `149f002f8`,
`149f03d10`, `149d87420` (+0x10 = `149d87430`) and `149ca3c80` — **all four exact**.

### UE 4.24 closed the sparse-delegate question

`DropIn_UE424` carries a `FSparseDelegateStorage::SparseDelegates` symbol, and its mangled name
demangles to

    TMap<UObjectBase const*, TMap<FName, TSharedPtr<TMulticastScriptDelegate<FWeakObjectPtr>>>>

— a **raw pointer key**, identical to 4.25 / 4.26 / 4.27 / 5.x. Sparse delegates were introduced
in 4.23, and **4.23 itself is now verified too** (see the UE4.23-Flying row): its PDB carries the
same raw-pointer key, so the shape is measured at every version the feature has existed.
`Aura`'s walker still probes the live key shape rather than gating on a version number; keep it
that way — that is what makes 4.23 and any licensee fork safe without a binary to test against.

### Oracles vs noise probes — why both

A row with truth answers *"does it resolve to the right address?"*. A row without answers only
*"did anything hit that should not have?"* — but that second question needs **monolithic** game
EXEs. A Satisfactory engine DLL is 4–30 MB of `.text`; a shipped game EXE is 100–200 MB of
engine + game + middleware. Per-pattern collision counts measured only on modular DLLs
understate real-world noise several-fold, which is why the report normalises to hits/MB and
calls out the monolithic-only figure separately.

### Broken imports you will see

Several supplied projects contain duplicate program entries with image base `0000:0000`, 0
functions and 1 symbol — failed imports sitting beside a good one **under the same name**.
`scan_patterns.java` skips any program with zero executable bytes and keys its output files on
`tag + program + image base`; without that the broken duplicate silently overwrote the real
results (this cost us the 5.6 Engine DLL on the first pass).

### `Satisfactory_UE521` — mis-imported, and how it was fixed

Only ONE program in it is genuinely UE 5.2: `FactoryGame-FactoryGame-Win64-Shipping.dll` (the
*game* module, which holds no engine globals). Its `Core` / `CoreUObject` / `Engine` entries are
**duplicates of the UE 4.26.2 DLLs** — their recorded `executablePath` points at
`…\Satisfactory\UE4.26.2\Engine\Binaries\Win64\` and their function/symbol counts match the 4.26
project exactly. Four further entries are broken empty imports.

Fixed by importing the real files into a **separate** project so the original stays untouched:

```bash
for M in CoreUObject Core Engine; do
  analyzeHeadless "$GHIDRA_PROJS" SF521_pdb \
    -import "D:/tmp/Game archive/Satisfactory/UE5.2.1/Engine/Binaries/Win64/FactoryGame-$M-Win64-Shipping.dll"
done
```

Run these **one at a time**: a Ghidra project takes an exclusive lock, so concurrent imports into
the same project fail with `LockException` and only the first gets in.

### `ES2-0517` needs a one-time language upgrade

It was created by an older Ghidra. Opening it triggers "Updating language version", which
`-readOnly` cannot save — the run stalls and the script never executes. Run it **once without
`-readOnly`** (`-noanalysis` is fine) to persist the upgrade, then the normal read-only sweep
works. If a previous read-only attempt was killed mid-upgrade it leaves `<project>.lock` behind;
delete the stale `*.lock` / `*.lock~` after confirming no `java.exe` holds that project.

### `Satfi426` — superseded, do not re-chase

`Satfi426.rep` contains only three *game* DLLs; the modules that **define** the globals were
never imported, so it has no ground truth and no engine code to mine. Its game module does carry
real IAT slots (`__imp_?GUObjectArray@@3VFUObjectArray@@A` @ `0x180722950`,
`__imp_?GWorld@@3VUWorldProxy@@A` @ `0x180727CB8`, `__imp_?GEngine@@3PEAVUEngine@@EA` @
`0x180727CB0`) but all 490 referencing sites are Coffee Stain's own code (`UFG*`/`AFG*`/`FFG*`),
so any pattern mined there would match Satisfactory and nothing else.
**`Satisfactory_UE426` replaces it entirely** — same engine version, all four modules, full PDBs.
Note `find_syms3.java` deliberately does **not** filter `__imp_*`; without that the IAT is
invisible and a modular build looks empty.

## Deriving truth for a new game

00. **Adding a VERSION rather than a game? Look for the engine's own prebuilt targets before you
    package or compile anything.** A launcher-installed engine ships monolithic game binaries
    **with full PDBs** in `Engine/Binaries/Win64`, named `UE4Game*.exe` (UE4) or `UnrealGame*.exe`
    (UE5). Surveyed 2026-07-29 across the installed engines:

    | engine | Shipping | Development | DebugGame |
    |---|---|---|---|
    | 4.15 / 4.10 | ✅ | ✅ | ✗ |
    | 4.23 / 4.27 / 5.4 / 5.7 / 5.8 | ✅ | ✅ | ✅ |

    That is the entire oracle, free: copy the `.exe`+`.pdb`, run step 0, import with `-noanalysis`.
    **It is what made UE 4.10 possible at all** — 4.10 needs VS2015, which is not installed, and no
    project can be compiled for it. It is also the cheap route for any version where the C++ path
    fights back (5.3's UBT `FamilyRank` failure). Packaging a Blueprint project is only worth it
    when you need a *game-shaped* binary (game modules, cooked content); for engine globals these
    are the same engine code.
    ⚠ These are Epic's own builds, so they are Epic-stock by construction — good for engine truth,
    useless for questions about how a *licensee fork* or a third-party build behaves.

0. **If the binary ships a PDB, skip Ghidra entirely** — `py tools/pe/pdb_globals.py <file.pdb>`
   prints all five globals and a paste-ready `GS_TRUE=` line in about two seconds. It decodes the
   MSF publics stream itself (no deps), maps `(segment, offset)` through the PDB's own section
   headers, emits the `base|base+0x10` GObjects pair, and recovers GNames by disassembling
   `FNameDebugVisualizer::GetBlocks` and printing the bytes it read so the `-0x10` stays
   checkable. Two flags matter: `--no-gobjects-alias` on **UE 5.8+** (5.8 moved `ObjObjects` to
   `+0x00`, so `base+0x10` is `NumChunks` and the alias would score an int32 as correct), and
   `--grep <str>` to hunt decoys or confirm a symbol is absent.
   **It is validated by reproducing two rows in this table byte-for-byte** — UE4.23-Flying and
   UE5.8-StackOBot, including the `GetBlocks @0x14062c010` / `48 8d 05 f9 83 82 02 c3` detail
   recorded in `sweep.sh`. Re-run those two after touching it; a decoder that drifts silently is
   exactly the failure this file exists to prevent.
   Then CORROBORATE independently before writing the row down — replay Himmel.h's patterns against
   `.text` and check the consensus lands on the same VA (rule 4). Values derived both ways and
   agreeing are what "double-derived" means in the `sweep.sh` comments.
1. **`probe.java` first** — confirms the project opened and whether symbols exist at all.
2. **If symbols:** read the globals straight off it. Three catches:
   - `NamePoolData` often has no symbol. Disassemble `FNameDebugVisualizer::GetBlocks` — it is
     always `lea rax,[&Pool.Entries.Blocks]; ret`, so subtract `0x10`.
   - The name pool moved from **CoreUObject to Core** at 5.6. Check both.
   - Pre-4.23 there is no `FNamePool` at all: GNames is a `TNameEntryArray*` lazily allocated
     inside `FName::GetNames`. Dump that function
     (`-postScript dump_func.java "FName::GetNames"`) and take the global it tests-and-stores.
3. **If no symbols:** run the sweep with no `GS_TRUE` and read `consensus_*.txt`. Addresses that
   **≥3 independent patterns** agree on are reliable — validated on Everspace, where the pre-PDB
   consensus matched the symbols exactly once the PDB arrived. The `-- priority walk --` block in
   the same file shows what the runtime would actually land on.
4. **If consensus is ambiguous, disassemble.** This is how FF7 Remake was cracked (below), and it
   is often faster than it sounds — one `dump_func.java` on a candidate site settles it.
5. Add the row to **`sweep.sh`**, then re-run the **whole** sweep, not just the new game.

### The FF7 Remake derivation (worked example)

FF7R has no PDB and its GObjects consensus was empty — the generic patterns each resolved
somewhere different. A candidate GEngine pattern produced exactly one hit; dumping that site
(`FUN_140FD1490`) showed:

```
  MOV    RCX,[0x145879EE8]     ; <- loaded as `this`
  CALL   FUN_1416C7CE0         ; ...returns a UWorld   => this is GEngine
  MOVSXD RAX,[RBX + 0xC]       ; UObject::InternalIndex of that UWorld
  CMP    EAX,[0x1453BD48C]     ; ObjObjects.NumElements  (Objects + 0xC)
  LEA    RDX,[RAX + RAX*2]
  MOV    RAX,[0x1453BD480]     ; ObjObjects.Objects      => GUObjectArray + 0x10
  LEA    RCX,[RAX + RDX*8]     ; 24-byte FUObjectItem stride
  MOV    EAX,[RCX + 8]         ; FUObjectItem.Flags
```

so `GEngine = 0x145879EE8` and `GUObjectArray = 0x1453BD470`. Both independently corroborated:
`GOBJ_RE2` + `GOBJ_V12` agree on `0x1453BD470`, and a second candidate shape on `0x145879EE8`.
GNames/GWorld are deliberately **left unset** — the consensus is suggestive but unproven, and a
guessed truth is worse than none (it mislabels every hit as a decoy).

## Reading the report

`aggregate_sweep.py` writes `out/sweep/REPORT.md`. Section 1 decides whether a change is safe.

**Model note — read this before trusting a verdict.** `scan_patterns.java`'s `>>> SELECTED` line
names the first pattern that *hits*. That is NOT what the runtime settles on:
`Genau::ScanForTarget` validates every match and moves on when they all fail, returning only on
the first that validates. So a `DECOY-ONLY` pattern at the top of the list is a **fall-through
(cost)**, not a wrong answer **(correctness)**. The report's regression matrix replays the real
walk and shows the pattern actually landed on plus the validations wasted getting there. Reading
the raw SELECTED line as "what we resolve to" overstates risk; ignoring the fall-through
understates cost.

| Verdict (per pattern) | Meaning |
|---|---|
| `UNIQUE-OK` | every hit resolves to the true VA |
| `OK-FIRST` | reaches truth, and the correct site is scanned before its decoys |
| `OK-BEHIND` | reaches truth, but decoys scan first — the validator must reject them |
| `DECOY-ONLY` | hits, never correct → the runtime falls through to the next pattern |
| `MISS` | no hits |
| `NO-TRUTH` | no usable truth supplied — verdict withheld on purpose, **not** evidence of anything |

One more thing the cost numbers do NOT say: patterns are scanned in **batches of 8**, and
ScanForTarget returns on the first validated match, so a pattern that wins from batch 1 avoids
every later `.text` pass. Rejecting a few hundred candidates by validation is much cheaper than
an extra AVX2 sweep of a 130 MB `.text`. Do not demote a noisy pattern that is also a *winner*.

## `GS_TRUE` reference

`GObjects` accepts two values because `ValidateGObjects` matches both the `FUObjectArray` base
and its `ObjObjects` sub-struct (base + `0x10`).

For **modular** builds every entry must carry a `programNameSubstring:` prefix. Their DLLs all
share image base `0x180000000`, so their address ranges OVERLAP — an unscoped union would score
a hit inside `Core` as a correct `GObjects`, which lives in `CoreUObject`. Use substrings that
cannot alias: `-Core-Win64` does not match `-CoreUObject-Win64`.

```sh
# UE 4.10.4 — UE4Game, the prebuilt monolithic target the LAUNCHER ENGINE ALREADY SHIPS with a full
# PDB (Engine/Binaries/Win64/UE4Game{-Win64-Shipping,}.exe). Nothing was compiled: 4.10 needs VS2015
# and it is not installed. Check for those prebuilt targets before assuming a version needs a
# toolchain. The corpus's OLDEST binary. GObjects is EXPECTED to score 0 on both rows for two
# independent reasons — see the pre-4.11 floor entry in "Settled facts". SparseDelegates absent by
# design (4.23+); GNames is the pre-4.23 TNameEntryArray*; GWorld is typed UWorldProxy here.
GS_TRUE="GObjects=1423422b0|1423422c0,GNames=14232f530,GWorld=14234edb8,GEngine=14234a450"   # Shipping
GS_TRUE="GObjects=144bdb090|144bdb0a0,GNames=144bc0d50,GWorld=144be85f8,GEngine=144be35c8"   # Development

# UE 4.18 — FF7 Remake. DERIVED BY DISASSEMBLY, not a PDB. GNames/GWorld intentionally absent.
GS_TRUE="GObjects=1453bd470|1453bd480,GEngine=145879ee8"

# UE 4.18 — DRAGON QUEST XI S. DERIVED BY DISASSEMBLY. The process is ASLR-relocated; the image
# base was recovered from FFixedUObjectArray's shape at 145d83c08 (Objects / +8 Max / +0xC Num,
# 24-byte FUObjectItem, non-chunked), so the array base is that minus 0x10. GEngine confirmed via
# UWorld::GetGameViewport + GetRealTimeSeconds; GWorld and GEngine also appear in one function.
# GNames INTENTIONALLY ABSENT — 4.18 predates FNamePool and every GNames pattern here is
# FNamePool-shaped, so the consensus is noise. No sparse delegates before 4.23.
GS_TRUE="GObjects=145d83bf8|145d83c08,GWorld=145e70c98,GEngine=145e6eeb0"

# UE 4.27 — DRAGON QUEST VII Reimagined. DERIVED BY DISASSEMBLY (all globals live in `.shared`).
# GEngine is 145ff4b28. The live runtime log's most-hit candidate, 145d76d78, is a GAME-SIDE
# singleton: GENG_X4 alone accounts for 50 of its 55 hits, and no UWorld/FWorldContext semantics
# appear at any of them. Do not "correct" this back to the runtime value.
GS_TRUE="GObjects=145ea7660|145ea7670,GNames=145e6b300,GWorld=145ff8470,SparseDelegates=145b26520,GEngine=145ff4b28"

# UE 5.4 — The Adventures of Elliot. DERIVED BY DISASSEMBLY; the corpus's only 5.4. All five
# corroborated (GetGameViewport, FNamePool::Resolve, the NotifyUObjectDeleted twin-ref that pins
# Sparse and GObjects in one function). Note this game reports 4.27 in its PE as a publisher
# fallback — it is 5.4.
GS_TRUE="GObjects=149bfc140|149bfc150,GNames=149b18600,GWorld=149d8bda0,SparseDelegates=14990e1c0,GEngine=149d8e290"

# UE 4.11.0-preview7 — NEKOPALIVE (Nekopara). The OLDEST binary in the corpus and the first that
# needed the "Flat-Base" preset: a FLAT FFixedUObjectArray presented at the FUObjectArray BASE
# (Objects@+0x10 / Max@+0x18 / Num@+0x1C), NOT at ObjObjects. FUObjectItem is 16 BYTES here —
# Object / ClusterAndFlags / SerialNumber, cluster index and flags sharing one int32 (read off
# Epic's source at 4.11.0-release; 4.13 splits them and grows to 24).
# GWorld DECOY at 0x1423C9940 — a TSharedPtr {Object, ReferenceController} singleton whose +0
# reads like a UWorld pointer, which GWLD_SAT52_1 lands on and the live validator accepts.
# Pre-4.23: GNames is a TNameEntryArray*, SparseDelegates correctly absent.
GS_TRUE="GObjects=1423c1510|1423c1520,GNames=1423b8f58,GWorld=1424a4370,GEngine=1424a2de0"

# UE 4.13 — Fantasynth. Same Flat-Base shape as 4.11 but the standard 24-byte item. A VR title
# that shows a black screen without a headset, so Ghidra is the only evidence available for it.
GS_TRUE="GObjects=142779240|142779250,GNames=14273ffd0,GWorld=1427851f0,GEngine=14277d350"

# UE 4.20 — Everspace. No FNamePool (TNameEntryArray era) and no sparse delegates (4.23+).
GS_TRUE="GObjects=142e797f0|142e79800,GNames=1431dead8,GWorld=1432e1ac0,GEngine=1432df470"

# UE 4.22 — Satisfactory (monolithic). GNames = `Names`, the TNameEntryArray* lazily new'd in
# FName::GetNames @0x140BCEBF0 (the load is at +4). Pre-4.23: no sparse delegates.
GS_TRUE="GObjects=144006f80|144006f90,GNames=144002a78,GWorld=1441073b8,GEngine=144104e58"

# UE 4.23 — the "Flying" template, SHIPPING, built by us against Epic's INSTALLED 4.23.1 Launcher
# engine (CL 9631420, IsPromotedBuild=1, IsLicenseeVersion=0 — engine objects are Epic stock, and a
# Shared build environment forbids overriding bUseChecksInShipping etc., so fork/override risk is
# nil). This is the version that introduced BOTH FNamePool and sparse delegates, so it is the
# EARLIEST binary either target can be checked against.
#
# GNames is the interesting one: 4.23 has NEITHER a `NamePoolData` symbol NOR `?GetNames@FName@@`
# (zero occurrences of both strings in all three config PDBs), so BOTH recipes this file documents
# — "pre-4.23: go straight to FName::GetNames" and "4.23+: NamePoolData often has no symbol" —
# fail at exactly this version. Taken from FNameDebugVisualizer::GetBlocks @0x14062c010
# (`48 8d 05 f9 83 82 02 c3` -> 0x142e54410), minus 0x10.
#
# Every value is TRIPLE-derived and the three agree exactly: (1) a PDB S_PUB32/S_GDATA32 decode,
# (2) a full 150-pattern byte replay of Himmel.h against .text, (3) the live run, which rebases all
# five off ONE shared ASLR base (0x7FF7ED7D0000) with ZERO residual. Ghidra was never opened.
#
# DECOYS, all three confirmed present: GCoreObjectArrayForDebugVisualizers has a PLAIN name (no
# leading `?`), so find_syms3.java — which skips only `?`-prefixed names — WILL hand it to you, and
# its RUNTIME VALUE equals the ObjObjects VA. GObjectArrayForDebugVisualizers is a C++ reference to
# that, i.e. TWO indirections off. GNameBlocksDebug holds NamePoolData + 0x10.
GS_TRUE="GObjects=142e6b968|142e6b978,GNames=142e54400,GWorld=142f6cf10,SparseDelegates=142c4d060,GEngine=142f6a8a0"

# UE 4.24 — DropIn (a second, older build of the same unplayable VR title). NamePoolData from
# FNameDebugVisualizer::GetBlocks @0x141323510 = `lea rax,[0x1471BCA10]; ret`, minus 0x10.
GS_TRUE="GObjects=1471db720|1471db730,GNames=1471bca00,GWorld=1472ea620,SparseDelegates=146da38d0,GEngine=1472e74a0"

# UE 4.25 — Everspace 2 (Steam depot). NamePoolData from FNameDebugVisualizer::GetBlocks
# @0x140EF8410 = `lea rax,[0x144497D10]; ret`, minus 0x10.
GS_TRUE="GObjects=1444b0520|1444b0510,GNames=144497d00,GWorld=1445f1160,SparseDelegates=1440070c0,GEngine=1445edad8"

# UE 4.26 — Satisfactory, MODULAR (4 DLLs in one project).
GS_TRUE="-CoreUObject-:GObjects=1803f9210|1803f9220,-CoreUObject-:SparseDelegates=1803f37d0,-Core-Win64:GNames=180659380,-Engine-:GWorld=18182a0b8,-Engine-:GEngine=181826658"

# UE 4.27 — DropIn. NamePoolData from FNameDebugVisualizer::GetBlocks
# @0x1426F59C0 = `lea rax,[0x14A363950]; ret`, minus 0x10.
GS_TRUE="GObjects=14a3aa670|14a3aa660,GNames=14a363940,GWorld=14a52ced8,SparseDelegates=149ec0910,GEngine=14a528890"

# UE 5.1 — Grimhook. The FIRST symbolised 5.1; every 5.1 claim before it rested on Palworld's
# unproven consensus. NamePoolData from FNameDebugVisualizer::GetBlocks @0x141BEEAD0 =
# `lea rax,[0x14639E150]; ret`, minus 0x10 — and independently corroborated by its 58 xrefs
# being FName::ToString / GetEntry / AppendString / FLazyName::Resolve loading it as the pool.
# Stock layout: 24-byte FUObjectItem, UObject* at +0x00, chunked (FChunkedFixedUObjectArray).
# Version confirmed structurally, not from the label: the PDB's EUnrealEngineObjectUE5Version
# terminates at ADD_SOFTOBJECTPATH_LIST = 1008, which is exactly 5.1 (5.0 stops at 1004,
# 5.2 adds 1009).
GS_TRUE="GObjects=14643d800|14643d810,GNames=14639e140,GWorld=1465aa438,SparseDelegates=1461417d0,GEngine=1465a6630"

# UE 5.2 — Satisfactory, MODULAR (project SF521_pdb, built by us — see above). Cross-checked
# against the DLL export table: GEngine RVA 0x1CD1140, GWorld 0x1CD4828, GUObjectArray 0x4194D0.
GS_TRUE="-CoreUObject-:GObjects=1804194d0|1804194e0,-CoreUObject-:SparseDelegates=1803edcb0,-Core-Win64:GNames=18073d0c0,-Engine-:GWorld=181cd4828,-Engine-:GEngine=181cd1140"

# UE 5.5 — Everspace 2. Needs the one-time language upgrade described above.
GS_TRUE="GObjects=149aa7ef0|149aa7ee0,GNames=149c009c0,GWorld=149b37d18,SparseDelegates=149aa7e90,GEngine=149da5810"

# UE 5.5 — Everspace 2, 2025-06-17 build (two manifests newer than ES2-0517). Every global moved
# ~-0x2000 versus that snapshot, so this is a real "does a pattern survive a game update?" test
# and not a trivially identical binary. It passes: both builds land on the SAME patterns with
# the SAME cost for all five targets.
GS_TRUE="GObjects=149aa5f60|149aa5f70,GNames=149bfe940,GWorld=149b35dd8,SparseDelegates=149aa5f10,GEngine=149da37b0"

# UE 5.5 — Meltopia (monolithic, PDB applied via MSDIA). NamePoolData from
# FNameDebugVisualizer::GetBlocks @0x141270620 = `lea rax,[0x149CA3C90]; ret`, minus 0x10.
GS_TRUE="GObjects=149d87420|149d87430,GNames=149ca3c80,GWorld=149f03d10,SparseDelegates=149a9a070,GEngine=149f002f8"

# UE 5.6 — Satisfactory, MODULAR. The name pool moved CoreUObject -> Core at 5.6.
# CrashReportClient.exe in the same project is a bonus MONOLITHIC 5.6 oracle (it links no Engine
# module, so it legitimately has no GWorld/GEngine).
GS_TRUE="-CoreUObject-:GObjects=1805a3620|1805a3630,-CoreUObject-:SparseDelegates=1805661a0,-Core-Win64:GNames=18082e8c0,-Engine-:GWorld=18216db68,-Engine-:GEngine=182170748,CrashReportClient:GObjects=141a9d4b0|141a9d4c0,CrashReportClient:GNames=1419c7e80,CrashReportClient:SparseDelegates=1419307f0"

# UE 5.7 — Solarpunk.
GS_TRUE="GObjects=1476ca920|1476ca910,GNames=1478e50c0,GWorld=1478cce58,SparseDelegates=1476ca4b0,GEngine=147a2fc20"

# Noise probes — pass NO GS_TRUE at all.
```

## Exported symbols (the O(1) path)

Modular builds export the globals, and `Genau::TrySymbolExport` enumerates every loaded module,
so these resolve via `GetProcAddress` before any scan runs. Verified with
`py tools/pe/pe_imports_exports.py exports <dll>`:

| Symbol | Exported by | Confirmed on |
|---|---|---|
| `?GUObjectArray@@3VFUObjectArray@@A` | CoreUObject | 4.26, 5.2, 5.6 |
| `?GWorld@@3VUWorldProxy@@A` | Engine | 4.26, 5.2, 5.6 |
| `?GEngine@@3PEAVUEngine@@EA` | Engine | 4.26, 5.2 |
| `?GMalloc@@3PEAVFMalloc@@EA` | Core | 4.26, 5.2 |

`NamePoolData` / `GNames` are **not** exported anywhere — which is why GNames has only
`SymbolCallFollow` entries (resolve `FName::ToString`, then scan its body for the pool
reference).
