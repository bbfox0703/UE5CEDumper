# Archived verification-register units — closed 2026-11-20 (build 3776)

Units lifted **byte-identical** out of [`../verification-register.md`](../verification-register.md) on 2026-11-20,
when the maintainer asked for the oversized docs to be checked for archiving. The register went from **1,082,861
bytes / 13,171 lines** to **1,045,688 bytes / 12,792 lines** (pointer blocks included); this file holds the **389
moved lines (38,666 bytes)** of **7 sections**.

⛔ **Kept byte-identical on purpose — do not repair anything in here**, including the relative links, which were
written relative to `docs/` and are therefore wrong from this folder. [`README.md`](README.md) says how to read them.

**How these were selected.** The register's stricter rule of 2026-09-26
(`verification-register-closed-2026-09-26-build-3559.md` states it): on top of the todo rule, a unit stays when its
body records an unexercised leg of its own acceptance, an unexplained observation, an unfiled lead or a standing
warning found nowhere else; **no `⬜` / `🟡` heading moves** (the derived open-batch count is computed from them);
and no blockquoted evidence sub-block is cut out of an open section. One proposer and one adversarial verifier
(workflow `wf_b5b94773-aa0`); the verifier sent back `[A4-508MARKER-2026-09-06]`, whose acceptance table carries a
standing warning (the Pointers panel binds `UeVersion` as a raw int, with no converter) found nowhere else.

**What moved:** `[MCP-WRITE-GRANTS-2026-11-14]`'s holder-labels and Phase 1b invoke-grants rows,
`[PIPE-WALK-DURING-INIT]` / `[DYNOFF-LATCH-STALE]`, `[RELAXED-D-PREALLOC-HALF]`, `[A2-ES2-506-2026-09-05]` and
`[A7-EMPTYBASE-2026-09-05]` from `## Pending live-game verification`, and `[U16-ENUM65-2026-08-22]` from the
migrated 繁中 checklist's step 3.

-----

### ✅ The cards' holder labels (build 3716) — live checks, all six closed 2026-11-17 `[MCP-WRITE-GRANTS-2026-11-14]`

Each gameplay card on the Teleport tab names who other than the window holds its feature, from `get_holds` every 2 s,
and follows that holder's value -- dev-log 3716, docs/mcp-write-approval-eval.md §4.10. Three review rounds; unit
tests (`TeleportViewModelTests` holds poll, `HolderTextTests`, `AiWriteBlockGateTests`) and source pins
(`PipePerProcessSourceTests`) for the DLL's debug-camera ownership.

