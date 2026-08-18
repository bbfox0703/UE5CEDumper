# Unattended verification session — handoff and run plan

**Written 2026-08-17 · dist build 3262 · 64 checklist rows classified · A=16 · B=35 · C=12 · D=1**

This is the *operational* companion to [todo.md § Pending live-game verification](todo.md) and
[pending-verification_zh-TW.md](pending-verification_zh-TW.md). Those two own **what** to verify and
**why**; this file owns **how to run the batch unattended** — what is already staged, what may be
launched without a human, and what must never be started without one.

> **The register is canonical.** When an item closes, tick it in `todo.md` and delete its section in
> the zh-TW checklist. Do not record results here.

-----

## ▶ RESUME HERE — Y1 is CLOSED; the register is the work (rewritten 2026-08-18)

**Machine state:** see the end of this block. Working tree clean.

### ✅ CLOSED — do not re-run
Earlier sittings: `AA14–AA20` **5/5** · `AB1/AB2` **5/5** · `AB3/AB5` **1–4** · `G2` **4+5** ·
`X2` **1–3**. This sitting: **`Y1` fully closed** (`[ELLIOT-Y1c-2026-08-18]`), steps 1–4 including
the null control. Results live in `todo.md`; several rows had their *instructions* corrected too.

### ⛔ TWO THINGS THIS SITTING PRODUCED THAT ARE NOT YET FIXED

1. **`[PEHOOKONCE-2026-08-18]`** — a FAILED ProcessEvent *detection* is permanent for the process
   (`offset=-1`; every retry is gated `>= 0` or `== -2`), and the message it prints tells the user to
   do the one thing that cannot work. **Triggered simply by calling `pe_profile_start` before the
   scan** — which is easy to do from a script. Full write-up + fix shape in `todo.md`.
2. The Y1b conclusion *"`paramsData` is cleared by the invoke path"* was **refuted** — see below.

### ▶ THE ORDER THAT MATTERS WHEN STAGING ANY INVOKE ROW

On a proxy-mode DLL the log says *"pipe server only (no scan)"*, so **GObjects is unset until you
scan**. Always:

```
init  →  trigger_scan  →  one invoke (teleport_get_pov)  →  pe_profile_start
```

Doing `pe_profile_start` first poisons the hook for the whole process (finding 1 above). Verified as
a one-variable negative control: same binary, same game — scan-first gave `hook_active: true`,
profiler-first gave `false` permanently. **This, not `MH_ERROR_MEMORY_ALLOC`, was what made Elliot's
hook look "intermittent" in the previous sitting.**

### ▶ WITNESSING AN INVOKE — what is now known to work

* **`paramsData` IS a valid witness** if read immediately after FIRE with **no other mailbox command
  in between**. Nothing in the DLL clears it (game-thread path copies `ownedParams` back at
  `Stark.cpp:430`; timeout path deliberately doesn't copy; static-native and no-hook fallback pass
  the buffer straight through). What *does* wipe it: the **eight** `memset(paramsData, …)` calls in
  other `Mimic.cpp` handlers. The previous "it is cleared" finding is refuted in `todo.md`.
* **The generated script picks its OWN instance**, not the one Live Walker is showing. Read
  `instanceAddr` out of the mailbox header and witness *that* object.
* Rigs, both committed: `tools/verify/mailbox_addr.py` (resolves `g_invokeMailbox` from the injected
  DLL's export table — deliberately independent of CE's `getAddress`, which is part of the path under
  test) and `tools/verify/y1_witness.py`.
* Thread-freezing was **not needed** and is no longer the recommended first approach.

### ⚠ OPERATIONAL TRAPS WORTH MORE THAN THEY COST

* **A big black rectangle over a panel is computer-use MASKING, not a UI bug.** Any window not in
  the allowlist is painted as a solid rectangle in the screenshot, **at its screen position
  regardless of z-order** — so it covers the app you are driving and looks exactly like a render
  failure. It cost several rounds here (restarting the UI, blaming Skia, hunting a stuck popup).
  **The tell: the rectangle does NOT move when you move the window.** Fix: `py tools/verify/
  front_window.py list`, then `front_window.py minimize <proc>` for each — on this machine the usual
  offenders are `notepad++`, `Taskmgr`, `GitHubDesktop`, `RtkUWP`, `Nahimic3`, `XboxPcApp`,
  `SystemSettings`, and `SearchHost` (the Start-menu search overlay, which also steals foreground).
* **Screenshots list what they hid** — the "were open and got hidden" note names the processes.
  Read it before diagnosing anything visual.

### ⚠ MORE TRAPS

* ⛔ **Never run `pipe_client.py` while the UI is connected.** `kMaxPipeInstances = 3` and the UI
  holds **2**, so one rig client fills the pool and the accept loop then logs
  `CreateNamedPipe failed (err=231)` **every second, forever** — 1,826 lines in 31 min, measured
  (`[PIPEBUSY-2026-08-18]`). Close the UI first, or drive the check through the UI instead.
* ⏱ **`list_all_functions` is FAST; a long wait means the RIG, not the DLL.** Measured on Avowed
  (281,501 objects): the DLL logs `EnumerateAllFunctions: 21845 entries from 8780 classes`
  **0.34 s** after the request, and the full round trip is **0.8 s** once the client reads in blocks.
  Before that fix the same call appeared to take **>10 minutes** — the DLL had finished and was
  BLOCKED writing a multi-MB reply into a pipe drained one byte at a time. ⚠ An earlier revision of
  this file recorded "genuinely slow, ~10 min, budget for it"; that was **wrong** and is exactly the
  wrong lesson to carry.

* **A reader that returns 0 on failure is worthless when 0 is also the bug.** A screener missing its
  `ReadProcessMemory` return check reported "does not persist" for a store that had *already
  happened* — the UI was displaying the stored value at that moment. Every `tools/verify/` reader now
  fails loudly.
* **The IME eats typed hex.** Default input on this machine is Chinese: typing `0x…` into a CE form
  yields Han characters plus a candidate window that also swallows `Ctrl+A`/`Ctrl+V`. **`shift`**
  toggles to English; clear with `End` + repeated `BackSpace` (triple-click-to-replace silently
  leaves the old text). Always zoom and confirm the field **before** FIRE.

### Hosts — grants now held for BOTH alternatives

| title | UE | status |
|---|---|---|
| **Elliot** | 5.04 | `dxgi` proxy auto-loads, exe granted. **Known-good invoke host** — hook installs reliably if you scan first. |
| **Solarpunk** | 5.7 | `steam://` **and** `SolarpunkSteam-Win64-Shipping.exe` granted 2026-08-18. Also a candidate for `G2` step 3. |
| **EVERSPACE 2** | 5.5 | `steam://` **and** `ES2-Win64-Shipping.exe` granted 2026-08-18. Save is on a landing pad — may lack live actors. Also a `G2` step 3 candidate. |

⚠ **A bare exe can only be granted while it is RUNNING** (`request_access` resolves installed *or
running* apps). Both were launched for exactly that purpose, so no further dialog is needed for them
this session. `Everspace2.exe` / `Solarpunk.exe` are launcher shims that own no window and do not
resolve — irrelevant, the shipping exe is what matters.
⚠ **`steam://rungameid/...` launches silently fail** via `open_application`; drive the Steam client's
library UI, or start the shipping exe directly (Elliot works this way and skips Steam entirely).

### ▶ WHAT TO PICK UP NEXT
`py tools/check_audit_register.py --list` for the open tier. Named remainders: `AB3/AB5` step 5
(`Vector3f` 12 B beside a 24 B vector) and `X2` steps 4–5 (>5,000 classes **and** game-class exec
commands).

⛔ **`G2` step 3 is BLOCKED-NO-SAMPLE — do not spend a session on it** (`[G2-TIER0-SWEEP-2026-08-18]`).
Its `ascii` and UE5 branches need a UE5 title that falls past Tier 0, and an offline replay of
`Genau::DetectVersionFromPEResource` over **71** UE binaries on this machine found only three that
do — all UE4-era. Every listed candidate (Solarpunk, TQ2, Manor Lords, ES2, STVoyager) is refuted:
they carry a real `5.x` `ProductVersion`, exactly like Lushfoil. **Screen any future candidate with
`py tools/verify/pe_version_probe.py --scan <dir>` BEFORE installing or launching it** — which tier a
title reaches is decided by its version RESOURCE, not by the engine that built it.

