# Reference builds — the engine samples we build ourselves

**What these are.** Stock Unreal projects packaged by the maintainer from an unmodified Epic
engine, used as **oracles**: binaries whose ground truth we can derive exactly (they ship full
PDBs) and against which `Himmel.h`'s AOB patterns are regression-tested.

**They are not games, and this is not [test-games.md](test-games.md).** That file records
*per-game behaviour* — GWorld status, FUObjectItem stride, which proxy DLL a title needs — which
is meaningless here: a stock template has no publisher quirks, no anti-tamper, no fork. The two
files answer different questions:

| | [test-games.md](test-games.md) | this file |
|---|---|---|
| subject | real shipped titles | stock engine samples we build |
| question | "what does THIS game do differently?" | "what does the ENGINE do at version X?" |
| truth | usually inferred, rarely a PDB | exact, from a PDB |
| replaceable | no — reinstall or lose it | yes, rebuild from the engine |

**Do not call them "artifacts".** In this repo that word already means build output (`dist/*.dll`).
The corpus's own vocabulary is **oracle** (a row with ground truth) vs **noise probe** (a row
without); `corpus-manifest.json` tags these `source: self-built`.

**Authoritative data lives elsewhere, on purpose.** Ground truth is in
[`tools/ghidra/sweep.sh`](../tools/ghidra/sweep.sh) in executable form — prose drifts, a script
that runs cannot. Per-version reasoning is in
[`tools/ghidra/GROUND-TRUTH.md`](../tools/ghidra/GROUND-TRUTH.md). **This file holds only what
neither can: the inventory, why each build exists, and how to make another one.**

-----

## Why they exist at all

A shipped game gives you one data point you cannot control: engine version, build config, template,
compiler and studio patches all vary at once. A self-built sample fixes every variable but one, so
it can answer questions the game corpus structurally cannot:

* **Config-only A/B.** UE 4.27.2 Flying in all three configs proved `GOBJ_DI427_*` encodes a
  *Development build*, not "UE 4.27" — DropIn, their previous sole oracle, is itself Development,
  which is why it looked version-shaped. Hit counts 832 / 1415 / 246 on Dev+DbgG, **0** on Shipping.
* **Bisecting a regression.** 5.3 vs 5.4 pinned the non-Shipping GNames collapse to a single
  version step (15/15 patterns correct at 5.3, 1/6 at 5.4).
* **A control for a licensee fork.** Stock 5.4.4 is MindsEye's exact patch version, so
  [mindseye-fork-notes.md](mindseye-fork-notes.md)'s "the fork changed X" claims become measurable
  deltas instead of inference.
* **Measuring an unsupported floor.** 4.10 is in the corpus *to fail* — see below.

-----

## Inventory

Regenerate with `py tools/ghidra/inventory_builds.py --md`; do not hand-edit.

| engine | config | sweep tag | binary |
|---|---|---|---|
⛔ **There are no `Test`-configuration rows here, and that is a wall rather than an oversight.**
A UE 5.7+ **Test** build is the only shape that produces a **40-byte `FUObjectItem`**
(`UE_BUILD_TEST` began defining `ENABLE_STATNAMEDEVENTS_UOBJECT` at 5.7.0, adding `TStatId` +
`StatIDStringStorage` after `ClusterRootIndex`), and audit **A5** added stride 40 to the sweep
for it. It cannot be verified with what is here, on two independent counts:

