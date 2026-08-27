# AOB code-block library — §4 and §6 BUILT and CI-gated; ONE decision still open

> **Status (2026-08-05).** The title said "NOT BUILT (decision pending)" while two of its
> proposals were already shipped *and enforced by CI*: the **§4 block library**
> (`tools/ghidra/blocks/blocks.json`, `.github/workflows/ci.yml`) and the **§6 n-gram
> specificity index** (same workflow). The genuinely open item is **build-order step 5** —
> whether to gate pattern authoring on the pre-filter (`docs/todo.md`). Do not read this as a
> flat SHIPPED either.

> **2026-07-29 late addendum — the density half is now SOLVED and measured; see §6.**
> §3 below says a block library "structurally cannot" answer *"how many spurious hits will this take
> on a 150 MB `.text`?"*. That is still true **of blocks**, but a second artifact answers it: a
> thresholded **byte n-gram frequency index**, which ships no code and needs no legal call. Built
> and validated 2026-07-29: **0 upper-bound violations on the 11 binaries it is built from** — but
> step 4 then measured it on the 58 it never saw and found **0.20% violations for `CLEAR`, with a
> real tail (932 hits against a bound of 15)**. So it is a proof on its own sources and a strong
> prior elsewhere; read §6's step-4 subsection before quoting the clean number. The two artifacts
> are complements, not alternatives — blocks cover *shape*, the index covers *density*.

