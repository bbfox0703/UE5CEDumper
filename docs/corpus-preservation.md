# AOB corpus preservation — what to keep, what to reinstall, what to drop

> **Read this before deleting anything under `$GHIDRA_PROJS` (the Ghidra corpus root),
> `D:\UE_Analyze_data\Game archive`, `D:\UE_Analyze_data\Game Binary backup`, or before
> uninstalling a corpus Steam title.**
>
> Companion to [tools/ghidra/GROUND-TRUTH.md](../tools/ghidra/GROUND-TRUTH.md) (which patterns
> are proven by which binary) and [tools/ghidra/sweep.sh](../tools/ghidra/sweep.sh) (the corpus
> list itself, and the source of truth for it).

> ### ⚠ 2026-10-01 — the Ghidra projects were dropped, except five (the maintainer's decision)
>
> **58 of the 63 `.rep` (146 GB) left the corpus root.** Five stay (19 GB): the self-built UE 5.8
> projects `StackOBot_Shipping_UE58`, `StackOBot_DebugGame_UE58`, `StackOBot_Shipping_UE581`,
> `StackOBot_Development_UE581` and `Titan_DebugGame_UE58`. Their binaries are no longer on disk
> (`py tools/ghidra/corpus_relocate.py`: 52/57 rows recoverable, these five `EXE-MISSING`), so
> each `.rep` is the only copy of that row on this machine.
>
> **The removed projects are not lost.** Before the cleanup the maintainer shrank the project
> files and copied all 63 projects to the NAS, mapped as drive `Y:` — `Y:\GHIDRA_Projs`
> (63 `.rep` + 63 `.gpr`, listed 2026-10-01). This machine no longer holds the 58; that is all.
> ⚠ If `Y:` does not exist, or `GHIDRA_Projs` is not under it, the NAS is NOT MOUNTED. That is
> not a missing backup: mount it and look again before concluding anything.
>
> **Why.** Nothing routine reads a `.rep` any more: the regression sweep is `pe_sweep.py` over the
> binaries (§0a), and the n-gram index has always been built from the binaries. The one use left
> was reading code to author a NEW pattern, and every pattern in use is already recorded in
> `Himmel.h`. The next new pattern is expected for a new engine generation; the plan then is to
> package a DumperTest for that engine and compare it against `Himmel.h`, not to analyse a
> shipped game.
>
> **What follows from it.**
> * `sweep.sh`, `preflight.py`, `run_headless_export.py` and `run_all_aob_export.py` open the
>   `.rep` and will report 52 rows' projects missing. That is the expected state, not damage.
> * To read one game's code again: re-import it from the archived binary (§0b shows it works);
>   budget hours of Auto Analyze for that one project.
> * `D:\UE_Analyze_data` is now the whole corpus for 52 rows. The rules below about its two
>   archive roots and about uninstalling a corpus Steam title matter MORE, not less.
> * Everything below that calls a `.rep` the artifact of record, the drop order and the
>   never-drop set describe the corpus before this date. Read them as history.

> ### ⚠ THE PATHS IN THIS DOCUMENT ARE ONE MACHINE'S — not a property of this repo
>
> The corpus root does **not** follow a clone. Every tool resolves it from the `GHIDRA_PROJS`
> environment variable — [`sweep.sh:19`](../tools/ghidra/sweep.sh), [`preflight.py:797`](../tools/ghidra/preflight.py),
> [`build_corpus_manifest.py:423`](../tools/ghidra/build_corpus_manifest.py),
> [`run_headless_export.py`](../tools/ghidra/run_headless_export.py),
> [`run_all_aob_export.py`](../tools/ghidra/run_all_aob_export.py) — falling back to
> `D:\Tools\GHIDRA_Projs` only because that is where the machine which wrote these lines kept it.
>
> **Current location:** `D:\Tools\GHIDRA_Projs` (internal NVMe) — 63 `.rep`, 182.3 GB, verified
> 2026-08-01 (all 57 `sweep.sh`-referenced projects present).
>
> Set `GHIDRA_PROJS` before running anything here, and read every literal `D:\Tools\GHIDRA_Projs`
> below as "wherever yours is". A path written on a different machine is the single most common
> reason a documented command fails for the next person — run `py tools/ghidra/preflight.py`
> first, which reports what it actually found rather than what this file assumes.

> ### 🔥 DO NOT HOST THE CORPUS ON REMOVABLE / USB MEDIA — measured 2026-08-01
>
> The corpus lived on an external USB SSD (Plextor EX1) for a while. A sweep run at
> `SWEEP_JOBS=16 SWEEP_XMX=2G` knocked the drive **off the bus mid-write**. RAM was never the
> constraint — 16 × 2 GB = 32 GB on a 61.6 GB machine. The storage TRANSPORT was.
>
> ```
> disk  / Event 51  ×12+   paging-operation error on \Device\Harddisk2\DR2
> Ntfs  / Event 140        cannot flush the transaction log; "corruption may have occurred, VolumeId: E:"
> Ntfs  / Event 50         delayed-write FAILED on \$Mft — "data has been lost"
> Ntfs  / Event 50         delayed-write FAILED on GHIDRA_Projs\<proj>\idata\00\~00000000.db\tmp…
> volume re-enumerated     HarddiskVolume9 → HarddiskVolume12  (surprise removal + re-attach)
> ```
>
> **Lost `$Mft` writes with a Ghidra project mid-write** is precisely the shape that silently
> corrupts a `.rep`. The Ghidra processes then wedged in unkillable kernel I/O waits —
> `Stop-Process` reported "no such process" while WMI still listed them, and enumerating them
> hung. Only a reboot cleared it.
>
> This corpus survived (verified above). It did not have to. Three rules follow:
>
> 1. **Internal storage only.** §0 says a `.rep` is the artifact of record; putting the artifact of
>    record behind a connector that can drop under load is not a trade-off, it is a bug.
> 2. **If it must be on USB, cap `SWEEP_JOBS` at 2–3.** Not for speed — the full sweep is
>    4m38s on the desktop / 12-15 min on the laptop (`GROUND-TRUTH.md` carries both, with the machines
>    — a sweep time without its machine is not a measurement). Parallelism beyond a handful buys
>    ~12% and risks the corpus.
> 3. **After ANY surprise disconnect, verify before trusting.** "The volume reports Healthy" is a
>    statement about the *re-mounted* volume, not about the data. Run `preflight.py`, and check
>    that every `sweep.sh` project still has a `.rep` on disk.

> ### ✅ `D:\UE_Analyze_data` IS SELF-SUFFICIENT — measured 2026-08-01
>
> ```
> py tools/ghidra/corpus_relocate.py            # 57/57 rows -> exit 0
> ```
>
> `corpus_relocate.py` answers "is this folder set enough to rebuild the whole corpus?" by HASH,
> not by path — because `binary_last_seen` records where a binary WAS at import time, and 21 of 57
> of those paths had rotted (`D:\tmp\...`, `X:\...`, moved Steam libraries).
> `build_corpus_manifest.py` cannot fix that: it re-derives the same recorded path, so it can only
> re-confirm "gone", and `preflight.py` then calls those rows BLOCKING while the file sits on disk
> under another name.
>
> | | |
> |---|---|
> | 39 rows | exe hash-matched **and** a PDB paired by CodeView GUID+Age |
> | 18 rows | no PDB needed |
> | **0 rows** | missing |
>
> **59.4 GB of source carries what 181.2 GB of `.rep` was holding** — and it is the
> regenerable-*from* form, not the derived one. Two traps it refuses to fall into: a matching
> FILENAME is not a match (and a SHA-256-only check under-reports, because `binary_sha256` is
> nulled on drifted rows while `binary_md5` never is — measured, it silently lost 4 rows), and a
> PDB beside the exe is not its PDB.