1. ✅ **CLOSED 2026-11-16** (AOT exe and DLL 3716, DumperTest Shipping pid 35288, the window connected;
    `tools/verify/holder_chips_live.py --seconds 120`, PASS; log `out/holder_chips_live_3716.log`).
    - **DLL side** (the rig's `get_holds`): `god_mode`, `time:global`, `movement:walk_speed` active, holder client
      pid 30864 background; `movement:jump` active, holder client pid 34140 not background.
    - **UI side** (screenshots): God Mode, Move Speed and Whole world read "Held by an AI session (pid 30864)" with
      the badges ON, Move Speed's readout 750 cm/s (base 500 x 1.5) and Whole world's "0.5x (held; natural 1x)" while
      its slider stayed at 100%; Super Jump read "Held by another program (pid 34140)" at 840 cm/s; Gravity, Stealth,
      Keep Foreground and Player pawn had no chip. The chip's tooltip read as en.axaml words it.
    - **The card's Reset takes over:** Super Jump's Reset turned it OFF (700 cm/s), the chip went, and the rig then
      read the jump inactive and nobody's.
    - **The holder leaves:** the background client's exit released its three holds (DLL side: inactive, kind none);
      the window's chips went and the God Mode, Move Speed and Whole world badges read OFF (500 cm/s, 1x).

    *Was:* Acceptance: with the window connected, holds taken by a background client and by another foreground
    process show their labels on the matching cards, the badges follow their values, the card's Reset ends the hold,
    and the labels go once the holders let go -- read on both sides (`get_holds` and the screen).
2. ✅ **CLOSED 2026-11-16, DLL side** (DLL 3716, DumperTest58 Development, UE 5.8, 35,860 objects;
    `tools/verify/dbgcam_ledger_live.py` 13/13; log `out/dbgcam_ledger_live_3716.log`). The AI's invoke of
    ToggleDebugCamera was refused by name, pointing at `write_debug_camera`; the window stand-in's invokes named it on
    and off; after the AI's `write_debug_camera` ON, the window's invokes off and on made the camera the window's, and
    the AI's exit left it on; the mailbox's INVOKE_BY_NAME (written with WriteProcessMemory, no CE) named CE, off and
    on. The UI side is item 1's path (`get_holds` to the chip). **Not run:** a negative control on a pre-3716 DLL
    (none kept); each fix's red step is a unit test or a source pin.
3. ✅ **CLOSED 2026-11-17** (AOT exe and DLL 3719, DumperTest Shipping pid 30528, the window connected;
    `tools/verify/holder_chips_live.py --experimental --seconds 90` PASS, log `out/holder_chips_exp_3719.log`).
    - **DLL side:** the background client (pid 4784) turned See-through on (`claims` 1, `you` true) and took Fly and
      Keep Foreground (if_free): `get_holds` named it, background, for `fly` and `foreground_lock`; after it left,
      both read inactive and nobody's and See-through off with 0 claims.
    - **UI side** (screenshots, switch on): Keep Foreground and Fly read State ON, "Held by an AI session (pid 4784)";
      See-through read State ON, "Held by another program or AI session"; after the client left, the chips were gone
      and the cards read OFF.
    - **Switch off** (experimental.json `enabled` false, its exact bytes restored after): the window, restarted and
      connected, polled `get_holds` alone -- the pipe log's repeat counter read "repeated 49x: get_holds" from
      03:59:19 to 04:00:58, about one per 2 s, with nothing between; switched on, each poll had read get_holds,
      seethrough_get_state, get_foreground_lock, fly_get_state. Two `seethrough_get_state` came in the connect's
      first refresh (03:59:17), before the poll began: once, not per poll.
4. ✅ **CLOSED 2026-11-17** (AOT exe and DLL 3719, DumperTest Shipping pid 25912; driven through the window with
    computer-use, `get_holds` read with a pipe client). The window (pid 1264) turned god mode on with Force ON and was
    closed (WM_CLOSE); `get_holds` still read `god_mode` active, holder client pid 1264, not background (godmode 1).
    The window started again (pid 10044) and connected: its God Mode card read State ON, "Held by a program that has
    exited (pid 1264)". Its Force OFF turned it off and cleared the chip; `get_holds` then read inactive, holder none
    (godmode 0).
5. ✅ **CLOSED 2026-11-17, its premise corrected** (AOT exe and DLL 3719, DumperTest58 Development pid 21556, UE 5.8,
    the window connected on the Debug Camera card; `tools/verify/dbgcam_ledger_live.py --ui-queued --suspend-name
    DumperTest58 --seconds 30` 9/9, log `out/dbgcam_uiqueued_3719.log`; the cards read by screenshot).
    - **A, queued:** game thread frozen; the background client's (pid 14868) `set_debug_camera` ON answered -5 and
      `get_holds` read it pending, the client's; the card read State Queued, "Held by an AI session (pid 14868)"; the
      window sent no `get_debug_camera_state` in the 30 s it was pending (pipe log: 0). Resumed, the queued ON ran:
      camera on, held by the client.
    - **B, owed:** frozen again with the camera on, the client exited: `get_holds` read `release_owed`, kind none; the
      card read State ON, "Turning off: the AI session that held it has ended". Resumed, the owed OFF was sent: camera
      off, the hold nobody's, the card OFF with no chip.
    - **C, dropped:** a second background client's ON queued (-5, pending), then the client exited while still frozen:
      the hold read nobody's at once, nothing owed, and after the resume the camera stayed off -- the DLL drops a
      departed background client's queued invokes first (Fern.cpp, [D2-3-INVOKE]), so that ON never ran.
    - **Premise corrected:** the acceptance had the client exit while its ON was still queued and expected an owed
      release. The first run (log `out/dbgcam_uiqueued_3719_firstpremise.log`, 4 of 5) showed what the DLL does
      instead: case C. An OFF is owed only when the camera is ON as its holder leaves with the game thread stalled.
      *Was:* "Then, still frozen, the client exits: get_holds reads release_owed: true, kind none, and the card reads
      'Turning off: ...'; after the resume the camera turns off and the chip goes." 
6. ✅ **CLOSED 2026-11-17** (DLL 3719, DumperTest58 Development pid 13776, UE 5.8, 35,859 objects;
    `tools/verify/dbgcam_ledger_live.py --busy-lock --suspend-name DumperTest58` 7/7, log
    `out/dbgcam_busylock_3719.log`). The game thread (tid 33700) suspended; a second foreground client's
    `set_debug_camera` ON waiting on it, holding the camera's lock; the mailbox's INVOKE_BY_NAME of ToggleDebugCamera,
    written with WriteProcessMemory (no CE), answered in **5.0 s with a 5 s invoke timeout** (one timeout; waiting for
    the lock would have taken about 9.75 s) with -5 "ProcessEvent returned -5"; the writer answered -5 (queued); the
    pipe log read "Mailbox: ToggleDebugCamera ran unrecorded -- another writer was changing the debug camera"; resumed,
    the clean-up left the camera off. *Was:* ⬜ with this acceptance test.

### ✅ MCP invoke grants, Phase 1b (build 3717) — all three rows closed 2026-11-17 `[MCP-WRITE-GRANTS-2026-11-14]`

One UFunction per grant: `grant_invoke_one_function_any_object_and_args`, then `write_invoke_granted_function` unasked
-- dev-log 3717, docs/mcp-write-approval-eval.md §4.4. Unit tests (`McpGrantsTests`, `McpToolsTests`,
`McpInvokeRulesTests`, `dll_helpers_test` Test_Voll_InvokeParamIndirection / Test_D23) and source pins
(`PipePerProcessSourceTests` for Fern's grant mode).

1. ✅ **CLOSED 2026-11-17** (AOT exe and DLL 3717, DumperTest Shipping pid 31104, UE 5.4; `mcp_client.py invokegrant`
    22/22, log `out/mcp_invokegrant_3717.log`).
    - **MCP side:** the grant marked, the granted invoke listed unmarked and destructive; before the grant, refused naming
      it; `Add_IntInt` granted by full name and called unasked, returning 42, `read_grants` uses 1; grants refused for
      K2_DestroyActor, K2_SetTimer, Delay, K2_ClearTimer, a missing class and a short class name, nothing else granted;
      a granted SetActorHiddenInGame refused on Default__StaticMeshActor ("class default or a template") and run on a
      live StaticMeshActor; one function's grant revoked, its call refused again, the other grant standing.
    - **DLL side** (a background pipe client, no server between): `grant` with another `expect_func_full` refused -28
      with `func_full` naming Add_IntInt; K2_ClearTimer under a grant refused -28 naming FunctionName; Delay per call
      refused -24 "latent".
    - Regressions on the same run: `probe` 29/29, `raw` 28/28, `invokeparams` 26/26, `writes --toggle-experimental`
      39/39, `holdsmore` 78/78.
2. ✅ **CLOSED 2026-11-17** (the server registered from the AOT exe 3727 -- the acceptance names 3717, the build that
    brought the grant; DLL 3727 injected into DumperTest Shipping pid 37752, UE 5.4; a Claude Code desktop session in
    auto mode; the server's log `mcp-20261117-190901-13664.log` under `Logs\UE5DumpUI\`).
    - Asked to call `Add_IntInt` repeatedly, the AI called `grant_invoke_one_function_any_object_and_args` (function
      `Add_IntInt`, class `//Script/Engine/KismetMathLibrary`). The maintainer read the prompt as the tool's name, then
      Function, then Class, then the scope_ack sentence, each whole, and pressed Allow.
    - `write_invoke_granted_function` three times on `Default__KismetMathLibrary`, no prompt (the maintainer's reading),
      returning 5, 42 and 993 for 2+3, 40+2 and -7+1000; `read_grants` uses 3.
    - `write_invoke` of `Subtract_IntInt` on the same object asked; the maintainer denied it, and the server's log never
      names it.
    - The log has one `Grant 1: Invoke for game 37752 (run 1DD7683C3CC1DC5):
      //Script/Engine/KismetMathLibrary.Add_IntInt.` line and one `Grant 1 admitted a call of ...` line per call.
    *Was:* ⬜ **Claude Code's prompt for an invoke grant.** Acceptance: with the server registered from a 3717 exe, the
    AI asked to call one function repeatedly calls `grant_invoke_one_function_any_object_and_args`; the prompt shows
    that name, then Function, then Class, then the scope_ack sentence, each whole; after Allow,
    `write_invoke_granted_function` runs without a prompt in auto mode, `write_invoke` of another function still asks,
    and the server's log has one `Grant N: Invoke ... : <full name>` line. Covered meanwhile by
    `McpToolsTests.TheInvokeGrant_StatesItsScope_WithTheFunctionFirst` (the order) and Phase 0's M1 (whole arguments).
3. ✅ **CLOSED 2026-11-17** (AOT exe and DLL 3719, DumperTest Shipping pid 6244; `mcp_client.py invokegrant` 29/29,
    its step 7, log `out/mcp_invokegrant_3719b.log`). `BlueprintUpdateAnimation`, the anim Blueprint
    `//Game/Characters/Mannequins/Animations/ABP_Manny/ABP_Manny_C`'s override of AnimInstance's native event, was
    granted by its `//Game` full name and run unasked on the live `ABP_Quinn_C` (a Blueprint subclass of it, so the
    call was on a subclass's instance rather than the class's own), result 0. With only the native declaration
    granted (`//Script/Engine/AnimInstance.BlueprintUpdateAnimation`), the server refused the call naming the
    override; asked directly as a background client (`grant`, `expect_func_full` the native name), the DLL resolved
    the override and refused it -28 with `func_full` naming `ABP_Manny_C.BlueprintUpdateAnimation`.

### ✅ The init-window gaps (build 3722) — both rows closed 2026-11-17 — `[PIPE-WALK-DURING-INIT]` and `[DYNOFF-LATCH-STALE]` `[CPP-HARDENING-2026-11-14]`

Two LOW rows of the hardening analysis's item 2, each fixed as the maintainer decided on 2026-11-17 and reviewed in
three rounds (`wf_8f451d97-434`, `wf_b0aa9098-dd5`, `wf_2d9f83d5-21b`); docs/todo.md has the commits. Unit tests:
`Test_Voll_DuringInitNotes` (dll_helpers_test); the LATCHSTALE, OFFSETS, UPROPSLOT and FAMILYEPOCH straddle checks
(dll_core_test); the source pins `PipePerProcessSourceTests.ACommandDuringInit_IsNotedBeforeDispatch_AndNeverRefused`
and `InvokeScriptTests.MailboxInitCheck_ReadsTheFenceLast_AndReChecksAfterUE5Init`.

1. ✅ **CLOSED 2026-11-17** (DLL 3722 as a `dxgi.dll` proxy, DumperTest Shipping pid 628, UE 5.4;
    `initlog_live.py` 6/6). The pipe log has four lines, one per name -- the unknown `initlog_no_such_command`,
    `get_pointers`, `pipe_clients`, and lane A's own `scan_status` polls -- over 48 lane-B commands, every known one
    answered ok and the connection never dropped. The same DLL injected (pid 19744) logged none, as it must: the pipe
    opens after `UE5_Init`. The proxy was removed from the fixture afterwards. **Re-run on 3723** (the round-4 fix,
    pid 40176): 6/6, the same four names once each; proxy removed again.
    In inject mode the pipe opens only after `UE5_Init`, so only a proxy reaches the window. Acceptance: `py tools/verify/initlog_live.py --pid <pid>` on a game whose 3722 proxy has
    not scanned yet passes every check -- at least one `<cmd> arrived during an init or rescan apply` line in the pipe
    log, each name at most once, lane B's connection never dropped, every known command answered ok. A run whose scan
    ends before lane B's first command proves nothing: run it again.
2. ✅ **CLOSED 2026-11-17, as a regression check** (DLL 3722; `latch_regression_live.py` 4/4 injected, pid 19744,
    and 4/4 after row 1's proxy-mode `trigger_scan`, pid 628). `DumperTestActor`'s six StructProperty fields each name
    their struct (ActorTickFunction, RepAttachment, RepMovement, DumperTestAttribute, DumperTestBracketPayload,
    DumperTestNestedBag); no `CorrectSubclassOffsets: delta=` line; one `ValidateAndFixOffsets` run. Same build,
    injected: `mcp_client.py probe` 29/29, `raw` 28/28, `fieldgrant` 44/44. **Not reached:** the failure itself -- no
    fixture mis-derives the family; dll_core_test's LATCHSTALE, OFFSETS, UPROPSLOT and straddle checks hold it.
    **Re-run on 3723** (the seqlock-shaped retire count): 4/4 injected (pid 24120) and 4/4 in proxy mode (pid 40176);
    `probe` 29/29, `raw` 28/28, `fieldgrant` 44/44. The half-retire interleaving is dll_core_test's (a timed thread).
    The failure it fixes -- a family published after a calibration, one
    pointer off -- needs a game whose Genau derivation is wrong, and no fixture has one. Acceptance: on DumperTest
    Shipping with an injected 3722 DLL, the walk log has no more `CorrectSubclassOffsets: delta=` lines than a 3721 run,
    `walk_class` of `DumperTestActor` names a struct type for each of its StructProperty fields, and the same holds after
    row 1's proxy-mode `trigger_scan`.

### ✅ PreAllocatedObjects is not read as Max / Num (build 3725) — closed 2026-11-17 `[RELAXED-D-PREALLOC-HALF]`

`Aura::CountIsHalfOfFirstEntry` now runs for every strict FUObjectArray row and relaxed D, so the check owed was that no
title's own row moved. **Closed 2026-11-17 on DLL 3725**, `relaxed_layout_live.py` 4/4 each, the games closed afterwards:
DumperTest Shipping (UE 5.4, pid 9824) `UE5-Extended`, 24,530 objects, ThirdPersonMap walked; DumperTest58 Shipping
(UE 5.8, pid 42784) `UE5.8`, 34,570 objects, Lvl_ThirdPerson walked; NEKOPALIVE (UE 4.11, the inner `Nekopara.exe`, pid
33752, injected) `Flat-Base`, 25,749 objects, Title walked. The Outer Worlds 2 (relaxed A/C) was checked on 3724; this
build does not touch that row. No title in the corpus carries a preallocated array; dll_core_test holds the misread at
both heap heights.

### ✅ FIXED + LIVE-VERIFIED 2026-09-05 `[A2-ES2-506-2026-09-05]` — audit A2: the 506 row is CORROBORATED at `0x260` on a retail UE 5.6.1 build

✅ **The UI legs, CLOSED 2026-11-20** `[LIVEPASS-2026-11-20]` (DLL and AOT UI 3776; EVERSPACE 2 from Steam, injected --
no proxy is deployed there now -- UE 506, 79,835 objects). Same process: `a2_es2_pehook.py ES2-Win64-Shipping` →
`DetectProcessEvent (pattern): match at vtable+0x260`, Add_IntInt(3,4) = 7, `validation OK — hook fired 840 times in
1500ms`, table 0x260, **PASS**. The UI: header **`Connected — UE506 (79835 objects)`**; the System tab's UE Version
card **506 ✓ Detected**; Live Funcs Start/Stop recorded **2,694 calls of 4 functions** (361 calls/s, a 5,388-record
trace), i.e. the hook active and firing (this build shows the hook through the recording it feeds rather than a
HookActive/fire-count line).

> **CLOSED on the verification PC, build 3371, 2026-09-05 21:00.** Rig:
> [`tools/verify/a2_es2_pehook.py`](../tools/verify/a2_es2_pehook.py) (written for this row; it
> re-runs from one command). Every observable this row asked for was captured in one session, and
> **both absence checks held** — which is the half a play-test cannot produce:
>
> | observable | expected | measured |
> |---|---|---|
> | `scan-0.log` | `DetectVersion: … -> 506` | `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506` |
> | `init-0.log` | pattern match | `DetectProcessEvent (pattern): match at vtable+0x260 -> 0x7FF6E6297080` |
> | `init-0.log` | what was **installed** | `ProcessEvent: offset resolved to vtable+0x260 via the pattern scan (detection run 0/8)` |
> | `init-0.log` | `(fallback)` **absent** | **0 lines** |
> | `init-0.log` | `VALIDATION FAILED` **absent** | **0 lines** — ⚠ vacuous, see below |
> | `init-0.log` | ⭐ **`validation OK — hook fired N times`** | `validation OK — hook fired **660** times in 1500ms` |
> | `init-0.log` | `fired 0 times in` **absent** | **0 lines** |
> | invoke | `Add_IntInt(3,4) == 7` | **7** — a computed value, not a plausible one |
> | diagnostics | hook live | `hook_active=True`, `hook_fire_count=52` |
> | pipe | right binary | `build 3371`, `ue_version 506`, `79,831` objects, `load_mode proxy:version.dll` |
>
> ➕ **ADDENDUM 2026-09-06 — the `508` row now has a live witness too, and it never had one.**
> The rig takes a log-folder name now, because the table has **one row per UE version** and each
> row needs its own witness; every prior live measurement was 506. On **DumperTest58 Development
> (UE 5.8.2)**: `DetectProcessEvent (pattern): match at vtable+0x250`, `offset resolved to
> vtable+0x250 via the pattern scan`, `Add_IntInt(3,4) = 7`, `validation OK — hook fired 184 times
> in 1500ms`, `hook_fire_count` 7,696, no fallback, no `VALIDATION FAILED`. **`0x250` is exactly
> what `case 508: return 0x250` says**, and it is independently corroborated offline: RE-UE4SS's
> newly-shipped `VTableLayout_5_08_Template.ini` puts ProcessEvent at raw index 76, and every 5.0x
> template sits a constant 2 entries (`0x10`) above our table — a calibration that is *measured*,
> not assumed, because ES2 read `0x260` live at 5.6 where the raw index gives `0x270`.
>
> ⚠ **Two rig defects this run exposed, both of which manufactured false FAILs** — recorded because
> each is the same shape as the register's own traps:
> * it demanded a `DetectVersion:` line in `scan-0.log`. `FindAll` **skips detection entirely** when
>   `UE5CEDumper.{Machine}.json` already holds a verdict for that PE hash, so on any previously
>   scanned host there are zero such lines and a `FindAll: UE Version = N (cached…)` line instead.
> * it **raced the validator**. `hook installed … validator armed (1500ms)` is written at install
>   and the verdict lands ~1.5 s later; on a host where listing and invoking is quick, the log read
>   happened first and the rig reported "no validation OK line". It now waits for the verdict.
>
> **Verdict: `0x260`.** The 506 row now has a second independent live witness (Lushfoil was the
> first, the UVTD oracle aside). The title's twice-measured `0x278` stays what it always was — a
> live witness for the **5.5** row, taken on the pre-patch binary — so the table's deliberate
> non-monotonicity is confirmed from both sides by the same game. ⛔ Still do **not** collapse the
> table into a `>=` ladder; that is the exact bug A2 fixed.
>
> ⛔ **CORRECTION TO THIS ROW'S OWN GREP SET — "`VALIDATION FAILED` is absent" PROVES NOTHING on
> the path this row expects.** `Stark::ShouldActOnValidationFailure` returns `offsetFromVersionTable`
> (`dll/src/Stark.h:305-307`), so a **pattern**-derived offset that fires **zero** times takes the
> early return at `Frieren.cpp:1951` and logs a line containing neither `VALIDATION FAILED` nor
> `FAILED` — the hook is deliberately **KEPT**. A mis-detected pattern slot therefore produces
> *exactly* the log the original grep set scores as a pass: no fallback line, no failure line,
> `hook_active == true`. Two consequences, both now in the rig:
>   * the discriminator is the **positive** line `GameThreadDispatch: validation OK — hook fired N
>     times`, which this run has (660 in the 1500 ms window, and `hook_fire_count` 8.8 M once
>     in-world). Absence-of-failure was never the evidence; it only corroborates.
>   * `fired 0 times in` is a **hard fail**, not "the game thread was idle".
>
> The verdict is unaffected — it never rested on the absence checks — but the row as written
> invited a false PASS, and a later session leaning on it would have got one.
>
> ⚠ **The build gate below was real and had to be paid twice.** The deployed `version.dll` was
> dated 2026-08-29 (not 2026-08-27 as written below) and owned the pipe; `proxy_refresh.py` fixed
> that. The run was then thrown away and repeated anyway, because build 3370 could not be read —
> see `[SEINSHARE-2026-09-05]`.

<details><summary>original row (kept — its reasoning is what made the row cheap to close)</summary>

**FIXED 2026-09-05, NEEDED A LIVE CHECK — audit A2: EVERSPACE 2 patched to UE 5.6 and crossed the table's own boundary**

⚠ Deliberately **not** a `###` heading and deliberately carrying no ⬜: `check_derived_counts`
counts `^### .*⬜` inside this section, so leaving either here would keep the row counted as open.

*A2 replaced an unreachable `>= 550` band (every UE5 game silently took `0x220`, wrong by `0x28-0x58`) with a measured per-version table, `DynOff::ProcessEventVTableSlotFor`, `dll/src/Grimoire.h:321-341`. The table is deliberately **non-monotonic**: `case 505: return 0x278` (`:336`) then `case 506: case 507: return 0x260` (`:337`). It is **fallback only** — `Frieren.cpp:1693-1701` runs the pattern scan across up to 12 candidate vtables and reaches `DetectProcessEventVTableOffsetByVersion` (`:1601`) at `:1703` only after all miss.*

*⭐ **The open EVERSPACE 2 question is settled, and settled in a way that helps.** The `0x278` measurement is a **live retail witness for the 5.5 row**, not a contradiction of the 5.6 row: it was taken twice (2026-05-11, `docs/lessons-learned.md:140`; 2026-08-20 `[PEHOOK-6-2026-08-20]`, `docs/verification-register.md:3504`) and bracketed by in-process PE-resource detections of **505** on 2026-08-19 (`:4434`) and **505 (5.5)** on 2026-08-24 (`:9031`) — all of it on the exe that Steam replaced on **2026-09-01 22:05**. The installed binary now reads **`prod=5.6.1.0 → 506`** (`py tools/verify/pe_version_probe.py`, measured 2026-09-05) and the 2026-09-03 session logged it in-process: `scan-20260903-123758.log` `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506`, `init-0.log` `UE5_Init: Complete (UE506, …, Objects=1154897)`. So the maintainer's report is correct — and the title has moved from the `0x278` row to the `0x260` row with no slot ever measured on the new build.*

**Why the suite cannot close it.** `dll_helpers_test.cpp:5607-5672` pins every table row *and* its non-monotonicity — 20 assertions of pure header arithmetic. `DetectProcessEventVTableOffset` is `static` in `Frieren.cpp` and the test target links headers, so nothing exercises whether the value is ever reached, and nothing can say what a real 5.6 licensee build's vtable actually looks like.

**Acceptance test** — **EVERSPACE 2**, `D:\SteamLibrary\steamapps\common\EVERSPACE™ 2\ES2\Binaries\Win64\`. Detection is **lazy on the invoke path**: `RunPeDetection` (`Frieren.cpp:1804`) is reached only from `EnsureProcessEventReady` (`:2025`) and `UE5_EnsureGameThreadHook` (`:2188`), so a connect + scan + walk emits **nothing** — confirmed empirically, `grep -c DetectProcessEvent` over the whole 2026-09-03 ES2 log folder returns **0** in all 18 files. Sequence: refresh the proxy → inject → `trigger_scan` → **one invoke** (or `pe_profile_start`) → grep.

| side | where | expect |
|---|---|---|
| DLL | `init-0.log` | `DetectProcessEvent (pattern): match at vtable+0x260 -> 0x…` (`Frieren.cpp:1589`), and `ProcessEvent: offset resolved to vtable+0x260 via the pattern scan (detection run 0/8)` (`Frieren.cpp:1850` — the only line reporting what was actually **installed**; `primary=` is not necessarily the return value, `:1640-1654` sweeps ±8/±16) |
| DLL | `init-0.log` | **ABSENT**: `DetectProcessEvent (fallback)` (`Frieren.cpp:1623`) and `VALIDATION FAILED` (`Frieren.cpp:1980`). A fallback line means the table quoted itself and the run must be discarded |
| DLL | `scan-0.log` | `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506` (`Genau.cpp:2741`, category `SCAN:Ver` → `Sein.cpp:74`) — the run must be on the 5.6 binary, not a rolled-back one |
| UI | status bar | `Connected — UE506 (…)` (`MainWindowViewModel.cs:734`, `:2763`) and Pointers panel showing **506** (`PointerPanel.axaml:42`, raw int) |
| UI | Live Funcs | `HookActive == true` **and** `HookFireCount > 0` (`Fern.cpp:4097-4098` → `DumpService.cs:2832-2833`) |

**Negative control, and it is what makes this a test rather than a screenshot**: the invoke must return a **computed** value — `Add_IntInt(3,4) = 7`, as `[PEHOOK-6]` did. A wrong slot returns 0/unchanged while `HookActive` can still read true. ⚠ Do **not** use the Self-Test advice strings as a positive: all five (`en.axaml:435-439`) are keyed `str.System.SelfTest.Fail.*` and `PointerPanelViewModel.cs:1739` documents `ClassifySelfTestFailureAsync` as running "only on the failure path" — nothing renders on a pass, so their absence is vacuously true for a UI that never connected.

⛔ **Build gate, and this is the trap `[B648-TWOENGINES]` already hit.** ES2's deployed `version.dll` is dated **2026-08-27** (`init-0.log` line 1: `build: 1.0.0.3367 344d9242-dirty`), i.e. it predates all fourteen commits and **owns the pipe** — a fresh injection does not displace it. Refresh the proxy from a `-Mode Publish` dist and `assert_build()` (`tools/verify/pipe_client.py:11-22`) **before** any number is taken.

**Outcome rule.** `0x260` → the table's 506 row is corroborated on a real retail 5.6 licensee build (Lushfoil and the UVTD oracle are currently its only support). Still `0x278` → the 506 row is wrong **for this title**; record it as a register note, and ⛔ do **not** collapse the table back into a `>=` ladder — the non-monotonicity is the bug A2 fixed.

</details>

-----

### ✅ FIXED + LIVE-VERIFIED 2026-09-05 `[A7-EMPTYBASE-2026-09-05]` — audit A7: all four hops, plus a real-world before/after on **96** engine structs

> **CLOSED on the verification PC, build 3371, DumperTest Development (re-cooked today).** Rig:
> [`tools/verify/a7_emptybase.py`](../tools/verify/a7_emptybase.py) for the DLL/pipe half; the
> emitter half is a diff of two whole SDK exports.
>
> **DLL → pipe (rig, on the new fixture):**
>
> | | `props_size` | `super_props_size` | `own_props_start` | fields |
> |---|---|---|---|---|
> | `DumperTestEmptyBase` (base) | **1** | — | **-1** | **0** |
> | `DumperTestBracketPayload` | 0x10 | **1** | **0** | `Description` @ **offset 0** |
>
> and the live read at offset 0 returns **`'A7EmptyBase'`** — the field is not merely *described*
> at 0, it is *readable* there.
>
> **UI → emitter (the generated header):**
> ```
> struct DumperTestEmptyBase
> {
> }; // Size: 0x0001                                       <- empty, no Pad_0000[0x0001]
>
> struct DumperTestBracketPayload : public DumperTestEmptyBase
> {
>     FString Description; // 0x0000 (0x0010) StrProperty  <- at 0x0000, no leading pad
> }; // Size: 0x0010
> ```
>
> ⭐ **AND THE REAL EVIDENCE IS NOT THE FIXTURE — it is a before/after on stock engine types.** An
> SDK export of the *same* fixture taken **2026-08-20** (pre-fix) still sat in `out/`, so the two
> are directly diffable. Same game, same machine, one emitter change between them:
>
> * **69 empty bases** emitted a spurious `uint8_t Pad_0000[0x0001]`; they are now emitted empty.
> * **27 derived structs had their offset-0 field REPLACED BY PADDING** and now have it back —
>   `TypedElementLabelColumn` `Pad_0001[0x000F]` → `FString Label`, `TypedElementAlertColumn`
>   `Pad_0001[0x0019]` → `FText Message`, `TypedElementLocalTransformColumn` `Pad_0001[0x005F]` →
>   `struct Transform Transform`, `TypedElementSelectionColumn` → `FName SelectionSet`, and 23 more.
>   In every case the **rest** of the layout is byte-identical, which is what shows the fix restored
>   the missing field without disturbing anything around it.
> * `UniversalObjectLocatorEmptyPayload` is among the 69 — i.e. this row's originally-named
>   `FEmptyPayload` family was affected after all.
>
> **Negative control (the W2 guard, the one this fix could plausibly break):** a non-empty super must
> still split at `superPropsSize` and must not re-emit the inherited chain. `struct Subsystem :
> public Object` begins at **`Pad_0028`**, and `Object`'s own `Pad_0000[0x0028]` appears only in
> `Object`. Unchanged between the two exports.
>
> ⛔ **CORRECTION TO MY OWN EARLIER NOTE ON THIS ROW.** It said "the empty base does not exist in any
> live pool… zero structs with `PropertiesSize == 1`". **That was wrong**, and the mistake is worth
> keeping: I harvested struct addresses from `walk_instance` field rows on a handful of walked
> instances, which reaches only structs that are *fields of something I happened to walk*. The SDK
> export walks the **whole pool** and found 92. The fixture is still worth having — it is
> deterministic and does not depend on which engine types a given title happens to load — but it was
> not *necessary*, and "I could not find one" is not "there is none".

> **PARTIAL, `[A7-WIRE-2026-09-05]`, build 3371.** Two of the four hops are settled; the row's own
> "rides along on any live session, no dedicated launch" turned out to be false.
>
> ✅ **Hops 1–2 (DLL emits → pipe carries) are PROVEN, and not on a hand-built model.**
> `own_props_start` is present on every `walk_class` response's class object and is internally
> consistent wherever a super exists — measured across EVERSPACE 2 (1.15 M objects) and DumperTest:
>
> | struct | `super_props_size` | `own_props_start` | `props_size` | reading |
> |---|---|---|---|---|
> | `ActorTickFunction` (super `TickFunction`) | 40 | **-1** | 48 | derived, adds no own properties |
> | `Vector_NetQuantizeNormal` (super `Vector`) | 24 | **-1** | 24 | ditto |
> | `DialogIDs` (ES2, super `DialogIDs_Chunk1`) | 32040 | **32040** | 40896 | the W2 case: split exactly at `superPropsSize` |
>
> ⛔ **Hops 3–4 CANNOT be run: the empty base does not exist in any live pool.** The row names
> `FEmptyPayload` and says it rides along on any session. Measured 2026-09-05: **zero** structs with
> `PropertiesSize == 1` in EVERSPACE 2's 3,808 loaded classes, and none among DumperTest's reachable
> structs either. Two reasons, and both are structural rather than bad luck: `list_classes` returns
> **UClass** objects only, so a `UScriptStruct` is not enumerable that way at all (struct addresses
> have to be harvested from `walk_instance` field rows); and a whole-pool walk only ever sees what
> is **LOADED**, which `FEmptyPayload` (an editor-adjacent AnimData type) is not at runtime.
>
> ▶ **FIXTURE WRITTEN, awaiting a package build.** `FDumperTestEmptyBase` (empty `USTRUCT`) plus
> `FDumperTestBracketPayload : FDumperTestEmptyBase` with a single `FString Description` at offset 0,
> reachable through a new `UPROPERTY() FDumperTestBracketPayload EmptyBasePayload` on the actor and
> initialised to `A7EmptyBase`. `check_ue_sample_values` green. It needs a DumperTest cook/pack
> before it can be walked — **ask the maintainer, and name the configuration**; source edits and
> `Build.bat` are theirs to accept, `RunUAT BuildCookRun` is not something to start unasked.

*UE reports an empty native `USTRUCT`'s `PropertiesSize` as **1**, so the emitter's `Offset >= superPropsSize` split dropped a derived struct's offset-0 field and the trailing-pad pass wrote padding in its place. The floor is now lowered by a new wire field, and the empty base is emitted empty so EBO applies: `Ubel.cpp:1004-1007` captures `OwnPropertiesStart` after the own-chain walk and **before** the super chain is prepended at `:1015-1017`; `Ubel.h:72` defaults it `-1`; `Fern.cpp:2117` sends `own_props_start` from the serialiser shared by `walk_class` and `walk_class_batch`; `DumpService.cs:377` parses it with a load-bearing `?? -1`; `SdkExportService.cs:446` lowers the floor and `:495-496` suppresses the pad.*

**Why the suite cannot close it — and why the obvious observable is illegal here.** The five C# tests (`SdkExportServiceTests.cs:340, 371, 398, 423, 447`) hand-construct `ClassInfoModel { OwnPropertiesStart = 0 / -1 / 0x18 }` and pin the emitter arithmetic in **both** directions. There is **zero** DLL-side coverage — `grep -rn "OwnPropertiesStart|own_props_start" dll/tests/` returns nothing. But the generated header **alone cannot be the paired observable**: `DumpService.cs:377` defaults a missing field to `-1`, so a pre-fix DLL with a correct UI and a correct DLL with a stale UI produce a **byte-identical wrong header**. That is precisely the ambiguity the register charter exists to break, so the DLL leg must be read off the wire.

**Acceptance test** — rides along on any live session; no dedicated launch. Vehicle: `FEmptyPayload` (`vendor/UnrealEngine/.../Animation/AnimData/AnimDataNotifications.h:88`, Engine module, **outside** the file's only `#if WITH_EDITOR` at `:281-296`, byte-identical in the installed UE 5.4 tree) and a child such as `BracketPayload` (`FString Description`) or `AnimationTrackPayload` (`FName Name`).

| side | where | expect |
|---|---|---|
| DLL | `py tools/verify/pipe_client.py walk_class --args '{...}'` | on the derived struct: `own_props_start` **present and == 0**, `super_props_size == 1`, `props_size == 0x10`, `Description` at offset 0. On `EmptyPayload` itself: `props_size == 1`, no fields, `own_props_start == -1`. ⚠ There is **no log line** — `own_props_start` occurs once in `dll/src`, at `Fern.cpp:2117`, and `Fern` logs the request, never the reply body |
| UI | the generated `.h` | `struct BracketPayload : public EmptyPayload`, then `    FString Description; // 0x0000 (0x0010) StrProperty`, then `}; // Size: 0x0010` — **no** leading `Pad_0001[…]`. And the base emitted as `struct EmptyPayload` / `}; // Size: 0x0001` with **no** `Pad_0000[0x0001]` |

⚠ **Names carry no `F` prefix.** `SdkExportService.cs:380-386` appends `classInfo.Name` and `superName` raw, and a `UScriptStruct`'s FName has no `F`. Grep for `EmptyPayload`, never `FEmptyPayload` — the latter has zero hits in the packaged fixture.

**Negative controls, two, both free in the same export run**: (1) **the W2 guard** — a class with a **non-empty** super must still split at `superPropsSize` and must **not** re-emit the inherited chain (`SdkExportService.cs:443-447`; this is the failure the fix could *cause*, and is the load-bearing one); (2) **narrowness** — an opaque field-less struct with `PropertiesSize > 1` must still receive its `Pad_0000[…]`, proving the `emptyBase` suppression (`:495`, `own.Count == 0 && ownStart == 0 && propsSize == 1`) stayed narrow.

✅ **Negative control 2 (narrowness) recorded 2026-11-20** `[LIVEPASS-2026-11-20]` (DumperTest Shipping, UE 504, DLL and
AOT UI 3776): Export → **SDK Header (.h)**, 7,691 types, 3,387,877 bytes. Every base-less type with no member and a
`Size` above 0x0001 -- **175** of them -- carries `uint8_t Pad_0000[0x00NN]` for its whole size (`Object` 0x28,
`AnimDataModelNotifPayload` 0x10, `BranchingPointNotifyPayload` 0x20, `ScriptTypedElementHandle` 0x08, …); **0**
lack it. The positive in the same file: `DumperTestEmptyBase` is emitted empty at `// Size: 0x0001`, and
`DumperTestBracketPayload : public DumperTestEmptyBase` starts its `FString Description` at 0x0000. DLL side, same
process: `walk_class` gives `AnimDataModelNotifPayload` props_size 16 with 0 fields, `DumperTestEmptyBase` props_size
1 with 0 fields. The suppression fires only on the `propsSize == 1` case.

-----

### ✅ U16 —— 大型 enum 的成員清單 — **CLOSED 2026-08-22**，證據見上一節 `[U16-ENUM65-2026-08-22]`

*優先度 **中** · 需要：有 `EPhysicalSurface` 規模（數十個成員）enum 欄位的遊戲*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | ✅ **2026-08-22 在 DQ7R 上通過**：`DefaultPhysicalMaterial` → `SurfaceType`（`EPhysicalSurface`，**65 個成員**）。四個互相獨立的見證都一致：DLL log `read 65 of 65`、匯出的 CE XML 65 行且 index `0..64` 連續、CE 自己在 Lua Engine 裡回報 `n=65 offby0=0 dups=0`（**負控制有開火**：同一圈對 `i+1` 記分得到 `CTRLoffby1=65`）、以及 CE 畫出來的 dropdown 展開到底是 `64 : EPhysicalSurface_MAX`。 | 成員完整，沒有缺尾。<br>⚠⚠ **原本寫在這裡的「DumperTest 的天花板就是 26」是錯的 —— 它其實到 113**（`DumperTest/walk-0.log:212`）。26 從來不是宿主的性質，只是「那一次走訪剛好碰到哪些 class」。<br>▶ 連帶地，原本建議的篩選指令只 grep `walk-0.log`（**只有當次執行**），旁邊還躺著 127 個 `walk-*.log`。要篩就篩整個資料夾，指令見上一節。 |
| 2 | ✅ **2026-08-22 重新在整個 log corpus 上量過**（294 個 walk log、5 個宿主）：**4,919** 次 `ResolveEnumValue`，`N != M` **0 個**，`truncated read` **0 個**，最大的表 **212**（DQ7R）。 | `read N of M` 中 N 等於 M；出現任何 `GetEnumEntries: ... truncated read` 就是真的有問題，要記錄下來。 |