* **A launcher engine cannot build it at all.** `Engine\Binaries\Win64\` ships
  `UnrealGame.target`, `-DebugGame.target` and `-Shipping.target` — there is no `-Test.target`,
  and `BaseEngine.ini` contains no `Configuration="Test"`. UnrealBuildTool refuses at target
  validation, before any linker runs.
* **DumperTest cannot serve even if it could be built.** It is UE **5.4**, and the `Build.h`
  change landed at 5.7.0, so a Test build of it would still yield a 24-byte item.

Producing one means a from-source engine build (hours, >150 GB). **Not worth it for one stride
candidate** — A5's 40-byte half is deliberately left unverified, and no register row was opened
for it, because a row with no producible fixture is unfalsifiable. A5's other two halves (the
explicit tie-break and the high-null warning) need no fixture: the tie-break is a provable
no-op over the current candidate set, and the warning's silence on a healthy pool is the
expected result.

| 4.10 | Development | UE4.10-GameDev | `4.10\Development\UE4Game.exe` |
| 4.10 | Shipping | UE4.10-Game | `4.10\Shipping\UE4Game-Win64-Shipping.exe` |
| 4.15.3 | DebugGame | UE4.15-FlyingDbgGame | `4.15.3\DebugGame\UE415_Flyinh-Win64-DebugGame.exe` |
| 4.15.3 | Development | UE4.15-FlyingDev | `4.15.3\Development\UE415_Flyinh.exe` |
| 4.15.3 | Shipping | UE4.15-Flying | `4.15.3\Shipping\WindowsNoEditor\UE415_Flyinh\Binaries\Win64\UE415_Flyinh-Win64-Shipping.exe` |
| 4.23.1 | DebugGame | UE4.23-FlyingDbgGame | `4.23.1\Binaries_DebugGame\Win64\UE423_Flying-Win64-DebugGame.exe` |
| 4.23.1 | Shipping | UE4.23-Flying | `4.23.1\Binaries_Shipping\Win64\UE423_Flying-Win64-Shipping.exe` |
| 4.23.1 | Shipping | **— not swept —** | `4.23.1\Shipping\WindowsNoEditor\UE423_Flying\Binaries\Win64\UE423_Flying-Win64-Shipping.exe` |
| 4.27.2 | DebugGame | **— not swept —** | `4.27.2\1stPerson_DebugGame\WindowsNoEditor\UE427_FirstPerson\Binaries\Win64\UE427_FirstPerson-Win64-DebugGame.exe` |
| 4.27.2 | Development | **— not swept —** | `4.27.2\1stPerson_Development\WindowsNoEditor\UE427_FirstPerson\Binaries\Win64\UE427_FirstPerson.exe` |
| 4.27.2 | Shipping | **— not swept —** | `4.27.2\1stPerson_Shipping\WindowsNoEditor\UE427_FirstPerson\Binaries\Win64\UE427_FirstPerson-Win64-Shipping.exe` |
| 4.27.2 | DebugGame | **— not swept —** | `4.27.2\3rdPerson_DebugGame\WindowsNoEditor\UE427_3rdPerson\Binaries\Win64\UE427_3rdPerson-Win64-DebugGame.exe` |
| 4.27.2 | Development | **— not swept —** | `4.27.2\3rdPerson_Development\WindowsNoEditor\UE427_3rdPerson\Binaries\Win64\UE427_3rdPerson.exe` |
| 4.27.2 | Shipping | **— not swept —** | `4.27.2\3rdPerson_Shipping\WindowsNoEditor\UE427_3rdPerson\Binaries\Win64\UE427_3rdPerson-Win64-Shipping.exe` |
| 4.27.2 | DebugGame | UE4.27-FlyingDbgGame | `4.27.2\DebugGame\Win64\UE427_Flying-Win64-DebugGame.exe` |
| 4.27.2 | Development | UE4.27-FlyingDev | `4.27.2\Development\Win64\UE427_Flying.exe` |
| 4.27.2 | Shipping | UE4.27-FlyingShipping | `4.27.2\Shipping\Win64\UE427_Flying-Win64-Shipping.exe` |
| 5.3 | DebugGame | UE5.3-ThirdPersonDbgGame | `5.3\DebugGame\Windows\ThirdPerson53\Binaries\Win64\ThirdPerson53-Win64-DebugGame.exe` |
| 5.3 | Development | UE5.3-ThirdPersonDev | `5.3\Development\Windows\ThirdPerson53\Binaries\Win64\ThirdPerson53.exe` |
| 5.3 | Shipping | UE5.3-ThirdPerson | `5.3\Shipping\Windows\ThirdPerson53\Binaries\Win64\ThirdPerson53-Win64-Shipping.exe` |
| 5.4 | DebugGame | UE5.4-ThirdPersonDbgGame | `5.4\DebugGame\Windows\ThirdPerson54\Binaries\Win64\ThirdPerson54-Win64-DebugGame.exe` |
| 5.4 | Development | UE5.4-ThirdPersonDev | `5.4\Development\Windows\ThirdPerson54\Binaries\Win64\ThirdPerson54.exe` |
| 5.4 | Shipping | UE5.4-ThirdPerson | `5.4\Shipping\Windows\ThirdPerson54\Binaries\Win64\ThirdPerson54-Win64-Shipping.exe` |
| 5.7 | DebugGame | UE5.7.4-StackOBotDbgGame | `5.7\Stack_O_Bot\DebugGame\Windows\StackOBot\Binaries\Win64\StackOBot-Win64-DebugGame.exe` |
| 5.7 | Shipping | UE5.7.4-StackOBot | `5.7\Stack_O_Bot\Shipping\Windows\StackOBot\Binaries\Win64\StackOBot-Win64-Shipping.exe` |
| 5.8 | DebugGame | UE5.8-TitanDbgGame | `5.8\Binaries_DebugGame\Win64\Titan-Win64-DebugGame.exe` |
| 5.8 | DebugGame | UE5.8-StackOBotDbgGame | `5.8\Stack_O_Bot\DebugGame\Windows\StackOBot\Binaries\Win64\StackOBot-Win64-DebugGame.exe` |
| 5.8 | Development | UE5.8.1-StackOBotDev | `5.8\Stack_O_Bot\Development\Windows\StackOBot\Binaries\Win64\StackOBot.exe` |
| 5.8 | Shipping | UE5.8-StackOBot | `5.8\Stack_O_Bot\Shipping\Windows\StackOBot\Binaries\Win64\StackOBot-Win64-Shipping.exe` |
| 5.8 | Shipping | UE5.8.1-StackOBot | `5.8\Stack_O_Bot\Shipping_581\Windows\StackOBot\Binaries\Win64\StackOBot-Win64-Shipping.exe` |

**8 engine versions, 30 binaries, 23 swept.** The 7 unswept ones are listed below **with reasons** —
that is the point of this file. An unswept build is invisible to `preflight.py`, which walks
`sweep.sh` outward to files and so cannot see a binary no row mentions.

### Deliberately not swept

* **4.27.2 FirstPerson ×3 and 3rdPerson ×3 (6 binaries).** Built to test whether the *template*
  affects engine-global resolution. It does not: at 4.27, Flying vs 3rdPerson gave **identical
  voter sets down to the individual pattern IDs**. Adding them as rows would triple the 4.27 scan
  cost for zero information. **Keep them** — they are the evidence for that claim, and 3rdPerson is
  the Character-based target the gameplay-feature matrix needs (Flying's pawn has no
  `CharacterMovement`).
* **4.23.1 Shipping, the `Shipping\WindowsNoEditor\...` copy.** A *repackage*, so it is a
  genuinely different PE from the swept `Binaries_Shipping\Win64\...` one (md5 `43f6b130` vs
  `f61ec1be`) — **not** a duplicate, and not a backup of the swept row.

### 4.10 is in the corpus TO FAIL — leave it ❌

Its two GObjects cells are the only failures in the regression matrix. That is the entire reason it
was added: it turns "below 4.11 is unsupported" from an assertion into a measurement. Full reasoning
in `Himmel.h`'s corpus block and GROUND-TRUTH.md §"Settled facts".

-----

## Making another one

1. **Check for prebuilt targets first — usually there is nothing to build.** A launcher-installed
   engine ships monolithic `UE4Game*.exe` / `UnrealGame*.exe` **with full PDBs** under
   `Engine/Binaries/Win64`. Surveyed: 4.23 / 4.27 / 5.4 / 5.7 / 5.8 have all three configs,
   4.10 / 4.15 have Shipping + Development. **This is what made 4.10 possible at all** (it needs
   VS2015, which is not installed).
   ⚠ They are content-free engine defaults — fine for engine globals, **useless for the
   gameplay-feature matrix**, which needs a real pawn. Package a project when the version is also a
   gameplay target.
2. **Otherwise package a Blueprint-only ThirdPerson project in all three configs.** No C++ needed,
   which matters: a C++ project on 5.3 dies in UBT (`must be compiled with VS2022 17.4 …
   detected 14.29.30159`) because of how UBT ranks MSVC toolchain families.
   **One template is enough** — see the 4.27 measurement above.
3. **Derive truth without Ghidra**: `py tools/pe/pdb_globals.py <pdb>` → a paste-ready `GS_TRUE=`
   line. Corroborate with `py tools/ghidra/replay_patterns.py` (rule 4 — double-derive).
   Vet an uncertain PDB first with `py tools/pe/pdb_match.py <exe>`.
4. **Import raw**: `analyzeHeadless <projs> <Name> -import <exe> -noanalysis`. The sweep reads only
   raw bytes; Auto Analyze is ~88% of a `.rep` and buys nothing here.
5. **Add the row to `sweep.sh`** with a comment saying what the row is *for*, then run the full
   sweep — it costs 4m38s on the desktop, 12-15 min on the laptop (`GROUND-TRUTH.md` has both, with
   the machine each was measured on), so never use a tag filter to save time.
6. **The engine install is transient.** Package, import, then delete the engine (~100+ GB). What
   stays is ~3 GB of packages + PDBs; mirror it to `X:` like the rest of the corpus.

-----

## Related

* [`tools/ghidra/sweep.sh`](../tools/ghidra/sweep.sh) — the rows and their ground truth (authoritative)
* [`tools/ghidra/GROUND-TRUTH.md`](../tools/ghidra/GROUND-TRUTH.md) — per-version reasoning, derivation recipes
* [corpus-preservation.md](corpus-preservation.md) — what to keep/drop, and recovering a lost binary
* [test-games.md](test-games.md) — the real-game corpus, a different question