> ### 💾 Compression: LZX on the ARCHIVE, never on the `.rep`
>
> Measured 2026-08-01 with `compact /c /exe:lzx` on copies:
>
> | sample | ratio |
> |---|---|
> | `ES2-Win64-Shipping.pdb` (1801 MB) | **4.0 : 1** |
> | `Titan-Win64-DebugGame.exe` | 2.9 : 1 |
> | `Elliot-Win64-Shipping.exe` (Shipping) | 1.7 : 1 |
> | `.pak` | 1.7 : 1 |
>
> `.pdb` is 34.73 GB of the 59.4 GB *and* compresses best, so projected: **59.4 -> ~21 GB**, or
> **~16 GB** after deleting the `.ucas` / `.pak` (9.2 GB the corpus never reads).
>
> **Use `/exe:lzx`, NOT the Explorer checkbox** — that is legacy `compact /c` (LZNT1, 64 KB blocks):
> far worse ratio and it fragments badly. LZX is built for write-once/read-rarely, which is exactly
> this archive; compression is slow one-time, decompression is fast.
>
> ⚠ **Do NOT compress `$GHIDRA_PROJS`.** The archive is COLD (read at import only); the `.rep` is
> the sweep's HOT path, read in full on every run. Compressing it adds CPU to the slowest operation
> on the machine. The rule is read-frequency, not file type.
>
> ⚠ An LZX file that gets MODIFIED is decompressed and stays that way — re-run `compact` after
> adding anything to the archive.

Every number in this document is **measured**, not estimated, and carries the date it was
measured. Free space and archive sizes move; re-measure with `preflight.py --sizes` before acting.

-----

## 0. The one fact that reorders everything

`sweep.sh` runs

```
analyzeHeadless <projdir> <proj> -process <glob> -noanalysis -readOnly -postScript scan_patterns.java
```

(`sweep.sh:237-240`). **It never opens the original game binary.** Ghidra applied the PDB at
import time and the symbols live inside the `.rep`.

Consequences, and they were the spine of this whole document:

* A `.rep` is the **artifact of record**. Losing one loses the capability.
* The game install / archived binary is only needed to **RE-IMPORT** — i.e. to rebuild a `.rep`
  you deleted, or to re-analyse after a pattern change that needs fresh decompilation.
* Therefore "is the game still installed?" is a **recovery** question, never a **can I sweep
  today** question. `preflight.py` reports the two at different severities on purpose.

Verified empirically: in the last sweep, `UE4.18-FF7R` and `UE4.27-DropIn` both produced complete
scan TSVs while their binaries were off-disk.

### 0a. …and since 2026-08-01 there is a second sweep with the OPPOSITE dependency

`py tools/ghidra/pe_sweep.py` replays the same signature database from the **game binaries**, and
opens no project at all. It is not an approximation of the above — `compare_sweeps.py` grades it at
**210/210 files byte-identical**, and `check_pe_memory.py` shows **70/70** programs' image base,
complete block map and per-block **MD5** reconstructed from the PE. See
[dev-log](dev-log.md) (build 2545).

So the two questions have swapped tools, and both are now answerable:

| question | tool | needs |
|---|---|---|
| can I re-run the regression sweep today? | `pe_sweep.py` | the **binaries** (`D:\UE_Analyze_data`) |
| can I author a NEW AOB today? | Ghidra | the **`.rep`** (decompiler, xrefs, symbols) |

**This does NOT license deleting a `.rep`.** What changed is that the `.rep` is no longer the
artifact of record *for the sweep*; it is still the artifact of record for **authoring**, and a
re-import has still never been demonstrated end to end. The rule in
[todo.md](todo.md) stands: demonstrate one reconstruction before deleting anything.

One practical consequence worth banking now: `ES2-0517` re-runs a **language-version upgrade on
every open** (`-readOnly` discards it), and that upgrade — not the scan — dominates its runtime. It
was the last row to finish in all three Ghidra runs on 2026-08-01, by a wide margin. If a future
run looks hung on that project, it is probably not hung. §0c explains why it is the only one.

-----

### 0b. A `.rep` CAN be rebuilt — demonstrated 2026-08-01

The standing rule was "no `.rep` may be deleted until a re-import is demonstrated end to end".
`tools/ghidra/reimport_verify.py` does it and grades the result on three things that fail
independently: Ghidra's recorded executable MD5/SHA256, a **SHA-256 over every `(address, name,
type, scope, source)` in the symbol table**, the block map with a per-block MD5, and byte-identical
sweep output. Matching symbol *counts* would pass a rebuild that put the right number of symbols at
the wrong addresses — which is exactly what a same-named-but-different PDB produces.

| rebuild | original | result | wall |
|---|---|---|---|
| `UE4.10-Game`, `-noanalysis` | raw import, no PDB | **REBUILT-IDENTICAL** | 95 s |
| `AudioMixerCore` (0.1 MB), `--analyze` | PDB + disassembled | **REBUILT-EQUIVALENT** | 98 s |
| `FactoryGame-CoreUObject` (4.2 MB, 684,805 instructions), `--analyze` | PDB + disassembled | **REBUILT-IDENTICAL** | 422 s |

"Identical" is literal: same executable hashes, same symbol digest, same function / instruction /
defined-data / datatype counts, same `PDB Loaded` and `RTTI Found`, same block map, same scan and
consensus files.

**Breadth: 17 rows / 22 programs re-imported**, chosen to cover the axes (raw vs analysed original,
PDB vs none, monolithic vs modular, well-behaved vs packed, store-supplied vs self-built):

| verdict | rows | meaning |
|---|---|---|
| `REBUILT-IDENTICAL` | 3 | the raw, no-PDB originals — nothing left over |
| `REBUILT-MODULO-ANALYSIS` | 13 | binary, memory image and sweep answers reproduce; the original also carried disassembly this run did not regenerate |
| `MISMATCH` | 1 | `ES2-0517`, and correctly so — see §0c |

**Across all of it: 23 block maps and 69 sweep files compared, ZERO differences.** Every row's
memory image and every scan/consensus answer is reproducible from the archived binary, including
the packed DQ7R and Avowed builds and the four-DLL modular Satisfactory rows. 34 Ghidra-synthesized
`tdb` blocks were accepted by the computed reachability rule (non-executable, outside any RIP
displacement), each one listed.

### The controls — because a test that has never failed is not evidence

Two negative controls, and one of them bit:

* **A same-named binary from a DIFFERENT build must be rejected.** Pointing the ES2 row at its
  sibling build is refused on the executable hash, the symbol digest, the **PDB GUID** and the
  block map. Getting that control to run at all exposed a real blocker: `analyzeHeadless.bat` is a
  cmd batch file whose parser breaks on `(` / `)` **even inside quotes**, and both ES2 binaries sit
  under `…\ES2\5.5 (735055807809773736)\`. The parentheses are NEW — `meta.Executable Location`
  shows both projects were imported from a Steam path without them — so **the archive's own folder
  naming had made those two rows unrebuildable**, and 8.3 short names are disabled on that volume.
  `reimport_verify.py` now hardlinks the exe *and its PDB* into a clean directory.
* **A truncated baseline must fail closed.** It does. But the fail-open this was meant to fix
  **did not exist** — tested against the pre-guard code, an empty baseline already failed on the
  input and symbol legs. The guard is kept for the error message, not for correctness.

**Ghidra's analysis is deterministic here, and that was measured rather than assumed.** Two
independent `--analyze` rebuilds of the same input were **field-for-field identical (0 differing
fields)**. That is what makes the one anomaly interpretable: `AudioMixerCore`'s rebuild differed
from its original by **+1 data type and +7 in `# of Symbols`** while the symbol *digest* and every
address-bearing count matched. Since rebuilds agree with each other, that delta is history the
ORIGINAL carries, not rebuild noise — and it did not recur on the 40x larger CoreUObject, so it is
specific to that project rather than a property of the method.