### ▶ STEP 1 — GRANTS, IN BATCHES OF ≤7. Do this before anything else.

⛔ **A single `request_access` listing many apps CANNOT BE ACCEPTED**: the dialog grows taller than
the screen and the **Accept button is unreachable**, so it comes back `user_denied` for the whole
set — which is *not* a refusal, just an unclickable dialog. **Grants also never survive a session**,
so this is the first action of every new session, not a fallback for when something fails.

**One batch of 7 covers everything the next two rows need:**

| # | app (Start-menu name) | why |
|---|---|---|
| 1 | `UE5DumpUI` | the UI under test |
| 2 | `Cheat Engine (64-bit)` | AA14–AA20 is the CE Lua invoke path |
| 3 | `Steam` | launcher for the two titles below |
| 4 | `冒險家艾略特的千年奇譚` (Elliot) | **PE hook verified good** — the invoke host |
| 5 | `Lushfoil Photography Sim` | second verified invoke host; also the Tier-1 title for G2 step 3 |
| 6 | `DumperTest Development` | fallback / G10 step 2 later |
| 7 | `DumperTest Shipping` | fallback |

Add more only in a **second** batch, never by extending this one.

### ▶ STEP 2 — the work: `AA14–AA20`, then `G2` steps 3/4/5

**`AA14–AA20` — CE Lua invoke in a real game.** Host: **Elliot** (`Add_IntInt(3,4) = 7 → PE hook
verified`) or Lushfoil. ⚠ **Step 5 needs the GAME THREAD ONLY suspended** — CE's whole-process pause
hits the status-0 branch, not the 0xFF branch under test. Related and now proven: a whole-process
`NtSuspendProcess` does **not** stop `executeCodeEx` either, because it runs on a *newly created*
remote thread.

**`G2` — steps 1 and 2 are ✅ CLOSED; only 3, 4, 5 remain.**
* **3** (regression) — any ordinary UE5 title: `scan-0.log` still shows
  `DetectVersion: Tier 1 (ascii|utf16) '++UEx+Release-N.N' -> NNN`. Lushfoil is a Tier-1 title.
* **4** — **proxy mode**, start a scan from the UI, close the UI mid-scan, and expect one of
  `DataScanGObjectsCandidates` / `FindGObjectsStaticStruct` / `FindGNamesByStringRef` to log
  `aborted (client gone / shutdown)`.
  ⚠ **Do not assume this behaves like the AOB cancel.** It was measured on 2026-08-18 that killing
  the UI mid-scan does **not** cancel the *AOB* scan (`trigger_scan` is async, so nothing is
  in-flight). These are **different** cancel points on the recovery/data-scan path, so they may well
  fire — but that is the question, not the assumption.
* **5** (regression) — disconnect the UI mid-command, reconnect, and confirm a fresh scan resolves
  GObjects/GNames with **no** `aborted` line.

### ⛔ STEP 3 — kill everything when a row is done

**One game at a time, sequential, never parallel**, and **kill the game, CE and the UI as soon as the
row that needed them is finished** — do not leave a title running "for the next row". Confirm the
process is actually gone before launching anything else; a Steam title can leave its shipping exe
alive after the window closes.

### 📁 `out/` is the cross-session scratch area on THIS PC

`out/` is gitignored (`.gitignore:33` — `[Oo]ut/`), so it is the place to hand intermediate data
between sessions on the same machine: captured logs, a half-finished measurement, a note to the next
session. ⚠ **It is temporary by definition and may be wiped at any time — never put anything there
that is not reproducible.** Anything that must survive goes in `todo.md` (results), this file (run
state), or `tools/verify/` (rigs).

### ⭐ The staging lesson that unlocked three long-open rows — read before any mid-scan/mid-command row

