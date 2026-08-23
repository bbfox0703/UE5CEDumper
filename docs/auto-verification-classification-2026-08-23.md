# Auto + Computer-Use verification classification (2026-08-23)

> **What this is.** Every open row in [todo.md](todo.md)'s `## Pending live-game verification`
> register, classified by whether **Auto + Computer Use can close it with no human in the loop** —
> and, for the ones that cannot, what exactly is missing. Produced 2026-08-23 by a six-way parallel
> read of the register plus a synthesis pass, then **spot-verified by hand** (below).
>
> **Why it exists.** The maintainer asked "what else can be verified with Auto + Computer Use?"
> after the DumperTest fixture extension turned four dead rows (AD4 / MG2 / V1a / V8) into
> headless ones in a single day. That made *"which rows would a fixture unlock?"* the question
> worth answering systematically rather than one row at a time.

-----

## ⚠ Read this before trusting any line below

**This is agent-produced analysis.** `working-lessons.md` §2 measures such output at **~52% wrong
before refutation**. What follows was *not* individually refuted. Treat every unmarked claim as a
lead, not a fact — re-derive before acting. The ones marked ✅ below are the ones actually checked.

### ✅ Verified by hand, 2026-08-23

| Claim | Verdict |
|---|---|
| `kDeepWalkMaxTotalElems = 50000` at `Aura.cpp:42` | ✅ exact |
| `DumperTest-Win64-Shipping.exe` already carries `++UE5+Release-5.4` | ✅ **3 UTF-16 occurrences** — the G2 Tier-1 unlock (C4) has a real basis |
| DumperTest ships four cooked maps | ✅ all four names present — but **in the IoStore `.ucas`/`.utoc`, not the `.pak`**; a `.pak`-only scan finds zero and looks like a refutation |
| `[AF16-XREF-2026-08-23]`, `[B29-LIVE-2026-08-23]`, `[A6-SPAWN-DQ7R-2026-08-23]` already closed | ✅ all three are ✅ rows in todo.md |
| todo.md is 18,428 lines; the segments covered 2711–16669 | ✅ — see the method note |

### ⛔ One entry is WRONG — do not follow it

**B8's deferred half (bucket A1).** The table says to launch with
`-ExecCmds="t.IdleWhenNotForeground 1"` and calls it *"dev only — silently dropped in Shipping"*.

Both halves are wrong. The sample sets that CVar **from C++** —
`DumperTestSubsystem.cpp ApplyIdleWhenNotForeground`, `CVar->Set(TEXT("1"), ECVF_SetByCode)` — behind
its own `-DumperTestIdle` switch, which `launch_dumpertest.py --idle` already passes. The Shipping
console restriction does not touch it (only `ProcessUserConsoleInput` refuses cheat cvars), so it
works in **all three flavours**. The analyst never opened `DumperTestSubsystem.cpp`.

⭐ The same file also explains *why* idle mode exists: **"for exactly two checks — B8's deferred
collision restore and Grausam's foreground lock"**. The fixture was built for this row. The
register's "需要：背景時真的會停止 tick 的遊戲…Elliot 背景仍在 tick，測不到" is simply out of date.

### ⛔ CAPABILITY LIMIT, measured 2026-08-23 — Avalonia MENU items are not clickable

Every `B_UI_DRIVE` estimate below assumes the UI can be driven. **Top-level menus cannot.** Measured
on `Tools ▾` and `Export ▾`, four items, by coordinate click and by keyboard:

* clicking the menu **header** opens the popup reliably;
* clicking an **item** dismisses it and runs **nothing**;
* six `Down` presses give **no selection highlight**;
* the UI log shows no trace and `StatusText` never changes — so the command never fires.

⚠ It cost most of a session: two Tools actions looked like broken features when they were
unreachable input. **Never file an Avalonia menu item as defective without first checking the UI log
to prove the command ran.**

⭐ **CE's own menus are Win32 and work fine.** `Table → Add file` opened, accepted a typed path and
worked first try. So a UI-menu-gated row is not necessarily dead — obtain the artefact another way
and use CE's side. `[AA12-STEP3-EMPTY-2026-08-23]` closed exactly that way, using
`scripts/ue5_freeze_helper.lua` (which `UE5DumpUI.csproj:150-152` embeds verbatim, so it is
byte-identical to what the menu would have exported).

**Re-read any B_UI_DRIVE row that depends on a menu before scheduling it.**