### 0c. What the corpus actually contains, measured

`dump_identity.java` over all 57 rows (74 programs). The discriminator is **instructions, not
functions or `.rep` size**: applying a PDB creates functions and defined data without disassembling
anything, so `UE4.10-GameDev` shows 195,451 functions, 840,853 defined data and `Analyzed=false`
with **zero instructions**.

| PDB loaded | disassembled | programs | rebuild cost |
|---|---|---|---|
| yes | yes | **42** | import + PDB + analysis (minutes) |
| no | yes | 18 | import + analysis |
| no | no | 13 | import only (~1 min); includes the 4 broken stubs |

The raw imports' symbols are **not PDB symbols** — `UE4.10-Game` has 184,438 `DEFAULT` (auto-named,
from the `.pdata` function table) plus 1,233 `IMPORTED` (the PE export/import table). GROUND-TRUTH's
note that `-noanalysis` skips PDB application is correct. A PDB-loaded project looks different:
`UE4.20-Everspace` carries 445,962 `IMPORTED` symbols.

**`ES2-0517` is the only project in the corpus created by Ghidra 11.3.2**; the other 72 programs
are 12.1.2. That is the cause of its per-open language upgrade, and it is the one project whose
original toolchain a rebuild on the installed Ghidra cannot reproduce.

**Do NOT treat that as something to preserve.** For about an hour this document advised "keep that
`.rep`, or keep an 11.3.2 install", because `reimport_verify.py` graded the version difference as a
`MISMATCH`. That is backwards and it does not scale: on Ghidra 12.2 or 13.0, *every* project would
suddenly read as "irreproducible" against a pinned original, and the corpus would be chasing a
version it can never get back.

The strategy is the opposite one:

> **Always rebuild on the installed Ghidra.** If a new release breaks something, stay on the
> working version until it is fixed — do not pin an artifact's birth version as a requirement.

`meta.Created With Ghidra Version` is therefore reported as an informational note and never as a
failure, and `ES2-0517` grades `REBUILT-MODULO-ANALYSIS` like every other analysed row (block map
and all three sweep files match). **17 of 17 rows verified, zero mismatches.**

### 0d. UPGRADE the project, do not re-import it — done 2026-08-01, measured

Re-importing from the archive would have cost a **full re-analysis** (hours for a 169 MB binary
with 28.6 M instructions). The language-version upgrade is a **migration of the existing database**,
not a re-analysis: it keeps every instruction, function and PDB symbol. So the fix is to let the
upgrade PERSIST — run the project once **without `-readOnly`**, which `GROUND-TRUTH.md` has
prescribed all along and which had never been done:

```sh
analyzeHeadless "$GHIDRA_PROJS" ES2-0517 -process -noanalysis     # NOTE: no -readOnly
```

| | |
|---|---|
| one-time cost | **12 m 43 s** |
| a scan BEFORE the upgrade | **>10 min — did not finish**; the whole window went into the upgrade, which `-readOnly` then discarded |
| the same scan AFTER | **30 s**, zero `Updating language version` lines |
| `.rep` size | 12 GB → 12 GB (unchanged) |

It pays for itself on the **second** run, and only ONE project in the corpus needed it — the log
line `Updating language version` appears for `ES2-0517` and for nothing else across every run.

**It is behaviour-preserving, and that was checked rather than assumed.** Against the pre-upgrade
baselines: `scan_*.txt`, `scan_*.tsv`, `consensus_*.txt` and `blocks_*.tsv` all **byte-identical**,
symbol digest identical (5,298,149 symbols), 507,555 functions and 28,635,821 instructions
unchanged, `PDB Loaded=true`. `meta.Created With Ghidra Version` stays `11.3.2` — that field records
what CREATED the program and a migration does not rewrite it, which is fine because per §0c it is
not a property worth preserving.

⚠ **Killing a `-readOnly` run mid-upgrade leaves debris.** Taking a 12 GB safety copy first was
right, but then timing a "before" run against that copy — and killing it at a 10-minute timeout —
left **~6 GB of orphaned transient DB files** inside the backup's `idata` plus a stale
`ES2-0517.lock` / `.lock~`. That is §0's "`-readOnly` does not mean no writes" happening in
miniature. Delete the stale locks after confirming no `java.exe` holds the project.

-----

## 1. The manifest — `tools/ghidra/corpus-manifest.json`

Schema `ue5cedumper.corpus-manifest/2`. Regenerate with:

```
py tools/ghidra/build_corpus_manifest.py            # writes .json and .tsv
py tools/ghidra/build_corpus_manifest.py --no-hash  # skip the ~15 GB of hashing
```

Nothing in it is typed by hand. Five measured sources, in order of authority:

| # | Source | Gives |
|---|--------|-------|
| 1 | `tools/ghidra/sweep.sh` `ROWS=()` | TAG, project, glob, GS_TRUE → the tag list and `ROLE` |
| 2 | `<proj>.rep/idata/**/*.gbf` | program names, Ghidra's `Executable Location` + `Executable MD5` — **read straight off disk, no Ghidra, no project lock, ~3 s for all 43 projects** |
| 3 | the recorded path | does the binary still exist, is a `.pdb` beside it, what does it hash to today |
| 4 | `libraryfolders.vdf` + `appmanifest_*.acf` | appid / installdir / SizeOnDisk / buildid for what is installed **now** |
| 5 | `appcache/appinfo.vdf` (entry framing) | appid for titles that are **not** installed |

Anything none of the five can answer is `null` = UNKNOWN. **A guessed app id is worse than a gap** —
it sends you to install the wrong game.

### Grain and counts

`38 tags / 51 selected programs / 55 TSV rows`, all three asserted by the drift check.

* 38 = `ROWS=()` entries in `sweep.sh`. **Not 51.**
* 55 = one row per (tag, Ghidra program); modular projects hold several programs.
* 51 = rows the `-process` glob actually selects (the 4 unselected ones are the
  wrong-version programs inside the mis-imported `Satisfactory_UE521` project — the glob there is
  load-bearing **correctness**, not scoping convenience).