**The idea (maintainer's).** Extract the `.text` regions the AOB patterns actually land on —
frequently-hit **hotspots**, per-game **occasional/unique** sites, and **decoy noise** — commit them
to the repo, and add a fast pre-test script. When a new game's AOBs miss, compare against these
blocks first instead of reaching for the sweep (which needs the corpus + Ghidra, and costs 4m38s
on the desktop / 12-15 min on the laptop — the "40-minute" figure this doc used to quote was never
measured; see GROUND-TRUTH.md).

**Why it matters more than it looks.** The sweep needs the Ghidra corpus root (`$GHIDRA_PROJS`,
182.3 GB as of 2026-08-01 — machine-specific, currently `D:\Tools\GHIDRA_Projs` on internal NVMe;
see [corpus-preservation.md](corpus-preservation.md), which also records why it must NOT live on
USB) and a
Ghidra install. **The maintainer's second machine has neither**, plus only 1–2 UE games installed —
and Auto Analyze is 3–4 hours per project there. So on that machine the sweep does not exist, and a
committed block library would be the *only* diagnostic available. That, not speed, is the case for
building it.

-----

## 1. The copyright question — I cannot clear this, and will not pretend to

Not a legal opinion. What can be said factually:

* **Anonymising the blocks does NOT change the copyright position.** Removing the game name removes
  *attribution*, not the copyright status of the excerpt. It is still worth doing for other reasons
  (it avoids implying endorsement, and avoids the "how to cheat at game X" framing), but it must not
  be mistaken for a fix. It arguably makes one thing *worse*: it removes the ability to honour a
  targeted takedown or to show the excerpt's scope in good faith.
* **Size is genuinely tiny.** A useful window is ~64–128 bytes around a match, out of a 100–200 MB
  image. That is about as de minimis as an excerpt gets, and the use is interoperability analysis.
  But de minimis is a *defence*, not a permission.
* **Most of what would be stored is not the game studio's expression at all** — it is MSVC's codegen
  from Epic's engine source (lazy-init thunks, TSet stride math, prologues). Whose work it is, is
  exactly the kind of question that needs a lawyer, not a heuristic.

**This does not have to be resolved, because of §2.**

-----

## 2. The decisive observation: the self-built oracles already carry the diagnostic value

The corpus splits into two provenance tiers, and they are not equally hard:

| tier | binaries | copyright position |
|---|---|---|
| **Self-built** | 4.15 Flying · 4.23.1 Flying (Shipping + DebugGame) · 4.27.2 Flying (Shipping + Development + DebugGame) · StackOBot 5.7.4 (Shipping + DebugGame) · 5.8.0 (Shipping + DebugGame) · 5.8.1 (Shipping + Development) · Titan 5.8.0 DebugGame | the maintainer's **own build output**, reproducible from installed engines |
| **Third-party** | Hogwarts, FF7R/Rebirth, Avowed, Palworld, Satisfactory, Everspace 1/2, DQ7R/DQXIS/DQ12, Octopath, TQ2, Meltopia, Solarpunk, DropIn, Breeders, Maelstrom, Elliot, Nekopara, Fantasynth, HeliumRain, FreudGate, LightMaze, Grimhook, Artisan, ManorLords | third-party shipped binaries |

**Every finding of the 2026-07-29 session came from the self-built tier**, without exception:

* `GOBJ_DI427_1` is config-gated, not a 4.27 trait — proved on 4.27.2 Flying × 3 configs.
* GNames unreachable on non-Shipping UE5 — proved on 4.23.1, 4.27.2, 5.7.4, 5.8.0, 5.8.1 + Titan.
* The root cause (first `lea` targets **rbx/r15**, patterns pin rax/r8/rdx/rsi/rbp) — read straight
  off self-built bytes.

The self-built tier spans **6 engine versions × up to 3 build configs**, which is a broader
version/config matrix than most of the third-party corpus. So the proposal can be built from the
easy tier alone and still answer the questions it exists to answer.

> **The table above is a snapshot and has since grown** — 4.10.4, stock 5.3 ×3, stock 5.4.4 ×3 and
> the 4.27.2 FirstPerson/3rdPerson sets all landed later the same day: **30 binaries across 8 engine
> versions**, 23 of them swept. Do not maintain the list here; the authoritative inventory is
> [reference-builds.md](reference-builds.md), regenerated by `tools/ghidra/inventory_builds.py`.
> The argument only gets stronger with size.

-----

## 3. What the library can and cannot answer

**CAN** — everything shape-related, which is what actually bit us:
* "Does my new pattern match a known decoy shape?"
* "What does the true site look like on engine X, config Y?" — the register-allocation question that
  caused BOTH failures found this session (`GOBJ_V1`'s hardcoded `rcx` on DropIn; GNames' rax/r8/rdx
  vs the rbx/r15 the 5.7+ non-Shipping codegen emits).
* "Is this new game's codegen shaped like anything we have seen?"

**CANNOT** — anything density-related:
* **"How many spurious hits will this take on a 150 MB `.text`?"** That is REPORT.md §6 (hits/MB) and
  it needs whole images. A block library structurally cannot produce it.
  → **but a companion artifact can, and now does — see §6.** The blocks stay shape-only; the n-gram
  index carries density. Read this bullet as "not from blocks", not as "impossible".
* "Which pattern would the runtime land on first?" — needs the full priority walk over a real image.
  **This one really is impossible offline** and stays the sweep's job.

⚠ **Therefore it is a TRIAGE tool, never an acceptance gate.** `Himmel.h` step 5 ("verify against the
corpus before trusting it") must keep meaning the sweep. The risk of building this is precisely that
it becomes a shortcut that lets an unmeasured pattern into the table — a pattern can pass every block
and still take 22,000 hits on a real game, which is exactly how `GWLD_V3` and `GNAM_V3` behave.

-----

## 4. Design — BUILT 2026-07-29 (step 5)

> **SHIPPED.** `tools/ghidra/extract_blocks.py` produced **340 blocks / 146 KB** from
> **22 self-built oracles**, and `tools/ghidra/blocktest.py` runs them in seconds with
> no Ghidra and no corpus. Wired into `.github/workflows/ci.yml` — **the first and only
> automated check on `Himmel.h`'s patterns.**
>
> Blocks record the window, its base VA, and the VA the match **resolves to**, so the
> test asserts the strong property. Negative controls confirm it bites: flipping one
> literal byte in `GOBJ_ES53_1` fails 64 blocks, and — the case it exists for —
> perturbing `GWLD_TQ_1`'s displacement `adj` by 8 still MATCHES but resolves wrong,
> failing 15 blocks. A "bytes still match" test would have passed that silently; it is
> the `GNAM_V7` out-of-bounds-resolve class.
>
> The self-built-only rule is enforced in code, not by convention: `extract_blocks.py`
> reads `corpus-manifest.json` and skips any row whose `source` is not `self-built`.
>
> One result worth noticing — `UE5.8.1-StackOBotDev`, `UE5.8-StackOBotDbgGame` and
> `UE5.8-TitanDbgGame` each yield **GNames/true: 1**, against 2 everywhere else. The
> blocks independently reproduce the non-Shipping GNames collapse.

The design as specified, for reference:

Store structured records, not a byte blob:

```
tools/ghidra/blocks/<target>/<id>.json
{
  "id": "GNAM-TRUE-5.8.1-DEV-01",
  "target": "GNames",
  "class": "true" | "decoy" | "hotspot",
  "engine": "5.8.1", "config": "Development",
  "provenance": "self-built",
  "site_role": "FNamePool lazy-init twin-LEA (initialized path uses rbx)",
  "bytes": "74 09 48 8d 1d .. .. .. .. eb 2f 48 8d 0d .. .. .. .. e8",
  "target_offset": 13,
  "expect": { "should_match": ["GNAM_ES53_1"], "must_not_match": ["GNAM_CT3","GNAM_CT4"] }
}
```

* **`bytes` present only for `provenance: self-built`.** Third-party sites get the same record with
  `bytes` omitted and a `sha256` of the window instead — enough to record that the shape exists and
  to let anyone who owns that game regenerate it locally, without redistributing anything.
* `tools/ghidra/blocktest.py` runs every `Himmel.h` pattern against every block and asserts the
  `expect` sets. Milliseconds, no Ghidra, no corpus — **runs in CI and on the bare second machine.**
* The repo currently has **no pattern regression test at all** between `extract_patterns.py`'s
  dead-constant check and the full sweep (corpus + Ghidra; 4m38s desktop / 12-15 min laptop). This
  would fill that gap, which is arguably a bigger
  win than the second-machine diagnostic.

Seed set (~40–60 blocks), all self-built, all from sites this session already characterised:
the GNames twin-LEA across 4.23 / 4.27 / 5.7.4 / 5.8 × Shipping-vs-non-Shipping; the GObjects
chunk-load register spread; the `check()`-fail `E8 … 90 CC` shape present/absent by config; and the
known decoys `GCoreObjectArrayForDebugVisualizers`, `GNameBlocksDebug`, and the pre-4.23
`GNAM_CT3`/`CT4`/`G42_1` convergence.

-----

## 5. Recommendation

**Worth building, in the self-built-only form, as a shape-regression test.** It is small (a few
hundred KB), needs no legal call, gives the second machine a real diagnostic, and closes a genuine
testing gap. Third-party blocks: metadata + hash only.

**Open for the maintainer, not for me:** whether to store third-party bytes at all. My input is only
that §2 means you do not have to, so the cheapest resolution is to not take the risk.

> **Partly resolved, 2026-07-29 — and the remaining part must not be glossed.**
>
> * **§6's index: no legal call needed.** It stores frequencies, not code. Settled.
> * **§4's blocks: the THIRD-PARTY question is gone; §1's question is NOT.** `extract_blocks.py`
>   enforces `source == self-built` in code, so no third-party game bytes ship — that was §2's
>   point and it holds. But the blocks still ship **10.4 KB of real compiled code** (340 × pattern-sized
>   windows → shrunk to **10.4 KB**, ≈0.011% of one binary), and by §1's own reasoning that is
>   *"MSVC's codegen from Epic's
>   engine source … whose work it is needs a lawyer, not a heuristic."* Building it yourself does
>   not make the bytes yours.
>
> **Context that reframes the scale, measured 2026-07-30.** `Himmel.h` has shipped **3,268 bytes of
> byte patterns extracted from real binaries** for years — and their source tags are overwhelmingly
> THIRD-PARTY shipped games (`DI427` DropIn, `ES2` Everspace 2, `SF`/`SAT*` Satisfactory, `SP57`
> Solarpunk, `TQ` Titan Quest 2, `RE`/`FF7R` FF7 Remake, `AV53` Avowed, `PAL51` Palworld, `MEL55`
> Meltopia, `OT` Octopath). The repo's core artifact already IS byte sequences from third-party
> binaries. The blocks are **self-built only, i.e. strictly better provenance than what already
> ships**, and after shrinking they are **3.3×** that existing footprint rather than 8.3×.
>
> ⚠ They are not identical in *character*, and that should not be glossed: `Himmel.h` patterns are
> wildcarded and discontinuous, mined as identifiers (strongly functional); blocks are verbatim
> contiguous windows (more excerpt-like). That difference is exactly why the windows were shrunk
> from a fixed 80 bytes to pattern-sized — 61% less code for an identical test (negative controls
> still fail 64 and 15 blocks respectively).
>
> So the position improved from "third-party studios' code, unknown quantity" to "Epic engine
> codegen, 10.4 KB, self-built, de minimis, interoperability purpose" — much more defensible, and
> still not *cleared*. An earlier draft of this box said "resolved by construction"; that
> overstated it. If the remaining exposure is ever unwanted, the blocks are regenerable from
> `extract_blocks.py` and could ship as hashes only, at the cost of the regression test.

> **Revised 2026-07-29 (late), after §6 was measured.** Build order should now be **§6 first, §4
> second**, and the reason is not preference but risk: the n-gram index needs **no legal call at all**
> (it ships no bytes, from anyone), it is validated end-to-end against all 151 patterns, and it
> answers the question that actually blocks pattern authoring — *"will this be noisy?"* — which §3
> correctly said blocks never could. The block library remains worth building for shape regression,
> but it is the half that still carries an unresolved (if small) legal question and has no
> validation behind it yet.
>
> They also share one hard limit, and it must not blur: **neither can say a pattern hits the RIGHT
> address.** Rule 5 keeps meaning the sweep.

-----

## 6. The density half — an n-gram index. MEASURED 2026-07-29, feasible

§3 said density needs whole images. It needs whole images **at build time**; it does not need them
at *query* time. That distinction is the whole design.

### The licensing question dissolves, rather than getting answered

§1 could not clear whether excerpts are redistributable, and §2's escape was "use self-built only".
This artifact does not need that escape, because **it never ships code at all** — only a table of
byte-sequence frequencies:

* it is **aggregate measurement**, not the excerpt;
* it **keeps only the COMMON sequences, discarding the rare ones** — the inverse of what
  reconstruction would need. What survives is generic MSVC codegen, which §1 already noted is the
  least expressive part.

⚠ **"Nothing can be reconstructed" was too strong, and measuring it proved so.** Over a 97 MB
source `.text`: **53.2% of 6-grams are absent** (those regions are gone outright), but **46.8%
survive**, and the longest run of *consecutive* surviving 6-grams is **681 → 686 contiguous bytes**
— and that region is real AVX code (`vmovd` / `vpextrd`), not padding. So k-mer coverage alone does
not rule assembly out.

What actually blocks it is narrower and worth stating precisely, because it is what the format
guarantees rather than what sounds good:

1. **The multiplicities Eulerian assembly needs are not shipped.** Counts are stored as **log2
   buckets** (2× granularity) of the **max across 12 different binaries**. A de Bruijn multigraph
   cannot be built from that — the edge counts are neither exact nor from one source.
2. **No positions, no ordering, no source attribution.** Nodes are 5-byte prefixes with heavy
   branching, so the candidate path count is astronomical with no signal to pick the true one.
3. **The majority is simply missing** — 53.2% — and what remains is by construction the *repeated*
   material, i.e. the least distinctive.

Honest summary: thresholding is a **strong barrier, not a proof of impossibility**. Do not lower
the threshold, and do not repeat the "nothing can be reconstructed" phrasing.

Build it from the self-built tier anyway (§2): it costs nothing and keeps provenance clean.

### How it works

Split a candidate into its literal runs (non-wildcard stretches), look up the **rarest** window, and
that frequency is a hard **upper bound** on the pattern's hit count — every occurrence of the whole
pattern must contain that window. Absent from the table = below threshold = still a sound bound.

### Validated — index from UE 5.4.4 Shipping (97.2 MB `.text`), scored against all 151 patterns

Compared to the hits `scan_patterns.java` **measured on the same binary**:

| index bound | max measured hits |
|---|---|
| **< 10** | **4** |
| 10–99 | 35 |
| 100–999 | 99 |
| **≥ 1000** | **852** |

**Upper-bound violations: 0 of 151.** Sound and monotone — a low bound *guarantees* a quiet pattern.
Deliberately loose (`GNAM_V2` bounds 148,426, measures 17) because wildcards constrain far more than
literal runs do; looseness is the safe direction, and the actionable verdicts are "bound < 10 → fine"
and "bound huge → prove it on real data".

⚠ **The first attempt scored the NOISIEST patterns as rare** — `GWLD_V3`, 852 measured hits, came
back "<8". Cause: it indexed only n=6 and treated *"no literal run reaches 6 bytes"* as rare, when
that is the signature of the most generic patterns there are. Fixed by indexing n=4/5/6 and scoring
at the largest n the longest run supports. **Never let "unscoreable" fall into the "rare" bucket** —
this is the same absence-of-evidence trap `replay_patterns.py`'s header warns about.

**Free corollary:** longest literal run **< 4 bytes → generic, no lookup needed.** Every such
pattern in the corpus is already in the noisy set, which independently reproduces the band table's
specificity-vs-priority rule.

### Size (per binary; key + 1-byte log bucket)

| threshold | n=4 | n=5 | n=6 | total |
|---|---|---|---|---|
| ≥ 8 | 4.3 MB | 6.7 MB | 8.1 MB | 19.1 MB |
| **≥ 16** | 2.3 MB | 3.3 MB | 3.7 MB | **9.3 MB** |
| ≥ 32 | 1.3 MB | 1.6 MB | 1.7 MB | 4.6 MB |

Eight versions × 9.3 MB is too much to commit. **Ship a UNION index taking the max count per
n-gram** — still a sound bound for "any UE version", and far smaller than 8 tables since the engine
code is largely shared. Expect ~2–3× a single binary. Raising the threshold only loosens the bound,
never breaks it, so tune it to the size budget.

### Still cannot do the thing that matters most

**It cannot say whether a pattern hits the RIGHT address.** `GNAM_XX_1` scores a respectable bound of
57 and is `DECOY-ONLY` on that binary. So §3's warning stands unchanged and applies here too: this is
a **pre-filter, not an acceptance gate**. `Himmel.h` rule 5 keeps meaning the sweep. What it buys is
that the 200 GB corpus only ever sees candidates that are already plausible.

### ANSWERED 2026-07-29 (step 4) — and the answer reshapes the contract

Two questions, and the one that mattered was not the one this section originally asked.

**Q1 — does a ONE-VERSION index generalise across versions? YES.** A 4.27-only index judged against
the 5.4 binary it had never seen: **0 violations in 113 patterns.** Two single-version indexes
(4.27 vs 5.4) disagree on the CLEAR/UNPROVEN *label* for 9 of 158 rows (6%), but neither is ever
unsound. Stock engine code is similar enough across versions that version coverage is not the risk.

**Q2 — does the shipped 12-binary union hold on binaries it never saw? NO, not as a proof.**

| group | pairs | violations | rate |
|---|---|---|---|
| the 11 source binaries | 1,017 | **0** | 0.00% |
| 58 binaries never indexed | 7,345 | **27** | 0.37% |
| …of those, `CLEAR`-verdict | 3,055 | **6** | **0.20%** — median 0, 99th pct 10, **MAX 932** |

**`CLEAR` is a proof on the index's own sources and a strong prior everywhere else.** The tail is
real: `GNAM_UD2` bounds at ≤15 and takes **932** hits on FF7 Remake; `GOBJ_AV2` bounds at ≤15 and
takes 510 on Avowed.

**The cause is code coverage, not version.** The index is built from *content-free stock templates*,
so it has seen UE engine code and essentially **no game code**, while a shipped title adds 100+ MB
of studio code on top. The worst offenders are exactly the most non-standard binaries in the corpus
(FF7R, Avowed). Since §1/§2 mean third-party code can never be indexed, **this limit is STRUCTURAL
and permanent — not a threshold to tune, not a bug to fix.**

**What that changes:** nothing about the build order, and everything about the wording. The tool
must never say "certified"; it says *quiet in stock engine code*, with the measured unseen-binary
rate attached. Both `aob_specificity.py`'s header and its `CLEAR` verdict text now carry these
numbers, so a user on another machine reads the caveat without finding this file.

### Build order

1. `tools/pe/build_ngram_index.py` — `.text` → thresholded union index over the self-built inventory
   in [reference-builds.md](reference-builds.md). Runs on the corpus machine, offline.
2. Commit the index (one artifact, regenerated rarely).
3. `tools/pe/aob_specificity.py` — AOB string in; bound, limiting window, and the run-<4 verdict out.
   **Stdlib only, no corpus, no Ghidra — so it CAN run on the bare second machine and in CI.**
   ✅ **Both, since 2026-08 — this line used to say the opposite and was stale.** `tools/check_all.py`
   runs `aob_specificity --check`, and CI compares `tools/pe/aob-specificity-baseline.tsv` byte-wise,
   so a pattern whose specificity moves fails the build. Step 5 below is still the part not built.
4. Answer the generalisation question and record the divergence here.
5. Gate authoring on it: a candidate clears the pre-filter before it earns a sweep.

Note 1–3 are independent of the §4 block work and can land first; §4's `blocktest.py` and this share
the same motivation (a regression test that is not the 200 GB corpus) and the same home.

-----

## 7. MEASURED 2026-08-01 — what `CLEAR` actually means, and why merging corpora is dominated

Two questions were put to the index a year's worth of assumptions later: *should the threshold be
lowered to make it more accurate?* and *should non-UE binaries be folded in as extra coverage?*
Both were answered by instrumenting the reader rather than by reasoning about it, and both answers
are different from the obvious one.

### 7.1 `CLEAR` means "never seen", not "rare" — so a higher CLEAR count is a WORSE index

`aob_specificity.py`'s `lookup()` returns `threshold - 1` (= 15) on a binary-search miss, and the
smallest bucket any table stores decodes to 16. **A present key therefore can never fall under the
floor.** Measured across `patterns.tsv`:

```
CLEAR verdicts whose limiting window is ABSENT from the table : 47
CLEAR verdicts whose limiting window is PRESENT (real bucket) :  0
```

So `CLEAR` is exactly the statement *"this window occurs fewer than `threshold` times in the code we
indexed"*, and the 3.57 M stored records contribute to **no** CLEAR — they exist only to deny it.
The reductio: AOBMaker's **x86** index, which contains zero x64 code, certifies **109** of our x64
patterns `CLEAR`. Ignorance and quietness are indistinguishable to this metric, and the count moves
the wrong way as coverage improves. Never report the CLEAR count as a quality figure.

### 7.2 Threshold 16 → 8: a real improvement, with a real price

Lowering it moves the floor 15 → 7, so every surviving `CLEAR` is a stronger claim. Measured:

| | T = 16 | T = 8 |
|---|---|---|
| worst TRUE hit count under a `CLEAR` | 13 | **1** |
| known out-of-sample violators | 2 (`GNAM_UD2` 932 on FF7R, `GOBJ_AV2` 510 on Avowed) | **1** — `GNAM_UD2`'s window has union count 13, so at T=8 it becomes a present key, loses `CLEAR`, and the 932-hit error disappears |
| committed size (gz) | 10.3 MB | ~22.5 MB |
| patterns demoted from `CLEAR` | — | 8 (genuinely quiet ones, correctly reclassified as unmeasured) |

This corrects an earlier claim that lowering the threshold "would not touch the violations at all".
It touches one of the two.

### 7.3 Merging corpora is strictly dominated by keeping them separate

`merge_max` (`build_ngram_index.py`) is a per-key **MAX**, so adding sources is monotone
non-decreasing on the bound — folding AOBMaker's 38-binary non-UE index into ours made **39 bounds
looser and 0 tighter**. That is correct behaviour, not degradation: it is the tool acquiring
evidence (§7.1).

But `score()` **minimises over windows**, and that settles the design:

```
merged index          =  min_w  max(A, B)
two indexes, take max =  max( min_w A , min_w B )
                         min-max  >=  max-min
```

Measured over 114 scoreable patterns: merged was **tighter in 0 cases and looser in 4**
(`GENG_DI427_1` 64 vs 32, `GOBJ_G42_1` 128 vs 64, `GWLD_SF_2` 262144 vs 131072, `GWLD_ES53_2` 65536
vs 32768), with identical CLEAR counts. **Keep indexes separate and query each; take the larger
score.** Never looser than merging, and it preserves the ability to ask the UE-only question —
which merging destroys irreversibly.

The "mixed corpus is hard to reconstruct" argument is not what protects the artifact, and should not
be relied on. The structural barrier is stronger and simpler: because the stored count is a **MAX
across sources, not a sum**, the multiset is not the n-gram spectrum of *any* byte stream. There is
no assembly target to recover. Thresholding is a second, tunable barrier layered on top of that one.

Practical note: `build_ngram_index.py` cannot build a mixed corpus as written — `pick_sources()`
hardcodes a single root, Shipping-only, `\Engine\`-excluded, ≥5 MB, with no `--roots`. Adding one
would be the prerequisite, and §7.3 says not to bother.