### ⚠ Method note — how ~12 already-closed rows got analysed as open

The six segments were partitioned over lines **2711–16669**, derived as "the `## Pending live-game
verification` heading to the next `##`". But todo.md is **18,428 lines**, and closure records for
this work continue past 16669 (`[AF16-XREF]` at 17022, `[B29-LIVE]` at 17910). **Every segment was
blind to them**, so five of six independently spent effort on finished rows.

The bug was in how the run was SET UP, not in the analysts. A register whose closures live outside
the section being read cannot be classified by reading that section. Next time: partition over the
**whole file**, or grep the finding tags first and exclude ✅ rows up front.

-----

# What else can be verified with Auto + Computer Use

## Counts after dedup and correction

| Class | Rows | Note |
|---|---:|---|
| **A_HEADLESS** — pipe / log / rig, no GUI | **31** | runnable today, one DumperTest launch covers most |
| **A\*_STAGED** — headless, but needs a scratch DLL build or a witnessed memory poke | **6** | same technique as `[PEHOOK-3B]` / `[GENAURIP-RECOVERY]`, both already on record |
| **B_UI_DRIVE** — Avalonia UI and/or Cheat Engine | **34** | 5 of these need a `git worktree` build of an old artifact |
| **C_FIXTURE** — ~11 fixture additions unlock **19** rows | **19** | one spawner fixture alone serves **7** |
| **D_ENVIRONMENT** | **15** | 3 of them are the *same* missing game |
| **E_HUMAN** | **3** | genuinely a person |
| **X / already closed** | **~27** | see the correction block below — this is the first thing to act on |

**Honest headline: it is not mostly D/E.** ~65% of what remains is runnable without a human. But a large share of the A list is LOW-tier audit residue (Grausam/Fern/Solitar LOWs), so it is *volume*, not *value* — the value is concentrated in the See-through non-regression, the two "fix landed, never re-measured" re-runs, and the spawner fixture.

---

## ⚠ FIRST: ~12 of the rows I was handed are already closed

The six segment ranges stopped short of `docs/todo.md`'s trailing closure section (**lines 17000–18428**), so several segments' "highest-value" items are spent. Verified by grep just now:

| Segment called it open | Actually | Evidence |
|---|---|---|
| **AF16–AF23 step 2** (segment 1's *"best value-per-line-of-C++"* fixture) | **CLOSED 2026-08-23** — the fixture was *found*, not built, by `tools/verify/af16_xref_fixture.py` inverting `walk_function_props` | todo.md:17022 `[AF16-XREF-2026-08-23]` |
| **W1/W7 usmap** (segment 6's A_HEADLESS "just `dotnet add package CUE4Parse`") | **CLOSED 2026-08-22** — CUE4Parse already did it | todo.md:17307 |
| **Y11 FText param + FIRE path** (segment 1, one C + one B) | **CLOSED 2026-08-23**, build 3319 | todo.md:18253 |
| **U3/U17 step 4** (segment 3's GameplayAbilities fixture) | **CLOSED 2026-08-23** on Elliot, 30 `FGameplayAttributeData` fields, vtable proven from raw bytes | todo.md:17625 |
| **A6 step 5** (segment 4's spawn-probe fixture) | **CLOSED 2026-08-23** on DQ7R | todo.md:17198 |
| **B29 log half + third-party wrapper** (segment 6, two B rows) | **CLOSED 2026-08-23** via ReShade; only step 3 (non-ASCII path) open | todo.md:17910 |
| **DragonSword base anchor + regression watch** (segment 6) | **CLOSED 2026-08-23** | todo.md:18066 |
| **AE2/AE3 step 5** (segment 4's B row) | **CLOSED 2026-08-23**, build 3322 | todo.md:17285 |
| plus V6/U8, `.CT` slots, U16, AE27, AE10 1/2/4, B19, AA12 steps 2+4, B25, V1a, MG2, AD4, V8 | closed — segments flagged these correctly | — |

**Two corrections in the other direction:**

- **AF2 is not closed in full** (segment 5 said it was). todo.md:17767: *step 3* closed; **step 2 (X3, non-ASCII `StrProperty` > 50 bytes) and step 1 (G1, partial-offsets amber banner) are still open**. Segment 5's own CJK fixture is the right answer for step 2.
- **AF16's residual survives**: `Re` values on the found fixture were all single-digit, so **numeric-vs-string sorting on that dialog is still undiscriminated**. `af16_xref_fixture.py --top` can find a ≥10-reference field on DQ7R — that is a 15-minute A_HEADLESS row, not a fixture.

**Cheapest work in the whole queue: a bookkeeping pass** marking those headings closed. Segments 1, 2, 3, 5 and 6 each independently wasted analysis on rows that were already done, which is exactly the drift the DERIVE-NEVER-QUOTE rule exists for.

---

## BUCKET A — runnable headless today (31 rows, grouped by sitting)

### A1. One DumperTest `dev` launch, no UI, no CE — **9 rows** ⭐ cheapest batch left
```
py tools/verify/launch_dumpertest.py dev
py tools/verify/inject.py
```
Then over one `pipe_client.py` session (UI **must** be disconnected — `kMaxPipeInstances=3`, the UI holds 2):

| Row | What | Entry |
|---|---|---|
| DLL LOW **L5** | Welford gap underflow | `py tools/verify/linie_cadence_gap.py` — rig exists |
| DLL LOW **L1** | Solitar worker start/stop race | two PipeClient lanes alternating `set_god_mode` ×few-thousand |
| DLL LOW **L8** | Grausam `GetWindowTextW` under `g_mutex` | spam `get_foreground_lock` on lane A while lane B runs a long `find_instances`; measure lane-A latency |
| DLL LOW **L10** | Grausam teardown | `set_foreground_lock` on → new window state → off → graceful shutdown; grep `init-0.log` |
| DLL LOW **L12** | Fern `str_params` malloc leak | 10k mistyped-string params + psutil private bytes |
| **M4** | Tot latch zombifying a Solide hold | `force_field` → drop socket → reconnect → `get_forced_fields` → poke the field → confirm the worker restores it |
| **M5** | `UE5_Shutdown` worker-join ordering | hold active, `PostMessage(WM_CLOSE)` (not `taskkill /F`), assert sub-second exit + empty `CrashDumps` |
| **B10** correctness half | struct types / enum names / bool masks | `walk_class` on `DumperTestActor`, assert `bFlagA/B/C` masks = 1/2/4 |
| **B8** deferred half | Fly disable while game thread quiet | launch with `-ExecCmds="t.IdleWhenNotForeground 1"` (dev only — silently dropped in Shipping), `fly_set` on → `front_window.py front <other>` → off → grep `walk-0.log` |

### A2. One DumperTest launch, See-through arms — **3 rows**
`tools/verify/seethrough_arms.py` exists and refuses a vacuous pass (fails if `hidden_count` never rises, fails if a named actor's own `bHidden` is unset).

- **M1–M5 step 1 arm (a)** — classified human-only ("someone must move"), but `teleport_relative` is a pipe command and DumperTest **has a player pawn** (`ADumperTestCharacter : ACharacter`, and `DumperTestActor.cpp:290` already reaches it via `GetPlayerPawn`). Drive a ~5 Hz teleport loop on the *same* connection, then post `WM_CLOSE` mid-loop.
- **M1–M5 step 1 arm (b)** — hang the game with `py tools/verify/suspend.py suspend-tid <game thread>` (the X7 technique), **witness the hang** with `IsHungAppWindow` before concluding, then `WM_CLOSE`.
- **M1/M2/M3 restore-set (a)(b)(c)** — the row says "only visible on screen"; that is out of date. `seethrough_get_state` now returns `hidden_actors` (addresses, `dll/src/Fern.cpp:5922`), so `read_mem.py` re-reads each actor's own `bHidden`. Only case (d) resists.

Run the `wevtutil` Application query **before** each arm so the detector is shown able to report something.

### A3. ⭐ One DQ7R launch — the single highest-value A row
**`[SEETHRUNOOP]` / `[SEETHRUTALLY]` non-regression.** The blocker on record is *"Tower of Mask and DQ7R are not runnable on this machine"* — **false since 2026-08-20**, and DQ7R was driven four more times on 2026-08-23 (`[A6-SPAWN-DQ7R-2026-08-23]`). The `ResolveToActor` rewrite has never been shown to still hide on a build where the *old* extraction worked. One `seethrough_arms.py run`. Same sitting also covers **FREEZESCOPE step 6** (B) and the **Solide capped badge** (B) — DQ7R has 1024-CAPPED pools.

### A4. Version/detection sweep — **4 rows**, one launch each
- **G11 step 1** — Solarpunk and TQ2 sit at `versionDetectRev` 1/3/absent; copy `UE5CEDumper.{Machine}.json` aside, launch, `get_pointers`, diff. (Re-read todo.md:11357 first — the 12-title cache sweep may already discharge this.)
- **G11 step 2** — Avowed injection; expect **UE504** and do *not* file it as a defect (`[AVOWEDCACHE-2026-08-22]`).
- **G1 + X3 screening** — one `get_offsets` per title looking for `unmeasured` + `validated:false`. Three sittings have found nothing; run it as cheap disconfirmation and *record the negative*.
- **AB12 screening** — enumerate every process's module count for >1024 before assuming a fixture is needed (module-walk code exists in `octopath_step1_rig.py`).

### A5. Pipe-only reads, minutes each — **8 rows**
`AE10 no_path/invalid branch` · `A6 steps 1+2 re-observation` (`force_field` → `get_forced_fields`, record `held` and `truncated`) · `A3 step 3` corroboration (use Double/NumericAll, **never** Float — LWC) · `U4 step 3` zero-field ScriptStruct survey · `U3/U17 step 5` `f:[` fallback survey · `U6/F3 step 4` level travel (**DumperTest ships four cooked maps** — `ThirdPersonMap`, `StarterMap`, `Minimal_Default`, `Advanced_Lighting`; the register's "no second level" note is wrong) · `Z8 DLL half` (`list_all_functions limit:100` forces the truncated marker; `Fern.cpp:3913` honours it) · `b636` static-native fast-path latency.

### A6. Offline / no game at all — **4 rows**
`AC13 step 4` PipeTransportStats unit test (`dotnet test ui/UE5DumpUI.Tests/…` — **not** `build.ps1 -Target Test`, which overwrites `dist` with the non-trimmed UI) · `[FORCESTATUSCLIP]` sibling `.axaml` sweep · `Lushfoil` proxy-not-loading chase (module list ×3) · `AF16` residual `--top` hunt.

### A7. Two granted-title one-offs
`b648` ES2 half (EVERSPACE 2, appid 1128920) · `PEHOOKONCE step 3` (Lushfoil in **proxy** mode — a self-scanning host makes the window non-existent and the test vacuous).

---

## BUCKET A\* — headless but needs a staged build or a witnessed poke (6 rows)

⭐ **This is the technique the segments most under-used.** Six rows are filed as *"no host exists"* when the missing thing is a **DLL condition**, not a game. This repo has staged exactly that twice already, both with byte-exact reverts.

| Row | Stage what | Oracle (name it before staging) |
|---|---|---|
| **G12 step 2** | force `FindStructByName` → 0 at `Genau.cpp:3370-3371` so `ValidateAndFixOffsets` takes the heuristic fallback | ✅ the validated-Guid path already published ground truth for the same fields (`Grade → EDumperTestGrade::Elite`, the enum's hole at 3..6 makes index-vs-value confusion impossible to land by accident) |
| **G3 steps 3+4** | disable `Genau::FindGEngineSlot`'s AOB (`Genau.cpp:4669`) | ✅ `py tools/verify/g3_rescan_apply.py` asserts exactly one `ValidateAndFixOffsets: Starting` + `apply_rescan: Applied GEngine=` |
| **PEHOOK step 3c** | gate the two `kPePat*Sib*` alternates on a first-call-only counter (`Frieren.cpp:1514/1519`), copying `pehook_3b_refusal.py` | ✅ `Frieren.cpp:1869` "this offset is TRUSTED again" appears or does not. Drive **queued** invokes, not `direct_call` |
| **MB3 mailbox throw** | `#ifdef UE5_TEST_THROW` Cmd in `Mimic.cpp` whose handler throws | ✅ `result == -11` and the next ordinary dispatch still succeeds |
| **AF1** malformed UEnum | `mutate_guard.py` (witnessed read → poke → restore) writes `0x80000000` into `NumValues` | ✅ the read REFUSES vs a negative count. **⭐ Needs UE5.6+** (`Neu.h:85-101`) — DumperTest at 5.4 can never reach it, but **StackOBot 5.8 Shipping is already on disk** (`docs/reference-builds.md:78-84`) |
| **U1** degraded ELEMSIZE | poke a bogus `ElementSize` into a live `FMapProperty` | ✅ `Cannot read map elements for '…'` in `walk-0.log`, no wedge |

⛔ **The counterweight, from this repo's own lesson**: *staging a path makes it run; it does not make it meaningful.* Do **not** stage the Genau-RIP GObjects arm — it has no oracle, which is precisely why it stayed unclosable (the forced fallback accepted pools of 583 and 2,556,928 against a real 25,179).

---

## BUCKET B — UI / Cheat Engine drive (34 rows, grouped)

### B1. ⭐ Two "the fix landed, nobody re-measured" re-runs — do these first
A ❌ whose fix has shipped is an **owed re-run**, not a closed row.

- **AC11 step 2** — `ProxyDeployService.cs:1007` `IsTargetUnreplaceable` now maps `UnauthorizedAccessException` to the locked arm. Launch Elliot (dxgi loaded) → Proxy Deploy → tick → **Force Overwrite** (required; `PlanDeploy` returns `AlreadyCurrent` otherwise) → Deploy. Expect `ErrorLocked`, not `ErrorOther`. Cross-check `tools/verify/ac11_locked_rename.py`.
- **AE20** — `[ORPHANCANCEL]` is pinned only at the VM seam with a stubbed service; the row says *"Not re-run on disk"*. `py tools/verify/ae20_orphans.py` → Find leftovers → tick 4 → Delete → Cancel. Diff the log's `Recycled leftover proxy` count against the summary tally — that mismatch **was** the defect.

### B2. ⭐ Two rows unblocked by a stale premise, no fixture needed
- **X12 (CE autorun denied-write)** — filed "maintainer only, needs elevation", but `MainWindowViewModel.cs:3201` resolves CE's folder from the **running `cheatengine*` process's path**. A user-owned portable CE copy + `icacls … /deny (W)` on its `autorun\` hits `FileWriteFault.IsPlacementDenied` (`FileWriteFault.cs:17`) with **no UAC anywhere**. This trick generalises to any row filed as needing an unwritable install folder.
- **`.CT` DLL discovery, registry/MRU half** — was ⛔ only because `%ProgramFiles%\Cheat Engine\UE5Dumper.dll` answered first. `[STALEDLL]`(a) closed 2026-08-22 (both CE folders swept clean), so the cheap slots now genuinely miss and the fallback runs for the first time. Nobody re-linked the two rows.

### B3. ⭐ One `git worktree` unblocks three rows filed as "no old artifact exists"
`git log -S"MAILBOX_CONTRACT = 3" -- dll/src/Mimic.h` → **2c2a950c**. Its parent builds a *genuine* contract-2 DLL and a *genuine* pre-contract-3 `.CT` — a period artifact, not a reconstruction, so the "testing the reconstruction" objection dissolves.
- **AA2/AA3 step 1** — a current freeze script must refuse the old DLL **before any write**.
- **FREEZESCOPE step 8** — a pre-contract-3 `.CT` must still run against the current DLL. Keep the artifact.
- **AA12/AA13 step 5** — separately, `git show 04d40803^:scripts/ue5_freeze_helper.lua` is version **1.1** (current 1.4, `scripts/ue5_freeze_helper.lua:90`). *"An old version of our own artifact" is never a missing fixture in a git repo.*
- (**B10 perf half** is the same shape: a `git worktree` at pre-build-2596 gives the missing baseline.)

### B4. One AOT publish covers the trimming-specific display rows — **4**
`build.ps1 -Mode Publish -NoBumpBuildNumber` — **background it**, a foreground link exceeds the 120 s tool timeout mid-link.
`PARAMSSORT` AOT header click-through (Live Funcs / Console / Interesting Funcs) · `PARAMSSORT (b)` snapshot cap-notice wrap (`snapshot_cap_fixture.py`; closest thing in this bucket to a judgement — I kept it B because *clipped vs not* is factual) · `SNAPINTERVAL` NumericUpDown live check · `CONTAINERCAP` steps 1-3 (`Set_Big` = 199, expect `⚠ showing 128 of 199` in breadcrumb **and** header; `Set_Int` is the clean control).

### B5. Proxy Deploy sitting — **4**
`AE4–AE7 step 4` (extend `stage_synth.py` / `ae20_orphans.py` to ~1500 trees so the delete window is seconds, then click Scan mid-delete) · `AE4–AE7 step 2` busy indicator (the Satisfactory `DeployedOutdated 1.0.0.2498` row is the only slow one on this machine) · `AF25 step 1 / AC15` drive-scan vs Steam-scan — **no baseline exists, so create one this session rather than claiming the set is unchanged** · `B29 step 3` non-ASCII game path (`D:\測試\DumperTest`).

### B6. Cheat Engine sitting — **9**
`AF25 step 7` teleport "run one" · `L3 step 2 (AD10)` AOBMaker push must still carry the AOB · `Y13` dump-window clamp text (assert on the **emitted text**, no tick needed) · `Y10` baked-invoke untick re-run (read `.Active` from the Lua Engine, never the checkbox icon) · `AA2/AA3 step 5` permanent rescan failure (`read_mem.py` as the second detector — the console line alone does not prove the writes stopped) · `AA4–AA7 step 4` (`dofile` **first**, then kill the game, then `createFromPath` — that puts the raise inside `createFromClass`) · `B5` mailbox flavour (3 CE `createThread`s inside the ~3 s proxy scan window) · `executeCodeEx` negative control (freeze the **game thread** via `suspend.py`, not the process) · `executeCodeEx` 5000 ms budget at 250K objects (OCTOPATH, 273,956 objects, **winmm** proxy).

### B7. Misc UI — **5**
`Dump Explorer` identity gate cases (2) and (3) — case (3)'s "needs an actual DQ7R patch" is wrong: the gate keys on module name + `pe_hash`, so flipping one padding byte in a copied exe manufactures it in a minute · `Solide capped badge` · `B16` Group/Map sort columns · `b637/b644` Return Value diagnostic strings · `G1` amber banner (staged DLL, UI reads it).

---

## BUCKET C — fixtures (11 additions unlock 19 rows)

Ranked by rows-per-package-build.

### ⭐ C1. THE SPAWNER — one addition, **7 rows**
Every remaining "needs objects that appear and disappear" row wants the same thing, and DumperTest's eight existing mutators only *mutate fields* — none creates objects. This is the natural next step in the same pattern.

```cpp
// DumperTestTypes.h / a new header
UCLASS() class ADumperTestHolder : public AActor { GENERATED_BODY()
public:
    UPROPERTY() float  HolderValue = 0.f;   // seeded DISTINCT per instance: 1000 + index
    UPROPERTY() bool   bHolderFlag = false;
    UPROPERTY() int32  HolderIndex = 0;
};
UCLASS() class ADumperTestHolderChild : public ADumperTestHolder { GENERATED_BODY() };  // must be held (derivation)
UCLASS() class ADumperTestHolderDecoy : public AActor { GENERATED_BODY()               // must NOT be held (name only)
    UPROPERTY() float HolderValue = 0.f; };
UCLASS() class UDumperTestLateSpawn  : public UObject { GENERATED_BODY()               // never instantiated at BeginPlay
    UPROPERTY() int32 LateValue = 0; };
UCLASS() class UDumperTestPayloadB   : public UObject { GENERATED_BODY()               // different-but-plausible layout
    UPROPERTY() int32 BValue = 0; UPROPERTY() float BScalar = 0.f; };

// on ADumperTestActor — DECLARED HERE, not inherited (invoke_function cannot
// resolve inherited functions — [INVOKEINHERIT-2026-08-20])
UPROPERTY() TArray<TObjectPtr<AActor>>  SpawnedHolders;
UPROPERTY() TArray<TObjectPtr<UObject>> LateSpawns;     // GC roots

UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") void  Spawn_Holders(int32 Count = 300, bool bChild = false);
UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") void  Spawn_DestroyHolders();   // Destroy() + Empty() + GEngine->ForceGarbageCollection(true)
UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") int32 Spawn_CountHolders() const;
UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") int32 Spawn_Generation() const; // proves churn HAPPENED, not assumed
UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") void  Spawn_LateInstance();     // one UDumperTestLateSpawn
UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") void  Spawn_RecycleChurn(int32 Rounds = 32); // A/B alternation + GC
UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") int64 Spawn_LastRecycledAddr() const;
UFUNCTION(BlueprintCallable, Category="DumperTest|Spawn") void  Spawn_ManyComponents(int32 Count = 1500); // NewObject<UActorComponent>+Register
```

Rows it closes:

| Row | What the spawner supplies |
|---|---|
| **Solide L4** (per-instance restore bases) | 300 instances with **distinct** `HolderValue = 1000+i` — the defect (one shared base restored to all) is undetectable with one instance |
| **Solide L3** (substring vs derivation) | Child **must** be held, Decoy **must not** — no discriminating pair exists anywhere today |
| **Solide `⚠ capped` badge** | 300 > 256 locally and deterministically, instead of hunting a class in a commercial title |
| **AA2/AA3 step 4** (freeze across churn) | spawn A → freeze → destroy → **GC** → spawn B. ⚠ Same-class respawn does *not* test the guard; foreign-class slot reuse is the whole defect |
| **AA12/AA13 step 3** (legitimate empty case must NOT untick) | `UDumperTestLateSpawn` has zero live instances *and no subclasses* — the previous attempt picked `NiagaraComponent`, which had two |
| **U4 class-to-class recycling** | `Spawn_LastRecycledAddr()` hands the harness the reused address instead of making it guess |
| **AE4–AE7 step 4** concurrency gate | `Spawn_Holders(200000)` inflates GObjects so a guarded op runs long enough to collide (today it reads `執行時間太短無法測試`) |
| **FREEZESCOPE step 6** fallback | `Spawn_ManyComponents(1500)` pushes the derived `ActorComponent` pool past 1024 if DQ7R is unavailable |

### C2. CJK strings — **2 rows** (X3/U7 **and** AF2 step 2)
No localized title supplies this and the register now explains *why*: games store display text as `FText`, so their `FString`s are short identifiers. Use `\uXXXX` literals per that header's own rule.
```cpp
UPROPERTY() FString Str_Cjk_Long;      // 24 CJK chars = 72 UTF-8 bytes, past the 50-byte cut
UPROPERTY() FString Str_Cjk_Boundary;  // engineered so byte 50 lands INSIDE a 3-byte sequence
UPROPERTY() FText   Text_Cjk_Long;     // the FText mirror
```
PASS: `…` appears **inside** the quotes (test `"…" in preview`, not `endswith`) and the last glyph is whole, not `U+FFFD`.

### C3. Small, one row each — **7**
| Row | Addition |
|---|---|
| **U3/U17 step 3** (LWC) | `TMap<int32,FVector>` + `TSet<FVector>` seeded at large magnitudes (62010.5…) — existing `Map_IntToVec3f` is 12-byte float and cannot reach the 24-byte double case |
| **U11** | `TOptional<FText> Opt_Text_Set` (CJK) + `Opt_Text_Unset` control — the sample already ships four `TOptional`s and was **one line short** |
| **A9** | `TArray<FDumperTestDeepBucket>` at 500×500 = 250k leaves on ONE object (budget is `kDeepWalkMaxTotalElems = 50000`, `Aura.cpp:42` — confirmed) + a tunable `A9_BuildDeepContainers(Outer,Inner)` |
| **Y15 step 6** | an **unscoped** `UENUM` (int32 underlying ⇒ 4-byte EnumProperty) **plus a non-zero neighbour** `int32 = 0x7F7F7F7F` — on zero neighbours a 4-byte and a 1-byte write are indistinguishable and the probe can only return "pass" |
| **AA14–AA20 step 2** | `void AA16_FillNumbers(TArray<int32>& Out)` / `AA16_FillNames(TArray<FString>& Out)` **declared on `ADumperTestActor`** — both prior attempts died at `0 functions walked` because they targeted inherited functions |
| **U4 step 3** fallback | `USTRUCT() struct FDumperTestEmpty { GENERATED_BODY() };` + a member, if the survey finds no natural 0-field struct |
| **b637/b644** determinism | `FString V637_GetString()` + `UObject* V637_GetPointer()` |

### C4. Build-config only, no C++ — **2 rows**
- **G2, the UE5 Tier-1 branch** ⭐ — recorded as having *no host on this machine*, but `DumperTest-Win64-Shipping.exe` **already carries `++UE5+Release-` in UTF-16**; it only exits early because its PE VERSIONINFO reads 5.4.4.0. Add `ProjectVersion=1.0.0.0` under `[/Script/EngineSettings.GeneralProjectSettings]` in `Config/DefaultGame.ini` (absent today) and package as a **fourth flavour** so the three existing ones keep their Tier-0 behaviour. **Both Product and File version must move** — `Genau.cpp:2749` is a FileVersion fallback. Verify with `tools/verify/tier1_host_survey.py`.
- **AD18 dinput8 arm** — no installed UE exe imports `dinput8`, so deploying the proxy produces *no log line at all* (inconclusive, not a pass). Add `dinput8.lib` to `DumperTest.Build.cs` + one guarded `DirectInput8Create` call so the linker emits the static import; confirm with `tools/pe/pe_imports_exports.py`.

### C5. Needs care — the fixture must not produce a false FAIL
**FREEZESCOPE step 5.** Verified in the installed UE 5.4 source: `AActor::TakeDamage` **never consults `bCanBeDamaged`**, and `UGameplayStatics::ApplyDamage` just forwards to it. The only engine path honouring the flag is the radial overlap filter `OverlapActor->CanBeDamaged()` (`GameplayStatics.cpp:744`). So the mutator must be `FZ_ApplyRadialDamageToPawn(float)` calling **ApplyRadialDamage**, paired with a `PawnDamageEvents` counter as the freeze-off negative control. ⭐ **A cheaper surrogate exists today (A_HEADLESS)** and covers the finding's actual mechanism: with the derived freeze armed, read the player pawn's own `bCanBeDamaged` byte before/after with `read_mem.py` — pre-fix the freeze held only a `ChaosDebugDrawActor` and never touched the pawn.

### C6. Do not promise — **Z8** (>100,000 UFunctions)
~2,000 UCLASSes × 60 UFUNCTIONs. UHT + linker on 120k reflected functions is hours and **may not build**; if it doesn't, this reverts to D. Also note the boundary: the pipe can prove the DLL emits the truncated marker (`Fern.cpp:3913`), but it **cannot make the UI render its string** — `ConsoleViewModel.cs:255` and `InterestingFunctionsViewModel.cs:440` both take `DumpService.cs:2657`'s hardcoded `limit:100000`. Two different claims; only the first is free.

---

## BUCKET D — the honest "no" (15 rows, but only ~8 distinct gaps)

| Missing thing | Rows blocked |
|---|---|
| ⭐ **A UE title whose GObjects/GNames AOB FAILS, or whose first scan leaves a global unresolved** | **B18** (Extra Scan cancel), **V10** (refresh erases the result), **Genau RIP GObjects arm** — *three rows, one sample*. Measured absent across 25 processes / 437 logs. Track them together |
| A build with `WITH_CASE_PRESERVING_NAME` | **U2** + its `TArray<FName>` stride sub-bullet. DumperTest cannot change an engine build flag |
| A title validating offsets only on the **second** scan | **G7** (all nine swept titles validate first pass) |
| ~27M UObjects | **A7** (five measurements; the row itself says stop) |
| The MindsEye licensee fork | **G6** (XOR-obfuscated FNameEntry) |
| A title where `++UE5+Release-` is **absent** but a weaker version string is present | **G11 step 4** (Tier 3), **G2** Tier 2/3. ⚠ A planted decoy loses — Tier 1 scans the whole image first and game C++ cannot strip the engine's own tag. Do not re-propose a fixture here |
| A `privatebuild` Cheat Engine | **AB1/AB2** APC injection (parent item already closed) |
| A **removable** volume mounted into a folder | **AC17 step 8** negative half. ⚠ **I downgraded the positive half too** — segment 1 called it B_UI_DRIVE, but `mountvol` requires elevation, which is outside the unattended envelope; it needs the maintainer |
| Geri / UE 4.18–4.24 / a heavily-forked build | **b648** remainder |
| A Blueprint existing only pre-transition whose package unloads | **AE2/AE3 step 4** — ⚠ **downgraded from B**: the DQ7R run proved the premise is structurally wrong (UE frees *instances*, not UClasses, on travel), so segment 4's four-maps idea does not rescue it |

---

## BUCKET E — genuinely a person (3)

- **NumericAll result volume** — "is a 1-byte scan's flood still usable" is a UX judgement; no assertion settles it.
- **b719 Property freeze Route B** — needs NPCs to die and respawn plus a level transition; the named candidate (Geri) is not granted.
- **AE2/AE3 step 3, second half** — "the spinner must not vanish *early*" requires *seeing* a spinner; on this machine even a 2224-byte class renders before a zero-wait screenshot. A very large fixture class could rescue it, otherwise it is a human watching a load.

---

## If you point me at exactly three things

1. **The bookkeeping pass** on the ~12 stale-closed headings (30 minutes, prevents the next planner repeating five segments' worth of wasted analysis).
2. **One DQ7R launch** — the See-through non-regression (`seethrough_arms.py`), FREEZESCOPE step 6, and the Solide capped badge, all in one sitting behind a blocker that stopped being true three days ago.
3. **The spawner fixture (C1)** — one package build, seven rows, and it is the first DumperTest addition that creates *objects* rather than fields, which several future registers will inherit.