`corpus-manifest.tsv` is the same data at program grain, for diffing.

### Fields that exist because they were needed

* **`binary_md5`** — Ghidra's recorded import MD5. Unlike `steam_buildid` / `binary_size_bytes` /
  `binary_sha256` (which the generator deliberately nulls on a drifted row, so it never asserts
  the replacement build is the corpus build) this is **never nulled**: it describes the corpus
  build, not today's file. It is the only identity a GONE or DRIFTED row still has, and it is what
  lets `preflight --verify-hash` say *"this file is NOT the corpus build"* instead of shrugging.
* **`duplicate_copies`** — other paths whose bytes are byte-identical, MD5-confirmed. Searched only
  for rows no store can re-serve (self-built / archive). Same-name **same-size is not proof**:
  measured, a repackage of UE423_Flying emits a fresh PE at identical size
  (`43f6b130…` vs the imported `f61ec1be…`).
* **`pdb_relpath` / `pdb_size_bytes`** — the PDB checklist has to be machine-readable, or it drifts.

### Drift check

`preflight.py` parses `sweep.sh` itself and compares three ways: sweep-only tags, manifest-only
tags, project-name mismatches. **Any drift → exit 3.** Verified in both directions (adding a tag
and renaming one). A changed `GLOB` or `GS_TRUE` is *not* yet compared — see Open questions.

-----

## 2. Preflight — `tools/ghidra/preflight.py`

Pure-stdlib Python, no third-party deps, **never invokes Ghidra** (project state comes from
`.rep/idata/**/*.prp` XML on disk, so it takes no project lock and honours the read-only rule by
construction).

```
py tools/ghidra/preflight.py                  # the normal pre-sweep check, seconds
py tools/ghidra/preflight.py --sizes          # + per-project .rep sizes (for triage)
py tools/ghidra/preflight.py --verify-hash    # + MD5 every located binary (minutes, reads ~15 GB)
py tools/ghidra/preflight.py UE5.7            # filter to matching tags
py tools/ghidra/preflight.py --json           # machine-readable
```

**Two independent notions of "present", because the actions differ:**

| Level | Condition | Severity |
|-------|-----------|----------|
| A — Ghidra project | missing / locked / no `.gpr` / empty / glob matches nothing | **the sweep FAILS.** Gates (exit 2). |
| B — binary / PDB | not installed / wrong build / no symbols | sweep is fine; **re-import** is impossible. Advisory; gates only under `--strict` (exit 4). |

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | GO |
| 1 | tool error (**including argparse usage errors**, remapped from 2 so a typo can never read as "corpus incomplete") |
| 2 | NO-GO — a blocking Ghidra project problem (`--allow-partial` downgrades) |
| 3 | DRIFT — manifest vs `sweep.sh` disagree, either direction (`--allow-drift` downgrades) |
| 4 | GAPS — only under `--strict` |

Highest precedence wins; **all sections always print**, so a gate never hides information.

### What it cannot determine, and why

* **Whether a re-download reproduces the corpus build** without `--verify-hash`. Steam serves only
  the current build; only the MD5 comparison answers it.
* **`needs_pdb` for a title that is not installed.** It is derived by `stat()`-ing beside a located
  binary, so an uninstalled row reports `UNKNOWN_UNCHECKABLE` — *unmeasured, not "no"*.
* **PDB coverage of a modular project.** `pdb_relpath` tracks the anchor program only; the other
  8 programs of `UE5.6-Satisfactory` are not accounted for.
* **Ghidra database health.** It reads project *metadata*, never opens a program database. A
  silently corrupted `.rep` (the realistic failure mode when moving 120 GB onto a spinning HDD)
  looks perfectly healthy here. Only an actual sweep would notice.
* **App ids it was never given.** Where the manifest has `null` it prints
  *"app id UNKNOWN in manifest — cannot name what to install"* rather than guessing.

### Section [2] is informational, not a defect

`Maelstrom` and `Satisfactory_v1.2.3.1` contain `<name>.0` programs. **Measured**: these are
MS-DOS/MZ-loader imports that map only the 1104-byte DOS stub (`scanning … CODE_0 size=1104` at
image base `0000:0000`) and score `hits=0` on all 151 patterns. `aggregate_sweep.py` already
excludes them (`exec_mb <= 0` → "broken import"), which is why `REPORT.md` reads
**"programs scanned: 51"** and not 55. They do **not** double-score truth and do **not** inflate
any statistic. Deleting them in Ghidra is optional disk hygiene (~0.2 GB).

They do expose a **real bug**: `scan_patterns.java:206` builds the output filename from
`currentProgram.getImageBase()` and `sanitize()` does not strip `:`. On NTFS `…@0000:0000.txt`
truncates at the colon, so the content lands in **alternate data streams** of a 0-byte file:

```
Get-Item 'out/sweep/scan_UE4.27-Maelstrom__MaelstromV2-Win64-Shipping.exe@0000' -Stream *
    :$DATA 0   0000.tsv 16769   0000.txt 14167
```

Harmless for a stub; **silent data loss** for any real program Ghidra loads with a segmented
address space.

-----

## 3. PDB checklist — which titles must be installed to keep symbols

Measured 2026-07-29. **21 sweep rows resolve to a PDB, but only 20 distinct PDB files exist**
(`UE5.5-Everspace2` and `UE5.5-Everspace2b` point at the same path — see §5). Total 8.11 GB.

### 3a. Steam-resident — FRAGILE, a game patch overwrites these in place

10 distinct files, 6.25 GB. **There is no way to get an old PDB back.**

| Tag | Title (Steam) | App id | PDB MB |
|-----|---------------|--------|--------|
| UE5.5-Everspace2b | EVERSPACE™ 2 | 1128920 | 1801.0 |
| UE5.7-Solarpunk | Solarpunk | 1805110 | 1553.2 |
| UE4.27-Maelstrom | Maelstrom | 764050 | 954.2 |
| UE4.27-Breeders | Breeders of the Nephelym | 1161770 | 762.6 |
| UE4.20-Everspace | EVERSPACE | 396750 | 382.9 |
| UE5.5-Meltopia | Meltopia | 3601800 | 347.4 |
| UE4.27-DropIn | Drop In - VR F2P | 1144800 | 273.2 |
| UE5.1-Grimhook | Grimhook | 2667430 | 159.4 |
| UE4.20-HeliumRain | HeliumRain | 681330 | 109.6 |
| UE5.6-Satisfactory | Satisfactory | 526870 | 53.4 (anchor only) |

> **Action:** copying these 10 files into maintainer-held storage is 6.25 GB and removes the
> single largest fragility in the corpus. `UE5.6-Satisfactory` needs the whole
> `Engine/Binaries/Win64` PDB set, not just the anchor.

> ⚠ **EVERSPACE 2 already patched past these rows.** Measured 2026-09-05: the installed exe was replaced
> on 2026-09-01 and is UE 5.6.1 (detector code 506), with a 1.98 GB `ES2-Win64-Shipping.pdb` carrying the
> new exe's mtime beside it; the two archived builds are both 5.5.4. `UE5.5-Everspace2` in
> `tools/ghidra/corpus-provenance.tsv` still names the live Steam location while recording the old
> 169,063,424 B, so it describes the archive copy, not what is installed. (Moved from the handover,
> 2026-10-08.)