**A short window cannot be hit by two consecutive operator actions.** Each tool round trip costs
~10 s of wall clock and a GUI click can silently fail to register, so an 8 s scan is unreachable by
"switch window, then click". Four routes were measured and none work: untick the CE record (the
`.CT` blocks **CE's GUI thread** for the whole scan, so the click queues), kill the UI (async
`trigger_scan`), a fixed CE Lua timer (cannot be aimed), and a CE Lua thread polling the log
(**CE Lua's `io.open` cannot read our live log** — writer share mode).

▶ **What works: a two-process chain with BOTH halves pre-armed.**
`py tools/verify/kill_on_marker.py <log> "<marker>" --touch <flagfile> --after-ms N` watches the log
(Python *can* read it) and drops a flag file; a **pre-armed** CE `createThread` polls that flag and
fires `executeCodeEx(...)`. Neither half is on the operator's critical path. Give the CE loop a
**generous** timeout — one mis-registered click cost 3 minutes and expired a 120 s loop — and
**restart CE between attempts**: it keeps one Lua state, and an abandoned thread later shut down a
*fresh* game.

-----

### ✅ GROUP 5 DONE · GROUP 6 MOSTLY DONE (2026-08-18). ▶ NEXT: `AA14–AA20`, `G2`, `Y1`

**GROUP 6 closed, all on Elliot:**
* **`B4` ✅** (open since build 2592) — arming line captured. Vehicle: a **single synchronous**
  `begin_value_scan`, kill fired **from the log**. ⛔ **`trigger_scan` is ASYNC and cannot arm the
  latch** — a third trap beside Dump All and Snapshot, and the one that looks ideal.
* **`B5` ✅** — the concurrent-`UE5_Init` handshake. Needs **three `createThread` calls at once**.
* **`MA1` ✅** — the AOB cancel guard **and all three of its guards**, plus the healthy-scan
  regression. Cold scan 8.0 s vs 3.3 s warm.
* **`executeCodeEx` 5000 ms ✅** — 844 ms for a full `Actor` dissect at 85 K objects.
  ⛔ Its negative control is **refuted**: a whole-process suspend does not stop `executeCodeEx`.
* **`G10` steps 1 & 3** were already closed 2026-08-16; **step 2 is NOT decidable on Elliot** (its
  only many-match pattern validates, so the one stageable `Hint MISS` has a true count of 1 —
  confounded). Use **DumperTest**.

### ⭐ The staging lesson this batch paid for — read before any mid-scan/mid-command row

**An 8 s window cannot be hit by two consecutive operator actions**: each tool round trip costs
~10 s of wall clock, and a GUI click can silently fail to register. Four routes were measured and
none work — untick the CE record (the `.CT` blocks CE's **GUI thread** for the whole scan, so the
click queues), kill the UI (async `trigger_scan`, nothing in flight), a fixed CE Lua timer (can't be
aimed), and a CE Lua thread polling the log (**CE Lua's `io.open` cannot read our live log**).

▶ **What works is a two-process chain with BOTH halves pre-armed:**
`py tools/verify/kill_on_marker.py <log> "<marker>" --touch <flagfile> --after-ms N` watches the log
(Python *can* read it) and drops a flag; a **pre-armed** CE `createThread` polls that flag and fires
`executeCodeEx(...)`. Give the CE loop a generous timeout — one mis-registered click cost 3 minutes
and expired a 120 s loop. Restart CE between attempts: **it keeps one Lua state, and an abandoned
thread later shut down a *fresh* game.**

**Still to run in GROUP 6:** `AA14–AA20` (Elliot's PE hook is verified good), `G2` speed-split,
`Y1`, `ST1` *(human-gated)*, and `G10` step 2 **on DumperTest**.

ℹ️ **Epic Games Launcher was closed** mid-session — it repeatedly stole foreground and made
computer-use refuse every click. Reopen it freely.

---

### ✅ GROUP 5 IS DONE (2026-08-18)

**Closed this sitting:** `M1/M3/A2/U1/V1` + `U3` steps 1–2 · `AA1` · `Y15` (step 6 skipped, no
4-byte enum) · `U4/U6/F3` · `executeCodeEx` basic path (all 3 steps) · `AA7` step 4 → **AA4–AA7 is
now 5-of-5** · `B26` · `Fern::Stop` graceful path (open since build 2813) · the `.CT` breadcrumb
discovery half. `M2`/`U16` are PARTIAL and say why on their rows.

**Two rows moved to a bigger title, with measurements rather than impressions:**
* **`B4`** — DumperTest cannot produce a command that blocks for seconds. The two heaviest whole-pool
  value scans measured **113 ms** and **52 ms** against `MonitorLoop`'s 200 ms poll. Run it on Elliot.
* **`Y1`** — needs a game-thread invoke, and DumperTest's PE hook never fires
  (`[PEHOOK-2026-08-17]`). Run it on a title with a verified hook.

**Still open from GROUP 5, needing a specific condition:** `B18` step 3 (a title whose GObjects is
*not* AOB-resolvable) · the `.CT` **registry/recent-files** slot (a cheaper slot answered — see
`[STALEDLL-2026-08-18]`) · the `TSet<FName>` / `TSet<UObject*>` / `UDataTable` regression (DumperTest
ships none of the three).

**Five findings filed this sitting** — `[FREEZESTUCK-2026-08-18]`, `[CONTAINERCAP-2026-08-18]`,
`[FREEZESCOPE-2026-08-18]`, `[SLOTSYM-2026-08-18]`, `[STALEDLL-2026-08-18]`. Full write-ups in
todo.md.

⚠ **`C:\Program Files\Cheat Engine\UE5Dumper.dll` is a stale February build** (536 KB vs dist's
2.86 MB). It did not load this session, but it *will* on a clean game with no `dll-path.txt`.
Maintainer's call to delete it.

### Operational facts learned the hard way this session — they cost round trips

* **Host choice matters now.** `DumperTest`'s **ProcessEvent hook never fires**
  (`[PEHOOK-2026-08-17]`), so **anything needing a game-thread invoke must run on another title**.
  **Lushfoil is verified good** (`✓ PE hook verified`). Use it as the invoke host.
* ⚠ **Read CE's checkbox correctly** (maintainer, 2026-08-18): a **big red ✗ = the record is ACTIVE**.
  Inactive is an *empty* box. It is not a failure marker, so never infer one from it.
* **A failed record therefore looks like a working one.** Open **CE → Lua Engine** *before* concluding
  anything about a record — the hygiene rules keep the message out of any dialog. And CE must be
  **attached to the live game**; a symbol registered against a killed process gives
  *"contract symbol resolved to the wrong memory"*.
* **Freeze needs its helper**: UI → **Tools → Inject Freeze Helper into Current CE Table** (verify via
  CE's `Table` menu listing `ue5_freeze_helper.lua`).
* **UI window handling**: it starts un-maximized and mostly off-screen left — click maximize at
  ~(298, 63) first. **Collapsing/expanding the left Object Tree shifts every tab x-coordinate by
  ~290 px**, so re-screenshot after toggling it. Killing the game leaves the UI `Disconnected`; press
  **Connect**, and press **Reload** on the Object Tree or it keeps the previous game's tree.
* **The tree's Class/Struct list needs discrete `Down` presses** — held-key repeat does not reach it.
* Scratchpad helpers built this session (rebuild from these descriptions if the folder is gone):
  `front_window.py` (force-front by process name, and it must NOT `SW_RESTORE` a maximized window),
  `suspend.py` (NtSuspendProcess/Resume — how V7's dead refresh is forced), `sweep_title.py`
  (per-title pointers + CPN + version-log evidence over the pipe), `stage_synth.py` (the synthetic
  game/orphan folders for AC1 and AE4 step 4), `cold_detect.py` (drop one game's cache entry to force
  a cold re-detect), `tier_triage.py` (which titles enter the memory tier ladder),
  `pe_hook_survey.py` (which titles validated their PE hook).

-----

## 0. Hard operating rules — these bound every group below

1. ⛔ **ONE GAME AT A TIME. Sequential only, never parallel.** This PC over-loads otherwise
   (maintainer, 2026-08-17). DumperTest counts as a game. A long `build.ps1` or a full scan running
   *alongside* a game is the same violation.
2. ⛔ **Kill the game process as soon as the group that needed it is done.** Every group below ends
   with an explicit kill step. Do not leave a title running "in case a later group wants it" — a
   later group re-launches it.
3. **Never launch a second title before the first is confirmed dead.** Poll for process exit; do not
   assume a close request worked. A Steam title can leave the shipping exe alive after the window
   closes.
4. **A "not tested" is a legitimate, required result.** Three rows are *expected* to end that way
   (U1 step 4, B18, G3). Recording one as PASS is the failure mode this whole register exists to
   avoid.
5. **Record every number with its conditions — including absences** (working-lessons §1.6).

-----

## 1. Already staged (done 2026-08-17, do not repeat)

| Thing | State |
|---|---|
| `dist/UE5DumpUI.exe` | **57.1 MB Native AOT** — verified by PE: no `hostfxr`/`hostpolicy` import, CLR data-directory 14 = 0, imports are OS + CRT only. (The previous 107 MB self-contained build was **not** trimmed.) |
| `dist/UE5Dumper.dll` + 4 proxies | Rebuilt in the same `-Mode Publish` run |
| `dist/build_number.txt` | **3262** |
| repo `build_number.txt` | **3261** — reverted on purpose (verification-only publish). ⚠ **Compare a DLL's reported `build_number` against 3262**, not against the repo file. |
| DumperTest packages | `D:\UE_Analyze_Data\For Testing\DumperTest\{Development,Shipping}`, built 2026-08-14. **Current — no repackage needed** (see §2). |
| Start-menu entries | 7 Steam `.url` created + `DumperTest Development` / `DumperTest Shipping` as real `.lnk` |

**Future builds during a verification session must pass `-NoBumpBuildNumber`.** `build.ps1`
increments `build_number.txt` on **every** invocation, not only `-Mode Publish`
([build.ps1:381](../build.ps1)).

### The Start-menu entries — what is MEASURED, and what is still unknown

⚠ **Do not read a mechanism into this section. Two candidate explanations both survive, and the
useful part is the observation table, not a theory.**

| Case | Steam-installed? | Start-menu entry? | `HKCU\…\Uninstall`? | Running? | `request_access` |
|---|---|---|---|---|---|
| The 7 Steam titles given a `.url` this session (TQ2, Solarpunk, Light Maze, STVoyager, DSA, OCTOPATH, ES2) | yes | created **mid-session** | yes (Steam's) | no | ✅ resolves |
| The 9 Steam titles that already had one (Elliot, DQ7R, 滿意工廠, …) | yes | pre-existing | yes | no | ✅ resolves |
| **Hollow Knight: Silksong** — Steam-installed, never touched by this session | yes | **no** | yes | no | ❌ |
| **UE5DumpUI** — as `.lnk`, as `.url` with a `file:` URL, under its `ProductName`, while **running**, and then **with** a per-user Uninstall entry written | no | yes | **yes (added, made no difference)** | tried both | ❌ all five |
| **DumperTest** ×2 — real `.lnk` to the inner exe, plus a per-user Uninstall entry | no | yes | **yes (made no difference)** | no | ❌ |

**What is settled:** a Start-menu entry alone is not sufficient (Silksong has none and fails; our
exes have one and fail), and **a per-user `HKCU\…\Uninstall` registration does not help** — that was
tried on 2026-08-17 and refused identically. Steam titles resolve; our loose exes do not.

### ✅ SETTLED 2026-08-17 (late) — the timing/snapshot theory is DEAD. Stop testing shortcut formats.

The one-call experiment below was run in a new session. **`request_access(["UE5DumpUI"])` returned
`notInstalled` with an empty `didYouMean`, and the dialog was never shown to the user** — the request
short-circuits at name resolution, so this is not a refusal and never reaches the grant layer at all.

What makes it decisive rather than one more failed attempt is the **timing**, checked instead of
assumed:

| Artifact | Created |
|---|---|
| `…\Start Menu\Programs\UE5DumpUI.lnk` | **2026-08-16 22:56** |
| every `claude` process (13 of them, incl. the MCP host) | **2026-08-17 13:49** (two helpers later, 20:12 / 22:10) |

The `.lnk` predates every live process by ~15 hours. **Any** snapshot — taken at MCP-server startup,
at app launch, or per call — would therefore already contain it, and it still does not resolve. So
the failure is **structural in the resolver, not a matter of when the entry was created**, and the
sixth format would tell us nothing the first five did not.

### ✅ SOLVED, same session — it was the WRONG START-MENU FOLDER all along

> **⛔ Read this before the sub-section below it.** The probe table further down concluded "the index
> takes no new `.lnk` at all". **That conclusion was wrong**, and it is kept only because the
> measurements in it are still valid and the mistake is instructive: every probe, and all five earlier
> attempts, wrote to the **per-user** `%APPDATA%\Microsoft\Windows\Start Menu\Programs`. That folder
> is not enumerated on this machine. The **all-users** folder is.

**What works, verified end to end on 2026-08-17:**

1. Create the shortcut in **`C:\ProgramData\Microsoft\Windows\Start Menu\Programs`** (a subfolder is
   fine — `…\Programs\Test\` is where these live). Needs elevation, so the **maintainer** creates it
   via Explorer → right-click → New → Shortcut; computer-use cannot drive the UAC prompt.
2. Name it **exactly** what `request_access` will ask for. Target the **inner** exe.
3. Confirm Windows took it — free, no MCP call:
   `Get-StartApps | Where-Object Name -match 'UE5'`. The count rises by one and the `AppID` column
   shows the target path.
4. `request_access(["UE5DumpUI"])` → **granted, `tier: full`, in the same session, with no Claude
   Desktop restart.**

**Two things this settles for good:**

* **The resolver queries live.** Mid-session creation *does* work. Every "snapshotted at startup"
  theory in this file's history is dead — the entry that failed all morning succeeded within a minute
  of landing in the right folder, in the same session.
* **Groups 3, 4 and 5 are unblocked** (20+ rows, including W2/W3 step 5 and D2 step 4, the two
  stranded half-items).

⚠ **There is a lag on the resolver's side — a first failure is not a verdict.**
`DumperTest Development` / `DumperTest Shipping` returned `notInstalled` on the first attempt, minutes
after their shortcuts were created and while `Get-StartApps` already listed both (index total
255 → 258). **Retried ~10 minutes later with nothing else changed: both granted at full tier.**
`UE5DumpUI` happened to resolve immediately, so the two cases together bracket it. **So: after
creating a shortcut, if `Get-StartApps` shows it but `request_access` says `notInstalled`, wait and
retry rather than changing anything.** Do not start editing names or paths on the strength of one
refusal — that is how the original five-format wild goose chase began.

### The superseded probe work — measurements good, conclusion wrong

The resolver's index is **`Get-StartApps` / AppsFolder** — 255 entries against the tool's own
truncated list of ~254, and `Titan Quest II` is present in the former while absent from the latter's
visible range. So the question "why is our exe not grantable" reduces to "why is our `.lnk` not in
AppsFolder", which is answerable locally with no MCP call at all.

**Measured, with the confound removed.** A first probe copied an already-indexed `.lnk`
(`Ollama.lnk`) under a new name and did not appear — but that proved nothing, because AppsFolder
keys legacy shortcuts by **target** (the `AppID` column *is* the target path), so a second shortcut
to the same exe is deduplicated. The clean re-run used three **unique** targets, written through
`IShellLinkW` + `IPersistFile` with WorkingDirectory/Description/Icon filled in:

| probe | target | `.lnk` size | indexed? |
|---|---|---|---|
| `ZZProbeC` | `C:\Windows\System32\where.exe` | 1880 B | ❌ |
| `ZZProbeD` | `D:\…\dist\UE5DumpUI.exe` | 949 B | ❌ |
| `ZZProbeD2` | `D:\…\dist\UE5Dumper.dll` | 951 B | ❌ |

All three returned `S_OK`; the total stayed **exactly 255** across all of it, **including after the
maintainer restarted `explorer.exe` from Task Manager**. Both drives are `DriveType 3` (local fixed,
NTFS), so the drive is not the discriminator either.

The conclusion drawn at the time — *"the app index enumerates no new `.lnk` at all"* — **was wrong**.
Every row above wrote to the **per-user** Programs folder, so the table measures one cell three times.
It reads as a general law and is actually a property of that one directory.

**The lesson worth keeping is the method failure, not the result.** Three probes varying target,
drive and file completeness all held the *directory* fixed, and the fixed variable was the answer.
Two earlier claims in this same investigation failed the same way: the first probe duplicated an
indexed shortcut's **target** (AppsFolder dedupes on target, so its absence meant nothing), and the
"snapshot at startup" theory rested on **alphabetical position in a truncated list**. Three
confounded probes in one sitting is the signal to go and enumerate what actually differs between a
working case and a broken one — `Ollama.lnk` was sitting in the working list the whole time, and the
one attribute nobody had compared was which of the two Start-menu roots it lived in.

*Cleanup state: the three `ZZProbe*` shortcuts were deleted. The per-user `UE5DumpUI.lnk` /
`UE5DumpUI.url` / `DumperTest *.lnk` and the two per-user Uninstall keys are now redundant — the
all-users entries are what work — and can be reverted with
`py tools/verify/register_apps.py --revert --apply`.*

⚠ **And do not build any further argument on the `<installed-apps>` list's contents.** It is
truncated (`… and 34 more`) *and* it omits names that the resolver demonstrably grants — `Titan
Quest II` was granted at full tier on 2026-08-17 yet does not appear in the list's own visible
`Thunderbird → Tools for Desktop Apps` window. The list is a display, not the resolver's index;
alphabetical-position reasoning over it (in either direction) is worthless.

**Consequence either way: `UE5DumpUI` and `DumperTest` are not grantable in the session that created
their entries.** This costs nothing for Groups 0/1/2 — those drive the DLL over the pipe and read
logs, with **no grant involved at any point**. It costs the on-screen work: Groups 3/4/5, W2/W3
step 5, D2 step 4, and the two HUD reads (D2 樣本心跳, B8).

*The registrations were left in place so the next-session test is possible. They are visible in
Settings → Apps → Installed apps, do nothing else, and each carries
`Comment = UE5CEDumper-verification-registration` so a revert can find exactly them.*

The two `DumperTest` entries point at the **inner** executable
(`…\DumperTest\Binaries\Win64\DumperTest.exe` / `DumperTest-Win64-Shipping.exe`), **not** the
154 KB launcher shim beside `Windows\`. The grant is enforced against the *foreground process*, and
the window belongs to the inner exe; a shortcut aimed at the shim would resolve to a name that never
owns a window.

> ⚠ **UNVERIFIED, and the first action of the next session must settle it.** Steam titles are `.url`
> and that format is known to be enumerated (`Steam Support Center` is a `.url` and appears in the
> installed-app list). The two DumperTest `.lnk` files have **not** been confirmed to resolve.
> **First action: one `request_access` with the whole list in §3, then report which names resolved
> and which did not.** If DumperTest does not resolve, its pipe-driven work (Groups 1, 2) is
> unaffected — only the on-screen HUD reads (D2 樣本心跳, B8) lose their route.

The entries were made by two throwaway Python helpers (ctypes → `IShellLinkW`, no PowerShell); the
`.url` writer supports `--revert`.

-----

## 2. Does DumperTest need modifying? **No — start against the packages already on disk.**

Verified by binary probe of both packaged exes, not inferred from a changelog: all five audit-#5
properties (`Map_I64ToI32`, `Map_StrToInt`, `Map_IntToVec3f`, `Set_Big`, `Set_Struct`) are present
as **narrow** strings, i.e. genuinely reflected, while `RawInt`/`RawFloat`/`RawDouble`(`_Ticking`)
are narrow=0 / utf16=1 — printed by the HUD, **not** reflected, so the Native-C hole test is intact.

`capture_package_identity.py --check` reports `package DRIFT`. That is **bookkeeping, not
staleness**: the packages were rebuilt 2026-08-14 and `package-identity.json` was last written
2026-08-12. Re-record it (drop `--check`) rather than repackaging.

**Four rows want a property the sample lacks. None blocks any other row. All four are DEFER.**

| Wanted | Unlocks | Call |
|---|---|---|
| A **churn container** — `TArray<FDumperTestStat> Arr_Churn` + `TMap<int32,FDumperTestStat> Map_Churn` + `UFUNCTION`s to grow / remove-at / reserve | **A12** steps 2-4 · **A11** steps 2-5 · **V1a** step 1 · makes M1/M2/M3/A2/U1/V1/V2 step 5 literal | **The only high-value one.** It converts three C_HYBRID rows to fully automatable. Every container today is filled once in `BeginPlay` and never resized. Worth one ~20-min packaging cycle **if this batch will recur** — not before the first run. |
| `UPROPERTY() FName Name_Slot = FName(TEXT("Slot_7"))` | **U8** only (1 of 4 sub-checks in A5/V6/AE9/U8) | Defer. The sample's only FName UPROPERTYs carry no `Number` component. Huntable in a commercial title instead. |
| `UPROPERTY() FVector3f Vec3f_Known` | **AB3/AB5** step 5 only | Defer, probably unnecessary — the exe already carries `Vector3f` 90× narrow. Probe with `search_properties` first. |
| Three `UFUNCTION`s (a `TArray<AActor*>&` out-param, an `FText` param, a hardcoded `-1` return) | would make **AA14–AA20** a repeatable fixture | Defer. That batch runs on Elliot. The sample has **zero** `UFUNCTION`s today. |

**Impossible from the sample, at any price:** **U2** (`WITH_CASE_PRESERVING_NAME` is an engine build
flag — needs a from-source UE build) and **Y15 step 6** (a 4-byte `UENUM`; Blueprint-exposed enums
are always `uint8`).

-----

## 3. Granting — do this FIRST in every session, in batches of ≤7

> ### ▶ START HERE IN A NEW SESSION — step 0, the one-call experiment
>
> **Before anything else, call `request_access(["UE5DumpUI"])` on its own and record the result.**
> It costs one call and settles the open question in §1. Both the `.lnk` and the per-user Uninstall
> entry already exist on disk, so a fresh startup snapshot would contain whatever the resolver keys
> on.
>
> * **If it RESOLVES** → the list *is* snapshotted at MCP-server startup, mid-session creation never
>   works, and §1's table should be rewritten to say so. Then add `DumperTest Development` /
>   `DumperTest Shipping` and **Groups 3, 4 and 5 become runnable** — that is 20+ rows, including
>   W2/W3 step 5 and D2 step 4, which are the two currently stranded half-items.
> * **If it STILL FAILS** → loose exes are excluded for some other reason. Do **not** keep guessing
>   shortcut formats; five have been tried. The remaining untried combination is
>   `dist\startup_shortcut.py install`, whose `.lnk` lands in the **Startup** subfolder under the
>   name **`UE5CEDumper`** — a name never yet paired with a shortcut of that name. It also makes the
>   UI auto-start at sign-in, so ask before running it. Otherwise the UI groups need the maintainer
>   present and should be scheduled, not attempted.
>
> Either way, **write the answer into §1** — that section currently states an open question, and it
> should not stay open once one call has settled it.

⚠ **Grants do not survive a session.** Every session re-requests the whole list; nothing carries
over. Budget the three calls below into the start of every run.

⛔ **At most ~7 apps per `request_access` call.** Measured 2026-08-17: an 18-app request rendered a
dialog **taller than the display**, so the Allow button could not be reached — and it came back
**`user_denied` for all eighteen**, which reads exactly like a deliberate refusal and is not one.
Re-sent as **4 + 7 + 7** and every one was granted at full tier. **Never diagnose a blanket
`user_denied` as a decision without checking the batch size first.**

The batches below are the ones that worked verbatim.

**Batch 1 (4):** `Cheat Engine (64-bit)` · `Steam` · `Titan Quest II` · `冒險家艾略特的千年奇譚`
**Batch 2 (7):** `勇者鬥惡龍 VII Reimagined` · `勇者鬥惡龍I & II HD-2D Remake` · `滿意工廠` ·
`莊園領主 Manor Lords` · `機動戰士 GUNDAM SEED 激鬥命運 復刻版` · `DragonSword Awakening` ·
`EVERSPACE 2`
**Batch 3 (7):** `EVERSPACE™` · `The Artisan of Glimmith` · `OCTOPATH TRAVELER` · `Light Maze` ·
`Lushfoil Photography Sim` · `Star Trek Voyager - Across the Unknown` · `Solarpunk`

**Plus, only if step 0 succeeded:** `UE5DumpUI` · `DumperTest Development` · `DumperTest Shipping`.

> A mid-run grant request stalls an unattended batch until the maintainer returns, which is the whole
> reason these go first.

| Start-menu display name | = | | Start-menu display name | = |
|---|---|---|---|---|
| `Titan Quest II` | TQ2 | | `EVERSPACE 2` | ES2 |
| `冒險家艾略特的千年奇譚` | Elliot | | `EVERSPACE™` | EVERSPACE 1 |
| `勇者鬥惡龍 VII Reimagined` | DQ7R | | `The Artisan of Glimmith` | Geri |
| `勇者鬥惡龍I & II HD-2D Remake` | DQ I&II | | `OCTOPATH TRAVELER` | OCTOPATH |
| `滿意工廠` | Satisfactory | | `Light Maze` | Light Maze |
| `莊園領主 Manor Lords` | Manor Lords | | `Lushfoil Photography Sim` | Lushfoil |
| `機動戰士 GUNDAM SEED 激鬥命運 復刻版` | SEED | | `Star Trek Voyager - Across the Unknown` | STVoyager |
| `DragonSword Awakening` | DSA | | `Solarpunk` | Solarpunk |

⚠ Start-menu names differ from the on-disk folder names (`EVERSPACE 2` vs `EVERSPACE™ 2`).
Not needed: browsers (read-only tier), terminals/IDEs (all shell work goes through Bash),
`python.exe` (AA38 launches it headless and never shows it).

-----

## 3a. Deployed proxies — ✅ all ten refreshed to 3262. **Re-check before the run anyway.**

⛔ **Do not take this section's numbers on trust.** A Steam update, a "verify integrity", or simply a
newer `dist` makes them wrong again, and the failure is silent. **Re-run the inventory first** —
match on PE `VERSIONINFO` `ProductName`, the same signal the Proxy Deploy panel uses, **not** on
filename (`dxgi.dll` / `version.dll` are real system DLL names too).

**State as measured and then fixed on 2026-08-17:**

| | Before | After |
|---|---|---|
| ES2 · DQ7R · DQ I&II · EVERSPACE · Lushfoil · Manor Lords · OCTOPATH · SEED · Geri (`version.dll`) | 3122 | **3262** |
| Elliot (`dxgi.dll`) | 3161 | **3262** |
| foreign / third-party wrappers | 0 | 0 |

All ten now SHA-match `dist/proxy`, confirmed by a **second, independent** scanner rather than by the
updater's own success report (working-lessons §1.4). **DumperTest carries no proxy in either
config** — which is why Groups 1/2/4/5 inject directly and skip `trigger_scan`. Keep it that way.

**Why this is the first thing to check, every time.** A deployed proxy makes a fresh injection a
silent no-op: `injectDLL` returns `true`, the DLL loads, and `DllMain AutoStart` logs *"pipe already
exists … skip"* while the **old** proxy keeps serving the pipe (working-lessons §2.6). Had this gone
unchecked, eight of GROUP 7's ten titles and **Elliot — the host for the whole of GROUP 6** — would
have answered as build 3122/3161, i.e. from before AA38 (3245), A11 (3253) and A12 (3261) existed.
The batch would have reported those three fixes *verified* against code that does not contain them.
Nothing in the run surfaces that on its own; only the version check does.

**Do not remove them.** Three rows need the proxy path to exist: **B5 (active half)**,
**`.CT` DLL discovery (b2576)** and **B29**. Proxy mode also *auto-starts the pipe on launch*, which
makes GROUP 7's sweep cheaper than injecting — and the "pipe already exists" clash between two
proxied games cannot arise under §0's one-game-at-a-time rule.

**Do not update them with the panel's own "Update All" button.** That button is **AE4 step 2's
subject**. A prerequisite performed by the thing under test leaves you half-updated and unable to say
which half failed. Copy from `dist/proxy` directly, then let AE4 exercise the button separately.

> **Measured, not assumed:** all ten paths — **including ES2 under `C:\Program Files (x86)`** — open
> `r+b` without elevation, so no UAC prompt is involved. This is narrower than the general rule in
> §4: *deployment writes* to that tree are fine here; it is `inject-ue.ps1`'s auto-elevation that an
> unattended run still cannot answer.

**The `build_number` guard stays either way.** A current proxy is not a substitute for reading
`get_pointers.build_number` and comparing it against 3262, and proxy mode still needs `trigger_scan`
because `init` never scans.

### ⛔ A SHA match is necessary and NOT sufficient — at least one of the ten never loads

Measured 2026-08-17 (`[PROXYLOAD-2026-08-17]`, full write-up in todo.md). **OCTOPATH TRAVELER**'s
`version.dll` is byte-identical to `dist/proxy`, the panel reads `DeployedCurrent 1.0.0.3262`, and the
running process maps **only `C:\WINDOWS\SYSTEM32\VERSION.dll`**. No log folder is ever created, so the
no-op is completely silent: no error, no toast, nothing in the panel. A batch that trusted the status
column would have recorded OCTOPATH's rows against a DLL that never ran.

**The cheap diagnostic, and it is the only honest one:** after launching, confirm
`%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\` **exists**, or list the process's modules and check
that *our* path is mapped and not just System32's. On a working title (DQ I&II) **both** appear —
ours, plus the real one loaded by ours forwarding.

**Screening, offline, before deploying anything:** `py tools/pe/pe_imports_exports.py imports <exe>`.
Correlates 3 for 3 so far — a title that **statically imports** the proxy name gets System32's copy
(OCTOPATH), one that does not gets ours (DQ7R, DQ I&II). ⚠ `KnownDLLs` is **not** the mechanism; it
was checked and none of the four proxy names is in it. The mechanism is still unknown, so treat the
import-table correlation as a screen, not a law.

-----

## 4. Steps that write outside our own files — **AUTHORISED 2026-08-17**, with conditions

Both were raised for review and the maintainer approved both. The conditions below are what makes
them safe; they are not optional.

### 4.1 Plant a foreign `dxgi.dll` — target: **Light Maze** ✅ authorised

*Used by **AC1** step 1 and **B29** step 3.*

**Target: `D:\SteamLibrary\steamapps\common\Light Maze\LightMaze\Binaries\Win64\dxgi.dll`.**
Chosen on measurements, not convenience:

- **0.2 GB** — smallest installed UE title by 5×, so a Steam re-download is trivial if it ever comes
  to that.
- **It has no `dxgi.dll` and no `version.dll` today** — no proxy of ours, nothing of anyone's. This
  is the property that matters: **"restore" here means *delete the file we planted*, not overwrite
  something back.** There is no original to lose, so the destructive case the review flagged cannot
  arise. (Contrast Elliot, which carries our own `dxgi.dll` — never use it for this.)
- On `D:\SteamLibrary`, writable without elevation.

Source for the foreign DLL: copy `C:\Windows\System32\winhttp.dll` (read-only use of System32;
nothing there is modified). Its real `ProductName` is what makes the panel render
`Other proxy: <name>` — the whole point of the test.

Conditions:
1. Record the SHA-256 of the planted file, and assert the destination did **not** exist beforehand.
2. **Never launch Light Maze while the planted DLL is present** — the game would try to load
   `winhttp.dll` as `dxgi.dll`. AC1 explicitly needs no running game.
3. ⛔ **Ordering: delete the planted file and assert `dxgi.dll` is absent again, before GROUP 7
   launches Light Maze.** GROUP 3 (AC1) runs long before GROUP 7, so this is sequencing, not a
   conflict — but it must be an explicit asserted step, not an assumption.
4. Fallback if anything is left behind: Steam → Light Maze → Properties → Installed Files → Verify
   integrity (or reinstall, 0.2 GB).

> Still worth trying first, because it costs nothing: a **synthetic folder** under
> `D:\SteamLibrary\steamapps\common\` shaped like a game (`<name>\Binaries\Win64\`), the same trick
> the orphan-scan probe uses. If the Proxy Deploy Steam scan lists it, no real title is touched at
> all. Fall back to Light Maze only if it does not.

### 4.2 Delete one hint-cache key ✅ authorised

*Used by **AA38** step 5 (the cold-scan precondition).*

Delete key `67F515A70001A000` (python.exe) from
`%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.MSI-NB.json`. App-generated and regenerable, but the file
also holds every other game's hints, so:

1. Copy the whole file to the scratchpad first.
2. Edit with a Python `json` load → `del` → dump round-trip. **Never a text rewrite** — a regex over
   JSON is how a sibling key gets clipped.
3. Restore the backup when AA38 is done, or leave the key deleted and let the next scan re-learn it —
   either is fine, but say which.

### 4.3 Update the ten stale proxies

Covered in §3a. Overwrites our own older DLLs with our own newer ones; reversible by redeploying any
build. Measured writable without elevation at all ten paths.

**Elevation.** `inject-ue.ps1` auto-elevates via `Start-Process -Verb RunAs` — **a UAC dialog an
unattended run can never answer**, and computer-use cannot drive elevated windows at all. Prefer
`D:\SteamLibrary` for anything the batch creates. ⚠ Do **not** generalise this into "C: is
unwritable": §3a measured every deployed-proxy path, ES2's `C:\Program Files (x86)` one included, as
writable without elevation. Probe with an `r+b` open before assuming either way.

-----

## 5. Batched run order

### ✅ Already run 2026-08-17 — do not repeat these

Nine rows were closed or advanced in the first sitting, all headless (pipe + logs, no grant used).
Each is committed separately with its evidence; grep `todo.md` for the tag in brackets.

| Row | State | Tag |
|---|---|---|
| U1 / M1 / M3 | ✅ log half. U1's degraded branch **not tested** — it cannot fire on this sample | `[DUMPERTEST-LOG-2026-08-17]` |
| D1 / D3 | ✅ re-confirmed on the **2026-08-14** package (the old evidence described a superseded build) | `[DUMPERTEST-LOG-2026-08-17]` |
| AA38 | ✅ steps 1/2/3/5. **Step 4 (modular build) not tested** — Satisfactory is the candidate | `[AA38-PYTHON-2026-08-17]` |
| F9 | ✅ all six. Step 6 used `limit=10` on a 58-actor level, **not** a streaming map | `[F9-PIPE-2026-08-17]` |
| W2 / W3 | 🟡 headless half. **Step 5 (SDK header export) blocked on the UI** | `[W23-PIPE-2026-08-17]` |
| G12 general branch | ✅ both writers, four enum fields | `[G12-PIPE-2026-08-17]` |
| Z1 | ✅ re-confirmed at 60× the original sample (497 Path-2 analyses) | `[Z1-PIPE-2026-08-17]` |
| D2 | ✅ steps 1/2/3. **Step 4 (Leaves/slot clamp) blocked on the UI** | `[D2-PIPE-2026-08-17]` |
| G8 / G9 | 🟡 step 2 by cold re-detect. **Step 1 needs Elliot** — DumperTest short-circuits at PE VERSIONINFO | `[G89-PIPE-2026-08-17]` |

**Three defects in the checklist itself were found and corrected** while running these — they cost
time and would have cost it again:
`D2` said to grep `pipe-0.log`, but the marker is `[SCAN:grp]` and lands in **`scan-0.log`**;
`Z1`'s single "⇊ Funcs" step conflates **two** commands (`find_property_xrefs` is the bytecode path
and never triggers Zydis; Path 2 needs `walk_function_props` on a script-less UFunction);
and `G8/G9` step 1 is **structurally impossible on DumperTest**.

### ✅ Also run 2026-08-17 (late sitting) — the UI is unblocked, so GROUP 3 started

| Row | State | Tag |
|---|---|---|
| G8/G9 step 3 | ✅ **CLOSED** on DQ7R — the sample was missing, not the capability | `[DQ7R-PIPE-2026-08-17]` |
| G2 step 2 | 🟡 a refutation was claimed and then **WITHDRAWN** — scan rate varies 2.4×, so no extrapolation can decide it. Needs instrumenting Elliot | `[DQ7R-PIPE-2026-08-17]` |
| **Proxy load** | ⛔ **NEW: `DeployedCurrent` ≠ loaded.** OCTOPATH's proxy is byte-perfect and silently never runs | `[PROXYLOAD-2026-08-17]` |
| **SDK header** | ⛔ **NEW: the export does not compile** (`uint8_t[0x8] Name;` for every `TOptional`) | `[SDKHDR-UI-2026-08-17]` |
| G11 step 1 | 🟡 third title run but it does **not** discharge the step (DQ7R's PE hash changed) | `[DQ7R-PIPE-2026-08-17]` |
| U2 sweep | ✅ DQ7R is a third confirmed **non-CPN** title | `[DQ7R-PIPE-2026-08-17]` |
| AE4 steps 3/5/6 + 1 | ✅ **PASS** (step 1 by a stated substitution) | `[AE4-UI-2026-08-17]` |
| AE4 steps 2/4 | ⬜ **NOT TESTED**, with measured reasons | `[AE4-UI-2026-08-17]` |
| AC1 step 5 | ✅ **CLOSED** — needed no foreign DLL at all | `[AE4-UI-2026-08-17]` |
| W2/W3 step 5 | ✅ **CLOSED** — the last stranded half-item, all three checks | `[SDKHDR-UI-2026-08-17]` |
| D2 step 4 | ✅ **SETTLED** — and the step's premise was wrong | `[D2-UI-2026-08-17]` |
| **AC1 all 7 steps** | ✅ **CLOSED** on a synthetic folder — **Light Maze never touched** | `[AC1-UI-2026-08-17]` |
| AE4 step 4 | ✅ removal half (3 detectors incl. the Recycle Bin); gate arm not observable | `[AC1-UI-2026-08-17]` |
| A5 · V6 · AE9 · AF4 | ✅ **PASS** (AF4 has no unit test by design) | `[GRP4-UI-2026-08-17]` |
| U3 + U17 | ✅ **CLOSED** — the old bug's own output is the control | `[GRP4-UI-2026-08-17]` |
| D2 (樣本心跳) | ✅ **PASS** — DLL and game HUD agree to the digit over a measured 34 s | `[GRP4-UI-2026-08-17]` |
| Dump Explorer 跨遊戲身分閘 | ✅ **PASS** on the two DumperTest flavours | `[GRP4-UI-2026-08-17]` |

| Y9 · V7 · AF6 · AB6 · AE8 · D1/D3 step 6 | ✅ **PASS** | `[Y9-UI-2026-08-17]` · `[GRP4-UI-2026-08-17]` |
| AE2/AE3 | 🟡 **4 of 6** (steps 4 and 5 not run) | `[AE23-UI-2026-08-17]` |
| **PE hook** | ⛔ **NEW: detection FAILS on DumperTest**, verified good on Lushfoil | `[PEHOOK-2026-08-17]` |
| Skia ABI soak | ✅ see below | `[GRP4-UI-2026-08-17]` |

**GROUP 3 complete. GROUP 4: 14 rows done**, leaving `B10`, `AE2/AE3` steps 4–5, and `A5/V6/AE9`'s
`U8` sub-check (the sample has no `FName` with a `Number` component).

**Skia ABI soak — PASS, incidentally.** The AOT `dist` UI ran ~2.5 h across two instances under
heavy text/grid load: a 14,813-row Value Search with server-side re-sorts, a 58,618-object tree
reloaded across three different games, a 25,179-row tree, repeated Property Search result sets, six
modal dialogs, a 3.48 MB SDK header export and a 10.5 MB JSONL dump, and dozens of tab switches.
**No render fault, no crash, and `crash.log` gained nothing after the duplicate-launch entry.** Worth
recording because Skia/HarfBuzz are pinned to what Avalonia was built against, and the failure this
soak is looking for is precisely a font/text-shaping ABI break under sustained load.

**Three things this sitting learned that outlive the rows:**
1. **The version-detection ladder needs TWO properties, not one** — an unrecognised PE VERSIONINFO
   *and* a findable `++UE[45]+Release-` tag. Only **DQ7R / DQ I&II / OCTOPATH** have both; Elliot,
   which the register named, has neither half of the tag and can never produce a tier line.
2. **The exported SDK header does not compile** (`uint8_t[0x8] Name;` for every `TOptional`), which no
   existing check could see because headers are read and never compiled. Open.
3. **A duplicate UI launch writes an unhandled exception into `crash.log`** — the file documented as
   *the* AOT startup diagnostic.

**Next up, in order:** finish GROUP 3 (AE4 steps 2/4 + AC1 steps 1/2/3/4/6/7 — both need the
synthetic-folder staging in §4.1, and §4.1's AV caveat means doing it while someone can answer a
prompt), then GROUP 4 on DumperTest, then Elliot for `G8/G9 step 1` / `G11 step 3` / `AA14–AA20`.

-----

Ordered by items closed per environment launch. **Every group ends by killing what it started.**

### ▶ GROUP 0 — no environment · ~50 min · A
Grep and staging only; closes work *and* stages later groups. Run first.
`U1/M1/M3` steps 1-3 · `D1/D3` steps 1-5 · `G12(heuristic)` corpus grep · `DSA layout` step 3 ·
build the pre-fix DLL for `Genau RIP decode` in a **fresh** `git worktree` (⚠ not
`.claude/worktrees/suspicious-cannon-a8dec8`) with `-NoBumpBuildNumber`.
Staging steps that touch outside files are in §4 — **ask first**.

### ▶ GROUP 1 — DumperTest-Development, headless pipe · ~5 h · A · closes 12+
One launch, a Python client on `\\.\pipe\UE5DumpBfx`, no UI. Neither package carries a proxy DLL, so
`inject-ue.ps1 -ProcessId` scans directly — **no `trigger_scan`**.
`F9` · `W2/W3` · `D2` · `A3` · `Z1` · `G11` · `G8/G9` · `U3/U17` (1/2/3/5) · `AB3/AB5` (1-3) ·
`A6` (1/3/5) · `G2` Tier-1 · `G3` partial · `A12` (5-6) · `A11` (6) · `B19`.
**→ kill DumperTest.**

### ▶ GROUP 2 — headless, non-game hosts · ~2 h · A
`AA38` (python.exe sleeper; free second sample = the Solarpunk launcher shim; non-regression on
DumperTest-Shipping — diff **pattern id + method**, never addresses) · `B25` (two synthetic marker
exes) · `Genau RIP decode` (compare **module-relative RVAs**; ASLR rebases every launch).
**→ kill each host before starting the next.**

### ▶ GROUP 3 — UE5DumpUI only, no game · ~70 min · B
`AE4/AE5/AE6/AE7` · `AC1`. AC1's proof is the **unchanged SHA-256**, not a toast; its log line lands
in `Logs\UE5DumpUI\view-0.log` (the `ProxyDeploy` category falls through to the *view* logger).
Ends with a full app close + relaunch to check `ForceOverwrite` persisted and
`AllowForeignOverwrite` did not. **→ close the UI.**

### ▶ GROUP 4 — UE5DumpUI + DumperTest (Dev, then Shipping) · ~5 h · B · closes 10
`D1/D3` step 6 (the header ratio uses a **different denominator** from the log line — the log cannot
substitute) · `Y9` · `A5/V6/AE9` · `V7/AF4/AB6` (force V7's failure by **suspending the game
process** from Python, not by destroying an actor) · `D2(顯示配對)` · `B10` · `AF6/AE8` ·
`G12(一般分支)` · `Dump Explorer 跨遊戲身分閘` · `Skia ABI` soak · `D2(樣本心跳)` (⚠ do **not** pass
`-DumperTestIdle`) · `AE2/AE3` (1/2/3/5/6).
**→ kill DumperTest (both flavours), close the UI.**

### ▶ GROUP 5 — UE5DumpUI + Cheat Engine + DumperTest · ~4.5 h · B · closes 11
Run `AB1/AB2` **first** — B29 is downstream of the plugin being installed.
`AB1/AB2` · `B4` · `AA7` · `executeCodeEx 基本路徑` · `U4/U16/U6/F3` · `AA1` (bitfield byte must read
**0x05 → 0x07**) · `Y15` (skip step 6) · `Y1` · `Fern::Stop / B18` · `B26` (⚠ confirm the AOBMaker
bridge pipe answers first, or step 1 passes vacuously) · `B5` (.CT half) ·
`M1/M2/M3/A2/U1/V1/V2` (UI half).
**→ kill DumperTest, close CE, close the UI.**

### ▶ GROUP 6 — Cheat Engine + Elliot · ~3.5 h · B (+1 C)
Elliot's 482 MB image is what makes the race windows real; the sample is too small.
`G10/MA1` · `B5` (B5 half) · `executeCodeEx 5000 ms` (⚠ **not** downstream of the plugin —
`ue5_dissect.lua` loads into CE's Lua Engine directly) · `AA14–AA20` (⚠ step 5 needs the **game
thread only** suspended; CE's whole-process pause hits the status-0 branch, not the 0xFF branch
under test) · `G2` speed-split · `ST1` *(C — §6)*.
**→ kill Elliot, close CE.**

### ▶ GROUP 7 — Steam sweep, ten never-logged titles · ~90 min · B · advances 4 rows at once
DQ7R · DQ I&II · EVERSPACE™ · Light Maze · Lushfoil · Manor Lords · OCTOPATH · SEED · STVoyager ·
Geri.
⛔ **Strictly one at a time**: launch → dismiss launcher/EULA/splash → inject → one pipe round-trip →
grep → **kill and confirm dead** → next.
Pays for `G12(heuristic)` discovery · `U2` CPN screening (`get_offsets`, poll until
`probe_ran:true`; sweep **all** titles — CPN exists in UE4 too) · `G1/X3` · `U7`.
Expect null results and record them as "swept N, all false" — that is the honest form.

### ▶ GROUP 8 — TQ2 · ~2 h · B (+1 C)
`AE10` (a save already exists, so Continue reaches a level) · `X2` (backup host) · `U3/U17` step 4
(GAS control — a CDO walk, main menu suffices) · `AA12/AA13` *(C)*. **→ kill TQ2.**

### ▶ GROUP 9 — DQ7R · ~2 h · B (+1 C)
`X2` (primary host) · `G1/X3/U7/AF2` (DumperTest is AF2's `<30`-class control — but as a *separate*
launch, never concurrent) · `M1–M5 / Solide 256-cap` *(C)*. **→ kill DQ7R.**

### ▶ GROUP 10 — remaining single-title launches · ~2.5 h, one title at a time
`B8 (deferred half)` — DumperTest-Development with **`-DumperTestIdle`** (already compiled in; the
`-ExecCmds` cvar route is wrong). Grep **`walk-0.log`**. ⛔ closing the game never tests this. ·
`DSA layout` (retry loop; only a `…F8B0` anchor hit has evidential value) · `B29` (ES2; needs the
Group-5 plugin; step 2 needs **no** wrapper — System32's own `dxgi.dll` already exercises the
not-ours branch) · `b719 / b648 / b636 / b642 / b637+644` steps 2-3 on Geri + ES2.

### ▶ GROUP 11 — human-gated bouts. Schedule when the maintainer is back (§6).

-----

## 6. C_HYBRID — 12 rows, the exact human action and when

| Item | Human action | When |
|---|---|---|
| **A12** | Play a game with a growing container until it **reallocates**; remove an entry sitting **before** the matched element; repeat against a TMap-shaped container | Mid-batch ×3. Steps 5-6 already run in Group 1 |
| **A11** | Same bout: add until growth · remove an earlier index · remove the exact TSet/TMap entry · **append into slack without a realloc** (step 5 — do not skip, it is what catches an over-eager fix) | Mid-batch. Fold into A12's bout |
| **V1a** | Same bout (step 1) · then judge whether the NumericAll result volume is workable — the checklist states outright there is no mechanical PASS line | Mid-batch + one judgement |
| **AD4** | Play into combat and **take damage** so the game resets `bCanBeDamaged` while the batch spams ↻ · name a title whose pawn is immune for its own reasons (the "ON (not held)" control) | Naming before · damage mid-batch |
| **AE2/AE3** | On a streaming title, **travel to another level** so a walked class goes stale, then re-click the same row (step 4 only) | Mid-batch, once |
| **AA2/AA3** | Reach a world with **many live instances of one class**, then cause churn (kill and let respawn, or cross a streaming boundary) | World before · churn mid-batch. Try scripted long-distance `teleport_relative` first |
| **AA12/AA13** | Enter gameplay until an instance of the pre-chosen class **spawns**; then confirm the value is genuinely held, not just the record ticked | Mid-batch, once |
| **ST1** | With one invoke left in the queue, **play normally for a few minutes** — idling at a menu does not substitute, the drain must happen | Stall state before steps 3/4 · gameplay at the end |
| **M1–M5 / Solide cap** | Load DQ7R into gameplay with real occluders; then **four times, eyes on screen**, toggle See-through off while moving / paused / on a yanked connection / on game close, confirming no actor is left invisible (the DLL's hidden-count is **not** acceptance). Separately get **>256 live instances of one class** | Load before · toggles mid-batch · the >256 hunt before the cap step |
| **W1/W7** | **Install FModel** (or leave a runnable CUE4Parse `UsmapParser` on disk) and tell the batch its path — no independent parser exists on this machine, and writing one would reproduce the very shared misreading the item exists to rule out | Before the batch |
| **U2** | **Only if the Group-7 sweep returns all-false**: build UE from source with `WITH_CASE_PRESERVING_NAME=1` — hours, not a packaging cycle | After the sweep reports, never before |
| **b719 / b648 / b636 / b642 / b637+644** | Play Geri until **NPCs die and respawn** with a Route B freeze running, and change level once; then judge the freeze tick's FPS impact (no threshold exists in the doc) | Mid-batch. Steps 2-3 run in Group 10 |

-----

## 7. D_MANUAL — 1 row

**G3** (Extra Scan → Apply rescan gate). Measured, not assumed: Extra Scan is offered only when
`IsPointerMissing(GObjects) || GWorldMethod == "not_found"`, and every UE title in the local corpus
resolves all three. The only two pointer-missing processes are `python.exe` and the Solarpunk shim —
non-UE, so `apply_rescan` applies nothing, and **after the AA38 fix they report `GWorld=0` too,
removing them as candidates**. Needs an Avowed-shaped forked-engine title, not installed.
Group 1 still runs the 10-minute partial as a genuine non-regression, recorded as partial.

-----

## 8. Promoted — rows the docs call untestable that an installed environment satisfies

1. **G11** → DumperTest-Development. Its cached entry is at `versionDetectRev=3` against code rev 5,
   so it re-detects across both boundaries. Removes the GUI entirely (B → **A**).
2. **Dump Explorer 跨遊戲身分閘** → DumperTest-Development ↔ **-Shipping**: the gate compares
   main-module names and the two flavours differ. No second commercial title needed.
3. **AB3/AB5** → DumperTest-Development *is* a stock UE 5.4 project — LWC 24-byte FVector with a
   live pawn. Geri (UE4.27) supplies the 12-byte regression half.
4. **U3/U17** step 4 → **Titan Quest II** (backup Elliot); both carry `GameplayAttributeData` +
   `AbilitySystemComponent`, and a CDO walk needs no save.
5. **AE10** → **Titan Quest II**, installed *and* already carrying a save from 2026-08-14.
6. **B25** → two **synthetic marker exes**. Both branches read only the module's PE VERSIONINFO and
   two literals. **No game needed.**
7. **B29** step 2 → **no wrapper install**: every DX12 UE title already has System32's `dxgi.dll`
   mapped, which is exactly the not-ours branch.
8. **B8 (deferred half)** → **`-DumperTestIdle`**, already compiled into the packaged Development
   binary and named for B8 by id in the sample README.
9. **X2** → **DQ7R** and **TQ2** are installed; the doc's other candidates are not.
10. **executeCodeEx 5000 ms** → **Elliot** *and* **DSA**, the exact pair the doc names, both
    installed. It is **not** downstream of the CE-plugin item.
11. **G1/X3/U7/AF2** → DumperTest is AF2's mandatory `<30`-class control; the four Japanese titles
    serve U7.
12. **G12(heuristic)** → ten installed titles have never been logged at all — real sweep headroom.
13. **DSA layout** step 3 → the corpus grep is runnable **now**, before any launch.

> **Explicitly NOT promoted (refuted).** **U2 → TQ2.** TQ2 is installed but was *measured* non-CPN on
> 2026-08-14 (`votes standard=20, CPN=0`), and `test-games.md` already carries the note. Do not
> re-run TQ2 expecting a different answer.

-----

## 9. Traps to encode once in the harness

- **Before believing any pipe reply**, read `get_pointers.build_number` and compare against
  **`dist/build_number.txt` (3262)** — not the repo's 3261. Stale-proxy trap, working-lessons §2.6.
- **Proxy mode needs `trigger_scan`**; `init` returns cached values and never scans. Direct
  `inject-ue.ps1` into either DumperTest package *does* scan (both verified proxy-free).
- **Check `max_results` / `truncated` / `deadline_hit`** before turning an absence into a claim.
- **`build.ps1` bumps `build_number.txt` on every invocation.** Pass `-NoBumpBuildNumber`.
- **Bitdefender ATD**: commit before executing anything newly created; drive all create/delete
  automation from Python, never a `.ps1` (working-lessons §3.8).
- **Grep by FORMAT STRING, never line number** — every line cite in the register drifts.
- **CE's address edit boxes ignore `Ctrl+A`** — use `Home`, `shift+End`, then type.
- **Screenshots: no `scale` argument.** Scale is clamped but click coordinates are not, so a scaled
  screenshot puts every click ~25% off — which looks like the app ignoring input.