### 3b. Archive-held — safe, already yours

6 files, 1.39 GB, under `D:\tmp\Game archive` (mirrored to `X:\UE_Analyze_Data\Game archive`):
`UE4.22-Satisfactory` 700.1, `UE4.24-DropIn` 272.6, `UE5.2-SatGameDLL` 200.5,
`UE4.25-Everspace2` 165.8, `UE5.2-Satisfactory` 44.6, `UE4.26-Satisfactory` 36.3 MB.
These are superseded depot versions — **Steam cannot re-serve them.**

### 3c. Self-built — safe, and the cheapest backup on the machine

4 files, 484.4 MB. `UE5.8-StackOBot` 180.0, `UE5.7.4-StackOBot` 169.2, `UE4.23-Flying` 79.1,
`UE4.15-Flying` 56.1 MB.

> **The whole self-built oracle set — 4 exe + 4 pdb — is 897.0 MB.** Backing that up is under 1 GB
> and is the highest value-per-byte action available. All four have a second byte-identical copy
> today (see `duplicate_copies`), but three of those live in **volatile** locations
> (`D:\Unreal Projects\…\Binaries\` / `Saved\StagedBuilds\`) that a rebuild or Clean destroys.

Engines for a rebuild are installed on `C:\Program Files\Epic Games`: **UE_4.15 (22.54 GB),
UE_4.23 (45.33), UE_4.27 (54.19), UE_5.7 (74.13), UE_5.8 (84.70)** = 280.89 GB. Nothing is
registered with the Epic Launcher (`LauncherInstalled.dat` → `"InstallationList": []`), so a
delete is likely not re-downloadable in place — verify before removing.

### 3d. Not installed on Steam — but the BINARY is not lost

**Superseded 2026-07-29.** Six rows this document and the manifest called "binary GONE" —
`UE4.18-FF7R`, `FF7Re`, `Hogwarts_Legacy`, `Manor Lords`, `Octopath`, `DQ_I_II_HD2D` — all have
**md5-identical copies** under `D:\UE_Analyze_Data\Game Binary backup`, verified against the MD5
Ghidra recorded at import. Nothing was ever missing: `build_corpus_manifest.py` did not search
that root, and `find_duplicate_copies` additionally skipped the search for STEAM rows *and* for
GONE rows — exactly the two cases where a surviving copy matters most. Both guards are removed
and `D:\UE_Analyze_Data` is now first in `DUPE_ROOTS`. **36 of 38 rows now carry at least one
verified duplicate.** These titles therefore need a reinstall only to recover a **PDB**, never
the binary.

**`UE4.18-FF7R` — app 1462040, Steam title "FINAL FANTASY VII REMAKE INTERGRADE"** (the manifest's
`FINAL FANTASY VII REMAKE` is the *installdir*, not the store name — search the store for
INTERGRADE). `needs_pdb` is `null` = unmeasured. `sweep.sh` comments say it has no PDB and its
truth was derived by disassembly, but that is prose, not measurement. Install → re-run
`build_corpus_manifest.py` → the field settles itself.

### When a title reinstalls to a different path

**It already happened four times.** Octopath moved D:→E:, Manor Lords D:→H:, DQ I&II HD-2D D:→H:,
and Drop In reinstalled to a *different folder name* than the manifest recorded
(`Drop In - VR Battle Royale`, not `DropIn`).

The rule the tooling implements, and the rule to follow by hand:

1. **Key on Steam app id**, never on a path. `binary_last_seen` is a *hint*.
2. Resolve `appmanifest_<id>.acf` → `installdir` → bounded depth-3 search for `Binaries/Win64`,
   **shallowest depth wins** (otherwise P3R's Artbook and Soundtrack become phantom rows). Same
   logic as `ui/UE5DumpUI/Services/ProxyDeployService.cs` (`MaxBinariesSearchDepth = 3`).
3. `libraryfolders.vdf`'s `apps` map is **not proof of installation** — only
   `appmanifest_<id>.acf` **plus** the installdir folder are.
4. Re-run `build_corpus_manifest.py`; it records the new location and tags the row
   `relocated-library`. Do not hand-edit.
5. Modular builds scatter programs across **both** `Engine/Binaries/Win64` and
   `<Game>/Binaries/Win64` — resolve per program, not per title.

-----

## 4. Footprint (measured 2026-07-29 00:20–00:45 local)

Measured on the `D:`-rooted machine. Sizes are a property of that disk, not of the repo — see the
header note. The corpus has since moved (external USB → internal NVMe, 2026-08-01) and grew to
63 `.rep` / 182.3 GB, so the row below is a 2026-07-29 snapshot, not current. Re-measure with
`preflight.py --sizes` on whichever machine you are on.

| Asset | Size | Notes |
|-------|------|-------|
| `$GHIDRA_PROJS` (43 `.rep`, measured at `D:\Tools\GHIDRA_Projs`) | **120.94 GB** | 38 referenced by `sweep.sh` = 113.51; 5 orphans = 7.43 |
| — of which ORACLE projects (29 tags) | 92.60 GB | `sweep.sh` supplies `GS_TRUE` |
| — of which NOISE-PROBE projects (9 tags) | 20.93 GB | no truth; they only prove a pattern still fires |
| `C:\Program Files\Epic Games\UE_*` (5) | **280.89 GB** | biggest single item on the machine |
| Steam corpus payload (26 installed apps) | **558.47 GB** | 1 not installed (FF7R) |
| `D:\Unreal Projects` | 83.57 GB | only **22.47 GB** is corpus (ProjectTitan 61.11 GB is not) |
| `D:\UE_Analyze_data\Game archive` | **1.6 GB** / 678 binaries | MOVED from `D:\tmp\` and pruned; re-measured 2026-08-01 |
| `D:\UE_Analyze_data\Game Binary backup` | **10.2 GB** / 33 exe + 9 pdb | 24 hash-verified as corpus builds |
| `D:\UE_Analyze_data\Varies Version builds` | 7.28 GB | the self-built oracle packages (root moved X: -> D:) |
| Self-built oracle exe+pdb only | **897.0 MB** | the backup that matters most |

Free space at 00:18: C 980.3 · D 2362.1 · E 258.2 · F 801.2 · G 931.3 · H 636.2 · X 1670.1 GB.
**There is no disk crunch today.** D: alone can absorb the entire 120.94 GB Ghidra move. E: is the
tightest at 28.6% and holds two corpus titles — it is the drive most likely to force a decision
first, and it is *not* the Ghidra target.

### Recovering a binary you no longer have — `corpus-provenance.tsv`

`tools/ghidra/corpus-provenance.tsv` is a **snapshot** (2026-07-29) of every row's build identity.
It exists because `build_corpus_manifest.py` **nulls `steam_buildid`/`size`/`sha256` the moment a
row drifts** — correctly, since it must never assert the wrong build, but that destroys the pointer
to the build a `.rep` was made from. Palworld drifted that same day. **Re-snapshot before games
update, not after.** Four routes, strongest first:

| route | rows | what it gives you |
|---|---|---|
| `STEAMDB-BUILDID` | 22 | exact: SteamDB app → Builds → buildid → manifest |
| `STEAMLOG-MANIFEST` | **6** | Steam's own `console_log.txt` logs every depot fetch with app, depot, **exact manifest** and a timestamp. An archived file's mtime falls inside its download, so mtime → log line → manifest. A strong **candidate**, confirmed by md5. |
| `STEAM-BACKUP-MANIFEST` | **4** | the **exact depot manifest id** out of a `sku.sis` in `X:\SteamLibrary backup` — `steamcmd +download_depot <app> <depot> <manifest>`. Survives uninstall *and* delisting. |
| `REBUILD` | 4 | self-built; recompile from the installed engine |
| `STEAMDB-MANIFEST` | **2** | manifest id resolved by hand off SteamDB's depot history, tie broken by an independent repo record (see below) |
| `NONE-HASH-ONLY` | **0** | — |

**Nothing in the corpus is now without a recovery route.**

Two things that were wrong on the first pass and are worth not repeating:

* **Use `file_modified`, never `file_created`.** A copy RESETS ctime but PRESERVES mtime, so an
  *install → copy → uninstall* workflow keeps the original Steam write time. Measured: Hogwarts
  ctime `2026-07-29` but mtime `2025-12-04`; Octopath mtime `2025-05-12`; Palworld mtime
  `2026-07-15` = the pre-patch corpus build. Reading ctime made the dates look destroyed by the
  corpus move when they were not.
* **`sku.sis` beats every date.** The Steam backup descriptor records appid, depots and the exact
  manifest id. Harvesting `X:\SteamLibrary backup` moved **FF7 Remake, FF7 Rebirth, Hogwarts
  Legacy and DQ I&II** out of "md5 only" and into "exactly reproducible" — the three biggest
  uninstall candidates in the drop list below.

⚠ **`console_log.txt` ROTATES — it is the most perishable source here.** It was snapshotted on
2026-07-29 holding only ten download records, and those ten happen to cover every archive row.
Re-snapshot after any depot fetch you care about; once rotated, those manifest ids are gone and
the archive rows fall back to md5-only. The full capture, preserved because the log will not keep it:

```
2026-07-25 22:55:40  app=1128920 depot=1128921 manifest=1228092871900976040
2026-07-25 23:04:13  app=1128920 depot=1128921 manifest=1228092871900976040
2026-07-25 23:16:20  app=526870  depot=526871  manifest=5235170890177666837
2026-07-25 23:22:05  app=526870  depot=526871  manifest=4713640358549407449
2026-07-25 23:24:28  app=526870  depot=526871  manifest=3007809920758804289
2026-07-25 23:53:12  app=526870  depot=526871  manifest=5072022484048628830
2026-07-26 00:22:12  app=526870  depot=526871  manifest=4631200912822720421
2026-07-26 00:25:02  app=526870  depot=526871  manifest=5971929977835941106
2026-07-26 13:19:00  app=1144800 depot=1144801 manifest=5221052602514244898
2026-07-26 14:21:59  app=1144800 depot=1144801 manifest=8071507168854653981
```

**`UE4.22-Satisfactory` is the row that matters most** — it is the sole oracle for
`GNAM_SAT422_1` (priority 715, the UE 4.22 GNames lander), so losing it leaves that pattern with
no proof it works. It is **not** unrecoverable, only inconvenient: its best candidate is
`526870:526871:5235170890177666837`, reached two independent ways — the maintainer's own reading of
the Steam log (SteamDB dates it 2020-06-08, the right era for Satisfactory on UE 4.22), and the
mtime correlation above picking the same manifest as the download closest after `2026-07-25 23:13`.
Download it and confirm `md5 == a1df9f191f8978e6c05d7919237be565` before trusting it. It is also
mirrored twice already (67 MB on `D:\tmp\Game archive` + `X:\UE_Analyze_Data\Game archive`).

Two manifests in the log — `4631200912822720421` (2020-09-25) and `5971929977835941106`
(2021-01-11) — correlate to no archive row. They are later Satisfactory builds that may or may not
share a UE version with one already held; treat them as leads, not as identified rows.

**`UE5.5-Everspace2` (ES2-0517) is RESOLVED — `1128920:1128921:4415922863161237626`**, and the way
it was pinned is worth copying. SteamDB's depot history for 1128921 lists manifests published
2024-11-14, 2025-05-12, 2025-05-16, 2025-05-28 and 2025-06-17. A snapshot taken on **2025-05-17**
must have been running the **2025-05-16** one. The 2025-05-12 fallback is then *excluded* by an
independent record already in this repo: `sweep.sh`'s own note calls `Everspace2b` **"two manifests
newer (2025-06-17 vs the 05-17 snapshot)"** — newer than 2025-05-16 there are exactly two
(2025-05-28, 2025-06-17), whereas newer than 2025-05-12 there would be three. `Everspace2b` is
therefore the 2025-06-17 manifest `735055807809773736`, which is also the one currently installed.
Confirm on download with `md5 == 1a1b0ede76b80a173969343683579129`.

**Method worth reusing: date the snapshot, then let an existing repo note break the tie.** Neither
the SteamDB list nor the project name alone was decisive; a sentence written months earlier for an
unrelated reason was.

### Do this BEFORE any of the drop steps: re-import without analysis

**~88% of a `.rep` is Auto Analyze output, and the sweep does not read any of it.** This is the
only lever here that frees space at **zero** capability cost — every step in the table below
trades something away. Measured 2026-07-29, and verified rather than assumed:

| | `UE423_Flying-Win64-Shipping` (49 MB EXE) |
|---|---|
| `-noanalysis` import | **169 MB, 46 seconds** |
| fully analysed | **1,369 MB** |
| | **8.1×  —  analysis is 88% of the `.rep`** |

**Verified equivalent, not merely assumed:** `scan_patterns.java` run against the raw import
returns the *identical* five verdicts to the recorded `UE4.23-Flying` row in `REPORT.md` —
`GOBJ_ES53_1` OK-BEHIND, `GNAM_DI427_2` / `GWLD_TQ_1` / `SPARSE_DI427_1` / `GENG_X1` all
UNIQUE-OK. It works because the scanner touches only `getMemory()` / `getBytes()` /
`getImageBase()`; the imported program keeps the file's bytes, which is all it reads.
Corroborating the ratio at scale: the two mid-analysis `*_UE581` projects sit at ~3× their EXE
size while every finished project sits at 26–30×.

```bash
analyzeHeadless "$GHIDRA_PROJS" <Name> -import "<path>\<file>.exe" -noanalysis
```

**Three caveats, all load-bearing:**
1. **Needs the original binary.** Re-importing is only possible where the file still exists — so
   this does NOT apply to `ES2-0517` and the others in §"Never drop" whose source is gone. For
   those, Ghidra's *File → Export → Original File* should recover the bytes to re-import from,
   but **that path is untested here** — do not bet an irreplaceable `.rep` on it.
2. **Ten scripts genuinely need analysis** — `dump_func`, `decompile_functions`, `find_callers`,
   `dump_xrefs2`, `dump_global_xref_aob`, `find_gobjects`, `dump_vtables`, `dump_types`,
   `pe_probe`, `probe`. Those are for *reading code* when mining a new pattern or cracking a
   symbol-less binary — an occasional, per-investigation need. Analyse into a throwaway project
   then delete it, rather than storing analysis for all 43 permanently.
3. **`find_syms3.java` needs the PDB applied**, which `-noanalysis` skips. For the five sweep
   globals this no longer matters: `tools/pe/pdb_globals.py` reads them straight from the PDB
   with no Ghidra at all.

Applied across the 120.94 GB in `D:\Tools\GHIDRA_Projs` this is far and away the largest
zero-loss saving available; do it before considering a single deletion below.

### Drop order under pressure

Take these strictly in order. Re-run `preflight.py --sizes` between steps.

| Step | What | Frees | Capability lost |
|------|------|-------|-----------------|
| 0 | The orphan `.rep` — `Meltopia` 3.35, `Satfi426` 0.68, `ISDefenseEditor_UE410` 0.16, `ES1` 0.16 | **4.35 GB** | **None.** GROUND-TRUTH.md already calls Meltopia "superseded and can be deleted" and Satfi426 "superseded, do not re-chase". **Both 2026-07-29 caveats on this row are now discharged:** `UE423_Flying-Win64-DebugGame` (3.08 GB) is no longer an orphan at all — it is the live project behind the `UE4.23-FlyingDbgGame` sweep row, so it has been REMOVED from this list; and `ISDefenseEditor_UE410` is no longer the only evidence for the pre-4.11 floor, which is now measured by two 4.10.4 rows with public-symbol PDBs (`UE410_Game_Shipping` / `..._Development`; their type stream has 0 records, measured 2026-10-08, so the 4.10 type oracle is IS Defense's own PDB). Drop all four without further thought. |
| 1 | 4 zero-contribution noise probes — `UE4.27-Artisan` 1.35, `UE5.5-ManorLords` 2.88, `UEx-DQ12HD2D` 1.60, `UE5.2-SatGameDLL` 2.28 | **8.11 GB** | Nothing measurable. Simulated exclusion: **0** patterns stop firing, **0** lose correctness evidence. Only §6 denominator mass. |
| 2 | 5 exclusive-exercise noise probes — `UE4.27-Hogwarts` 3.65, `UE5.6-TQ2` 3.30, `UE5.1-Palworld` 2.77, `UE4.x-FF7Rebirth` 2.33, `UE4.18-Octopath` 0.77 | **12.82 GB** | **0** correctness. 6 patterns fall into the already-populated "never hits anywhere" bucket (`GOBJ_RE1`, `GOBJ_V9`, `GOBJ_OT_2`, `GWLD_TQ_2`, `GWLD_SF_3`, `GWLD_TQ_3`). Taking steps 1+2 removes 865.5 MB of the 2,877.2 MB monolithic executable mass (30.1%). Hogwarts first: it is Denuvo-packed and contributes 4.0% of §6 **numerator** hits from non-`.text` bytes. |
| 3 | Version-redundant ORACLES, **one at a time**, re-sweeping between each — `UE5.5-Meltopia` 4.54, `UE4.27-DQ7R` 3.89, `UE4.27-Maelstrom` 3.81, `UE4.27-Breeders` 2.84, `UE4.20-HeliumRain` 1.39 | ≤ **16.47 GB** | Individually nothing. **Not jointly redundant**: Breeders + Maelstrom are the two independent symbolised 4.27s that proved DropIn's 32-byte `FUObjectItem` is a config artifact — keep at least one. Meltopia is the second symbolised *monolithic* 5.5 in a modular-heavy corpus. |
| 4 | Steam uninstalls, biggest first — FF7 Rebirth 159.31, Hogwarts 71.19, Avowed 67.60, Palworld 38.35 | **336.45 GB** (60% of the Steam payload) | **Nothing today.** All four are `needs_pdb=false`; their `.rep` (13.83 GB total) already holds everything the sweep reads. Cost is re-download time *if* a re-import is ever wanted. |
| 5 | Epic engines you will not rebuild against | up to **280.89 GB** | Only the ability to rebuild a self-built oracle. Redundant *if* §3c's 897 MB is backed up. Verify Launcher re-download first. |

### Never drop

`.rep` files holding a **sole-landing** pattern (removing one regresses a target on an engine
version outright) — 7 such patterns across 5 tags:

| Tag | .rep GB | Sole-landing patterns |
|-----|---------|----------------------|
| UE5.7-Solarpunk | 5.43 | `GNAM_SAT425_3`, `SPARSE_SP57_1` (two — the most of any entry) |
| UE4.22-Satisfactory | 2.58 | `GNAM_SAT422_1` (+ 5 sole-ok, the highest count in the corpus) |
| UE5.6-Satisfactory | 4.08 | `GWLD_SF_1` |
| UE4.20-Everspace | 1.67 | `GOBJ_G42_4` |
| UE5.3-Avowed | 5.08 | `SPARSE_AV53_1` |

> `UE4.18-FF7R` is the sole *landing* site of `GOBJ_RE2` but its **sole-ok count is 0** — the
> pattern stays proven on `UE4.15-Flying` and `UE4.18-DQXIS`, and FF7R's truth coverage
> `{GEngine, GObjects}` is a strict subset of `UE4.18-DQXIS`'s. It belongs in the redundant tier,
> not the floor. This corrects an earlier "6 tags" framing.

**Version coverage is an override axis, never a tiebreak.** 18 engine versions rest on exactly one
oracle, and 12 of those oracles score `soleLanding = 0` **and** `soleOk = 0` — including the only
4.23, the only 5.4 and the only 5.8. A script ranking on pattern uniqueness alone would delete
them without a single metric objecting.

**The Satisfactory family is a concentration trap.** `{UE4.22, UE4.26, UE5.2-Satisfactory,
UE5.2-SatGameDLL, UE5.6-Satisfactory}` = 12.47 GB across 5 projects, and is the sole source of
truth for **13 patterns**. A single "I'll clear out the Satisfactory stuff" is the most damaging
action available on this disk.

### Recoverability — RE-MEASURED 2026-08-01. Nothing is unrecoverable.

This section previously said ES2-0517 and FF7R were unrecoverable. **Both claims were wrong**, and
the reasoning error is worth keeping because it is easy to repeat:

> *"Steam serves only the current build, so no reinstall restores it."*
> True for `steam://install`. **False for `DepotDownloader -manifest <id>`**, which fetches a
> specific historical manifest. That one sentence is what made the whole tier look hopeless.

Measured by hashing every `.exe`/`.dll` under both archive roots and matching against
`corpus-manifest.json`'s recorded `binary_sha256` / `binary_md5` — **a matching filename proves
nothing, a matching hash proves everything**:

| tier | rows | `.rep` GB | route |
|---|---|---|---|
| **A1** source still at its recorded path | 22 | 64.8 | re-import now |
| **A2** in a backup root, **hash-verified** | 28 | 75.4 | re-import now |
| **A3** self-built | 5 | 14.1 | rebuild from the installed engine |
| **A total** | **55** | **154.2 (90%)** | **already on this machine** |
| **B1** `depot:manifest` recorded | 2 | 18.0 | `DepotDownloader`, IDs already known |
| **B2/B3/C** | **0** | **0** | — |

**Nothing requires a SteamDB lookup.** The two B1 rows are the ES2 pair:

```
UE5.5-Everspace2   1128920:1128921:4415922863161237626    (SteamDB: seen 16 May 2025)
UE5.5-Everspace2b  1128920:1128921:735055807809773736     (SteamDB: seen 17 Jun 2025)
```

⚠ **Still unverified:** that Valve is *currently serving* those two manifests. SteamDB listing a
manifest means it was seen, not that the CDN still has it. Recorded ID + listed ≠ downloaded —
**try it before relying on it**, and verify the result against the recorded hash.

`UE4.18-FF7R` needed no download at all: `ff7remake_.exe` is present in the backup root and its hash
matches the corpus build. The 91.64 GB depot-cache restore path is moot.

What remains true about ES2: `sweep.sh` calls the pair the corpus's *only* same-game cross-build
control — the only thing that can answer *"does a pattern survive a game update?"* — so keep it.
⚠ Caveat unchanged: in the last sweep both ES2 rows resolved **identically** on all five targets, so
the differential signal is currently a null result. The value is in retaining the control.

> **Method, reusable.** `binary_sha256` is deliberately NULLED on a drifted row; `binary_md5` never
> is (§1). A recoverability check that only compares SHA-256 therefore under-reports — it silently
> lost 4 rows here until the MD5 fallback was added. Always try both.

### Cheap hedges worth knowing

* `D:\UE_Analyze_data\Game archive\DropIn` already holds an **unimported second 4.24 build**
  (`UE4.24.2`, MD5 `3D245058…`, distinct from `UE4.24`'s `2F640FF0…`). Importing it creates a
  second same-game cross-build pair for ~1.9 h of analysis — the cheapest available hedge against
  ever losing the ES2 pair.
* `X:\$RECYCLE.BIN` holds a complete `depot_1128921` ES2 4.25.2 tree (exe + 165.8 MB pdb).
  **"Empty recycle bin" silently deletes a corpus copy.**
* ⚠ **`X:` IS A DIFFERENT MACHINE.** `X:\Ghidra_Projs_Backup` / `sync_Ghidra_Projs.PS1` /
  `sync.cmd` are not reachable from the laptop this doc is usually edited on, and the maintainer
  confirms the backup lives on the other machine (2026-08-01). An earlier note here read
  "`X:\Ghidra_Projs_Backup` **is empty** … the whole ~120 GB lives on one machine"; on 2026-08-01 I
  compounded it by checking `X:` *from the laptop*, finding nothing, and writing "the corpus is
  single-copy" into three documents. **A drive-letter check only reports the machine you ran it
  on.** Backup state has to be asserted from the machine that holds it, with the machine named.

-----

## 5. Routine

**Before a sweep:** `py tools/ghidra/preflight.py`. Exit 0 → run it. Exit 2 → fix the project first.
Exit 3 → the manifest and `sweep.sh` disagree; regenerate the manifest, do not hand-edit.

**After installing/uninstalling/moving a corpus title, or after any game patch:**
`py tools/ghidra/build_corpus_manifest.py`, then `preflight.py --verify-hash` to catch a build that
drifted under you. The manifest is a **point-in-time snapshot** — it was already stale within an
hour of first generation (a title reinstalled and it still read `BINARY GONE`).

**Before deleting anything:** `preflight.py --sizes`, then this document's §4 in order.

-----

## 6. Open questions

* Does restoring the `X:\SteamLibrary backup` FF7R depot cache reproduce MD5 `3ea9092f…`?
  Untestable without doing it.
* Do the Epic engine trees re-download from the Launcher after deletion, given
  `"InstallationList": []`?
* `UE5.6-Satisfactory` PDB coverage is tracked for the anchor program only; the other 8 programs
  are unaccounted for.
* Drift detection does not yet compare `GLOB` or `GS_TRUE` — a re-scoped truth prefix changes
  sweep results silently.
* No integrity/restore check exists for the `.rep` databases themselves. On a spinning HDD, silent
  corruption is a more likely loss mode than a deletion decision, and only a sweep would notice.
* Re-analysis cost per project is **UNKNOWN**. The `.gpr`-creation → newest-`.gbf` heuristic is not
  reproducible (it returns ≤ 0 h for six projects whose timestamps were reset by a copy, and
  924 h for Avowed), so any "GB reclaimed per re-analysis hour" ranking is unsupported. Time a
  real re-import before relying on one.


-----

## 6. `D:` is the working copy, `X:` is the backup

`D:\UE_Analyze_Data` holds three trees and is the one to keep fast and current:

| tree | size | contents |
|---|---|---|
| `Game Binary backup` | 11 GB | per-title EXE (+PDB where one exists), already consolidated |
| `Game archive` | 21 GB | DropIn / ES2 / Satisfactory full installs |
| `Varies Version builds` | 8.2 GB | the self-built engine samples, per config |

`X:\UE_Analyze_Data` mirrors this onto a spinning disk with 800k+ files. **Do not walk it in a
tool.** `build_corpus_manifest.py` now builds a single lazy basename index over all dupe roots
instead of walking per row — without that, adding `X:` to the search would have walked it 38
times. Full regeneration is 75 s.

Everything a sweep needs is derivable from EXE+PDB, so the preservation target is **12.17 GB**
(37 distinct EXEs 4.06 GB + 36 distinct PDBs 8.11 GB), not the ~123 GB of `.rep`.

## 7. UE 4.27.2 — built, not yet in the corpus

`D:\UE_Analyze_Data\Varies Version builds\4.27.2\{DebugGame,Development,Shipping}\Win64\`, each
with EXE + PDB. **Import the Development one and it replaces `UE4.27-DropIn`'s sole-oracle role.**

Why Development specifically: `GOBJ_DI427_1/2/3` are the only patterns for which DropIn is the
sole oracle, and what they encode is the **32-byte `FUObjectItem`** (`shl r,5`). Those 8 bytes are
`TStatId`, gated at 4.27 by `#if STATS || ENABLE_STATNAMEDEVENTS_UOBJECT` (`UObjectArray.h`
@ `4.27.2-release`). `STATS` is 0 in Shipping, so a **Shipping 4.27 sample adds nothing** —
Breeders and Maelstrom already cover the stock 24-byte item, which is how GROUND-TRUTH.md proved
the 32-byte item is a config artifact rather than a 4.27 trait.

This converts a sole-oracle dependency on an external store — where a patch can silently replace
the build, as happened to ES2-0517 — into a locally rebuildable asset. It also gives a second
three-config control group after 4.23, which can re-test the config-axis conclusions.

## 8. Moving the corpus to a spinning disk: verify with a sweep, not with preflight

`preflight.py` reads `.rep` metadata (`idata/**/*.prp`) and **never opens a program database**.
A silently corrupted `.rep` — the realistic failure mode when moving ~120 GB onto an HDD — looks
perfectly healthy to it. There is no cheap integrity check. **After the move, run one full sweep
and diff `out/sweep/REPORT.md` against the previous one; an unchanged regression matrix is the
only real acceptance test.**
